using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NetCheck
{
    internal sealed class DataReportForm : Form
    {
        private readonly string machineName;
        private readonly string machineId;
        private readonly RadioButton allDates = new RadioButton();
        private readonly RadioButton dateRange = new RadioButton();
        private readonly DateTimePicker fromDate = new DateTimePicker();
        private readonly DateTimePicker toDate = new DateTimePicker();
        private readonly Button exportButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly Label statusLabel = new Label();

        public DataReportForm(string computerName, string computerId)
        {
            machineName = computerName;
            machineId = computerId;
            Text = L.T("下載 NetCheckMonitor PDF 報表", "Download NetCheckMonitor PDF Report");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(600, 315);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label { Text = L.T("下載監控報表", "Download Monitoring Report"), Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
            var note = new Label { Text = L.T("可在監控進行中匯出 PDF；讀取的是產生當下已儲存的資料。", "You can export a PDF while monitoring. It uses all data saved at export time."), AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 60) };
            allDates.Text = L.T("全部已儲存資料", "All Saved Data");
            allDates.Checked = true;
            allDates.SetBounds(28, 98, 180, 28);
            dateRange.Text = L.T("指定日期範圍", "Date Range");
            dateRange.SetBounds(28, 136, 150, 28);
            fromDate.Format = DateTimePickerFormat.Custom;
            fromDate.CustomFormat = "yyyy/MM/dd";
            fromDate.Value = DateTime.Today.AddDays(-7);
            fromDate.SetBounds(185, 136, 130, 28);
            toDate.Format = DateTimePickerFormat.Custom;
            toDate.CustomFormat = "yyyy/MM/dd";
            toDate.Value = DateTime.Today;
            toDate.SetBounds(345, 136, 130, 28);
            var separator = new Label { Text = L.T("至", "to"), AutoSize = true, Location = new Point(321, 141) };
            fromDate.Enabled = toDate.Enabled = false;

            exportButton.Text = L.T("下載為 PDF", "Download PDF");
            exportButton.SetBounds(28, 190, 150, 42);
            closeButton.Text = L.T("關閉", "Close");
            closeButton.SetBounds(420, 190, 135, 42);
            statusLabel.Text = L.T("PDF 會包含每日斷線統計、網卡／連線類型／Wi-Fi 訊號、延遲指標與時間軸。", "The PDF includes outage statistics, adapter/type/Wi-Fi signal, latency metrics, and timelines.");
            statusLabel.AutoSize = false;
            statusLabel.SetBounds(28, 255, 527, 45);
            statusLabel.ForeColor = Color.DimGray;

            Controls.AddRange(new Control[] { title, note, allDates, dateRange, fromDate, separator, toDate, exportButton, closeButton, statusLabel });
            allDates.CheckedChanged += delegate { fromDate.Enabled = toDate.Enabled = dateRange.Checked; };
            exportButton.Click += delegate { ExportPdf(); };
            closeButton.Click += delegate { Close(); };
        }

        private void ExportPdf()
        {
            if (dateRange.Checked && fromDate.Value.Date > toDate.Value.Date)
            {
                MessageBox.Show(L.T("開始日期不能晚於結束日期。", "The start date cannot be later than the end date."), "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string range = allDates.Checked ? "All" : fromDate.Value.ToString("yyyyMMdd") + "-" + toDate.Value.ToString("yyyyMMdd");
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = L.T("下載 NetCheckMonitor PDF 報表", "Download NetCheckMonitor PDF Report");
                dialog.Filter = L.T("PDF 檔案 (*.pdf)|*.pdf", "PDF files (*.pdf)|*.pdf");
                dialog.DefaultExt = "pdf";
                dialog.AddExtension = true;
                dialog.FileName = "NetCheck_Report_" + Safe(machineName, 16) + "-" + machineId + "_" + range + ".pdf";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string output = dialog.FileName;
                bool exportAll = allDates.Checked;
                DateTime exportFrom = fromDate.Value.Date;
                DateTime exportTo = toDate.Value.Date;
                SetBusy(true, L.T("正在整理已儲存資料並製作 PDF…監控仍會繼續。", "Preparing saved data and creating the PDF… Monitoring continues."));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string error = null;
                    try { ArchiveReport.ExportPdf(output, exportAll, exportFrom, exportTo); }
                    catch (Exception ex) { error = ex.Message; }
                    if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate
                    {
                        SetBusy(false, error == null ? L.T("PDF 已儲存：", "PDF saved: ") + output : L.T("匯出失敗：", "Export failed: ") + error);
                        if (error == null)
                        {
                            if (MessageBox.Show(L.T("PDF 報表已下載完成。\n\n", "The PDF report is ready.\n\n") + output + L.T("\n\n是否立即開啟？", "\n\nOpen it now?"), "NetCheckMonitor", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                                Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
                        }
                        else MessageBox.Show(error, L.T("PDF 匯出失敗", "PDF Export Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                });
            }
        }

        private void SetBusy(bool busy, string text)
        {
            exportButton.Enabled = closeButton.Enabled = !busy;
            allDates.Enabled = dateRange.Enabled = !busy;
            fromDate.Enabled = toDate.Enabled = !busy && dateRange.Checked;
            statusLabel.Text = text;
        }

        private static string Safe(string value, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in value ?? "PC") { if (Char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c); if (sb.Length >= max) break; }
            return sb.Length == 0 ? "PC" : sb.ToString();
        }
    }

    internal static class ArchiveReport
    {
        private sealed class Record { public DateTime Time; public bool Online; public string Status; public long Latency; public string Target; public string Detail; }
        private sealed class Period { public DateTime Start; public DateTime End; }
        private sealed class Outage { public DateTime Start; public DateTime End; public int Count; public string Machine; public TimeSpan Duration; }
        private sealed class ReportEvent { public bool IsNote; public DateTime Time; public DateTime End; public string Machine; public string Text; public TimeSpan Duration; public int Count; }
        private sealed class Session
        {
            public string MachineName = Environment.MachineName;
            public string MachineId = "LEGACY";
            public DateTime Start = DateTime.MaxValue;
            public DateTime End = DateTime.MinValue;
            public bool Stopped;
            public string AdapterName = "";
            public string AdapterDescription = "";
            public string ConnectionType = "Disconnected";
            public int WifiSignal = -1;
            public readonly List<Record> Records = new List<Record>();
            public readonly List<Period> Pauses = new List<Period>();
            public readonly List<EventNote> EventNotes = new List<EventNote>();
            public string SourceFile;
        }
        private sealed class Daily
        {
            public string MachineName;
            public string MachineId;
            public DateTime Day;
            public TimeSpan Effective;
            public TimeSpan Outage;
            public int Checks;
            public int Offline;
            public int OutageEvents;
            public TimeSpan LongestOutage;
            public readonly List<Record> Records = new List<Record>();
            public readonly List<Period> Pauses = new List<Period>();
            public readonly List<EventNote> EventNotes = new List<EventNote>();
        }

        public static void ExportPdf(string outputPath, bool allDates, DateTime from, DateTime to)
        {
            List<Session> sessions = LoadSessions();
            if (!HasStatisticalData(sessions)) throw new InvalidOperationException(L.T("找不到任何有效的 NetCheck 監控檢查資料。", "No valid NetCheck monitoring checks were found."));
            DateTime start = allDates ? MinStart(sessions).Date : from.Date;
            DateTime endExclusive = allDates ? MaxEnd(sessions).Date.AddDays(1) : to.Date.AddDays(1);
            string html = BuildHtml(sessions, start, endExclusive, allDates);
            string tempRoot = Path.Combine(Path.GetTempPath(), "NetCheckPdf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            string htmlPath = Path.Combine(tempRoot, "report.html");
            string profilePath = Path.Combine(tempRoot, "edge-profile");
            try
            {
                File.WriteAllText(htmlPath, html, new UTF8Encoding(true));
                string edge = FindEdge();
                if (edge == null) throw new InvalidOperationException(L.T("找不到 Microsoft Edge，無法自動產生 PDF。請先安裝或修復 Microsoft Edge。", "Microsoft Edge was not found, so the PDF cannot be created automatically. Install or repair Microsoft Edge."));
                if (File.Exists(outputPath)) File.Delete(outputPath);
                string args = "--headless --disable-gpu --disable-extensions --no-pdf-header-footer --print-to-pdf=\"" + outputPath + "\" --user-data-dir=\"" + profilePath + "\" \"" + new Uri(htmlPath).AbsoluteUri + "\"";
                var info = new ProcessStartInfo(edge, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                using (Process process = Process.Start(info))
                {
                    if (process == null || !process.WaitForExit(60000)) { try { if (process != null) process.Kill(); } catch { } throw new TimeoutException(L.T("PDF 產生超過 60 秒，已取消。", "PDF generation exceeded 60 seconds and was canceled.")); }
                }
                for (int i = 0; i < 20 && !File.Exists(outputPath); i++) Thread.Sleep(250);
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000) throw new InvalidOperationException(L.T("Microsoft Edge 未能建立有效的 PDF 檔案。", "Microsoft Edge did not create a valid PDF file."));
                using (var stream = File.OpenRead(outputPath))
                {
                    byte[] signature = new byte[5];
                    if (stream.Read(signature, 0, 5) != 5 || Encoding.ASCII.GetString(signature) != "%PDF-") throw new InvalidDataException(L.T("輸出檔不是有效的 PDF。", "The output file is not a valid PDF."));
                }
            }
            finally { try { Directory.Delete(tempRoot, true); } catch { } }
        }

        public static string WriteCumulativeHtml(string outputPath, bool live)
        {
            return WriteCumulativeHtmlCore(outputPath, live, false);
        }

        public static string ForceRebuildDailyDetailReports(string outputPath, bool live)
        {
            return WriteCumulativeHtmlCore(outputPath, live, true);
        }

        private static string WriteCumulativeHtmlCore(string outputPath, bool live, bool forceDailyDetails)
        {
            List<Session> sessions = LoadSessions();
            if (!HasStatisticalData(sessions))
            {
                string emptyTitle = L.T("對外網路連線能力累積監控報表", "NetCheckMonitor Cumulative Network Monitoring Report");
                string emptyMessage = L.T("目前沒有有效的監控檢查資料；沒有紀錄的時間不會納入統計。", "There are currently no valid monitoring checks. Unrecorded time is not included in statistics.");
                string emptyHtml = "<!doctype html><html lang='" + L.HtmlLanguage + "'><head><meta charset='utf-8'><title>" + H(emptyTitle) + "</title><style>body{font-family:'Microsoft JhengHei UI','Segoe UI',sans-serif;margin:36px;color:#17202a}.card{border:1px solid #dfe4e8;border-radius:10px;padding:20px;max-width:760px}</style></head><body><h1>" + H(emptyTitle) + "</h1><div class='card'>" + H(emptyMessage) + "</div></body></html>";
                File.WriteAllText(outputPath, emptyHtml, new UTF8Encoding(true));
                return outputPath;
            }
            DateTime start = MinStart(sessions).Date;
            DateTime endExclusive = MaxEnd(sessions).Date.AddDays(1);
            Dictionary<string, string> dailyLinks = PrepareDailyDetailReports(outputPath, sessions, start, endExclusive, forceDailyDetails);
            string html = BuildHtmlCore(sessions, start, endExclusive, true, false, dailyLinks);
            html = AddScreenStyles(html);
            string note = live
                ? L.T("這是監控進行中的累積即時快照；所有尚未清除的歷史 CSV 都已納入，產生報表不會中斷檢查。", "This is a cumulative live snapshot. All historical CSV files that have not been cleared are included, and creating the report does not interrupt monitoring.")
                : L.T("這是所有尚未清除之監控資料的累積報表。", "This is a cumulative report of all monitoring data that has not been cleared.");
            html = html.Replace("</body>", "<div class='foot'>" + H(note) + "</div></body>");
            File.WriteAllText(outputPath, html, new UTF8Encoding(true));
            return outputPath;
        }

        private static Dictionary<string, string> PrepareDailyDetailReports(string outputPath, List<Session> sessions, DateTime start, DateTime endExclusive, bool force)
        {
            var days = new SortedDictionary<string, DateTime>();
            foreach (Session session in sessions)
                foreach (Record record in session.Records)
                    if (record.Time >= start && record.Time < endExclusive) days[record.Time.ToString("yyyyMMdd")] = record.Time.Date;
            string directory = Path.GetDirectoryName(outputPath);
            if (String.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);
            var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, DateTime> item in days)
            {
                string fileName = DailyDetailFileName(outputPath, item.Value);
                string path = Path.Combine(directory, fileName);
                bool completedDay = item.Value.Date < DateTime.Today;
                if (force || !completedDay || !File.Exists(path))
                {
                    string dailyHtml = BuildHtmlCore(sessions, item.Value.Date, item.Value.Date.AddDays(1), false, true, null);
                    dailyHtml = AddScreenStyles(dailyHtml);
                    WriteHtmlFile(path, dailyHtml);
                }
                if (File.Exists(path)) links[item.Key] = fileName;
            }
            return links;
        }

        private static string DailyDetailFileName(string outputPath, DateTime day)
        {
            string stem = Path.GetFileNameWithoutExtension(outputPath);
            string[] suffixes = new string[] { "_Cumulative_Live", "_Cumulative_Report" };
            foreach (string suffix in suffixes)
                if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { stem = stem.Substring(0, stem.Length - suffix.Length); break; }
            return stem + "_Daily_Detail_" + day.ToString("yyyyMMdd") + ".html";
        }

        private static string AddScreenStyles(string html)
        {
            string screenStyles = "<style>body{font-size:16px;line-height:1.5;padding:24px}h1{font-size:28px}h2{font-size:20px}table{font-size:16px}th,td{padding:9px}.metric b{font-size:24px}.legend,.foot{font-size:13px}</style>";
            return html.Replace("</head>", screenStyles + "</head>");
        }

        private static void WriteHtmlFile(string path, string html)
        {
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, html, new UTF8Encoding(true));
                File.Copy(temp, path, true);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        public static string FindLatestCumulativeHtml(string machineId)
        {
            string latest = null;
            DateTime latestWrite = DateTime.MinValue;
            string id = machineId ?? String.Empty;
            foreach (string file in GetManagedFiles())
            {
                if (!file.EndsWith("_Cumulative_Report.html", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Length > 0 && Path.GetFileName(file).IndexOf("-" + id + "_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                DateTime write;
                try { write = File.GetLastWriteTime(file); }
                catch { continue; }
                if (latest == null || write > latestWrite) { latest = file; latestWrite = write; }
            }
            return latest;
        }

        public static string EnsureCumulativeHtml(string machineName, string machineId)
        {
            List<Session> sessions = LoadSessions();
            if (!HasStatisticalData(sessions)) return null;
            string existing = FindLatestCumulativeHtml(machineId);
            DateTime newestCsvWrite = DateTime.MinValue;
            Session newestSession = null;
            foreach (Session session in sessions)
            {
                if (session.Records.Count == 0) continue;
                DateTime write;
                try { write = File.GetLastWriteTime(session.SourceFile); }
                catch { continue; }
                if (newestSession == null || write > newestCsvWrite) { newestSession = session; newestCsvWrite = write; }
            }
            if (existing != null)
            {
                try { if (File.GetLastWriteTime(existing) >= newestCsvWrite && HasDailyDetailLinkMarker(existing)) return existing; }
                catch { }
            }
            if (newestSession == null || String.IsNullOrEmpty(newestSession.SourceFile)) return existing;
            string directory = Path.GetDirectoryName(newestSession.SourceFile);
            string name = "NetCheck_" + SafeName(machineName, 16) + "-" + (machineId ?? "LEGACY") + "_Cumulative_Report.html";
            return WriteCumulativeHtml(Path.Combine(directory, name), false);
        }

        private static bool HasDailyDetailLinkMarker(string path)
        {
            const string marker = "NETCHECK_DAILY_DETAIL_LINKS_V1";
            char[] buffer = new char[8192];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                int count = reader.Read(buffer, 0, buffer.Length);
                return count > 0 && new string(buffer, 0, count).IndexOf(marker, StringComparison.Ordinal) >= 0;
            }
        }

        public static int ExportAllDataZip(string outputPath)
        {
            List<string> files = GetManagedFiles();
            if (files.Count == 0) throw new InvalidOperationException(L.T("目前沒有可匯出的監控紀錄資料。", "There is currently no monitoring data to export."));
            string directory = Path.GetDirectoryName(outputPath);
            if (String.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, ".NetCheckMonitor_Backup_" + Guid.NewGuid().ToString("N") + ".tmp");
            int count = 0;
            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var manifest = new List<string>();
            try
            {
                using (var archiveStream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, false))
                {
                    foreach (string file in files)
                    {
                        if (!File.Exists(file)) continue;
                        string extension = Path.GetExtension(file).ToLowerInvariant();
                        string folder = extension == ".csv" ? "CSV_Raw_Data" : (extension == ".pdf" ? "PDF_Reports" : "HTML_Reports");
                        string baseName = Path.GetFileName(file);
                        string candidate = folder + "/" + baseName;
                        int duplicate = 1;
                        while (usedNames.ContainsKey(candidate))
                        {
                            duplicate++;
                            candidate = folder + "/" + Path.GetFileNameWithoutExtension(baseName) + "_copy" + duplicate + extension;
                        }
                        usedNames[candidate] = 1;
                        ZipArchiveEntry entry = archive.CreateEntry(candidate, CompressionLevel.Optimal);
                        using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (Stream target = entry.Open()) source.CopyTo(target);
                        manifest.Add(candidate + "\t" + new FileInfo(file).Length + " bytes");
                        count++;
                    }
                    if (count == 0) throw new InvalidOperationException(L.T("監控紀錄檔案已不存在或無法讀取。", "The monitoring files no longer exist or could not be read."));
                    ZipArchiveEntry manifestEntry = archive.CreateEntry("Backup_Manifest.txt", CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(true)))
                    {
                        writer.WriteLine("NetCheckMonitor 0.9.7");
                        writer.WriteLine("Exported: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
                        writer.WriteLine("Computer: " + Environment.MachineName);
                        writer.WriteLine("Files: " + count);
                        writer.WriteLine();
                        foreach (string line in manifest) writer.WriteLine(line);
                    }
                }
                File.Copy(temp, outputPath, true);
                return count;
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        public static string[] ExportDailyArtifacts(string outputDirectory, string machineName, string machineId, DateTime day)
        {
            Directory.CreateDirectory(outputDirectory);
            string stem = "NetCheck_" + SafeName(machineName, 16) + "-" + machineId + "_" + day.ToString("yyyyMMdd");
            string pdf = Path.Combine(outputDirectory, stem + "_Report.pdf");
            string csv = Path.Combine(outputDirectory, stem + "_Raw.csv");
            ExportPdf(pdf, false, day.Date, day.Date);
            CreateDailyCsv(csv, machineName, machineId, day.Date);
            return new string[] { pdf, csv };
        }

        public static bool HasChecksForDay(DateTime day)
        {
            DateTime start = day.Date, end = start.AddDays(1);
            foreach (string file in GetCsvFiles())
            {
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            List<string> fields = ParseCsv(line); DateTime time;
                            if (fields.Count >= 3 && fields[1] == "CHECK" && DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time) && time >= start && time < end) return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        private static void CreateDailyCsv(string outputPath, string machineName, string machineId, DateTime day)
        {
            DateTime end = day.AddDays(1);
            var rows = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int checks = 0;
            foreach (string file in GetCsvFiles())
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        List<string> fields = ParseCsv(line);
                        if (fields.Count < 6 || fields[0] == "Timestamp") continue;
                        DateTime time;
                        if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time) || time < day || time >= end) continue;
                        if (seen.Add(line)) rows.Add(line);
                        if (fields[1] == "CHECK") checks++;
                    }
                }
            }
            if (checks == 0) throw new InvalidOperationException(day.ToString("yyyy/MM/dd") + L.T(" 沒有可備份的監控檢查資料。", " has no monitoring checks to back up."));
            using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Timestamp,Type,Status,LatencyMs,Target,Detail");
                writer.WriteLine(Csv(DateTime.Now.ToString("o")) + ",MARKER,DAILY_SNAPSHOT,,," + Csv(machineName + " [" + machineId + "]" + L.T("；資料日期：", "; data date: ") + day.ToString("yyyy/MM/dd")));
                foreach (string row in rows) writer.WriteLine(row);
            }
        }

        public static List<string> ClearAllData(out int deleted)
        {
            deleted = 0;
            var files = GetManagedFiles();
            var locked = new List<string>();
            foreach (string path in files)
            {
                if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                try { using (FileStream s = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                catch { locked.Add(Path.GetFileName(path)); }
            }
            if (locked.Count > 0) return locked;
            foreach (string path in files) { try { File.Delete(path); deleted++; } catch (Exception ex) { locked.Add(Path.GetFileName(path) + L.T("：", ": ") + ex.Message); } }
            return locked;
        }

        private static List<Session> LoadSessions()
        {
            var files = GetCsvFiles();
            var result = new List<Session>();
            foreach (string file in files)
            {
                try { var session = ParseSession(file); if (session != null) result.Add(session); } catch { }
            }
            return result;
        }

        private static Session ParseSession(string path)
        {
            var session = new Session { SourceFile = path };
            bool pauseOpen = false;
            DateTime pauseStart = DateTime.MinValue;
            DateTime last = DateTime.MinValue;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> f = ParseCsv(line);
                    if (f.Count < 6 || f[0] == "Timestamp") continue;
                    DateTime time;
                    if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) continue;
                    if (time > last) last = time;
                    string type = f[1], status = f[2], detail = f[5];
                    if (type == "MARKER")
                    {
                        if (status == "COMPUTER") ParseComputer(detail, session);
                        else if (status == "NETWORK") ParseNetwork(detail, session);
                        else if (status == "EVENT_NOTE") session.EventNotes.Add(new EventNote { Time = time, Text = detail });
                        else if (status == "STARTED") session.Start = time;
                        else if (status == "STOPPED") { session.End = time; session.Stopped = true; }
                        else if (status == "PAUSED" || status == "INTERRUPTED") { pauseOpen = true; pauseStart = time; }
                        else if ((status == "RESUMED" || status == "SESSION_RESUMED") && pauseOpen) { session.Pauses.Add(new Period { Start = pauseStart, End = time }); pauseOpen = false; }
                    }
                    else if (type == "CHECK")
                    {
                        long latency;
                        Int64.TryParse(f[3], out latency);
                        session.Records.Add(new Record { Time = time, Online = status == "ONLINE", Status = status, Latency = latency, Target = f[4], Detail = detail });
                        if (session.Start == DateTime.MaxValue) session.Start = time;
                    }
                }
            }
            if (session.Start == DateTime.MaxValue) return null;
            if (!session.Stopped)
            {
                DateTime write = File.GetLastWriteTime(path);
                session.End = (DateTime.Now - write).TotalMinutes <= 5 ? DateTime.Now : last;
            }
            if (session.End < session.Start) session.End = last >= session.Start ? last : session.Start;
            if (pauseOpen) session.Pauses.Add(new Period { Start = pauseStart, End = session.End });
            session.Records.Sort(delegate (Record a, Record b) { return a.Time.CompareTo(b.Time); });
            session.EventNotes.Sort(delegate (EventNote a, EventNote b) { return a.Time.CompareTo(b.Time); });
            return session;
        }

        private static void ParseComputer(string detail, Session session)
        {
            int open = detail.IndexOf(" [", StringComparison.Ordinal);
            int close = open >= 0 ? detail.IndexOf(']', open + 2) : -1;
            if (open > 0) session.MachineName = detail.Substring(0, open).Trim();
            if (open >= 0 && close > open) session.MachineId = detail.Substring(open + 2, close - open - 2).Trim();
        }

        private static void ParseNetwork(string detail, Session session)
        {
            foreach (string part in (detail ?? "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;
                string key = part.Substring(0, equals), value = part.Substring(equals + 1);
                if (key == "Adapter") session.AdapterName = DecodeNetworkValue(value);
                else if (key == "Description") session.AdapterDescription = DecodeNetworkValue(value);
                else if (key == "Type") session.ConnectionType = value;
                else if (key == "Signal") { int signal; if (Int32.TryParse(value, out signal)) session.WifiSignal = signal; }
            }
        }

        private static string DecodeNetworkValue(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return ""; }
        }

        private static string BuildHtml(List<Session> sessions, DateTime start, DateTime endExclusive, bool allDates)
        {
            return BuildHtmlCore(sessions, start, endExclusive, allDates, true, null);
        }

        private static string BuildHtmlCore(List<Session> sessions, DateTime start, DateTime endExclusive, bool allDates, bool includeDetailedRecords, Dictionary<string, string> dailyLinks)
        {
            var daily = new SortedDictionary<string, Daily>();
            var selectedOutages = new List<Outage>();
            var machines = new SortedDictionary<string, Session>();
            int checks = 0, offline = 0, sourceFiles = 0;
            long latencyTotal = 0; int latencyCount = 0;
            var latencies = new List<long>();
            TimeSpan effectiveTotal = TimeSpan.Zero, outageTotal = TimeSpan.Zero;

            foreach (Session s in sessions)
            {
                // A session with no CHECK rows contains no measurable monitoring
                // interval. Keep its file on disk, but do not add its wall-clock
                // duration to cumulative availability or outage statistics.
                if (s.Records.Count == 0) continue;
                DateTime aSession = Max(s.Start, start), bSession = Min(s.End, endExclusive);
                if (bSession <= aSession) continue;
                sourceFiles++;
                Session latestMachine;
                if (!machines.TryGetValue(s.MachineId, out latestMachine) || s.End >= latestMachine.End) machines[s.MachineId] = s;
                List<Outage> outages = BuildOutages(s);
                for (DateTime day = aSession.Date; day < bSession; day = day.AddDays(1))
                {
                    DateTime a = Max(aSession, day), b = Min(bSession, day.AddDays(1));
                    string key = day.ToString("yyyyMMdd") + "|" + s.MachineId;
                    Daily d;
                    if (!daily.TryGetValue(key, out d)) { d = new Daily { Day = day, MachineName = s.MachineName, MachineId = s.MachineId }; daily[key] = d; }
                    TimeSpan active = Effective(a, b, s.Pauses);
                    d.Effective += active; effectiveTotal += active;
                    foreach (Outage o in outages)
                    {
                        DateTime oa = Max(o.Start, a), ob = Min(o.End, b);
                        if (ob > oa)
                        {
                            TimeSpan lost = Effective(oa, ob, s.Pauses);
                            d.Outage += lost; outageTotal += lost; d.OutageEvents++;
                            if (lost > d.LongestOutage) d.LongestOutage = lost;
                        }
                    }
                    foreach (Record r in s.Records) if (r.Time >= a && r.Time < b)
                    {
                        d.Records.Add(r);
                        string status = String.IsNullOrEmpty(r.Status) ? (r.Online ? "ONLINE" : "OFFLINE") : r.Status;
                        if (status == "ONLINE") { d.Checks++; checks++; latencyTotal += r.Latency; latencyCount++; latencies.Add(r.Latency); }
                        else if (status == "OFFLINE") { d.Checks++; checks++; d.Offline++; offline++; }
                    }
                    foreach (EventNote note in s.EventNotes) if (note.Time >= a && note.Time < b) d.EventNotes.Add(note);
                    foreach (Period p in s.Pauses) { DateTime pa = Max(p.Start, a), pb = Min(p.End, b); if (pb > pa) d.Pauses.Add(new Period { Start = pa, End = pb }); }
                }
                foreach (Outage o in outages)
                {
                    DateTime oa = Max(o.Start, aSession), ob = Min(o.End, bSession);
                    if (ob > oa) selectedOutages.Add(new Outage { Start = oa, End = ob, Count = o.Count, Machine = s.MachineName + " [" + s.MachineId + "]", Duration = Effective(oa, ob, s.Pauses) });
                }
            }
            if (sourceFiles == 0) throw new InvalidOperationException(L.T("指定日期範圍內沒有監控資料。", "No monitoring data exists in the selected date range."));
            double timeOutagePercent = effectiveTotal.TotalSeconds <= 0 ? 0 : 100.0 * outageTotal.TotalSeconds / effectiveTotal.TotalSeconds;
            double availability = checks == 0 ? 0 : 100.0 * (checks - offline) / checks;
            TimeSpan longestOutage = TimeSpan.Zero, shortestOutage = TimeSpan.Zero;
            foreach (Outage o in selectedOutages)
            {
                TimeSpan duration = o.Duration;
                if (duration > longestOutage) longestOutage = duration;
                if (shortestOutage == TimeSpan.Zero || duration < shortestOutage) shortestOutage = duration;
            }
            TimeSpan averageOutage = selectedOutages.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(outageTotal.Ticks / selectedOutages.Count);
            long maxLatency = MaxValue(latencies), p95Latency = Percentile95(latencies), jitter = AverageLatencyVariation(latencies);
            selectedOutages.Sort(delegate (Outage a, Outage b) { return b.Start.CompareTo(a.Start); });
            var dailyRows = new List<Daily>(daily.Values);
            dailyRows.Sort(delegate (Daily a, Daily b)
            {
                int byDay = b.Day.CompareTo(a.Day);
                return byDay != 0 ? byDay : String.Compare(a.MachineName, b.MachineName, StringComparison.OrdinalIgnoreCase);
            });
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='" + L.HtmlLanguage + "'><head><meta charset='utf-8'><title>" + H(L.T("對外網路連線能力累積監控報表", "NetCheckMonitor Cumulative Network Monitoring Report")) + "</title><style>@page{size:A4 landscape;margin:10mm}*{-webkit-print-color-adjust:exact;print-color-adjust:exact;box-sizing:border-box}body{font-family:'Microsoft JhengHei UI','Segoe UI',sans-serif;color:#17202a;font-size:11px;margin:0}h1{font-size:24px;margin:0 0 5px}h2{font-size:16px;margin:0 0 10px}.sub{color:#59636e;margin-bottom:12px}.card{border:1px solid #dfe4e8;border-radius:7px;padding:12px;margin:0 0 12px;page-break-inside:avoid}.grid{display:grid;grid-template-columns:repeat(6,1fr);gap:7px}.metric{background:#f3f6f8;border-left:4px solid #2e86c1;padding:8px}.metric b{display:block;font-size:17px;margin-top:3px}.bad{color:#b03a2e}.good{color:#1e8449}table{border-collapse:collapse;width:100%;font-size:10px}th,td{padding:6px;border-bottom:1px solid #e5e7e9;text-align:left;vertical-align:middle}th{background:#f3f6f8}tr{page-break-inside:avoid}.timeline-chart{width:100%}.timeline-chart svg{display:block;width:100%;height:22px;background:#eef1f3}.timeline-axis{display:flex;justify-content:space-between;width:100%;font-size:8px;color:#657}.timeline-row td{padding-top:3px;padding-bottom:12px}.timeline-indent{margin-left:42px;margin-right:12px}.timeline-hit{fill:transparent;cursor:help;pointer-events:all}.date-shade-0 td{background:#f5f9ff}.date-shade-1 td{background:#fff9f0}.date-shade-2 td{background:#f3faf5}.date-shade-3 td{background:#fbf5fa}.event-badge{display:inline-block;border-radius:10px;padding:2px 7px;font-weight:bold;white-space:nowrap}.event-outage{background:#fde8e6;color:#a93226}.event-note{background:#f1e6f7;color:#6c3483}.legend{font-size:9px;color:#657}.foot{font-size:9px;color:#657;margin-top:8px}.detail-report{page-break-inside:auto}.detail-day{margin-top:14px}.detail-day h3{font-size:13px;margin:0;padding:7px 9px;background:#eaf2f8;border-left:4px solid #2e86c1}.detail-status{font-weight:bold;white-space:nowrap}.detail-target{word-break:break-all}@media print{.detail-day{page-break-before:always}.detail-day:first-of-type{page-break-before:auto}}</style></head><body>");
            if (dailyLinks != null) sb.Append("<!--NETCHECK_DAILY_DETAIL_LINKS_V1-->");
            sb.Append("<h1>" + H(L.T("對外網路連線能力累積監控報表", "NetCheckMonitor Cumulative Network Monitoring Report")) + "</h1><div class='sub'>" + H(L.T("資料範圍：", "Date range: ")) + start.ToString("yyyy/MM/dd") + " - " + endExclusive.AddDays(-1).ToString("yyyy/MM/dd") + L.T("｜產生時間：", " | Generated: ") + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + L.T("｜來源檔案：", " | Source files: ") + sourceFiles + "</div>");
            sb.Append("<div class='card grid'>");
            Metric(sb, L.T("電腦數", "Computers"), machines.Count.ToString(), ""); Metric(sb, L.T("有效監控", "Effective Monitoring"), Dur(effectiveTotal), ""); Metric(sb, L.T("估計斷線", "Estimated Outage"), Dur(outageTotal), outageTotal > TimeSpan.Zero ? "bad" : "good"); Metric(sb, L.T("時間斷線率", "Time Outage Rate"), timeOutagePercent.ToString("0.00") + "%", timeOutagePercent > 0 ? "bad" : "good"); Metric(sb, L.T("檢查連線率", "Check Availability"), availability.ToString("0.00") + "%", availability >= 99 ? "good" : "bad"); Metric(sb, L.T("平均延遲", "Average Latency"), (latencyCount == 0 ? 0 : latencyTotal / latencyCount) + " ms", "");
            sb.Append("</div><div class='card grid'>");
            Metric(sb, L.T("斷線事件", "Outage Events"), selectedOutages.Count.ToString(), selectedOutages.Count > 0 ? "bad" : "good"); Metric(sb, L.T("最長斷線", "Longest Outage"), Dur(longestOutage), longestOutage > TimeSpan.Zero ? "bad" : "good"); Metric(sb, L.T("平均斷線", "Average Outage"), Dur(averageOutage), averageOutage > TimeSpan.Zero ? "bad" : "good"); Metric(sb, L.T("最短斷線", "Shortest Outage"), Dur(shortestOutage), shortestOutage > TimeSpan.Zero ? "bad" : "good"); Metric(sb, L.T("第 95 百分位延遲", "95th Percentile Latency"), p95Latency + " ms", ""); Metric(sb, L.T("最高延遲／平均變動", "Max / Avg Variation"), maxLatency + " / " + jitter + " ms", "");
            sb.Append("</div><div class='card'><h2>" + H(L.T("測試電腦與網路介面", "Test Computers and Network Interfaces")) + "</h2><table><tr><th>" + H(L.T("電腦名稱", "Computer Name")) + "</th><th>" + H(L.T("8 碼識別碼", "8-character ID")) + "</th><th>" + H(L.T("目前網卡", "Current Network Adapter")) + "</th><th>" + H(L.T("連線類型", "Connection Type")) + "</th><th>" + H(L.T("Wi-Fi 訊號", "Wi-Fi Signal")) + "</th></tr>");
            foreach (var m in machines)
            {
                Session s = m.Value;
                var network = new NetworkSnapshot { AdapterName = s.AdapterName, AdapterDescription = s.AdapterDescription, TypeCode = s.ConnectionType, WifiSignal = s.WifiSignal };
                sb.Append("<tr><td>" + H(s.MachineName) + "</td><td>" + H(m.Key) + "</td><td>" + H(network.AdapterDisplay) + "</td><td>" + H(network.TypeDisplay) + "</td><td>" + H(network.SignalDisplay) + "</td></tr>");
            }
            sb.Append("</table></div>");
            bool showDailyLinks = dailyLinks != null;
            sb.Append("<div class='card'><h2>" + H(L.T("每日斷線統計", "Daily Outage Statistics")) + "</h2><div class='legend'>" + H(L.T("綠色＝正常　紅色＝確認斷線　橙色＝疑似斷線　紫色＝事件註記　灰色＝暫停或程式中斷", "Green = online, red = confirmed outage, orange = suspected outage, purple = event note, gray = paused or interrupted")) + "</div><table><thead><tr><th>" + H(L.T("電腦", "Computer")) + "</th><th>" + H(L.T("日期", "Date")) + "</th><th>" + H(L.T("有效監控", "Effective Monitoring")) + "</th><th>" + H(L.T("估計斷線", "Estimated Outage")) + "</th><th>" + H(L.T("斷線百分比", "Outage Percentage")) + "</th><th>" + H(L.T("事件／最長", "Events / Longest")) + "</th><th>" + H(L.T("檢查／確認失敗", "Checks / Confirmed Failures")) + "</th>" + (showDailyLinks ? "<th>" + H(L.T("詳細資料", "Details")) + "</th>" : "") + "</tr></thead><tbody>");
            foreach (Daily d in dailyRows)
            {
                double pct = d.Effective.TotalSeconds <= 0 ? 0 : 100.0 * d.Outage.TotalSeconds / d.Effective.TotalSeconds; string cls = pct > 0 ? "bad" : "good";
                string detailLink = "";
                string dailyFile;
                if (showDailyLinks && dailyLinks.TryGetValue(d.Day.ToString("yyyyMMdd"), out dailyFile)) detailLink = "<a href='" + H(Uri.EscapeDataString(dailyFile)) + "'>" + H(L.T("查看當日詳細資料", "View daily details")) + "</a>";
                sb.Append("<tr class='daily-text-row'><td>" + H(d.MachineName + " [" + d.MachineId + "]") + "</td><td>" + d.Day.ToString("yyyy/MM/dd") + "</td><td>" + H(Dur(d.Effective)) + "</td><td class='" + cls + "'>" + H(Dur(d.Outage)) + "</td><td class='" + cls + "'>" + pct.ToString("0.00") + "%</td><td>" + d.OutageEvents + " / " + H(Dur(d.LongestOutage)) + "</td><td>" + d.Checks + " / " + d.Offline + "</td>" + (showDailyLinks ? "<td>" + detailLink + "</td>" : "") + "</tr>");
                sb.Append("<tr class='timeline-row'><td colspan='" + (showDailyLinks ? "8" : "7") + "'><div class='timeline-indent'>" + Timeline(d) + "</div></td></tr>");
            }
            sb.Append("</tbody></table></div>");
            AppendOutageAndNoteTable(sb, selectedOutages, sessions, start, endExclusive);
            AppendDiagnosticTable(sb, sessions, start, endExclusive);
            if (includeDetailedRecords) AppendDetailedTestRecords(sb, dailyRows);
            sb.Append("<div class='foot'>" + H(L.T("首次失敗會在 5 秒後快速複查，連續失敗才確認斷線。暫停、程式中斷、電腦關機、程式未執行，以及沒有任何檢查紀錄的日期或工作階段，都不列入有效監控時間及斷線百分比。進階診斷的開關不影響斷線判定與統計；關閉期間的失敗會標示為未執行進階診斷。", "The first failure triggers a fast retry after 5 seconds, and only consecutive failures confirm an outage. Paused, interrupted, powered-off, app-not-running, and no-check dates or sessions are excluded from effective monitoring time and outage percentage. Enabling or disabling advanced diagnostics does not change outage detection or statistics; failures recorded while disabled are marked as not diagnosed.")) + "</div></body></html>");
            return sb.ToString();
        }

        private static void AppendOutageAndNoteTable(StringBuilder sb, List<Outage> outages, List<Session> sessions, DateTime start, DateTime endExclusive)
        {
            var events = new List<ReportEvent>();
            foreach (Outage outage in outages) events.Add(new ReportEvent { Time = outage.Start, End = outage.End, Machine = outage.Machine, Duration = outage.Duration, Count = outage.Count });
            foreach (Session session in sessions)
                foreach (EventNote note in session.EventNotes)
                    if (note.Time >= start && note.Time < endExclusive) events.Add(new ReportEvent { IsNote = true, Time = note.Time, Machine = session.MachineName + " [" + session.MachineId + "]", Text = note.Text });
            events.Sort(delegate (ReportEvent a, ReportEvent b) { return b.Time.CompareTo(a.Time); });
            sb.Append("<div class='card'><h2>" + H(L.T("斷線事件與事件註記", "Outage Events and Event Notes")) + "</h2>");
            if (events.Count == 0) sb.Append("<p class='good'>" + H(L.T("選取範圍內沒有斷線事件或手動註記。", "No outage events or manual notes were recorded in the selected range.")) + "</p>");
            else
            {
                sb.Append("<table><tr><th>" + H(L.T("類型", "Type")) + "</th><th>" + H(L.T("電腦", "Computer")) + "</th><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("恢復 / 結束", "Recovered / Ended")) + "</th><th>" + H(L.T("持續時間", "Duration")) + "</th><th>" + H(L.T("內容 / 檢查", "Details / Checks")) + "</th></tr>");
                DateTime shadeDate = DateTime.MinValue;
                int shade = -1;
                foreach (ReportEvent item in events)
                {
                    if (item.Time.Date != shadeDate) { shadeDate = item.Time.Date; shade = (shade + 1) % 4; }
                    string badge = item.IsNote ? "<span class='event-badge event-note'>" + H(L.T("註記", "Note")) + "</span>" : "<span class='event-badge event-outage'>" + H(L.T("斷線", "Outage")) + "</span>";
                    string end = item.IsNote ? "—" : item.End.ToString("yyyy/MM/dd HH:mm:ss");
                    string duration = item.IsNote ? "—" : Dur(item.Duration);
                    string detail = item.IsNote ? item.Text : L.T("失敗檢查 ", "Failed checks: ") + item.Count;
                    sb.Append("<tr class='date-shade-" + shade + "'><td>" + badge + "</td><td>" + H(item.Machine) + "</td><td>" + item.Time.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + H(end) + "</td><td>" + H(duration) + "</td><td>" + H(detail) + "</td></tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }

        private static void AppendDiagnosticTable(StringBuilder sb, List<Session> sessions, DateTime start, DateTime endExclusive)
        {
            sb.Append("<div class='card'><h2>" + H(L.T("進階分層連線診斷", "Advanced Layered Connectivity Diagnostics")) + "</h2>");
            var failures = new List<KeyValuePair<string, Record>>();
            foreach (Session session in sessions)
                foreach (Record record in session.Records)
                    if (!record.Online && record.Time >= start && record.Time < endExclusive) failures.Add(new KeyValuePair<string, Record>(session.MachineName + " [" + session.MachineId + "]", record));
            failures.Sort(delegate (KeyValuePair<string, Record> a, KeyValuePair<string, Record> b) { return b.Value.Time.CompareTo(a.Value.Time); });
            if (failures.Count == 0) sb.Append("<p class='good'>" + H(L.T("選取範圍內沒有失敗檢查需要診斷。", "No failed checks in the selected range required diagnostics.")) + "</p>");
            else
            {
                sb.Append("<table><tr><th>" + H(L.T("電腦", "Computer")) + "</th><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("診斷標示", "Diagnostic Finding")) + "</th><th>" + H(L.T("分層證據", "Layer Evidence")) + "</th></tr>");
                DateTime shadeDate = DateTime.MinValue;
                int shade = -1;
                foreach (KeyValuePair<string, Record> item in failures)
                {
                    Record record = item.Value;
                    if (record.Time.Date != shadeDate) { shadeDate = record.Time.Date; shade = (shade + 1) % 4; }
                    string finding = AdvancedDiagnosticResult.FindingsFromLog(record.Detail);
                    if (String.IsNullOrEmpty(finding)) finding = L.T("未執行進階診斷", "Advanced diagnostics not performed");
                    sb.Append("<tr class='date-shade-" + shade + "'><td>" + H(item.Key) + "</td><td>" + record.Time.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + H(finding) + "</td><td>" + H(AdvancedDiagnosticResult.EvidenceFromLog(record.Detail)) + "</td></tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }

        private static void AppendDetailedTestRecords(StringBuilder sb, List<Daily> dailyRows)
        {
            sb.Append("<div class='card detail-report'><h2>" + H(L.T("每日完整測試記錄", "Complete Daily Test Records")) + "</h2><div class='legend'>" + H(L.T("以下依日期列出每一次實際執行的連線檢查；日期與時間均為最新在上。暫停或沒有執行檢查的時段不會產生資料列。", "Every connectivity check that actually ran is listed below by date, with newest dates and times first. Paused periods and periods without a check do not create rows.")) + "</div>");
            foreach (Daily daily in dailyRows)
            {
                var records = new List<Record>(daily.Records);
                records.Sort(delegate (Record a, Record b) { return b.Time.CompareTo(a.Time); });
                if (records.Count == 0) continue;
                sb.Append("<section class='detail-day'><h3>" + daily.Day.ToString("yyyy/MM/dd") + "　" + H(daily.MachineName + " [" + daily.MachineId + "]") + L.T("　共 ", " | ") + records.Count + H(L.T(" 筆檢查", " checks")) + "</h3>");
                sb.Append("<table><thead><tr><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("測試結果", "Result")) + "</th><th>" + H(L.T("回應時間", "Latency")) + "</th><th>" + H(L.T("測試目標", "Test Target")) + "</th><th>" + H(L.T("完整測試內容", "Full Test Details")) + "</th></tr></thead><tbody>");
                foreach (Record record in records)
                {
                    string status = String.IsNullOrEmpty(record.Status) ? (record.Online ? "ONLINE" : "OFFLINE") : record.Status;
                    string label = status == "ONLINE" ? L.T("正常", "Online") : (status == "SUSPECTED" ? L.T("疑似斷線／快速複查", "Suspected outage / fast retry") : L.T("確認斷線", "Confirmed outage"));
                    string cls = status == "ONLINE" ? "good" : "bad";
                    string latency = status == "ONLINE" ? record.Latency + " ms" : "—";
                    sb.Append("<tr><td>" + record.Time.ToString("HH:mm:ss") + "</td><td class='detail-status " + cls + "'>" + H(label) + "</td><td>" + H(latency) + "</td><td class='detail-target'>" + H(record.Target) + "</td><td>" + H(record.Detail) + "</td></tr>");
                }
                sb.Append("</tbody></table></section>");
            }
            sb.Append("</div>");
        }

        private static void AppendEventNotes(StringBuilder sb, List<Session> sessions, DateTime start, DateTime endExclusive)
        {
            var notes = new List<KeyValuePair<string, EventNote>>();
            foreach (Session session in sessions)
                foreach (EventNote note in session.EventNotes)
                    if (note.Time >= start && note.Time < endExclusive) notes.Add(new KeyValuePair<string, EventNote>(session.MachineName + " [" + session.MachineId + "]", note));
            notes.Sort(delegate (KeyValuePair<string, EventNote> a, KeyValuePair<string, EventNote> b) { return b.Value.Time.CompareTo(a.Value.Time); });
            sb.Append("<div class='card'><h2>" + H(L.T("事件註記", "Event Notes")) + "</h2>");
            if (notes.Count == 0) sb.Append("<p>" + H(L.T("選取範圍內沒有手動事件註記。", "No manual event notes were recorded in the selected range.")) + "</p>");
            else
            {
                sb.Append("<table><tr><th>" + H(L.T("電腦", "Computer")) + "</th><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("事件或處理內容", "Event or Action")) + "</th></tr>");
                foreach (KeyValuePair<string, EventNote> item in notes) sb.Append("<tr><td>" + H(item.Key) + "</td><td>" + item.Value.Time.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + H(item.Value.Text) + "</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }

        private static List<Outage> BuildOutages(Session s)
        {
            var list = new List<Outage>(); Outage current = null; DateTime suspected = DateTime.MinValue;
            foreach (Record r in s.Records)
            {
                string status = String.IsNullOrEmpty(r.Status) ? (r.Online ? "ONLINE" : "OFFLINE") : r.Status;
                if (status == "SUSPECTED") { if (suspected == DateTime.MinValue) suspected = r.Time; }
                else if (status == "OFFLINE")
                {
                    if (current == null) current = new Outage { Start = suspected == DateTime.MinValue ? r.Time : suspected, End = r.Time, Count = suspected == DateTime.MinValue ? 0 : 1 };
                    current.End = r.Time; current.Count++;
                }
                else if (status == "ONLINE")
                {
                    if (current != null) { current.End = r.Time; list.Add(current); current = null; }
                    suspected = DateTime.MinValue;
                }
            }
            if (current != null) { current.End = s.End; list.Add(current); }
            return list;
        }

        private static string Timeline(Daily d)
        {
            var sb = new StringBuilder("<div class='timeline-chart'><svg viewBox='0 0 1000 18' preserveAspectRatio='none'>");
            DateTime dayEnd = d.Day.AddDays(1);
            foreach (Record record in d.Records)
            {
                double x = Math.Max(0, Math.Min(998.4, (dayEnd - record.Time).TotalSeconds / 86400.0 * 1000.0 - 1.6));
                double hitX = Math.Max(0, Math.Min(990, x - 4.2));
                string status = String.IsNullOrEmpty(record.Status) ? (record.Online ? "ONLINE" : "OFFLINE") : record.Status;
                string color = status == "ONLINE" ? "#28a745" : (status == "SUSPECTED" ? "#f39c12" : "#dc3545");
                string label = status == "ONLINE" ? L.T("正常", "Online") : (status == "SUSPECTED" ? L.T("疑似斷線", "Suspected outage") : L.T("確認斷線", "Confirmed outage"));
                string tooltip = record.Time.ToString("yyyy/MM/dd HH:mm:ss") + "｜" + label;
                if (!String.IsNullOrWhiteSpace(record.Detail)) tooltip += "｜" + record.Detail;
                sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' width='1.6' height='18' fill='" + color + "' pointer-events='none'/>");
                sb.Append("<rect class='timeline-hit' x='" + hitX.ToString("0.0", CultureInfo.InvariantCulture) + "' width='10' height='18'><title>" + H(tooltip) + "</title></rect>");
            }
            foreach (Period p in d.Pauses) { double w = Math.Max(1, (p.End - p.Start).TotalSeconds / 86400.0 * 1000.0), x = Math.Max(0, Math.Min(1000 - w, (dayEnd - p.End).TotalSeconds / 86400.0 * 1000.0)); sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' width='" + w.ToString("0.0", CultureInfo.InvariantCulture) + "' height='18' fill='#9aa0a6'/>"); }
            foreach (EventNote note in d.EventNotes)
            {
                double x = Math.Max(0, Math.Min(996, (dayEnd - note.Time).TotalSeconds / 86400.0 * 1000.0 - 4));
                double hitX = Math.Max(0, Math.Min(988, x - 4));
                string tooltip = note.Time.ToString("yyyy/MM/dd HH:mm:ss") + L.T("｜事件註記｜", " | Event note | ") + note.Text;
                sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' width='4' height='18' fill='#8e44ad' pointer-events='none'/>");
                sb.Append("<rect class='timeline-hit' x='" + hitX.ToString("0.0", CultureInfo.InvariantCulture) + "' width='12' height='18'><title>" + H(tooltip) + "</title></rect>");
            }
            return sb.Append("</svg><div class='timeline-axis'><span>24:00</span><span>18:00</span><span>12:00</span><span>06:00</span><span>00:00</span></div></div>").ToString();
        }

        private static TimeSpan Effective(DateTime start, DateTime end, List<Period> pauses)
        {
            TimeSpan value = end > start ? end - start : TimeSpan.Zero;
            foreach (Period p in pauses) { DateTime a = Max(start, p.Start), b = Min(end, p.End); if (b > a) value -= b - a; }
            return value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        private static List<string> GetCsvFiles()
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in StorageDirectories())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "NetCheck_*.csv")) if (!found.ContainsKey(Path.GetFileName(file))) found[Path.GetFileName(file)] = file;
            }
            return new List<string>(found.Values);
        }

        private static List<string> GetManagedFiles()
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in StorageDirectories())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "NetCheck_*.*"))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".csv" || ext == ".html" || ext == ".pdf") found[file] = file;
                }
            }
            return new List<string>(found.Values);
        }

        private static List<string> StorageDirectories()
        {
            string overrideRoots = Environment.GetEnvironmentVariable("NETCHECK_DATA_ROOTS");
            if (!String.IsNullOrEmpty(overrideRoots))
            {
                var isolated = new List<string>();
                foreach (string root in overrideRoots.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) AddUnique(isolated, root.Trim());
                return isolated;
            }
            string exe = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var result = new List<string>();
            AddUnique(result, Path.Combine(exe, "NetCheck_Data"));
            AddUnique(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NetCheck_Data"));
            AddUnique(result, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Data"));
            string recovery = Environment.GetEnvironmentVariable("NETCHECK_BACKUP_DIR");
            if (String.IsNullOrEmpty(recovery)) recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Recovery");
            AddUnique(result, recovery);
            return result;
        }

        private static void AddUnique(List<string> list, string path) { foreach (string s in list) if (String.Equals(s, path, StringComparison.OrdinalIgnoreCase)) return; list.Add(path); }
        private static string FindEdge()
        {
            string[] candidates = new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe") };
            foreach (string p in candidates) if (File.Exists(p)) return p;
            return null;
        }

        private static List<string> ParseCsv(string line)
        {
            var fields = new List<string>(); var field = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
                else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Length = 0; }
                else field.Append(c);
            }
            fields.Add(field.ToString()); return fields;
        }

        private static bool HasStatisticalData(List<Session> list) { foreach (Session s in list) if (s.Records.Count > 0) return true; return false; }
        private static DateTime MinStart(List<Session> list) { DateTime v = DateTime.MaxValue; foreach (Session s in list) if (s.Records.Count > 0 && s.Start < v) v = s.Start; return v; }
        private static DateTime MaxEnd(List<Session> list) { DateTime v = DateTime.MinValue; foreach (Session s in list) if (s.Records.Count > 0 && s.End > v) v = s.End; return v; }
        private static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }
        private static DateTime Min(DateTime a, DateTime b) { return a < b ? a : b; }
        private static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }
        private static string Dur(TimeSpan t) { if (L.TraditionalChinese) { if (t.TotalDays >= 1) return ((int)t.TotalDays) + "天 " + t.Hours + "小時 " + t.Minutes + "分"; if (t.TotalHours >= 1) return ((int)t.TotalHours) + "小時 " + t.Minutes + "分"; return ((int)t.TotalMinutes) + "分 " + t.Seconds + "秒"; } if (t.TotalDays >= 1) return ((int)t.TotalDays) + "d " + t.Hours + "h " + t.Minutes + "m"; if (t.TotalHours >= 1) return ((int)t.TotalHours) + "h " + t.Minutes + "m"; return ((int)t.TotalMinutes) + "m " + t.Seconds + "s"; }
        private static long MaxValue(List<long> values) { long max = 0; foreach (long value in values) if (value > max) max = value; return max; }
        private static long Percentile95(List<long> values) { if (values.Count == 0) return 0; var sorted = new List<long>(values); sorted.Sort(); return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * 0.95) - 1)]; }
        private static long AverageLatencyVariation(List<long> values) { if (values.Count < 2) return 0; long total = 0; for (int i = 1; i < values.Count; i++) total += Math.Abs(values[i] - values[i - 1]); return total / (values.Count - 1); }
        private static void Metric(StringBuilder sb, string name, string value, string cls) { sb.Append("<div class='metric'><span>" + H(name) + "</span><b class='" + cls + "'>" + H(value) + "</b></div>"); }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static string SafeName(string value, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in value ?? "PC") { if (Char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c); if (sb.Length >= max) break; }
            return sb.Length == 0 ? "PC" : sb.ToString();
        }
    }
}
