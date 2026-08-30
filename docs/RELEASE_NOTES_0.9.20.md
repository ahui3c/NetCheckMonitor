# NetCheckMonitor 0.9.20 更新說明 / Release Notes

NetCheckMonitor 0.9.20 讓雲端備份狀態更容易辨識，並將 NetCheck Viewer 更新至 0.3.1，加入第一次啟動說明與可隨時重新開啟的使用指引。本版也加入 WinGet ZIP portable manifest 的產生與驗證工具，方便後續提交至 WinGet Community Repository。

## 監控程式首頁備份狀態

- 首頁新增 Google Drive 備份資訊列，顯示未連接、已連接或備份中。
- 直接顯示每日 PDF／CSV 備份是否開啟及預定時間。
- 直接顯示定時測速資料是否會隨每日備份一併處理，並說明未生效原因。
- 狀態會在 Google 登入、備份執行、Viewer 更新備份時間或定時測速設定變更後自動刷新。
- 可由資訊列的「設定…」直接開啟 Google Drive 備份設定。

## NetCheck Viewer 0.3.1

- 第一次啟動時說明 Viewer 是集中分析工具，不是即時主從或遠端遙控系統。
- 圖解多台監控電腦、共用 Google Drive 備份資料夾與中央 Viewer 的資料流程。
- 說明可檢視的資料、非同步設定能力及 Google Drive 同步造成的生效延遲。
- 可勾選「之後不再自動顯示」，仍能由首頁「使用說明」重新開啟。

## WinGet 發行準備

- 新增 `AHui3C.NetCheckMonitor` ZIP portable manifest 產生器。
- 新增 manifest 結構、版本、下載網址與 SHA-256 驗證測試。
- 新增正式 Release 後的 WinGet Community Repository 提交流程文件。
- 在套件正式獲 WinGet Community Repository 收錄前，README 會清楚標示指令尚待收錄後使用。

## 驗證

- NetCheckMonitor 完整自我測試與中英文介面測試。
- Google Drive 首頁狀態的未連接、已連接、備份中、時間及定時測速開關組合測試。
- NetCheck Viewer 自我測試、第一次使用說明設定保存及畫面渲染。
- WinGet manifest 驗證、兩個可攜版 ZIP 內容、版本與 SHA-256 檢查。

---

NetCheckMonitor 0.9.20 adds a live Google Drive backup status rail to the monitor home page. It shows connection and backup progress, the daily PDF/CSV schedule, and whether scheduled speed-test data is included. NetCheck Viewer 0.3.1 adds first-run guidance that explains the shared Google Drive workflow, centralized analysis role, asynchronous settings, and synchronization delay. This release also adds WinGet ZIP portable manifest generation, validation, and maintenance documentation for a future WinGet Community Repository submission.
