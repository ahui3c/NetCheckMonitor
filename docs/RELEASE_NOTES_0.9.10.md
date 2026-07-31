# NetCheckMonitor 0.9.10 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.10 修正較大的每日 Gmail 報表無法寄送的問題。

### Gmail 大型日報修正

- 修正 PDF／CSV 附件經 MIME Base64 與 Gmail Base64URL 封裝後，JSON 超過 .NET `JavaScriptSerializer` 預設長度上限而寄送失敗的問題。
- Gmail API payload 現在使用明確支援大型郵件的序列化設定。
- 不改變既有安全限制：寄件者與收件者仍固定為登入的同一個 Google 帳戶，且只要求 `gmail.send` 權限。
- 保留失敗後自動退避與重試機制；升級後可再次儲存寄送設定以立即重試尚未寄出的日報。

### 測試

- 新增超過 2 MB Gmail JSON payload 的回歸測試。
- 驗證 Gmail MIME、自寄限制、OAuth PKCE、DPAPI 儲存、每日 PDF／CSV、累積報表及測速報表。

## English

NetCheckMonitor 0.9.10 fixes Gmail delivery failures for larger daily reports.

### Large Gmail daily-report fix

- Fixes failures when PDF/CSV attachments expand through MIME Base64 and Gmail Base64URL packaging beyond the default .NET `JavaScriptSerializer` length limit.
- Gmail API payloads now use serialization settings that explicitly support large messages.
- Keeps the existing safety boundary: sender and recipient remain fixed to the same signed-in Google account, with only the `gmail.send` scope requested.
- Retains automatic backoff and retry. After upgrading, saving the delivery settings again can immediately retry a pending daily report.

### Validation

- Adds a regression test for Gmail JSON payloads larger than 2 MB.
- Verifies Gmail MIME, self-recipient enforcement, OAuth PKCE, DPAPI storage, daily PDF/CSV generation, cumulative reports, and speed reports.
