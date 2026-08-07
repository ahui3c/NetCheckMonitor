using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetCheck
{
    internal static class DeliveryAuditLog
    {
        private static readonly object Sync = new object();

        internal static void Record(string machineName, string machineId, string service, string action, string status, string detail)
        {
            try
            {
                string directory = DataDirectory();
                string path = Path.Combine(directory, "NetCheck_Delivery_" + Safe(machineName, 16) + "-" + Safe(machineId, 16) + ".csv");
                string safeStatus = NormalizeCode(status, "FAILED");
                string safeService = NormalizeCode(service, "UNKNOWN");
                string safeAction = NormalizeCode(action, "UNKNOWN");
                string safeDetail = CleanDetail(detail);
                lock (Sync)
                {
                    bool create = !File.Exists(path) || new FileInfo(path).Length == 0;
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                    {
                        if (create) writer.WriteLine("Timestamp,Type,Status,LatencyMs,Target,Detail");
                        writer.WriteLine(Csv(DateTime.Now.ToString("o", CultureInfo.InvariantCulture)) + ",DELIVERY," + safeStatus + ",," + Csv(safeService) + "," + Csv("Action=" + safeAction + (String.IsNullOrEmpty(safeDetail) ? "" : ";" + safeDetail)));
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
            }
            catch { }
        }

        internal static string DataDirectory()
        {
            string configured = Environment.GetEnvironmentVariable("NETCHECK_DELIVERY_LOG_DIR");
            if (!String.IsNullOrWhiteSpace(configured)) { Directory.CreateDirectory(configured); return configured; }
            string executable = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string portable = Path.Combine(executable, "NetCheck_Data");
            try { Directory.CreateDirectory(portable); return portable; }
            catch
            {
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Data");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        private static string NormalizeCode(string value, string fallback)
        {
            var result = new StringBuilder();
            foreach (char c in (value ?? "").ToUpperInvariant())
            {
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') result.Append(c);
                if (result.Length >= 40) break;
            }
            return result.Length == 0 ? fallback : result.ToString();
        }

        private static string CleanDetail(string value)
        {
            string result = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (result.IndexOf("  ", StringComparison.Ordinal) >= 0) result = result.Replace("  ", " ");
            return result.Length > 1000 ? result.Substring(0, 1000) : result;
        }

        private static string Safe(string value, int max)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "PC")
            {
                if (Char.IsLetterOrDigit(c) || c == '-' || c == '_') result.Append(c);
                if (result.Length >= max) break;
            }
            return result.Length == 0 ? "PC" : result.ToString();
        }

        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
