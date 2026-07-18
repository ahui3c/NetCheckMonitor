# NetCheckMonitor 0.9.5 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.5 以長時間監控的自動化、斷線判讀與資料復原為主要更新方向：

- 增加登入 Windows 後自動執行功能，電腦異常重新啟動後可接續未完成的監控測試。
- 增加程式開啟後自動開始檢測的獨立設定。
- 增加自訂測試目標功能，可設定最多三組網站或 IP 並依序測試。
- 強化 HTML 與 PDF 報表，加入每日斷線時間及百分比、斷線事件、延遲統計、24 小時時間軸與更多完整資訊。
- 系統匣圖示以綠色、紅色、橘色與灰色分別顯示正常、斷線、檢查中與暫停狀態。
- 主畫面顯示使用中的網卡、連線方式及 Wi-Fi 訊號，相關資訊也會寫入 CSV、HTML 與 PDF。
- 增加可選用的進階分層連線診斷，只在一般連線測試失敗時檢查網卡、閘道、DNS、IPv4、IPv6、HTTPS 及 Wi-Fi 訊號。
- 完善首次失敗快速複查、長時間斷線追蹤、單一執行個體、防誤關、異常結束與 Windows 重新開機後的資料接續。

進階診斷的開關不會改變斷線判定、斷線時間或百分比的計算方式；未啟用期間的失敗會在報表中標示為未執行進階診斷。

## English

NetCheckMonitor 0.9.5 focuses on long-running automation, clearer outage diagnosis, and durable recovery:

- Added automatic launch after Windows sign-in so an unfinished monitoring session can resume after an unexpected restart.
- Added an independent option to start monitoring automatically when the app opens.
- Added up to three custom website or IP targets, tested in sequence.
- Expanded HTML and PDF reports with daily outage duration and percentage, outage events, latency statistics, 24-hour timelines, and more complete test information.
- Added color-coded tray status: green for online, red for outage, orange for checking, and gray for paused.
- Added active adapter, connection type, and Wi-Fi signal information to the main window and CSV/HTML/PDF reports.
- Added optional advanced layered diagnostics that check the adapter, gateway, DNS, IPv4, IPv6, HTTPS, and Wi-Fi signal only after a normal connectivity test fails.
- Improved fast failure verification, prolonged-outage tracking, single-instance protection, accidental-close protection, abnormal-exit handling, and session recovery after Windows restarts.

The advanced diagnostics setting does not change outage detection, outage duration, or percentage calculations. Failures recorded while it is disabled are marked as not diagnosed.
