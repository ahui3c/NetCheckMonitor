using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetCheck
{
    internal sealed class EventNote
    {
        public DateTime Time;
        public string Text = "";
    }

    internal sealed class EventNoteForm : Form
    {
        private readonly TextBox noteBox = new TextBox();
        private readonly Button saveButton = new Button();
        public string NoteText { get; private set; }

        public EventNoteForm()
        {
            Text = L.T("插入事件註記", "Add Event Note");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(560, 350);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label { Text = L.T("記錄當下發生的特殊事件或處理動作", "Record a special event or action at the current time"), Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
            var intro = new Label { Text = L.T("註記會連同目前時間寫入原始資料與報表，但不影響斷線統計。", "The note and current time are written to raw data and reports without affecting outage statistics."), AutoSize = false, ForeColor = Color.DimGray, Location = new Point(27, 60), Size = new Size(505, 42) };
            var quickLabel = new Label { Text = L.T("快速填寫", "Quick entries"), AutoSize = true, Location = new Point(27, 106) };
            string[] quickZh = { "重開數據機", "重開無線路由", "電腦重新開機", "下雨", "打雷" };
            string[] quickEn = { "Restarted modem", "Restarted wireless router", "Restarted computer", "Rain", "Thunder" };
            int[] widths = { 98, 120, 112, 62, 62 };
            int x = 27;
            for (int i = 0; i < quickZh.Length; i++)
            {
                string value = L.T(quickZh[i], quickEn[i]);
                var button = new Button { Text = value, Tag = value };
                button.SetBounds(x, 132, widths[i], 30);
                button.Click += delegate (object sender, EventArgs args) { AppendQuick(Convert.ToString(((Button)sender).Tag)); };
                Controls.Add(button);
                x += widths[i] + 7;
            }

            noteBox.Multiline = true;
            noteBox.ScrollBars = ScrollBars.Vertical;
            noteBox.MaxLength = 500;
            noteBox.SetBounds(27, 177, 505, 105);
            saveButton.Text = L.T("加入註記", "Add Note");
            saveButton.SetBounds(300, 300, 110, 34);
            var cancel = new Button { Text = L.T("取消", "Cancel"), DialogResult = DialogResult.Cancel };
            cancel.SetBounds(422, 300, 110, 34);
            saveButton.Click += delegate { SaveNote(); };
            AcceptButton = saveButton;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { title, intro, quickLabel, noteBox, saveButton, cancel });
        }

        private void AppendQuick(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            string current = noteBox.Text.Trim();
            noteBox.Text = current.Length == 0 ? value : current + L.T("；", "; ") + value;
            noteBox.SelectionStart = noteBox.TextLength;
            noteBox.Focus();
        }

        private void SaveNote()
        {
            string value = noteBox.Text.Trim();
            if (value.Length == 0)
            {
                MessageBox.Show(L.T("請輸入事件或選擇快速填寫項目。", "Enter an event or select a quick entry."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                noteBox.Focus();
                return;
            }
            NoteText = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
