using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace NetCheckViewer
{
    internal sealed class ViewerSettings
    {
        public string BackupRoot { get; set; }
        public int NormalReturnHours { get; set; }
        public int WarningReturnHours { get; set; }
        public double AvailabilityThreshold24Hours { get; set; }
        public int SpeedFailureThreshold { get; set; }
        public int ControlPendingHours { get; set; }
        public int FullReconcileMinutes { get; set; }
        public bool IntroDismissed { get; set; }

        internal static ViewerSettings Defaults()
        {
            return new ViewerSettings { BackupRoot = "", NormalReturnHours = 36, WarningReturnHours = 72, AvailabilityThreshold24Hours = 99, SpeedFailureThreshold = 2, ControlPendingHours = 1, FullReconcileMinutes = 60 };
        }
    }

    internal sealed class ScanResult
    {
        public string RootPath;
        public DateTime ScannedAt;
        public readonly List<MachineSummary> Machines = new List<MachineSummary>();
        public readonly List<DailySummary> Days = new List<DailySummary>();
        public readonly List<OutageEvent> Outages = new List<OutageEvent>();
        public readonly List<SpeedRecord> Speeds = new List<SpeedRecord>();
        public readonly List<SourceFileInfo> Files = new List<SourceFileInfo>();
        public readonly List<MonitorRecord> Monitoring = new List<MonitorRecord>();
        public readonly List<string> Issues = new List<string>();
        public int CsvFileCount;
        public int MonitoringRowCount;
        public int ParsedFileCount;
        public int ReusedFileCount;
        public bool FullReconciliation;
        public long ScanMilliseconds;
    }

    internal sealed class MachineSummary
    {
        public string MachineName;
        public string MachineId;
        public string FolderPath;
        public DateTime FirstDataTime;
        public DateTime LastDataTime;
        public DateTime LastBackupTime;
        public string LastConnectionStatus;
        public int Checks;
        public int OnlineChecks;
        public int OfflineChecks;
        public int SuspectedChecks;
        public double AvailabilityPercent;
        public double AverageLatencyMs;
        public double MaxLatencyMs;
        public int OutageCount;
        public TimeSpan LongestOutage;
        public DateTime LatestSpeedTime;
        public double LatestDownloadMbps;
        public double LatestUploadMbps;
        public double LatestSpeedLatencyMs;
        public string ReturnState;
        public int ReturnStateRank;
        public string Analysis;
    }

    internal sealed class DailySummary
    {
        public string MachineName;
        public string MachineId;
        public DateTime Day;
        public int Checks;
        public int Online;
        public int Offline;
        public int Suspected;
        public double AvailabilityPercent;
        public double AverageLatencyMs;
        public double MaxLatencyMs;
        public int OutageCount;
        public TimeSpan OutageDuration;
        public TimeSpan LongestOutage;
    }

    internal sealed class OutageEvent
    {
        public string MachineName;
        public string MachineId;
        public DateTime Start;
        public DateTime End;
        public int ConfirmedFailures;
        public bool Recovered;
        public TimeSpan Duration { get { return End > Start ? End - Start : TimeSpan.Zero; } }
    }

    internal sealed class SpeedRecord
    {
        public string MachineName;
        public string MachineId;
        public DateTime Time;
        public string Status;
        public string Mode;
        public string Level;
        public double DownloadMbps;
        public double UploadMbps;
        public double LatencyMs;
        public double JitterMs;
        public string Network;
        public string Error;
        public string SourceFile;
    }

    internal sealed class SourceFileInfo
    {
        public string MachineName;
        public string MachineId;
        public string FileName;
        public string FullPath;
        public string Kind;
        public DateTime LastWriteTime;
        public long SizeBytes;
        public int ParsedRows;
        public int InvalidRows;
        public DateTime DataStartTime;
        public DateTime DataEndTime;
    }

    internal sealed class MonitorRecord
    {
        public string MachineName;
        public string MachineId;
        public DateTime Time;
        public string Status;
        public long Latency;
        public string Target;
        public string Detail;
        public string SourceFile;
    }

    internal static class BackupAnalyzer
    {
        private static readonly Regex MonitorFile = new Regex(@"^NetCheck_(?!Speed_)(?<name>.+)-(?<id>[A-Za-z0-9]{4,16})_(?<date>\d{8})(?:_\d{6})?(?:_Raw)?\.csv$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SpeedFile = new Regex(@"^NetCheck_Speed_(?<name>.+)-(?<id>[A-Za-z0-9]{4,16})(?:_(?<date>\d{8}))?(?:_Raw)?\.csv$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Identity = new Regex(@"^(?<name>.+?)\s*\[(?<id>[^\]]+)\]", RegexOptions.Compiled);

        internal static ScanResult Analyze(string rootPath, ViewerSettings settings)
        {
            var result = new ScanResult { RootPath = rootPath, ScannedAt = DateTime.Now };
            if (String.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                result.Issues.Add("備份資料夾不存在或尚未設定。");
                return result;
            }

            var monitoring = new List<MonitorRecord>();
            var speeds = new List<SpeedRecord>();
            string[] paths;
            try { paths = Directory.GetFiles(rootPath, "*.csv", SearchOption.AllDirectories); }
            catch (Exception ex) { result.Issues.Add("無法掃描備份資料夾：" + ex.Message); return result; }

            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string fileName = Path.GetFileName(path);
                if (!fileName.StartsWith("NetCheck_", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    SourceFileInfo info;
                    if (fileName.StartsWith("NetCheck_Speed_", StringComparison.OrdinalIgnoreCase))
                        info = ParseSpeedFile(path, speeds);
                    else
                        info = ParseMonitorFile(path, monitoring);
                    if (info != null)
                    {
                        result.Files.Add(info); result.CsvFileCount++;
                        if (info.InvalidRows > 0) result.Issues.Add(info.FileName + "：發現 " + info.InvalidRows + " 筆格式損壞或無法解析的資料列。");
                    }
                }
                catch (Exception ex)
                {
                    result.Issues.Add(fileName + "：" + ex.Message);
                }
            }

            result.ParsedFileCount = result.CsvFileCount;
            result.FullReconciliation = true;
            FinalizeResult(result, monitoring, speeds, settings);
            return result;
        }

        internal static SourceFileInfo ParseMonitorFile(string path, List<MonitorRecord> output)
        {
            string machineName = "";
            string machineId = "";
            InferFromFile(Path.GetFileName(path), false, out machineName, out machineId);
            var rows = new List<string[]>();
            int invalidRows = 0;
            DateTime firstTime = DateTime.MaxValue;
            DateTime lastTime = DateTime.MinValue;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    List<string> fields = ParseCsv(line);
                    if (fields.Count > 0 && fields[0] == "Timestamp") continue;
                    if (fields.Count < 6) { invalidRows++; continue; }
                    DateTime time;
                    if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) { invalidRows++; continue; }
                    if (time < firstTime) firstTime = time;
                    if (time > lastTime) lastTime = time;
                    if (fields[1] == "MARKER" && (fields[2] == "COMPUTER" || fields[2] == "DAILY_SNAPSHOT"))
                    {
                        string parsedName, parsedId;
                        if (TryParseIdentity(fields[5], out parsedName, out parsedId)) { machineName = parsedName; machineId = parsedId; }
                    }
                    rows.Add(new string[] { fields[0], fields[1], fields[2], fields[3], fields[4], fields[5] });
                }
            }
            if (String.IsNullOrWhiteSpace(machineName)) machineName = Path.GetFileName(Path.GetDirectoryName(path)) ?? "未知電腦";
            if (String.IsNullOrWhiteSpace(machineId)) machineId = StableUnknownId(machineName + "|" + Path.GetDirectoryName(path));
            int parsed = 0;
            foreach (string[] fields in rows)
            {
                if (fields[1] != "CHECK") continue;
                DateTime time;
                if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) continue;
                long latency;
                Int64.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out latency);
                output.Add(new MonitorRecord { MachineName = machineName, MachineId = machineId, Time = time, Status = fields[2], Latency = latency, Target = fields[4], Detail = fields[5], SourceFile = path });
                parsed++;
            }
            var file = new FileInfo(path);
            return new SourceFileInfo { MachineName = machineName, MachineId = machineId, FileName = file.Name, FullPath = path, Kind = "監控資料", LastWriteTime = file.LastWriteTime, SizeBytes = file.Length, ParsedRows = parsed, InvalidRows = invalidRows, DataStartTime = firstTime == DateTime.MaxValue ? DateTime.MinValue : firstTime, DataEndTime = lastTime };
        }

        internal static SourceFileInfo ParseSpeedFile(string path, List<SpeedRecord> output)
        {
            string fallbackName, fallbackId;
            InferFromFile(Path.GetFileName(path), true, out fallbackName, out fallbackId);
            int parsed = 0;
            int invalidRows = 0;
            DateTime firstTime = DateTime.MaxValue;
            DateTime lastTime = DateTime.MinValue;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    List<string> fields = ParseCsv(line);
                    if (fields.Count > 0 && fields[0] == "Timestamp") continue;
                    if (fields.Count < 6) { invalidRows++; continue; }
                    if (fields[1] != "SPEEDTEST") continue;
                    DateTime time;
                    if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) { invalidRows++; continue; }
                    if (time < firstTime) firstTime = time;
                    if (time > lastTime) lastTime = time;
                    Dictionary<string, string> detail = ParseDetail(fields[5]);
                    string machineName = GetDecoded(detail, "Machine");
                    string machineId = Get(detail, "MachineId");
                    if (String.IsNullOrWhiteSpace(machineName)) machineName = fallbackName;
                    if (String.IsNullOrWhiteSpace(machineId)) machineId = fallbackId;
                    double latency;
                    Double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out latency);
                    string adapter = GetDecoded(detail, "Adapter");
                    string connection = GetDecoded(detail, "ConnectionType");
                    string signal = Get(detail, "WifiSignal");
                    string network = connection;
                    if (!String.IsNullOrWhiteSpace(adapter)) network += (network.Length == 0 ? "" : " / ") + adapter;
                    if (!String.IsNullOrWhiteSpace(signal) && signal != "-1") network += (network.Length == 0 ? "" : " / ") + "Wi-Fi " + signal + "%";
                    output.Add(new SpeedRecord
                    {
                        MachineName = String.IsNullOrWhiteSpace(machineName) ? "未知電腦" : machineName,
                        MachineId = String.IsNullOrWhiteSpace(machineId) ? StableUnknownId(path) : machineId,
                        Time = time,
                        Status = fields[2],
                        Mode = Get(detail, "Mode"),
                        Level = Get(detail, "Level"),
                        DownloadMbps = GetDouble(detail, "DownloadMbps"),
                        UploadMbps = GetDouble(detail, "UploadMbps"),
                        LatencyMs = latency,
                        JitterMs = GetDouble(detail, "JitterMs"),
                        Network = network,
                        Error = GetDecoded(detail, "Error"),
                        SourceFile = path
                    });
                    parsed++;
                }
            }
            if (String.IsNullOrWhiteSpace(fallbackName)) fallbackName = Path.GetFileName(Path.GetDirectoryName(path)) ?? "未知電腦";
            if (String.IsNullOrWhiteSpace(fallbackId)) fallbackId = StableUnknownId(fallbackName + "|" + Path.GetDirectoryName(path));
            var file = new FileInfo(path);
            return new SourceFileInfo { MachineName = fallbackName, MachineId = fallbackId, FileName = file.Name, FullPath = path, Kind = "定時測速", LastWriteTime = file.LastWriteTime, SizeBytes = file.Length, ParsedRows = parsed, InvalidRows = invalidRows, DataStartTime = firstTime == DateTime.MaxValue ? DateTime.MinValue : firstTime, DataEndTime = lastTime };
        }

        internal static void FinalizeResult(ScanResult result, List<MonitorRecord> monitoring, List<SpeedRecord> speeds, ViewerSettings settings)
        {
            DeduplicateMonitoring(monitoring);
            DeduplicateSpeeds(speeds);
            result.Monitoring.AddRange(monitoring.OrderBy(delegate (MonitorRecord r) { return r.Time; }));
            result.MonitoringRowCount = monitoring.Count;
            result.Speeds.AddRange(speeds.OrderByDescending(delegate (SpeedRecord s) { return s.Time; }));
            BuildSummaries(result, monitoring, speeds, settings ?? ViewerSettings.Defaults());
        }

        private static void BuildSummaries(ScanResult result, List<MonitorRecord> monitoring, List<SpeedRecord> speeds, ViewerSettings settings)
        {
            List<OutageEvent> outages = BuildOutages(monitoring);
            result.Outages.AddRange(outages.OrderByDescending(delegate (OutageEvent o) { return o.Start; }));

            var machineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MonitorRecord record in monitoring) machineIds.Add(record.MachineId);
            foreach (SpeedRecord speed in speeds) machineIds.Add(speed.MachineId);
            foreach (SourceFileInfo file in result.Files) machineIds.Add(file.MachineId);

            foreach (string id in machineIds)
            {
                List<MonitorRecord> records = monitoring.Where(delegate (MonitorRecord r) { return String.Equals(r.MachineId, id, StringComparison.OrdinalIgnoreCase); }).OrderBy(delegate (MonitorRecord r) { return r.Time; }).ToList();
                List<SpeedRecord> machineSpeeds = speeds.Where(delegate (SpeedRecord s) { return String.Equals(s.MachineId, id, StringComparison.OrdinalIgnoreCase); }).OrderBy(delegate (SpeedRecord s) { return s.Time; }).ToList();
                List<SourceFileInfo> files = result.Files.Where(delegate (SourceFileInfo f) { return String.Equals(f.MachineId, id, StringComparison.OrdinalIgnoreCase); }).ToList();
                string name = records.Count > 0 ? records[records.Count - 1].MachineName : (machineSpeeds.Count > 0 ? machineSpeeds[machineSpeeds.Count - 1].MachineName : files.Select(delegate (SourceFileInfo f) { return f.MachineName; }).FirstOrDefault());
                if (String.IsNullOrWhiteSpace(name)) name = "未知電腦";
                var machine = new MachineSummary { MachineName = name, MachineId = id, FolderPath = CommonFolder(files) };
                if (records.Count > 0)
                {
                    machine.FirstDataTime = records[0].Time;
                    machine.LastDataTime = records[records.Count - 1].Time;
                    machine.LastConnectionStatus = records[records.Count - 1].Status;
                    machine.Checks = records.Count(delegate (MonitorRecord r) { return r.Status == "ONLINE" || r.Status == "OFFLINE"; });
                    machine.OnlineChecks = records.Count(delegate (MonitorRecord r) { return r.Status == "ONLINE"; });
                    machine.OfflineChecks = records.Count(delegate (MonitorRecord r) { return r.Status == "OFFLINE"; });
                    machine.SuspectedChecks = records.Count(delegate (MonitorRecord r) { return r.Status == "SUSPECTED"; });
                    machine.AvailabilityPercent = machine.Checks == 0 ? 0 : 100.0 * machine.OnlineChecks / machine.Checks;
                    List<MonitorRecord> latency = records.Where(delegate (MonitorRecord r) { return r.Status == "ONLINE"; }).ToList();
                    machine.AverageLatencyMs = latency.Count == 0 ? 0 : latency.Average(delegate (MonitorRecord r) { return (double)r.Latency; });
                }
                if (files.Count > 0) machine.LastBackupTime = files.Max(delegate (SourceFileInfo f) { return f.LastWriteTime; });
                List<OutageEvent> machineOutages = outages.Where(delegate (OutageEvent o) { return String.Equals(o.MachineId, id, StringComparison.OrdinalIgnoreCase); }).ToList();
                machine.OutageCount = machineOutages.Count;
                machine.LongestOutage = machineOutages.Count == 0 ? TimeSpan.Zero : machineOutages.Max(delegate (OutageEvent o) { return o.Duration; });
                SpeedRecord latestSpeed = machineSpeeds.LastOrDefault(delegate (SpeedRecord s) { return s.Status == "COMPLETED"; });
                if (latestSpeed != null)
                {
                    machine.LatestSpeedTime = latestSpeed.Time;
                    machine.LatestDownloadMbps = latestSpeed.DownloadMbps;
                    machine.LatestUploadMbps = latestSpeed.UploadMbps;
                    machine.LatestSpeedLatencyMs = latestSpeed.LatencyMs;
                }
                ApplyHealth(machine, settings);
                result.Machines.Add(machine);
            }

            result.Machines.Sort(delegate (MachineSummary a, MachineSummary b)
            {
                int rank = b.ReturnStateRank.CompareTo(a.ReturnStateRank);
                return rank != 0 ? rank : String.Compare(a.MachineName, b.MachineName, StringComparison.OrdinalIgnoreCase);
            });

            foreach (IGrouping<string, MonitorRecord> machineGroup in monitoring.GroupBy(delegate (MonitorRecord r) { return r.MachineId; }, StringComparer.OrdinalIgnoreCase))
            {
                foreach (IGrouping<DateTime, MonitorRecord> dayGroup in machineGroup.GroupBy(delegate (MonitorRecord r) { return r.Time.Date; }))
                {
                    List<MonitorRecord> rows = dayGroup.OrderBy(delegate (MonitorRecord r) { return r.Time; }).ToList();
                    int online = rows.Count(delegate (MonitorRecord r) { return r.Status == "ONLINE"; });
                    int offline = rows.Count(delegate (MonitorRecord r) { return r.Status == "OFFLINE"; });
                    int suspected = rows.Count(delegate (MonitorRecord r) { return r.Status == "SUSPECTED"; });
                    int checks = online + offline;
                    List<MonitorRecord> latency = rows.Where(delegate (MonitorRecord r) { return r.Status == "ONLINE"; }).ToList();
                    List<OutageEvent> dayOutages = outages.Where(delegate (OutageEvent o) { return String.Equals(o.MachineId, machineGroup.Key, StringComparison.OrdinalIgnoreCase) && o.Start.Date <= dayGroup.Key && o.End >= dayGroup.Key; }).ToList();
                    TimeSpan duration = TimeSpan.Zero;
                    TimeSpan longest = TimeSpan.Zero;
                    DateTime dayEnd = dayGroup.Key.AddDays(1);
                    foreach (OutageEvent outage in dayOutages)
                    {
                        DateTime start = outage.Start < dayGroup.Key ? dayGroup.Key : outage.Start;
                        DateTime end = outage.End > dayEnd ? dayEnd : outage.End;
                        TimeSpan part = end > start ? end - start : TimeSpan.Zero;
                        duration += part;
                        if (part > longest) longest = part;
                    }
                    MonitorRecord last = rows[rows.Count - 1];
                    result.Days.Add(new DailySummary { MachineName = last.MachineName, MachineId = machineGroup.Key, Day = dayGroup.Key, Checks = checks, Online = online, Offline = offline, Suspected = suspected, AvailabilityPercent = checks == 0 ? 0 : 100.0 * online / checks, AverageLatencyMs = latency.Count == 0 ? 0 : latency.Average(delegate (MonitorRecord r) { return (double)r.Latency; }), MaxLatencyMs = latency.Count == 0 ? 0 : latency.Max(delegate (MonitorRecord r) { return (double)r.Latency; }), OutageCount = dayOutages.Count, OutageDuration = duration, LongestOutage = longest });
                }
            }
            result.Days.Sort(delegate (DailySummary a, DailySummary b) { int day = b.Day.CompareTo(a.Day); return day != 0 ? day : String.Compare(a.MachineName, b.MachineName, StringComparison.OrdinalIgnoreCase); });
        }

        private static List<OutageEvent> BuildOutages(List<MonitorRecord> monitoring)
        {
            var result = new List<OutageEvent>();
            foreach (IGrouping<string, MonitorRecord> group in monitoring.GroupBy(delegate (MonitorRecord r) { return r.MachineId; }, StringComparer.OrdinalIgnoreCase))
            {
                List<MonitorRecord> rows = group.OrderBy(delegate (MonitorRecord r) { return r.Time; }).ToList();
                DateTime suspected = DateTime.MinValue;
                OutageEvent current = null;
                foreach (MonitorRecord row in rows)
                {
                    if (row.Status == "SUSPECTED") { if (suspected == DateTime.MinValue) suspected = row.Time; }
                    else if (row.Status == "OFFLINE")
                    {
                        if (current == null) current = new OutageEvent { MachineName = row.MachineName, MachineId = row.MachineId, Start = suspected == DateTime.MinValue ? row.Time : suspected, End = row.Time, ConfirmedFailures = 0, Recovered = false };
                        current.End = row.Time;
                        current.ConfirmedFailures++;
                    }
                    else if (row.Status == "ONLINE")
                    {
                        if (current != null) { current.End = row.Time; current.Recovered = true; result.Add(current); current = null; }
                        suspected = DateTime.MinValue;
                    }
                }
                if (current != null)
                {
                    current.End = rows.Count == 0 ? current.Start : rows[rows.Count - 1].Time;
                    result.Add(current);
                }
            }
            return result;
        }

        private static void ApplyHealth(MachineSummary machine, ViewerSettings settings)
        {
            double hours = machine.LastBackupTime == DateTime.MinValue ? Double.MaxValue : Math.Max(0, (DateTime.Now - machine.LastBackupTime).TotalHours);
            if (hours <= Math.Max(1, settings.NormalReturnHours)) { machine.ReturnState = "回傳正常"; machine.ReturnStateRank = 0; }
            else if (hours <= Math.Max(settings.NormalReturnHours + 1, settings.WarningReturnHours)) { machine.ReturnState = "回傳延遲"; machine.ReturnStateRank = 1; }
            else { machine.ReturnState = "長時間未回傳"; machine.ReturnStateRank = 2; }

            if (machine.Checks == 0) machine.Analysis = "尚無可分析的監控檢查資料";
            else if (machine.LastConnectionStatus == "OFFLINE") machine.Analysis = "最後一筆為確認斷線，請優先檢查";
            else if (machine.AvailabilityPercent < 95) machine.Analysis = "連線明顯不穩，確認斷線比例偏高";
            else if (machine.AvailabilityPercent < 99) machine.Analysis = "近期有多次斷線，建議檢查時段與設備";
            else if (machine.AverageLatencyMs >= 250) machine.Analysis = "連線可用但平均延遲偏高";
            else if (machine.ReturnStateRank > 0) machine.Analysis = "監控資料未準時回傳，請確認程式或備份";
            else machine.Analysis = "目前資料顯示連線穩定";
        }

        private static void DeduplicateMonitoring(List<MonitorRecord> records)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            records.RemoveAll(delegate (MonitorRecord r)
            {
                string key = r.MachineId + "|" + r.Time.ToString("o") + "|" + r.Status + "|" + r.Target + "|" + r.Detail;
                return !seen.Add(key);
            });
        }

        private static void DeduplicateSpeeds(List<SpeedRecord> records)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            records.RemoveAll(delegate (SpeedRecord r)
            {
                string key = r.MachineId + "|" + r.Time.ToString("o") + "|" + r.Status + "|" + r.DownloadMbps.ToString("0.000", CultureInfo.InvariantCulture) + "|" + r.UploadMbps.ToString("0.000", CultureInfo.InvariantCulture);
                return !seen.Add(key);
            });
        }

        private static void InferFromFile(string fileName, bool speed, out string machineName, out string machineId)
        {
            Match match = (speed ? SpeedFile : MonitorFile).Match(fileName ?? "");
            machineName = match.Success ? match.Groups["name"].Value : "";
            machineId = match.Success ? match.Groups["id"].Value : "";
        }

        private static bool TryParseIdentity(string detail, out string machineName, out string machineId)
        {
            Match match = Identity.Match(detail ?? "");
            machineName = match.Success ? match.Groups["name"].Value.Trim() : "";
            machineId = match.Success ? match.Groups["id"].Value.Trim() : "";
            return match.Success && machineName.Length > 0 && machineId.Length > 0;
        }

        private static List<string> ParseCsv(string line)
        {
            var result = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { result.Add(field.ToString()); field.Length = 0; }
                else field.Append(c);
            }
            result.Add(field.ToString());
            return result;
        }

        private static Dictionary<string, string> ParseDetail(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (value ?? "").Split(';'))
            {
                int index = part.IndexOf('=');
                if (index > 0) result[part.Substring(0, index)] = part.Substring(index + 1);
            }
            return result;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
        }

        private static string GetDecoded(Dictionary<string, string> values, string key)
        {
            string value = Get(values, key);
            if (String.IsNullOrWhiteSpace(value)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return value; }
        }

        private static double GetDouble(Dictionary<string, string> values, string key)
        {
            double value;
            return Double.TryParse(Get(values, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static string StableUnknownId(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? "UNKNOWN") { hash ^= c; hash *= 16777619; }
                return hash.ToString("X8", CultureInfo.InvariantCulture);
            }
        }

        private static string CommonFolder(List<SourceFileInfo> files)
        {
            if (files == null || files.Count == 0) return "";
            return Path.GetDirectoryName(files.OrderByDescending(delegate (SourceFileInfo f) { return f.LastWriteTime; }).First().FullPath);
        }
    }

    internal static class ViewerSelfTest
    {
        internal static bool Run(string resultPath, out string message)
        {
            string root = Path.Combine(Path.GetTempPath(), "NetCheckViewerSelfTest_" + Guid.NewGuid().ToString("N"));
            string cachePath = null;
            string previousAlertStatePath = Environment.GetEnvironmentVariable("NETCHECK_VIEWER_ALERT_STATE_PATH");
            try
            {
                CreateDemoData(root);
                ScanResult result = BackupAnalyzer.Analyze(root, ViewerSettings.Defaults());
                MachineSummary office = result.Machines.FirstOrDefault(delegate (MachineSummary m) { return m.MachineId == "A1B2C3D4"; });
                MachineSummary store = result.Machines.FirstOrDefault(delegate (MachineSummary m) { return m.MachineId == "E5F6A7B8"; });
                bool controlAvailable = office != null && !String.IsNullOrWhiteSpace(ViewerControlClient.FindControlFile(office, root));
                bool controlProtocol = ViewerControlClient.RunSelfTest(root);
                string settingsPath = Path.Combine(root, "viewer-settings-test.json");
                SettingsStore.SaveTo(settingsPath, new ViewerSettings { BackupRoot = root, NormalReturnHours = 24, WarningReturnHours = 48, IntroDismissed = true });
                ViewerSettings remembered = SettingsStore.LoadFrom(settingsPath);
                string emptyRoot = Path.Combine(root, "EMPTY");
                Directory.CreateDirectory(emptyRoot);
                ScanResult emptyResult = BackupAnalyzer.Analyze(emptyRoot, ViewerSettings.Defaults());
                bool remembersFolder = String.Equals(remembered.BackupRoot, root, StringComparison.Ordinal) && remembered.NormalReturnHours == 24 && remembered.WarningReturnHours == 48 && remembered.IntroDismissed;
                bool onboardingCopy = ViewerIntroContent.Positioning.IndexOf("不是即時主從", StringComparison.Ordinal) >= 0
                    && ViewerIntroContent.Usage.IndexOf("Google Drive", StringComparison.Ordinal) >= 0
                    && ViewerIntroContent.Capabilities.IndexOf("非同步", StringComparison.Ordinal) >= 0;
                bool onboardingSetting;
                using (var intro = new ViewerIntroForm(true)) onboardingSetting = intro.DoNotShowAgain;
                bool dataDetection = ViewerDataState.HasUsableData(result) && !ViewerDataState.HasUsableData(emptyResult);

                cachePath = IncrementalScanEngine.CachePath(root);
                ScanResult firstIncremental = IncrementalScanEngine.Analyze(root, ViewerSettings.Defaults(), true);
                ScanResult cachedIncremental = IncrementalScanEngine.Analyze(root, ViewerSettings.Defaults(), false);
                string changedFile = firstIncremental.Files.First(delegate (SourceFileInfo value) { return value.MachineId == "A1B2C3D4" && value.Kind == "監控資料"; }).FullPath;
                File.AppendAllText(changedFile, Environment.NewLine + Row(DateTime.Now, "CHECK", "ONLINE", "31", "https://example.com", "incremental") + Environment.NewLine + "broken,row", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(changedFile, DateTime.UtcNow.AddSeconds(2));
                ScanResult changedIncremental = IncrementalScanEngine.Analyze(root, ViewerSettings.Defaults(), false);
                bool incremental = firstIncremental.ParsedFileCount == firstIncremental.CsvFileCount && cachedIncremental.ParsedFileCount == 0
                    && cachedIncremental.ReusedFileCount == cachedIncremental.CsvFileCount && changedIncremental.ParsedFileCount == 1
                    && changedIncremental.ReusedFileCount == changedIncremental.CsvFileCount - 1;
                bool corruptionDetected = changedIncremental.Issues.Any(delegate (string value) { return value.IndexOf("格式損壞", StringComparison.Ordinal) >= 0; });

                string alertStatePath = Path.Combine(root, "alert-state-test.json");
                Environment.SetEnvironmentVariable("NETCHECK_VIEWER_ALERT_STATE_PATH", alertStatePath);
                List<ViewerAlert> alerts = AlertCenter.Build(firstIncremental, ViewerSettings.Defaults(), root);
                bool alertCenter = alerts.Count > 0;
                if (alerts.Count > 0)
                {
                    AlertCenter.SetAcknowledged(alerts[0].Key, true);
                    alertCenter = AlertCenter.Build(firstIncremental, ViewerSettings.Defaults(), root).Any(delegate (ViewerAlert value) { return value.Key == alerts[0].Key && value.Acknowledged; });
                }
                bool trendData = result.Days.Any(delegate (DailySummary value) { return value.MaxLatencyMs >= value.AverageLatencyMs; })
                    && result.Files.Any(delegate (SourceFileInfo value) { return value.DataEndTime != DateTime.MinValue; });

                bool ok = result.Machines.Count == 3 && office != null && store != null && office.OutageCount == 1 && office.LatestDownloadMbps > 300 && store.ReturnState == "長時間未回傳" && result.Days.Count >= 3 && controlAvailable && controlProtocol && remembersFolder && onboardingCopy && onboardingSetting && dataDetection && incremental && corruptionDetected && alertCenter && trendData;
                message = ok ? "NETCHECK_VIEWER_SELFTEST_OK" : "Self-test result mismatch.";
                if (!String.IsNullOrWhiteSpace(resultPath)) File.WriteAllText(resultPath, message, Encoding.UTF8);
                return ok;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                if (!String.IsNullOrWhiteSpace(resultPath)) File.WriteAllText(resultPath, message, Encoding.UTF8);
                return false;
            }
            finally
            {
                Environment.SetEnvironmentVariable("NETCHECK_VIEWER_ALERT_STATE_PATH", previousAlertStatePath);
                try { if (!String.IsNullOrWhiteSpace(cachePath) && File.Exists(cachePath)) File.Delete(cachePath); } catch { }
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        internal static void CreateDemoData(string root)
        {
            string pcA = Path.Combine(root, "OFFICE-PC");
            string pcB = Path.Combine(root, "STORE-PC");
            string pcC = Path.Combine(root, "NOTEBOOK");
            Directory.CreateDirectory(pcA);
            Directory.CreateDirectory(pcB);
            Directory.CreateDirectory(pcC);
            DateTime now = DateTime.Now;
            string a = Path.Combine(pcA, "NetCheck_OFFICE-PC-A1B2C3D4_" + now.ToString("yyyyMMdd") + "_Raw.csv");
            string b = Path.Combine(pcB, "NetCheck_STORE-PC-E5F6A7B8_" + now.AddDays(-4).ToString("yyyyMMdd") + "_Raw.csv");
            string c = Path.Combine(pcC, "NetCheck_NOTEBOOK-C9D0E1F2_" + now.AddDays(-2).ToString("yyyyMMdd") + "_Raw.csv");
            WriteMonitor(a, "OFFICE-PC", "A1B2C3D4", now.AddHours(-4), true);
            WriteMonitor(b, "STORE-PC", "E5F6A7B8", now.AddDays(-4), false);
            WriteHealthy(c, "NOTEBOOK", "C9D0E1F2", now.AddDays(-2));
            File.SetLastWriteTime(a, now);
            File.SetLastWriteTime(b, now.AddHours(-80));
            File.SetLastWriteTime(c, now.AddHours(-48));
            string speed = Path.Combine(pcA, "NetCheck_Speed_OFFICE-PC-A1B2C3D4_" + now.ToString("yyyyMMdd") + "_Raw.csv");
            string detail = "Machine=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("OFFICE-PC")) + ";MachineId=A1B2C3D4;Provider=Cloudflare;Level=Standard;Mode=Scheduled;DownloadMbps=321.500;UploadMbps=101.250;JitterMs=2.100";
            File.WriteAllText(speed, "Timestamp,Type,Status,LatencyMs,Target,Detail\r\n\"" + now.AddHours(-1).ToString("o") + "\",SPEEDTEST,COMPLETED,8.500,speed.cloudflare.com,\"" + detail + "\"\r\n", new UTF8Encoding(true));
            var control = new ViewerControlDocument { SchemaVersion = 1, MachineName = "OFFICE-PC", MachineId = "A1B2C3D4", Desired = new ViewerControlDesired { Revision = "demo", RequestedAtUtc = DateTime.UtcNow.ToString("o"), MonitorIntervalSeconds = 60, BackupTime = "23:55" }, Applied = new ViewerControlApplied { Revision = "demo", AppliedAtUtc = DateTime.UtcNow.ToString("o"), Status = "APPLIED", Message = "示範設定已套用。", MonitorIntervalSeconds = 60, BackupTime = "23:55" } };
            File.WriteAllText(Path.Combine(pcA, ViewerControlClient.FileName), new JavaScriptSerializer().Serialize(control), new UTF8Encoding(false));
        }

        private static void WriteMonitor(string path, string machine, string id, DateTime start, bool recover)
        {
            var lines = new List<string>();
            lines.Add("Timestamp,Type,Status,LatencyMs,Target,Detail");
            lines.Add(Row(start, "MARKER", "DAILY_SNAPSHOT", "", "", machine + " [" + id + "]；資料日期：" + start.ToString("yyyy/MM/dd")));
            lines.Add(Row(start.AddMinutes(1), "CHECK", "ONLINE", "18", "https://example.com", "OK"));
            lines.Add(Row(start.AddMinutes(2), "CHECK", "SUSPECTED", "0", "https://example.com", "retry"));
            lines.Add(Row(start.AddMinutes(3), "CHECK", "OFFLINE", "0", "https://example.com", "confirmed"));
            if (recover) lines.Add(Row(start.AddMinutes(8), "CHECK", "ONLINE", "22", "https://example.com", "recovered"));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
        }

        private static void WriteHealthy(string path, string machine, string id, DateTime start)
        {
            var lines = new List<string>();
            lines.Add("Timestamp,Type,Status,LatencyMs,Target,Detail");
            lines.Add(Row(start, "MARKER", "DAILY_SNAPSHOT", "", "", machine + " [" + id + "]；資料日期：" + start.ToString("yyyy/MM/dd")));
            for (int i = 1; i <= 12; i++) lines.Add(Row(start.AddMinutes(i * 10), "CHECK", "ONLINE", (14 + i).ToString(CultureInfo.InvariantCulture), "https://example.com", "OK"));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
        }

        private static string Row(DateTime time, string type, string status, string latency, string target, string detail)
        {
            return Csv(time.ToString("o")) + "," + type + "," + status + "," + latency + "," + Csv(target) + "," + Csv(detail);
        }

        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
