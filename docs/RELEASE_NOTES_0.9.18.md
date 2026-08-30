# NetCheckMonitor 0.9.18 更新說明 / Release Notes

NetCheckMonitor 0.9.18 修正安裝於 Program Files 後，一般使用者啟動或自動開始監控時可能因無法寫入設定檔而中止的問題。

## 主要修正

- 安裝版不再嘗試將 NetCheckMonitor.settings.json.tmp 寫入 Program Files。
- 位於 Program Files 或其他唯讀位置時，設定自動改存 %LOCALAPPDATA%\NetCheck。
- 主設定、Google Drive、Gmail 與 Session 狀態使用一致的安全儲存位置。
- 一般可寫資料夾中的可攜版仍將設定保存在程式旁，不改變既有可攜行為。
- 安裝目錄若已有舊設定，首次啟動會自動搬移到使用者資料夾。
- 即使程式以系統管理員身分啟動，Program Files 安裝版仍固定使用 AppData，避免日後以一般權限執行再次失敗。

## 驗證

- Program Files、一般可攜資料夾與其他唯讀資料夾路徑測試
- 主設定、Google Drive、Gmail、Session 四種設定檔自動遷移測試
- 自動啟動、Cloud、Gmail 與完整 NetCheckMonitor 回歸測試
- 可攜版 ZIP、更新 manifest、版本與 SHA-256 驗證

---

NetCheckMonitor 0.9.18 fixes a startup crash that could occur when an installed copy tried to write settings into Program Files without administrator permission.

## Highlights

- Installed copies store settings under %LOCALAPPDATA%\NetCheck.
- Main settings, Google Drive, Gmail, and session state share the same safe storage policy.
- Portable copies in writable folders continue to store settings beside the executable.
- Existing settings found beside an installed executable are migrated automatically.
