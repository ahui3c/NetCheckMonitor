# NetCheckMonitor 0.9.6 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.6 強化長時間監控保護、介面語言選擇、事件追蹤與累積報表呈現。

### 避免 Windows 休眠與關機

- 增加「監控期間防止電腦進入休眠」選項，預設開啟，避免長時間測試因自動休眠中斷。
- 增加可選用的 Windows 關機／重新啟動阻擋功能；啟用後應先停止監控或使用程式內的「關閉程式」按鈕再關機。
- 兩項保護功能可分別設定，強制更新、斷電及硬體重置仍可能中斷程式。

### 介面語言改為手動選擇

- 取消依照 Windows 系統語言自動判斷。
- 第一次執行時由使用者選擇「繁體中文」或「English」，選擇結果會保存且不重複詢問。
- 可在設定頁手動變更介面語言，新語言會在下次啟動程式時套用。
- 編譯時明確使用 UTF-8 保存中英文介面文字。

### 增加事件註記

- 監控中或暫停期間都可加入最多 500 字元的事件註記。
- 提供「重開數據機」、「重開無線路由」、「電腦重新開機」、「下雨」及「打雷」快速填寫。
- 註記會立即保存到主要 CSV 與本機復原副本，不影響斷線次數、時間及百分比。
- 報表將註記整合到「斷線事件與事件註記」表格，24 小時時間軸以紫色標線顯示。

### 調整報表顯示細節

- 即時與結束 HTML 報表累積所有尚未清除的歷史 CSV；只有執行「清除儲存資料」才重新歸零。
- 程式未執行、關機、工作階段間空白、暫停、中斷及沒有檢查紀錄的區段不納入有效監控與斷線百分比。
- 恢復 HTML 易讀字級，本文與表格為 16px、主要統計數字為 24px；PDF 維持適合列印的緊湊版面。
- 每日統計改為一行文字、一行全寬且縮排的 24 小時時間軸；日期列最新在上，圖內時間由右側 00:00 往左至 24:00。
- 斷線事件與進階診斷依日期交替使用淡色背景，斷線與註記表格移到進階診斷之前。
- HTML 時間軸支援滑鼠提示，可查看連線狀態、檢查內容及事件註記。

## English

NetCheckMonitor 0.9.6 improves long-running monitoring protection, manual language selection, event tracking, and cumulative report presentation.

### Windows sleep and shutdown protection

- Added a default-enabled option to prevent system sleep while monitoring.
- Added an optional Windows shutdown/restart block; stop monitoring or use the in-app Exit button before shutting down.
- Both protections can be configured independently. Forced updates, power loss, and hardware resets can still interrupt the app.

### Manual interface language selection

- Removed automatic Windows system-language detection.
- The first launch asks the user to choose Traditional Chinese or English and remembers that choice.
- The language can be changed later in Settings and is applied the next time the app starts.
- Source files are explicitly compiled as UTF-8 to preserve interface text.

### Event notes

- Added timestamped notes of up to 500 characters while monitoring or paused.
- Added quick entries for restarting the modem, wireless router, or computer, plus rain and thunder.
- Notes are flushed immediately to the primary CSV and recovery copy without affecting outage statistics.
- Reports integrate notes into the Outage Events and Event Notes table and show purple markers on 24-hour timelines.

### Report presentation refinements

- Live and final HTML reports aggregate every historical CSV that has not been cleared; only Clear Saved Data resets cumulative totals.
- App-not-running, powered-off, between-session, paused, interrupted, and no-check periods are excluded from effective monitoring and outage percentages.
- Restored readable HTML sizing with 16px body/table text and 24px key metrics while keeping PDFs compact for printing.
- Daily statistics use one text row plus one indented full-width timeline row. Dates are newest-first, while chart time runs from 00:00 on the right to 24:00 on the left.
- Outage and diagnostic tables use subtle alternating date colors, and the combined outage/note table appears before diagnostics.
- HTML timelines provide hover details for connectivity checks and event notes.
