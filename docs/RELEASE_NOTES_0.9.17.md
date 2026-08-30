# NetCheckMonitor 0.9.17 更新說明 / Release Notes

NetCheckMonitor 0.9.17 搭配 NetCheck Viewer 0.2.1，改善 Viewer 啟動時的備份資料夾回復與提醒流程。

## 主要更新

- Viewer 會保存最後一次成功使用的備份資料夾，下一次啟動時自動載入並掃描。
- 上次路徑不存在、無法存取，或資料夾內沒有可分析的 NetCheck 備份資料時才顯示提醒。
- 每 5 分鐘的背景自動重新掃描不會重複彈出無資料提醒。
- 手動重新掃描或重新選擇資料夾時，如果沒有資料仍會提供明確提示。
- Viewer 自我測試新增設定保存、重新載入與空資料夾判斷驗證。

## 版本

- NetCheckMonitor 0.9.17
- NetCheck Viewer 0.2.1

## 驗證

- NetCheckMonitor 完整回歸測試
- NetCheck Viewer 多電腦分析、設定保存、空資料判斷與 UI render 測試
- 兩個可攜版 ZIP 的版本、內容與 SHA-256 驗證

---

NetCheckMonitor 0.9.17 ships with NetCheck Viewer 0.2.1 and improves restoration and validation of the Viewer backup folder.

## Highlights

- Viewer remembers the last successfully used backup folder and scans it automatically on the next launch.
- A reminder appears only when the saved path is missing or inaccessible, or when no usable NetCheck backup data is found.
- Five-minute background refreshes do not repeatedly display an empty-data reminder.
- Manual rescans and newly selected folders still provide clear feedback when no data is available.
- Viewer self-tests now verify settings persistence, reload behavior, and empty-folder detection.
