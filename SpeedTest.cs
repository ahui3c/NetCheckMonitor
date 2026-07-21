using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NetCheck
{
    internal enum SpeedTestLevel { Quick, Standard, Full }

    internal sealed class SpeedTestOptions
    {
        public bool ScheduledEnabled { get; set; }
        public int IntervalHours { get; set; }
        public string Level { get; set; }
        public bool AllowMeteredNetwork { get; set; }
        public DateTime LastScheduledRunUtc { get; set; }
        public DateTime LastAttemptUtc { get; set; }
        public DateTime ServerCooldownUntilUtc { get; set; }
        public int RateLimitBackoffLevel { get; set; }

        internal SpeedTestLevel EffectiveLevel
        {
            get
            {
                SpeedTestLevel value;
                return Enum.TryParse(Level, true, out value) ? value : SpeedTestLevel.Standard;
            }
        }

        internal static SpeedTestOptions Defaults()
        {
            return new SpeedTestOptions { ScheduledEnabled = false, IntervalHours = 24, Level = SpeedTestLevel.Standard.ToString(), AllowMeteredNetwork = false };
        }
    }

    internal sealed class SpeedTestResult
    {
        public DateTime Time;
        public string Status;
        public SpeedTestLevel Level;
        public bool Scheduled;
        public double DownloadMbps;
        public double UploadMbps;
        public double IdleLatencyMs;
        public double JitterMs;
        public long DownloadBytes;
        public long UploadBytes;
        public string Error;
        public NetworkSnapshot Network;
        public bool RateLimited;
        public int RetryAfterSeconds;

        public string DisplaySummary
        {
            get
            {
                if (Status != "COMPLETED") return Status == "CANCELLED" ? L.T("測速已取消", "Speed test cancelled") : Status == "SKIPPED" ? L.T("已略過測速：", "Speed test skipped: ") + (Error ?? "") : L.T("測速失敗：", "Speed test failed: ") + (Error ?? "");
                return L.T("下載 ", "Download ") + DownloadMbps.ToString("0.0") + " Mbps" + L.T("｜上傳 ", " | Upload ") + UploadMbps.ToString("0.0") + " Mbps" + L.T("｜延遲 ", " | Latency ") + IdleLatencyMs.ToString("0.0") + " ms";
            }
        }
    }

    internal sealed class SpeedTestCancellation
    {
        private volatile bool cancelled;
        private readonly List<HttpWebRequest> activeRequests = new List<HttpWebRequest>();
        private readonly object sync = new object();
        internal bool IsCancelled { get { return cancelled; } }
        internal void SetActive(HttpWebRequest request) { lock (sync) { if (request != null) activeRequests.Add(request); if (cancelled && request != null) request.Abort(); } }
        internal void ClearActive(HttpWebRequest request) { lock (sync) activeRequests.Remove(request); }
        internal void Cancel() { lock (sync) { cancelled = true; foreach (HttpWebRequest request in activeRequests) try { request.Abort(); } catch { } activeRequests.Clear(); } }
    }

    internal static class CloudflareSpeedTest
    {
        private const string DownloadUrl = "https://speed.cloudflare.com/__down";
        private const string UploadUrl = "https://speed.cloudflare.com/__up";

        internal static SpeedTestResult Run(SpeedTestLevel level, bool scheduled, SpeedTestCancellation cancellation)
        {
            var result = new SpeedTestResult { Time = DateTime.Now, Status = "FAILED", Level = level, Scheduled = scheduled, Network = NetworkStatusReader.Capture() };
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 16);
                if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) throw new IOException("Windows detected no available network interface.");
                var latencies = new List<double>();
                for (int i = 0; i < 5; i++)
                {
                    ThrowIfCancelled(cancellation);
                    latencies.Add(MeasureDownload(0, cancellation).Milliseconds);
                }
                result.IdleLatencyMs = Median(latencies);
                result.JitterMs = AverageVariation(latencies);

                BatchSpec[] downloads, uploads;
                GetProfile(level, out downloads, out uploads);
                var downRates = new List<double>();
                foreach (BatchSpec batch in downloads)
                {
                    ThrowIfCancelled(cancellation);
                    TransferMeasurement m = MeasureBatch(false, batch.Bytes, batch.Count, cancellation);
                    ApplyRateLimit(result, m);
                    result.DownloadBytes += m.Bytes;
                    if (m.Bytes > 0) downRates.Add(m.Mbps);
                    if (m.Milliseconds >= 10000) break;
                }
                var upRates = new List<double>();
                foreach (BatchSpec batch in uploads)
                {
                    ThrowIfCancelled(cancellation);
                    TransferMeasurement m = MeasureBatch(true, batch.Bytes, batch.Count, cancellation);
                    ApplyRateLimit(result, m);
                    result.UploadBytes += m.Bytes;
                    if (m.Bytes > 0) upRates.Add(m.Mbps);
                    if (m.Milliseconds >= 10000) break;
                }
                if (downRates.Count == 0 || upRates.Count == 0) throw new IOException("No valid bandwidth samples were returned.");
                result.DownloadMbps = Percentile(downRates, 0.8);
                result.UploadMbps = Percentile(upRates, 0.8);
                if (result.RateLimited)
                {
                    result.Status = "FAILED";
                    result.Error = "HTTP 403/429: Cloudflare temporarily rejected one or more speed-test requests.";
                }
                else result.Status = "COMPLETED";
            }
            catch (Exception ex)
            {
                result.Status = cancellation != null && cancellation.IsCancelled ? "CANCELLED" : "FAILED";
                result.Error = ShortError(ex);
                int retryAfterSeconds;
                result.RateLimited = IsRateLimitResponse(ex, out retryAfterSeconds);
                result.RetryAfterSeconds = retryAfterSeconds;
            }
            result.Time = DateTime.Now;
            return result;
        }

        private static void GetProfile(SpeedTestLevel level, out BatchSpec[] downloads, out BatchSpec[] uploads)
        {
            if (level == SpeedTestLevel.Quick)
            {
                downloads = new BatchSpec[] { new BatchSpec(100000, 4), new BatchSpec(2500000, 4) };
                uploads = new BatchSpec[] { new BatchSpec(100000, 4), new BatchSpec(1250000, 4) };
            }
            else if (level == SpeedTestLevel.Full)
            {
                downloads = new BatchSpec[] { new BatchSpec(5000000, 8), new BatchSpec(25000000, 8) };
                // Keep each POST well below 25 MB. Larger individual POSTs are
                // more likely to be rejected by the public endpoint, while eight
                // simultaneous streams can saturate gigabit-class upstream links.
                uploads = new BatchSpec[] { new BatchSpec(2500000, 8), new BatchSpec(12500000, 8) };
            }
            else
            {
                downloads = new BatchSpec[] { new BatchSpec(1000000, 8), new BatchSpec(10000000, 8) };
                uploads = new BatchSpec[] { new BatchSpec(1000000, 8), new BatchSpec(5000000, 8) };
            }
        }

        private static TransferMeasurement MeasureBatch(bool upload, long bytes, int count, SpeedTestCancellation cancellation)
        {
            var start = new ManualResetEventSlim(false);
            var tasks = new System.Threading.Tasks.Task<TransferMeasurement>[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = System.Threading.Tasks.Task.Factory.StartNew(delegate
                {
                    start.Wait();
                    return upload ? MeasureUpload(bytes, cancellation) : MeasureDownload(bytes, cancellation);
                });
            }
            start.Set();
            AggregateException batchError = null;
            try { System.Threading.Tasks.Task.WaitAll(tasks); }
            catch (AggregateException ex)
            {
                batchError = ex.Flatten();
                if (cancellation != null && cancellation.IsCancelled) throw new OperationCanceledException();
            }
            finally { start.Dispose(); }
            long totalBytes = 0; double aggregateMbps = 0; double longest = 0; int succeeded = 0; bool rateLimited = false; int retryAfterSeconds = 0;
            if (batchError != null)
            {
                foreach (Exception error in batchError.InnerExceptions)
                {
                    int retry;
                    if (IsRateLimitResponse(error, out retry)) { rateLimited = true; retryAfterSeconds = Math.Max(retryAfterSeconds, retry); }
                }
            }
            foreach (System.Threading.Tasks.Task<TransferMeasurement> task in tasks)
            {
                if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) continue;
                TransferMeasurement value = task.Result;
                totalBytes += value.Bytes;
                aggregateMbps += value.Mbps;
                longest = Math.Max(longest, value.Milliseconds);
                succeeded++;
            }
            if (succeeded < Math.Max(1, count / 2))
            {
                Exception first = batchError != null && batchError.InnerExceptions.Count > 0 ? batchError.InnerExceptions[0] : null;
                throw first ?? new IOException("Too few speed-test streams completed successfully.");
            }
            return new TransferMeasurement(totalBytes, longest, aggregateMbps, rateLimited, retryAfterSeconds);
        }

        private static TransferMeasurement MeasureDownload(long requestedBytes, SpeedTestCancellation cancellation)
        {
            string url = DownloadUrl + "?bytes=" + requestedBytes.ToString(CultureInfo.InvariantCulture) + "&cache=" + Guid.NewGuid().ToString("N");
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 45000;
            request.ReadWriteTimeout = 45000;
            request.UserAgent = "NetCheckMonitor/0.9.8";
            request.KeepAlive = true;
            request.AutomaticDecompression = DecompressionMethods.None;
            cancellation.SetActive(request);
            var sw = Stopwatch.StartNew();
            long total = 0;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    byte[] buffer = new byte[65536];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { total += read; ThrowIfCancelled(cancellation); }
                }
            }
            finally { sw.Stop(); cancellation.ClearActive(request); }
            return new TransferMeasurement(total, sw.Elapsed.TotalMilliseconds);
        }

        private static TransferMeasurement MeasureUpload(long bytes, SpeedTestCancellation cancellation)
        {
            var request = (HttpWebRequest)WebRequest.Create(UploadUrl + "?bytes=" + bytes.ToString(CultureInfo.InvariantCulture) + "&cache=" + Guid.NewGuid().ToString("N"));
            request.Method = "POST";
            request.ContentType = "text/plain;charset=UTF-8";
            request.ContentLength = bytes;
            request.AllowWriteStreamBuffering = false;
            request.Timeout = 45000;
            request.ReadWriteTimeout = 45000;
            request.UserAgent = "NetCheckMonitor/0.9.8";
            request.KeepAlive = true;
            request.ServicePoint.Expect100Continue = false;
            cancellation.SetActive(request);
            Stopwatch sw = null;
            try
            {
                byte[] buffer = new byte[65536];
                for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)'0';
                using (Stream stream = request.GetRequestStream())
                {
                    // ResourceTiming.requestStart in Cloudflare's browser engine
                    // begins after connection setup. Starting here prevents TLS,
                    // proxy and connection-pool setup from depressing upload Mbps.
                    sw = Stopwatch.StartNew();
                    long remaining = bytes;
                    while (remaining > 0)
                    {
                        ThrowIfCancelled(cancellation);
                        int count = (int)Math.Min(buffer.Length, remaining);
                        stream.Write(buffer, 0, count);
                        remaining -= count;
                    }
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    sw.Stop();
                    if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300) throw new WebException("HTTP " + (int)response.StatusCode);
                }
            }
            finally { if (sw != null && sw.IsRunning) sw.Stop(); cancellation.ClearActive(request); }
            return new TransferMeasurement(bytes, sw == null ? 1 : sw.Elapsed.TotalMilliseconds);
        }

        private static void ThrowIfCancelled(SpeedTestCancellation cancellation)
        {
            if (cancellation != null && cancellation.IsCancelled) throw new OperationCanceledException();
        }

        private static double Median(List<double> values) { return Percentile(values, 0.5); }
        private static double Percentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0) return 0;
            var sorted = new List<double>(values); sorted.Sort();
            int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
        }
        private static double AverageVariation(List<double> values)
        {
            if (values == null || values.Count < 2) return 0;
            double sum = 0; for (int i = 1; i < values.Count; i++) sum += Math.Abs(values[i] - values[i - 1]);
            return sum / (values.Count - 1);
        }
        private static string ShortError(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            string value = ex.Message ?? ex.GetType().Name;
            return value.Length > 240 ? value.Substring(0, 240) : value;
        }
        private static bool IsRateLimitResponse(Exception ex, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                WebException web = current as WebException;
                HttpWebResponse response = web == null ? null : web.Response as HttpWebResponse;
                if (response == null) continue;
                int status = (int)response.StatusCode;
                if (status != 403 && status != 429) continue;
                string retryAfter = response.Headers["Retry-After"];
                int seconds;
                DateTime retryDate;
                if (Int32.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)) retryAfterSeconds = Math.Max(0, seconds);
                else if (DateTime.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out retryDate)) retryAfterSeconds = Math.Max(0, (int)Math.Ceiling((retryDate.ToUniversalTime() - DateTime.UtcNow).TotalSeconds));
                return true;
            }
            return false;
        }
        private static void ApplyRateLimit(SpeedTestResult result, TransferMeasurement measurement)
        {
            if (measurement == null || !measurement.RateLimited) return;
            result.RateLimited = true;
            result.RetryAfterSeconds = Math.Max(result.RetryAfterSeconds, measurement.RetryAfterSeconds);
        }
        private sealed class TransferMeasurement
        {
            internal readonly long Bytes; internal readonly double Milliseconds; private readonly double measuredMbps;
            internal readonly bool RateLimited; internal readonly int RetryAfterSeconds;
            internal TransferMeasurement(long bytes, double milliseconds) : this(bytes, milliseconds, -1) { }
            internal TransferMeasurement(long bytes, double milliseconds, double aggregateMbps) : this(bytes, milliseconds, aggregateMbps, false, 0) { }
            internal TransferMeasurement(long bytes, double milliseconds, double aggregateMbps, bool rateLimited, int retryAfterSeconds) { Bytes = bytes; Milliseconds = Math.Max(1, milliseconds); measuredMbps = aggregateMbps; RateLimited = rateLimited; RetryAfterSeconds = retryAfterSeconds; }
            internal double Mbps { get { return measuredMbps >= 0 ? measuredMbps : Bytes * 8.0 / (Milliseconds * 1000.0); } }
        }
        private sealed class BatchSpec
        {
            internal readonly long Bytes; internal readonly int Count;
            internal BatchSpec(long bytes, int count) { Bytes = bytes; Count = count; }
        }
    }

    internal enum NetworkCostState { Unknown, Unrestricted, Metered, Roaming, OverLimit }

    internal static class NetworkCostReader
    {
        [ComImport, Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
        private class NetworkListManager { }
        [ComImport, Guid("DCB00008-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface INetworkCostManager
        {
            void GetCost(out uint cost, IntPtr destinationAddress);
            void GetDataPlanStatus(IntPtr dataPlanStatus, IntPtr destinationAddress);
            void SetDestinationAddresses(uint length, IntPtr destinationAddresses, [MarshalAs(UnmanagedType.Bool)] bool append);
        }

        internal static NetworkCostState GetCurrent()
        {
            object instance = null;
            try
            {
                instance = new NetworkListManager();
                uint cost; ((INetworkCostManager)instance).GetCost(out cost, IntPtr.Zero);
                if ((cost & 0x00040000) != 0) return NetworkCostState.Roaming;
                if ((cost & 0x00010000) != 0) return NetworkCostState.OverLimit;
                if ((cost & 0x00000006) != 0 || (cost & 0x00080000) != 0) return NetworkCostState.Metered;
                if ((cost & 0x00000001) != 0) return NetworkCostState.Unrestricted;
            }
            catch { }
            finally { if (instance != null && Marshal.IsComObject(instance)) try { Marshal.FinalReleaseComObject(instance); } catch { } }
            return NetworkCostState.Unknown;
        }
    }

    internal static class SpeedTestStorage
    {
        private static readonly object Sync = new object();

        internal static void Append(string machineName, string machineId, SpeedTestResult result)
        {
            string dir = DataDirectory();
            string path = Path.Combine(dir, "NetCheck_Speed_" + Safe(machineName, 16) + "-" + machineId + ".csv");
            lock (Sync)
            {
                bool create = !File.Exists(path) || new FileInfo(path).Length == 0;
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    if (create) writer.WriteLine("Timestamp,Type,Status,LatencyMs,Target,Detail");
                    string detail = "Machine=" + B64(machineName) + ";MachineId=" + machineId + ";Provider=Cloudflare;Level=" + result.Level + ";Mode=" + (result.Scheduled ? "Scheduled" : "Manual")
                        + ";DownloadMbps=" + result.DownloadMbps.ToString("0.000", CultureInfo.InvariantCulture) + ";UploadMbps=" + result.UploadMbps.ToString("0.000", CultureInfo.InvariantCulture)
                        + ";JitterMs=" + result.JitterMs.ToString("0.000", CultureInfo.InvariantCulture) + ";DownloadBytes=" + result.DownloadBytes + ";UploadBytes=" + result.UploadBytes;
                    if (result.Network != null) detail += ";Adapter=" + B64(result.Network.AdapterDisplay) + ";ConnectionType=" + B64(result.Network.TypeDisplay) + ";WifiSignal=" + result.Network.WifiSignal;
                    if (!String.IsNullOrEmpty(result.Error)) detail += ";Error=" + B64(result.Error);
                    writer.WriteLine(Csv(result.Time.ToString("o")) + ",SPEEDTEST," + result.Status + "," + result.IdleLatencyMs.ToString("0.000", CultureInfo.InvariantCulture) + ",speed.cloudflare.com," + Csv(detail));
                    writer.Flush(); stream.Flush(true);
                }
            }
        }

        internal static string DataDirectory()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_SPEED_DATA_DIR");
            if (!String.IsNullOrWhiteSpace(overridePath)) { Directory.CreateDirectory(overridePath); return overridePath; }
            string dir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "NetCheck_Data");
            try { Directory.CreateDirectory(dir); return dir; }
            catch { dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NetCheck_Data"); Directory.CreateDirectory(dir); return dir; }
        }
        internal static string Safe(string value, int max) { var sb = new StringBuilder(); foreach (char c in value ?? "PC") { if (Char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c); if (sb.Length >= max) break; } return sb.Length == 0 ? "PC" : sb.ToString(); }
        internal static string B64(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? "")); }
        internal static string FromB64(string value) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); } catch { return ""; } }
        internal static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
