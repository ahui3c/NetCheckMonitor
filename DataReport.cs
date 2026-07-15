using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
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
            statusLabel.Text = L.T("PDF 會包含每日斷線時間、斷線百分比與時間軸。", "The PDF includes daily outage time, outage percentage, and timelines.");
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
        private sealed class Record { public DateTime Time; public bool Online; public long Latency; }
        private sealed class Period { public DateTime Start; public DateTime End; }
        private sealed class Outage { public DateTime Start; public DateTime End; public int Count; public string Machine; }
        private sealed class Session
        {
            public string MachineName = Environment.MachineName;
            public string MachineId = "LEGACY";
            public DateTime Start = DateTime.MaxValue;
            public DateTime End = DateTime.MinValue;
            public bool Stopped;
            public readonly List<Record> Records = new List<Record>();
            public readonly List<Period> Pauses = new List<Period>();
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
            public readonly List<Record> Records = new List<Record>();
            public readonly List<Period> Pauses = new List<Period>();
        }

        public static void ExportPdf(string outputPath, bool allDates, DateTime from, DateTime to)
        {
            List<Session> sessions = LoadSessions();
            if (sessions.Count == 0) throw new InvalidOperationException(L.T("找不到任何 NetCheck CSV 監控資料。", "No NetCheck CSV monitoring data was found."));
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
                        else if (status == "STARTED") session.Start = time;
                        else if (status == "STOPPED") { session.End = time; session.Stopped = true; }
                        else if (status == "PAUSED") { pauseOpen = true; pauseStart = time; }
                        else if (status == "RESUMED" && pauseOpen) { session.Pauses.Add(new Period { Start = pauseStart, End = time }); pauseOpen = false; }
                    }
                    else if (type == "CHECK")
                    {
                        long latency;
                        Int64.TryParse(f[3], out latency);
                        session.Records.Add(new Record { Time = time, Online = status == "ONLINE", Latency = latency });
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
            return session;
        }

        private static void ParseComputer(string detail, Session session)
        {
            int open = detail.IndexOf(" [", StringComparison.Ordinal);
            int close = open >= 0 ? detail.IndexOf(']', open + 2) : -1;
            if (open > 0) session.MachineName = detail.Substring(0, open).Trim();
            if (open >= 0 && close > open) session.MachineId = detail.Substring(open + 2, close - open - 2).Trim();
        }

        private static string BuildHtml(List<Session> sessions, DateTime start, DateTime endExclusive, bool allDates)
        {
            var daily = new SortedDictionary<string, Daily>();
            var selectedOutages = new List<Outage>();
            var machines = new SortedDictionary<string, string>();
            int checks = 0, offline = 0, sourceFiles = 0;
            long latencyTotal = 0; int latencyCount = 0;
            TimeSpan effectiveTotal = TimeSpan.Zero, outageTotal = TimeSpan.Zero;

            foreach (Session s in sessions)
            {
                DateTime aSession = Max(s.Start, start), bSession = Min(s.End, endExclusive);
                if (bSession <= aSession) continue;
                sourceFiles++;
                machines[s.MachineId] = s.MachineName;
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
                        if (ob > oa) { TimeSpan lost = Effective(oa, ob, s.Pauses); d.Outage += lost; outageTotal += lost; }
                    }
                    foreach (Record r in s.Records) if (r.Time >= a && r.Time < b) { d.Records.Add(r); d.Checks++; checks++; if (!r.Online) { d.Offline++; offline++; } else { latencyTotal += r.Latency; latencyCount++; } }
                    foreach (Period p in s.Pauses) { DateTime pa = Max(p.Start, a), pb = Min(p.End, b); if (pb > pa) d.Pauses.Add(new Period { Start = pa, End = pb }); }
                }
                foreach (Outage o in outages)
                {
                    DateTime oa = Max(o.Start, aSession), ob = Min(o.End, bSession);
                    if (ob > oa) selectedOutages.Add(new Outage { Start = oa, End = ob, Count = o.Count, Machine = s.MachineName + " [" + s.MachineId + "]" });
                }
            }
            if (sourceFiles == 0) throw new InvalidOperationException(L.T("指定日期範圍內沒有監控資料。", "No monitoring data exists in the selected date range."));
            double timeOutagePercent = effectiveTotal.TotalSeconds <= 0 ? 0 : 100.0 * outageTotal.TotalSeconds / effectiveTotal.TotalSeconds;
            double availability = checks == 0 ? 0 : 100.0 * (checks - offline) / checks;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='" + L.HtmlLanguage + "'><head><meta charset='utf-8'><title>" + H(L.T("NetCheckMonitor 網路監控 PDF 報表", "NetCheckMonitor Network Monitoring PDF Report")) + "</title><style>@page{size:A4 landscape;margin:10mm}*{-webkit-print-color-adjust:exact;print-color-adjust:exact;box-sizing:border-box}body{font-family:'Microsoft JhengHei UI','Segoe UI',sans-serif;color:#17202a;font-size:11px;margin:0}h1{font-size:24px;margin:0 0 5px}h2{font-size:16px;margin:0 0 10px}.sub{color:#59636e;margin-bottom:12px}.card{border:1px solid #dfe4e8;border-radius:7px;padding:12px;margin:0 0 12px;page-break-inside:avoid}.grid{display:grid;grid-template-columns:repeat(6,1fr);gap:7px}.metric{background:#f3f6f8;border-left:4px solid #2e86c1;padding:8px}.metric b{display:block;font-size:17px;margin-top:3px}.bad{color:#b03a2e}.good{color:#1e8449}table{border-collapse:collapse;width:100%;font-size:10px}th,td{padding:6px;border-bottom:1px solid #e5e7e9;text-align:left;vertical-align:middle}th{background:#f3f6f8}tr{page-break-inside:avoid}svg{display:block;width:280px;height:18px;background:#eef1f3}.legend{font-size:9px;color:#657}.foot{font-size:9px;color:#657;margin-top:8px}</style></head><body>");
            sb.Append("<h1>" + H(L.T("NetCheckMonitor 網路監控 PDF 報表", "NetCheckMonitor Network Monitoring PDF Report")) + "</h1><div class='sub'>" + H(L.T("資料範圍：", "Date range: ")) + start.ToString("yyyy/MM/dd") + " - " + endExclusive.AddDays(-1).ToString("yyyy/MM/dd") + L.T("｜產生時間：", " | Generated: ") + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + L.T("｜來源檔案：", " | Source files: ") + sourceFiles + "</div>");
            sb.Append("<div class='card grid'>");
            Metric(sb, L.T("電腦數", "Computers"), machines.Count.ToString(), ""); Metric(sb, L.T("有效監控", "Effective Monitoring"), Dur(effectiveTotal), ""); Metric(sb, L.T("估計斷線", "Estimated Outage"), Dur(outageTotal), outageTotal > TimeSpan.Zero ? "bad" : "good"); Metric(sb, L.T("時間斷線率", "Time Outage Rate"), timeOutagePercent.ToString("0.00") + "%", timeOutagePercent > 0 ? "bad" : "good"); Metric(sb, L.T("檢查連線率", "Check Availability"), availability.ToString("0.00") + "%", availability >= 99 ? "good" : "bad"); Metric(sb, L.T("平均延遲", "Average Latency"), (latencyCount == 0 ? 0 : latencyTotal / latencyCount) + " ms", "");
            sb.Append("</div><div class='card'><h2>" + H(L.T("測試電腦", "Test Computers")) + "</h2><table><tr><th>" + H(L.T("電腦名稱", "Computer Name")) + "</th><th>" + H(L.T("8 碼識別碼", "8-character ID")) + "</th></tr>");
            foreach (var m in machines) sb.Append("<tr><td>" + H(m.Value) + "</td><td>" + H(m.Key) + "</td></tr>");
            sb.Append("</table></div><div class='card'><h2>" + H(L.T("每日斷線統計", "Daily Outage Statistics")) + "</h2><div class='legend'>" + H(L.T("綠色＝正常　紅色＝斷線　灰色＝暫停；每日斷線百分比＝估計斷線時間 ÷ 有效監控時間", "Green = online, red = offline, gray = paused; daily outage percentage = estimated outage time / effective monitoring time")) + "</div><table><thead><tr><th>" + H(L.T("電腦", "Computer")) + "</th><th>" + H(L.T("日期", "Date")) + "</th><th>" + H(L.T("有效監控", "Effective Monitoring")) + "</th><th>" + H(L.T("估計斷線", "Estimated Outage")) + "</th><th>" + H(L.T("斷線百分比", "Outage Percentage")) + "</th><th>" + H(L.T("檢查 / 失敗", "Checks / Failures")) + "</th><th>" + H(L.T("24 小時時間軸", "24-hour Timeline")) + "</th></tr></thead><tbody>");
            foreach (var pair in daily)
            {
                Daily d = pair.Value; double pct = d.Effective.TotalSeconds <= 0 ? 0 : 100.0 * d.Outage.TotalSeconds / d.Effective.TotalSeconds; string cls = pct > 0 ? "bad" : "good";
                sb.Append("<tr><td>" + H(d.MachineName + " [" + d.MachineId + "]") + "</td><td>" + d.Day.ToString("yyyy/MM/dd") + "</td><td>" + H(Dur(d.Effective)) + "</td><td class='" + cls + "'>" + H(Dur(d.Outage)) + "</td><td class='" + cls + "'>" + pct.ToString("0.00") + "%</td><td>" + d.Checks + " / " + d.Offline + "</td><td>" + Timeline(d) + "</td></tr>");
            }
            sb.Append("</tbody></table></div><div class='card'><h2>" + H(L.T("斷線事件", "Outage Events")) + "</h2>");
            if (selectedOutages.Count == 0) sb.Append("<p class='good'>" + H(L.T("選取範圍內沒有偵測到斷線。", "No outage was detected in the selected range.")) + "</p>");
            else
            {
                sb.Append("<table><tr><th>" + H(L.T("電腦", "Computer")) + "</th><th>" + H(L.T("開始", "Started")) + "</th><th>" + H(L.T("恢復 / 最後偵測", "Recovered / Last Detected")) + "</th><th>" + H(L.T("估計區間", "Estimated Duration")) + "</th><th>" + H(L.T("失敗檢查", "Failed Checks")) + "</th></tr>");
                foreach (Outage o in selectedOutages) sb.Append("<tr><td>" + H(o.Machine) + "</td><td>" + o.Start.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + o.End.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + H(Dur(o.End - o.Start)) + "</td><td>" + o.Count + "</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div><div class='foot'>" + H(L.T("斷線時間是由失敗檢查到下一次成功檢查的估計區間，精度受檢查間隔影響。暫停區段不列入每日有效監控時間及斷線百分比。", "Outage time is estimated from a failed check to the next successful check; precision depends on the check interval. Paused periods are excluded from daily effective monitoring time and outage percentage.")) + "</div></body></html>");
            return sb.ToString();
        }

        private static List<Outage> BuildOutages(Session s)
        {
            var list = new List<Outage>(); Outage current = null;
            foreach (Record r in s.Records)
            {
                if (!r.Online) { if (current == null) current = new Outage { Start = r.Time, End = r.Time, Count = 0 }; current.End = r.Time; current.Count++; }
                else if (current != null) { current.End = r.Time; list.Add(current); current = null; }
            }
            if (current != null) { current.End = s.End; list.Add(current); }
            return list;
        }

        private static string Timeline(Daily d)
        {
            var sb = new StringBuilder("<svg viewBox='0 0 1000 18' preserveAspectRatio='none'>");
            foreach (Record r in d.Records) { double x = (r.Time - d.Day).TotalSeconds / 86400.0 * 1000.0; sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' width='1.6' height='18' fill='" + (r.Online ? "#28a745" : "#dc3545") + "'/>"); }
            foreach (Period p in d.Pauses) { double x = (p.Start - d.Day).TotalSeconds / 86400.0 * 1000.0, w = Math.Max(1, (p.End - p.Start).TotalSeconds / 86400.0 * 1000.0); sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' width='" + w.ToString("0.0", CultureInfo.InvariantCulture) + "' height='18' fill='#9aa0a6'/>"); }
            return sb.Append("</svg>").ToString();
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

        private static DateTime MinStart(List<Session> list) { DateTime v = DateTime.MaxValue; foreach (Session s in list) if (s.Start < v) v = s.Start; return v; }
        private static DateTime MaxEnd(List<Session> list) { DateTime v = DateTime.MinValue; foreach (Session s in list) if (s.End > v) v = s.End; return v; }
        private static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }
        private static DateTime Min(DateTime a, DateTime b) { return a < b ? a : b; }
        private static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }
        private static string Dur(TimeSpan t) { if (L.TraditionalChinese) { if (t.TotalDays >= 1) return ((int)t.TotalDays) + "天 " + t.Hours + "小時 " + t.Minutes + "分"; if (t.TotalHours >= 1) return ((int)t.TotalHours) + "小時 " + t.Minutes + "分"; return ((int)t.TotalMinutes) + "分 " + t.Seconds + "秒"; } if (t.TotalDays >= 1) return ((int)t.TotalDays) + "d " + t.Hours + "h " + t.Minutes + "m"; if (t.TotalHours >= 1) return ((int)t.TotalHours) + "h " + t.Minutes + "m"; return ((int)t.TotalMinutes) + "m " + t.Seconds + "s"; }
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
