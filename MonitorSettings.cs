using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetCheck
{
    internal static class UiPreferenceStore
    {
        internal static bool ShouldShowCloseToTrayNotice()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_UI_STATE");
            if (String.IsNullOrWhiteSpace(overridePath)) return PortableSettingsStore.LoadCloseNoticeShown() != true;
            try { return !String.Equals(File.ReadAllText(overridePath, Encoding.UTF8).Trim(), "1", StringComparison.Ordinal); }
            catch { return true; }
        }

        internal static void MarkCloseToTrayNoticeShown()
        {
            string path = Environment.GetEnvironmentVariable("NETCHECK_UI_STATE");
            if (String.IsNullOrWhiteSpace(path)) { PortableSettingsStore.SaveCloseNoticeShown(true); return; }
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, "1", new UTF8Encoding(false));
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

        internal static bool RunStorageSelfTest(string path)
        {
            string previous = Environment.GetEnvironmentVariable("NETCHECK_UI_STATE");
            try
            {
                Environment.SetEnvironmentVariable("NETCHECK_UI_STATE", path);
                if (File.Exists(path)) File.Delete(path);
                bool firstTime = ShouldShowCloseToTrayNotice();
                MarkCloseToTrayNoticeShown();
                return firstTime && !ShouldShowCloseToTrayNotice() && File.ReadAllText(path, Encoding.UTF8).Trim() == "1";
            }
            finally { Environment.SetEnvironmentVariable("NETCHECK_UI_STATE", previous); }
        }

    }

    internal sealed class MonitorTargetSettings
    {
        public bool UseCustomTargets { get; set; }
        public List<string> CustomTargets { get; set; }
        public bool AutoStartWindows { get; set; }
        public bool AutoStartMonitoring { get; set; }
        public bool AdvancedDiagnosticsEnabled { get; set; }
        public bool PreventSleepWhileMonitoring { get; set; }
        public bool PreventShutdownWhileMonitoring { get; set; }
        public SpeedTestOptions SpeedTest { get; set; }
    }

    internal static class MonitorSettingsStore
    {
        public static MonitorTargetSettings Load()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_MONITOR_SETTINGS");
            return String.IsNullOrWhiteSpace(overridePath) ? NormalizeLoaded(PortableSettingsStore.LoadMonitorSettings(), true) : LoadFromPath(overridePath);
        }

        public static void Save(MonitorTargetSettings value)
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_MONITOR_SETTINGS");
            if (String.IsNullOrWhiteSpace(overridePath)) PortableSettingsStore.SaveMonitorSettings(value);
            else SaveToPath(overridePath, value);
        }

        internal static string[] GetEffectiveTargets(MonitorTargetSettings settings, string[] builtInTargets)
        {
            if (settings != null && settings.UseCustomTargets && settings.CustomTargets != null && settings.CustomTargets.Count > 0)
                return settings.CustomTargets.ToArray();
            return (string[])builtInTargets.Clone();
        }

        public static bool TryNormalizeTarget(string input, out string normalized, out string error)
        {
            normalized = null;
            error = null;
            string value = (input ?? String.Empty).Trim();
            if (value.Length == 0) { error = L.T("目標不可空白。", "The target cannot be empty."); return false; }
            if (value.IndexOfAny(new char[] { '\r', '\n', '\t' }) >= 0) { error = L.T("目標格式無效。", "The target format is invalid."); return false; }

            string candidate = value;
            if (!value.Contains("://"))
            {
                IPAddress address;
                if (IPAddress.TryParse(value, out address))
                    candidate = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "http://[" + value + "]/" : "http://" + value + "/";
                else
                    candidate = "https://" + value;
            }

            Uri uri;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri) || String.IsNullOrWhiteSpace(uri.Host))
            {
                error = L.T("請輸入有效的網站或 IP。", "Enter a valid website or IP address.");
                return false;
            }
            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = L.T("僅支援 HTTP 或 HTTPS 目標。", "Only HTTP or HTTPS targets are supported.");
                return false;
            }
            if (!String.IsNullOrEmpty(uri.UserInfo))
            {
                error = L.T("目標不可包含帳號或密碼。", "The target cannot contain a username or password.");
                return false;
            }
            normalized = uri.AbsoluteUri;
            return true;
        }

        public static bool RunStorageSelfTest(string path)
        {
            var expected = new MonitorTargetSettings
            {
                UseCustomTargets = true,
                CustomTargets = new List<string> { "http://127.0.0.1:9/", "http://127.0.0.1:8/", "http://127.0.0.1:7/" },
                AutoStartWindows = true,
                AutoStartMonitoring = true,
                AdvancedDiagnosticsEnabled = true,
                PreventSleepWhileMonitoring = true,
                PreventShutdownWhileMonitoring = false,
                SpeedTest = new SpeedTestOptions { ScheduledEnabled = true, IntervalHours = 24, Level = "Quick", AllowMeteredNetwork = false }
            };
            SaveToPath(path, expected);
            MonitorTargetSettings loaded = LoadFromPath(path);
            string website, ip, error;
            bool normalized = TryNormalizeTarget("example.com/status", out website, out error)
                && website == "https://example.com/status"
                && TryNormalizeTarget("1.1.1.1", out ip, out error)
                && ip == "http://1.1.1.1/";
            return loaded.UseCustomTargets && loaded.CustomTargets.Count == 3
                && loaded.CustomTargets[0] == expected.CustomTargets[0]
                && loaded.CustomTargets[1] == expected.CustomTargets[1]
                && loaded.CustomTargets[2] == expected.CustomTargets[2]
                && loaded.AutoStartWindows
                && loaded.AutoStartMonitoring
                && loaded.AdvancedDiagnosticsEnabled
                && loaded.PreventSleepWhileMonitoring
                && !loaded.PreventShutdownWhileMonitoring
                && loaded.SpeedTest != null && loaded.SpeedTest.ScheduledEnabled && loaded.SpeedTest.IntervalHours == 24 && loaded.SpeedTest.Level == "Quick"
                && normalized;
        }

        private static MonitorTargetSettings DefaultSettings()
        {
            return new MonitorTargetSettings { UseCustomTargets = false, CustomTargets = new List<string>(), PreventSleepWhileMonitoring = true, SpeedTest = SpeedTestOptions.Defaults() };
        }

        private static MonitorTargetSettings LoadFromPath(string path)
        {
            try
            {
                if (!File.Exists(path)) return DefaultSettings();
                string json = File.ReadAllText(path, Encoding.UTF8);
                bool hasSleepSetting = json.IndexOf("\"PreventSleepWhileMonitoring\"", StringComparison.Ordinal) >= 0;
                var value = new JavaScriptSerializer().Deserialize<MonitorTargetSettings>(json);
                return NormalizeLoaded(value, hasSleepSetting);
            }
            catch { return DefaultSettings(); }
        }

        private static MonitorTargetSettings NormalizeLoaded(MonitorTargetSettings value, bool hasSleepSetting)
        {
            if (value == null) return DefaultSettings();
            if (!hasSleepSetting) value.PreventSleepWhileMonitoring = true;
            if (value.SpeedTest == null) value.SpeedTest = SpeedTestOptions.Defaults();
            value.SpeedTest.IntervalHours = Math.Max(1, Math.Min(168, value.SpeedTest.IntervalHours <= 0 ? 24 : value.SpeedTest.IntervalHours));
            value.SpeedTest.Level = value.SpeedTest.EffectiveLevel.ToString();
            value.SpeedTest.RateLimitBackoffLevel = Math.Max(0, Math.Min(5, value.SpeedTest.RateLimitBackoffLevel));
            if (value.CustomTargets == null) value.CustomTargets = new List<string>();
            var valid = new List<string>();
            foreach (string target in value.CustomTargets)
            {
                string normalized, error;
                if (valid.Count < 3 && TryNormalizeTarget(target, out normalized, out error) && !ContainsIgnoreCase(valid, normalized)) valid.Add(normalized);
            }
            value.CustomTargets = valid;
            if (value.UseCustomTargets && valid.Count == 0) value.UseCustomTargets = false;
            return value;
        }

        private static void SaveToPath(string path, MonitorTargetSettings value)
        {
            if (value == null) throw new ArgumentNullException("value");
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string json = new JavaScriptSerializer().Serialize(value);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
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

        private static bool ContainsIgnoreCase(List<string> values, string candidate)
        {
            foreach (string value in values) if (String.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal sealed class MonitorSettingsForm : Form
    {
        private readonly RadioButton builtInRadio = new RadioButton();
        private readonly RadioButton customRadio = new RadioButton();
        private readonly TextBox[] targetBoxes = new TextBox[] { new TextBox(), new TextBox(), new TextBox() };
        private readonly CheckBox autoStartWindowsBox = new CheckBox();
        private readonly CheckBox autoStartMonitoringBox = new CheckBox();
        private readonly CheckBox advancedDiagnosticsBox = new CheckBox();
        private readonly CheckBox preventSleepBox = new CheckBox();
        private readonly CheckBox preventShutdownBox = new CheckBox();
        private readonly ComboBox languageBox = new ComboBox();
        private readonly Button exportBackupButton = new Button();
        private readonly Button rebuildDailyReportsButton = new Button();
        private readonly Button speedSettingsButton = new Button();
        private readonly Button cloudSettingsButton = new Button();
        private readonly Button clearDataButton = new Button();
        private readonly Button saveButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Action rebuildDailyReports;
        private readonly Action openSpeedReport;
        private readonly Action showCloudSettings;
        private readonly Action clearStoredData;
        private SpeedTestOptions speedOptions;
        public MonitorTargetSettings Result { get; private set; }
        public string SelectedLanguage { get; private set; }

        public MonitorSettingsForm(MonitorTargetSettings current) : this(current, null, null, null, null) { }

        public MonitorSettingsForm(MonitorTargetSettings current, Action rebuildDailyReportsAction) : this(current, rebuildDailyReportsAction, null, null, null) { }

        public MonitorSettingsForm(MonitorTargetSettings current, Action rebuildDailyReportsAction, Action openSpeedReportAction) : this(current, rebuildDailyReportsAction, openSpeedReportAction, null, null) { }

        public MonitorSettingsForm(MonitorTargetSettings current, Action rebuildDailyReportsAction, Action openSpeedReportAction, Action showCloudSettingsAction, Action clearStoredDataAction)
        {
            rebuildDailyReports = rebuildDailyReportsAction;
            openSpeedReport = openSpeedReportAction;
            showCloudSettings = showCloudSettingsAction;
            clearStoredData = clearStoredDataAction;
            Text = L.T("監控目標設定", "Monitoring Target Settings");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(620, 760);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label { Text = L.T("對外連線檢測目標", "Internet Connectivity Targets"), Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
            var intro = new Label { Text = L.T("選擇內建目標，或改用最多三組自訂網站／IP。監控中變更目標會安全保存並重新開始監控。", "Use the built-in targets or up to three custom websites/IP addresses. Target changes during monitoring safely save and restart the session."), AutoSize = false, Location = new Point(27, 62), Size = new Size(565, 48), ForeColor = Color.DimGray };

            builtInRadio.Text = L.T("使用內建測試目標（建議）", "Use built-in test targets (recommended)");
            builtInRadio.SetBounds(28, 116, 300, 26);
            var builtInInfo = new Label { Text = L.T("由程式自動使用預設目標進行連線測試。", "The app automatically uses its default connectivity targets."), AutoSize = false, Location = new Point(51, 146), Size = new Size(540, 24), ForeColor = Color.SlateGray, Font = new Font(Font.FontFamily, 8.5F) };

            customRadio.Text = L.T("使用自訂測試目標", "Use custom test targets");
            customRadio.SetBounds(28, 174, 260, 26);
            for (int i = 0; i < targetBoxes.Length; i++)
            {
                var label = new Label { Text = L.T("目標 " + (i + 1), "Target " + (i + 1)), AutoSize = true, Location = new Point(51, 214 + i * 43) };
                targetBoxes[i].SetBounds(120, 209 + i * 43, 465, 28);
                Controls.Add(label);
                Controls.Add(targetBoxes[i]);
            }
            var hint = new Label { Text = L.T("依序測試，任一目標成功即判定本次連線正常。未填通訊協定時，網站使用 https://，IP 使用 http://。", "Targets are tried in order; the first success marks the check online. Without a scheme, websites use https:// and IPs use http://."), AutoSize = false, Location = new Point(51, 338), Size = new Size(535, 42), ForeColor = Color.DimGray, Font = new Font(Font.FontFamily, 8.5F) };

            advancedDiagnosticsBox.Text = L.T("HTTPS 失敗時執行進階分層連線診斷（選用）", "Run advanced layered diagnostics after an HTTPS failure (optional)");
            advancedDiagnosticsBox.SetBounds(51, 380, 535, 30);
            preventSleepBox.Text = L.T("監控期間防止電腦進入休眠（建議）", "Prevent the computer from sleeping while monitoring (recommended)");
            preventSleepBox.SetBounds(51, 412, 535, 26);
            preventShutdownBox.Text = L.T("監控期間阻止 Windows 關機或重新啟動（請使用程式內關閉按鈕）", "Block Windows shutdown or restart while monitoring (use the in-app exit button)");
            preventShutdownBox.SetBounds(51, 439, 555, 26);
            autoStartWindowsBox.Text = L.T("登入 Windows 後自動啟動程式", "Start the app after Windows sign-in");
            autoStartWindowsBox.SetBounds(51, 471, 535, 26);
            autoStartMonitoringBox.Text = L.T("程式啟動後自動開始監控", "Start monitoring automatically when the app opens");
            autoStartMonitoringBox.SetBounds(51, 498, 535, 26);

            var languageLabel = new Label { Text = L.T("介面語言", "Interface language"), AutoSize = true, Location = new Point(51, 536) };
            languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            languageBox.Items.Add("繁體中文");
            languageBox.Items.Add("English");
            languageBox.SetBounds(155, 530, 160, 28);
            languageBox.SelectedIndex = L.TraditionalChinese ? 0 : 1;
            var languageHint = new Label { Text = L.T("下次啟動程式時套用", "Applied the next time the app starts"), AutoSize = false, Location = new Point(329, 536), Size = new Size(255, 25), ForeColor = Color.DimGray, Font = new Font(Font.FontFamily, 8.5F) };

            exportBackupButton.Text = L.T("匯出全部紀錄備份 ZIP", "Export All Data Backup ZIP");
            speedSettingsButton.Text = L.T("定時測速設定（Beta）…", "Scheduled Speed Test Settings (Beta)…");
            speedSettingsButton.SetBounds(51, 570, 534, 32);
            speedSettingsButton.Click += delegate { EditSpeedSettings(); };

            cloudSettingsButton.Text = L.T("Google Drive 備份設定…", "Google Drive Backup Settings…");
            cloudSettingsButton.SetBounds(51, 612, 260, 34);
            cloudSettingsButton.Enabled = showCloudSettings != null;
            cloudSettingsButton.Click += delegate { if (showCloudSettings != null) showCloudSettings(); };
            clearDataButton.Text = L.T("清除全部儲存資料…", "Clear All Saved Data…");
            clearDataButton.SetBounds(325, 612, 260, 34);
            clearDataButton.ForeColor = Color.Firebrick;
            clearDataButton.Enabled = clearStoredData != null;
            clearDataButton.Click += delegate { if (clearStoredData != null) clearStoredData(); };

            exportBackupButton.SetBounds(51, 654, 260, 34);
            exportBackupButton.Click += delegate { ExportBackupZip(); };
            rebuildDailyReportsButton.Text = L.T("強制重製每日詳細報表", "Rebuild Daily Detail Reports");
            rebuildDailyReportsButton.SetBounds(325, 654, 260, 34);
            rebuildDailyReportsButton.Enabled = rebuildDailyReports != null;
            rebuildDailyReportsButton.Click += delegate { RebuildDailyReports(); };

            saveButton.Text = L.T("儲存", "Save");
            cancelButton.Text = L.T("取消", "Cancel");
            saveButton.SetBounds(352, 712, 110, 30);
            cancelButton.SetBounds(475, 712, 110, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            saveButton.Click += delegate { ValidateAndClose(); };
            builtInRadio.CheckedChanged += delegate { UpdateTargetState(); };
            customRadio.CheckedChanged += delegate { UpdateTargetState(); };
            AcceptButton = saveButton;
            CancelButton = cancelButton;

            current = current ?? new MonitorTargetSettings { CustomTargets = new List<string>() };
            builtInRadio.Checked = !current.UseCustomTargets;
            customRadio.Checked = current.UseCustomTargets;
            autoStartWindowsBox.Checked = current.AutoStartWindows;
            autoStartMonitoringBox.Checked = current.AutoStartMonitoring;
            advancedDiagnosticsBox.Checked = current.AdvancedDiagnosticsEnabled;
            preventSleepBox.Checked = current.PreventSleepWhileMonitoring;
            preventShutdownBox.Checked = current.PreventShutdownWhileMonitoring;
            speedOptions = current.SpeedTest ?? SpeedTestOptions.Defaults();
            if (current.CustomTargets != null)
                for (int i = 0; i < current.CustomTargets.Count && i < targetBoxes.Length; i++) targetBoxes[i].Text = current.CustomTargets[i];
            UpdateTargetState();
            Controls.AddRange(new Control[] { title, intro, builtInRadio, builtInInfo, customRadio, hint, advancedDiagnosticsBox, preventSleepBox, preventShutdownBox, autoStartWindowsBox, autoStartMonitoringBox, languageLabel, languageBox, languageHint, speedSettingsButton, cloudSettingsButton, clearDataButton, exportBackupButton, rebuildDailyReportsButton, saveButton, cancelButton });
        }

        private void EditSpeedSettings()
        {
            using (var form = new SpeedTestSettingsForm(speedOptions, openSpeedReport))
                if (form.ShowDialog(this) == DialogResult.OK) speedOptions = form.Result;
        }

        private void ExportBackupZip()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = L.T("匯出全部紀錄備份", "Export All Data Backup");
                dialog.Filter = L.T("ZIP 壓縮檔 (*.zip)|*.zip", "ZIP archive (*.zip)|*.zip");
                dialog.DefaultExt = "zip";
                dialog.AddExtension = true;
                dialog.FileName = "NetCheckMonitor_Backup_" + BackupFilePart(Environment.MachineName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                exportBackupButton.Enabled = false;
                Cursor previous = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    int count = ArchiveReport.ExportAllDataZip(dialog.FileName);
                    MessageBox.Show(L.T("備份完成，共匯出 ", "Backup completed. Exported ") + count + L.T(" 個紀錄檔案。\n\n", " data files.\n\n") + dialog.FileName, exportBackupButton.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(L.T("無法匯出備份：", "Could not export backup: ") + ex.Message, exportBackupButton.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { Cursor = previous; exportBackupButton.Enabled = true; }
            }
        }

        private static string BackupFilePart(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "PC") if (Char.IsLetterOrDigit(c) || c == '-' || c == '_') result.Append(c);
            return result.Length == 0 ? "PC" : result.ToString();
        }

        private void RebuildDailyReports()
        {
            if (rebuildDailyReports == null) return;
            if (MessageBox.Show(L.T("要忽略既有快取，重新製作所有日期的詳細 HTML 報表嗎？", "Ignore the existing cache and rebuild detailed HTML reports for every date?"), rebuildDailyReportsButton.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            rebuildDailyReportsButton.Enabled = false;
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                rebuildDailyReports();
                MessageBox.Show(L.T("每日詳細報表已全部重新製作完成。", "All daily detail reports were rebuilt."), rebuildDailyReportsButton.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("無法重新製作每日詳細報表：", "Could not rebuild daily detail reports: ") + ex.Message, rebuildDailyReportsButton.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = previous; rebuildDailyReportsButton.Enabled = true; }
        }

        private void UpdateTargetState()
        {
            foreach (TextBox box in targetBoxes) box.Enabled = customRadio.Checked;
        }

        private void ValidateAndClose()
        {
            var values = new List<string>();
            if (customRadio.Checked)
            {
                for (int i = 0; i < targetBoxes.Length; i++)
                {
                    string raw = targetBoxes[i].Text.Trim();
                    if (raw.Length == 0) continue;
                    string normalized, error;
                    if (!MonitorSettingsStore.TryNormalizeTarget(raw, out normalized, out error))
                    {
                        MessageBox.Show(L.T("目標 ", "Target ") + (i + 1) + L.T("：", ": ") + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        targetBoxes[i].Focus();
                        return;
                    }
                    bool duplicate = false;
                    foreach (string value in values) if (String.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)) duplicate = true;
                    if (duplicate)
                    {
                        MessageBox.Show(L.T("自訂目標不可重複。", "Custom targets cannot be duplicated."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        targetBoxes[i].Focus();
                        return;
                    }
                    values.Add(normalized);
                }
                if (values.Count == 0)
                {
                    MessageBox.Show(L.T("請至少輸入一組自訂網站或 IP。", "Enter at least one custom website or IP address."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    targetBoxes[0].Focus();
                    return;
                }
            }
            SelectedLanguage = languageBox.SelectedIndex == 0 ? LanguagePreferenceStore.TraditionalChinese : LanguagePreferenceStore.English;
            Result = new MonitorTargetSettings { UseCustomTargets = customRadio.Checked, CustomTargets = values, AutoStartWindows = autoStartWindowsBox.Checked, AutoStartMonitoring = autoStartMonitoringBox.Checked, AdvancedDiagnosticsEnabled = advancedDiagnosticsBox.Checked, PreventSleepWhileMonitoring = preventSleepBox.Checked, PreventShutdownWhileMonitoring = preventShutdownBox.Checked, SpeedTest = speedOptions ?? SpeedTestOptions.Defaults() };
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class SpeedTestSettingsForm : Form
    {
        private readonly CheckBox scheduledBox = new CheckBox();
        private readonly NumericUpDown intervalBox = new NumericUpDown();
        private readonly ComboBox levelBox = new ComboBox();
        private readonly CheckBox meteredBox = new CheckBox();
        private readonly Button speedReportButton = new Button();
        private readonly LinkLabel speedtestLink = new LinkLabel();
        private readonly LinkLabel hinetLink = new LinkLabel();
        private readonly bool originallyScheduled;
        private readonly bool originallyMetered;
        private readonly SpeedTestOptions currentOptions;
        private readonly Action openSpeedReport;
        private DateTime lastScheduledRunUtc;
        internal SpeedTestOptions Result { get; private set; }

        internal SpeedTestSettingsForm(SpeedTestOptions current) : this(current, null) { }

        internal SpeedTestSettingsForm(SpeedTestOptions current, Action openSpeedReportAction)
        {
            current = current ?? SpeedTestOptions.Defaults();
            currentOptions = current;
            openSpeedReport = openSpeedReportAction;
            originallyScheduled = current.ScheduledEnabled;
            originallyMetered = current.AllowMeteredNetwork;
            lastScheduledRunUtc = current.LastScheduledRunUtc;
            Text = L.T("定時測速設定（Beta）", "Scheduled Speed Test Settings (Beta)");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(620, 570);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var title = new Label { Text = L.T("Cloudflare 定時網路測速（Beta）", "Cloudflare Scheduled Speed Test (Beta)"), Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
            var warning = new Label { Text = L.T("本功能使用 Cloudflare 測速服務，實測結果可能與 Speedtest／中華電信測速不同，僅供網路趨勢與叫修參考。測速會使用大量流量並可能暫時占用頻寬。", "This feature uses Cloudflare's speed-test service. Results may differ from Speedtest or Chunghwa Telecom and are for trend and troubleshooting reference only. Tests use substantial data and may temporarily consume bandwidth."), AutoSize = false, Location = new Point(27, 64), Size = new Size(565, 66), ForeColor = Color.Firebrick };
            scheduledBox.Text = L.T("啟用定時測速（僅在監控中且未暫停時執行）", "Enable scheduled speed tests (only while monitoring and not paused)"); scheduledBox.SetBounds(28, 139, 560, 28);
            var intervalLabel = new Label { Text = L.T("定時間隔", "Schedule interval"), AutoSize = true, Location = new Point(51, 184) };
            intervalBox.Minimum = 1; intervalBox.Maximum = 168; intervalBox.Value = Math.Max(1, Math.Min(168, current.IntervalHours <= 0 ? 24 : current.IntervalHours)); intervalBox.SetBounds(160, 179, 90, 28);
            var hourLabel = new Label { Text = L.T("小時（預設 24，最短 1 小時）", "hours (default 24, minimum 1 hour)"), AutoSize = true, Location = new Point(260, 184) };
            var levelLabel = new Label { Text = L.T("定時測速等級", "Scheduled test level"), AutoSize = true, Location = new Point(51, 227) };
            levelBox.DropDownStyle = ComboBoxStyle.DropDownList; levelBox.Items.AddRange(new object[] { L.T("快速（最多約 25 MB）", "Quick (up to about 25 MB)"), L.T("標準（最多約 130 MB）", "Standard (up to about 130 MB)"), L.T("完整（最多約 400 MB）", "Full (up to about 400 MB)") }); levelBox.SetBounds(190, 222, 300, 28); levelBox.SelectedIndex = current.EffectiveLevel == SpeedTestLevel.Quick ? 0 : current.EffectiveLevel == SpeedTestLevel.Full ? 2 : 1;
            meteredBox.Text = L.T("允許在 Windows 計量付費網路執行定時測速", "Allow scheduled tests on Windows metered connections"); meteredBox.SetBounds(51, 269, 520, 28);
            var meteredHint = new Label { Text = L.T("預設不允許。若允許，每次執行前仍會再次警告。兩次測速至少間隔 15 分鐘；伺服器拒絕時會自動延長冷卻。", "Disabled by default. If enabled, every run still warns first. Tests are at least 15 minutes apart, with longer cooldown after server rejection."), AutoSize = false, Location = new Point(74, 300), Size = new Size(510, 45), ForeColor = Color.DimGray, Font = new Font(Font.FontFamily, 8.5F) };
            speedReportButton.Text = L.T("開啟速度趨勢報表", "Open Speed Trend Report");
            speedReportButton.SetBounds(51, 357, 220, 34);
            speedReportButton.Enabled = openSpeedReport != null;
            speedReportButton.Click += delegate { if (openSpeedReport != null) openSpeedReport(); };
            var compareLabel = new Label { Text = L.T("可另行比較其他測速服務：", "Compare with other speed-test services:"), AutoSize = true, Location = new Point(51, 412), ForeColor = Color.DimGray };
            speedtestLink.Text = "Speedtest by Ookla"; speedtestLink.AutoSize = true; speedtestLink.Location = new Point(51, 443);
            hinetLink.Text = L.T("中華電信 HiNet 測速", "Chunghwa Telecom HiNet Speed Test"); hinetLink.AutoSize = true; hinetLink.Location = new Point(245, 443);
            speedtestLink.LinkClicked += delegate { OpenExternalLink("https://www.speedtest.net/"); };
            hinetLink.LinkClicked += delegate { OpenExternalLink("https://speed.hinet.net/agreement.html"); };
            var save = new Button { Text = L.T("儲存", "Save") }; var cancel = new Button { Text = L.T("取消", "Cancel"), DialogResult = DialogResult.Cancel }; save.SetBounds(374, 518, 100, 32); cancel.SetBounds(488, 518, 100, 32); save.Click += delegate { Save(); };
            scheduledBox.Checked = current.ScheduledEnabled; meteredBox.Checked = current.AllowMeteredNetwork;
            scheduledBox.CheckedChanged += delegate { UpdateEnabled(); }; UpdateEnabled(); AcceptButton = save; CancelButton = cancel;
            Controls.AddRange(new Control[] { title, warning, scheduledBox, intervalLabel, intervalBox, hourLabel, levelLabel, levelBox, meteredBox, meteredHint, speedReportButton, compareLabel, speedtestLink, hinetLink, save, cancel });
        }

        private void UpdateEnabled()
        {
            intervalBox.Enabled = levelBox.Enabled = meteredBox.Enabled = scheduledBox.Checked;
        }
        private void OpenExternalLink(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(L.T("無法開啟網頁：", "Could not open the webpage: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void Save()
        {
            if (scheduledBox.Checked && !originallyScheduled)
            {
                if (MessageBox.Show(L.T("定時測速會依設定週期自動下載及上傳資料，可能占用頻寬並產生網路流量費用。是否仍要啟用？", "Scheduled speed tests automatically download and upload data at the selected interval, may use bandwidth, and may incur data charges. Enable anyway?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                if (MessageBox.Show(L.T("請再次確認：測速結果僅供參考，作者與程式不對流量費用、網路降速或測量差異負責。確定接受並啟用嗎？", "Confirm again: results are informational only. The author and app are not responsible for data charges, temporary slowdown, or measurement differences. Accept and enable?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            if (meteredBox.Checked && !originallyMetered)
            {
                if (MessageBox.Show(L.T("計量付費網路可能依流量收費。允許自動測速可能造成額外費用，確定要開放嗎？", "Metered networks may charge by usage. Allowing automatic speed tests may cause extra charges. Continue?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                if (MessageBox.Show(L.T("最後確認：即使已允許，實際執行時仍會再次詢問。是否儲存此設定？", "Final confirmation: the app will still ask again before each metered scheduled test. Save this setting?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            SpeedTestLevel level = levelBox.SelectedIndex == 0 ? SpeedTestLevel.Quick : levelBox.SelectedIndex == 2 ? SpeedTestLevel.Full : SpeedTestLevel.Standard;
            Result = new SpeedTestOptions
            {
                ScheduledEnabled = scheduledBox.Checked,
                IntervalHours = (int)intervalBox.Value,
                Level = level.ToString(),
                AllowMeteredNetwork = meteredBox.Checked,
                LastScheduledRunUtc = lastScheduledRunUtc,
                LastAttemptUtc = currentOptions.LastAttemptUtc,
                ServerCooldownUntilUtc = currentOptions.ServerCooldownUntilUtc,
                RateLimitBackoffLevel = currentOptions.RateLimitBackoffLevel
            };
            DialogResult = DialogResult.OK; Close();
        }
    }
}
