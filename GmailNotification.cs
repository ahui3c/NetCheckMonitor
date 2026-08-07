using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetCheck
{
    internal sealed class GmailPendingRecovery
    {
        public string Id { get; set; }
        public DateTime OutageStart { get; set; }
        public DateTime RecoveredAt { get; set; }
        public int FailedChecks { get; set; }
        public int Attempts { get; set; }
        public DateTime NextAttemptUtc { get; set; }
    }

    internal sealed class GmailNotificationConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string AuthUri { get; set; }
        public string TokenUri { get; set; }
        public string RefreshToken { get; set; }
        public string AccountEmail { get; set; }
        public bool DailyReportEnabled { get; set; }
        public bool RecoveryNotificationEnabled { get; set; }
        public string Schedule { get; set; }
        public string LastReportDay { get; set; }
        public int DailyAttempts { get; set; }
        public DateTime NextDailyAttemptUtc { get; set; }
        public List<GmailPendingRecovery> PendingRecoveries { get; set; }
    }

    internal sealed class GmailNotificationManager : IDisposable
    {
        private const string EmbeddedClientId = "635420604050-ol0vfpmi07jcd8fe6hkpk1ki22vh3r4o.apps.googleusercontent.com";
        private const string GoogleAuthUri = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string GoogleTokenUri = "https://oauth2.googleapis.com/token";
        private const string GmailSendScope = "https://www.googleapis.com/auth/gmail.send";
        private readonly string machineName;
        private readonly string machineId;
        private readonly string settingsPath;
        private readonly object sync = new object();
        private readonly System.Threading.Timer scheduleTimer;
        private GmailNotificationConfig config;
        private string accessToken;
        private DateTime accessTokenExpires;
        private int sendRunning;
        private bool disposed;
        private string lastStatus;

        public GmailNotificationManager(string computerName, string computerId)
        {
            machineName = computerName;
            machineId = computerId;
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_GMAIL_SETTINGS");
            settingsPath = String.IsNullOrWhiteSpace(overridePath) ? PortableSettingsStore.GmailPath : overridePath;
            config = LoadConfig(settingsPath) ?? NewConfig();
            NormalizeConfig(config);
            lastStatus = Connected
                ? L.T("Gmail 已連接；通知只會寄給登入帳戶。", "Gmail is connected; messages are sent only to the signed-in account.")
                : L.T("尚未連接 Gmail。", "Gmail is not connected.");
            scheduleTimer = new System.Threading.Timer(delegate { CheckSchedule(); }, null, 30000, 60000);
        }

        public bool Connected
        {
            get
            {
                lock (sync)
                    return !String.IsNullOrWhiteSpace(config.RefreshToken)
                        && !String.IsNullOrWhiteSpace(config.ClientId)
                        && IsSafeEmail(config.AccountEmail);
            }
        }

        public bool SendInProgress { get { return Volatile.Read(ref sendRunning) != 0; } }
        public string AccountEmail { get { lock (sync) return config.AccountEmail; } }
        public string LastStatus { get { lock (sync) return lastStatus; } }
        public string LastReportDay { get { lock (sync) return config.LastReportDay; } }
        public bool DailyReportEnabled { get { lock (sync) return config.DailyReportEnabled; } }
        public bool RecoveryNotificationEnabled { get { lock (sync) return config.RecoveryNotificationEnabled; } }
        public int PendingRecoveryCount { get { lock (sync) return config.PendingRecoveries.Count; } }
        public TimeSpan ScheduleTime
        {
            get
            {
                lock (sync)
                {
                    TimeSpan parsed;
                    return TimeSpan.TryParseExact(config.Schedule, @"hh\:mm", CultureInfo.InvariantCulture, out parsed)
                        ? parsed : new TimeSpan(23, 50, 0);
                }
            }
        }

        public void SaveOptions(bool dailyReport, bool recoveryNotification, TimeSpan schedule)
        {
            if (schedule < TimeSpan.Zero || schedule >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException("schedule");
            lock (sync)
            {
                config.DailyReportEnabled = dailyReport;
                config.RecoveryNotificationEnabled = recoveryNotification;
                config.Schedule = schedule.ToString(@"hh\:mm");
                config.DailyAttempts = 0;
                config.NextDailyAttemptUtc = DateTime.MinValue;
                SaveConfig(settingsPath, config);
                lastStatus = L.T("Gmail 報表與通知設定已儲存。", "Gmail report and notification settings were saved.");
            }
            CheckSchedule();
        }

        public void Connect()
        {
            GmailNotificationConfig credentials = EmbeddedCredentials();
            if (String.IsNullOrEmpty(credentials.ClientSecret))
                throw new InvalidOperationException(L.T("此版本缺少 Google 登入憑證，請更新程式後再試。", "This build is missing its Google sign-in credential. Update the app and try again."));
            OAuthResult result = Authorize(credentials);
            string email = ReadSignedInEmail(result.AccessToken);
            if (!IsSafeEmail(email)) throw new InvalidOperationException(L.T("Google 未回傳可用的登入信箱。", "Google did not return a usable signed-in email address."));
            lock (sync)
            {
                config.ClientId = credentials.ClientId;
                config.ClientSecret = credentials.ClientSecret;
                config.AuthUri = credentials.AuthUri;
                config.TokenUri = credentials.TokenUri;
                config.RefreshToken = result.RefreshToken;
                config.AccountEmail = email;
                accessToken = result.AccessToken;
                accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, result.ExpiresIn - 60));
                SaveConfig(settingsPath, config);
                lastStatus = L.T("Gmail 登入成功；郵件只會寄給 ", "Gmail sign-in succeeded; mail will only be sent to ") + email + "。";
            }
        }

        public void Disconnect()
        {
            lock (sync)
            {
                config = NewConfig();
                accessToken = null;
                accessTokenExpires = DateTime.MinValue;
                try { if (File.Exists(settingsPath)) File.Delete(settingsPath); } catch { }
                lastStatus = L.T("已移除本機 Gmail 登入權杖與待寄通知。", "The local Gmail sign-in token and queued notifications were removed.");
            }
        }

        public void BeginTestEmail(Action<bool, string> completed)
        {
            if (!Connected)
            {
                DeliveryAuditLog.Record(machineName, machineId, "GMAIL", "TEST_EMAIL", "FAILED", "Reason=NotConnected");
                if (completed != null) completed(false, L.T("請先登入 Gmail。", "Sign in to Gmail first."));
                return;
            }
            if (!TryBeginSend(completed)) { DeliveryAuditLog.Record(machineName, machineId, "GMAIL", "TEST_EMAIL", "SKIPPED", "Reason=AlreadyRunning"); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string message;
                try
                {
                    string email = AccountEmail;
                    string subject = BuildSubject(machineName, L.T("測試郵件", "Test Email"));
                    string body = L.T(
                        "這是 NetCheckMonitor 的測試郵件。\r\n\r\n寄件與收件帳戶：" + email + "\r\n電腦：" + machineName + " [" + machineId + "]\r\n時間：" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                        "This is a NetCheckMonitor test email.\r\n\r\nSender and recipient: " + email + "\r\nComputer: " + machineName + " [" + machineId + "]\r\nTime: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    SendSelfEmail(subject, body, null);
                    ok = true;
                    message = L.T("測試郵件已寄到 ", "A test email was sent to ") + email + "。";
                }
                catch (Exception ex) { message = L.T("測試郵件寄送失敗：", "Test email failed: ") + ex.Message; }
                DeliveryAuditLog.Record(machineName, machineId, "GMAIL", "TEST_EMAIL", ok ? "SUCCESS" : "FAILED", ok ? "Recipient=SignedInAccount" : "Error=" + message);
                CompleteSend(ok, message, completed);
            });
        }

        public void QueueRecoveryNotification(DateTime outageStart, DateTime recoveredAt, int failedChecks)
        {
            if (!Connected || !RecoveryNotificationEnabled || outageStart == DateTime.MinValue) return;
            var pending = new GmailPendingRecovery
            {
                Id = Guid.NewGuid().ToString("N"),
                OutageStart = outageStart,
                RecoveredAt = recoveredAt,
                FailedChecks = Math.Max(2, failedChecks),
                NextAttemptUtc = DateTime.UtcNow
            };
            lock (sync)
            {
                config.PendingRecoveries.Add(pending);
                SaveConfig(settingsPath, config);
                lastStatus = L.T("網路恢復通知已排入寄送佇列。", "The recovery notification was queued for delivery.");
            }
            CheckSchedule();
        }

        private void CheckSchedule()
        {
            if (disposed || !Connected || SendInProgress) return;
            GmailPendingRecovery pending = null;
            lock (sync)
            {
                if (config.RecoveryNotificationEnabled)
                {
                    foreach (GmailPendingRecovery item in config.PendingRecoveries)
                    {
                        if (item.NextAttemptUtc == DateTime.MinValue || item.NextAttemptUtc <= DateTime.UtcNow)
                        {
                            pending = item;
                            break;
                        }
                    }
                }
            }
            if (pending != null)
            {
                BeginRecoveryEmail(pending);
                return;
            }

            DateTime now = DateTime.Now;
            DateTime due = DateTime.MinValue;
            lock (sync)
            {
                if (!config.DailyReportEnabled || config.NextDailyAttemptUtc > DateTime.UtcNow) return;
                DateTime last;
                if (!DateTime.TryParseExact(config.LastReportDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last)) last = DateTime.MinValue;
                if (last.Date < now.Date.AddDays(-1)) due = now.Date.AddDays(-1);
                else if (now.TimeOfDay >= ScheduleTime && last.Date < now.Date) due = now.Date;
            }
            if (due == DateTime.MinValue) return;
            if (!ArchiveReport.HasChecksForDay(due))
            {
                lock (sync)
                {
                    config.LastReportDay = due.ToString("yyyy-MM-dd");
                    config.DailyAttempts = 0;
                    config.NextDailyAttemptUtc = DateTime.MinValue;
                    lastStatus = due.ToString("yyyy/MM/dd") + L.T(" 沒有監控資料，已略過每日郵件。", " had no monitoring data, so the daily email was skipped.");
                    SaveConfig(settingsPath, config);
                }
                DeliveryAuditLog.Record(machineName, machineId, "GMAIL", "DAILY_REPORT", "SKIPPED", "DataDate=" + due.ToString("yyyy-MM-dd") + ";Reason=NoMonitoringData");
                return;
            }
            BeginDailyReportEmail(due);
        }

        private void BeginRecoveryEmail(GmailPendingRecovery pending)
        {
            if (!TryBeginSend(null)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string message;
                string auditDetail = "OutageStart=" + pending.OutageStart.ToString("o", CultureInfo.InvariantCulture) + ";RecoveredAt=" + pending.RecoveredAt.ToString("o", CultureInfo.InvariantCulture);
                try
                {
                    TimeSpan duration = pending.RecoveredAt - pending.OutageStart;
                    if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                    string subject = BuildSubject(machineName, L.T("網路已恢復", "Internet Connection Restored"));
                    string body = L.T(
                        "NetCheckMonitor 已確認網路恢復。\r\n\r\n電腦：" + machineName + " [" + machineId + "]\r\n斷線開始：" + pending.OutageStart.ToString("yyyy/MM/dd HH:mm:ss") + "\r\n恢復時間：" + pending.RecoveredAt.ToString("yyyy/MM/dd HH:mm:ss") + "\r\n持續時間：" + FormatDuration(duration) + "\r\n失敗檢查次數：" + pending.FailedChecks,
                        "NetCheckMonitor confirmed that the internet connection was restored.\r\n\r\nComputer: " + machineName + " [" + machineId + "]\r\nOutage started: " + pending.OutageStart.ToString("yyyy-MM-dd HH:mm:ss") + "\r\nRecovered: " + pending.RecoveredAt.ToString("yyyy-MM-dd HH:mm:ss") + "\r\nDuration: " + FormatDuration(duration) + "\r\nFailed checks: " + pending.FailedChecks);
                    SendSelfEmail(subject, body, null);
                    lock (sync)
                    {
                        config.PendingRecoveries.RemoveAll(delegate (GmailPendingRecovery item) { return item.Id == pending.Id; });
                        SaveConfig(settingsPath, config);
                    }
                    ok = true;
                    message = L.T("網路恢復通知已寄到 ", "The recovery notification was sent to ") + AccountEmail + "。";
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        pending.Attempts++;
                        pending.NextAttemptUtc = DateTime.UtcNow.AddMinutes(RetryMinutes(pending.Attempts));
                        SaveConfig(settingsPath, config);
                    }
                    message = L.T("恢復通知暫時無法寄出，已保留稍後重試：", "The recovery notification could not be sent and is queued for retry: ") + ex.Message;
                    auditDetail += ";Error=" + ex.Message;
                }
                DeliveryAuditLog.Record(machineName, machineId, "GMAIL", "RECOVERY_NOTIFICATION", ok ? "SUCCESS" : "FAILED", auditDetail);
                CompleteSend(ok, message, null);
            });
        }

        private void BeginDailyReportEmail(DateTime day)
        {
            if (!TryBeginSend(null)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string message;
                string auditDetail = "DataDate=" + day.ToString("yyyy-MM-dd");
                string temp = Path.Combine(Path.GetTempPath(), "NetCheckGmail_" + Guid.NewGuid().ToString("N"));
                try
                {
                    string[] artifacts = ArchiveReport.ExportDailyDeliveryArtifacts(temp, machineName, machineId, day.Date);
                    bool includesSpeedReport = artifacts.Length > 2;
                    string subject = BuildSubject(machineName, L.T("每日網路報表", "Daily Network Report")) + " " + day.ToString("yyyy-MM-dd");
                    string body = L.T(
                        "附件是 NetCheckMonitor " + day.ToString("yyyy/MM/dd") + " 的每日網路監控報表。\r\n\r\n電腦：" + machineName + " [" + machineId + "]\r\n收件帳戶：" + AccountEmail + "\r\n附件：PDF 報表、原始 CSV" + (includesSpeedReport ? "、定時測速 HTML 報表與測速原始 CSV" : ""),
                        "Attached is the NetCheckMonitor daily network report for " + day.ToString("yyyy-MM-dd") + ".\r\n\r\nComputer: " + machineName + " [" + machineId + "]\r\nRecipient: " + AccountEmail + "\r\nAttachments: PDF report and raw CSV" + (includesSpeedReport ? ", scheduled speed-test HTML report and raw speed-test CSV" : ""));
                    SendSelfEmail(subject, body, artifacts);
                    lock (sync)
                    {
                        config.LastReportDay = day.ToString("yyyy-MM-dd");
                        config.DailyAttempts = 0;
                        config.NextDailyAttemptUtc = DateTime.MinValue;
                        SaveConfig(settingsPath, config);
                    }
                    ok = true;
                    auditDetail += ";Attachments=" + artifacts.Length.ToString(CultureInfo.InvariantCulture);
                    message = day.ToString("yyyy/MM/dd") + L.T(" 每日報表已寄到 ", " daily report was sent to ") + AccountEmail + "。";
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        config.DailyAttempts++;
                        config.NextDailyAttemptUtc = DateTime.UtcNow.AddMinutes(RetryMinutes(config.DailyAttempts));
                        SaveConfig(settingsPath, config);
                    }
                    message = L.T("每日報表暫時無法寄出，稍後會自動重試：", "The daily report could not be sent and will be retried: ") + ex.Message;
                    auditDetail += ";Error=" + ex.Message;
                }
                finally { try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { } }
                DeliveryAuditLog.Record(machineName, machineId, "GMAIL", "DAILY_REPORT", ok ? "SUCCESS" : "FAILED", auditDetail);
                CompleteSend(ok, message, null);
            });
        }

        private bool TryBeginSend(Action<bool, string> completed)
        {
            if (Interlocked.CompareExchange(ref sendRunning, 1, 0) == 0) return true;
            if (completed != null) completed(false, L.T("已有 Gmail 郵件正在製作或寄送。", "A Gmail message is already being prepared or sent."));
            return false;
        }

        private void CompleteSend(bool ok, string message, Action<bool, string> completed)
        {
            lock (sync) lastStatus = message;
            Interlocked.Exchange(ref sendRunning, 0);
            if (completed != null) completed(ok, message);
        }

        private void SendSelfEmail(string subject, string body, string[] attachmentPaths)
        {
            string email = AccountEmail;
            if (!IsSafeEmail(email)) throw new InvalidOperationException(L.T("登入信箱格式無效，請重新連接 Gmail。", "The signed-in email address is invalid. Reconnect Gmail."));
            byte[] mime = BuildSelfMime(email, subject, body, attachmentPaths);
            ApiRequest("POST", "https://gmail.googleapis.com/gmail/v1/users/me/messages/send", GetAccessToken(), "application/json; charset=UTF-8", BuildSendPayload(mime));
        }

        private static byte[] BuildSendPayload(byte[] mime)
        {
            var payload = new Dictionary<string, object>();
            payload["raw"] = Base64Url(mime ?? new byte[0]);
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            return Utf8(serializer.Serialize(payload));
        }

        private static string BuildSubject(string computerName, string title)
        {
            string safeName = (computerName ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (safeName.IndexOf("  ", StringComparison.Ordinal) >= 0) safeName = safeName.Replace("  ", " ");
            if (safeName.Length == 0) safeName = "Unknown-PC";
            if (safeName.Length > 64) safeName = safeName.Substring(0, 64);
            return "[NetCheckMonitor][" + safeName + "] " + (title ?? "").Trim();
        }

        private string GetAccessToken()
        {
            lock (sync)
            {
                if (!String.IsNullOrEmpty(accessToken) && DateTime.UtcNow < accessTokenExpires) return accessToken;
                if (String.IsNullOrEmpty(config.RefreshToken)) throw new InvalidOperationException(L.T("Gmail 登入已失效，請重新連接。", "The Gmail sign-in has expired. Reconnect it."));
                if (String.IsNullOrEmpty(config.ClientSecret))
                {
                    GmailNotificationConfig embedded = EmbeddedCredentials();
                    if (String.Equals(config.ClientId, embedded.ClientId, StringComparison.Ordinal) && !String.IsNullOrEmpty(embedded.ClientSecret))
                    {
                        config.ClientSecret = embedded.ClientSecret;
                        SaveConfig(settingsPath, config);
                    }
                }
                var form = new Dictionary<string, string>();
                form["client_id"] = config.ClientId;
                if (!String.IsNullOrEmpty(config.ClientSecret)) form["client_secret"] = config.ClientSecret;
                form["refresh_token"] = config.RefreshToken;
                form["grant_type"] = "refresh_token";
                Dictionary<string, object> json = JsonObject(PostForm(String.IsNullOrEmpty(config.TokenUri) ? GoogleTokenUri : config.TokenUri, form));
                accessToken = GetString(json, "access_token");
                int seconds = GetInt(json, "expires_in", 3600);
                if (String.IsNullOrEmpty(accessToken)) throw new InvalidOperationException(L.T("Google 未回傳 access token。", "Google did not return an access token."));
                accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
                return accessToken;
            }
        }

        private sealed class OAuthResult
        {
            public string AccessToken;
            public string RefreshToken;
            public int ExpiresIn;
        }

        private static OAuthResult Authorize(GmailNotificationConfig credentials)
        {
            string verifier = RandomUrlSafe(48);
            string state = RandomUrlSafe(24);
            string challenge;
            using (SHA256 sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://127.0.0.1:" + port + "/";
            string scope = "openid email " + GmailSendScope;
            string auth = String.IsNullOrEmpty(credentials.AuthUri) ? GoogleAuthUri : credentials.AuthUri;
            string url = BuildAuthorizationUrl(auth, credentials.ClientId, redirect, scope, state, challenge);
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
                    requestLine = reader.ReadLine();
                    string line;
                    do { line = reader.ReadLine(); } while (!String.IsNullOrEmpty(line));
                }
                string target = requestLine == null ? "" : requestLine.Split(' ')[1];
                Dictionary<string, string> query = ParseQuery(new Uri("http://127.0.0.1" + target).Query);
                string html = "<!doctype html><html lang='" + L.HtmlLanguage + "'><meta charset='utf-8'><title>NetCheckMonitor</title><body style='font-family:sans-serif;padding:40px'><h1>NetCheckMonitor Gmail</h1><p>" + WebUtility.HtmlEncode(L.T("登入完成，可以關閉此視窗並回到 NetCheckMonitor。", "Sign-in is complete. You can close this window and return to NetCheckMonitor.")) + "</p></body></html>";
                byte[] responseBody = Utf8(html);
                byte[] headers = Utf8("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + responseBody.Length + "\r\nConnection: close\r\n\r\n");
                client.GetStream().Write(headers, 0, headers.Length);
                client.GetStream().Write(responseBody, 0, responseBody.Length);
                client.GetStream().Flush();
                if (!query.ContainsKey("state") || query["state"] != state) throw new InvalidOperationException(L.T("Google 登入狀態驗證失敗。", "Google sign-in state validation failed."));
                if (query.ContainsKey("error")) throw new InvalidOperationException(L.T("Google 登入未完成：", "Google sign-in was not completed: ") + query["error"]);
                if (!query.ContainsKey("code")) throw new InvalidOperationException(L.T("Google 未回傳授權碼。", "Google did not return an authorization code."));
                var form = new Dictionary<string, string>();
                form["code"] = query["code"];
                form["client_id"] = credentials.ClientId;
                if (!String.IsNullOrEmpty(credentials.ClientSecret)) form["client_secret"] = credentials.ClientSecret;
                form["redirect_uri"] = redirect;
                form["grant_type"] = "authorization_code";
                form["code_verifier"] = verifier;
                Dictionary<string, object> token = JsonObject(PostForm(String.IsNullOrEmpty(credentials.TokenUri) ? GoogleTokenUri : credentials.TokenUri, form));
                var result = new OAuthResult
                {
                    AccessToken = GetString(token, "access_token"),
                    RefreshToken = GetString(token, "refresh_token"),
                    ExpiresIn = GetInt(token, "expires_in", 3600)
                };
                if (String.IsNullOrEmpty(result.AccessToken) || String.IsNullOrEmpty(result.RefreshToken))
                    throw new InvalidOperationException(L.T("Google 未提供完整的離線授權，請移除既有授權後重新登入。", "Google did not provide complete offline access. Remove the existing grant and sign in again."));
                return result;
            }
            finally
            {
                try { if (client != null) client.Close(); } catch { }
                listener.Stop();
            }
        }

        private static string BuildAuthorizationUrl(string auth, string clientId, string redirect, string scope, string state, string challenge)
        {
            return auth + "?client_id=" + E(clientId)
                + "&redirect_uri=" + E(redirect)
                + "&response_type=code&scope=" + E(scope)
                + "&access_type=offline&prompt=consent"
                + "&code_challenge=" + E(challenge)
                + "&code_challenge_method=S256&state=" + E(state);
        }

        private static string ReadSignedInEmail(string token)
        {
            Dictionary<string, object> user = JsonObject(ApiRequest("GET", "https://openidconnect.googleapis.com/v1/userinfo", token, null, null));
            string email = GetString(user, "email");
            object verified;
            if (!user.TryGetValue("email_verified", out verified) || !Convert.ToBoolean(verified, CultureInfo.InvariantCulture))
                throw new InvalidOperationException(L.T("Google 登入信箱尚未驗證。", "The Google account email is not verified."));
            return email == null ? null : email.Trim();
        }

        private static byte[] BuildSelfMime(string accountEmail, string subject, string body, string[] attachmentPaths)
        {
            if (!IsSafeEmail(accountEmail)) throw new InvalidDataException("Unsafe account email.");
            string boundary = "netcheck_" + Guid.NewGuid().ToString("N");
            var message = new StringBuilder();
            message.Append("Date: ").Append(DateTimeOffset.Now.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
            message.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append("@netcheckmonitor.local>\r\n");
            message.Append("From: NetCheckMonitor <").Append(accountEmail).Append(">\r\n");
            message.Append("To: ").Append(accountEmail).Append("\r\n");
            message.Append("Subject: ").Append(EncodedWord(subject)).Append("\r\n");
            message.Append("MIME-Version: 1.0\r\n");
            message.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
            message.Append("--").Append(boundary).Append("\r\n");
            message.Append("Content-Type: text/plain; charset=utf-8\r\n");
            message.Append("Content-Transfer-Encoding: base64\r\n\r\n");
            message.Append(WrapBase64(Convert.ToBase64String(Utf8(body)))).Append("\r\n");
            if (attachmentPaths != null)
            {
                foreach (string path in attachmentPaths)
                {
                    if (String.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    string name = Path.GetFileName(path);
                    string extension = Path.GetExtension(path);
                    string mime = String.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf"
                        : (String.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) || String.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase) ? "text/html" : "text/csv");
                    message.Append("--").Append(boundary).Append("\r\n");
                    message.Append("Content-Type: ").Append(mime).Append("; name=\"").Append(EncodedWord(name)).Append("\"\r\n");
                    message.Append("Content-Disposition: attachment; filename=\"").Append(EncodedWord(name)).Append("\"\r\n");
                    message.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                    message.Append(WrapBase64(Convert.ToBase64String(File.ReadAllBytes(path)))).Append("\r\n");
                }
            }
            message.Append("--").Append(boundary).Append("--\r\n");
            return Utf8(message.ToString());
        }

        private static string EncodedWord(string value)
        {
            return "=?UTF-8?B?" + Convert.ToBase64String(Utf8(value)) + "?=";
        }

        private static string WrapBase64(string value)
        {
            var result = new StringBuilder();
            for (int i = 0; i < value.Length; i += 76)
            {
                int count = Math.Min(76, value.Length - i);
                result.Append(value, i, count).Append("\r\n");
            }
            return result.ToString().TrimEnd('\r', '\n');
        }

        private static bool IsSafeEmail(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 254 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) return false;
            int at = value.IndexOf('@');
            return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 3 && value.IndexOf('.', at) > at + 1;
        }

        private static int RetryMinutes(int attempts)
        {
            return Math.Min(60, Math.Max(1, (int)Math.Pow(2, Math.Min(6, Math.Max(0, attempts - 1)))));
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalDays >= 1) return ((int)value.TotalDays) + L.T(" 天 ", "d ") + value.ToString(@"hh\:mm\:ss");
            return value.ToString(@"hh\:mm\:ss");
        }

        private static GmailNotificationConfig NewConfig()
        {
            return new GmailNotificationConfig
            {
                DailyReportEnabled = true,
                RecoveryNotificationEnabled = true,
                Schedule = "23:50",
                PendingRecoveries = new List<GmailPendingRecovery>()
            };
        }

        private static void NormalizeConfig(GmailNotificationConfig value)
        {
            if (String.IsNullOrWhiteSpace(value.Schedule)) value.Schedule = "23:50";
            TimeSpan parsed;
            if (!TimeSpan.TryParseExact(value.Schedule, @"hh\:mm", CultureInfo.InvariantCulture, out parsed)) value.Schedule = "23:50";
            if (value.PendingRecoveries == null) value.PendingRecoveries = new List<GmailPendingRecovery>();
            value.PendingRecoveries.RemoveAll(delegate (GmailPendingRecovery item) { return item == null || String.IsNullOrWhiteSpace(item.Id); });
        }

        private static GmailNotificationConfig EmbeddedCredentials()
        {
            return new GmailNotificationConfig
            {
                ClientId = EmbeddedClientId,
                ClientSecret = GoogleOAuthBuildSecrets.ClientSecret,
                AuthUri = GoogleAuthUri,
                TokenUri = GoogleTokenUri
            };
        }

        private static GmailNotificationConfig LoadConfig(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] encrypted = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return new JavaScriptSerializer().Deserialize<GmailNotificationConfig>(Encoding.UTF8.GetString(plain));
            }
            catch { return null; }
        }

        private static void SaveConfig(string path, GmailNotificationConfig value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            byte[] plain = Utf8(new JavaScriptSerializer().Serialize(value));
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, encrypted);
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

        public static bool RunStorageSelfTest(string path)
        {
            var expected = NewConfig();
            expected.ClientId = "test-client";
            expected.RefreshToken = "test-refresh";
            expected.AccountEmail = "self@example.com";
            expected.LastReportDay = "2026-07-27";
            expected.PendingRecoveries.Add(new GmailPendingRecovery { Id = "pending-1", OutageStart = DateTime.Today, RecoveredAt = DateTime.Today.AddMinutes(2), FailedChecks = 3 });
            SaveConfig(path, expected);
            GmailNotificationConfig loaded = LoadConfig(path);
            bool protectedAtRest = Encoding.UTF8.GetString(File.ReadAllBytes(path)).IndexOf("test-refresh", StringComparison.Ordinal) < 0;
            try { File.Delete(path); } catch { }
            return loaded != null && loaded.AccountEmail == "self@example.com" && loaded.PendingRecoveries != null
                && loaded.PendingRecoveries.Count == 1 && loaded.PendingRecoveries[0].FailedChecks == 3 && protectedAtRest;
        }

        public static bool RunMimeSelfTest()
        {
            string email = "self@example.com";
            string raw = Encoding.UTF8.GetString(BuildSelfMime(email, "測試", "body", null));
            return raw.Contains("From: NetCheckMonitor <" + email + ">")
                && raw.Contains("To: " + email)
                && raw.Contains("Subject: =?UTF-8?B?")
                && raw.IndexOf("other@example.com", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public static bool RunAttachmentMimeSelfTest()
        {
            string directory = Path.Combine(Path.GetTempPath(), "NetCheckGmailMime_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                string html = Path.Combine(directory, "speed.html");
                string csv = Path.Combine(directory, "speed.csv");
                File.WriteAllText(html, "<html></html>", Encoding.UTF8);
                File.WriteAllText(csv, "header", Encoding.UTF8);
                string raw = Encoding.UTF8.GetString(BuildSelfMime("self@example.com", "report", "body", new string[] { html, csv }));
                return raw.Contains("Content-Type: text/html;") && raw.Contains("Content-Type: text/csv;");
            }
            finally { try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { } }
        }

        public static bool RunLargePayloadSelfTest()
        {
            byte[] mime = new byte[1600000];
            for (int i = 0; i < mime.Length; i++) mime[i] = (byte)(i % 251);
            byte[] json = BuildSendPayload(mime);
            string text = Encoding.UTF8.GetString(json);
            return json.Length > 2097152
                && text.StartsWith("{\"raw\":\"", StringComparison.Ordinal)
                && text.EndsWith("\"}", StringComparison.Ordinal)
                && text.IndexOf(Base64Url(new byte[] { 0, 1, 2, 3 }), StringComparison.Ordinal) > 0;
        }

        public static bool RunSubjectSelfTest()
        {
            string daily = BuildSubject("OFFICE-PC", "每日網路報表") + " 2026-07-30";
            string sanitized = BuildSubject("LINE1\r\nLINE2", "Test Email");
            return daily == "[NetCheckMonitor][OFFICE-PC] 每日網路報表 2026-07-30"
                && sanitized == "[NetCheckMonitor][LINE1 LINE2] Test Email"
                && sanitized.IndexOf('\r') < 0
                && sanitized.IndexOf('\n') < 0;
        }

        public static bool RunOAuthRequestSelfTest()
        {
            string scope = "openid email " + GmailSendScope;
            string url = BuildAuthorizationUrl(GoogleAuthUri, "client", "http://127.0.0.1:1234/", scope, "state", "challenge");
            return url.Contains(Uri.EscapeDataString(GmailSendScope))
                && url.Contains(Uri.EscapeDataString("openid email "))
                && url.Contains("code_challenge_method=S256")
                && url.Contains("access_type=offline");
        }

        private static string ApiRequest(string method, string url, string token, string contentType, byte[] body)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            request.Headers["Authorization"] = "Bearer " + token;
            if (body != null)
            {
                request.ContentType = contentType;
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            }
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd();
            }
            catch (WebException ex) { throw new InvalidOperationException(ReadWebError(ex)); }
        }

        private static string PostForm(string url, Dictionary<string, string> values)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, string> item in values) parts.Add(E(item.Key) + "=" + E(item.Value));
            byte[] body = Utf8(String.Join("&", parts.ToArray()));
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = body.Length;
            request.Timeout = 60000;
            using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd();
            }
            catch (WebException ex) { throw new InvalidOperationException(ReadWebError(ex)); }
        }

        private static string ReadWebError(WebException ex)
        {
            try
            {
                if (ex.Response == null) return ex.Message;
                using (var reader = new StreamReader(ex.Response.GetResponseStream())) return reader.ReadToEnd();
            }
            catch { return ex.Message; }
        }

        private static Dictionary<string, object> JsonObject(string json)
        {
            var value = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (value == null) throw new InvalidDataException(L.T("Google 回傳的 JSON 格式錯誤。", "Google returned invalid JSON."));
            return value;
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static int GetInt(Dictionary<string, object> values, string key, int fallback)
        {
            object value;
            int parsed;
            return values.TryGetValue(key, out value) && Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>();
            foreach (string part in query.TrimStart('?').Split('&'))
            {
                if (String.IsNullOrEmpty(part)) continue;
                string[] pair = part.Split(new char[] { '=' }, 2);
                result[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            }
            return result;
        }

        private static byte[] Utf8(string value) { return new UTF8Encoding(false).GetBytes(value ?? ""); }
        private static string E(string value) { return Uri.EscapeDataString(value ?? ""); }
        private static string RandomUrlSafe(int bytes)
        {
            byte[] data = new byte[bytes];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return Base64Url(data);
        }
        private static string Base64Url(byte[] data) { return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

        public void Dispose()
        {
            disposed = true;
            scheduleTimer.Dispose();
        }
    }

    internal sealed class GmailNotificationForm : Form
    {
        private readonly GmailNotificationManager manager;
        private readonly Label connection = new Label();
        private readonly Label account = new Label();
        private readonly Label status = new Label();
        private readonly CheckBox dailyReport = new CheckBox();
        private readonly CheckBox recoveryNotification = new CheckBox();
        private readonly DateTimePicker timePicker = new DateTimePicker();
        private readonly Button connect = new Button();
        private readonly Button test = new Button();
        private readonly Button disconnect = new Button();
        private readonly Button save = new Button();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();

        public GmailNotificationForm(GmailNotificationManager gmail)
        {
            manager = gmail;
            Text = L.T("Gmail 報表與恢復通知", "Gmail Reports and Recovery Notifications");
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(690, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label { Text = L.T("Gmail 報表與恢復通知", "Gmail Reports and Recovery Notifications"), Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 18) };
            connection.SetBounds(27, 61, 635, 27);
            connection.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            account.SetBounds(27, 89, 635, 24);
            account.ForeColor = Color.DimGray;
            var explain = new Label
            {
                Text = L.T("寄件者與收件者固定為登入的同一個 Google 帳戶，不能輸入其他收件地址。完全斷網時，恢復通知會先保存在本機，網路恢復後再寄出。", "Sender and recipient are always the same signed-in Google account; other recipient addresses cannot be entered. During a complete outage, the recovery notice is stored locally and sent after connectivity returns."),
                AutoSize = false,
                Location = new Point(27, 121),
                Size = new Size(635, 52),
                ForeColor = Color.DimGray
            };
            dailyReport.Text = L.T("每日寄送網路監控與定時測速報表（如有）", "Email daily monitoring and scheduled speed-test reports (when available)");
            dailyReport.SetBounds(28, 186, 620, 28);
            recoveryNotification.Text = L.T("網路恢復後寄送通知", "Send a notification after the internet connection recovers");
            recoveryNotification.SetBounds(28, 220, 530, 28);
            var scheduleLabel = new Label { Text = L.T("每日寄送時間", "Daily send time"), AutoSize = true, Location = new Point(51, 267) };
            timePicker.Format = DateTimePickerFormat.Custom;
            timePicker.CustomFormat = "HH:mm";
            timePicker.ShowUpDown = true;
            timePicker.SetBounds(170, 262, 95, 28);
            timePicker.Value = DateTime.Today.Add(manager.ScheduleTime);
            dailyReport.Checked = manager.DailyReportEnabled;
            recoveryNotification.Checked = manager.RecoveryNotificationEnabled;
            save.Text = L.T("儲存寄送設定", "Save Delivery Settings");
            save.SetBounds(279, 259, 155, 34);

            connect.Text = L.T("登入 Gmail", "Sign in to Gmail");
            connect.SetBounds(28, 320, 150, 40);
            test.Text = L.T("寄送測試郵件", "Send Test Email");
            test.SetBounds(188, 320, 165, 40);
            disconnect.Text = L.T("中斷連線", "Disconnect");
            disconnect.SetBounds(363, 320, 125, 40);
            disconnect.ForeColor = Color.Firebrick;
            status.SetBounds(28, 382, 634, 62);
            status.ForeColor = Color.DimGray;
            var close = new Button { Text = L.T("關閉", "Close"), Location = new Point(542, 452), Size = new Size(120, 32) };

            Controls.AddRange(new Control[] { title, connection, account, explain, dailyReport, recoveryNotification, scheduleLabel, timePicker, save, connect, test, disconnect, status, close });
            save.Click += delegate { SaveOptions(); };
            connect.Click += delegate { Connect(); };
            test.Click += delegate { SendTest(); };
            disconnect.Click += delegate
            {
                if (MessageBox.Show(L.T("確定移除這台電腦儲存的 Gmail 登入權杖與待寄通知嗎？", "Remove the Gmail sign-in token and queued notifications stored on this computer?"), disconnect.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    manager.Disconnect();
                    RefreshState();
                }
            };
            close.Click += delegate { Close(); };
            refresh.Interval = 1000;
            refresh.Tick += delegate { RefreshState(); };
            refresh.Start();
            FormClosed += delegate { refresh.Dispose(); };
            RefreshState();
        }

        private void SaveOptions()
        {
            try
            {
                manager.SaveOptions(dailyReport.Checked, recoveryNotification.Checked, timePicker.Value.TimeOfDay);
                RefreshState();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, save.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void Connect()
        {
            SetBusy(true);
            status.Text = L.T("等待系統瀏覽器完成 Google 登入與 Gmail 寄信授權…", "Waiting for Google sign-in and Gmail send permission in your system browser…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { manager.Connect(); }
                catch (Exception ex) { error = ex.Message; }
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke((MethodInvoker)delegate
                    {
                        SetBusy(false);
                        RefreshState();
                        if (error != null) MessageBox.Show(error, L.T("Gmail 登入失敗", "Gmail Sign-in Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
            });
        }

        private void SendTest()
        {
            SetBusy(true);
            manager.BeginTestEmail(delegate (bool ok, string message)
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke((MethodInvoker)delegate
                    {
                        SetBusy(false);
                        RefreshState();
                        MessageBox.Show(message, ok ? L.T("寄送成功", "Message Sent") : L.T("寄送失敗", "Send Failed"), MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                    });
            });
        }

        private void RefreshState()
        {
            bool connected = manager.Connected;
            connection.Text = connected ? L.T("狀態：已連接 Gmail", "Status: Gmail connected") : L.T("狀態：尚未連接", "Status: Not connected");
            connection.ForeColor = connected ? Color.SeaGreen : Color.DimGray;
            account.Text = connected ? L.T("唯一收件帳戶：", "Only recipient account: ") + manager.AccountEmail : L.T("登入後，郵件只能寄給該登入帳戶。", "After sign-in, mail can only be sent to that signed-in account.");
            test.Enabled = disconnect.Enabled = connected && connect.Enabled && !manager.SendInProgress;
            status.Text = manager.LastStatus
                + (String.IsNullOrEmpty(manager.LastReportDay) ? "" : L.T("\r\n最後每日報表日期：", "\r\nLast daily report date: ") + manager.LastReportDay)
                + (manager.PendingRecoveryCount == 0 ? "" : L.T("；待寄恢復通知：", "; queued recovery notices: ") + manager.PendingRecoveryCount);
        }

        private void SetBusy(bool busy)
        {
            connect.Enabled = test.Enabled = disconnect.Enabled = save.Enabled = dailyReport.Enabled = recoveryNotification.Enabled = timePicker.Enabled = !busy;
        }
    }
}
