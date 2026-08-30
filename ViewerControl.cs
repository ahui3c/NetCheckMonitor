using System;
using System.Globalization;
using System.Web.Script.Serialization;

namespace NetCheck
{
    internal sealed class ViewerControlDesired
    {
        public string Revision { get; set; }
        public string RequestedAtUtc { get; set; }
        public int MonitorIntervalSeconds { get; set; }
        public string BackupTime { get; set; }
    }

    internal sealed class ViewerControlApplied
    {
        public string Revision { get; set; }
        public string AppliedAtUtc { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public int MonitorIntervalSeconds { get; set; }
        public string BackupTime { get; set; }
    }

    internal sealed class ViewerControlDocument
    {
        public int SchemaVersion { get; set; }
        public string MachineName { get; set; }
        public string MachineId { get; set; }
        public ViewerControlDesired Desired { get; set; }
        public ViewerControlApplied Applied { get; set; }
    }

    internal sealed class ViewerControlApplyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int MonitorIntervalSeconds { get; set; }
        public string BackupTime { get; set; }
    }

    internal static class ViewerControlProtocol
    {
        internal const int SchemaVersion = 1;
        internal const string FileName = "NetCheck_Control.json";
        internal const int MinimumMonitorIntervalSeconds = 10;
        internal const int MaximumMonitorIntervalSeconds = 3600;

        internal static ViewerControlDocument Create(string machineName, string machineId, ViewerControlDesired current)
        {
            ViewerControlDesired desired = Clone(current);
            desired.Revision = String.IsNullOrWhiteSpace(desired.Revision) ? Guid.NewGuid().ToString("N") : desired.Revision;
            desired.RequestedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            return new ViewerControlDocument
            {
                SchemaVersion = SchemaVersion,
                MachineName = machineName,
                MachineId = machineId,
                Desired = desired,
                Applied = new ViewerControlApplied
                {
                    Revision = desired.Revision,
                    AppliedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Status = "APPLIED",
                    Message = "監控程式已建立 Viewer 控制設定檔。",
                    MonitorIntervalSeconds = desired.MonitorIntervalSeconds,
                    BackupTime = desired.BackupTime
                }
            };
        }

        internal static ViewerControlDocument Parse(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Viewer 控制設定檔是空白的。");
            ViewerControlDocument value = new JavaScriptSerializer().Deserialize<ViewerControlDocument>(json);
            if (value == null) throw new InvalidOperationException("Viewer 控制設定檔格式錯誤。");
            return value;
        }

        internal static string Serialize(ViewerControlDocument value)
        {
            return new JavaScriptSerializer().Serialize(value);
        }

        internal static void Validate(ViewerControlDocument value, string machineId)
        {
            if (value == null || value.SchemaVersion != SchemaVersion) throw new InvalidOperationException("不支援的 Viewer 控制設定檔版本。");
            if (!String.Equals(value.MachineId, machineId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Viewer 控制設定檔不屬於這台電腦。");
            if (value.Desired == null || String.IsNullOrWhiteSpace(value.Desired.Revision)) throw new InvalidOperationException("Viewer 控制設定缺少修訂編號。");
            if (value.Desired.Revision.Length > 80) throw new InvalidOperationException("Viewer 控制設定修訂編號過長。");
            if (value.Desired.MonitorIntervalSeconds < MinimumMonitorIntervalSeconds || value.Desired.MonitorIntervalSeconds > MaximumMonitorIntervalSeconds)
                throw new InvalidOperationException("監控間隔必須介於 10 到 3600 秒。");
            TimeSpan backup;
            if (!TimeSpan.TryParseExact(value.Desired.BackupTime, @"hh\:mm", CultureInfo.InvariantCulture, out backup) || backup.TotalHours >= 24)
                throw new InvalidOperationException("資料備份時間必須是 00:00 到 23:59。");
        }

        internal static ViewerControlDesired Clone(ViewerControlDesired value)
        {
            if (value == null) value = new ViewerControlDesired { MonitorIntervalSeconds = 60, BackupTime = "23:55" };
            return new ViewerControlDesired
            {
                Revision = value.Revision,
                RequestedAtUtc = value.RequestedAtUtc,
                MonitorIntervalSeconds = value.MonitorIntervalSeconds,
                BackupTime = value.BackupTime
            };
        }

        internal static bool RunSelfTest()
        {
            ViewerControlDocument value = Create("OFFICE-PC", "A1B2C3D4", new ViewerControlDesired { MonitorIntervalSeconds = 75, BackupTime = "21:30" });
            ViewerControlDocument parsed = Parse(Serialize(value));
            Validate(parsed, "A1B2C3D4");
            bool rejected = false;
            parsed.Desired.MonitorIntervalSeconds = 3;
            try { Validate(parsed, "A1B2C3D4"); }
            catch (InvalidOperationException) { rejected = true; }
            return value.Applied.Revision == value.Desired.Revision && rejected;
        }
    }
}
