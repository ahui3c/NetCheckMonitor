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

        private static bool RenderFolder(string root, string outputPath)
        {
            try
            {
                if (!Directory.Exists(root)) return false;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new ViewerForm(root))
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
        private readonly Label totalValue = new Label();
        private readonly Label normalValue = new Label();
        private readonly Label delayedValue = new Label();
        private readonly Label availabilityValue = new Label();
        private readonly DataGridView machineGrid = NewGrid();
        private readonly DataGridView dailyGrid = NewGrid();
        private readonly DataGridView outageGrid = NewGrid();
        private readonly DataGridView speedGrid = NewGrid();
        private readonly DataGridView fileGrid = NewGrid();
        private readonly Label selectionTitle = new Label();
        private readonly Label selectionDetail = new Label();
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private ViewerSettings settings;
        private ScanResult current;
        private bool scanning;

        internal bool DataLoaded { get { return current != null && !scanning; } }

        internal ViewerForm(string initialPath)
        {
            Text = "NetCheck Viewer｜多電腦監控資料中心";
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            BackColor = Color.FromArgb(246, 250, 252);
            ForeColor = Ink;
            MinimumSize = new Size(1120, 720);
            ClientSize = new Size(1380, 880);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            settings = SettingsStore.Load();
            if (!String.IsNullOrWhiteSpace(initialPath)) settings.BackupRoot = initialPath;
            BuildUi();
            folderBox.Text = settings.BackupRoot ?? "";
            Shown += delegate { if (Directory.Exists(folderBox.Text)) StartScan(); };
            refreshTimer.Interval = 5 * 60 * 1000;
            refreshTimer.Tick += delegate { if (!scanning && Directory.Exists(folderBox.Text)) StartScan(); };
            refreshTimer.Start();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            Controls.Add(root);

            var head = new Panel { Dock = DockStyle.Fill };
            var title = new Label { Text = "NetCheck Viewer", Font = new Font(Font.FontFamily, 25F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 2), ForeColor = Ink };
            var subtitle = new Label { Text = "集中檢視所有電腦回傳的監控、斷線、測速與備份狀態", AutoSize = true, Location = new Point(3, 46), ForeColor = Muted };
            scanStatus.AutoSize = false; scanStatus.TextAlign = ContentAlignment.MiddleRight; scanStatus.Dock = DockStyle.Right; scanStatus.Width = 430; scanStatus.ForeColor = Muted;
            head.Controls.Add(title); head.Controls.Add(subtitle); head.Controls.Add(scanStatus);
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
            scanButton.Text = "重新掃描"; StyleButton(scanButton, true); scanButton.Click += delegate { StartScan(); };
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

            ConfigureMachineGrid();
            machineGrid.SelectionChanged += delegate { BindSelectedMachine(); };
            root.Controls.Add(WrapSection("多電腦總覽", machineGrid), 0, 3);

            var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            lower.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var selected = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 8, 2, 6) };
            selectionTitle.AutoSize = true; selectionTitle.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold); selectionTitle.Location = new Point(0, 8);
            selectionDetail.AutoSize = true; selectionDetail.ForeColor = Muted; selectionDetail.Location = new Point(0, 35);
            remoteSettingsButton.Text = "修改遠端設定…"; StyleButton(remoteSettingsButton, false); remoteSettingsButton.Dock = DockStyle.Right; remoteSettingsButton.Width = 170; remoteSettingsButton.Enabled = false; remoteSettingsButton.Click += delegate { OpenRemoteSettings(); };
            selected.Controls.Add(remoteSettingsButton); selected.Controls.Add(selectionTitle); selected.Controls.Add(selectionDetail); lower.Controls.Add(selected, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Tab("每日歷史", dailyGrid)); tabs.TabPages.Add(Tab("斷線事件", outageGrid)); tabs.TabPages.Add(Tab("定時測速", speedGrid)); tabs.TabPages.Add(Tab("來源檔案", fileGrid));
            ConfigureDetailGrids(); lower.Controls.Add(tabs, 0, 1); root.Controls.Add(lower, 0, 4);
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
                StartScan();
            }
        }

        private void StartScan()
        {
            string path = folderBox.Text.Trim();
            if (!Directory.Exists(path)) { MessageBox.Show(this, "請先選擇存在的備份資料夾。", "NetCheck Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (scanning) return;
            scanning = true; SetBusy(true); scanStatus.Text = "正在掃描所有電腦的備份資料…";
            settings.BackupRoot = path; SettingsStore.Save(settings);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ScanResult result = BackupAnalyzer.Analyze(path, settings);
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    current = result; scanning = false; SetBusy(false); BindAll();
                    scanStatus.Text = "更新時間 " + result.ScannedAt.ToString("yyyy/MM/dd HH:mm:ss") + "｜" + result.CsvFileCount + " 個 CSV｜" + result.MonitoringRowCount.ToString("N0") + " 筆監控" + (result.Issues.Count > 0 ? "｜" + result.Issues.Count + " 個讀取提醒" : "");
                });
            });
        }

        private void BindAll()
        {
            int total = current == null ? 0 : current.Machines.Count;
            int normal = current == null ? 0 : current.Machines.Count(delegate (MachineSummary m) { return m.ReturnState == "回傳正常"; });
            int attention = current == null ? 0 : current.Machines.Count(delegate (MachineSummary m) { return m.ReturnState != "回傳正常"; });
            double availability = current == null || current.Machines.Count == 0 ? 0 : current.Machines.Where(delegate (MachineSummary m) { return m.Checks > 0; }).Select(delegate (MachineSummary m) { return m.AvailabilityPercent; }).DefaultIfEmpty(0).Average();
            totalValue.Text = total.ToString(); normalValue.Text = normal.ToString(); delayedValue.Text = attention.ToString(); availabilityValue.Text = availability.ToString("0.00") + "%";
            BindMachines();
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
                    if (form.ShowDialog(this) == DialogResult.OK) StartScan();
            }
            catch (Exception ex) { MessageBox.Show(this, "無法讀取遠端設定：" + ex.Message, "遠端設定", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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

        private void SetBusy(bool busy) { scanButton.Enabled = browseButton.Enabled = openFolderButton.Enabled = filterBox.Enabled = !busy; scanButton.Text = busy ? "掃描中…" : "重新掃描"; }
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
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return ViewerSettings.Defaults();
                ViewerSettings value = new JavaScriptSerializer().Deserialize<ViewerSettings>(File.ReadAllText(path, Encoding.UTF8));
                return value ?? ViewerSettings.Defaults();
            }
            catch { return ViewerSettings.Defaults(); }
        }

        internal static void Save(ViewerSettings value)
        {
            try
            {
                string path = SettingsPath(); string directory = Path.GetDirectoryName(path); if (!String.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
