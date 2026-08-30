using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetCheckViewer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                string output = args.Length > 1 ? args[1] : "";
                string message;
                Environment.ExitCode = ViewerSelfTest.Run(output, out message) ? 0 : 1;
                return;
            }
            if (args.Length > 1 && String.Equals(args[0], "--render-test", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RenderTest(args[1]) ? 0 : 1;
                return;
            }
            if (args.Length > 1 && String.Equals(args[0], "--render-intro", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RenderIntro(args[1]) ? 0 : 1;
                return;
            }
            if (args.Length > 2 && String.Equals(args[0], "--render-folder", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RenderFolder(args[1], args[2]) ? 0 : 1;
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ViewerForm(args.Length > 0 ? args[0] : null));
        }

        private static bool RenderTest(string outputPath)
        {
            string root = Path.Combine(Path.GetTempPath(), "NetCheckViewerRender_" + Guid.NewGuid().ToString("N"));
            try { ViewerSelfTest.CreateDemoData(root); return RenderFolder(root, outputPath); }
            catch (Exception ex) { try { File.WriteAllText(outputPath + ".error.txt", ex.ToString(), Encoding.UTF8); } catch { } return false; }
            finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
        }

        private static bool RenderIntro(string outputPath)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new ViewerIntroForm(false))
                {
                    form.Show(); Application.DoEvents(); form.Refresh(); Application.DoEvents();
                    using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                        bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                }
                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            }
            catch (Exception ex) { try { File.WriteAllText(outputPath + ".error.txt", ex.ToString(), Encoding.UTF8); } catch { } return false; }
        }

        private static bool RenderFolder(string root, string outputPath)
        {
            try
            {
                if (!Directory.Exists(root)) return false;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new ViewerForm(root, true))
                {
                    form.Show();
                    DateTime deadline = DateTime.Now.AddSeconds(30);
                    while (!form.DataLoaded && DateTime.Now < deadline) { Application.DoEvents(); Thread.Sleep(50); }
                    if (!form.DataLoaded) return false;
                    form.Refresh(); Application.DoEvents();
                    using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                        bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                }
                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            }
            catch (Exception ex) { try { File.WriteAllText(outputPath + ".error.txt", ex.ToString(), Encoding.UTF8); } catch { } return false; }
        }
    }

    internal sealed class ViewerForm : Form
    {
        private readonly Color Ink = Color.FromArgb(27, 49, 72);
        private readonly Color Muted = Color.FromArgb(91, 112, 132);
        private readonly Color Blue = Color.FromArgb(28, 119, 206);
        private readonly Color Green = Color.FromArgb(35, 157, 97);
        private readonly Color Amber = Color.FromArgb(220, 146, 37);
        private readonly Color Red = Color.FromArgb(205, 74, 69);
        private readonly TextBox folderBox = new TextBox();
        private readonly Button browseButton = new Button();
        private readonly Button scanButton = new Button();
        private readonly Button openFolderButton = new Button();
        private readonly Button remoteSettingsButton = new Button();
        private readonly ComboBox filterBox = new ComboBox();
        private readonly Label scanStatus = new Label();
        private readonly LinkLabel helpLink = new LinkLabel();
        private readonly Label totalValue = new Label();
        private readonly Label normalValue = new Label();
        private readonly Label delayedValue = new Label();
        private readonly Label availabilityValue = new Label();
        private readonly DataGridView machineGrid = NewGrid();
        private readonly DataGridView alertGrid = NewGrid();
        private readonly DataGridView dailyGrid = NewGrid();
        private readonly DataGridView outageGrid = NewGrid();
        private readonly DataGridView speedGrid = NewGrid();
        private readonly DataGridView fileGrid = NewGrid();
        private readonly Label selectionTitle = new Label();
        private readonly Label selectionDetail = new Label();
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer watcherDebounceTimer = new System.Windows.Forms.Timer();
        private readonly CheckBox showHandledCheck = new CheckBox();
        private readonly Button alertSettingsButton = new Button();
        private readonly Label alertCountLabel = new Label();
        private readonly TrendDashboard trendDashboard = new TrendDashboard();
        private ViewerSettings settings;
        private ScanResult current;
        private List<ViewerAlert> currentAlerts = new List<ViewerAlert>();
        private FileSystemWatcher folderWatcher;
        private DateTime lastFullReconciliationUtc = DateTime.MinValue;
        private bool watcherRequiresFullReconciliation;
        private bool scanning;

        internal bool DataLoaded { get { return current != null && !scanning; } }

        internal ViewerForm(string initialPath, bool suppressIntro = false)
        {
            Text = "NetCheck Viewer｜多電腦監控資料中心";
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            BackColor = Color.FromArgb(246, 250, 252);
            ForeColor = Ink;
            MinimumSize = new Size(1180, 760);
            ClientSize = new Size(1440, 940);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            settings = SettingsStore.Load();
            if (!String.IsNullOrWhiteSpace(initialPath)) settings.BackupRoot = initialPath;
            BuildUi();
            folderBox.Text = settings.BackupRoot ?? "";
            Shown += delegate
            {
                if (!suppressIntro && !settings.IntroDismissed) ShowViewerIntro();
                string rememberedPath = folderBox.Text.Trim();
                if (Directory.Exists(rememberedPath)) StartScan(true, false);
                else ShowMissingFolderReminder(rememberedPath);
            };
            refreshTimer.Interval = 5 * 60 * 1000;
            refreshTimer.Tick += delegate
            {
                if (scanning || !Directory.Exists(folderBox.Text)) return;
                bool due = watcherRequiresFullReconciliation || lastFullReconciliationUtc == DateTime.MinValue
                    || DateTime.UtcNow - lastFullReconciliationUtc >= TimeSpan.FromMinutes(Math.Max(10, settings.FullReconcileMinutes));
                StartScan(false, due);
            };
            refreshTimer.Start();
            watcherDebounceTimer.Interval = 1500;
            watcherDebounceTimer.Tick += delegate
            {
                watcherDebounceTimer.Stop();
                if (scanning) { watcherDebounceTimer.Start(); return; }
                if (Directory.Exists(folderBox.Text)) StartScan(false, watcherRequiresFullReconciliation);
            };
            FormClosed += delegate { refreshTimer.Stop(); watcherDebounceTimer.Stop(); DisposeWatcher(); };
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            Controls.Add(root);

            var head = new Panel { Dock = DockStyle.Fill };
            var title = new Label { Text = "NetCheck Viewer", Font = new Font(Font.FontFamily, 25F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 2), ForeColor = Ink };
            var subtitle = new Label { Text = "集中檢視所有電腦回傳的監控、斷線、測速與備份狀態", AutoSize = true, Location = new Point(3, 46), ForeColor = Muted };
            scanStatus.AutoSize = false; scanStatus.TextAlign = ContentAlignment.MiddleRight; scanStatus.Size = new Size(430, 28); scanStatus.Location = new Point(Math.Max(400, head.ClientSize.Width - 430), 39); scanStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right; scanStatus.ForeColor = Muted;
            helpLink.Text = "使用說明"; helpLink.AutoSize = true; helpLink.LinkColor = Blue; helpLink.ActiveLinkColor = Ink; helpLink.Font = new Font(Font, FontStyle.Bold); helpLink.Location = new Point(Math.Max(420, head.ClientSize.Width - 505), 8); helpLink.Anchor = AnchorStyles.Top | AnchorStyles.Right; helpLink.Click += delegate { ShowViewerIntro(); };
            head.Controls.Add(title); head.Controls.Add(subtitle); head.Controls.Add(scanStatus); head.Controls.Add(helpLink); helpLink.BringToFront();
            root.Controls.Add(head, 0, 0);

            var source = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, Padding = new Padding(0, 8, 0, 8) };
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            source.Controls.Add(new Label { Text = "備份資料夾", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            folderBox.Dock = DockStyle.Fill; folderBox.Margin = new Padding(0, 4, 8, 4);
            browseButton.Text = "選擇資料夾…"; StyleButton(browseButton, false); browseButton.Click += delegate { BrowseFolder(); };
            scanButton.Text = "完整校正"; StyleButton(scanButton, true); scanButton.Click += delegate { StartScan(true, true); };
            openFolderButton.Text = "開啟資料夾"; StyleButton(openFolderButton, false); openFolderButton.Click += delegate { OpenFolder(); };
            filterBox.Dock = DockStyle.Fill; filterBox.DropDownStyle = ComboBoxStyle.DropDownList; filterBox.Margin = new Padding(8, 4, 0, 4);
            filterBox.Items.AddRange(new object[] { "所有電腦", "回傳正常", "回傳延遲", "長時間未回傳" }); filterBox.SelectedIndex = 0; filterBox.SelectedIndexChanged += delegate { BindMachines(); };
            source.Controls.Add(folderBox, 1, 0); source.Controls.Add(browseButton, 2, 0); source.Controls.Add(scanButton, 3, 0); source.Controls.Add(openFolderButton, 4, 0); source.Controls.Add(filterBox, 5, 0);
            root.Controls.Add(source, 0, 1);

            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 3, 0, 9) };
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.Controls.Add(MetricCard("電腦總數", totalValue, Blue), 0, 0);
            cards.Controls.Add(MetricCard("準時回傳", normalValue, Green), 1, 0);
            cards.Controls.Add(MetricCard("需要注意", delayedValue, Amber), 2, 0);
            cards.Controls.Add(MetricCard("平均連線率", availabilityValue, Blue), 3, 0);
            root.Controls.Add(cards, 0, 2);

            ConfigureAlertGrid();
            root.Controls.Add(BuildAlertSection(), 0, 3);

            ConfigureMachineGrid();
            machineGrid.SelectionChanged += delegate { BindSelectedMachine(); };
            root.Controls.Add(WrapSection("多電腦總覽", machineGrid), 0, 4);

            var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            lower.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var selected = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 8, 2, 6) };
            selectionTitle.AutoSize = true; selectionTitle.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold); selectionTitle.Location = new Point(0, 8);
            selectionDetail.AutoSize = true; selectionDetail.ForeColor = Muted; selectionDetail.Location = new Point(0, 35);
            remoteSettingsButton.Text = "修改遠端設定…"; StyleButton(remoteSettingsButton, false); remoteSettingsButton.Dock = DockStyle.Right; remoteSettingsButton.Width = 170; remoteSettingsButton.Enabled = false; remoteSettingsButton.Click += delegate { OpenRemoteSettings(); };
            selected.Controls.Add(remoteSettingsButton); selected.Controls.Add(selectionTitle); selected.Controls.Add(selectionDetail); lower.Controls.Add(selected, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Tab("趨勢分析", trendDashboard)); tabs.TabPages.Add(Tab("每日歷史", dailyGrid)); tabs.TabPages.Add(Tab("斷線事件", outageGrid)); tabs.TabPages.Add(Tab("定時測速", speedGrid)); tabs.TabPages.Add(Tab("來源檔案", fileGrid));
            ConfigureDetailGrids(); lower.Controls.Add(tabs, 0, 1); root.Controls.Add(lower, 0, 5);
        }

        private void ConfigureAlertGrid()
        {
            AddText(alertGrid, "等級", "Severity", 70);
            AddText(alertGrid, "電腦", "Machine", 145);
            AddText(alertGrid, "類型", "Type", 92);
            AddText(alertGrid, "需要處理", "Title", 220);
            var detail = AddText(alertGrid, "說明", "Detail", 420); detail.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddText(alertGrid, "偵測時間", "Detected", 140);
            var action = new DataGridViewButtonColumn { HeaderText = "處理狀態", Name = "Action", Width = 92, FlatStyle = FlatStyle.Flat, UseColumnTextForButtonValue = false };
            alertGrid.Columns.Add(action);
            alertGrid.CellFormatting += delegate (object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0) return;
                ViewerAlert alert = alertGrid.Rows[e.RowIndex].Tag as ViewerAlert;
                if (alert == null) return;
                if (alert.Acknowledged) { e.CellStyle.ForeColor = Muted; e.CellStyle.BackColor = Color.FromArgb(247, 249, 250); return; }
                if (alertGrid.Columns[e.ColumnIndex].Name == "Severity")
                {
                    e.CellStyle.ForeColor = alert.SeverityRank == 3 ? Red : alert.SeverityRank == 2 ? Amber : Blue;
                    e.CellStyle.Font = new Font(alertGrid.Font, FontStyle.Bold);
                }
            };
            alertGrid.CellContentClick += delegate (object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || alertGrid.Columns[e.ColumnIndex].Name != "Action") return;
                ViewerAlert alert = alertGrid.Rows[e.RowIndex].Tag as ViewerAlert;
                if (alert == null) return;
                AlertCenter.SetAcknowledged(alert.Key, !alert.Acknowledged);
                alert.Acknowledged = !alert.Acknowledged;
                BindAlerts();
            };
        }

        private Control BuildAlertSection()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 42, 12, 10) };
            panel.Controls.Add(alertGrid);
            var title = new Label { Text = "需要處理", AutoSize = true, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), Location = new Point(13, 11), ForeColor = Ink };
            alertCountLabel.AutoSize = true; alertCountLabel.Location = new Point(105, 14); alertCountLabel.ForeColor = Muted;
            showHandledCheck.Text = "顯示已標記"; showHandledCheck.AutoSize = true; showHandledCheck.Anchor = AnchorStyles.Top | AnchorStyles.Right; showHandledCheck.Location = new Point(panel.Width - 265, 13); showHandledCheck.CheckedChanged += delegate { BindAlerts(); };
            alertSettingsButton.Text = "門檻設定…"; alertSettingsButton.Size = new Size(112, 28); alertSettingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; alertSettingsButton.Location = new Point(panel.Width - 125, 6); StyleButton(alertSettingsButton, false); alertSettingsButton.Dock = DockStyle.None; alertSettingsButton.Click += delegate { OpenAlertSettings(); };
            panel.Resize += delegate { showHandledCheck.Left = Math.Max(400, panel.ClientSize.Width - 265); alertSettingsButton.Left = Math.Max(520, panel.ClientSize.Width - 125); };
            panel.Controls.Add(title); panel.Controls.Add(alertCountLabel); panel.Controls.Add(showHandledCheck); panel.Controls.Add(alertSettingsButton);
            return panel;
        }

        private void ConfigureMachineGrid()
        {
            AddText(machineGrid, "狀態", "ReturnState", 112);
            AddText(machineGrid, "電腦名稱", "MachineName", 150);
            AddText(machineGrid, "識別碼", "MachineId", 92);
            AddText(machineGrid, "最後回傳", "LastBackup", 145);
            AddText(machineGrid, "最後資料", "LastData", 145);
            AddText(machineGrid, "最後連線", "Connection", 96);
            AddText(machineGrid, "連線率", "Availability", 82);
            AddText(machineGrid, "斷線事件", "Outages", 76);
            AddText(machineGrid, "平均延遲", "Latency", 86);
            AddText(machineGrid, "最近測速", "Speed", 155);
            var analysis = AddText(machineGrid, "分析", "Analysis", 250); analysis.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            machineGrid.CellFormatting += delegate (object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || machineGrid.Columns[e.ColumnIndex].Name != "ReturnState") return;
                string value = Convert.ToString(e.Value);
                e.CellStyle.ForeColor = value == "回傳正常" ? Green : value == "回傳延遲" ? Amber : Red;
                e.CellStyle.Font = new Font(machineGrid.Font, FontStyle.Bold);
            };
        }

        private void ConfigureDetailGrids()
        {
            AddText(dailyGrid, "日期", "Day", 105); AddText(dailyGrid, "檢查", "Checks", 70); AddText(dailyGrid, "正常", "Online", 70); AddText(dailyGrid, "確認失敗", "Offline", 82); AddText(dailyGrid, "疑似", "Suspected", 70); AddText(dailyGrid, "連線率", "Availability", 90); AddText(dailyGrid, "平均延遲", "Latency", 92); AddText(dailyGrid, "斷線事件", "Outages", 82); AddText(dailyGrid, "估計斷線", "Duration", 120); AddText(dailyGrid, "最長斷線", "Longest", 120);
            AddText(outageGrid, "開始時間", "Start", 150); AddText(outageGrid, "恢復／最後紀錄", "End", 150); AddText(outageGrid, "持續時間", "Duration", 130); AddText(outageGrid, "確認失敗", "Failures", 90); AddText(outageGrid, "結果", "Recovered", 100);
            AddText(speedGrid, "測速時間", "Time", 150); AddText(speedGrid, "狀態", "Status", 90); AddText(speedGrid, "模式", "Mode", 80); AddText(speedGrid, "等級", "Level", 90); AddText(speedGrid, "下載", "Download", 110); AddText(speedGrid, "上傳", "Upload", 110); AddText(speedGrid, "延遲／抖動", "Latency", 130); var network = AddText(speedGrid, "網路", "Network", 250); network.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddText(fileGrid, "類型", "Kind", 100); AddText(fileGrid, "檔案名稱", "FileName", 330); AddText(fileGrid, "修改時間", "LastWrite", 150); AddText(fileGrid, "資料列", "Rows", 80); AddText(fileGrid, "大小", "Size", 90); var path = AddText(fileGrid, "路徑", "Path", 300); path.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            fileGrid.CellDoubleClick += delegate (object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenSelectedFile(); };
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "選擇 Net_Check 備份根目錄或包含多台電腦報表的資料夾", ShowNewFolderButton = false, SelectedPath = Directory.Exists(folderBox.Text) ? folderBox.Text : "" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                folderBox.Text = dialog.SelectedPath;
                settings.BackupRoot = dialog.SelectedPath;
                SettingsStore.Save(settings);
                StartScan(true, false);
            }
        }

        private void StartScan(bool notifyIfNoData, bool forceFull)
        {
            string path = folderBox.Text.Trim();
            if (!Directory.Exists(path)) { MessageBox.Show(this, "請先選擇存在的備份資料夾。", "NetCheck Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (scanning) return;
            ConfigureWatcher(path);
            scanning = true; SetBusy(true); scanStatus.Text = forceFull ? "正在執行完整校正…" : "正在更新異動的備份資料…";
            settings.BackupRoot = path; SettingsStore.Save(settings);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ScanResult result = IncrementalScanEngine.Analyze(path, settings, forceFull);
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    current = result; scanning = false; SetBusy(false); BindAll();
                    if (result.FullReconciliation) { lastFullReconciliationUtc = DateTime.UtcNow; watcherRequiresFullReconciliation = false; }
                    scanStatus.Text = (result.FullReconciliation ? "完整校正" : "增量更新") + " " + result.ScannedAt.ToString("MM/dd HH:mm:ss")
                        + "｜解析 " + result.ParsedFileCount + "／快取 " + result.ReusedFileCount + "｜" + result.ScanMilliseconds.ToString("N0") + " ms"
                        + (result.Issues.Count > 0 ? "｜" + result.Issues.Count + " 個提醒" : "");
                    if (notifyIfNoData && !ViewerDataState.HasUsableData(result))
                        MessageBox.Show(this, "這個資料夾內尚未找到可讀取的 NetCheck 備份資料。\r\n\r\n請確認 Google Drive 已完成同步，或按「選擇資料夾」改用其他備份位置。", "尚無備份資料", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            });
        }

        private void ConfigureWatcher(string path)
        {
            if (folderWatcher != null && String.Equals(folderWatcher.Path, path, StringComparison.OrdinalIgnoreCase)) return;
            DisposeWatcher();
            try
            {
                folderWatcher = new FileSystemWatcher(path) { IncludeSubdirectories = true, Filter = "*.*", NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                folderWatcher.Changed += WatcherChanged; folderWatcher.Created += WatcherChanged; folderWatcher.Deleted += WatcherChanged; folderWatcher.Renamed += WatcherChanged;
                folderWatcher.Error += delegate { watcherRequiresFullReconciliation = true; QueueWatcherRefresh(); };
                folderWatcher.EnableRaisingEvents = true;
            }
            catch { watcherRequiresFullReconciliation = true; }
        }

        private void WatcherChanged(object sender, FileSystemEventArgs e)
        {
            string extension = Path.GetExtension(e.FullPath);
            if (!String.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(Path.GetFileName(e.FullPath), ViewerControlClient.FileName, StringComparison.OrdinalIgnoreCase)) return;
            QueueWatcherRefresh();
        }

        private void QueueWatcherRefresh()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { watcherDebounceTimer.Stop(); watcherDebounceTimer.Start(); }); } catch { }
        }

        private void DisposeWatcher()
        {
            if (folderWatcher == null) return;
            try { folderWatcher.EnableRaisingEvents = false; folderWatcher.Dispose(); } catch { }
            folderWatcher = null;
        }

        private void ShowMissingFolderReminder(string rememberedPath)
        {
            string message = String.IsNullOrWhiteSpace(rememberedPath)
                ? "尚未設定備份資料夾，請按「選擇資料夾」指定 NetCheck 備份位置。"
                : "上次使用的備份資料夾目前不存在或無法存取：\r\n" + rememberedPath + "\r\n\r\n請按「選擇資料夾」重新指定位置。";
            MessageBox.Show(this, message, "需要備份資料夾", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowViewerIntro()
        {
            using (var form = new ViewerIntroForm(settings.IntroDismissed))
            {
                form.ShowDialog(this);
                if (settings.IntroDismissed == form.DoNotShowAgain) return;
                settings.IntroDismissed = form.DoNotShowAgain;
                SettingsStore.Save(settings);
            }
        }

        private void BindAll()
        {
            int total = current == null ? 0 : current.Machines.Count;
            int normal = current == null ? 0 : current.Machines.Count(delegate (MachineSummary m) { return m.ReturnState == "回傳正常"; });
            int attention = current == null ? 0 : current.Machines.Count(delegate (MachineSummary m) { return m.ReturnState != "回傳正常"; });
            double availability = current == null || current.Machines.Count == 0 ? 0 : current.Machines.Where(delegate (MachineSummary m) { return m.Checks > 0; }).Select(delegate (MachineSummary m) { return m.AvailabilityPercent; }).DefaultIfEmpty(0).Average();
            totalValue.Text = total.ToString(); normalValue.Text = normal.ToString(); delayedValue.Text = attention.ToString(); availabilityValue.Text = availability.ToString("0.00") + "%";
            currentAlerts = AlertCenter.Build(current, settings, folderBox.Text.Trim());
            BindAlerts();
            trendDashboard.Bind(current, SelectedMachineId());
            BindMachines();
        }

        private void BindAlerts()
        {
            alertGrid.Rows.Clear();
            IEnumerable<ViewerAlert> alerts = currentAlerts ?? Enumerable.Empty<ViewerAlert>();
            if (!showHandledCheck.Checked) alerts = alerts.Where(delegate (ViewerAlert value) { return !value.Acknowledged; });
            List<ViewerAlert> visible = alerts.ToList();
            int open = currentAlerts == null ? 0 : currentAlerts.Count(delegate (ViewerAlert value) { return !value.Acknowledged; });
            int serious = currentAlerts == null ? 0 : currentAlerts.Count(delegate (ViewerAlert value) { return !value.Acknowledged && value.SeverityRank == 3; });
            alertCountLabel.Text = open == 0 ? "目前沒有未處理異常" : open + " 項未處理" + (serious > 0 ? "，其中 " + serious + " 項嚴重" : "");
            alertCountLabel.ForeColor = serious > 0 ? Red : open > 0 ? Amber : Green;
            foreach (ViewerAlert alert in visible)
            {
                int row = alertGrid.Rows.Add(alert.Severity, String.IsNullOrWhiteSpace(alert.MachineName) ? "所有電腦" : alert.MachineName, alert.Type, alert.Title, alert.Detail, alert.DetectedAt.ToString("yyyy/MM/dd HH:mm"), alert.Acknowledged ? "取消標記" : "標記完成");
                alertGrid.Rows[row].Tag = alert;
            }
        }

        private void BindMachines()
        {
            string selectedId = SelectedMachineId();
            machineGrid.Rows.Clear();
            if (current == null) return;
            string filter = Convert.ToString(filterBox.SelectedItem);
            foreach (MachineSummary machine in current.Machines)
            {
                if (filter != "所有電腦" && machine.ReturnState != filter) continue;
                int row = machineGrid.Rows.Add(machine.ReturnState, machine.MachineName, machine.MachineId, DateText(machine.LastBackupTime), DateText(machine.LastDataTime), ConnectionText(machine.LastConnectionStatus), machine.Checks == 0 ? "—" : machine.AvailabilityPercent.ToString("0.00") + "%", machine.OutageCount, machine.Checks == 0 ? "—" : machine.AverageLatencyMs.ToString("0") + " ms", machine.LatestSpeedTime == DateTime.MinValue ? "—" : "↓" + machine.LatestDownloadMbps.ToString("0.0") + " / ↑" + machine.LatestUploadMbps.ToString("0.0") + " Mbps", machine.Analysis);
                machineGrid.Rows[row].Tag = machine.MachineId;
            }
            if (machineGrid.Rows.Count > 0)
            {
                int index = 0;
                for (int i = 0; i < machineGrid.Rows.Count; i++) if (String.Equals(Convert.ToString(machineGrid.Rows[i].Tag), selectedId, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
                machineGrid.ClearSelection(); machineGrid.Rows[index].Selected = true; machineGrid.CurrentCell = machineGrid.Rows[index].Cells[0];
            }
            else BindSelectedMachine();
        }

        private void BindSelectedMachine()
        {
            string id = SelectedMachineId();
            MachineSummary machine = current == null ? null : current.Machines.FirstOrDefault(delegate (MachineSummary m) { return String.Equals(m.MachineId, id, StringComparison.OrdinalIgnoreCase); });
            dailyGrid.Rows.Clear(); outageGrid.Rows.Clear(); speedGrid.Rows.Clear(); fileGrid.Rows.Clear();
            if (machine == null) { selectionTitle.Text = "尚未選擇電腦"; selectionDetail.Text = "選擇上方任一台電腦即可檢視詳細歷史。"; remoteSettingsButton.Enabled = false; remoteSettingsButton.Tag = null; return; }
            selectionTitle.Text = machine.MachineName + "  [" + machine.MachineId + "]";
            selectionDetail.Text = machine.ReturnState + "｜" + machine.Analysis + "｜最後回傳 " + DateText(machine.LastBackupTime) + (String.IsNullOrWhiteSpace(machine.FolderPath) ? "" : "｜" + machine.FolderPath);
            trendDashboard.SelectMachine(machine.MachineId);
            string controlPath = ViewerControlClient.FindControlFile(machine, folderBox.Text.Trim());
            remoteSettingsButton.Tag = controlPath; remoteSettingsButton.Enabled = true;
            remoteSettingsButton.Text = !String.IsNullOrWhiteSpace(controlPath) ? "修改遠端設定…" : "尚未支援遠端設定";
            foreach (DailySummary day in current.Days.Where(delegate (DailySummary d) { return String.Equals(d.MachineId, id, StringComparison.OrdinalIgnoreCase); }))
                dailyGrid.Rows.Add(day.Day.ToString("yyyy/MM/dd"), day.Checks, day.Online, day.Offline, day.Suspected, day.Checks == 0 ? "—" : day.AvailabilityPercent.ToString("0.00") + "%", day.Online == 0 ? "—" : day.AverageLatencyMs.ToString("0") + " ms", day.OutageCount, Duration(day.OutageDuration), Duration(day.LongestOutage));
            foreach (OutageEvent outage in current.Outages.Where(delegate (OutageEvent o) { return String.Equals(o.MachineId, id, StringComparison.OrdinalIgnoreCase); }))
                outageGrid.Rows.Add(outage.Start.ToString("yyyy/MM/dd HH:mm:ss"), outage.End.ToString("yyyy/MM/dd HH:mm:ss"), Duration(outage.Duration), outage.ConfirmedFailures, outage.Recovered ? "已恢復" : "尚無恢復紀錄");
            foreach (SpeedRecord speed in current.Speeds.Where(delegate (SpeedRecord s) { return String.Equals(s.MachineId, id, StringComparison.OrdinalIgnoreCase); }))
                speedGrid.Rows.Add(speed.Time.ToString("yyyy/MM/dd HH:mm:ss"), SpeedStatus(speed.Status), speed.Mode == "Scheduled" ? "定時" : "手動", speed.Level, speed.Status == "COMPLETED" ? speed.DownloadMbps.ToString("0.0") + " Mbps" : "—", speed.Status == "COMPLETED" ? speed.UploadMbps.ToString("0.0") + " Mbps" : "—", speed.Status == "COMPLETED" ? speed.LatencyMs.ToString("0.0") + " / " + speed.JitterMs.ToString("0.0") + " ms" : "—", String.IsNullOrWhiteSpace(speed.Error) ? speed.Network : speed.Error);
            foreach (SourceFileInfo file in current.Files.Where(delegate (SourceFileInfo f) { return String.Equals(f.MachineId, id, StringComparison.OrdinalIgnoreCase); }).OrderByDescending(delegate (SourceFileInfo f) { return f.LastWriteTime; }))
            {
                int row = fileGrid.Rows.Add(file.Kind, file.FileName, file.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"), file.ParsedRows, FileSize(file.SizeBytes), file.FullPath);
                fileGrid.Rows[row].Tag = file.FullPath;
            }
        }

        private void OpenRemoteSettings()
        {
            string path = Convert.ToString(remoteSettingsButton.Tag);
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(this, "這台電腦尚未建立 Viewer 控制設定檔。請先將 NetCheckMonitor 更新到支援版本、確認 Google Drive 備份已連接，並等待完成一次同步。", "尚未支援遠端設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                ViewerControlDocument document = ViewerControlClient.Load(path);
                using (var form = new RemoteSettingsForm(path, document))
                    if (form.ShowDialog(this) == DialogResult.OK) StartScan(false, false);
            }
            catch (Exception ex) { MessageBox.Show(this, "無法讀取遠端設定：" + ex.Message, "遠端設定", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenAlertSettings()
        {
            using (var form = new AlertSettingsForm(settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                form.ApplyTo(settings);
                SettingsStore.Save(settings);
                StartScan(false, true);
            }
        }
        private void OpenFolder()
        {
            string path = folderBox.Text.Trim();
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        private void OpenSelectedFile()
        {
            if (fileGrid.CurrentRow == null) return;
            string path = Convert.ToString(fileGrid.CurrentRow.Tag);
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void SetBusy(bool busy) { scanButton.Enabled = browseButton.Enabled = openFolderButton.Enabled = filterBox.Enabled = alertSettingsButton.Enabled = !busy; scanButton.Text = busy ? "掃描中…" : "完整校正"; }
        private string SelectedMachineId() { return machineGrid.CurrentRow == null ? "" : Convert.ToString(machineGrid.CurrentRow.Tag); }

        private Panel MetricCard(string caption, Label value, Color accent)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(16) };
            panel.Paint += delegate (object sender, PaintEventArgs e) { using (var brush = new SolidBrush(accent)) e.Graphics.FillRectangle(brush, 0, 0, 5, panel.Height); };
            var label = new Label { Text = caption, AutoSize = true, ForeColor = Muted, Location = new Point(18, 10) };
            value.AutoSize = true; value.Text = "—"; value.Font = new Font(Font.FontFamily, 17F, FontStyle.Bold); value.ForeColor = accent; value.Location = new Point(17, 31);
            panel.Controls.Add(label); panel.Controls.Add(value); return panel;
        }

        private Control WrapSection(string title, Control body)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 34, 12, 10) };
            panel.Controls.Add(body); body.Dock = DockStyle.Fill;
            panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Location = new Point(13, 10), ForeColor = Ink });
            return panel;
        }

        private static TabPage Tab(string title, Control control) { var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(8) }; page.Controls.Add(control); control.Dock = DockStyle.Fill; return page; }
        private void StyleButton(Button button, bool primary) { button.Dock = DockStyle.Fill; button.Margin = new Padding(4); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(198, 211, 220); button.BackColor = primary ? Blue : Color.White; button.ForeColor = primary ? Color.White : Ink; button.Font = new Font(Font, FontStyle.Bold); }

        private static DataGridView NewGrid()
        {
            var grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, ReadOnly = true, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoGenerateColumns = false, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 34, RowTemplate = { Height = 30 } };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 243, 248); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(27, 49, 72); grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold); grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 238, 250); grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(27, 49, 72); grid.GridColor = Color.FromArgb(228, 235, 240);
            return grid;
        }

        private static DataGridViewTextBoxColumn AddText(DataGridView grid, string header, string name, int width) { var column = new DataGridViewTextBoxColumn { HeaderText = header, Name = name, Width = width, SortMode = DataGridViewColumnSortMode.Automatic }; grid.Columns.Add(column); return column; }
        private static string DateText(DateTime value) { return value == DateTime.MinValue ? "—" : value.ToString("yyyy/MM/dd HH:mm:ss"); }
        private static string ConnectionText(string value) { return value == "ONLINE" ? "正常" : value == "OFFLINE" ? "確認斷線" : value == "SUSPECTED" ? "疑似異常" : "—"; }
        private static string SpeedStatus(string value) { return value == "COMPLETED" ? "完成" : value == "SKIPPED" ? "略過" : value == "CANCELLED" ? "取消" : "失敗"; }
        private static string Duration(TimeSpan value) { if (value.TotalDays >= 1) return ((int)value.TotalDays) + " 天 " + value.Hours + " 小時"; if (value.TotalHours >= 1) return ((int)value.TotalHours) + " 小時 " + value.Minutes + " 分"; if (value.TotalMinutes >= 1) return ((int)value.TotalMinutes) + " 分 " + value.Seconds + " 秒"; return Math.Max(0, (int)value.TotalSeconds) + " 秒"; }
        private static string FileSize(long value) { if (value >= 1048576) return (value / 1048576.0).ToString("0.0") + " MB"; if (value >= 1024) return (value / 1024.0).ToString("0.0") + " KB"; return value + " B"; }
    }

    internal static class ViewerIntroContent
    {
        internal const string Positioning = "NetCheck Viewer 是 NetCheckMonitor 的獨立集中分析工具，不是即時主從控制系統。Viewer 不會直接連線、登入或遙控其他電腦，而是讀取已同步到本機的備份資料。";
        internal const string Usage = "請讓每台受監控電腦使用 NetCheckMonitor 登入同一個 Google 帳號並開啟 Google Drive 備份。各電腦的報表會分別存入 Net_Check／電腦名稱資料夾；中央電腦再透過 Google Drive 電腦版同步同一個 Net_Check 資料夾，交由 Viewer 統一分析。";
        internal const string Capabilities = "Viewer 可集中檢查多台電腦的最後回傳時間、連線率、斷線事件、測速、趨勢與異常。也能透過同步控制檔，非同步調整監控間隔與每日備份時間。設定何時生效取決於 Google Drive 同步及監控程式下一次檢查，不是即時遠端操作。";
    }

    internal sealed class ViewerIntroForm : Form
    {
        private readonly CheckBox doNotShowAgain = new CheckBox();
        internal bool DoNotShowAgain { get { return doNotShowAgain.Checked; } }

        internal ViewerIntroForm(bool dismissed)
        {
            Text = "NetCheck Viewer 使用說明";
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            BackColor = Color.FromArgb(242, 248, 252);
            ForeColor = Color.FromArgb(27, 49, 72);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(760, 630);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var hero = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 119, 206), Padding = new Padding(20, 12, 20, 10), Margin = new Padding(0, 0, 0, 10) };
            hero.Controls.Add(new Label { Text = "集中分析，不是即時遙控", AutoSize = true, Font = new Font(Font.FontFamily, 20F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(18, 10) });
            hero.Controls.Add(new Label { Text = "第一次使用前，先了解 Viewer 如何取得多台電腦的資料", AutoSize = true, Font = new Font(Font.FontFamily, 10F), ForeColor = Color.FromArgb(220, 239, 253), Location = new Point(21, 52) });
            root.Controls.Add(hero, 0, 0);

            var flow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(0, 7, 0, 8), Margin = new Padding(0) };
            flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6));
            flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6)); flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            flow.Controls.Add(FlowNode("多台監控電腦", "各自產生報表與記錄", Color.FromArgb(35, 157, 97)), 0, 0);
            flow.Controls.Add(FlowArrow(), 1, 0);
            flow.Controls.Add(FlowNode("同一 Google Drive", "分電腦資料夾同步", Color.FromArgb(28, 119, 206)), 2, 0);
            flow.Controls.Add(FlowArrow(), 3, 0);
            flow.Controls.Add(FlowNode("NetCheck Viewer", "中央檢視與趨勢分析", Color.FromArgb(165, 83, 182)), 4, 0);
            root.Controls.Add(flow, 0, 1);

            root.Controls.Add(InfoCard("Viewer 的定位", ViewerIntroContent.Positioning, Color.FromArgb(229, 241, 250)), 0, 2);

            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0), Margin = new Padding(0) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.Controls.Add(InfoCard("開始使用", ViewerIntroContent.Usage, Color.FromArgb(234, 247, 240)), 0, 0);
            Control capability = InfoCard("可以做什麼", ViewerIntroContent.Capabilities, Color.FromArgb(247, 239, 250)); capability.Margin = new Padding(6, 0, 0, 0);
            details.Controls.Add(capability, 1, 0); root.Controls.Add(details, 0, 3);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(3, 14, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            doNotShowAgain.Text = "下次啟動時不再自動顯示此說明"; doNotShowAgain.AutoSize = true; doNotShowAgain.Checked = dismissed; doNotShowAgain.Margin = new Padding(0, 11, 0, 0);
            var close = new Button { Text = "了解，開始使用", DialogResult = DialogResult.OK, Dock = DockStyle.Fill, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(28, 119, 206), ForeColor = Color.White, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(8, 0, 0, 0) };
            close.FlatAppearance.BorderColor = Color.FromArgb(28, 119, 206);
            footer.Controls.Add(doNotShowAgain, 0, 0); footer.Controls.Add(close, 1, 0); root.Controls.Add(footer, 0, 4);
            AcceptButton = close;
        }

        private static Control FlowNode(string title, string detail, Color accent)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 0) };
            panel.Paint += delegate (object sender, PaintEventArgs e) { using (var brush = new SolidBrush(accent)) e.Graphics.FillRectangle(brush, 0, 0, panel.Width, 5); };
            panel.Controls.Add(new Label { Text = title, AutoSize = false, Dock = DockStyle.Top, Height = 31, TextAlign = ContentAlignment.BottomCenter, Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(27, 49, 72) });
            panel.Controls.Add(new Label { Text = detail, AutoSize = false, Dock = DockStyle.Bottom, Height = 31, TextAlign = ContentAlignment.TopCenter, Font = new Font("Microsoft JhengHei UI", 8.5F), ForeColor = Color.FromArgb(91, 112, 132) });
            return panel;
        }

        private static Control FlowArrow()
        {
            return new Label { Text = "→", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Symbol", 18F, FontStyle.Bold), ForeColor = Color.FromArgb(91, 112, 132) };
        }

        private static Control InfoCard(string title, string content, Color background)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = background, Padding = new Padding(16, 12, 16, 10), Margin = new Padding(0, 5, 0, 5) };
            panel.Controls.Add(new Label { Text = content, AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(0, 31, 0, 0), Font = new Font("Microsoft JhengHei UI", 9.2F), ForeColor = Color.FromArgb(50, 70, 89) });
            panel.Controls.Add(new Label { Text = title, AutoSize = false, Dock = DockStyle.Top, Height = 28, Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(27, 49, 72) });
            return panel;
        }
    }

    internal sealed class AlertSettingsForm : Form
    {
        private readonly NumericUpDown normalHours = Number(1, 720, 36, 1);
        private readonly NumericUpDown seriousHours = Number(2, 1440, 72, 1);
        private readonly NumericUpDown availability = Number(1, 100, 99, 0.1M);
        private readonly NumericUpDown speedFailures = Number(1, 20, 2, 1);
        private readonly NumericUpDown pendingHours = Number(1, 168, 1, 1);
        private readonly NumericUpDown reconcileMinutes = Number(10, 1440, 60, 10);

        internal AlertSettingsForm(ViewerSettings settings)
        {
            Text = "異常與掃描門檻";
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(520, 390);
            normalHours.Value = Clamp(settings.NormalReturnHours, normalHours);
            seriousHours.Value = Clamp(settings.WarningReturnHours, seriousHours);
            availability.Value = Clamp((decimal)settings.AvailabilityThreshold24Hours, availability);
            speedFailures.Value = Clamp(settings.SpeedFailureThreshold, speedFailures);
            pendingHours.Value = Clamp(settings.ControlPendingHours, pendingHours);
            reconcileMinutes.Value = Clamp(settings.FullReconcileMinutes, reconcileMinutes);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(22) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            for (int i = 1; i <= 6; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var heading = new Label { Text = "需要處理的判定門檻", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            root.Controls.Add(heading, 0, 0); root.SetColumnSpan(heading, 2);
            AddRow(root, 1, "備份超過多久列為注意", normalHours, "小時");
            AddRow(root, 2, "備份超過多久列為嚴重", seriousHours, "小時");
            AddRow(root, 3, "24 小時連線率最低門檻", availability, "%");
            AddRow(root, 4, "定時測速連續失敗門檻", speedFailures, "次");
            AddRow(root, 5, "Viewer 設定等待套用門檻", pendingHours, "小時");
            AddRow(root, 6, "背景完整校正間隔", reconcileMinutes, "分鐘");
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
            var ok = new Button { Text = "儲存", DialogResult = DialogResult.OK, Width = 100, Height = 34 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 100, Height = 34 };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 7); root.SetColumnSpan(buttons, 2);
            Controls.Add(root); AcceptButton = ok; CancelButton = cancel;
        }

        internal void ApplyTo(ViewerSettings settings)
        {
            settings.NormalReturnHours = (int)normalHours.Value;
            settings.WarningReturnHours = Math.Max(settings.NormalReturnHours + 1, (int)seriousHours.Value);
            settings.AvailabilityThreshold24Hours = (double)availability.Value;
            settings.SpeedFailureThreshold = (int)speedFailures.Value;
            settings.ControlPendingHours = (int)pendingHours.Value;
            settings.FullReconcileMinutes = (int)reconcileMinutes.Value;
        }

        private static void AddRow(TableLayoutPanel root, int row, string text, NumericUpDown number, string suffix)
        {
            root.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            number.Width = 105; number.Margin = new Padding(0, 5, 8, 0); panel.Controls.Add(number);
            panel.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }); root.Controls.Add(panel, 1, row);
        }

        private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, decimal increment)
        {
            return new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value, Increment = increment, DecimalPlaces = increment < 1 ? 1 : 0, ThousandsSeparator = true };
        }

        private static decimal Clamp(decimal value, NumericUpDown control) { return Math.Max(control.Minimum, Math.Min(control.Maximum, value)); }
    }

    internal static class ViewerDataState
    {
        internal static bool HasUsableData(ScanResult result)
        {
            return result != null && result.Machines.Count > 0;
        }
    }

    internal static class SettingsStore
    {
        private static string SettingsPath()
        {
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetCheck_Viewer.settings.json");
            try { using (FileStream stream = File.Open(portable + ".write-test", FileMode.Create, FileAccess.Write, FileShare.None)) { } File.Delete(portable + ".write-test"); return portable; }
            catch { string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck_Viewer", "settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)); return path; }
        }

        internal static ViewerSettings Load()
        {
            return LoadFrom(SettingsPath());
        }

        internal static void Save(ViewerSettings value)
        {
            SaveTo(SettingsPath(), value);
        }

        internal static ViewerSettings LoadFrom(string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return ViewerSettings.Defaults();
                ViewerSettings value = new JavaScriptSerializer().Deserialize<ViewerSettings>(File.ReadAllText(path, Encoding.UTF8));
                return Normalize(value ?? ViewerSettings.Defaults());
            }
            catch { return ViewerSettings.Defaults(); }
        }

        private static ViewerSettings Normalize(ViewerSettings value)
        {
            ViewerSettings defaults = ViewerSettings.Defaults();
            if (value.NormalReturnHours <= 0) value.NormalReturnHours = defaults.NormalReturnHours;
            if (value.WarningReturnHours <= value.NormalReturnHours) value.WarningReturnHours = Math.Max(defaults.WarningReturnHours, value.NormalReturnHours + 1);
            if (value.AvailabilityThreshold24Hours <= 0 || value.AvailabilityThreshold24Hours > 100) value.AvailabilityThreshold24Hours = defaults.AvailabilityThreshold24Hours;
            if (value.SpeedFailureThreshold <= 0) value.SpeedFailureThreshold = defaults.SpeedFailureThreshold;
            if (value.ControlPendingHours <= 0) value.ControlPendingHours = defaults.ControlPendingHours;
            if (value.FullReconcileMinutes < 10) value.FullReconcileMinutes = defaults.FullReconcileMinutes;
            return value;
        }

        internal static void SaveTo(string path, ViewerSettings value)
        {
            if (String.IsNullOrWhiteSpace(path) || value == null) return;
            try
            {
                string directory = Path.GetDirectoryName(path); if (!String.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
