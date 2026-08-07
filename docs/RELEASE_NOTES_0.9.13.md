# NetCheckMonitor 0.9.13 更新說明 / Release Notes

NetCheckMonitor 0.9.13 新增 Windows 可攜版的一鍵線上自動更新機制。

## 主要更新

- 「關於」頁改為「線上更新」按鈕；按一次即可檢查、下載、驗證並安裝最新正式公開版本。
- 新增獨立 `NetCheckUpdater.exe`，主程式安全結束後自動替換檔案並重新啟動。
- 更新前驗證 GitHub Release SHA-256、套件大小、更新清單、每個檔案雜湊、EXE 產品名稱與版本。
- 安全解壓縮會拒絕 ZIP 路徑穿越及未列入清單的必要檔案缺失。
- 更新器會備份舊版；替換或新版啟動失敗時自動回復。
- 更新器只替換清單內的程式與說明文件，不修改 `NetCheck_Data`、Google 權杖或使用者檔案。
- 若正在監控，會等待 Gmail、Google Drive、連線檢查與測速安全結束，保留工作階段並在新版啟動後無提示接續。
- 新增 `NetCheck_Update.csv`，記錄檢查、下載、驗證、替換、重新啟動與回復結果。

## 限制

- 程式必須放在目前使用者可寫入的資料夾；若位於受保護的系統目錄，更新會安全取消並保留原版本。
- 0.9.13 是第一個內含更新器的版本；使用者升級到此版本後，後續版本才可使用完整自動更新。

---

NetCheckMonitor 0.9.13 adds one-click automatic updates for the portable Windows edition.

## Highlights

- The About page can check, download, verify, install, and restart to the latest public release with one click.
- A separate `NetCheckUpdater.exe` replaces files only after the main application exits safely.
- Verifies the GitHub release SHA-256 digest, package size, manifest, per-file hashes, executable product name, and version.
- Rejects ZIP path traversal and missing required payload files.
- Backs up the previous version and rolls back when replacement or relaunch fails.
- Replaces only managed program and guide files; `NetCheck_Data`, Google tokens, and user files remain untouched.
- Waits for Gmail, Drive, connectivity checks, and speed tests, then resumes an active monitoring session without prompting.
- Adds `NetCheck_Update.csv` for check, download, verification, replacement, relaunch, and rollback results.

0.9.13 is the first updater-enabled version; automatic updates apply to releases after users have this bootstrap version.
