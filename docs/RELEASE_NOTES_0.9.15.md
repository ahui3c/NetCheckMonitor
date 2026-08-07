# NetCheckMonitor 0.9.15 更新說明 / Release Notes

NetCheckMonitor 0.9.15 修正程式安裝於 Windows 受保護目錄時無法自動更新的問題，並改善設定頁維護按鈕的可讀性。

## 主要更新

- 程式位於 `Program Files` 等目前使用者不可寫入的目錄時，不再於更新檢查階段直接失敗。
- 更新套件下載並完成 SHA-256、manifest、執行檔版本與檔案雜湊驗證後，會顯示一次 Windows UAC 系統管理員權限確認。
- 使用者允許 UAC 後，更新器會自動替換程式、重新啟動新版，並在原本正在監控時自動接續。
- 提權時只執行安裝目錄內既有的可信任更新器；更新器會再次驗證快取中的 manifest 與所有更新檔案，避免使用者可寫入的快取程式直接取得系統管理員權限。
- 若使用者取消 UAC，原版本會繼續執行；原本正在監控時會恢復監控，並在更新紀錄中寫入 `CANCELLED`。
- 設定頁的「清除全部資料」與「強制重製詳細報表」改用正常 10pt 字級，並同步精簡中英文按鈕文字。

## 首次升級注意事項

- 0.9.13 與已發布的 0.9.14 尚未包含 UAC 後備流程。若這些版本已放在 `Program Files`，本次仍需手動下載並覆蓋升級至 0.9.15 一次。
- 升級至 0.9.15 後，後續版本即可從受保護目錄按「線上更新」，經一次 UAC 確認後自動完成更新。

## 驗證

- 一般目錄無提權更新與受保護目錄 `runas` 啟動參數測試
- 更新 ZIP SHA-256、Zip Slip、manifest 與逐檔雜湊驗證
- 更新器替換、使用者資料保留、竄改套件拒絕及更新紀錄測試
- 設定頁三行配置、維護按鈕正常字級及中英文介面測試

---

NetCheckMonitor 0.9.15 fixes automatic updates when the application is installed under a protected Windows folder and improves the readability of Settings maintenance buttons.

## Highlights

- Update checks no longer fail immediately when the application is under a non-writable folder such as `Program Files`.
- After SHA-256, manifest, executable-version, and per-file hash verification, Windows displays one UAC administrator-permission prompt.
- Approving UAC automatically replaces the application, restarts the new version, and resumes active monitoring when needed.
- Elevated updates execute only the trusted updater already installed in the protected application folder. That updater revalidates the cached manifest and every payload file before replacement.
- Cancelling UAC keeps the current version running, restores monitoring when applicable, and records `CANCELLED` in the update log.
- Clear All Data and Rebuild Detail Reports now use the normal 10pt Settings font with shorter Chinese and English labels.

## First Upgrade Note

- Versions 0.9.13 and the previously published 0.9.14 do not contain this UAC fallback. If either is already under `Program Files`, manually download and replace it with 0.9.15 once.
- After upgrading to 0.9.15, future releases can update from a protected folder automatically after one UAC approval.
