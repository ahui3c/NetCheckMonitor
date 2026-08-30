using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetCheckViewer
{
    internal sealed class ViewerControlDesired
    {
        public string Revision { get; set; }
        public string RequestedAtUtc { get; set; }
        public int MonitorIntervalSeconds { get; set; }
        public string BackupTime { get; set; }
    }

    internal sealed class ViewerControlApplied
    {
        public string Revision { get; set; }
        public string AppliedAtUtc { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public int MonitorIntervalSeconds { get; set; }
        public string BackupTime { get; set; }
    }

    internal sealed class ViewerControlDocument
    {
        public int SchemaVersion { get; set; }
        public string MachineName { get; set; }
        public string MachineId { get; set; }
        public ViewerControlDesired Desired { get; set; }
        public ViewerControlApplied Applied { get; set; }
    }

    internal static class ViewerControlClient
    {
        internal const string FileName = "NetCheck_Control.json";

        internal static string FindControlFile(MachineSummary machine, string root)
        {
            if (machine == null) return null;
            string direct = String.IsNullOrWhiteSpace(machine.FolderPath) ? null : Path.Combine(machine.FolderPath, FileName);
            if (!String.IsNullOrWhiteSpace(direct) && File.Exists(direct))
            {
                try { if (String.Equals(Load(direct).MachineId, machine.MachineId, StringComparison.OrdinalIgnoreCase)) return direct; }
                catch { }
            }
            if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            try
            {
                foreach (string path in Directory.GetFiles(root, FileName, SearchOption.AllDirectories))
                {
                    try { if (String.Equals(Load(path).MachineId, machine.MachineId, StringComparison.OrdinalIgnoreCase)) return path; }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        internal static ViewerControlDocument Load(string path)
        {
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) json = reader.ReadToEnd();
            ViewerControlDocument value = new JavaScriptSerializer().Deserialize<ViewerControlDocument>(json);
            if (value == null || value.SchemaVersion != 1 || value.Desired == null) throw new InvalidDataException("控制設定檔格式不正確或版本不支援。");
            return value;
        }

        internal static ViewerControlDocument RequestChange(string path, string machineId, int intervalSeconds, string backupTime)
        {
            if (intervalSeconds < 10 || intervalSeconds > 3600) throw new ArgumentOutOfRangeException("intervalSeconds", "監控間隔必須介於 10 到 3600 秒。");
            TimeSpan parsed;
            if (!TimeSpan.TryParseExact(backupTime, @"hh\:mm", CultureInfo.InvariantCulture, out parsed) || parsed.TotalHours >= 24)
                throw new InvalidOperationException("資料備份時間必須是 00:00 到 23:59。");
            ViewerControlDocument value = Load(path);
            if (!String.Equals(value.MachineId, machineId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("控制設定檔不屬於目前選取的電腦。");
            value.Desired = new ViewerControlDesired
            {
                Revision = Guid.NewGuid().ToString("N"),
                RequestedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                MonitorIntervalSeconds = intervalSeconds,
                BackupTime = parsed.ToString(@"hh\:mm")
            };
            WritePreservingFileIdentity(path, new JavaScriptSerializer().Serialize(value));
            return value;
        }

        private static void WritePreservingFileIdentity(string path, string json)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            Exception last = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(250 * (attempt + 1));
                }
            }
            throw new IOException("Google Drive 同步程式持續占用控制設定檔，請稍後再試。", last);
        }
        internal static bool RunSelfTest(string root)
        {
            string folder = Path.Combine(root, "ControlProtocolTest");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName);
            var initial = new ViewerControlDocument
            {
                SchemaVersion = 1,
                MachineName = "OFFICE-PC",
                MachineId = "A1B2C3D4",
                Desired = new ViewerControlDesired { Revision = "old", RequestedAtUtc = DateTime.UtcNow.ToString("o"), MonitorIntervalSeconds = 60, BackupTime = "23:55" },
                Applied = new ViewerControlApplied { Revision = "old", Status = "APPLIED", MonitorIntervalSeconds = 60, BackupTime = "23:55" }
            };
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(initial), new UTF8Encoding(false));
            ViewerControlDocument changed = RequestChange(path, "A1B2C3D4", 90, "21:30");
            ViewerControlDocument loaded = Load(path);
            bool rejected = false;
            try { RequestChange(path, "A1B2C3D4", 2, "21:30"); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            return changed.Desired.Revision != "old" && loaded.Desired.MonitorIntervalSeconds == 90 && loaded.Desired.BackupTime == "21:30" && loaded.Applied.Revision == "old" && rejected;
        }
    }

    internal sealed class RemoteSettingsForm : Form
    {
        private readonly string controlPath;
        private readonly string machineId;
        private readonly NumericUpDown intervalBox = new NumericUpDown();
        private readonly DateTimePicker backupTime = new DateTimePicker();

        internal RemoteSettingsForm(string path, ViewerControlDocument document)
        {
            controlPath = path;
            machineId = document.MachineId;
            Text = "Viewer 遠端設定｜" + document.MachineName;
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(590, 420);
            MinimumSize = new Size(606, 459);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var title = new Label { Text = document.MachineName + "  [" + document.MachineId + "]", Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 24), ForeColor = Color.FromArgb(27, 49, 72) };
            var hint = new Label { Text = "設定會先寫入 Google Drive 同步資料夾，再由該電腦的 NetCheckMonitor 安全套用。通常會在 5 分鐘內生效。", AutoSize = false, Location = new Point(31, 67), Size = new Size(525, 48), ForeColor = Color.FromArgb(91, 112, 132) };
            var intervalLabel = new Label { Text = "監控檢查間隔（秒）", AutoSize = true, Location = new Point(32, 137) };
            intervalBox.Minimum = 10; intervalBox.Maximum = 3600; intervalBox.Value = Math.Max(10, Math.Min(3600, document.Desired.MonitorIntervalSeconds)); intervalBox.Location = new Point(250, 133); intervalBox.Size = new Size(150, 28);
            var backupLabel = new Label { Text = "每日資料備份時間", AutoSize = true, Location = new Point(32, 184) };
            backupTime.Format = DateTimePickerFormat.Custom; backupTime.CustomFormat = "HH:mm"; backupTime.ShowUpDown = true; backupTime.Location = new Point(250, 180); backupTime.Size = new Size(150, 28);
            TimeSpan parsed; if (!TimeSpan.TryParseExact(document.Desired.BackupTime, @"hh\:mm", CultureInfo.InvariantCulture, out parsed)) parsed = new TimeSpan(23, 55, 0);
            backupTime.Value = DateTime.Today.Add(parsed);

            string appliedAt = "尚未回報";
            if (document.Applied != null && !String.IsNullOrWhiteSpace(document.Applied.AppliedAtUtc))
            {
                DateTime utc; if (DateTime.TryParse(document.Applied.AppliedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc)) appliedAt = utc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
            }
            string statusText = document.Applied == null ? "尚未收到監控程式回報。" :
                (document.Applied.Status == "APPLIED" ? "已套用" : "未套用") + "｜" + appliedAt + (String.IsNullOrWhiteSpace(document.Applied.Message) ? "" : "\r\n" + document.Applied.Message);
            var status = new Label { Text = "最近回報\r\n" + statusText, AutoSize = false, Location = new Point(32, 238), Size = new Size(522, 78), BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10), ForeColor = document.Applied != null && document.Applied.Status == "APPLIED" ? Color.FromArgb(35, 137, 88) : Color.FromArgb(180, 100, 35) };
            var save = new Button { Text = "送出設定", Location = new Point(320, 345), Size = new Size(112, 42), BackColor = Color.FromArgb(28, 119, 206), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "取消", Location = new Point(446, 345), Size = new Size(112, 42), DialogResult = DialogResult.Cancel };
            save.Click += delegate { SaveRequest(); };
            CancelButton = cancel;
            Controls.AddRange(new Control[] { title, hint, intervalLabel, intervalBox, backupLabel, backupTime, status, save, cancel });
        }

        private void SaveRequest()
        {
            try
            {
                ViewerControlClient.RequestChange(controlPath, machineId, (int)intervalBox.Value, backupTime.Value.ToString("HH:mm"));
                MessageBox.Show(this, "設定要求已寫入備份資料夾。\r\n\r\nGoogle Drive 完成同步後，監控程式通常會在 5 分鐘內套用並將結果寫回此檔案。", "遠端設定已送出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, "無法寫入遠端設定：" + ex.Message, "遠端設定失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
