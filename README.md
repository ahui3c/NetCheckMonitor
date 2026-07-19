# NetCheckMonitor

[繁體中文](README.md) | [English](README_EN.md)

<img src="assets/NetCheckMonitor-icon.png" alt="NetCheckMonitor 圖示" width="128">

NetCheckMonitor（中文名稱：對外網路連線能力監控程式）是免費、開源、無廣告的 Windows 工具。它會定時檢查電腦是否能連上外部網路，長時間記錄斷線狀況，並產生圖形化 HTML 與 PDF 報表，適合家用網路障礙報修與連線品質佐證。

目前版本：**0.9.6**

## 0.9.6 更新簡述

- 增加監控期間避免 Windows 進入休眠，以及可選擇阻止 Windows 關機／重新啟動的功能。
- 介面語言取消系統自動偵測，第一次執行時由使用者選擇繁體中文或英文，之後可在設定頁變更。
- 增加事件註記與快速填寫功能，方便記錄重開設備、天候或其他處理狀況並搭配斷線追蹤。
- 強化累積 HTML／PDF 報表、24 小時時間軸、事件整合、滑鼠提示、排序、字級與表格辨識細節。

完整內容請參閱 [0.9.6 更新說明](docs/RELEASE_NOTES_0.9.6.md)。

## 下載

- [下載 NetCheckMonitor Windows 可攜版](dist/NetCheckMonitor-Portable.zip)
- 解壓縮後執行 `NetCheckMonitor.exe`，不需要安裝。
- 系統需求：Windows 10／11、.NET Framework 4.8、Microsoft Edge（產生 PDF 使用）。

## 主要功能

- 預設每 60 秒依序測試 Microsoft、Google 與 Cloudflare HTTPS 端點。
- 任一端點成功即判定可對外連線，降低單一網站異常造成的誤判。
- 首次失敗會在 5 秒後快速複查，連續失敗才確認斷線；長時間斷線會自動降低複查頻率，但檢查間隔不會超過主畫面設定的週期。
- 監控期間系統匣圖示會即時顯示狀態：正常連線為綠色、確認斷線為紅色、檢查中為橘色、暫停為灰色。
- 主畫面即時顯示目前網卡、連線類型（有線／Wi-Fi／VPN）與 Wi-Fi 訊號百分比；網卡切換會寫入原始紀錄及報表。
- 「設定」可在內建目標與自訂目標間二選一；自訂可輸入最多三組網站或 IP，依序測試並在第一個成功時停止。
- 自訂設定保存於目前 Windows 使用者環境；監控中變更目標時會另開新工作階段，避免同一份報表混用不同測試目標。
- 監控期間仍可開啟設定；若測試目標有變更，程式會先安全保存原工作階段與報表，再以新目標自動重新開始監控。
- 可選擇「HTTPS 失敗時執行進階分層連線診斷」；正常連線仍只做原有輕量測試，失敗時才檢查網卡、閘道、DNS、IPv4、IPv6、HTTPS 與 Wi-Fi 訊號。此開關不改變斷線判定、時間或百分比統計。
- 支援繁體中文與英文；首次執行時由使用者選擇，之後可在設定頁手動變更。
- 暫停與繼續監控；暫停時段會標示，但不納入連線率與每日斷線百分比。
- 監控中可插入帶有當下時間的事件註記，自行輸入內容或快速填入重開數據機、重開無線路由、電腦重新開機、下雨及打雷；註記會整合到 CSV、HTML 與 PDF，但不影響斷線統計。
- 每筆結果立即寫入 UTF-8 CSV，並同步保存本機復原副本。
- 監控中可產生即時 HTML 報表，不會中斷檢查。
- 即時與結束 HTML 報表會累積彙整所有尚未清除的歷史 CSV；不同執行工作階段之間、程式未執行及沒有檢查紀錄的區段不納入統計。
- 可依全部資料或指定日期下載 A4 橫式 PDF。
- 報表包含每日斷線統計、最長／平均／最短斷線、第 95 百分位與最高延遲、平均延遲變動及 24 小時時間軸。
- 監控狀態會持久保存；程式當機或 Windows 重新開機後可接續原 CSV，未執行區段會標示並排除統計。
- 設定可分別選擇「登入 Windows 後自動啟動程式」與「程式啟動後自動開始監控」；若有未完成工作階段，會優先詢問是否接續。
- 啟動時會檢查 NetCheckMonitor 是否已在執行；重複開啟不會建立第二份監控，而會顯示既有視窗。
- Google Drive 每日定時備份完整 PDF 與原始 CSV 至 `Net_Check` 資料夾。
- 設定可選擇監控期間防止 Windows 進入休眠，並可另外選擇阻止關機或重新啟動；關機保護啟用時需先停止監控或按右下角「關閉程式」。強制更新、斷電或硬體重置仍可能使程式中斷。
- 按右上角 X 時縮到系統匣，首次操作會提醒應使用右下角「關閉程式」安全結束，提醒只顯示一次。
- 主畫面的安全關閉功能會先保存資料並建立最終報表。
- 「關於」頁提供 GitHub 專案連結及手動版本檢查；只有按下按鈕時才查詢 GitHub，發現新版後由使用者決定是否開啟 Releases 頁面。

## 快速開始

1. 下載並解壓縮可攜版。
2. 執行 `NetCheckMonitor.exe`。
3. 如需改用自己的網站或 IP，或啟用失敗後的進階分層診斷，先開啟「設定」。
4. 確認檢查間隔後按「開始監控」。
   若偵測到上次未完成的工作，程式會先詢問是否接續。
5. 不希望某段時間列入統計時，按「暫停」；恢復時按「繼續」。
6. 測試期間可按「產生即時報表」或「下載報表 PDF 文件」。
7. 完成測試時按「結束並產生報表」。
7. 離開程式請按右下角「關閉程式」，讓程式確認資料已安全保存。

完整操作方式請參閱[繁體中文使用說明](docs/使用說明_繁體中文.md)。

## Google Drive 備份

1. 按「Google Drive 備份設定」。
2. 按「登入 Google Drive」，使用自己的 Google 帳號完成瀏覽器授權。
3. 設定每日備份時間。

使用者不需要建立 Google Cloud 專案或下載憑證。程式使用 Desktop OAuth client ID 與 PKCE，不在原始碼或執行檔內嵌 client secret；只要求 `drive.file` 權限，登入權杖由 Windows DPAPI 加密保存在目前 Windows 帳號中。

## 資料與隱私

- 檔名格式：`NetCheck_<電腦名稱>-<8碼識別碼>_yyyyMMdd_HHmmss.csv`。
- 8 碼識別碼優先由 Windows MachineGuid 單向雜湊產生；原始 MachineGuid、MAC 位址與硬體序號不會寫入檔案。
- 主要資料存放在程式旁的 `NetCheck_Data`；無法寫入時改存「文件」資料夾。
- 復原副本存放在 `%LOCALAPPDATA%\NetCheck\Recovery`。
- 個人 CSV、HTML、PDF、Google refresh token 與設定檔不應提交到公開儲存庫。

## 從原始碼建置

在 Windows PowerShell 5.1 執行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

建置只使用 Windows 內建的 C# 編譯器，不需要安裝 .NET SDK。輸出位於 `NetCheck-Portable\NetCheckMonitor.exe`。

## 測試

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\SelfTest.ps1
```

完整測試會使用 Microsoft Edge 的無介面列印功能建立 PDF，並在隔離資料夾中驗證監控、暫停、報表、資料保護、雲端備份檔案及雙語介面。

## 作者與授權

- 廖阿輝
- <chehui@gmail.com>
- <https://ahui3c.com>

本專案採用 [MIT License](LICENSE)。
