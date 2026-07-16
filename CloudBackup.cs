using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetCheck
{
    internal sealed class CloudConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string AuthUri { get; set; }
        public string TokenUri { get; set; }
        public string RefreshToken { get; set; }
        public string FolderId { get; set; }
        public string Schedule { get; set; }
        public string LastBackupDay { get; set; }
    }

    internal sealed class CloudBackupManager : IDisposable
    {
        private const string EmbeddedClientId = "635420604050-ol0vfpmi07jcd8fe6hkpk1ki22vh3r4o.apps.googleusercontent.com";
        private const string GoogleAuthUri = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string GoogleTokenUri = "https://oauth2.googleapis.com/token";
        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private readonly string machineName;
        private readonly string machineId;
        private readonly string settingsPath;
        private readonly System.Threading.Timer scheduleTimer;
        private readonly object sync = new object();
        private CloudConfig config;
        private string accessToken;
        private DateTime accessTokenExpires;
        private int backupRunning;
        private string lastStatus;

        public CloudBackupManager(string computerName, string computerId)
        {
            machineName = computerName;
            machineId = computerId;
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_CLOUD_SETTINGS");
            settingsPath = String.IsNullOrEmpty(overridePath) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Cloud", "settings.dat") : overridePath;
            config = LoadConfig(settingsPath) ?? NewConfig();
            lastStatus = Connected ? L.T("Google Drive 已連接；等待每日備份。", "Google Drive is connected; waiting for the daily backup.") : L.T("尚未連接 Google Drive。", "Google Drive is not connected.");
            scheduleTimer = new System.Threading.Timer(delegate { CheckSchedule(); }, null, 30000, 60000);
        }

        public bool Connected { get { lock (sync) return !String.IsNullOrEmpty(config.RefreshToken) && !String.IsNullOrEmpty(config.ClientId); } }
        public bool BackupInProgress { get { return backupRunning != 0; } }
        public string LastStatus { get { lock (sync) return lastStatus; } }
        public TimeSpan ScheduleTime
        {
            get
            {
                lock (sync)
                {
                    TimeSpan value;
                    return TimeSpan.TryParseExact(config.Schedule, @"hh\:mm", CultureInfo.InvariantCulture, out value) ? value : new TimeSpan(23, 55, 0);
                }
            }
        }
        public string LastBackupDay { get { lock (sync) return config.LastBackupDay; } }

        public void SaveSchedule(TimeSpan value)
        {
            lock (sync) { config.Schedule = value.ToString(@"hh\:mm"); SaveConfig(settingsPath, config); lastStatus = L.T("每日備份時間已設為 ", "Daily backup time set to ") + config.Schedule + "."; }
        }

        public void Connect()
        {
            CloudConfig credentials = EmbeddedCredentials();
            OAuthResult result = Authorize(credentials);
            lock (sync)
            {
                config.ClientId = credentials.ClientId;
                config.ClientSecret = credentials.ClientSecret;
                config.AuthUri = credentials.AuthUri;
                config.TokenUri = credentials.TokenUri;
                config.RefreshToken = result.RefreshToken;
                config.FolderId = null;
                accessToken = result.AccessToken;
                accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, result.ExpiresIn - 60));
                SaveConfig(settingsPath, config);
                lastStatus = L.T("Google Drive 登入成功，將使用 Net_Check 資料夾。", "Google Drive sign-in succeeded. The Net_Check folder will be used.");
            }
            string token = GetAccessToken();
            EnsureFolder(token);
        }

        public void Disconnect()
        {
            lock (sync)
            {
                config = NewConfig();
                accessToken = null;
                accessTokenExpires = DateTime.MinValue;
                try { if (File.Exists(settingsPath)) File.Delete(settingsPath); } catch { }
                lastStatus = L.T("已移除本機 Google Drive 連線權杖。", "The local Google Drive connection token was removed.");
            }
        }

        public void BeginBackup(DateTime day, Action<bool, string> completed)
        {
            if (!Connected) { if (completed != null) completed(false, L.T("尚未連接 Google Drive。", "Google Drive is not connected.")); return; }
            if (Interlocked.Exchange(ref backupRunning, 1) == 1) { if (completed != null) completed(false, L.T("已有雲端備份正在進行。", "A cloud backup is already in progress.")); return; }
            lock (sync) lastStatus = L.T("正在製作 ", "Creating and uploading the ") + day.ToString("yyyy/MM/dd") + L.T(" 報表並上傳…", " report…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                bool ok = false; string message;
                string temp = Path.Combine(Path.GetTempPath(), "NetCheckCloud_" + Guid.NewGuid().ToString("N"));
                try
                {
                    string[] artifacts = ArchiveReport.ExportDailyArtifacts(temp, machineName, machineId, day.Date);
                    string token = GetAccessToken();
                    string folder = EnsureFolder(token);
                    UploadOrReplace(token, folder, artifacts[0], "application/pdf");
                    UploadOrReplace(token, folder, artifacts[1], "text/csv");
                    lock (sync) { config.LastBackupDay = day.ToString("yyyy-MM-dd"); SaveConfig(settingsPath, config); }
                    ok = true;
                    message = day.ToString("yyyy/MM/dd") + L.T(" PDF 與 CSV 已備份到 Google Drive / Net_Check。", " PDF and CSV were backed up to Google Drive / Net_Check.");
                }
                catch (Exception ex) { message = L.T("雲端備份失敗：", "Cloud backup failed: ") + ex.Message; }
                finally { try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { } SetThreadExecutionState(ES_CONTINUOUS); Interlocked.Exchange(ref backupRunning, 0); }
                lock (sync) lastStatus = message;
                if (completed != null) completed(ok, message);
            });
        }

        private void CheckSchedule()
        {
            if (!Connected || backupRunning != 0) return;
            DateTime now = DateTime.Now, due = DateTime.MinValue, last = DateTime.MinValue;
            lock (sync) DateTime.TryParseExact(config.LastBackupDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last);
            if (last.Date < now.Date.AddDays(-1)) due = now.Date.AddDays(-1);
            else if (now.TimeOfDay >= ScheduleTime && last.Date < now.Date) due = now.Date;
            if (due != DateTime.MinValue)
            {
                if (!ArchiveReport.HasChecksForDay(due))
                {
                    lock (sync) { config.LastBackupDay = due.ToString("yyyy-MM-dd"); lastStatus = due.ToString("yyyy/MM/dd") + L.T(" 沒有監控資料，已略過雲端備份。", " has no monitoring data; cloud backup was skipped."); SaveConfig(settingsPath, config); }
                    return;
                }
                BeginBackup(due, null);
            }
        }

        private string GetAccessToken()
        {
            lock (sync)
            {
                if (!String.IsNullOrEmpty(accessToken) && DateTime.UtcNow < accessTokenExpires) return accessToken;
                if (String.IsNullOrEmpty(config.RefreshToken)) throw new InvalidOperationException(L.T("Google Drive 登入已失效，請重新連接。", "The Google Drive sign-in has expired. Reconnect it."));
                var form = new Dictionary<string, string>();
                form["client_id"] = config.ClientId;
                if (!String.IsNullOrEmpty(config.ClientSecret)) form["client_secret"] = config.ClientSecret;
                form["refresh_token"] = config.RefreshToken;
                form["grant_type"] = "refresh_token";
                Dictionary<string, object> json = JsonObject(PostForm(String.IsNullOrEmpty(config.TokenUri) ? "https://oauth2.googleapis.com/token" : config.TokenUri, form));
                accessToken = GetString(json, "access_token");
                int seconds = GetInt(json, "expires_in", 3600);
                if (String.IsNullOrEmpty(accessToken)) throw new InvalidOperationException(L.T("Google 未回傳 access token。", "Google did not return an access token."));
                accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
                return accessToken;
            }
        }

        private string EnsureFolder(string token)
        {
            lock (sync) if (!String.IsNullOrEmpty(config.FolderId)) return config.FolderId;
            string query = "name = 'Net_Check' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            string url = "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(query) + "&spaces=drive&fields=files(id,name)&pageSize=10";
            string id = FirstFileId(JsonObject(ApiRequest("GET", url, token, null, null)));
            if (String.IsNullOrEmpty(id))
            {
                var meta = new Dictionary<string, object>(); meta["name"] = "Net_Check"; meta["mimeType"] = "application/vnd.google-apps.folder";
                id = GetString(JsonObject(ApiRequest("POST", "https://www.googleapis.com/drive/v3/files?fields=id", token, "application/json; charset=UTF-8", Utf8(Json(meta)))), "id");
            }
            if (String.IsNullOrEmpty(id)) throw new InvalidOperationException(L.T("無法建立或找到 Google Drive / Net_Check 資料夾。", "Could not create or find the Google Drive / Net_Check folder."));
            lock (sync) { config.FolderId = id; SaveConfig(settingsPath, config); }
            return id;
        }

        private void UploadOrReplace(string token, string folderId, string path, string mimeType)
        {
            string name = Path.GetFileName(path);
            string q = "name = '" + name.Replace("'", "\\'") + "' and '" + folderId + "' in parents and trashed = false";
            string listUrl = "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(q) + "&spaces=drive&fields=files(id,name)&pageSize=10";
            string existing = FirstFileId(JsonObject(ApiRequest("GET", listUrl, token, null, null)));
            byte[] content = File.ReadAllBytes(path);
            if (!String.IsNullOrEmpty(existing))
            {
                ApiRequest("PATCH", "https://www.googleapis.com/upload/drive/v3/files/" + Uri.EscapeDataString(existing) + "?uploadType=media", token, mimeType, content);
                return;
            }
            string boundary = "netcheck_" + Guid.NewGuid().ToString("N");
            var metadata = new Dictionary<string, object>(); metadata["name"] = name; metadata["parents"] = new string[] { folderId };
            byte[] prefix = Utf8("--" + boundary + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n" + Json(metadata) + "\r\n--" + boundary + "\r\nContent-Type: " + mimeType + "\r\n\r\n");
            byte[] suffix = Utf8("\r\n--" + boundary + "--\r\n");
            byte[] body = new byte[prefix.Length + content.Length + suffix.Length];
            Buffer.BlockCopy(prefix, 0, body, 0, prefix.Length); Buffer.BlockCopy(content, 0, body, prefix.Length, content.Length); Buffer.BlockCopy(suffix, 0, body, prefix.Length + content.Length, suffix.Length);
            ApiRequest("POST", "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id", token, "multipart/related; boundary=" + boundary, body);
        }

        private sealed class OAuthResult { public string AccessToken; public string RefreshToken; public int ExpiresIn; }

        private static OAuthResult Authorize(CloudConfig credentials)
        {
            string verifier = RandomUrlSafe(48), state = RandomUrlSafe(24);
            string challenge; using (SHA256 sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://127.0.0.1:" + port + "/";
            string auth = String.IsNullOrEmpty(credentials.AuthUri) ? "https://accounts.google.com/o/oauth2/v2/auth" : credentials.AuthUri;
            string scope = "https://www.googleapis.com/auth/drive.file";
            string url = auth + "?client_id=" + E(credentials.ClientId) + "&redirect_uri=" + E(redirect) + "&response_type=code&scope=" + E(scope) + "&access_type=offline&prompt=consent&code_challenge=" + E(challenge) + "&code_challenge_method=S256&state=" + E(state);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            TcpClient client = null;
            try
            {
                IAsyncResult pending = listener.BeginAcceptTcpClient(null, null);
                if (!pending.AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(5))) throw new TimeoutException(L.T("等待 Google 登入超過 5 分鐘。", "Waiting for Google sign-in exceeded 5 minutes."));
                client = listener.EndAcceptTcpClient(pending);
                string requestLine;
                using (var reader = new StreamReader(client.GetStream(), Encoding.ASCII, false, 1024, true))
                {
                    requestLine = reader.ReadLine(); string line; do { line = reader.ReadLine(); } while (!String.IsNullOrEmpty(line));
                }
                string target = requestLine == null ? "" : requestLine.Split(' ')[1];
                Dictionary<string, string> query = ParseQuery(new Uri("http://127.0.0.1" + target).Query);
                string html = "<!doctype html><html lang='" + L.HtmlLanguage + "'><meta charset='utf-8'><title>NetCheckMonitor</title><body style='font-family:sans-serif;padding:40px'><h1>NetCheckMonitor Google Drive</h1><p>" + WebUtility.HtmlEncode(L.T("登入完成，可以關閉此視窗並回到 NetCheckMonitor。", "Sign-in is complete. You can close this window and return to NetCheckMonitor.")) + "</p></body></html>";
                byte[] responseBody = Utf8(html);
                byte[] headers = Utf8("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + responseBody.Length + "\r\nConnection: close\r\n\r\n");
                client.GetStream().Write(headers, 0, headers.Length); client.GetStream().Write(responseBody, 0, responseBody.Length); client.GetStream().Flush();
                if (!query.ContainsKey("state") || query["state"] != state) throw new InvalidOperationException(L.T("Google 登入狀態驗證失敗。", "Google sign-in state validation failed."));
                if (query.ContainsKey("error")) throw new InvalidOperationException(L.T("Google 登入未完成：", "Google sign-in was not completed: ") + query["error"]);
                if (!query.ContainsKey("code")) throw new InvalidOperationException(L.T("Google 未回傳授權碼。", "Google did not return an authorization code."));
                var form = new Dictionary<string, string>(); form["code"] = query["code"]; form["client_id"] = credentials.ClientId; if (!String.IsNullOrEmpty(credentials.ClientSecret)) form["client_secret"] = credentials.ClientSecret; form["redirect_uri"] = redirect; form["grant_type"] = "authorization_code"; form["code_verifier"] = verifier;
                Dictionary<string, object> token = JsonObject(PostForm(String.IsNullOrEmpty(credentials.TokenUri) ? "https://oauth2.googleapis.com/token" : credentials.TokenUri, form));
                var result = new OAuthResult { AccessToken = GetString(token, "access_token"), RefreshToken = GetString(token, "refresh_token"), ExpiresIn = GetInt(token, "expires_in", 3600) };
                if (String.IsNullOrEmpty(result.RefreshToken)) throw new InvalidOperationException(L.T("Google 未提供離線 refresh token，請移除既有授權後重新登入。", "Google did not provide an offline refresh token. Remove the existing authorization and sign in again."));
                return result;
            }
            finally { try { if (client != null) client.Close(); } catch { } listener.Stop(); }
        }

        private static CloudConfig EmbeddedCredentials()
        {
            return new CloudConfig { ClientId = EmbeddedClientId, AuthUri = GoogleAuthUri, TokenUri = GoogleTokenUri, Schedule = "23:55" };
        }

        private static CloudConfig NewConfig() { return new CloudConfig { Schedule = "23:55" }; }
        private static CloudConfig LoadConfig(string path)
        {
            try { if (!File.Exists(path)) return null; byte[] encrypted = File.ReadAllBytes(path); byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser); return new JavaScriptSerializer().Deserialize<CloudConfig>(Encoding.UTF8.GetString(plain)); } catch { return null; }
        }
        private static void SaveConfig(string path, CloudConfig value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); byte[] plain = Utf8(new JavaScriptSerializer().Serialize(value)); byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); string temp = path + ".tmp"; File.WriteAllBytes(temp, encrypted); if (File.Exists(path)) File.Delete(path); File.Move(temp, path);
        }

        public static bool RunStorageSelfTest(string path)
        {
            try { var c = NewConfig(); c.ClientId = "test-client"; c.RefreshToken = "test-refresh"; c.Schedule = "21:30"; SaveConfig(path, c); CloudConfig loaded = LoadConfig(path); return loaded != null && loaded.ClientId == c.ClientId && loaded.RefreshToken == c.RefreshToken && loaded.Schedule == c.Schedule; } finally { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        }

        private static string ApiRequest(string method, string url, string token, string contentType, byte[] body)
        {
            var request = (HttpWebRequest)WebRequest.Create(url); request.Method = method; request.Timeout = 60000; request.ReadWriteTimeout = 60000; request.UserAgent = "NetCheckMonitor/0.9.2"; request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            if (body != null) { request.ContentType = contentType; request.ContentLength = body.Length; using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length); }
            try { using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd(); }
            catch (WebException ex) { throw new InvalidOperationException(ReadWebError(ex)); }
        }
        private static string PostForm(string url, Dictionary<string, string> values)
        {
            var parts = new List<string>(); foreach (var p in values) parts.Add(E(p.Key) + "=" + E(p.Value)); byte[] body = Utf8(String.Join("&", parts.ToArray()));
            var request = (HttpWebRequest)WebRequest.Create(url); request.Method = "POST"; request.ContentType = "application/x-www-form-urlencoded"; request.ContentLength = body.Length; request.Timeout = 60000; using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            try { using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd(); }
            catch (WebException ex) { throw new InvalidOperationException(ReadWebError(ex)); }
        }
        private static string ReadWebError(WebException ex) { try { using (var reader = new StreamReader(ex.Response.GetResponseStream())) return reader.ReadToEnd(); } catch { return ex.Message; } }
        private static Dictionary<string, object> JsonObject(string json) { var value = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; if (value == null) throw new InvalidDataException(L.T("Google 回傳的 JSON 格式錯誤。", "Google returned invalid JSON.")); return value; }
        private static string Json(object value) { return new JavaScriptSerializer().Serialize(value); }
        private static string FirstFileId(Dictionary<string, object> json) { object filesObj; if (!json.TryGetValue("files", out filesObj)) return null; object[] files = filesObj as object[]; if (files == null || files.Length == 0) return null; var first = files[0] as Dictionary<string, object>; return first == null ? null : GetString(first, "id"); }
        private static string GetString(Dictionary<string, object> d, string key) { object value; return d.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null; }
        private static int GetInt(Dictionary<string, object> d, string key, int fallback) { object value; int parsed; return d.TryGetValue(key, out value) && Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback; }
        private static Dictionary<string, string> ParseQuery(string query) { var result = new Dictionary<string, string>(); foreach (string part in query.TrimStart('?').Split('&')) { if (String.IsNullOrEmpty(part)) continue; string[] pair = part.Split(new char[] { '=' }, 2); result[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : ""; } return result; }
        private static byte[] Utf8(string value) { return new UTF8Encoding(false).GetBytes(value ?? ""); }
        private static string E(string value) { return Uri.EscapeDataString(value ?? ""); }
        private static string RandomUrlSafe(int bytes) { byte[] data = new byte[bytes]; using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(data); return Base64Url(data); }
        private static string Base64Url(byte[] data) { return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        public void Dispose() { scheduleTimer.Dispose(); }
    }

    internal sealed class CloudBackupForm : Form
    {
        private readonly CloudBackupManager manager;
        private readonly Label connection = new Label();
        private readonly Label status = new Label();
        private readonly DateTimePicker timePicker = new DateTimePicker();
        private readonly Button connect = new Button();
        private readonly Button backupNow = new Button();
        private readonly Button disconnect = new Button();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();

        public CloudBackupForm(CloudBackupManager cloud)
        {
            manager = cloud; Text = L.T("Google Drive 每日雲端備份", "Google Drive Daily Backup"); Font = new Font("Microsoft JhengHei UI", 10F); ClientSize = new Size(660, 360); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var title = new Label { Text = L.T("Google Drive 每日雲端備份", "Google Drive Daily Backup"), Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 18) };
            connection.SetBounds(27, 62, 600, 28); connection.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            var explain = new Label { Text = L.T("登入自己的 Google 帳號並同意權限後，每日會把完整 PDF 與原始 CSV 上傳到 Drive / Net_Check。\n程式必須保持執行；錯過時間時，下次啟動會優先補傳最近一天。", "Sign in with your Google account and grant permission to upload the full PDF and raw CSV to Drive / Net_Check each day.\nThe program must remain running. If a backup is missed, the most recent day is uploaded first at the next start."), AutoSize = false, Location = new Point(27, 94), Size = new Size(610, 48), ForeColor = Color.DimGray };
            var scheduleLabel = new Label { Text = L.T("每日備份時間", "Daily Backup Time"), AutoSize = true, Location = new Point(28, 157) };
            timePicker.Format = DateTimePickerFormat.Custom; timePicker.CustomFormat = "HH:mm"; timePicker.ShowUpDown = true; timePicker.SetBounds(140, 152, 95, 28); timePicker.Value = DateTime.Today.Add(manager.ScheduleTime);
            var saveTime = new Button { Text = L.T("儲存時間", "Save Time"), Location = new Point(245, 151), Size = new Size(105, 32) };
            connect.Text = L.T("登入 Google Drive", "Sign in to Google Drive"); connect.SetBounds(28, 205, 165, 40);
            backupNow.Text = L.T("立即備份今天", "Back Up Today Now"); backupNow.SetBounds(203, 205, 145, 40);
            disconnect.Text = L.T("中斷連線", "Disconnect"); disconnect.SetBounds(358, 205, 120, 40); disconnect.ForeColor = Color.Firebrick;
            status.SetBounds(28, 263, 604, 48); status.ForeColor = Color.DimGray;
            var close = new Button { Text = L.T("關閉", "Close"), Location = new Point(512, 315), Size = new Size(120, 32) };
            Controls.AddRange(new Control[] { title, connection, explain, scheduleLabel, timePicker, saveTime, connect, backupNow, disconnect, status, close });
            saveTime.Click += delegate { manager.SaveSchedule(timePicker.Value.TimeOfDay); RefreshState(); };
            connect.Click += delegate { Connect(); };
            backupNow.Click += delegate { BackupNow(); };
            disconnect.Click += delegate { if (MessageBox.Show(L.T("確定移除這台電腦儲存的 Google Drive 登入權杖嗎？雲端檔案不會刪除。", "Remove the Google Drive sign-in token stored on this computer? Cloud files will not be deleted."), L.T("中斷連線", "Disconnect"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { manager.Disconnect(); RefreshState(); } };
            close.Click += delegate { Close(); };
            refresh.Interval = 1000; refresh.Tick += delegate { RefreshState(); }; refresh.Start(); FormClosed += delegate { refresh.Dispose(); };
            RefreshState();
        }

        private void Connect()
        {
            SetBusy(true); status.Text = L.T("等待系統瀏覽器完成 Google 登入與授權…", "Waiting for Google sign-in and permission in your system browser…");
            ThreadPool.QueueUserWorkItem(delegate { string error = null; try { manager.Connect(); } catch (Exception ex) { error = ex.Message; } if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { SetBusy(false); RefreshState(); if (error != null) MessageBox.Show(error, L.T("Google Drive 登入失敗", "Google Drive Sign-in Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }); });
        }
        private void BackupNow() { SetBusy(true); manager.BeginBackup(DateTime.Today, delegate (bool ok, string message) { if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { SetBusy(false); RefreshState(); MessageBox.Show(message, ok ? L.T("備份完成", "Backup Complete") : L.T("備份失敗", "Backup Failed"), MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error); }); }); }
        private void RefreshState() { connection.Text = manager.Connected ? L.T("狀態：已連接 Google Drive", "Status: Google Drive connected") : L.T("狀態：尚未連接", "Status: Not connected"); connection.ForeColor = manager.Connected ? Color.SeaGreen : Color.DimGray; backupNow.Enabled = disconnect.Enabled = manager.Connected && connect.Enabled; status.Text = manager.LastStatus + (String.IsNullOrEmpty(manager.LastBackupDay) ? "" : L.T("\n最後成功備份日期：", "\nLast successful backup date: ") + manager.LastBackupDay); }
        private void SetBusy(bool busy) { connect.Enabled = backupNow.Enabled = disconnect.Enabled = timePicker.Enabled = !busy; }
    }
}
