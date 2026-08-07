# NetCheckMonitor 0.9.12 更新說明 / Release Notes

發布日期：2026-08-07

NetCheckMonitor 0.9.12 新增 Gmail 通知與 Google Drive 備份的傳送稽核紀錄，讓每日自動作業的成功、失敗或略過原因都能在本機留存並供後續追蹤。

## 主要更新

- 新增 `NetCheck_Delivery_<電腦名稱>-<電腦識別碼>.csv` 傳送紀錄檔。
- 記錄 Gmail 的測試郵件、每日報表與網路恢復通知。
- 記錄 Google Drive 每日備份。
- 每筆紀錄包含時間、傳送類型、`SUCCESS`／`FAILED`／`SKIPPED` 狀態、耗時、目標及詳細資訊。
- 詳細資訊會包含動作、報表日期、附件數量、Drive 資料夾或精簡錯誤原因，並轉為單行及限制長度，避免紀錄檔損壞。
- 傳送紀錄會一併收錄於「匯出全部紀錄備份 ZIP」。
- 紀錄不會保存 OAuth 權杖或郵件本文。

## 驗證

- 新增傳送紀錄格式、狀態、錯誤清理及 Gmail／Drive 未連線失敗路徑的自動測試。
- 通過完整自我測試、Gmail 通知、Google Drive 備份、語言介面與定時測速測試。

---

Release date: 2026-08-07

NetCheckMonitor 0.9.12 adds durable delivery audit records for Gmail notifications and Google Drive backups so daily automation outcomes can be traced locally.

## Highlights

- Adds `NetCheck_Delivery_<computer name>-<computer ID>.csv`.
- Records Gmail test emails, daily reports, and network-recovery notifications.
- Records Google Drive daily backups.
- Each row contains the timestamp, delivery type, `SUCCESS`, `FAILED`, or `SKIPPED` status, latency, target, and details.
- Details include the action, report date, attachment count, Drive folder, or a concise sanitized error summary.
- Includes the delivery audit file in the full record-backup ZIP.
- Never stores OAuth tokens or email bodies in the audit log.

## Verification

- Adds automated coverage for record format, statuses, error sanitization, and disconnected Gmail/Drive failure paths.
- Passes the complete self-test plus Gmail notification, Google Drive backup, language UI, and scheduled speed-test probes.
