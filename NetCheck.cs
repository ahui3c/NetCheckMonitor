using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("NetCheckMonitor")]
[assembly: AssemblyProduct("NetCheckMonitor")]
[assembly: AssemblyDescription("Internet connection monitoring and outage reporting")]
[assembly: AssemblyCompany("廖阿輝")]
[assembly: AssemblyVersion("0.9.1.0")]
[assembly: AssemblyFileVersion("0.9.1.0")]

namespace NetCheck
{
    internal sealed class CheckRecord
    {
        public DateTime Time;
        public bool Online;
        public long LatencyMs;
        public string Target;
        public string Detail;
    }

    internal sealed class TimePeriod
    {
        public DateTime Start;
        public DateTime End;
    }

    internal sealed class MainForm : Form
    {
        private readonly Label stateLabel = new Label();
        private readonly Label lastLabel = new Label();
        private readonly Label statsLabel = new Label();
        private readonly Button startButton = new Button();
        private readonly Button pauseButton = new Button();
        private readonly Button stopButton = new Button();
        private readonly Button reportButton = new Button();
        private readonly Button dataButton = new Button();
        private readonly Button clearDataButton = new Button();
        private readonly Button exitButton = new Button();
        private readonly Button cloudButton = new Button();
        private readonly Button aboutButton = new Button();
        private readonly NumericUpDown intervalBox = new NumericUpDown();
        private readonly ListView recentList = new ListView();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private System.Threading.Timer timer;
        private StreamWriter writer;
        private StreamWriter backupWriter;
        private string csvPath;
        private string backupCsvPath;
        private string reportPath;
        private string machineName;
        private string machineId;
        private string machineIdSource;
        private string sessionFileStem;
        private DateTime sessionStart;
        private DateTime pauseStart;
        private bool running;
        private bool paused;
        private bool allowExit;
        private string exitSaveError;
        private int checking;
        private DateTime lastAutoReport = DateTime.MinValue;
        private string logWarning;
        private CloudBackupManager cloudManager;
        private readonly List<CheckRecord> records = new List<CheckRecord>();
        private readonly List<TimePeriod> pauses = new List<TimePeriod>();

        private static readonly string[] TestUrls = new string[] {
            "https://www.msftconnecttest.com/connecttest.txt",
            "https://connectivitycheck.gstatic.com/generate_204",
            "https://cp.cloudflare.com/generate_204"
        };

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        public MainForm()
        {
            Text = L.T("NetCheckMonitor 網路連線監控", "NetCheckMonitor Network Monitor");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(780, 530);
            MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = L.T("網路連線監控", "Network Connection Monitor"), Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Location = new Point(22, 18) };
            stateLabel.Text = L.T("尚未開始", "Not started");
            stateLabel.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
            stateLabel.ForeColor = Color.DimGray;
            stateLabel.AutoSize = true;
            stateLabel.Location = new Point(25, 62);

            var intervalLabel = new Label { Text = L.T("檢查間隔（秒）", "Interval (seconds)"), AutoSize = true, Location = new Point(535, 25) };
            intervalBox.Minimum = 10;
            intervalBox.Maximum = 3600;
            intervalBox.Value = 60;
            intervalBox.Location = new Point(660, 21);
            intervalBox.Size = new Size(90, 25);

            startButton.Text = L.T("開始監控", "Start");
            pauseButton.Text = L.T("暫停", "Pause");
            stopButton.Text = L.T("結束並產生報表", "Stop and Create Report");
            reportButton.Text = L.T("產生即時報表", "Create Live Report");
            dataButton.Text = L.T("下載報表 PDF 文件", "Download PDF Report");
            clearDataButton.Text = L.T("清除儲存資料", "Clear Saved Data");
            exitButton.Text = L.T("關閉程式", "Exit");
            cloudButton.Text = L.T("Google Drive 備份設定", "Google Drive Backup");
            aboutButton.Text = L.T("關於", "About");
            startButton.SetBounds(25, 112, 125, 38);
            pauseButton.SetBounds(160, 112, 105, 38);
            stopButton.SetBounds(275, 112, 175, 38);
            reportButton.SetBounds(460, 112, 145, 38);
            dataButton.SetBounds(615, 112, 140, 38);
            clearDataButton.SetBounds(520, 502, 120, 24);
            clearDataButton.Font = new Font(Font.FontFamily, 8.5F);
            clearDataButton.ForeColor = Color.Firebrick;
            clearDataButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            exitButton.SetBounds(650, 502, 105, 24);
            exitButton.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);
            exitButton.ForeColor = Color.Firebrick;
            exitButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cloudButton.SetBounds(25, 502, 165, 24);
            cloudButton.Font = new Font(Font.FontFamily, 8.5F);
            cloudButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            aboutButton.SetBounds(200, 502, 70, 24);
            aboutButton.Font = new Font(Font.FontFamily, 8.5F);
            aboutButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            reportButton.Enabled = false;

            lastLabel.Text = L.T("最後檢查：—", "Last check: —");
            lastLabel.AutoSize = true;
            lastLabel.Location = new Point(27, 166);
            statsLabel.Text = L.T("有效檢查 0 次｜正常 0 次｜斷線 0 次｜暫停時間不列入統計", "Checks 0 | Online 0 | Offline 0 | Paused time excluded");
            statsLabel.AutoSize = true;
            statsLabel.Location = new Point(27, 193);

            recentList.Location = new Point(25, 228);
            recentList.Size = new Size(730, 270);
            recentList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            recentList.View = View.Details;
            recentList.FullRowSelect = true;
            recentList.GridLines = true;
            recentList.Columns.Add(L.T("時間", "Time"), 155);
            recentList.Columns.Add(L.T("狀態", "Status"), 90);
            recentList.Columns.Add(L.T("延遲", "Latency"), 90);
            recentList.Columns.Add(L.T("檢測目標 / 說明", "Target / Details"), 365);

            Controls.AddRange(new Control[] { title, stateLabel, intervalLabel, intervalBox, startButton, pauseButton, stopButton, reportButton, dataButton, lastLabel, statsLabel, recentList, clearDataButton, exitButton, cloudButton, aboutButton });

            startButton.Click += delegate { StartMonitoring(); };
            pauseButton.Click += delegate { TogglePause(); };
            stopButton.Click += delegate { StopMonitoring(true); };
            reportButton.Click += delegate { if (running) CreateLiveReport(true); else OpenReport(); };
            dataButton.Click += delegate { ShowDataManager(); };
            clearDataButton.Click += delegate { ClearStoredData(); };
            exitButton.Click += delegate { RequestExit(); };
            cloudButton.Click += delegate { ShowCloudSettings(); };
            aboutButton.Click += delegate { using (var form = new AboutForm()) form.ShowDialog(this); };
            FormClosing += OnFormClosing;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };

            trayIcon.Text = L.T("NetCheckMonitor 網路連線監控", "NetCheckMonitor Network Monitor");
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(L.T("顯示視窗", "Show Window"), null, delegate { ShowFromTray(); });
            trayMenu.Items.Add(L.T("結束程式", "Exit"), null, delegate { ShowFromTray(); RequestExit(); });
            trayIcon.ContextMenuStrip = trayMenu;
            EnsureMachineIdentity();
            cloudManager = new CloudBackupManager(machineName, machineId);
        }

        private void StartMonitoring()
        {
            records.Clear();
            pauses.Clear();
            recentList.Items.Clear();
            reportPath = null;
            reportButton.Text = L.T("產生即時報表", "Create Live Report");
            reportButton.Enabled = true;
            lastAutoReport = DateTime.MinValue;
            logWarning = null;
            sessionStart = DateTime.Now;
            EnsureMachineIdentity();
            sessionFileStem = "NetCheck_" + SafeFilePart(machineName, 16) + "-" + machineId + "_" + sessionStart.ToString("yyyyMMdd_HHmmss");
            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string dataDir = Path.Combine(executableDir, "NetCheck_Data");
            try { Directory.CreateDirectory(dataDir); }
            catch { dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NetCheck_Data"); Directory.CreateDirectory(dataDir); }
            csvPath = Path.Combine(dataDir, sessionFileStem + ".csv");
            try { writer = CreateDurableWriter(csvPath); }
            catch
            {
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Data");
                Directory.CreateDirectory(dataDir);
                csvPath = Path.Combine(dataDir, sessionFileStem + ".csv");
                writer = CreateDurableWriter(csvPath);
            }
            try
            {
                string backupDir = Environment.GetEnvironmentVariable("NETCHECK_BACKUP_DIR");
                if (String.IsNullOrEmpty(backupDir)) backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Recovery");
                Directory.CreateDirectory(backupDir);
                backupCsvPath = Path.Combine(backupDir, Path.GetFileName(csvPath));
                if (!String.Equals(Path.GetFullPath(backupCsvPath), Path.GetFullPath(csvPath), StringComparison.OrdinalIgnoreCase)) backupWriter = CreateDurableWriter(backupCsvPath);
            }
            catch { backupWriter = null; backupCsvPath = null; }
            WriteLogLine("Timestamp,Type,Status,LatencyMs,Target,Detail");
            WriteMarker("COMPUTER", machineName + " [" + machineId + "]" + L.T("；識別方式：", "; identity: ") + machineIdSource);
            WriteMarker("STARTED", L.T("開始監控；檢查間隔 ", "Monitoring started; interval ") + intervalBox.Value + L.T(" 秒", " seconds"));

            running = true;
            paused = false;
            intervalBox.Enabled = false;
            startButton.Enabled = false;
            pauseButton.Enabled = true;
            stopButton.Enabled = true;
            pauseButton.Text = L.T("暫停", "Pause");
            SetAwake(true);
            UpdateState(L.T("準備檢查…", "Preparing check…"), Color.DarkOrange);
            timer = new System.Threading.Timer(delegate { PerformCheck(); }, null, 0, (int)intervalBox.Value * 1000);
        }

        private void EnsureMachineIdentity()
        {
            if (!String.IsNullOrEmpty(machineId)) return;
            machineName = Environment.MachineName;
            machineId = GetMachineId(out machineIdSource);
        }

        private void ShowDataManager()
        {
            EnsureMachineIdentity();
            using (var form = new DataReportForm(machineName, machineId)) form.ShowDialog(this);
        }

        private void ClearStoredData()
        {
            if (running || (cloudManager != null && cloudManager.BackupInProgress))
            {
                MessageBox.Show(L.T("目前正在監控或雲端備份，不能清除資料。請等待工作完成並結束監控後再清除。", "Saved data cannot be cleared while monitoring or a cloud backup is in progress. Wait for the work to finish and stop monitoring first."), L.T("無法清除", "Unable to Clear"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(L.T("這會刪除 NetCheck 管理的所有 CSV、HTML、即時報表與本機備援資料。\n\n自行下載到其他資料夾的 PDF 不會被刪除。此動作無法復原，確定繼續嗎？", "This will delete all CSV, HTML, live-report, and local recovery files managed by NetCheck.\n\nPDF files downloaded to other folders will not be deleted. This cannot be undone. Continue?"), L.T("清除全部儲存資料", "Clear All Saved Data"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int deleted;
            List<string> failures = ArchiveReport.ClearAllData(out deleted);
            if (failures.Count > 0)
            {
                string prefix = deleted == 0 ? L.T("沒有清除任何資料。偵測到仍被使用或無法刪除的檔案。", "No data was cleared. Some files are in use or could not be deleted.") : L.T("已清除 ", "Cleared ") + deleted + L.T(" 個檔案，但下列檔案無法刪除。", " files, but the following files could not be deleted.");
                MessageBox.Show(prefix + L.T("\n請先結束所有 NetCheck 監控程式後再試一次。\n\n", "\nClose all NetCheck monitoring programs and try again.\n\n") + String.Join("\n", failures.ToArray()), L.T("資料清除未完成", "Data Clearing Incomplete"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else MessageBox.Show(L.T("已清除 ", "Cleared ") + deleted + L.T(" 個 NetCheck 儲存檔案。", " NetCheck saved files."), L.T("清除完成", "Clearing Complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowCloudSettings()
        {
            using (var form = new CloudBackupForm(cloudManager)) form.ShowDialog(this);
        }

        private void TogglePause()
        {
            if (!running) return;
            if (!paused)
            {
                paused = true;
                pauseStart = DateTime.Now;
                if (timer != null) timer.Change(Timeout.Infinite, Timeout.Infinite);
                WriteMarker("PAUSED", L.T("監控暫停；此時段不列入統計", "Monitoring paused; this period is excluded from statistics"));
                AddRecent(pauseStart, L.T("暫停", "Paused"), "—", L.T("此時段不列入統計", "This period is excluded from statistics"), Color.Gray);
                pauseButton.Text = L.T("繼續", "Resume");
                UpdateState(L.T("已暫停（不列入統計）", "Paused (excluded from statistics)"), Color.Gray);
            }
            else
            {
                paused = false;
                DateTime now = DateTime.Now;
                pauses.Add(new TimePeriod { Start = pauseStart, End = now });
                WriteMarker("RESUMED", L.T("繼續監控；暫停 ", "Monitoring resumed; paused for ") + FormatDuration(now - pauseStart));
                AddRecent(now, L.T("繼續", "Resumed"), "—", L.T("恢復監控", "Monitoring resumed"), Color.RoyalBlue);
                pauseButton.Text = L.T("暫停", "Pause");
                UpdateState(L.T("準備檢查…", "Preparing check…"), Color.DarkOrange);
                if (timer != null) timer.Change(0, (int)intervalBox.Value * 1000);
            }
        }

        private void PerformCheck()
        {
            if (!running || paused || Interlocked.Exchange(ref checking, 1) == 1) return;
            DateTime at = DateTime.Now;
            bool online = false;
            long latency = 0;
            string target = "";
            string detail = "";
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    detail = L.T("Windows 未偵測到可用的網路介面", "Windows detected no available network interface");
                }
                else
                {
                    var errors = new List<string>();
                    foreach (string url in TestUrls)
                    {
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            var request = (HttpWebRequest)WebRequest.Create(url);
                            request.Method = "GET";
                            request.Timeout = 5000;
                            request.ReadWriteTimeout = 5000;
                            request.UserAgent = "NetCheck/1.0";
                            request.AllowAutoRedirect = true;
                            using (var response = (HttpWebResponse)request.GetResponse())
                            {
                                sw.Stop();
                                int code = (int)response.StatusCode;
                                if (code >= 200 && code < 400)
                                {
                                    online = true;
                                    latency = sw.ElapsedMilliseconds;
                                    target = new Uri(url).Host;
                                    detail = "HTTP " + code;
                                    break;
                                }
                                errors.Add(new Uri(url).Host + ": HTTP " + code);
                            }
                        }
                        catch (Exception ex)
                        {
                            sw.Stop();
                            errors.Add(new Uri(url).Host + ": " + ShortError(ex));
                        }
                    }
                    if (!online) detail = string.Join(L.T("；", "; "), errors.ToArray());
                }
            }
            catch (Exception ex) { detail = ShortError(ex); }
            finally { Interlocked.Exchange(ref checking, 0); }

            if (!running) return;
            var record = new CheckRecord { Time = at, Online = online, LatencyMs = latency, Target = target, Detail = detail };
            lock (records) records.Add(record);
            WriteCheck(record);
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((MethodInvoker)delegate { RenderCheck(record); });
            }
        }

        private static string ShortError(Exception ex)
        {
            if (ex is WebException)
            {
                var web = (WebException)ex;
                if (web.Status == WebExceptionStatus.Timeout) return L.T("逾時", "Timed out");
                if (web.Status == WebExceptionStatus.NameResolutionFailure) return L.T("DNS 解析失敗", "DNS resolution failed");
                if (web.Status == WebExceptionStatus.ConnectFailure) return L.T("無法連線", "Connection failed");
                return web.Status.ToString();
            }
            return ex.Message.Replace("\r", " ").Replace("\n", " ");
        }

        private void RenderCheck(CheckRecord record)
        {
            if (record.Online)
            {
                UpdateState(L.T("網路正常", "Online"), Color.SeaGreen);
                AddRecent(record.Time, L.T("正常", "Online"), record.LatencyMs + " ms", record.Target + L.T("（", " (") + record.Detail + L.T("）", ")"), Color.SeaGreen);
            }
            else
            {
                UpdateState(L.T("偵測到斷線", "Internet outage detected"), Color.Firebrick);
                AddRecent(record.Time, L.T("斷線", "Offline"), "—", record.Detail, Color.Firebrick);
                trayIcon.Visible = true;
                trayIcon.ShowBalloonTip(5000, L.T("NetCheck 偵測到斷線", "NetCheck Detected an Outage"), record.Time.ToString("yyyy/MM/dd HH:mm:ss") + L.T(" 無法連線到外部網路", " could not reach the Internet"), ToolTipIcon.Error);
            }
            lastLabel.Text = L.T("最後檢查：", "Last check: ") + record.Time.ToString("yyyy/MM/dd HH:mm:ss");
            int good = 0, bad = 0;
            lock (records) foreach (var r in records) { if (r.Online) good++; else bad++; }
            statsLabel.Text = L.T("有效檢查 ", "Checks ") + (good + bad) + L.T(" 次｜正常 ", " | Online ") + good + L.T(" 次｜斷線 ", " | Offline ") + bad + L.T(" 次｜暫停時間不列入統計", " | Paused time excluded");
            if (!String.IsNullOrEmpty(logWarning)) statsLabel.Text += L.T("｜警告：", " | Warning: ") + logWarning;
            if ((DateTime.Now - lastAutoReport).TotalMinutes >= 10)
            {
                CreateLiveReport(false);
                lastAutoReport = DateTime.Now;
            }
        }

        private void CreateLiveReport(bool open)
        {
            if (!running || String.IsNullOrEmpty(csvPath)) return;
            try
            {
                reportPath = BuildReport(DateTime.Now, true);
                AddRecent(DateTime.Now, L.T("報表", "Report"), "—", L.T("即時報表已更新，監控持續進行", "Live report updated; monitoring continues"), Color.RoyalBlue);
                if (open) OpenReport();
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("即時報表無法寫入：", "Could not write the live report: ") + ex.Message + L.T("\n監控仍會繼續。", "\nMonitoring will continue."), "NetCheck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void StopMonitoring(bool openReport)
        {
            if (!running) return;
            running = false;
            if (timer != null) { timer.Dispose(); timer = null; }
            if (paused)
            {
                pauses.Add(new TimePeriod { Start = pauseStart, End = DateTime.Now });
                paused = false;
            }
            WriteMarker("STOPPED", L.T("結束監控", "Monitoring stopped"));
            CloseWriter(ref writer);
            CloseWriter(ref backupWriter);
            SetAwake(false);
            intervalBox.Enabled = true;
            startButton.Enabled = true;
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            pauseButton.Text = L.T("暫停", "Pause");
            UpdateState(L.T("監控已結束", "Monitoring stopped"), Color.DimGray);
            reportPath = BuildReport(DateTime.Now, false);
            reportButton.Text = L.T("開啟最終報表", "Open Final Report");
            reportButton.Enabled = File.Exists(reportPath);
            if (openReport) OpenReport();
        }

        private bool SaveAndFinalizeForExit()
        {
            exitSaveError = null;
            try
            {
                if (running) StopMonitoring(false);
                CloseWriter(ref writer);
                CloseWriter(ref backupWriter);
                if (!String.IsNullOrEmpty(csvPath) && (String.IsNullOrEmpty(reportPath) || !File.Exists(reportPath)))
                {
                    reportPath = BuildReport(DateTime.Now, false);
                }
                return writer == null && backupWriter == null && (String.IsNullOrEmpty(csvPath) || (!String.IsNullOrEmpty(reportPath) && File.Exists(reportPath)));
            }
            catch (Exception ex)
            {
                exitSaveError = ex.Message;
                return false;
            }
        }

        private void RequestExit()
        {
            if (cloudManager != null && cloudManager.BackupInProgress)
            {
                MessageBox.Show(L.T("Google Drive 雲端備份仍在進行，請等待備份完成後再關閉程式。", "A Google Drive backup is still in progress. Wait for it to finish before closing the program."), "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string message = running
                ? L.T("監控仍在進行。確定要先儲存目前紀錄、建立最終報表，再關閉程式嗎？", "Monitoring is still running. Save the current records, create the final report, and then exit?")
                : L.T("確定要確認資料已儲存並關閉 NetCheckMonitor 嗎？", "Verify that data is saved and close NetCheckMonitor?");
            if (MessageBox.Show(message, L.T("關閉 NetCheckMonitor", "Exit NetCheckMonitor"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!SaveAndFinalizeForExit())
            {
                MessageBox.Show(L.T("資料或最終報表尚未完成儲存，因此程式沒有關閉。\n\n", "Data or the final report has not been saved, so the program remains open.\n\n") + (exitSaveError ?? L.T("未知錯誤", "Unknown error")), L.T("無法安全關閉", "Unable to Exit Safely"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            allowExit = true;
            Close();
        }

        private string BuildReport(DateTime sessionEnd, bool live)
        {
            string path = live ? Path.Combine(Path.GetDirectoryName(csvPath), Path.GetFileNameWithoutExtension(csvPath) + "_Live.html") : Path.ChangeExtension(csvPath, ".html");
            List<CheckRecord> snapshot;
            lock (records) snapshot = new List<CheckRecord>(records);
            var pauseSnapshot = new List<TimePeriod>();
            foreach (var p in pauses) pauseSnapshot.Add(new TimePeriod { Start = p.Start, End = p.End });
            if (paused) pauseSnapshot.Add(new TimePeriod { Start = pauseStart, End = sessionEnd });
            int good = 0, bad = 0;
            long latencyTotal = 0;
            foreach (var r in snapshot) { if (r.Online) { good++; latencyTotal += r.LatencyMs; } else bad++; }
            double availability = snapshot.Count == 0 ? 0 : (100.0 * good / snapshot.Count);
            long avgLatency = good == 0 ? 0 : latencyTotal / good;
            TimeSpan pausedTotal = TimeSpan.Zero;
            foreach (var p in pauseSnapshot) pausedTotal += p.End - p.Start;
            TimeSpan effective = (sessionEnd - sessionStart) - pausedTotal;

            var outages = BuildOutages(snapshot, sessionEnd);
            TimeSpan outageTotal = TimeSpan.Zero;
            foreach (var outage in outages) outageTotal += EffectiveDuration(outage.Start, outage.End, pauseSnapshot);
            double outagePercent = effective.TotalSeconds <= 0 ? 0 : 100.0 * outageTotal.TotalSeconds / effective.TotalSeconds;
            var dailyStats = BuildDailyStats(snapshot, pauseSnapshot, outages, sessionEnd);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='" + L.HtmlLanguage + "'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>" + H(L.T("NetCheckMonitor 網路監控報表", "NetCheckMonitor Network Monitoring Report")) + "</title><style>body{font-family:'Microsoft JhengHei UI','Segoe UI',sans-serif;background:#f4f6f8;color:#17202a;margin:0}.wrap{max-width:1100px;margin:auto;padding:28px}.card{background:white;border-radius:12px;padding:20px;margin:14px 0;box-shadow:0 2px 10px #00000012}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.metric{background:#f7f9fb;border-left:5px solid #2e86c1;padding:14px}.metric b{display:block;font-size:24px;margin-top:7px}.bad{color:#b03a2e}.good{color:#1e8449}table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:9px;border-bottom:1px solid #e5e7e9}svg{width:100%;height:42px;background:#eef1f3;border-radius:5px}.legend span{margin-right:18px}.dot{display:inline-block;width:11px;height:11px;margin-right:5px}.foot{color:#657;margin-top:18px;font-size:13px}@media print{body{background:white}.card{box-shadow:none;border:1px solid #ddd}}</style></head><body><div class='wrap'>");
            sb.Append("<h1>" + H(L.T("NetCheckMonitor 網路連線監控報表", "NetCheckMonitor Network Monitoring Report")) + "</h1><div class='card grid'>");
            Metric(sb, L.T("對外連線率", "Internet Availability"), availability.ToString("0.00") + "%", availability >= 99 ? "good" : "bad");
            Metric(sb, L.T("有效檢查", "Valid Checks"), snapshot.Count + L.T(" 次", ""), "");
            Metric(sb, L.T("斷線檢查", "Failed Checks"), bad + L.T(" 次", ""), bad == 0 ? "good" : "bad");
            Metric(sb, L.T("斷線事件", "Outage Events"), outages.Count + L.T(" 次", ""), outages.Count == 0 ? "good" : "bad");
            Metric(sb, L.T("平均回應時間", "Average Latency"), avgLatency + " ms", "");
            Metric(sb, L.T("有效監控時間", "Effective Monitoring"), FormatDuration(effective), "");
            Metric(sb, L.T("估計斷線時間", "Estimated Outage Time"), FormatDuration(outageTotal), outageTotal > TimeSpan.Zero ? "bad" : "good");
            Metric(sb, L.T("時間斷線率", "Time Outage Rate"), outagePercent.ToString("0.00") + "%", outagePercent > 0 ? "bad" : "good");
            sb.Append("</div><div class='card'><h2>" + H(L.T("監控資訊", "Monitoring Information")) + "</h2><table>");
            Row(sb, L.T("測試電腦名稱", "Computer Name"), machineName);
            Row(sb, L.T("電腦辨識碼", "Computer ID"), machineId + L.T("（", " (") + machineIdSource + L.T("，原始識別資料不寫入報表）", "; the original identifier is not stored in reports)"));
            Row(sb, L.T("開始", "Started"), sessionStart.ToString("yyyy/MM/dd HH:mm:ss"));
            Row(sb, L.T("結束", "Ended"), sessionEnd.ToString("yyyy/MM/dd HH:mm:ss"));
            Row(sb, L.T("暫停總時間（已排除）", "Total Paused Time (excluded)"), FormatDuration(pausedTotal));
            Row(sb, L.T("原始紀錄", "Raw Log"), Path.GetFileName(csvPath));
            if (!String.IsNullOrEmpty(backupCsvPath)) Row(sb, L.T("當機備援紀錄", "Crash-Recovery Log"), backupCsvPath);
            sb.Append("</table></div>");
            sb.Append("<div class='card'><h2>" + H(L.T("每日斷線統計", "Daily Outage Statistics")) + "</h2><table><thead><tr><th>" + H(L.T("日期", "Date")) + "</th><th>" + H(L.T("有效監控時間", "Effective Monitoring")) + "</th><th>" + H(L.T("估計斷線時間", "Estimated Outage")) + "</th><th>" + H(L.T("每日斷線百分比", "Daily Outage Percentage")) + "</th><th>" + H(L.T("斷線檢查", "Failed Checks")) + "</th></tr></thead><tbody>");
            foreach (var d in dailyStats)
            {
                string cls = d.Outage > TimeSpan.Zero ? "bad" : "good";
                sb.Append("<tr><td>" + d.Day.ToString("yyyy/MM/dd") + "</td><td>" + H(FormatDuration(d.Effective)) + "</td><td class='" + cls + "'>" + H(FormatDuration(d.Outage)) + "</td><td class='" + cls + "'>" + d.OutagePercent.ToString("0.00") + "%</td><td>" + d.OfflineChecks + L.T(" 次", "") + "</td></tr>");
            }
            sb.Append("</tbody></table></div>");
            sb.Append("<div class='card'><h2>" + H(L.T("每日連線時間軸", "Daily Connection Timeline")) + "</h2><p class='legend'><span><i class='dot' style='background:#28a745'></i>" + H(L.T("正常", "Online")) + "</span><span><i class='dot' style='background:#dc3545'></i>" + H(L.T("斷線", "Offline")) + "</span><span><i class='dot' style='background:#9aa0a6'></i>" + H(L.T("暫停", "Paused")) + "</span></p>");
            AppendTimelines(sb, snapshot, pauseSnapshot);
            sb.Append("</div><div class='card'><h2>" + H(L.T("斷線事件", "Outage Events")) + "</h2>");
            if (outages.Count == 0) sb.Append("<p class='good'>" + H(L.T("監控期間沒有偵測到斷線。", "No outage was detected during this monitoring period.")) + "</p>");
            else
            {
                sb.Append("<table><thead><tr><th>#</th><th>" + H(L.T("開始", "Started")) + "</th><th>" + H(L.T("恢復 / 最後偵測", "Recovered / Last Detected")) + "</th><th>" + H(L.T("估計斷線時間（排除暫停）", "Estimated Outage (paused time excluded)")) + "</th><th>" + H(L.T("失敗檢查", "Failed Checks")) + "</th></tr></thead><tbody>");
                for (int i = 0; i < outages.Count; i++)
                {
                    var o = outages[i];
                    sb.Append("<tr><td>" + (i + 1) + "</td><td>" + H(o.Start.ToString("yyyy/MM/dd HH:mm:ss")) + "</td><td>" + H(o.End.ToString("yyyy/MM/dd HH:mm:ss")) + "</td><td>" + H(FormatDuration(EffectiveDuration(o.Start, o.End, pauseSnapshot))) + "</td><td>" + o.Count + "</td></tr>");
                }
                sb.Append("</tbody></table>");
            }
            sb.Append("</div><p class='foot'>" + H(live ? L.T("這是監控進行中的即時快照，產生報表不會中斷檢查。", "This is a live snapshot. Creating it does not interrupt monitoring.") : L.T("這是監控結束後的最終報表。", "This is the final report after monitoring stopped.")) + " " + H(L.T("判定方式：依序嘗試 Microsoft、Google 與 Cloudflare 的 HTTPS 連線端點，任一成功即視為可對外連線。每日斷線百分比＝該日估計斷線時間 ÷ 該日有效監控時間；暫停區段會從兩者排除。斷線起訖精度受檢查間隔影響。", "Method: Microsoft, Google, and Cloudflare HTTPS endpoints are tried in order; any successful response means the Internet is reachable. Daily outage percentage equals estimated outage time divided by effective monitoring time. Paused periods are excluded from both. Outage start and end precision depends on the check interval.")) + "</p></div></body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private sealed class Outage { public DateTime Start; public DateTime End; public int Count; }
        private sealed class DailyStat
        {
            public DateTime Day;
            public TimeSpan Effective;
            public TimeSpan Outage;
            public double OutagePercent;
            public int OfflineChecks;
        }

        private static List<Outage> BuildOutages(List<CheckRecord> list, DateTime end)
        {
            var result = new List<Outage>();
            Outage current = null;
            foreach (var r in list)
            {
                if (!r.Online)
                {
                    if (current == null) current = new Outage { Start = r.Time, End = r.Time, Count = 0 };
                    current.End = r.Time;
                    current.Count++;
                }
                else if (current != null)
                {
                    current.End = r.Time;
                    result.Add(current);
                    current = null;
                }
            }
            if (current != null) { current.End = end; result.Add(current); }
            return result;
        }

        private List<DailyStat> BuildDailyStats(List<CheckRecord> list, List<TimePeriod> pauseList, List<Outage> outages, DateTime end)
        {
            var result = new List<DailyStat>();
            for (DateTime day = sessionStart.Date; day <= end.Date; day = day.AddDays(1))
            {
                DateTime rangeStart = sessionStart > day ? sessionStart : day;
                DateTime dayEnd = day.AddDays(1);
                DateTime rangeEnd = end < dayEnd ? end : dayEnd;
                if (rangeEnd <= rangeStart) continue;

                TimeSpan effective = EffectiveDuration(rangeStart, rangeEnd, pauseList);
                TimeSpan outageTime = TimeSpan.Zero;
                foreach (var outage in outages)
                {
                    DateTime a = outage.Start > rangeStart ? outage.Start : rangeStart;
                    DateTime b = outage.End < rangeEnd ? outage.End : rangeEnd;
                    if (b > a) outageTime += EffectiveDuration(a, b, pauseList);
                }
                int offlineChecks = 0;
                foreach (var record in list) if (!record.Online && record.Time >= rangeStart && record.Time < rangeEnd) offlineChecks++;
                double percent = effective.TotalSeconds <= 0 ? 0 : 100.0 * outageTime.TotalSeconds / effective.TotalSeconds;
                result.Add(new DailyStat { Day = day, Effective = effective, Outage = outageTime, OutagePercent = percent, OfflineChecks = offlineChecks });
            }
            return result;
        }

        private static TimeSpan EffectiveDuration(DateTime start, DateTime end, List<TimePeriod> pauseList)
        {
            if (end <= start) return TimeSpan.Zero;
            TimeSpan duration = end - start;
            foreach (var pause in pauseList)
            {
                DateTime a = pause.Start > start ? pause.Start : start;
                DateTime b = pause.End < end ? pause.End : end;
                if (b > a) duration -= b - a;
            }
            return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }

        private static void AppendTimelines(StringBuilder sb, List<CheckRecord> list, List<TimePeriod> pauseList)
        {
            var days = new SortedDictionary<DateTime, List<CheckRecord>>();
            foreach (var r in list)
            {
                DateTime d = r.Time.Date;
                if (!days.ContainsKey(d)) days[d] = new List<CheckRecord>();
                days[d].Add(r);
            }
            foreach (var p in pauseList)
            {
                for (DateTime d = p.Start.Date; d <= p.End.Date; d = d.AddDays(1)) if (!days.ContainsKey(d)) days[d] = new List<CheckRecord>();
            }
            if (days.Count == 0) { sb.Append("<p>" + H(L.T("沒有有效檢查資料。", "No valid check data.")) + "</p>"); return; }
            foreach (var pair in days)
            {
                DateTime day = pair.Key;
                sb.Append("<h3>" + day.ToString("yyyy/MM/dd") + "</h3><svg viewBox='0 0 1000 42' preserveAspectRatio='none'>");
                foreach (var r in pair.Value)
                {
                    double x = (r.Time - day).TotalSeconds / 86400.0 * 1000.0;
                    sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' y='0' width='1.6' height='42' fill='" + (r.Online ? "#28a745" : "#dc3545") + "'><title>" + H(r.Time.ToString("HH:mm:ss") + " " + (r.Online ? L.T("正常", "Online") : L.T("斷線", "Offline"))) + "</title></rect>");
                }
                foreach (var p in pauseList)
                {
                    DateTime a = p.Start < day ? day : p.Start;
                    DateTime b = p.End > day.AddDays(1) ? day.AddDays(1) : p.End;
                    if (b > a)
                    {
                        double x = (a - day).TotalSeconds / 86400.0 * 1000.0;
                        double w = Math.Max(1.0, (b - a).TotalSeconds / 86400.0 * 1000.0);
                        sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' y='0' width='" + w.ToString("0.0", CultureInfo.InvariantCulture) + "' height='42' fill='#9aa0a6'><title>" + H(L.T("暫停 ", "Paused ") + a.ToString("HH:mm:ss") + "–" + b.ToString("HH:mm:ss")) + "</title></rect>");
                    }
                }
                sb.Append("</svg><div style='display:flex;justify-content:space-between;color:#667;font-size:12px'><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div>");
            }
        }

        private static void Metric(StringBuilder sb, string name, string value, string cls) { sb.Append("<div class='metric'><span>" + H(name) + "</span><b class='" + cls + "'>" + H(value) + "</b></div>"); }
        private static void Row(StringBuilder sb, string name, string value) { sb.Append("<tr><th>" + H(name) + "</th><td>" + H(value) + "</td></tr>"); }
        private static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        private static string GetMachineId(out string source)
        {
            string seed = null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key != null) seed = Convert.ToString(key.GetValue("MachineGuid"));
                }
            }
            catch { }
            source = L.T("Windows 機器 ID 雜湊", "Windows machine ID hash");
            if (String.IsNullOrEmpty(seed))
            {
                var addresses = new List<string>();
                try
                {
                    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        string address = adapter.GetPhysicalAddress().ToString();
                        if (!String.IsNullOrEmpty(address)) addresses.Add(address);
                    }
                }
                catch { }
                addresses.Sort(StringComparer.Ordinal);
                seed = addresses.Count > 0 ? addresses[0] : Environment.MachineName;
                source = addresses.Count > 0 ? L.T("MAC 位址雜湊備援", "MAC address hash fallback") : L.T("電腦名稱雜湊備援", "computer name hash fallback");
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var id = new StringBuilder(8);
                for (int i = 0; i < 4; i++) id.Append(hash[i].ToString("X2"));
                return id.ToString();
            }
        }

        private static string SafeFilePart(string value, int maxLength)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "PC")
            {
                if (Char.IsLetterOrDigit(c) || c == '-' || c == '_') result.Append(c);
                else result.Append('-');
                if (result.Length >= maxLength) break;
            }
            string safe = result.ToString().Trim('-', '_');
            return String.IsNullOrEmpty(safe) ? "PC" : safe;
        }

        private void WriteCheck(CheckRecord r)
        {
            WriteLogLine(Csv(r.Time.ToString("o")) + ",CHECK," + (r.Online ? "ONLINE" : "OFFLINE") + "," + (r.Online ? r.LatencyMs.ToString() : "") + "," + Csv(r.Target) + "," + Csv(r.Detail));
        }

        private void WriteMarker(string status, string detail)
        {
            WriteLogLine(Csv(DateTime.Now.ToString("o")) + ",MARKER," + status + ",,," + Csv(detail));
        }

        private static StreamWriter CreateDurableWriter(string path)
        {
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            return new StreamWriter(stream, new UTF8Encoding(true));
        }

        private void WriteLogLine(string line)
        {
            lock (this)
            {
                TryWrite(ref writer, line, L.T("主要紀錄", "Primary log"));
                TryWrite(ref backupWriter, line, L.T("備援紀錄", "Recovery log"));
            }
        }

        private void TryWrite(ref StreamWriter target, string line, string name)
        {
            if (target == null) return;
            try
            {
                target.WriteLine(line);
                target.Flush();
                var file = target.BaseStream as FileStream;
                if (file != null) file.Flush(true);
            }
            catch (Exception ex)
            {
                logWarning = name + L.T("寫入失敗（", " write failed (") + ex.Message + L.T("）", ")");
                CloseWriter(ref target);
            }
        }

        private static void CloseWriter(ref StreamWriter target)
        {
            if (target == null) return;
            try
            {
                target.Flush();
                var file = target.BaseStream as FileStream;
                if (file != null) file.Flush(true);
            }
            catch { }
            try { target.Dispose(); } catch { }
            target = null;
        }

        private static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
        private static string FormatDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalDays >= 1) return L.TraditionalChinese ? ((int)t.TotalDays) + " 天 " + t.Hours + " 小時 " + t.Minutes + " 分" : ((int)t.TotalDays) + "d " + t.Hours + "h " + t.Minutes + "m";
            if (t.TotalHours >= 1) return L.TraditionalChinese ? ((int)t.TotalHours) + " 小時 " + t.Minutes + " 分" : ((int)t.TotalHours) + "h " + t.Minutes + "m";
            return L.TraditionalChinese ? ((int)t.TotalMinutes) + " 分 " + t.Seconds + " 秒" : ((int)t.TotalMinutes) + "m " + t.Seconds + "s";
        }

        private void AddRecent(DateTime time, string status, string latency, string detail, Color color)
        {
            var item = new ListViewItem(new string[] { time.ToString("yyyy/MM/dd HH:mm:ss"), status, latency, detail });
            item.ForeColor = color;
            recentList.Items.Insert(0, item);
            while (recentList.Items.Count > 300) recentList.Items.RemoveAt(recentList.Items.Count - 1);
        }

        private void UpdateState(string text, Color color) { stateLabel.Text = text; stateLabel.ForeColor = color; }
        private static void SetAwake(bool awake) { SetThreadExecutionState(awake ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS); }
        private void OpenReport() { if (!String.IsNullOrEmpty(reportPath) && File.Exists(reportPath)) Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true }); }
        private void HideToTray() { Hide(); trayIcon.Visible = true; trayIcon.ShowBalloonTip(2000, L.T("NetCheckMonitor 仍在執行", "NetCheckMonitor Is Still Running"), L.T("視窗已縮到系統匣，監控不會中斷。", "The window was minimized to the system tray. Monitoring continues."), ToolTipIcon.Info); }
        private void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); if (!running || records.Count == 0 || (records.Count > 0 && records[records.Count - 1].Online)) trayIcon.Visible = false; }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (running && e.CloseReason == CloseReason.UserClosing && !allowExit)
            {
                e.Cancel = true;
                HideToTray();
                trayIcon.ShowBalloonTip(4000, L.T("NetCheckMonitor 防誤關保護", "NetCheckMonitor Close Protection"), L.T("監控仍在進行，因此關閉按鈕只會縮到系統匣。請用「結束並產生報表」正常結束。", "Monitoring is still active, so the close button only minimizes to the tray. Use Stop and Create Report to finish normally."), ToolTipIcon.Info);
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing && cloudManager != null && cloudManager.BackupInProgress)
            {
                e.Cancel = true;
                allowExit = false;
                MessageBox.Show(L.T("Google Drive 雲端備份仍在進行，請等待備份完成後再關閉程式。", "A Google Drive backup is still in progress. Wait for it to finish before closing the program."), "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (running) StopMonitoring(false);
            if (cloudManager != null) cloudManager.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }
    }

    internal sealed class AboutForm : Form
    {
        internal const string AppVersion = "0.9.1";
        internal const string Purpose = "可定時監控對外網路連線，紀錄斷線並產生圖文報表，並支援網路硬碟備份，PDF 下載，程式完全免費開源無廣告。";
        internal const string EnglishPurpose = "Scheduled monitoring of external Internet connectivity, outage logging, graphical reports, cloud-drive backup, and PDF downloads. Completely free, open source, and ad-free.";

        public AboutForm()
        {
            Text = L.T("關於 NetCheckMonitor", "About NetCheckMonitor");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(540, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label { Text = "NetCheckMonitor", Font = new Font(Font.FontFamily, 22F, FontStyle.Bold), AutoSize = true, Location = new Point(26, 22) };
            var version = new Label { Text = L.T("版本 ", "Version ") + AppVersion, AutoSize = true, ForeColor = Color.DimGray, Location = new Point(29, 70) };
            var purpose = new Label { Text = L.T(Purpose, EnglishPurpose), AutoSize = false, Location = new Point(29, 105), Size = new Size(485, 45) };
            var authorTitle = new Label { Text = L.T("作者資訊", "Author"), Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), AutoSize = true, Location = new Point(29, 155) };
            var author = new Label { Text = "廖阿輝", AutoSize = true, Location = new Point(29, 187) };
            var email = new LinkLabel { Text = "chehui@gmail.com", AutoSize = true, Location = new Point(119, 187) };
            var website = new LinkLabel { Text = "https://ahui3c.com", AutoSize = true, Location = new Point(29, 218) };
            var close = new Button { Text = L.T("關閉", "Close"), Location = new Point(394, 252), Size = new Size(120, 32) };

            email.LinkClicked += delegate { Process.Start(new ProcessStartInfo("mailto:chehui@gmail.com") { UseShellExecute = true }); };
            website.LinkClicked += delegate { Process.Start(new ProcessStartInfo("https://ahui3c.com") { UseShellExecute = true }); };
            close.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { title, version, purpose, authorTitle, author, email, website, close });
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
