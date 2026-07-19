using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace NetCheck
{
    internal sealed class PortableAppSettings
    {
        public int FormatVersion { get; set; }
        public bool LegacyMigrationCompleted { get; set; }
        public string Language { get; set; }
        public bool? CloseToTrayNoticeShown { get; set; }
        public string CloudBackupSchedule { get; set; }
        public MonitorTargetSettings Monitor { get; set; }
    }

    internal static class PortableSettingsStore
    {
        private static readonly object Sync = new object();
        private static string migrationCheckedPath;

        internal static string SettingsPath
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("NETCHECK_PORTABLE_SETTINGS");
                return String.IsNullOrWhiteSpace(value) ? Path.Combine(ExecutableDirectory(), "NetCheckMonitor.settings.json") : value;
            }
        }

        internal static string CloudPath
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("NETCHECK_CLOUD_SETTINGS");
                return String.IsNullOrWhiteSpace(value) ? Path.Combine(ExecutableDirectory(), "NetCheckMonitor.cloud.dat") : value;
            }
        }

        internal static string SessionPath
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("NETCHECK_SESSION_STATE");
                return String.IsNullOrWhiteSpace(value) ? Path.Combine(ExecutableDirectory(), "NetCheckMonitor.session.json") : value;
            }
        }

        internal static string LoadLanguage()
        {
            lock (Sync) { EnsureMigrationLocked(); return ReadSettings(SettingsPath).Language; }
        }

        internal static void SaveLanguage(string language)
        {
            Update(delegate (PortableAppSettings value) { value.Language = language; });
        }

        internal static bool? LoadCloseNoticeShown()
        {
            lock (Sync) { EnsureMigrationLocked(); return ReadSettings(SettingsPath).CloseToTrayNoticeShown; }
        }

        internal static void SaveCloseNoticeShown(bool shown)
        {
            Update(delegate (PortableAppSettings value) { value.CloseToTrayNoticeShown = shown; });
        }

        internal static MonitorTargetSettings LoadMonitorSettings()
        {
            lock (Sync) { EnsureMigrationLocked(); return ReadSettings(SettingsPath).Monitor; }
        }

        internal static void SaveMonitorSettings(MonitorTargetSettings settings)
        {
            Update(delegate (PortableAppSettings value) { value.Monitor = settings; });
        }

        internal static string LoadCloudBackupSchedule()
        {
            lock (Sync) { EnsureMigrationLocked(); return ReadSettings(SettingsPath).CloudBackupSchedule; }
        }

        internal static void SaveCloudBackupSchedule(string schedule)
        {
            Update(delegate (PortableAppSettings value) { value.CloudBackupSchedule = schedule; });
        }

        internal static void EnsureMigration()
        {
            lock (Sync) EnsureMigrationLocked();
        }

        private static void Update(Action<PortableAppSettings> change)
        {
            lock (Sync)
            {
                EnsureMigrationLocked();
                PortableAppSettings value = ReadSettings(SettingsPath);
                change(value);
                value.FormatVersion = 1;
                value.LegacyMigrationCompleted = true;
                WriteSettings(SettingsPath, value);
            }
        }

        private static void EnsureMigrationLocked()
        {
            string path = SettingsPath;
            if (String.Equals(migrationCheckedPath, path, StringComparison.OrdinalIgnoreCase)) return;
            Migrate(path, CloudPath, SessionPath, LegacyMonitorDirectory(), LegacyCloudPath());
            migrationCheckedPath = path;
        }

        private static void Migrate(string settingsPath, string cloudPath, string sessionPath, string legacyMonitorDirectory, string legacyCloudPath)
        {
            PortableAppSettings value = ReadSettings(settingsPath);
            if (value.LegacyMigrationCompleted) return;
            if (value.Monitor == null) value.Monitor = ReadLegacyMonitor(Path.Combine(legacyMonitorDirectory, "settings.json"));
            if (String.IsNullOrWhiteSpace(value.Language)) value.Language = ReadText(Path.Combine(legacyMonitorDirectory, "language.dat"));
            if (!value.CloseToTrayNoticeShown.HasValue)
            {
                string notice = ReadText(Path.Combine(legacyMonitorDirectory, "ui-state.dat"));
                if (!String.IsNullOrEmpty(notice)) value.CloseToTrayNoticeShown = notice.Trim() == "1";
            }
            if (!CopyLegacyFile(Path.Combine(legacyMonitorDirectory, "active-session.json"), sessionPath)
                || !CopyLegacyFile(legacyCloudPath, cloudPath)) throw new IOException("Legacy portable-settings migration could not copy all files.");
            value.FormatVersion = 1;
            value.LegacyMigrationCompleted = true;
            WriteSettings(settingsPath, value);
        }

        private static PortableAppSettings ReadSettings(string path)
        {
            try
            {
                if (!File.Exists(path)) return new PortableAppSettings { FormatVersion = 1 };
                PortableAppSettings value = new JavaScriptSerializer().Deserialize<PortableAppSettings>(File.ReadAllText(path, Encoding.UTF8));
                return value ?? new PortableAppSettings { FormatVersion = 1 };
            }
            catch { return new PortableAppSettings { FormatVersion = 1 }; }
        }

        private static MonitorTargetSettings ReadLegacyMonitor(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                MonitorTargetSettings value = new JavaScriptSerializer().Deserialize<MonitorTargetSettings>(json);
                if (value != null && json.IndexOf("\"PreventSleepWhileMonitoring\"", StringComparison.Ordinal) < 0) value.PreventSleepWhileMonitoring = true;
                return value;
            }
            catch { return null; }
        }

        private static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : null; }
            catch { return null; }
        }

        private static bool CopyLegacyFile(string source, string destination)
        {
            try
            {
                if (!File.Exists(source) || File.Exists(destination)) return true;
                string directory = Path.GetDirectoryName(destination);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(source, destination, false);
                return true;
            }
            catch { return false; }
        }

        private static void WriteSettings(string path, PortableAppSettings value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null, true);
                else File.Move(temp, path);
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
        }

        private static string ExecutableDirectory()
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return String.IsNullOrEmpty(path) ? AppDomain.CurrentDomain.BaseDirectory : path;
        }

        private static string LegacyMonitorDirectory()
        {
            string value = Environment.GetEnvironmentVariable("NETCHECK_LEGACY_MONITOR_DIR");
            return String.IsNullOrWhiteSpace(value) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Monitor") : value;
        }

        private static string LegacyCloudPath()
        {
            string value = Environment.GetEnvironmentVariable("NETCHECK_LEGACY_CLOUD_SETTINGS");
            return String.IsNullOrWhiteSpace(value) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Cloud", "settings.dat") : value;
        }

        internal static bool RunMigrationSelfTest(string root)
        {
            string portable = Path.Combine(root, "Portable");
            string legacyMonitor = Path.Combine(root, "LegacyMonitor");
            string legacyCloud = Path.Combine(root, "LegacyCloud", "settings.dat");
            Directory.CreateDirectory(legacyMonitor);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyCloud));
            var legacySettings = new MonitorTargetSettings { UseCustomTargets = true, CustomTargets = new System.Collections.Generic.List<string> { "https://example.com/" }, AutoStartMonitoring = true, PreventSleepWhileMonitoring = true };
            File.WriteAllText(Path.Combine(legacyMonitor, "settings.json"), new JavaScriptSerializer().Serialize(legacySettings), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(legacyMonitor, "language.dat"), "zh-TW", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(legacyMonitor, "ui-state.dat"), "1", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(legacyMonitor, "active-session.json"), "session", new UTF8Encoding(false));
            File.WriteAllBytes(legacyCloud, new byte[] { 1, 2, 3, 4 });
            string settings = Path.Combine(portable, "NetCheckMonitor.settings.json");
            string cloud = Path.Combine(portable, "NetCheckMonitor.cloud.dat");
            string session = Path.Combine(portable, "NetCheckMonitor.session.json");
            Migrate(settings, cloud, session, legacyMonitor, legacyCloud);
            PortableAppSettings loaded = ReadSettings(settings);
            File.WriteAllText(Path.Combine(legacyMonitor, "language.dat"), "en-US", new UTF8Encoding(false));
            Migrate(settings, cloud, session, legacyMonitor, legacyCloud);
            PortableAppSettings second = ReadSettings(settings);
            return loaded.LegacyMigrationCompleted && loaded.FormatVersion == 1 && loaded.Language == "zh-TW"
                && loaded.CloseToTrayNoticeShown == true && loaded.Monitor != null && loaded.Monitor.AutoStartMonitoring
                && File.Exists(cloud) && File.Exists(session) && second.Language == "zh-TW";
        }
    }
}
