# NetCheckMonitor 0.9.11 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.11 改善多台電腦共用 Google 帳戶時的識別與整理，並將定時測速資料納入每日雲端備份及 Gmail 報表。

### Gmail 郵件識別

- 測試郵件、網路恢復通知及每日報表標題加入電腦名稱。
- 寄件者與收件者仍固定為登入的同一個 Google 帳戶，維持既有自寄安全限制。

### Google Drive 多電腦整理

- 每台電腦的新備份改存放於 `Net_Check/電腦名稱` 子資料夾。
- 同一 Google Drive 帳戶可依電腦名稱分開整理報表；同名檔案仍採更新方式，不會因重試持續產生副本。

### 定時測速報表交付

- 啟用定時測速且指定日期有定時測速紀錄時，Google Drive 與 Gmail 每日報表會額外包含當日測速 HTML 報表及測速原始 CSV。
- 沒有測速資料時仍照常處理網路監控 PDF／CSV，不會因缺少測速結果導致每日流程失敗。
- 僅納入定時測速紀錄，不混入舊有手動測速資料。

### 驗證

- 驗證每日測速報表日期篩選、定時測速開關、Drive 上傳 MIME、Gmail HTML／CSV 附件 MIME、大型 Gmail payload、自寄限制與 OAuth 權限。

## English

NetCheckMonitor 0.9.11 improves identification and organization when multiple computers share a Google account, and adds scheduled speed-test data to daily cloud and Gmail delivery.

### Gmail message identification

- Adds the computer name to test-email, recovery-notification, and daily-report subjects.
- Keeps sender and recipient fixed to the same signed-in Google account.

### Multi-computer Google Drive organization

- Stores new backups for each computer under its own `Net_Check/computer name` child folder.
- Multiple computers can share one Drive account while keeping reports separated. Same-name files are still updated instead of duplicated during retries.

### Scheduled speed-test delivery

- When scheduled speed testing is enabled and the selected day has scheduled records, Drive and Gmail daily delivery also include a daily speed-test HTML report and raw speed-test CSV.
- The normal monitoring PDF/CSV still succeeds when no speed-test data exists.
- Only scheduled records are included; legacy manual results are excluded.

### Validation

- Verifies daily speed-test date filtering, the scheduled-test setting, Drive upload MIME types, Gmail HTML/CSV attachment MIME types, large Gmail payloads, self-recipient enforcement, and OAuth scope behavior.
