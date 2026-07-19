using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace NetCheck
{
    internal sealed class ActiveSessionState
    {
        public bool Active { get; set; }
        public string MachineId { get; set; }
        public string CsvPath { get; set; }
        public string BackupCsvPath { get; set; }
        public string SessionFileStem { get; set; }
        public DateTime SessionStart { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public int IntervalSeconds { get; set; }
        public bool Paused { get; set; }
        public DateTime PauseStart { get; set; }
        public List<string> Targets { get; set; }
        public bool UseCustomTargets { get; set; }
        public int ConsecutiveFailures { get; set; }
        public bool OutageConfirmed { get; set; }
        public DateTime SuspectedStart { get; set; }
        public int ProcessId { get; set; }
        public DateTime ProcessStartedUtc { get; set; }
    }

    internal static class SessionStateStore
    {
        public static ActiveSessionState Load()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path)) return null;
                var value = new JavaScriptSerializer().Deserialize<ActiveSessionState>(File.ReadAllText(path, Encoding.UTF8));
                if (value == null || !value.Active || String.IsNullOrWhiteSpace(value.CsvPath)) return null;
                if (value.Targets == null) value.Targets = new List<string>();
                return value;
            }
            catch { return null; }
        }

        public static void Save(ActiveSessionState value)
        {
            if (value == null) throw new ArgumentNullException("value");
            string path = StatePath();
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            string json = new JavaScriptSerializer().Serialize(value);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(temp, path, null, true); }
                catch { File.Copy(temp, path, true); File.Delete(temp); }
            }
            else File.Move(temp, path);
        }

        public static void Delete()
        {
            try { string path = StatePath(); if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        public static bool IsOriginalProcessAlive(ActiveSessionState state)
        {
            if (state == null || state.ProcessId <= 0 || state.ProcessId == Process.GetCurrentProcess().Id) return false;
            try
            {
                using (Process process = Process.GetProcessById(state.ProcessId))
                {
                    return Math.Abs((process.StartTime.ToUniversalTime() - state.ProcessStartedUtc.ToUniversalTime()).TotalSeconds) < 2;
                }
            }
            catch { return false; }
        }

        public static bool RunStorageSelfTest(string path)
        {
            string old = Environment.GetEnvironmentVariable("NETCHECK_SESSION_STATE");
            try
            {
                Environment.SetEnvironmentVariable("NETCHECK_SESSION_STATE", path);
                Delete();
                var expected = new ActiveSessionState
                {
                    Active = true,
                    MachineId = "A1B2C3D4",
                    CsvPath = "C:\\Temp\\NetCheck.csv",
                    SessionFileStem = "NetCheck_TEST-A1B2C3D4_20260718_120000",
                    SessionStart = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Local),
                    LastHeartbeat = new DateTime(2026, 7, 18, 12, 1, 0, DateTimeKind.Local),
                    IntervalSeconds = 60,
                    Targets = new List<string> { "https://example.com/" },
                    ConsecutiveFailures = 2,
                    OutageConfirmed = true,
                    SuspectedStart = new DateTime(2026, 7, 18, 12, 0, 30, DateTimeKind.Local)
                };
                Save(expected);
                ActiveSessionState loaded = Load();
                bool ok = loaded != null && loaded.MachineId == expected.MachineId && loaded.IntervalSeconds == 60
                    && loaded.Targets.Count == 1 && loaded.ConsecutiveFailures == 2 && loaded.OutageConfirmed
                    && loaded.SessionStart != DateTime.MinValue && loaded.LastHeartbeat != DateTime.MinValue;
                Delete();
                return ok && !File.Exists(path);
            }
            finally { Environment.SetEnvironmentVariable("NETCHECK_SESSION_STATE", old); }
        }

        private static string StatePath()
        {
            string path = Environment.GetEnvironmentVariable("NETCHECK_SESSION_STATE");
            if (!String.IsNullOrWhiteSpace(path)) return path;
            PortableSettingsStore.EnsureMigration();
            return PortableSettingsStore.SessionPath;
        }
    }

    internal static class AutoStartManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NetCheckMonitor";

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                {
                    string exe = Assembly.GetExecutingAssembly().Location;
                    key.SetValue(ValueName, "\"" + exe + "\" --resume", RegistryValueKind.String);
                }
                else key.DeleteValue(ValueName, false);
            }
        }
    }

    internal static class ApplicationRecovery
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterApplicationRestart(string commandLine, int flags);

        public static void Register()
        {
            try { RegisterApplicationRestart("--resume", 0); }
            catch { }
        }
    }
}
