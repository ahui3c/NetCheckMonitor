using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace NetCheckViewer
{
    internal sealed class ViewerAlert
    {
        public string Key { get; set; }
        public string Severity { get; set; }
        public int SeverityRank { get; set; }
        public string MachineId { get; set; }
        public string MachineName { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public DateTime DetectedAt { get; set; }
        public bool Acknowledged { get; set; }
    }

    internal sealed class ViewerAlertState
    {
        public int SchemaVersion { get; set; }
        public List<string> AcknowledgedKeys { get; set; }
    }

    internal static class AlertCenter
    {
        internal static List<ViewerAlert> Build(ScanResult result, ViewerSettings settings, string rootPath)
        {
            settings = settings ?? ViewerSettings.Defaults();
            var alerts = new List<ViewerAlert>();
            if (result == null) return alerts;
            DateTime now = DateTime.Now;
            foreach (MachineSummary machine in result.Machines)
            {
                AddBackupAlert(alerts, machine, settings, now);
                AddAvailabilityAlert(alerts, machine, result.Monitoring, settings, now);
                AddSpeedAlert(alerts, machine, result.Speeds, settings);
                AddControlAlert(alerts, machine, rootPath, settings, now);
                AddGapAlert(alerts, machine, result.Monitoring, now);
            }
            foreach (string issue in result.Issues.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (String.IsNullOrWhiteSpace(issue)) continue;
                bool serious = issue.IndexOf("無法掃描", StringComparison.OrdinalIgnoreCase) >= 0 || issue.IndexOf("無法讀取", StringComparison.OrdinalIgnoreCase) >= 0;
                alerts.Add(NewAlert("FILE|" + StableHash(issue), serious ? "嚴重" : "資訊", serious ? 3 : 1, "", "所有電腦", "資料來源", "資料讀取異常", issue, now));
            }

            ViewerAlertState state = LoadState();
            var acknowledged = new HashSet<string>(state.AcknowledgedKeys ?? new List<string>(), StringComparer.Ordinal);
            foreach (ViewerAlert alert in alerts) alert.Acknowledged = acknowledged.Contains(alert.Key);
            return alerts.OrderBy(delegate (ViewerAlert value) { return value.Acknowledged; })
                .ThenByDescending(delegate (ViewerAlert value) { return value.SeverityRank; })
                .ThenByDescending(delegate (ViewerAlert value) { return value.DetectedAt; }).ToList();
        }

        internal static void SetAcknowledged(string key, bool acknowledged)
        {
            if (String.IsNullOrWhiteSpace(key)) return;
            ViewerAlertState state = LoadState();
            var values = new HashSet<string>(state.AcknowledgedKeys ?? new List<string>(), StringComparer.Ordinal);
            if (acknowledged) values.Add(key); else values.Remove(key);
            state.AcknowledgedKeys = values.Take(2000).ToList();
            SaveState(state);
        }

        private static void AddBackupAlert(List<ViewerAlert> alerts, MachineSummary machine, ViewerSettings settings, DateTime now)
        {
            double hours = machine.LastBackupTime == DateTime.MinValue ? Double.MaxValue : Math.Max(0, (now - machine.LastBackupTime).TotalHours);
            if (hours <= Math.Max(1, settings.NormalReturnHours)) return;
            bool serious = hours > Math.Max(settings.NormalReturnHours + 1, settings.WarningReturnHours);
            string detail = machine.LastBackupTime == DateTime.MinValue ? "尚未找到備份回傳時間。" : "最後回傳：" + machine.LastBackupTime.ToString("yyyy/MM/dd HH:mm") + "，已超過 " + ((int)hours) + " 小時。";
            alerts.Add(NewAlert("BACKUP|" + machine.MachineId + "|" + machine.LastBackupTime.Ticks, serious ? "嚴重" : "注意", serious ? 3 : 2, machine.MachineId, machine.MachineName, "備份", "備份未準時回傳", detail, machine.LastBackupTime == DateTime.MinValue ? now : machine.LastBackupTime));
        }

        private static void AddAvailabilityAlert(List<ViewerAlert> alerts, MachineSummary machine, List<MonitorRecord> monitoring, ViewerSettings settings, DateTime now)
        {
            DateTime since = now.AddHours(-24);
            List<MonitorRecord> rows = monitoring.Where(delegate (MonitorRecord value)
            {
                return String.Equals(value.MachineId, machine.MachineId, StringComparison.OrdinalIgnoreCase) && value.Time >= since
                    && (value.Status == "ONLINE" || value.Status == "OFFLINE");
            }).ToList();
            if (rows.Count == 0) return;
            int online = rows.Count(delegate (MonitorRecord value) { return value.Status == "ONLINE"; });
            double availability = 100.0 * online / rows.Count;
            double threshold = settings.AvailabilityThreshold24Hours <= 0 || settings.AvailabilityThreshold24Hours > 100 ? 99 : settings.AvailabilityThreshold24Hours;
            if (availability >= threshold) return;
            bool serious = availability < Math.Min(95, threshold - 2);
            alerts.Add(NewAlert("AVAIL24|" + machine.MachineId + "|" + DateTime.Today.Ticks, serious ? "嚴重" : "注意", serious ? 3 : 2, machine.MachineId, machine.MachineName, "連線率", "24 小時連線率低於門檻", "目前 " + availability.ToString("0.00") + "%，門檻 " + threshold.ToString("0.00") + "%；有效檢查 " + rows.Count + " 次。", now));
        }

        private static void AddSpeedAlert(List<ViewerAlert> alerts, MachineSummary machine, List<SpeedRecord> speeds, ViewerSettings settings)
        {
            List<SpeedRecord> scheduled = speeds.Where(delegate (SpeedRecord value)
            {
                return String.Equals(value.MachineId, machine.MachineId, StringComparison.OrdinalIgnoreCase) && String.Equals(value.Mode, "Scheduled", StringComparison.OrdinalIgnoreCase);
            }).OrderByDescending(delegate (SpeedRecord value) { return value.Time; }).ToList();
            int failures = 0;
            SpeedRecord latest = null;
            foreach (SpeedRecord speed in scheduled)
            {
                if (speed.Status == "COMPLETED") break;
                if (latest == null) latest = speed;
                failures++;
            }
            int threshold = Math.Max(1, settings.SpeedFailureThreshold);
            if (failures < threshold || latest == null) return;
            bool serious = failures >= threshold * 2;
            string reason = String.IsNullOrWhiteSpace(latest.Error) ? "最近狀態：" + latest.Status : latest.Error;
            alerts.Add(NewAlert("SPEED|" + machine.MachineId + "|" + latest.Time.Ticks, serious ? "嚴重" : "注意", serious ? 3 : 2, machine.MachineId, machine.MachineName, "測速", "定時測速連續失敗", "已連續失敗 " + failures + " 次；" + reason, latest.Time));
        }

        private static void AddControlAlert(List<ViewerAlert> alerts, MachineSummary machine, string rootPath, ViewerSettings settings, DateTime now)
        {
            string path = ViewerControlClient.FindControlFile(machine, rootPath);
            if (String.IsNullOrWhiteSpace(path)) return;
            try
            {
                ViewerControlDocument document = ViewerControlClient.Load(path);
                if (document.Desired == null || String.IsNullOrWhiteSpace(document.Desired.Revision)) return;
                if (document.Applied != null && String.Equals(document.Desired.Revision, document.Applied.Revision, StringComparison.Ordinal)) return;
                DateTime requestedUtc;
                if (!DateTime.TryParse(document.Desired.RequestedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out requestedUtc)) return;
                TimeSpan age = now.ToUniversalTime() - requestedUtc.ToUniversalTime();
                int threshold = Math.Max(1, settings.ControlPendingHours);
                if (age.TotalHours < threshold) return;
                bool serious = age.TotalHours >= Math.Max(6, threshold * 3);
                alerts.Add(NewAlert("CONTROL|" + machine.MachineId + "|" + document.Desired.Revision, serious ? "嚴重" : "注意", serious ? 3 : 2, machine.MachineId, machine.MachineName, "遠端設定", "Viewer 設定等待套用", "已等待 " + Math.Max(1, (int)age.TotalHours) + " 小時；請確認 Google Drive 同步及監控程式是否執行。", requestedUtc.ToLocalTime()));
            }
            catch (Exception ex)
            {
                alerts.Add(NewAlert("CONTROL_READ|" + machine.MachineId + "|" + StableHash(ex.Message), "資訊", 1, machine.MachineId, machine.MachineName, "遠端設定", "控制設定檔無法讀取", ex.Message, now));
            }
        }

        private static void AddGapAlert(List<ViewerAlert> alerts, MachineSummary machine, List<MonitorRecord> monitoring, DateTime now)
        {
            DateTime since = now.Date.AddDays(-30);
            List<DateTime> days = monitoring.Where(delegate (MonitorRecord value) { return String.Equals(value.MachineId, machine.MachineId, StringComparison.OrdinalIgnoreCase) && value.Time.Date >= since; })
                .Select(delegate (MonitorRecord value) { return value.Time.Date; }).Distinct().OrderBy(delegate (DateTime value) { return value; }).ToList();
            if (days.Count < 2) return;
            var missing = new List<DateTime>();
            for (DateTime day = days[0]; day < days[days.Count - 1]; day = day.AddDays(1))
                if (!days.Contains(day)) missing.Add(day);
            if (missing.Count == 0) return;
            string examples = String.Join("、", missing.Take(4).Select(delegate (DateTime value) { return value.ToString("MM/dd"); }).ToArray());
            if (missing.Count > 4) examples += " 等";
            alerts.Add(NewAlert("GAP|" + machine.MachineId + "|" + missing[missing.Count - 1].Ticks, "資訊", 1, machine.MachineId, machine.MachineName, "資料完整性", "監控資料日期中斷", "最近 30 天有 " + missing.Count + " 天缺少資料：" + examples, missing[missing.Count - 1]));
        }

        private static ViewerAlert NewAlert(string key, string severity, int rank, string machineId, string machineName, string type, string title, string detail, DateTime detected)
        {
            return new ViewerAlert { Key = key, Severity = severity, SeverityRank = rank, MachineId = machineId, MachineName = machineName, Type = type, Title = title, Detail = detail, DetectedAt = detected };
        }

        private static string StatePath()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_VIEWER_ALERT_STATE_PATH");
            if (!String.IsNullOrWhiteSpace(overridePath)) return overridePath;
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck_Viewer");
            return Path.Combine(directory, "alert-state.json");
        }

        private static ViewerAlertState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path)) return new ViewerAlertState { SchemaVersion = 1, AcknowledgedKeys = new List<string>() };
                ViewerAlertState value = new JavaScriptSerializer().Deserialize<ViewerAlertState>(File.ReadAllText(path, Encoding.UTF8));
                if (value == null || value.SchemaVersion != 1) return new ViewerAlertState { SchemaVersion = 1, AcknowledgedKeys = new List<string>() };
                if (value.AcknowledgedKeys == null) value.AcknowledgedKeys = new List<string>();
                return value;
            }
            catch { return new ViewerAlertState { SchemaVersion = 1, AcknowledgedKeys = new List<string>() }; }
        }

        private static void SaveState(ViewerAlertState value)
        {
            try
            {
                string path = StatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            }
            catch { }
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
    }
}
