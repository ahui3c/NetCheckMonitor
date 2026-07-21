using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetCheck
{
    internal static class SpeedTrendReport
    {
        private sealed class Item
        {
            internal DateTime Time; internal string Status; internal string Level; internal string Mode; internal double Down; internal double Up; internal double Latency; internal double Jitter; internal long Bytes; internal string Adapter; internal string ConnectionType; internal int WifiSignal; internal string Error;
        }
        private sealed class Daily
        {
            internal DateTime Day; internal int Count; internal double Down; internal double Up; internal double Latency; internal double MinDown = Double.MaxValue; internal double MaxDown; internal long Bytes;
        }

        internal static string Create(string machineName, string machineId)
        {
            List<Item> items = Load(machineId);
            string output = Path.Combine(SpeedTestStorage.DataDirectory(), "NetCheck_SpeedTrend_" + SpeedTestStorage.Safe(machineName, 16) + "-" + machineId + ".html");
            File.WriteAllText(output, BuildHtml(machineName, machineId, items), new UTF8Encoding(true));
            return output;
        }

        internal static void Open(string machineName, string machineId)
        {
            string path = Create(machineName, machineId);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private static List<Item> Load(string machineId)
        {
            var result = new List<Item>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string directory in DataDirectories())
            {
                if (!Directory.Exists(directory)) continue;
                foreach (string path in Directory.GetFiles(directory, "NetCheck_Speed_*.csv"))
                {
                    try
                    {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (!seen.Add(line)) continue;
                                List<string> f = ParseCsv(line);
                                if (f.Count < 6 || f[1] != "SPEEDTEST") continue;
                                DateTime time; if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) continue;
                                var values = ParseDetail(f[5]);
                                string id; if (values.TryGetValue("MachineId", out id) && !String.IsNullOrEmpty(machineId) && !String.Equals(id, machineId, StringComparison.OrdinalIgnoreCase)) continue;
                                var item = new Item { Time = time, Status = f[2] };
                                Double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out item.Latency);
                                Get(values, "Level", out item.Level); Get(values, "Mode", out item.Mode); GetDouble(values, "DownloadMbps", out item.Down); GetDouble(values, "UploadMbps", out item.Up); GetDouble(values, "JitterMs", out item.Jitter);
                                long downBytes, upBytes; GetLong(values, "DownloadBytes", out downBytes); GetLong(values, "UploadBytes", out upBytes); item.Bytes = downBytes + upBytes;
                                string encoded; if (values.TryGetValue("Adapter", out encoded)) item.Adapter = SpeedTestStorage.FromB64(encoded); if (values.TryGetValue("ConnectionType", out encoded)) item.ConnectionType = SpeedTestStorage.FromB64(encoded); if (values.TryGetValue("Error", out encoded)) item.Error = SpeedTestStorage.FromB64(encoded);
                                string signal; item.WifiSignal = values.TryGetValue("WifiSignal", out signal) && Int32.TryParse(signal, out item.WifiSignal) ? item.WifiSignal : -1;
                                result.Add(item);
                            }
                        }
                    }
                    catch { }
                }
            }
            result.Sort(delegate (Item a, Item b) { return a.Time.CompareTo(b.Time); });
            return result;
        }

        private static string BuildHtml(string machineName, string machineId, List<Item> items)
        {
            var completed = items.FindAll(delegate (Item i) { return i.Status == "COMPLETED"; });
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='" + L.HtmlLanguage + "'><head><meta charset='utf-8'><title>" + H(L.T("網路速度趨勢報表", "Network Speed Trend Report")) + "</title><style>*{box-sizing:border-box}body{font-family:'Microsoft JhengHei UI','Segoe UI',sans-serif;color:#17202a;font-size:15px;margin:28px}h1{font-size:28px;margin:0 0 6px}h2{font-size:19px;margin:0 0 12px}.sub,.foot{color:#61707c}.card{border:1px solid #dfe4e8;border-radius:9px;padding:15px;margin:15px 0}.grid{display:grid;grid-template-columns:repeat(5,1fr);gap:9px}.metric{background:#f3f6f8;border-left:4px solid #6c5ce7;padding:10px}.metric b{display:block;font-size:19px;margin-top:4px}.chart{width:100%;height:260px;background:#fafbfc;border:1px solid #e7eaed}.down{fill:none;stroke:#277da1;stroke-width:3}.up{fill:none;stroke:#43aa8b;stroke-width:3}.dot-down{fill:#277da1}.dot-up{fill:#43aa8b}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:8px;border-bottom:1px solid #e5e7e9;text-align:left}th{background:#f3f6f8}.good{color:#16834b}.bad{color:#b03a2e}.legend span{margin-right:22px}.swatch{display:inline-block;width:18px;height:4px;vertical-align:middle;margin-right:5px}@media print{@page{size:A4 landscape;margin:10mm}body{margin:0;font-size:11px}.card{page-break-inside:avoid}table{font-size:10px}}</style></head><body>");
            sb.Append("<h1>" + H(L.T("網路速度趨勢報表", "Network Speed Trend Report")) + "</h1><div class='sub'>" + H(machineName + " [" + machineId + "]") + L.T("｜產生時間：", " | Generated: ") + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "</div>");
            if (completed.Count == 0)
            {
                sb.Append("<div class='card'>" + H(L.T("目前沒有已完成的測速資料。測速失敗或取消的紀錄仍會顯示在下方。", "No completed speed-test data is available. Failed or cancelled attempts are still listed below.")) + "</div>");
            }
            else
            {
                Item latest = completed[completed.Count - 1];
                sb.Append("<div class='card grid'>"); Metric(sb, L.T("最近下載", "Latest Download"), latest.Down.ToString("0.0") + " Mbps"); Metric(sb, L.T("最近上傳", "Latest Upload"), latest.Up.ToString("0.0") + " Mbps"); Metric(sb, L.T("最近延遲", "Latest Latency"), latest.Latency.ToString("0.0") + " ms"); Metric(sb, L.T("測速次數", "Completed Tests"), completed.Count.ToString()); Metric(sb, L.T("累積測試流量", "Test Traffic"), Bytes(completed)); sb.Append("</div>");
                sb.Append("<div class='card'><h2>" + H(L.T("下載／上傳速度趨勢", "Download / Upload Trend")) + "</h2><div class='legend'><span><i class='swatch' style='background:#277da1'></i>" + H(L.T("下載", "Download")) + "</span><span><i class='swatch' style='background:#43aa8b'></i>" + H(L.T("上傳", "Upload")) + "</span></div>" + Chart(completed) + "</div>");
                AppendDaily(sb, completed);
            }
            AppendDetails(sb, items);
            sb.Append("<div class='foot'>" + H(L.T("測速使用 Cloudflare 測速端點。測速數據獨立保存，不納入斷線次數、有效監控時間或斷線百分比。結果會受 Wi-Fi 訊號、其他裝置流量、測速伺服器路由及電腦效能影響。", "Speed tests use Cloudflare endpoints. Results are stored separately and do not affect outage counts, effective monitoring time, or outage percentage. Wi-Fi signal, other network traffic, server routing, and computer performance can affect results.")) + "</div></body></html>");
            return sb.ToString();
        }

        private static string Chart(List<Item> items)
        {
            int count = items.Count, width = 1000, height = 240, pad = 32;
            double max = 1; foreach (Item i in items) max = Math.Max(max, Math.Max(i.Down, i.Up));
            var down = new StringBuilder(); var up = new StringBuilder(); var dots = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                double x = count == 1 ? width / 2.0 : pad + i * (width - 2.0 * pad) / (count - 1);
                double yd = height - pad - items[i].Down * (height - 2.0 * pad) / max, yu = height - pad - items[i].Up * (height - 2.0 * pad) / max;
                if (i > 0) { down.Append(' '); up.Append(' '); } down.Append(x.ToString("0.0", CultureInfo.InvariantCulture) + "," + yd.ToString("0.0", CultureInfo.InvariantCulture)); up.Append(x.ToString("0.0", CultureInfo.InvariantCulture) + "," + yu.ToString("0.0", CultureInfo.InvariantCulture));
                string tip = H(items[i].Time.ToString("yyyy/MM/dd HH:mm") + " | ↓ " + items[i].Down.ToString("0.0") + " Mbps | ↑ " + items[i].Up.ToString("0.0") + " Mbps");
                dots.Append("<circle class='dot-down' cx='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' cy='" + yd.ToString("0.0", CultureInfo.InvariantCulture) + "' r='4'><title>" + tip + "</title></circle><circle class='dot-up' cx='" + x.ToString("0.0", CultureInfo.InvariantCulture) + "' cy='" + yu.ToString("0.0", CultureInfo.InvariantCulture) + "' r='4'><title>" + tip + "</title></circle>");
            }
            return "<svg class='chart' viewBox='0 0 1000 240' preserveAspectRatio='none'><line x1='32' y1='208' x2='968' y2='208' stroke='#ccd2d7'/><polyline class='down' points='" + down + "'/><polyline class='up' points='" + up + "'/>" + dots + "</svg>";
        }

        private static void AppendDaily(StringBuilder sb, List<Item> items)
        {
            var map = new SortedDictionary<DateTime, Daily>();
            foreach (Item i in items)
            {
                Daily d; if (!map.TryGetValue(i.Time.Date, out d)) { d = new Daily { Day = i.Time.Date }; map[i.Time.Date] = d; }
                d.Count++; d.Down += i.Down; d.Up += i.Up; d.Latency += i.Latency; d.MinDown = Math.Min(d.MinDown, i.Down); d.MaxDown = Math.Max(d.MaxDown, i.Down); d.Bytes += i.Bytes;
            }
            var days = new List<Daily>(map.Values); days.Sort(delegate (Daily a, Daily b) { return b.Day.CompareTo(a.Day); });
            sb.Append("<div class='card'><h2>" + H(L.T("每日速度統計", "Daily Speed Statistics")) + "</h2><table><tr><th>" + H(L.T("日期", "Date")) + "</th><th>" + H(L.T("次數", "Tests")) + "</th><th>" + H(L.T("平均下載", "Average Download")) + "</th><th>" + H(L.T("最低／最高下載", "Min / Max Download")) + "</th><th>" + H(L.T("平均上傳", "Average Upload")) + "</th><th>" + H(L.T("平均延遲", "Average Latency")) + "</th><th>" + H(L.T("流量", "Traffic")) + "</th></tr>");
            foreach (Daily d in days) sb.Append("<tr><td>" + d.Day.ToString("yyyy/MM/dd") + "</td><td>" + d.Count + "</td><td>" + (d.Down / d.Count).ToString("0.0") + " Mbps</td><td>" + d.MinDown.ToString("0.0") + " / " + d.MaxDown.ToString("0.0") + " Mbps</td><td>" + (d.Up / d.Count).ToString("0.0") + " Mbps</td><td>" + (d.Latency / d.Count).ToString("0.0") + " ms</td><td>" + FormatBytes(d.Bytes) + "</td></tr>");
            sb.Append("</table></div>");
        }

        private static void AppendDetails(StringBuilder sb, List<Item> items)
        {
            var rows = new List<Item>(items); rows.Sort(delegate (Item a, Item b) { return b.Time.CompareTo(a.Time); });
            sb.Append("<div class='card'><h2>" + H(L.T("完整測速紀錄", "Complete Speed-Test Records")) + "</h2><table><tr><th>" + H(L.T("時間", "Time")) + "</th><th>" + H(L.T("狀態／模式", "Status / Mode")) + "</th><th>" + H(L.T("等級", "Level")) + "</th><th>" + H(L.T("下載", "Download")) + "</th><th>" + H(L.T("上傳", "Upload")) + "</th><th>" + H(L.T("延遲／抖動", "Latency / Jitter")) + "</th><th>" + H(L.T("網路", "Network")) + "</th><th>" + H(L.T("流量／錯誤", "Traffic / Error")) + "</th></tr>");
            foreach (Item i in rows)
            {
                bool ok = i.Status == "COMPLETED"; string status = ok ? L.T("完成", "Completed") : (i.Status == "CANCELLED" ? L.T("取消", "Cancelled") : L.T("失敗", "Failed"));
                string mode = i.Mode == "Scheduled" ? L.T("定時", "Scheduled") : L.T("手動", "Manual");
                string network = (i.ConnectionType ?? "") + (String.IsNullOrEmpty(i.Adapter) ? "" : " | " + i.Adapter) + (i.WifiSignal >= 0 ? " | Wi-Fi " + i.WifiSignal + "%" : "");
                sb.Append("<tr><td>" + i.Time.ToString("yyyy/MM/dd HH:mm:ss") + "</td><td class='" + (ok ? "good" : "bad") + "'>" + H(status + " / " + mode) + "</td><td>" + H(i.Level) + "</td><td>" + (ok ? i.Down.ToString("0.0") + " Mbps" : "—") + "</td><td>" + (ok ? i.Up.ToString("0.0") + " Mbps" : "—") + "</td><td>" + (ok ? i.Latency.ToString("0.0") + " / " + i.Jitter.ToString("0.0") + " ms" : "—") + "</td><td>" + H(network) + "</td><td>" + H(ok ? FormatBytes(i.Bytes) : (i.Error ?? "")) + "</td></tr>");
            }
            sb.Append("</table></div>");
        }

        private static void Metric(StringBuilder sb, string name, string value) { sb.Append("<div class='metric'>" + H(name) + "<b>" + H(value) + "</b></div>"); }
        private static string Bytes(List<Item> items) { long total = 0; foreach (Item i in items) total += i.Bytes; return FormatBytes(total); }
        private static string FormatBytes(long value) { if (value >= 1073741824) return (value / 1073741824.0).ToString("0.00") + " GB"; return (value / 1048576.0).ToString("0.0") + " MB"; }
        private static string H(string value) { return System.Net.WebUtility.HtmlEncode(value ?? ""); }
        private static void Get(Dictionary<string, string> d, string key, out string value) { if (!d.TryGetValue(key, out value)) value = ""; }
        private static void GetDouble(Dictionary<string, string> d, string key, out double value) { string s; value = d.TryGetValue(key, out s) ? ParseDouble(s) : 0; }
        private static void GetLong(Dictionary<string, string> d, string key, out long value) { string s; value = d.TryGetValue(key, out s) && Int64.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0; }
        private static double ParseDouble(string value) { double result; return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0; }
        private static Dictionary<string, string> ParseDetail(string value) { var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (string p in (value ?? "").Split(';')) { int i = p.IndexOf('='); if (i > 0) d[p.Substring(0, i)] = p.Substring(i + 1); } return d; }
        private static List<string> ParseCsv(string line) { var r = new List<string>(); var b = new StringBuilder(); bool q = false; for (int i = 0; i < (line ?? "").Length; i++) { char c = line[i]; if (c == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { b.Append('"'); i++; } else q = !q; } else if (c == ',' && !q) { r.Add(b.ToString()); b.Length = 0; } else b.Append(c); } r.Add(b.ToString()); return r; }
        private static List<string> DataDirectories()
        {
            var r = new List<string>(); string env = Environment.GetEnvironmentVariable("NETCHECK_DATA_ROOTS"); if (!String.IsNullOrWhiteSpace(env)) r.AddRange(env.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            r.Add(SpeedTestStorage.DataDirectory()); r.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NetCheck_Data")); r.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Data")); r.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Recovery"));
            var unique = new List<string>(); foreach (string p in r) if (!String.IsNullOrWhiteSpace(p) && !unique.Exists(delegate (string x) { return String.Equals(Path.GetFullPath(x), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase); })) unique.Add(p); return unique;
        }
    }
}
