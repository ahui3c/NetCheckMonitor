using System;
using System.Collections.Generic;
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
            try { return !String.Equals(File.ReadAllText(PreferencePath(), Encoding.UTF8).Trim(), "1", StringComparison.Ordinal); }
            catch { return true; }
        }

        internal static void MarkCloseToTrayNoticeShown()
        {
            string path = PreferencePath();
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

        private static string PreferencePath()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_UI_STATE");
            if (!String.IsNullOrWhiteSpace(overridePath)) return overridePath;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Monitor", "ui-state.dat");
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
    }

    internal static class MonitorSettingsStore
    {
        public static MonitorTargetSettings Load()
        {
            return LoadFromPath(SettingsPath());
        }

        public static void Save(MonitorTargetSettings value)
        {
            SaveToPath(SettingsPath(), value);
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
                PreventShutdownWhileMonitoring = false
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
                && normalized;
        }

        private static string SettingsPath()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_MONITOR_SETTINGS");
            if (!String.IsNullOrWhiteSpace(overridePath)) return overridePath;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Monitor", "settings.json");
        }

        private static MonitorTargetSettings DefaultSettings()
        {
            return new MonitorTargetSettings { UseCustomTargets = false, CustomTargets = new List<string>(), PreventSleepWhileMonitoring = true };
        }

        private static MonitorTargetSettings LoadFromPath(string path)
        {
            try
            {
                if (!File.Exists(path)) return DefaultSettings();
                string json = File.ReadAllText(path, Encoding.UTF8);
                bool hasSleepSetting = json.IndexOf("\"PreventSleepWhileMonitoring\"", StringComparison.Ordinal) >= 0;
                var value = new JavaScriptSerializer().Deserialize<MonitorTargetSettings>(json);
                if (value == null) return DefaultSettings();
                if (!hasSleepSetting) value.PreventSleepWhileMonitoring = true;
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
            catch { return DefaultSettings(); }
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
        private readonly Button saveButton = new Button();
        private readonly Button cancelButton = new Button();
        public MonitorTargetSettings Result { get; private set; }
        public string SelectedLanguage { get; private set; }

        public MonitorSettingsForm(MonitorTargetSettings current)
        {
            Text = L.T("監控目標設定", "Monitoring Target Settings");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(620, 625);
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
            preventShutdownBox.Text = L.T("監控期間阻止 Windows 關機或重新啟動（請先停止監控）", "Block Windows shutdown or restart while monitoring (stop monitoring first)");
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

            saveButton.Text = L.T("儲存", "Save");
            cancelButton.Text = L.T("取消", "Cancel");
            saveButton.SetBounds(352, 580, 110, 30);
            cancelButton.SetBounds(475, 580, 110, 30);
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
            if (current.CustomTargets != null)
                for (int i = 0; i < current.CustomTargets.Count && i < targetBoxes.Length; i++) targetBoxes[i].Text = current.CustomTargets[i];
            UpdateTargetState();
            Controls.AddRange(new Control[] { title, intro, builtInRadio, builtInInfo, customRadio, hint, advancedDiagnosticsBox, preventSleepBox, preventShutdownBox, autoStartWindowsBox, autoStartMonitoringBox, languageLabel, languageBox, languageHint, saveButton, cancelButton });
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
            Result = new MonitorTargetSettings { UseCustomTargets = customRadio.Checked, CustomTargets = values, AutoStartWindows = autoStartWindowsBox.Checked, AutoStartMonitoring = autoStartMonitoringBox.Checked, AdvancedDiagnosticsEnabled = advancedDiagnosticsBox.Checked, PreventSleepWhileMonitoring = preventSleepBox.Checked, PreventShutdownWhileMonitoring = preventShutdownBox.Checked };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
