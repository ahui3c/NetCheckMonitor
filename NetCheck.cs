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
[assembly: AssemblyVersion("0.9.7.0")]
[assembly: AssemblyFileVersion("0.9.7.0")]

namespace NetCheck
{
    internal static class SingleInstance
    {
        private const string DefaultMutexName = @"Local\NetCheckMonitor-7C54A9D1-839F-4D9A-A803-EC852DA27A14";
        private const string ShowMessageName = "NetCheckMonitor.ShowExistingWindow.7C54A9D1-839F-4D9A-A803-EC852DA27A14";
        internal static readonly int ShowWindowMessage = (int)RegisterWindowMessage(ShowMessageName);
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string messageName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal static bool TryAcquire(out Mutex mutex)
        {
            bool createdNew;
            string name = Environment.GetEnvironmentVariable("NETCHECK_INSTANCE_NAME");
            if (String.IsNullOrWhiteSpace(name)) name = DefaultMutexName;
            mutex = new Mutex(true, name, out createdNew);
            if (createdNew) return true;
            mutex.Dispose();
            mutex = null;
            return false;
        }

        internal static void ShowExistingWindow()
        {
            if (ShowWindowMessage == 0) return;
            for (int i = 0; i < 6; i++)
            {
                PostMessage(HwndBroadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(120);
            }
        }
    }

    internal enum TrayConnectionState
    {
        Idle,
        Checking,
        Online,
        Offline,
        Paused
    }

    internal sealed class CheckRecord
    {
        public DateTime Time;
        public bool Online;
        public long LatencyMs;
        public string Target;
        public string Detail;
        public string Status;
        public bool JustConfirmed;
        public bool JustRecovered;
        public int RetryNumber;
        public DateTime OutageStart;
        public NetworkSnapshot Network;
        public AdvancedDiagnosticResult Diagnostic;
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
        private readonly Label networkInfoLabel = new Label();
        private readonly Button startButton = new Button();
        private readonly Button pauseButton = new Button();
        private readonly Button stopButton = new Button();
        private readonly Button reportButton = new Button();
        private readonly Button dataButton = new Button();
        private readonly Button clearDataButton = new Button();
        private readonly Button exitButton = new Button();
        private readonly Button cloudButton = new Button();
        private readonly Button aboutButton = new Button();
        private readonly Button settingsButton = new Button();
        private readonly Button eventNoteButton = new Button();
        private readonly Label versionLabel = new Label();
        private readonly NumericUpDown intervalBox = new NumericUpDown();
        private readonly ListView recentList = new ListView();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private Icon neutralTrayIcon;
        private Icon checkingTrayIcon;
        private Icon onlineTrayIcon;
        private Icon offlineTrayIcon;
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
        private int consecutiveFailures;
        private int checkIntervalSeconds = 60;
        private bool outageConfirmed;
        private DateTime suspectedStart;
        private DateTime lastStateHeartbeat;
        private DateTime processStartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        private CloudBackupManager cloudManager;
        private MonitorTargetSettings monitorSettings;
        private NetworkSnapshot currentNetwork;
        private AdvancedDiagnosticResult lastAdvancedDiagnostic;
        private DateTime lastAdvancedDiagnosticAt = DateTime.MinValue;
        private bool shutdownBlockReasonActive;
        private readonly List<CheckRecord> records = new List<CheckRecord>();
        private readonly List<TimePeriod> pauses = new List<TimePeriod>();
        private readonly List<EventNote> eventNotes = new List<EventNote>();

        private const int FastRetrySeconds = 5;
        private const int FastRetryLimit = 6;
        private const int OutageBackoffSeconds = 30;

        private static readonly string[] TestUrls = new string[] {
            "https://www.msftconnecttest.com/connecttest.txt",
            "https://connectivitycheck.gstatic.com/generate_204",
            "https://cp.cloudflare.com/generate_204"
        };
        private string[] activeTestUrls = (string[])TestUrls.Clone();

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShutdownBlockReasonCreate(IntPtr window, string reason);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShutdownBlockReasonDestroy(IntPtr window);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const int WM_QUERYENDSESSION = 0x0011;

        public MainForm()
        {
            Text = L.T("對外網路連線能力監控程式", "NetCheckMonitor Network Monitor");
            Icon = LoadApplicationIcon();
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(780, 530);
            MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = L.T("對外連線能力監控", "Network Connection Monitor"), Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Location = new Point(22, 18) };
            versionLabel.Text = "v" + AboutForm.AppVersion;
            versionLabel.Font = new Font(Font.FontFamily, 8F);
            versionLabel.ForeColor = Color.DarkGray;
            versionLabel.AutoSize = true;
            versionLabel.Location = new Point(title.Left + TextRenderer.MeasureText(title.Text, title.Font).Width + 8, 34);
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
            settingsButton.Text = L.T("設定", "Settings");
            eventNoteButton.Text = L.T("事件註記", "Event Note");
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
            settingsButton.SetBounds(280, 502, 80, 24);
            settingsButton.Font = new Font(Font.FontFamily, 8.5F);
            settingsButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            eventNoteButton.SetBounds(370, 502, 120, 24);
            eventNoteButton.Font = new Font(Font.FontFamily, 8.5F);
            eventNoteButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            eventNoteButton.Enabled = false;
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            reportButton.Enabled = false;

            lastLabel.Text = L.T("最後檢查：—", "Last check: —");
            lastLabel.AutoSize = true;
            lastLabel.Location = new Point(27, 166);
            statsLabel.Text = L.T("有效檢查 0 次｜正常 0 次｜斷線 0 次｜暫停時間不列入統計", "Checks 0 | Online 0 | Offline 0 | Paused time excluded");
            statsLabel.AutoSize = true;
            statsLabel.Location = new Point(27, 193);

            networkInfoLabel.Text = L.T("目前網卡：正在讀取…", "Adapter: Reading…");
            networkInfoLabel.AutoEllipsis = true;
            networkInfoLabel.ForeColor = Color.DimGray;
            networkInfoLabel.Font = new Font(Font.FontFamily, 8.5F);
            networkInfoLabel.SetBounds(27, 216, 728, 24);

            recentList.Location = new Point(25, 248);
            recentList.Size = new Size(730, 250);
            recentList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            recentList.View = View.Details;
            recentList.FullRowSelect = true;
            recentList.GridLines = true;
            recentList.Columns.Add(L.T("時間", "Time"), 155);
            recentList.Columns.Add(L.T("狀態", "Status"), 90);
            recentList.Columns.Add(L.T("延遲", "Latency"), 90);
            recentList.Columns.Add(L.T("檢測目標 / 說明", "Target / Details"), 365);

            Controls.AddRange(new Control[] { title, versionLabel, stateLabel, intervalLabel, intervalBox, startButton, pauseButton, stopButton, reportButton, dataButton, lastLabel, statsLabel, networkInfoLabel, recentList, clearDataButton, exitButton, cloudButton, aboutButton, settingsButton, eventNoteButton });

            startButton.Click += delegate { StartMonitoring(); };
            pauseButton.Click += delegate { TogglePause(); };
            stopButton.Click += delegate { StopMonitoring(true); };
            reportButton.Click += delegate { if (running) CreateLiveReport(true); else OpenReport(); };
            dataButton.Click += delegate { ShowDataManager(); };
            clearDataButton.Click += delegate { ClearStoredData(); };
            exitButton.Click += delegate { RequestExit(); };
            cloudButton.Click += delegate { ShowCloudSettings(); };
            aboutButton.Click += delegate { using (var form = new AboutForm()) form.ShowDialog(this); };
            settingsButton.Click += delegate { ShowMonitorSettings(); };
            eventNoteButton.Click += delegate { ShowEventNoteDialog(); };
            FormClosing += OnFormClosing;
            Shown += delegate { BeginInvoke((MethodInvoker)HandleStartupMonitoring); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };

            neutralTrayIcon = (Icon)this.Icon.Clone();
            checkingTrayIcon = CreateStatusIcon(Color.DarkOrange);
            onlineTrayIcon = CreateStatusIcon(Color.LimeGreen);
            offlineTrayIcon = CreateStatusIcon(Color.Red);
            trayIcon.Text = L.T("對外網路連線能力監控程式", "NetCheckMonitor Network Monitor");
            trayIcon.Icon = neutralTrayIcon;
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(L.T("顯示視窗", "Show Window"), null, delegate { ShowFromTray(); });
            trayMenu.Items.Add(L.T("結束程式", "Exit"), null, delegate { ShowFromTray(); RequestExit(); });
            trayIcon.ContextMenuStrip = trayMenu;
            EnsureMachineIdentity();
            try { reportPath = ArchiveReport.EnsureCumulativeHtml(machineName, machineId); }
            catch { reportPath = ArchiveReport.FindLatestCumulativeHtml(machineId); }
            if (!String.IsNullOrEmpty(reportPath)) reportButton.Text = L.T("開啟累積報表", "Open Cumulative Report");
            reportButton.Enabled = !String.IsNullOrEmpty(reportPath);
            monitorSettings = MonitorSettingsStore.Load();
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NETCHECK_MONITOR_SETTINGS")))
                try { AutoStartManager.SetEnabled(monitorSettings.AutoStartWindows); } catch { }
            cloudManager = new CloudBackupManager(machineName, machineId);
        }

        private void StartMonitoring()
        {
            records.Clear();
            pauses.Clear();
            eventNotes.Clear();
            recentList.Items.Clear();
            reportPath = null;
            reportButton.Text = L.T("產生即時報表", "Create Live Report");
            reportButton.Enabled = true;
            lastAutoReport = DateTime.MinValue;
            logWarning = null;
            ResetOutageTracking();
            sessionStart = DateTime.Now;
            checkIntervalSeconds = (int)intervalBox.Value;
            EnsureMachineIdentity();
            monitorSettings = MonitorSettingsStore.Load();
            activeTestUrls = MonitorSettingsStore.GetEffectiveTargets(monitorSettings, TestUrls);
            currentNetwork = NetworkStatusReader.Capture();
            string baseSessionFileStem = "NetCheck_" + SafeFilePart(machineName, 16) + "-" + machineId + "_" + sessionStart.ToString("yyyyMMdd_HHmmss");
            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string dataDir = Path.Combine(executableDir, "NetCheck_Data");
            try { Directory.CreateDirectory(dataDir); }
            catch { dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NetCheck_Data"); Directory.CreateDirectory(dataDir); }
            sessionFileStem = AllocateSessionFileStem(dataDir, baseSessionFileStem);
            csvPath = Path.Combine(dataDir, sessionFileStem + ".csv");
            try { writer = CreateDurableWriter(csvPath); }
            catch
            {
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Data");
                Directory.CreateDirectory(dataDir);
                sessionFileStem = AllocateSessionFileStem(dataDir, baseSessionFileStem);
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
            WriteMarker("TARGETS", (monitorSettings.UseCustomTargets ? L.T("自訂目標：", "Custom targets: ") : L.T("內建目標：", "Built-in targets: ")) + String.Join(" | ", activeTestUrls));
            WriteMarker("ADVANCED_DIAGNOSTICS", monitorSettings.AdvancedDiagnosticsEnabled ? "ENABLED" : "DISABLED");
            WriteMarker("POWER_PROTECTION", PowerProtectionMarker(monitorSettings));
            WriteMarker("NETWORK", currentNetwork.ToMarker());

            running = true;
            paused = false;
            intervalBox.Enabled = false;
            startButton.Enabled = false;
            settingsButton.Enabled = true;
            pauseButton.Enabled = true;
            stopButton.Enabled = true;
            eventNoteButton.Enabled = true;
            pauseButton.Text = L.T("暫停", "Pause");
            UpdatePowerProtection();
            UpdateState(L.T("準備檢查…", "Preparing check…"), Color.DarkOrange);
            RenderNetworkInfo(currentNetwork);
            SetTrayConnectionState(TrayConnectionState.Checking, true);
            PersistSessionState();
            timer = new System.Threading.Timer(delegate { PerformCheck(); }, null, 0, Timeout.Infinite);
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
            if (failures.Count == 0) SessionStateStore.Delete();
            if (failures.Count > 0)
            {
                string prefix = deleted == 0 ? L.T("沒有清除任何資料。偵測到仍被使用或無法刪除的檔案。", "No data was cleared. Some files are in use or could not be deleted.") : L.T("已清除 ", "Cleared ") + deleted + L.T(" 個檔案，但下列檔案無法刪除。", " files, but the following files could not be deleted.");
                MessageBox.Show(prefix + L.T("\n請先結束所有 NetCheck 監控程式後再試一次。\n\n", "\nClose all NetCheck monitoring programs and try again.\n\n") + String.Join("\n", failures.ToArray()), L.T("資料清除未完成", "Data Clearing Incomplete"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                reportPath = null;
                reportButton.Text = L.T("開啟累積報表", "Open Cumulative Report");
                reportButton.Enabled = false;
                MessageBox.Show(L.T("已清除 ", "Cleared ") + deleted + L.T(" 個 NetCheck 儲存檔案。", " NetCheck saved files."), L.T("清除完成", "Clearing Complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowCloudSettings()
        {
            using (var form = new CloudBackupForm(cloudManager)) form.ShowDialog(this);
        }

        private void ShowMonitorSettings()
        {
            using (var form = new MonitorSettingsForm(monitorSettings, ForceRebuildDailyDetailReports))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string previousLanguage = LanguagePreferenceStore.Load() ?? LanguagePreferenceStore.English;
                    bool restarted = ApplyMonitorSettings(form.Result);
                    bool languageChanged = !String.Equals(previousLanguage, form.SelectedLanguage, StringComparison.OrdinalIgnoreCase);
                    LanguagePreferenceStore.Save(form.SelectedLanguage);
                    string autoStartWarning = null;
                    try { AutoStartManager.SetEnabled(form.Result.AutoStartWindows); }
                    catch (Exception ex) { autoStartWarning = ex.Message; }
                    string message = restarted
                        ? L.T("設定已儲存。原監控資料與報表已安全保存，並已使用新目標重新開始監控。", "Settings saved. The previous monitoring data and report were saved safely, and monitoring restarted with the new targets.")
                        : L.T("設定已儲存；目前監控使用的目標沒有變更，因此不需要重新啟動監控。", "Settings saved. The targets used by the current monitoring session did not change, so monitoring was not restarted.");
                    if (languageChanged) message += L.T("\n\n介面語言將在下次啟動程式時套用。", "\n\nThe interface language will be applied the next time the app starts.");
                    if (!String.IsNullOrEmpty(autoStartWarning)) message += L.T("\n\n但無法更新 Windows 自動啟動設定：", "\n\nWindows startup could not be updated: ") + autoStartWarning;
                    MessageBox.Show(message, form.Text, MessageBoxButtons.OK, String.IsNullOrEmpty(autoStartWarning) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(L.T("無法儲存設定：", "Could not save settings: ") + ex.Message, form.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ForceRebuildDailyDetailReports()
        {
            string output = reportPath;
            if (String.IsNullOrEmpty(output)) output = ArchiveReport.EnsureCumulativeHtml(machineName, machineId);
            if (String.IsNullOrEmpty(output)) throw new InvalidOperationException(L.T("目前沒有可製作報表的測試資料。", "There is currently no test data available for a report."));
            reportPath = ArchiveReport.ForceRebuildDailyDetailReports(output, running);
            reportButton.Text = running ? L.T("產生即時報表", "Create Live Report") : L.T("開啟累積報表", "Open Cumulative Report");
            reportButton.Enabled = true;
        }

        private void ShowEventNoteDialog()
        {
            if (!running) return;
            using (var form = new EventNoteForm())
                if (form.ShowDialog(this) == DialogResult.OK) AddEventNote(form.NoteText);
        }

        private void AddEventNote(string text)
        {
            if (!running || String.IsNullOrWhiteSpace(text)) return;
            string value = text.Trim();
            if (value.Length > 500) value = value.Substring(0, 500);
            var note = new EventNote { Time = DateTime.Now, Text = value };
            lock (eventNotes) eventNotes.Add(note);
            WriteMarkerAt(note.Time, "EVENT_NOTE", note.Text);
            PersistSessionState();
            AddRecent(note.Time, L.T("事件", "Event"), "—", note.Text, Color.MediumPurple);
        }

        private bool ApplyMonitorSettings(MonitorTargetSettings updated)
        {
            if (updated == null) throw new ArgumentNullException("updated");
            bool restart = running && MonitoringTargetsChanged(monitorSettings, updated);
            MonitorSettingsStore.Save(updated);
            monitorSettings = updated;
            UpdatePowerProtection();
            if (!updated.AdvancedDiagnosticsEnabled) { lastAdvancedDiagnostic = null; lastAdvancedDiagnosticAt = DateTime.MinValue; }
            if (running)
            {
                WriteMarker("ADVANCED_DIAGNOSTICS", updated.AdvancedDiagnosticsEnabled ? "ENABLED" : "DISABLED");
                WriteMarker("POWER_PROTECTION", PowerProtectionMarker(updated));
            }
            if (restart)
            {
                StopMonitoring(false);
                StartMonitoring();
            }
            return restart;
        }

        private static bool MonitoringTargetsChanged(MonitorTargetSettings before, MonitorTargetSettings after)
        {
            if (before == null || after == null) return true;
            if (before.UseCustomTargets != after.UseCustomTargets) return true;
            if (!before.UseCustomTargets) return false;
            List<string> oldTargets = before.CustomTargets ?? new List<string>();
            List<string> newTargets = after.CustomTargets ?? new List<string>();
            if (oldTargets.Count != newTargets.Count) return true;
            for (int i = 0; i < oldTargets.Count; i++)
                if (!String.Equals(oldTargets[i], newTargets[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
                SetTrayConnectionState(TrayConnectionState.Paused, true);
                PersistSessionState();
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
                SetTrayConnectionState(TrayConnectionState.Checking, true);
                PersistSessionState();
                if (timer != null) timer.Change(0, Timeout.Infinite);
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
            int nextDelay = checkIntervalSeconds * 1000;
            CheckRecord record = null;
            bool dismissedSuspected = false;
            NetworkSnapshot network = NetworkStatusReader.Capture();
            bool networkChanged = currentNetwork == null || currentNetwork.Signature != network.Signature;
            try
            {
                try
                {
                    if (!NetworkInterface.GetIsNetworkAvailable())
                    {
                        detail = L.T("Windows 未偵測到可用的網路介面", "Windows detected no available network interface");
                    }
                    else
                    {
                        var errors = new List<string>();
                        foreach (string url in activeTestUrls)
                        {
                            var sw = Stopwatch.StartNew();
                            try
                            {
                                var request = (HttpWebRequest)WebRequest.Create(url);
                                request.Method = "GET";
                                request.Timeout = 5000;
                                request.ReadWriteTimeout = 5000;
                                request.UserAgent = "NetCheckMonitor/0.9.7";
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

                if (online)
                {
                    int retries = consecutiveFailures;
                    bool recovered = outageConfirmed;
                    dismissedSuspected = retries > 0 && !outageConfirmed;
                    record = new CheckRecord { Time = at, Online = true, LatencyMs = latency, Target = target, Detail = detail, Status = "ONLINE", JustRecovered = recovered, RetryNumber = retries, Network = network };
                    ResetOutageTracking();
                }
                else
                {
                    AdvancedDiagnosticResult diagnostic = null;
                    if (monitorSettings != null && monitorSettings.AdvancedDiagnosticsEnabled)
                    {
                        if (lastAdvancedDiagnostic == null || (at - lastAdvancedDiagnosticAt).TotalSeconds >= 30)
                        {
                            lastAdvancedDiagnostic = AdvancedNetworkDiagnostics.Run(network, activeTestUrls);
                            lastAdvancedDiagnosticAt = at;
                        }
                        diagnostic = lastAdvancedDiagnostic;
                        detail += " || " + diagnostic.ToLogString();
                    }
                    consecutiveFailures++;
                    if (consecutiveFailures == 1) suspectedStart = at;
                    bool justConfirmed = consecutiveFailures >= 2 && !outageConfirmed;
                    if (justConfirmed) outageConfirmed = true;
                    string status = outageConfirmed ? "OFFLINE" : "SUSPECTED";
                    record = new CheckRecord { Time = at, Online = false, LatencyMs = 0, Target = target, Detail = detail, Status = status, JustConfirmed = justConfirmed, RetryNumber = consecutiveFailures, OutageStart = suspectedStart, Network = network, Diagnostic = diagnostic };
                    nextDelay = consecutiveFailures <= FastRetryLimit
                        ? FastRetrySeconds * 1000
                        : Math.Min(checkIntervalSeconds, OutageBackoffSeconds) * 1000;
                }

                if (!running) return;
                if (networkChanged) WriteMarker("NETWORK", network.ToMarker());
                currentNetwork = network;
                lock (records) records.Add(record);
                WriteCheck(record);
                if (record.Status == "SUSPECTED") WriteMarker("OUTAGE_SUSPECTED", L.T("首次失敗，5 秒後快速複查", "First failure; fast retry in 5 seconds"));
                if (record.JustConfirmed) WriteMarker("OUTAGE_CONFIRMED", L.T("連續失敗，確認斷線；疑似開始：", "Consecutive failures confirmed an outage; suspected start: ") + suspectedStart.ToString("o"));
                if (record.JustRecovered) WriteMarker("OUTAGE_RECOVERED", L.T("確認網路已恢復；快速追蹤失敗次數：", "Internet recovery confirmed; fast-tracking failures: ") + record.RetryNumber);
                if (dismissedSuspected) WriteMarker("OUTAGE_DISMISSED", L.T("快速複查成功，未形成確認斷線", "Fast retry succeeded; suspected outage dismissed"));
                PersistSessionState();
                if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { RenderCheck(record); });
            }
            finally
            {
                Interlocked.Exchange(ref checking, 0);
                ScheduleNextCheck(nextDelay);
            }
        }

        private void ScheduleNextCheck(int delayMilliseconds)
        {
            if (!running || paused || timer == null) return;
            try { timer.Change(Math.Max(1000, delayMilliseconds), Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        private void ResetOutageTracking()
        {
            consecutiveFailures = 0;
            outageConfirmed = false;
            suspectedStart = DateTime.MinValue;
            lastAdvancedDiagnostic = null;
            lastAdvancedDiagnosticAt = DateTime.MinValue;
        }

        private void PersistSessionState()
        {
            if (!running || String.IsNullOrEmpty(csvPath)) return;
            try
            {
                lastStateHeartbeat = DateTime.Now;
                SessionStateStore.Save(new ActiveSessionState
                {
                    Active = true,
                    MachineId = machineId,
                    CsvPath = csvPath,
                    BackupCsvPath = backupCsvPath,
                    SessionFileStem = sessionFileStem,
                    SessionStart = sessionStart,
                    LastHeartbeat = lastStateHeartbeat,
                    IntervalSeconds = checkIntervalSeconds,
                    Paused = paused,
                    PauseStart = pauseStart,
                    Targets = new List<string>(activeTestUrls),
                    UseCustomTargets = monitorSettings != null && monitorSettings.UseCustomTargets,
                    ConsecutiveFailures = consecutiveFailures,
                    OutageConfirmed = outageConfirmed,
                    SuspectedStart = suspectedStart,
                    ProcessId = Process.GetCurrentProcess().Id,
                    ProcessStartedUtc = processStartedUtc
                });
            }
            catch (Exception ex) { logWarning = L.T("接續狀態儲存失敗（", "Resume state save failed (") + ex.Message + L.T("）", ")"); }
        }

        private void HandleStartupMonitoring()
        {
            if (TryOfferSessionResume() || running) return;
            monitorSettings = MonitorSettingsStore.Load();
            if (monitorSettings == null || !monitorSettings.AutoStartMonitoring) return;
            try { StartMonitoring(); }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("無法自動開始監控：", "Could not start monitoring automatically: ") + ex.Message, "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool TryOfferSessionResume()
        {
            if (running) return true;
            ActiveSessionState state = SessionStateStore.Load();
            if (state == null) return false;
            EnsureMachineIdentity();
            if (!String.Equals(state.MachineId, machineId, StringComparison.OrdinalIgnoreCase) || !File.Exists(state.CsvPath))
            {
                SessionStateStore.Delete();
                return false;
            }
            if (SessionStateStore.IsOriginalProcessAlive(state)) return true;
            string message = L.T("發現上次未正常結束的監控工作。\n\n開始時間：", "An unfinished monitoring session was found.\n\nStarted: ")
                + state.SessionStart.ToString("yyyy/MM/dd HH:mm:ss")
                + L.T("\n最後保存：", "\nLast saved: ") + state.LastHeartbeat.ToString("yyyy/MM/dd HH:mm:ss")
                + L.T("\n\n是否接續原本的 CSV 與監控統計？程式未執行的空白時段會標示並排除統計。", "\n\nResume the original CSV and monitoring statistics? Time when the app was not running will be marked and excluded.");
            if (MessageBox.Show(message, L.T("接續未完成監控", "Resume Unfinished Monitoring"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { ResumeMonitoring(state); return true; }
                catch (Exception ex)
                {
                    MessageBox.Show(L.T("無法接續監控：", "Could not resume monitoring: ") + ex.Message, "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return true;
                }
            }
            SessionStateStore.Delete();
            return false;
        }

        private void ResumeMonitoring(ActiveSessionState state)
        {
            records.Clear();
            pauses.Clear();
            eventNotes.Clear();
            recentList.Items.Clear();
            LoadSessionHistory(state.CsvPath);
            csvPath = state.CsvPath;
            backupCsvPath = state.BackupCsvPath;
            sessionFileStem = String.IsNullOrWhiteSpace(state.SessionFileStem) ? Path.GetFileNameWithoutExtension(csvPath) : state.SessionFileStem;
            sessionStart = state.SessionStart;
            intervalBox.Value = Math.Max(intervalBox.Minimum, Math.Min(intervalBox.Maximum, state.IntervalSeconds));
            checkIntervalSeconds = (int)intervalBox.Value;
            activeTestUrls = state.Targets != null && state.Targets.Count > 0 ? state.Targets.ToArray() : MonitorSettingsStore.GetEffectiveTargets(monitorSettings, TestUrls);
            currentNetwork = NetworkStatusReader.Capture();
            consecutiveFailures = Math.Max(0, state.ConsecutiveFailures);
            outageConfirmed = state.OutageConfirmed;
            suspectedStart = state.SuspectedStart;
            writer = CreateDurableWriter(csvPath, true);
            if (!String.IsNullOrWhiteSpace(backupCsvPath) && !String.Equals(Path.GetFullPath(backupCsvPath), Path.GetFullPath(csvPath), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupCsvPath));
                backupWriter = CreateDurableWriter(backupCsvPath, true);
            }

            DateTime now = DateTime.Now;
            paused = state.Paused;
            if (paused)
            {
                pauseStart = state.PauseStart == DateTime.MinValue ? state.LastHeartbeat : state.PauseStart;
                WriteMarker("PROCESS_RESTARTED", L.T("程式重新啟動；原監控維持暫停", "Application restarted; monitoring remains paused"));
            }
            else
            {
                DateTime interruption = state.LastHeartbeat > sessionStart && state.LastHeartbeat < now ? state.LastHeartbeat : now;
                pauses.Add(new TimePeriod { Start = interruption, End = now });
                WriteMarkerAt(interruption, "INTERRUPTED", L.T("程式中斷或電腦關機；此區間不列入統計", "Application interruption or computer shutdown; interval excluded"));
                WriteMarkerAt(now, "SESSION_RESUMED", L.T("接續未完成的監控工作", "Unfinished monitoring session resumed"));
            }
            WriteMarker("NETWORK", currentNetwork.ToMarker());
            WriteMarker("POWER_PROTECTION", PowerProtectionMarker(monitorSettings));

            running = true;
            reportPath = null;
            reportButton.Text = L.T("產生即時報表", "Create Live Report");
            reportButton.Enabled = true;
            intervalBox.Enabled = false;
            startButton.Enabled = false;
            settingsButton.Enabled = true;
            pauseButton.Enabled = true;
            stopButton.Enabled = true;
            eventNoteButton.Enabled = true;
            pauseButton.Text = paused ? L.T("繼續", "Resume") : L.T("暫停", "Pause");
            UpdatePowerProtection();
            AddRecent(now, L.T("已接續", "Resumed"), "—", L.T("原監控資料已載入；中斷時段不列入統計", "Previous data loaded; interruption excluded from statistics"), Color.RoyalBlue);
            UpdateState(paused ? L.T("已接續，維持暫停", "Resumed and still paused") : L.T("已接續，準備檢查…", "Resumed; preparing check…"), paused ? Color.Gray : Color.DarkOrange);
            RenderNetworkInfo(currentNetwork);
            SetTrayConnectionState(paused ? TrayConnectionState.Paused : TrayConnectionState.Checking, true);
            PersistSessionState();
            timer = new System.Threading.Timer(delegate { PerformCheck(); }, null, paused ? Timeout.Infinite : 0, Timeout.Infinite);
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
            RenderNetworkInfo(record.Network);
            if (record.Status == "SUSPECTED")
            {
                UpdateState(L.T("疑似斷線，5 秒後快速複查", "Possible outage; fast retry in 5 seconds"), Color.DarkOrange);
                SetTrayConnectionState(TrayConnectionState.Checking, true);
                AddRecent(record.Time, L.T("疑似斷線", "Suspected"), "—", DisplayCheckDetail(record), Color.DarkOrange);
            }
            else if (record.Online)
            {
                string status = record.JustRecovered ? L.T("網路已恢復", "Internet recovered") : L.T("網路正常", "Online");
                UpdateState(status, Color.SeaGreen);
                SetTrayConnectionState(TrayConnectionState.Online, true);
                string rowStatus = record.JustRecovered ? L.T("已恢復", "Recovered") : (record.RetryNumber > 0 ? L.T("複查正常", "Retry online") : L.T("正常", "Online"));
                AddRecent(record.Time, rowStatus, record.LatencyMs + " ms", record.Target + L.T("（", " (") + record.Detail + L.T("）", ")"), Color.SeaGreen);
                if (record.JustRecovered)
                {
                    trayIcon.Visible = true;
                    trayIcon.ShowBalloonTip(5000, L.T("NetCheck 網路已恢復", "NetCheck Internet Recovered"), record.Time.ToString("yyyy/MM/dd HH:mm:ss") + L.T(" 已重新連上外部網路", " Internet connectivity has returned"), ToolTipIcon.Info);
                }
            }
            else
            {
                UpdateState(L.T("已確認斷線，持續追蹤", "Outage confirmed; tracking continues"), Color.Firebrick);
                SetTrayConnectionState(TrayConnectionState.Offline, true);
                AddRecent(record.Time, record.JustConfirmed ? L.T("確認斷線", "Confirmed outage") : L.T("斷線追蹤", "Outage tracking"), "—", DisplayCheckDetail(record), Color.Firebrick);
                if (record.JustConfirmed)
                {
                    trayIcon.Visible = true;
                    trayIcon.ShowBalloonTip(5000, L.T("NetCheck 確認偵測到斷線", "NetCheck Confirmed an Outage"), record.OutageStart.ToString("yyyy/MM/dd HH:mm:ss") + L.T(" 起連續無法連線到外部網路", " consecutive Internet checks failed"), ToolTipIcon.Error);
                }
            }
            lastLabel.Text = L.T("最後檢查：", "Last check: ") + record.Time.ToString("yyyy/MM/dd HH:mm:ss");
            int good = 0, bad = 0;
            lock (records) foreach (var r in records) { if (r.Status == "SUSPECTED") continue; if (r.Online) good++; else bad++; }
            statsLabel.Text = L.T("有效檢查 ", "Checks ") + (good + bad) + L.T(" 次｜正常 ", " | Online ") + good + L.T(" 次｜確認失敗 ", " | Confirmed failures ") + bad + L.T(" 次｜暫停時間不列入統計", " | Paused time excluded");
            if (consecutiveFailures == 1 && !outageConfirmed) statsLabel.Text += L.T("｜快速複查中", " | Fast retry in progress");
            if (!String.IsNullOrEmpty(logWarning)) statsLabel.Text += L.T("｜警告：", " | Warning: ") + logWarning;
            if ((DateTime.Now - lastAutoReport).TotalMinutes >= 10)
            {
                CreateLiveReport(false);
                lastAutoReport = DateTime.Now;
            }
        }

        private static string DisplayCheckDetail(CheckRecord record)
        {
            if (record == null) return "";
            string baseDetail = record.Detail ?? "";
            int marker = baseDetail.IndexOf(" || DIAG|", StringComparison.Ordinal);
            if (marker >= 0) baseDetail = baseDetail.Substring(0, marker);
            return record.Diagnostic == null ? baseDetail : baseDetail + L.T("｜分層診斷：", " | Layered diagnosis: ") + record.Diagnostic.DisplaySummary;
        }

        private void CreateLiveReport(bool open)
        {
            if (!running || String.IsNullOrEmpty(csvPath)) return;
            try
            {
                reportPath = BuildReport(DateTime.Now, true);
                AddRecent(DateTime.Now, L.T("報表", "Report"), "—", L.T("累積即時報表已更新，監控持續進行", "Cumulative live report updated; monitoring continues"), Color.RoyalBlue);
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
            SessionStateStore.Delete();
            ResetOutageTracking();
            UpdatePowerProtection();
            intervalBox.Enabled = true;
            startButton.Enabled = true;
            settingsButton.Enabled = true;
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            eventNoteButton.Enabled = false;
            pauseButton.Text = L.T("暫停", "Pause");
            UpdateState(L.T("監控已結束", "Monitoring stopped"), Color.DimGray);
            SetTrayConnectionState(TrayConnectionState.Idle, false);
            reportPath = BuildReport(DateTime.Now, false);
            reportButton.Text = L.T("開啟累積報表", "Open Cumulative Report");
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

        private void PrepareForSystemRestart()
        {
            if (!running) return;
            PersistSessionState();
            WriteMarker("SYSTEM_SHUTDOWN", L.T("Windows 正在關機；保留狀態供下次接續", "Windows is shutting down; state retained for resume"));
            running = false;
            if (timer != null) { timer.Dispose(); timer = null; }
            CloseWriter(ref writer);
            CloseWriter(ref backupWriter);
            UpdatePowerProtection();
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
            string directory = Path.GetDirectoryName(csvPath);
            string name = "NetCheck_" + SafeFilePart(machineName, 16) + "-" + machineId + (live ? "_Cumulative_Live.html" : "_Cumulative_Report.html");
            return ArchiveReport.WriteCumulativeHtml(Path.Combine(directory, name), live);
        }

        private string BuildSessionReportLegacy(DateTime sessionEnd, bool live)
        {
            string path = live ? Path.Combine(Path.GetDirectoryName(csvPath), Path.GetFileNameWithoutExtension(csvPath) + "_Live.html") : Path.ChangeExtension(csvPath, ".html");
            List<CheckRecord> snapshot;
            lock (records) snapshot = new List<CheckRecord>(records);
            List<EventNote> eventSnapshot;
            lock (eventNotes) eventSnapshot = new List<EventNote>(eventNotes);
            var pauseSnapshot = new List<TimePeriod>();
            foreach (var p in pauses) pauseSnapshot.Add(new TimePeriod { Start = p.Start, End = p.End });
            if (paused) pauseSnapshot.Add(new TimePeriod { Start = pauseStart, End = sessionEnd });
            int good = 0, bad = 0;
            long latencyTotal = 0;
            var latencies = new List<long>();
            foreach (var r in snapshot)
            {
                string status = String.IsNullOrEmpty(r.Status) ? (r.Online ? "ONLINE" : "OFFLINE") : r.Status;
                if (status == "ONLINE") { good++; latencyTotal += r.LatencyMs; latencies.Add(r.LatencyMs); }
                else if (status == "OFFLINE") bad++;
            }
            double availability = good + bad == 0 ? 0 : (100.0 * good / (good + bad));
            long avgLatency = good == 0 ? 0 : latencyTotal / good;
            long maxLatency = latencies.Count == 0 ? 0 : MaxValue(latencies);
            long p95Latency = Percentile95(latencies);
            long jitter = AverageLatencyVariation(latencies);
            TimeSpan pausedTotal = TimeSpan.Zero;
            foreach (var p in pauseSnapshot) pausedTotal += p.End - p.Start;
            TimeSpan effective = (sessionEnd - sessionStart) - pausedTotal;

            var outages = BuildOutages(snapshot, sessionEnd);
            TimeSpan outageTotal = TimeSpan.Zero;
            foreach (var outage in outages) outageTotal += EffectiveDuration(outage.Start, outage.End, pauseSnapshot);
            TimeSpan longestOutage = TimeSpan.Zero, shortestOutage = TimeSpan.Zero;
            foreach (var outage in outages)
            {
                TimeSpan duration = EffectiveDuration(outage.Start, outage.End, pauseSnapshot);
                if (duration > longestOutage) longestOutage = duration;
                if (shortestOutage == TimeSpan.Zero || duration < shortestOutage) shortestOutage = duration;
            }
            TimeSpan averageOutage = outages.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(outageTotal.Ticks / outages.Count);
            double outagePercent = effective.TotalSeconds <= 0 ? 0 : 100.0 * outageTotal.TotalSeconds / effective.TotalSeconds;
            var dailyStats = BuildDailyStats(snapshot, pauseSnapshot, outages, sessionEnd);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='" + L.HtmlLanguage + "'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>" + H(L.T("對外網路連線能力監控報表", "NetCheckMonitor Network Monitoring Report")) + "</title><style>body{font-family:'Microsoft JhengHei UI','Segoe UI',sans-serif;background:#f4f6f8;color:#17202a;margin:0}.wrap{max-width:1100px;margin:auto;padding:28px}.card{background:white;border-radius:12px;padding:20px;margin:14px 0;box-shadow:0 2px 10px #00000012}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.metric{background:#f7f9fb;border-left:5px solid #2e86c1;padding:14px}.metric b{display:block;font-size:24px;margin-top:7px}.bad{color:#b03a2e}.good{color:#1e8449}table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:9px;border-bottom:1px solid #e5e7e9}svg{width:100%;height:42px;background:#eef1f3;border-radius:5px}.legend span{margin-right:18px}.dot{display:inline-block;width:11px;height:11px;margin-right:5px}.foot{color:#657;margin-top:18px;font-size:13px}@media print{body{background:white}.card{box-shadow:none;border:1px solid #ddd}}</style></head><body><div class='wrap'>");
            sb.Append("<h1>" + H(L.T("對外網路連線能力監控報表", "NetCheckMonitor Network Monitoring Report")) + "</h1><div class='card grid'>");
            Metric(sb, L.T("對外連線率", "Internet Availability"), availability.ToString("0.00") + "%", availability >= 99 ? "good" : "bad");
            Metric(sb, L.T("有效檢查", "Valid Checks"), snapshot.Count + L.T(" 次", ""), "");
            Metric(sb, L.T("斷線檢查", "Failed Checks"), bad + L.T(" 次", ""), bad == 0 ? "good" : "bad");
            Metric(sb, L.T("斷線事件", "Outage Events"), outages.Count + L.T(" 次", ""), outages.Count == 0 ? "good" : "bad");
            Metric(sb, L.T("平均回應時間", "Average Latency"), avgLatency + " ms", "");
            Metric(sb, L.T("有效監控時間", "Effective Monitoring"), FormatDuration(effective), "");
            Metric(sb, L.T("估計斷線時間", "Estimated Outage Time"), FormatDuration(outageTotal), outageTotal > TimeSpan.Zero ? "bad" : "good");
            Metric(sb, L.T("時間斷線率", "Time Outage Rate"), outagePercent.ToString("0.00") + "%", outagePercent > 0 ? "bad" : "good");
            Metric(sb, L.T("最長斷線", "Longest Outage"), FormatDuration(longestOutage), longestOutage > TimeSpan.Zero ? "bad" : "good");
            Metric(sb, L.T("平均斷線", "Average Outage"), FormatDuration(averageOutage), averageOutage > TimeSpan.Zero ? "bad" : "good");
            Metric(sb, L.T("最短斷線", "Shortest Outage"), FormatDuration(shortestOutage), shortestOutage > TimeSpan.Zero ? "bad" : "good");
            Metric(sb, L.T("第 95 百分位延遲", "95th Percentile Latency"), p95Latency + " ms", "");
            Metric(sb, L.T("最高延遲", "Maximum Latency"), maxLatency + " ms", "");
            Metric(sb, L.T("平均延遲變動", "Average Latency Variation"), jitter + " ms", "");
            sb.Append("</div><div class='card'><h2>" + H(L.T("監控資訊", "Monitoring Information")) + "</h2><table>");
            Row(sb, L.T("測試電腦名稱", "Computer Name"), machineName);
            Row(sb, L.T("電腦辨識碼", "Computer ID"), machineId + L.T("（", " (") + machineIdSource + L.T("，原始識別資料不寫入報表）", "; the original identifier is not stored in reports)"));
            NetworkSnapshot reportNetwork = currentNetwork ?? NetworkStatusReader.Capture();
            Row(sb, L.T("目前網卡", "Current Network Adapter"), reportNetwork.AdapterDisplay);
            Row(sb, L.T("連線類型", "Connection Type"), reportNetwork.TypeDisplay);
            Row(sb, L.T("Wi-Fi 訊號", "Wi-Fi Signal"), reportNetwork.SignalDisplay);
            Row(sb, L.T("進階分層診斷（目前設定）", "Advanced Layered Diagnostics (current setting)"), monitorSettings != null && monitorSettings.AdvancedDiagnosticsEnabled ? L.T("已啟用；僅在 HTTPS 失敗後執行", "Enabled; runs only after an HTTPS failure") : L.T("未啟用；斷線統計仍照常記錄", "Disabled; outage statistics are still recorded normally"));
            Row(sb, L.T("開始", "Started"), sessionStart.ToString("yyyy/MM/dd HH:mm:ss"));
            Row(sb, L.T("結束", "Ended"), sessionEnd.ToString("yyyy/MM/dd HH:mm:ss"));
            Row(sb, L.T("暫停總時間（已排除）", "Total Paused Time (excluded)"), FormatDuration(pausedTotal));
            Row(sb, L.T("原始紀錄", "Raw Log"), Path.GetFileName(csvPath));
            if (!String.IsNullOrEmpty(backupCsvPath)) Row(sb, L.T("當機備援紀錄", "Crash-Recovery Log"), backupCsvPath);
            sb.Append("</table></div>");
            AppendEventNotes(sb, eventSnapshot);
            sb.Append("<div class='card'><h2>" + H(L.T("每日斷線統計", "Daily Outage Statistics")) + "</h2><table><thead><tr><th>" + H(L.T("日期", "Date")) + "</th><th>" + H(L.T("有效監控時間", "Effective Monitoring")) + "</th><th>" + H(L.T("估計斷線時間", "Estimated Outage")) + "</th><th>" + H(L.T("每日斷線百分比", "Daily Outage Percentage")) + "</th><th>" + H(L.T("斷線事件", "Outage Events")) + "</th><th>" + H(L.T("最長斷線", "Longest Outage")) + "</th><th>" + H(L.T("確認失敗檢查", "Confirmed Failed Checks")) + "</th></tr></thead><tbody>");
            foreach (var d in dailyStats)
            {
                string cls = d.Outage > TimeSpan.Zero ? "bad" : "good";
                sb.Append("<tr><td>" + d.Day.ToString("yyyy/MM/dd") + "</td><td>" + H(FormatDuration(d.Effective)) + "</td><td class='" + cls + "'>" + H(FormatDuration(d.Outage)) + "</td><td class='" + cls + "'>" + d.OutagePercent.ToString("0.00") + "%</td><td>" + d.OutageEvents + L.T(" 次", "") + "</td><td>" + H(FormatDuration(d.LongestOutage)) + "</td><td>" + d.OfflineChecks + L.T(" 次", "") + "</td></tr>");
            }
            sb.Append("</tbody></table></div>");
            AppendDiagnosticTable(sb, snapshot);
            sb.Append("<div class='card'><h2>" + H(L.T("每日連線時間軸", "Daily Connection Timeline")) + "</h2><p class='legend'><span><i class='dot' style='background:#28a745'></i>" + H(L.T("正常", "Online")) + "</span><span><i class='dot' style='background:#f39c12'></i>" + H(L.T("疑似斷線", "Suspected")) + "</span><span><i class='dot' style='background:#dc3545'></i>" + H(L.T("確認斷線", "Confirmed outage")) + "</span><span><i class='dot' style='background:#9aa0a6'></i>" + H(L.T("暫停／程式中斷", "Paused / interrupted")) + "</span></p>");
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
            sb.Append("</div><p class='foot'>" + H(live ? L.T("這是監控進行中的即時快照，產生報表不會中斷檢查。", "This is a live snapshot. Creating it does not interrupt monitoring.") : L.T("這是監控結束後的最終報表。", "This is the final report after monitoring stopped.")) + " " + H(L.T("判定方式：首次失敗會在 5 秒後快速複查，連續失敗才確認為斷線；長時間斷線會自動降低複查頻率，但檢查間隔絕不會超過主畫面設定的週期。每日斷線百分比＝確認斷線時間 ÷ 有效監控時間；暫停、程式中斷與電腦關機區段會排除。進階診斷的開關不改變斷線判定或統計；啟用時只為 HTTPS 失敗加入分層證據，未啟用或啟用前的失敗會標示為未執行進階診斷。", "Method: the first failure triggers a fast retry after 5 seconds, and only consecutive failures confirm an outage. During a prolonged outage, the retry frequency is reduced automatically, but the interval never exceeds the period configured on the main screen. Daily outage percentage equals confirmed outage time divided by effective monitoring time. Paused, interrupted, and powered-off periods are excluded. The advanced diagnostics setting does not change outage detection or statistics; when enabled, it only adds layered evidence after HTTPS failures. Failures recorded while disabled or before it was enabled are marked as not diagnosed.")) + "</p></div></body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static void AppendDiagnosticTable(StringBuilder sb, List<CheckRecord> records)
        {
            sb.Append("<div class='card'><h2>" + H(L.T("進階分層連線診斷", "Advanced Layered Connectivity Diagnostics")) + "</h2>");
            var failures = new List<CheckRecord>();
            foreach (CheckRecord record in records) if (!record.Online) failures.Add(record);
            if (failures.Count == 0) sb.Append("<p class='good'>" + H(L.T("沒有失敗檢查需要診斷。", "No failed checks required diagnostics.")) + "</p>");
            else
            {
                sb.Append("<table><tr><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("狀態", "Status")) + "</th><th>" + H(L.T("診斷標示", "Diagnostic Finding")) + "</th><th>" + H(L.T("分層證據", "Layer Evidence")) + "</th></tr>");
                foreach (CheckRecord record in failures)
                {
                    string finding = AdvancedDiagnosticResult.FindingsFromLog(record.Detail);
                    if (String.IsNullOrEmpty(finding)) finding = L.T("未執行進階診斷", "Advanced diagnostics not performed");
                    sb.Append("<tr><td>" + record.Time.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + H(record.Status) + "</td><td>" + H(finding) + "</td><td>" + H(AdvancedDiagnosticResult.EvidenceFromLog(record.Detail)) + "</td></tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }

        private static void AppendEventNotes(StringBuilder sb, List<EventNote> notes)
        {
            sb.Append("<div class='card'><h2>" + H(L.T("事件註記", "Event Notes")) + "</h2>");
            if (notes == null || notes.Count == 0) sb.Append("<p>" + H(L.T("沒有手動事件註記。", "No manual event notes.")) + "</p>");
            else
            {
                notes.Sort(delegate (EventNote a, EventNote b) { return a.Time.CompareTo(b.Time); });
                sb.Append("<table><tr><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("事件或處理內容", "Event or Action")) + "</th></tr>");
                foreach (EventNote note in notes) sb.Append("<tr><td>" + note.Time.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td>" + H(note.Text) + "</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }

        private sealed class Outage { public DateTime Start; public DateTime End; public int Count; }
        private sealed class DailyStat
        {
            public DateTime Day;
            public TimeSpan Effective;
            public TimeSpan Outage;
            public double OutagePercent;
            public int OfflineChecks;
            public int OutageEvents;
            public TimeSpan LongestOutage;
        }

        private static List<Outage> BuildOutages(List<CheckRecord> list, DateTime end)
        {
            var result = new List<Outage>();
            Outage current = null;
            DateTime suspected = DateTime.MinValue;
            foreach (var r in list)
            {
                string status = String.IsNullOrEmpty(r.Status) ? (r.Online ? "ONLINE" : "OFFLINE") : r.Status;
                if (status == "SUSPECTED")
                {
                    if (suspected == DateTime.MinValue) suspected = r.Time;
                }
                else if (status == "OFFLINE")
                {
                    if (current == null) current = new Outage { Start = suspected == DateTime.MinValue ? r.Time : suspected, End = r.Time, Count = suspected == DateTime.MinValue ? 0 : 1 };
                    current.End = r.Time;
                    current.Count++;
                }
                else if (status == "ONLINE")
                {
                    if (current != null)
                    {
                        current.End = r.Time;
                        result.Add(current);
                        current = null;
                    }
                    suspected = DateTime.MinValue;
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
                TimeSpan longest = TimeSpan.Zero;
                int outageEvents = 0;
                foreach (var outage in outages)
                {
                    DateTime a = outage.Start > rangeStart ? outage.Start : rangeStart;
                    DateTime b = outage.End < rangeEnd ? outage.End : rangeEnd;
                    if (b > a)
                    {
                        TimeSpan duration = EffectiveDuration(a, b, pauseList);
                        outageTime += duration;
                        outageEvents++;
                        if (duration > longest) longest = duration;
                    }
                }
                int offlineChecks = 0;
                foreach (var record in list)
                {
                    string status = String.IsNullOrEmpty(record.Status) ? (record.Online ? "ONLINE" : "OFFLINE") : record.Status;
                    if (status == "OFFLINE" && record.Time >= rangeStart && record.Time < rangeEnd) offlineChecks++;
                }
                double percent = effective.TotalSeconds <= 0 ? 0 : 100.0 * outageTime.TotalSeconds / effective.TotalSeconds;
                result.Add(new DailyStat { Day = day, Effective = effective, Outage = outageTime, OutagePercent = percent, OfflineChecks = offlineChecks, OutageEvents = outageEvents, LongestOutage = longest });
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
                    string status = String.IsNullOrEmpty(r.Status) ? (r.Online ? "ONLINE" : "OFFLINE") : r.Status;
                    string color = status == "ONLINE" ? "#28a745" : (status == "SUSPECTED" ? "#f39c12" : "#dc3545");
                    string label = status == "ONLINE" ? L.T("正常", "Online") : (status == "SUSPECTED" ? L.T("疑似斷線", "Suspected") : L.T("確認斷線", "Confirmed outage"));
                    sb.Append("<rect x='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' y='0' width='1.6' height='42' fill='" + color + "'><title>" + H(r.Time.ToString("HH:mm:ss") + " " + label) + "</title></rect>");
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
            string status = String.IsNullOrEmpty(r.Status) ? (r.Online ? "ONLINE" : "OFFLINE") : r.Status;
            WriteLogLine(Csv(r.Time.ToString("o")) + ",CHECK," + status + "," + (r.Online ? r.LatencyMs.ToString() : "") + "," + Csv(r.Target) + "," + Csv(r.Detail));
        }

        private void WriteMarker(string status, string detail)
        {
            WriteMarkerAt(DateTime.Now, status, detail);
        }

        private void WriteMarkerAt(DateTime time, string status, string detail)
        {
            WriteLogLine(Csv(time.ToString("o")) + ",MARKER," + status + ",,," + Csv(detail));
        }

        private static StreamWriter CreateDurableWriter(string path, bool append = false)
        {
            var stream = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            return new StreamWriter(stream, new UTF8Encoding(true));
        }

        private static string AllocateSessionFileStem(string directory, string baseStem)
        {
            string candidate = baseStem;
            int suffix = 2;
            while (File.Exists(Path.Combine(directory, candidate + ".csv")) || File.Exists(Path.Combine(directory, candidate + ".html")))
            {
                candidate = baseStem + "_" + suffix.ToString("00");
                suffix++;
            }
            return candidate;
        }

        private void LoadSessionHistory(string path)
        {
            bool pauseOpen = false;
            DateTime openPause = DateTime.MinValue;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    List<string> fields = ParseCsvLine(line);
                    if (fields.Count < 6 || fields[0] == "Timestamp") continue;
                    DateTime time;
                    if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) continue;
                    if (fields[1] == "CHECK")
                    {
                        long latency;
                        Int64.TryParse(fields[3], out latency);
                        string status = fields[2];
                        records.Add(new CheckRecord { Time = time, Online = status == "ONLINE", Status = status, LatencyMs = latency, Target = fields[4], Detail = fields[5] });
                    }
                    else if (fields[1] == "MARKER")
                    {
                        string status = fields[2];
                        if (status == "EVENT_NOTE") eventNotes.Add(new EventNote { Time = time, Text = fields[5] });
                        else if (status == "PAUSED" || status == "INTERRUPTED") { pauseOpen = true; openPause = time; }
                        else if ((status == "RESUMED" || status == "SESSION_RESUMED") && pauseOpen)
                        {
                            if (time > openPause) pauses.Add(new TimePeriod { Start = openPause, End = time });
                            pauseOpen = false;
                        }
                    }
                }
            }
            records.Sort(delegate (CheckRecord a, CheckRecord b) { return a.Time.CompareTo(b.Time); });
            eventNotes.Sort(delegate (EventNote a, EventNote b) { return a.Time.CompareTo(b.Time); });
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Length = 0; }
                else field.Append(c);
            }
            fields.Add(field.ToString());
            return fields;
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

        private static long MaxValue(List<long> values)
        {
            long max = 0;
            foreach (long value in values) if (value > max) max = value;
            return max;
        }

        private static long Percentile95(List<long> values)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<long>(values);
            sorted.Sort();
            int index = Math.Max(0, (int)Math.Ceiling(sorted.Count * 0.95) - 1);
            return sorted[index];
        }

        private static long AverageLatencyVariation(List<long> values)
        {
            if (values.Count < 2) return 0;
            long total = 0;
            for (int i = 1; i < values.Count; i++) total += Math.Abs(values[i] - values[i - 1]);
            return total / (values.Count - 1);
        }

        private void AddRecent(DateTime time, string status, string latency, string detail, Color color)
        {
            var item = new ListViewItem(new string[] { time.ToString("yyyy/MM/dd HH:mm:ss"), status, latency, detail });
            item.ForeColor = color;
            recentList.Items.Insert(0, item);
            while (recentList.Items.Count > 300) recentList.Items.RemoveAt(recentList.Items.Count - 1);
        }

        private void UpdateState(string text, Color color) { stateLabel.Text = text; stateLabel.ForeColor = color; }
        private void RenderNetworkInfo(NetworkSnapshot snapshot)
        {
            if (snapshot == null) snapshot = NetworkStatusReader.Capture();
            networkInfoLabel.Text = snapshot.UiText;
            networkInfoLabel.ForeColor = snapshot.TypeCode == "Disconnected" ? Color.Firebrick : Color.DimGray;
        }
        private static Icon CreateStatusIcon(Color color)
        {
            using (var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var border = new Pen(Color.FromArgb(210, 35, 35, 35), 2F))
            using (var fill = new SolidBrush(color))
            using (var highlight = new SolidBrush(Color.FromArgb(185, Color.White)))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(fill, 4, 4, 24, 24);
                graphics.DrawEllipse(border, 4, 4, 24, 24);
                graphics.FillEllipse(highlight, 9, 8, 7, 7);
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private void SetTrayConnectionState(TrayConnectionState state, bool visible)
        {
            Icon icon = neutralTrayIcon;
            string text = L.T("NetCheckMonitor：尚未監控", "NetCheckMonitor: Not monitoring");
            if (state == TrayConnectionState.Checking) { icon = checkingTrayIcon; text = L.T("NetCheckMonitor：正在檢查連線", "NetCheckMonitor: Checking connection"); }
            else if (state == TrayConnectionState.Online) { icon = onlineTrayIcon; text = L.T("NetCheckMonitor：對外連線正常", "NetCheckMonitor: Internet connection online"); }
            else if (state == TrayConnectionState.Offline) { icon = offlineTrayIcon; text = L.T("NetCheckMonitor：確認對外連線中斷", "NetCheckMonitor: Internet connection offline"); }
            else if (state == TrayConnectionState.Paused) { icon = neutralTrayIcon; text = L.T("NetCheckMonitor：監控已暫停", "NetCheckMonitor: Monitoring paused"); }
            trayIcon.Icon = icon;
            trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            trayIcon.Visible = visible;
        }

        private static Icon LoadApplicationIcon() { try { Icon icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); return icon ?? SystemIcons.Application; } catch { return SystemIcons.Application; } }
        private static string PowerProtectionMarker(MonitorTargetSettings settings)
        {
            return "PreventSleep=" + (settings != null && settings.PreventSleepWhileMonitoring ? "1" : "0")
                + ";PreventShutdown=" + (settings != null && settings.PreventShutdownWhileMonitoring ? "1" : "0");
        }

        private bool ShouldBlockWindowsShutdown()
        {
            return running && monitorSettings != null && monitorSettings.PreventShutdownWhileMonitoring;
        }

        private void UpdatePowerProtection()
        {
            bool preventSleep = running && monitorSettings != null && monitorSettings.PreventSleepWhileMonitoring;
            SetThreadExecutionState(preventSleep ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
            bool blockShutdown = ShouldBlockWindowsShutdown();
            if (blockShutdown && IsHandleCreated && !shutdownBlockReasonActive)
            {
                shutdownBlockReasonActive = ShutdownBlockReasonCreate(Handle, L.T("NetCheckMonitor 正在進行長時間網路監控；請先停止監控再關機。", "NetCheckMonitor is running a long-term network test. Stop monitoring before shutting down."));
            }
            else if (!blockShutdown) ReleaseShutdownBlockReason();
        }

        private void ReleaseShutdownBlockReason()
        {
            if (!shutdownBlockReasonActive) return;
            try { if (IsHandleCreated) ShutdownBlockReasonDestroy(Handle); }
            finally { shutdownBlockReasonActive = false; }
        }
        private void OpenReport() { if (!String.IsNullOrEmpty(reportPath) && File.Exists(reportPath)) Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true }); }
        private void HideToTray()
        {
            Hide();
            trayIcon.Visible = true;
            trayIcon.ShowBalloonTip(2000,
                L.T("NetCheckMonitor 已縮小", "NetCheckMonitor Minimized"),
                running ? L.T("視窗已縮到系統匣，監控不會中斷。", "The window was minimized to the system tray. Monitoring continues.") : L.T("程式仍在系統匣中執行。", "The app is still running in the system tray."),
                ToolTipIcon.Info);
        }
        private void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); if (!running) trayIcon.Visible = false; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_QUERYENDSESSION && ShouldBlockWindowsShutdown())
            {
                PersistSessionState();
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == SingleInstance.ShowWindowMessage)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref message);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !allowExit)
            {
                e.Cancel = true;
                if (UiPreferenceStore.ShouldShowCloseToTrayNotice())
                {
                    try { UiPreferenceStore.MarkCloseToTrayNoticeShown(); } catch { }
                    MessageBox.Show(
                        L.T("按下右上角 X 只會將程式縮小到系統匣，監控仍可在背景執行。\n\n若要確認資料已儲存並結束程式，請按主畫面右下角的「關閉程式」按鈕。\n\n此訊息只會顯示一次。", "Selecting the X button only minimizes the app to the system tray, and monitoring can continue in the background.\n\nTo verify that data is saved and exit the app, use the Exit button in the lower-right corner of the main window.\n\nThis message is shown only once."),
                        L.T("NetCheckMonitor 關閉提醒", "NetCheckMonitor Close Reminder"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                HideToTray();
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing && cloudManager != null && cloudManager.BackupInProgress)
            {
                e.Cancel = true;
                allowExit = false;
                MessageBox.Show(L.T("Google Drive 雲端備份仍在進行，請等待備份完成後再關閉程式。", "A Google Drive backup is still in progress. Wait for it to finish before closing the program."), "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (running && e.CloseReason == CloseReason.WindowsShutDown) PrepareForSystemRestart();
            else if (running) StopMonitoring(false);
            if (cloudManager != null) cloudManager.Dispose();
            ReleaseShutdownBlockReason();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (neutralTrayIcon != null) neutralTrayIcon.Dispose();
            if (checkingTrayIcon != null) checkingTrayIcon.Dispose();
            if (onlineTrayIcon != null) onlineTrayIcon.Dispose();
            if (offlineTrayIcon != null) offlineTrayIcon.Dispose();
        }
    }

    internal sealed class AboutForm : Form
    {
        internal const string AppVersion = "0.9.7";
        internal const string Purpose = "可定時監控對外網路連線，紀錄斷線並產生圖文報表，並支援網路硬碟備份，PDF 下載，程式完全免費開源無廣告。";
        internal const string EnglishPurpose = "Scheduled monitoring of external Internet connectivity, outage logging, graphical reports, cloud-drive backup, and PDF downloads. Completely free, open source, and ad-free.";
        private const string GitHubProjectUrl = "https://github.com/ahui3c/NetCheckMonitor";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/ahui3c/NetCheckMonitor/releases/latest";
        private readonly Button checkVersionButton = new Button();
        private readonly Label versionStatusLabel = new Label();

        public AboutForm()
        {
            Text = L.T("關於 NetCheckMonitor", "About NetCheckMonitor");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(540, 355);
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
            var websiteTitle = new Label { Text = L.T("網站：", "Website:"), AutoSize = true, Location = new Point(29, 218) };
            var website = new LinkLabel { Text = "https://ahui3c.com", AutoSize = true, Location = new Point(92, 218), LinkArea = new LinkArea(0, "https://ahui3c.com".Length) };
            var githubTitle = new Label { Text = L.T("GitHub 專案：", "GitHub project:"), AutoSize = true, Location = new Point(29, 245) };
            var github = new LinkLabel { Text = GitHubProjectUrl, AutoSize = true, Location = new Point(137, 245), LinkArea = new LinkArea(0, GitHubProjectUrl.Length) };
            versionStatusLabel.Text = L.T("按下按鈕時才會連線至 GitHub 檢查。", "GitHub is contacted only when you select the button.");
            versionStatusLabel.AutoEllipsis = true;
            versionStatusLabel.ForeColor = Color.DimGray;
            versionStatusLabel.SetBounds(29, 276, 485, 22);
            checkVersionButton.Text = L.T("檢查新版本", "Check for Updates");
            checkVersionButton.SetBounds(29, 307, 145, 34);
            var close = new Button { Text = L.T("關閉", "Close"), Location = new Point(394, 307), Size = new Size(120, 34) };

            email.LinkClicked += delegate { Process.Start(new ProcessStartInfo("mailto:chehui@gmail.com") { UseShellExecute = true }); };
            website.LinkClicked += delegate { Process.Start(new ProcessStartInfo("https://ahui3c.com") { UseShellExecute = true }); };
            github.LinkClicked += delegate { Process.Start(new ProcessStartInfo(GitHubProjectUrl) { UseShellExecute = true }); };
            checkVersionButton.Click += delegate { CheckForUpdates(); };
            close.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { title, version, purpose, authorTitle, author, email, websiteTitle, website, githubTitle, github, versionStatusLabel, checkVersionButton, close });
        }

        private void CheckForUpdates()
        {
            checkVersionButton.Enabled = false;
            versionStatusLabel.Text = L.T("正在向 GitHub 檢查最新版本…", "Checking the latest GitHub release…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string latestTag = null, releaseUrl = null, error = null;
                try
                {
                    ReadLatestRelease(out latestTag, out releaseUrl);
                }
                catch (Exception ex) { error = ex.Message; }

                if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate
                {
                    checkVersionButton.Enabled = true;
                    if (error != null)
                    {
                        versionStatusLabel.Text = L.T("檢查失敗：", "Update check failed: ") + error;
                        MessageBox.Show(versionStatusLabel.Text, L.T("檢查新版本", "Check for Updates"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (IsNewerVersion(latestTag))
                    {
                        versionStatusLabel.Text = L.T("發現新版本：", "New version available: ") + latestTag;
                        string message = L.T("GitHub 上有新版本 ", "A newer version is available on GitHub: ") + latestTag
                            + L.T("。\n\n是否開啟 Releases 頁面？程式不會自動下載或安裝。", "\n\nOpen the Releases page? The app will not download or install anything automatically.");
                        if (MessageBox.Show(message, L.T("發現新版本", "Update Available"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo(String.IsNullOrWhiteSpace(releaseUrl) ? GitHubProjectUrl + "/releases" : releaseUrl) { UseShellExecute = true });
                    }
                    else
                    {
                        versionStatusLabel.Text = L.T("目前已是最新版本（", "You are using the latest version (") + AppVersion + L.T("）", ")");
                        MessageBox.Show(versionStatusLabel.Text, L.T("檢查新版本", "Check for Updates"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            });
        }

        private static void ReadLatestRelease(out string latestTag, out string releaseUrl)
        {
            latestTag = null;
            releaseUrl = null;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
            request.Method = "GET";
            request.UserAgent = "NetCheckMonitor/" + AppVersion;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var data = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                object value;
                if (data.TryGetValue("tag_name", out value)) latestTag = Convert.ToString(value);
                if (data.TryGetValue("html_url", out value)) releaseUrl = Convert.ToString(value);
            }
            if (String.IsNullOrWhiteSpace(latestTag)) throw new InvalidDataException(L.T("GitHub 回傳資料中沒有版本號。", "The GitHub response did not contain a version tag."));
        }

        private static bool IsNewerVersion(string tag)
        {
            string value = (tag ?? "").Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int suffix = value.IndexOf('-');
            if (suffix >= 0) value = value.Substring(0, suffix);
            Version latest, current;
            return Version.TryParse(value, out latest) && Version.TryParse(AppVersion, out current) && latest > current;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Mutex instanceMutex;
            if (!SingleInstance.TryAcquire(out instanceMutex))
            {
                SingleInstance.ShowExistingWindow();
                return;
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (LanguagePreferenceStore.Load() == null)
                {
                    using (var languageForm = new LanguageSelectionForm())
                    {
                        if (languageForm.ShowDialog() != DialogResult.OK) return;
                        try { LanguagePreferenceStore.Save(languageForm.SelectedLanguage); }
                        catch (Exception ex)
                        {
                            MessageBox.Show("無法儲存語言設定，程式無法繼續。\n\nCould not save the language setting, so the app cannot continue.\n\n" + ex.Message, "NetCheckMonitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }
                ApplicationRecovery.Register();
                Application.Run(new MainForm());
            }
            finally
            {
                try { instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
                instanceMutex.Dispose();
            }
        }
    }

}
