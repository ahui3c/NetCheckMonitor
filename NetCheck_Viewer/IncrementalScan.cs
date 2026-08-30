using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace NetCheckViewer
{
    internal sealed class IncrementalCacheDocument
    {
        public int SchemaVersion { get; set; }
        public string RootPath { get; set; }
        public string SavedAtUtc { get; set; }
        public List<IncrementalCacheEntry> Files { get; set; }
    }

    internal sealed class IncrementalCacheEntry
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public long LastParsedPosition { get; set; }
        public CacheSourceFile Source { get; set; }
        public List<CacheMonitorRecord> Monitoring { get; set; }
        public List<CacheSpeedRecord> Speeds { get; set; }
        public string Issue { get; set; }
    }

    internal sealed class CacheSourceFile
    {
        public string MachineName { get; set; }
        public string MachineId { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string Kind { get; set; }
        public long LastWriteTicks { get; set; }
        public long SizeBytes { get; set; }
        public int ParsedRows { get; set; }
        public int InvalidRows { get; set; }
        public long DataStartTicks { get; set; }
        public long DataEndTicks { get; set; }
    }

    internal sealed class CacheMonitorRecord
    {
        public string MachineName { get; set; }
        public string MachineId { get; set; }
        public long TimeTicks { get; set; }
        public string Status { get; set; }
        public long Latency { get; set; }
        public string Target { get; set; }
        public string Detail { get; set; }
        public string SourceFile { get; set; }
    }

    internal sealed class CacheSpeedRecord
    {
        public string MachineName { get; set; }
        public string MachineId { get; set; }
        public long TimeTicks { get; set; }
        public string Status { get; set; }
        public string Mode { get; set; }
        public string Level { get; set; }
        public double DownloadMbps { get; set; }
        public double UploadMbps { get; set; }
        public double LatencyMs { get; set; }
        public double JitterMs { get; set; }
        public string Network { get; set; }
        public string Error { get; set; }
        public string SourceFile { get; set; }
    }

    internal static class IncrementalScanEngine
    {
        internal static ScanResult Analyze(string rootPath, ViewerSettings settings, bool forceFull)
        {
            var watch = Stopwatch.StartNew();
            var result = new ScanResult { RootPath = rootPath, ScannedAt = DateTime.Now, FullReconciliation = forceFull };
            if (String.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                result.Issues.Add("備份資料夾不存在或尚未設定。");
                return result;
            }

            string cachePath = CachePath(rootPath);
            bool actualFull = forceFull || !File.Exists(cachePath);
            result.FullReconciliation = actualFull;
            string cacheIssue = null;
            IncrementalCacheDocument cache = actualFull ? NewCache(rootPath) : LoadCache(cachePath, rootPath, out cacheIssue);
            if (!String.IsNullOrWhiteSpace(cacheIssue)) result.FullReconciliation = true;
            if (!String.IsNullOrWhiteSpace(cacheIssue)) result.Issues.Add(cacheIssue);
            var previous = (cache.Files ?? new List<IncrementalCacheEntry>())
                .Where(delegate (IncrementalCacheEntry value) { return value != null && !String.IsNullOrWhiteSpace(value.Path); })
                .GroupBy(delegate (IncrementalCacheEntry value) { return value.Path; }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(delegate (IGrouping<string, IncrementalCacheEntry> group) { return group.Key; }, delegate (IGrouping<string, IncrementalCacheEntry> group) { return group.First(); }, StringComparer.OrdinalIgnoreCase);
            var next = NewCache(rootPath);
            var monitoring = new List<MonitorRecord>();
            var speeds = new List<SpeedRecord>();
            string[] paths;
            try { paths = Directory.GetFiles(rootPath, "*.csv", SearchOption.AllDirectories); }
            catch (Exception ex) { result.Issues.Add("無法掃描備份資料夾：" + ex.Message); return result; }
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith("NetCheck_", StringComparison.OrdinalIgnoreCase)) continue;
                FileInfo file;
                try { file = new FileInfo(path); }
                catch (Exception ex) { result.Issues.Add(name + "：" + ex.Message); continue; }
                IncrementalCacheEntry entry;
                if (!forceFull && previous.TryGetValue(path, out entry) && EntryMatches(entry, file))
                {
                    ApplyEntry(entry, result, monitoring, speeds);
                    result.ReusedFileCount++;
                    next.Files.Add(entry);
                    continue;
                }

                entry = ParseEntry(path, file);
                ApplyEntry(entry, result, monitoring, speeds);
                result.ParsedFileCount++;
                next.Files.Add(entry);
            }

            result.CsvFileCount = result.Files.Count;
            BackupAnalyzer.FinalizeResult(result, monitoring, speeds, settings);
            next.SavedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveCache(cachePath, next, result.Issues);
            watch.Stop();
            result.ScanMilliseconds = watch.ElapsedMilliseconds;
            return result;
        }

        private static IncrementalCacheEntry ParseEntry(string path, FileInfo file)
        {
            var monitoring = new List<MonitorRecord>();
            var speeds = new List<SpeedRecord>();
            var entry = new IncrementalCacheEntry
            {
                Path = path,
                Size = file.Length,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                LastParsedPosition = file.Length,
                Monitoring = new List<CacheMonitorRecord>(),
                Speeds = new List<CacheSpeedRecord>()
            };
            try
            {
                SourceFileInfo source = file.Name.StartsWith("NetCheck_Speed_", StringComparison.OrdinalIgnoreCase)
                    ? BackupAnalyzer.ParseSpeedFile(path, speeds)
                    : BackupAnalyzer.ParseMonitorFile(path, monitoring);
                entry.Source = ToCache(source);
                if (source != null && source.InvalidRows > 0) entry.Issue = file.Name + "：發現 " + source.InvalidRows + " 筆格式損壞或無法解析的資料列。";
                entry.Monitoring = monitoring.Select(ToCache).ToList();
                entry.Speeds = speeds.Select(ToCache).ToList();
            }
            catch (Exception ex) { entry.Issue = file.Name + "：" + ex.Message; }
            return entry;
        }

        private static void ApplyEntry(IncrementalCacheEntry entry, ScanResult result, List<MonitorRecord> monitoring, List<SpeedRecord> speeds)
        {
            if (entry == null) return;
            if (!String.IsNullOrWhiteSpace(entry.Issue)) result.Issues.Add(entry.Issue);
            SourceFileInfo source = FromCache(entry.Source);
            if (source != null) result.Files.Add(source);
            if (entry.Monitoring != null) monitoring.AddRange(entry.Monitoring.Select(FromCache));
            if (entry.Speeds != null) speeds.AddRange(entry.Speeds.Select(FromCache));
        }

        private static bool EntryMatches(IncrementalCacheEntry entry, FileInfo file)
        {
            return entry != null && entry.Size == file.Length && entry.LastWriteUtcTicks == file.LastWriteTimeUtc.Ticks
                && entry.LastParsedPosition == file.Length;
        }

        private static IncrementalCacheDocument LoadCache(string path, string root, out string issue)
        {
            issue = null;
            try
            {
                if (!File.Exists(path)) return NewCache(root);
                var serializer = Serializer();
                IncrementalCacheDocument value = serializer.Deserialize<IncrementalCacheDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (value == null || value.SchemaVersion != 1 || !SamePath(value.RootPath, root)) return NewCache(root);
                if (value.Files == null) value.Files = new List<IncrementalCacheEntry>();
                return value;
            }
            catch (Exception ex)
            {
                issue = "增量索引無法讀取，已改用完整掃描：" + ex.Message;
                return NewCache(root);
            }
        }

        private static void SaveCache(string path, IncrementalCacheDocument value, List<string> issues)
        {
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, Serializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null, true);
                else File.Move(temp, path);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                issues.Add("增量索引無法保存：" + ex.Message);
            }
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 100 };
        }

        private static IncrementalCacheDocument NewCache(string root)
        {
            return new IncrementalCacheDocument { SchemaVersion = 1, RootPath = root, Files = new List<IncrementalCacheEntry>() };
        }

        internal static string CachePath(string root)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck_Viewer", "Cache");
            return Path.Combine(directory, StableHash(Path.GetFullPath(root).ToUpperInvariant()) + ".json");
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? "") { hash ^= c; hash *= 16777619; }
                return hash.ToString("X8", CultureInfo.InvariantCulture);
            }
        }

        private static bool SamePath(string first, string second)
        {
            try { return String.Equals(Path.GetFullPath(first).TrimEnd('\\'), Path.GetFullPath(second).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static CacheSourceFile ToCache(SourceFileInfo value)
        {
            if (value == null) return null;
            return new CacheSourceFile { MachineName = value.MachineName, MachineId = value.MachineId, FileName = value.FileName, FullPath = value.FullPath, Kind = value.Kind, LastWriteTicks = value.LastWriteTime.Ticks, SizeBytes = value.SizeBytes, ParsedRows = value.ParsedRows, InvalidRows = value.InvalidRows, DataStartTicks = value.DataStartTime.Ticks, DataEndTicks = value.DataEndTime.Ticks };
        }

        private static SourceFileInfo FromCache(CacheSourceFile value)
        {
            if (value == null) return null;
            return new SourceFileInfo { MachineName = value.MachineName, MachineId = value.MachineId, FileName = value.FileName, FullPath = value.FullPath, Kind = value.Kind, LastWriteTime = TickDate(value.LastWriteTicks), SizeBytes = value.SizeBytes, ParsedRows = value.ParsedRows, InvalidRows = value.InvalidRows, DataStartTime = TickDate(value.DataStartTicks), DataEndTime = TickDate(value.DataEndTicks) };
        }

        private static CacheMonitorRecord ToCache(MonitorRecord value)
        {
            return new CacheMonitorRecord { MachineName = value.MachineName, MachineId = value.MachineId, TimeTicks = value.Time.Ticks, Status = value.Status, Latency = value.Latency, Target = value.Target, Detail = value.Detail, SourceFile = value.SourceFile };
        }

        private static MonitorRecord FromCache(CacheMonitorRecord value)
        {
            return new MonitorRecord { MachineName = value.MachineName, MachineId = value.MachineId, Time = TickDate(value.TimeTicks), Status = value.Status, Latency = value.Latency, Target = value.Target, Detail = value.Detail, SourceFile = value.SourceFile };
        }

        private static CacheSpeedRecord ToCache(SpeedRecord value)
        {
            return new CacheSpeedRecord { MachineName = value.MachineName, MachineId = value.MachineId, TimeTicks = value.Time.Ticks, Status = value.Status, Mode = value.Mode, Level = value.Level, DownloadMbps = value.DownloadMbps, UploadMbps = value.UploadMbps, LatencyMs = value.LatencyMs, JitterMs = value.JitterMs, Network = value.Network, Error = value.Error, SourceFile = value.SourceFile };
        }

        private static SpeedRecord FromCache(CacheSpeedRecord value)
        {
            return new SpeedRecord { MachineName = value.MachineName, MachineId = value.MachineId, Time = TickDate(value.TimeTicks), Status = value.Status, Mode = value.Mode, Level = value.Level, DownloadMbps = value.DownloadMbps, UploadMbps = value.UploadMbps, LatencyMs = value.LatencyMs, JitterMs = value.JitterMs, Network = value.Network, Error = value.Error, SourceFile = value.SourceFile };
        }

        private static DateTime TickDate(long ticks)
        {
            return ticks <= 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Local);
        }
    }
}
