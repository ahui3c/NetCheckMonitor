# NetCheckMonitor 0.9.8 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.8 增加定時測速參考功能，並簡化主畫面與設定入口。

### 1. 增加定時測速參考功能（Beta）

- 可選擇啟用 Cloudflare 定時測速，預設每 24 小時一次，支援 1～168 小時間隔。
- 提供快速、標準與完整三種多串流測速等級，記錄下載、上傳、延遲、Jitter、測試流量及當時網路介面。
- 測速歷史使用獨立速度趨勢 HTML 報表，不會影響斷線次數、有效監控時間或斷線百分比。
- 加入計量網路警告、流量與免責提示、15 分鐘冷卻，以及 HTTP 403／429 自動退避保護。
- 設定頁提供 Speedtest 與中華電信 HiNet 官方測速連結；不同服務的伺服器、路由及算法不同，結果僅供趨勢與叫修參考。

### 2. 簡化界面版面

- 將開始與停止監控整合為同一按鈕；綠色表示可開始，監控中切換為紅色停止按鈕。
- 將「產生即時報表」統一改為「查看報表」，移除功能重疊的結束並產生報表按鈕。
- 將事件註記移到暫停按鈕右側，方便監控期間快速加入處理紀錄。
- 將 Google Drive 備份設定及清除全部儲存資料移入設定頁，降低主畫面按鈕數量。
- 首頁移除手動測速入口；測速僅由設定頁啟用定時執行，速度趨勢報表也集中在同一頁。

### Google Drive 登入相容性

- 本版保留 0.9.7 修正後的 Desktop OAuth 登入流程、PKCE、隨機 state 驗證與 `drive.file` 最小權限。
- 授權碼交換與 refresh token 更新仍會使用建置時安全注入的用戶端憑證。
- Google OAuth 本機憑證檔不會提交至公開原始碼；登入權杖仍由 Windows DPAPI 保護。

## English

NetCheckMonitor 0.9.8 adds an optional scheduled speed-test reference feature and simplifies the main interface and settings access.

### 1. Scheduled speed-test reference (Beta)

- Optionally runs Cloudflare speed tests every 1–168 hours, with a 24-hour default interval.
- Provides Quick, Standard, and Full multi-stream levels and records download, upload, latency, jitter, transferred data, and network-interface context.
- Stores history in a separate HTML speed trend report without affecting outage counts, effective monitoring time, or outage percentage.
- Includes metered-network warnings, data-usage notices, a 15-minute cooldown, and automatic backoff after HTTP 403/429 responses.
- Settings includes official Speedtest and Chunghwa Telecom HiNet links. Different services use different servers, routes, and algorithms, so results are for trend and troubleshooting reference only.

### 2. Simplified interface

- Combines Start and Stop Monitoring into one button: green when ready to start and red while monitoring is active.
- Renames Create Live Report to View Report and removes the overlapping Stop and Create Report button.
- Moves Event Note beside Pause for quicker access during monitoring.
- Moves Google Drive backup settings and Clear All Saved Data into Settings to reduce main-window clutter.
- Removes on-demand speed testing from the main window; scheduled testing and the speed trend report are managed from one settings page.

### Google Drive sign-in compatibility

- Retains the corrected Desktop OAuth flow from 0.9.7, including PKCE, random state validation, and the limited `drive.file` scope.
- Authorization-code exchange and refresh-token renewal continue to use the client credential securely injected at build time.
- The local Google OAuth credential file is not committed to public source, and Windows DPAPI continues to protect saved sign-in tokens.
