using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace NetCheckViewer
{
    internal sealed class TrendDashboard : UserControl
    {
        private readonly Color Ink = Color.FromArgb(27, 49, 72);
        private readonly Color Muted = Color.FromArgb(91, 112, 132);
        private readonly ComboBox periodBox = new ComboBox();
        private readonly CheckedListBox machineList = new CheckedListBox();
        private readonly Chart healthChart = NewChart();
        private readonly Chart speedChart = NewChart();
        private readonly Chart backupChart = NewChart();
        private readonly HourlyOutageHeatmap heatmap = new HourlyOutageHeatmap();
        private ScanResult current;
        private bool bindingMachines;
        private readonly Color[] palette = new[]
        {
            Color.FromArgb(28, 119, 206), Color.FromArgb(35, 157, 97), Color.FromArgb(219, 132, 52),
            Color.FromArgb(165, 83, 182), Color.FromArgb(35, 151, 163), Color.FromArgb(205, 74, 69)
        };

        internal TrendDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var heading = new Label { Text = "疊加比較電腦", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold), ForeColor = Ink };
            root.Controls.Add(heading, 0, 0);
            var periodPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(6, 4, 0, 0) };
            periodPanel.Controls.Add(new Label { Text = "顯示期間", AutoSize = true, Margin = new Padding(0, 8, 8, 0), ForeColor = Muted });
            periodBox.DropDownStyle = ComboBoxStyle.DropDownList;
            periodBox.Width = 110;
            periodBox.Items.AddRange(new object[] { "最近 7 天", "最近 30 天", "最近 90 天" });
            periodBox.SelectedIndex = 1;
            periodBox.SelectedIndexChanged += delegate { RebindCharts(); };
            periodPanel.Controls.Add(periodBox);
            periodPanel.Controls.Add(new Label { Text = "勾選多台即可疊加比較", AutoSize = true, Margin = new Padding(14, 8, 0, 0), ForeColor = Muted });
            root.Controls.Add(periodPanel, 1, 0);

            machineList.Dock = DockStyle.Fill;
            machineList.CheckOnClick = true;
            machineList.BorderStyle = BorderStyle.FixedSingle;
            machineList.ItemCheck += delegate { BeginInvoke((MethodInvoker)RebindCharts); };
            root.Controls.Add(machineList, 0, 1);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("連線率與延遲", healthChart));
            tabs.TabPages.Add(Page("測速趨勢", speedChart));
            tabs.TabPages.Add(Page("斷線時段熱點", heatmap));
            tabs.TabPages.Add(Page("備份回傳延遲", backupChart));
            root.Controls.Add(tabs, 1, 1);
        }

        internal void Bind(ScanResult result, string preferredMachineId)
        {
            current = result;
            var checkedIds = new HashSet<string>(CheckedMachineIds(), StringComparer.OrdinalIgnoreCase);
            if (checkedIds.Count == 0 && !String.IsNullOrWhiteSpace(preferredMachineId)) checkedIds.Add(preferredMachineId);
            bindingMachines = true;
            machineList.Items.Clear();
            if (result != null)
            {
                foreach (MachineSummary machine in result.Machines.OrderBy(delegate (MachineSummary value) { return value.MachineName; }))
                {
                    var item = new MachineChoice(machine.MachineId, machine.MachineName);
                    int index = machineList.Items.Add(item);
                    if (checkedIds.Contains(machine.MachineId)) machineList.SetItemChecked(index, true);
                }
                if (machineList.CheckedItems.Count == 0 && machineList.Items.Count > 0) machineList.SetItemChecked(0, true);
            }
            bindingMachines = false;
            RebindCharts();
        }

        internal void SelectMachine(string machineId)
        {
            if (String.IsNullOrWhiteSpace(machineId)) return;
            for (int i = 0; i < machineList.Items.Count; i++)
            {
                MachineChoice value = machineList.Items[i] as MachineChoice;
                if (value != null && String.Equals(value.Id, machineId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!machineList.GetItemChecked(i)) machineList.SetItemChecked(i, true);
                    break;
                }
            }
        }

        private void RebindCharts()
        {
            if (bindingMachines || current == null) return;
            List<string> ids = CheckedMachineIds();
            DateTime since = DateTime.Today.AddDays(-(PeriodDays() - 1));
            BindHealth(ids, since);
            BindSpeed(ids, since);
            BindBackup(ids, since);
            heatmap.Bind(current, ids, since);
        }

        private void BindHealth(List<string> ids, DateTime since)
        {
            PrepareChart(healthChart, "日期", "連線率 (%)", "延遲 (ms)");
            int colorIndex = 0;
            foreach (string id in ids)
            {
                MachineSummary machine = current.Machines.FirstOrDefault(delegate (MachineSummary value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase); });
                if (machine == null) continue;
                Color color = palette[colorIndex++ % palette.Length];
                List<DailySummary> days = current.Days.Where(delegate (DailySummary value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase) && value.Day >= since; }).OrderBy(delegate (DailySummary value) { return value.Day; }).ToList();
                Series availability = SeriesFor(machine.MachineName + " 連線率", color, SeriesChartType.Line, false, 3);
                Series average = SeriesFor(machine.MachineName + " 平均延遲", Lighten(color), SeriesChartType.Line, true, 2);
                Series maximum = SeriesFor(machine.MachineName + " 最高延遲", color, SeriesChartType.Line, true, 1);
                maximum.BorderDashStyle = ChartDashStyle.Dash;
                foreach (DailySummary day in days)
                {
                    availability.Points.AddXY(day.Day, day.AvailabilityPercent);
                    average.Points.AddXY(day.Day, day.AverageLatencyMs);
                    maximum.Points.AddXY(day.Day, day.MaxLatencyMs);
                }
                healthChart.Series.Add(availability); healthChart.Series.Add(average); healthChart.Series.Add(maximum);
            }
            healthChart.ChartAreas[0].AxisY.Minimum = 0; healthChart.ChartAreas[0].AxisY.Maximum = 100;
        }

        private void BindSpeed(List<string> ids, DateTime since)
        {
            PrepareChart(speedChart, "時間", "速度 (Mbps)", "延遲 (ms)");
            int colorIndex = 0;
            foreach (string id in ids)
            {
                MachineSummary machine = current.Machines.FirstOrDefault(delegate (MachineSummary value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase); });
                if (machine == null) continue;
                Color color = palette[colorIndex++ % palette.Length];
                List<SpeedRecord> rows = current.Speeds.Where(delegate (SpeedRecord value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase) && value.Time >= since && value.Status == "COMPLETED"; }).OrderBy(delegate (SpeedRecord value) { return value.Time; }).ToList();
                Series download = SeriesFor(machine.MachineName + " 下載", color, SeriesChartType.Line, false, 3);
                Series upload = SeriesFor(machine.MachineName + " 上傳", Lighten(color), SeriesChartType.Line, false, 2);
                upload.BorderDashStyle = ChartDashStyle.Dash;
                Series latency = SeriesFor(machine.MachineName + " 測速延遲", color, SeriesChartType.Line, true, 1);
                latency.BorderDashStyle = ChartDashStyle.Dot;
                foreach (SpeedRecord row in rows)
                {
                    download.Points.AddXY(row.Time, row.DownloadMbps);
                    upload.Points.AddXY(row.Time, row.UploadMbps);
                    latency.Points.AddXY(row.Time, row.LatencyMs);
                }
                speedChart.Series.Add(download); speedChart.Series.Add(upload); speedChart.Series.Add(latency);
            }
        }

        private void BindBackup(List<string> ids, DateTime since)
        {
            PrepareChart(backupChart, "資料時間", "回傳延遲 (小時)", "");
            backupChart.ChartAreas[0].AxisY2.Enabled = AxisEnabled.False;
            int colorIndex = 0;
            foreach (string id in ids)
            {
                MachineSummary machine = current.Machines.FirstOrDefault(delegate (MachineSummary value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase); });
                if (machine == null) continue;
                Series series = SeriesFor(machine.MachineName, palette[colorIndex++ % palette.Length], SeriesChartType.Line, false, 3);
                foreach (SourceFileInfo file in current.Files.Where(delegate (SourceFileInfo value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase) && value.Kind == "監控資料" && value.DataEndTime >= since; }).OrderBy(delegate (SourceFileInfo value) { return value.DataEndTime; }))
                {
                    double hours = Math.Max(0, (file.LastWriteTime - file.DataEndTime).TotalHours);
                    series.Points.AddXY(file.DataEndTime, hours);
                }
                backupChart.Series.Add(series);
            }
        }

        private List<string> CheckedMachineIds()
        {
            return machineList.CheckedItems.Cast<object>().Select(delegate (object value) { return ((MachineChoice)value).Id; }).ToList();
        }

        private int PeriodDays() { return periodBox.SelectedIndex == 0 ? 7 : periodBox.SelectedIndex == 2 ? 90 : 30; }

        private static Chart NewChart()
        {
            var chart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White, AntiAliasing = AntiAliasingStyles.All, TextAntiAliasingQuality = TextAntiAliasingQuality.High };
            chart.Legends.Add(new Legend { Docking = Docking.Bottom, Font = new Font("Microsoft JhengHei UI", 8F), BackColor = Color.White });
            return chart;
        }

        private void PrepareChart(Chart chart, string xTitle, string yTitle, string y2Title)
        {
            chart.Series.Clear(); chart.ChartAreas.Clear();
            var area = new ChartArea("Main");
            area.BackColor = Color.White;
            area.AxisX.Title = xTitle; area.AxisY.Title = yTitle; area.AxisY2.Title = y2Title;
            area.AxisX.LabelStyle.Format = PeriodDays() <= 7 ? "MM/dd HH:mm" : "MM/dd";
            area.AxisX.LabelStyle.Angle = -35;
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(236, 241, 244);
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(230, 237, 241);
            area.AxisY2.MajorGrid.Enabled = false;
            area.AxisY2.Enabled = String.IsNullOrWhiteSpace(y2Title) ? AxisEnabled.False : AxisEnabled.True;
            area.AxisX.LabelStyle.ForeColor = area.AxisY.LabelStyle.ForeColor = area.AxisY2.LabelStyle.ForeColor = Muted;
            chart.ChartAreas.Add(area);
        }

        private static Series SeriesFor(string name, Color color, SeriesChartType type, bool secondary, int width)
        {
            return new Series(name) { ChartType = type, Color = color, BorderWidth = width, XValueType = ChartValueType.DateTime, YValueType = ChartValueType.Double, YAxisType = secondary ? AxisType.Secondary : AxisType.Primary, ToolTip = name + "\n#VALX{yyyy/MM/dd HH:mm}\n#VALY{0.00}" };
        }

        private static Color Lighten(Color color)
        {
            return Color.FromArgb((color.R + 255) / 2, (color.G + 255) / 2, (color.B + 255) / 2);
        }

        private static TabPage Page(string title, Control control)
        {
            var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(6) };
            page.Controls.Add(control); control.Dock = DockStyle.Fill; return page;
        }

        private sealed class MachineChoice
        {
            internal readonly string Id;
            private readonly string name;
            internal MachineChoice(string id, string value) { Id = id; name = value + "  [" + id + "]"; }
            public override string ToString() { return name; }
        }
    }

    internal sealed class HourlyOutageHeatmap : Control
    {
        private ScanResult current;
        private List<string> ids = new List<string>();
        private DateTime since;

        internal HourlyOutageHeatmap()
        {
            Dock = DockStyle.Fill; BackColor = Color.White; DoubleBuffered = true; Font = new Font("Microsoft JhengHei UI", 8.5F);
        }

        internal void Bind(ScanResult result, List<string> machineIds, DateTime start)
        {
            current = result; ids = machineIds ?? new List<string>(); since = start; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (current == null || ids.Count == 0)
            {
                e.Graphics.DrawString("勾選電腦後顯示每小時斷線熱點。", Font, Brushes.Gray, 18, 18);
                return;
            }
            int labelWidth = 155;
            int top = 42;
            int cellWidth = Math.Max(18, (Width - labelWidth - 18) / 24);
            int rowHeight = Math.Max(34, Math.Min(48, (Height - top - 12) / Math.Max(1, ids.Count)));
            using (var textBrush = new SolidBrush(Color.FromArgb(71, 91, 109)))
            using (var border = new Pen(Color.FromArgb(230, 236, 240)))
            {
                for (int hour = 0; hour < 24; hour++) e.Graphics.DrawString(hour.ToString("00"), Font, textBrush, labelWidth + hour * cellWidth + 2, 15);
                int row = 0;
                foreach (string id in ids)
                {
                    MachineSummary machine = current.Machines.FirstOrDefault(delegate (MachineSummary value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase); });
                    if (machine == null) continue;
                    e.Graphics.DrawString(machine.MachineName, Font, textBrush, 8, top + row * rowHeight + 9);
                    int[] counts = new int[24];
                    foreach (OutageEvent outage in current.Outages.Where(delegate (OutageEvent value) { return String.Equals(value.MachineId, id, StringComparison.OrdinalIgnoreCase) && value.End >= since; }))
                    {
                        DateTime cursor = outage.Start < since ? since : outage.Start;
                        DateTime end = outage.End > DateTime.Now ? DateTime.Now : outage.End;
                        if (end < cursor) end = cursor;
                        var touched = new HashSet<int>();
                        while (cursor <= end)
                        {
                            touched.Add(cursor.Hour);
                            DateTime nextHour = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0).AddHours(1);
                            if (nextHour <= cursor) break;
                            cursor = nextHour;
                        }
                        foreach (int hour in touched) counts[hour]++;
                    }
                    int max = Math.Max(1, counts.Max());
                    for (int hour = 0; hour < 24; hour++)
                    {
                        double ratio = counts[hour] / (double)max;
                        Color color = counts[hour] == 0 ? Color.FromArgb(244, 248, 250) : Blend(Color.FromArgb(255, 231, 180), Color.FromArgb(205, 74, 69), ratio);
                        Rectangle box = new Rectangle(labelWidth + hour * cellWidth, top + row * rowHeight, cellWidth - 2, rowHeight - 5);
                        using (var fill = new SolidBrush(color)) e.Graphics.FillRectangle(fill, box);
                        e.Graphics.DrawRectangle(border, box);
                        if (counts[hour] > 0) e.Graphics.DrawString(counts[hour].ToString(), Font, Brushes.White, box.X + 4, box.Y + 8);
                    }
                    row++;
                }
            }
        }

        private static Color Blend(Color low, Color high, double ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            return Color.FromArgb((int)(low.R + (high.R - low.R) * ratio), (int)(low.G + (high.G - low.G) * ratio), (int)(low.B + (high.B - low.B) * ratio));
        }
    }
}
