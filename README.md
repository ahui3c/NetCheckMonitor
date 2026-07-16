# NetCheckMonitor

[繁體中文](README.md) | [English](README_EN.md)

<img src="assets/NetCheckMonitor-icon.png" alt="NetCheckMonitor 圖示" width="128">

NetCheckMonitor（中文名稱：對外網路連線能力監控程式）是免費、開源、無廣告的 Windows 工具。它會定時檢查電腦是否能連上外部網路，長時間記錄斷線狀況，並產生圖形化 HTML 與 PDF 報表，適合家用網路障礙報修與連線品質佐證。

目前版本：**0.9.1**

## 下載

- [下載 NetCheckMonitor Windows 可攜版](dist/NetCheckMonitor-Portable.zip)
- 解壓縮後執行 `NetCheckMonitor.exe`，不需要安裝。
- 系統需求：Windows 10／11、.NET Framework 4.8、Microsoft Edge（產生 PDF 使用）。

## 主要功能

- 預設每 60 秒依序測試 Microsoft、Google 與 Cloudflare HTTPS 端點。
- 任一端點成功即判定可對外連線，降低單一網站異常造成的誤判。
- 支援繁體中文與英文；繁體中文 Windows 自動顯示繁中，其他語言預設英文。
- 暫停與繼續監控；暫停時段會標示，但不納入連線率與每日斷線百分比。
- 每筆結果立即寫入 UTF-8 CSV，並同步保存本機復原副本。
- 監控中可產生即時 HTML 報表，不會中斷檢查。
- 可依全部資料或指定日期下載 A4 橫式 PDF。
- 報表包含測試電腦名稱、每日斷線時間、每日斷線百分比與 24 小時時間軸。
- Google Drive 每日定時備份完整 PDF 與原始 CSV 至 `Net_Check` 資料夾。
- 監控期間防止 Windows 自動睡眠；關閉視窗時縮到系統匣，避免誤關。
- 主畫面的安全關閉功能會先保存資料並建立最終報表。

## 快速開始

1. 下載並解壓縮可攜版。
2. 執行 `NetCheckMonitor.exe`。
3. 確認檢查間隔後按「開始監控」。
4. 不希望某段時間列入統計時，按「暫停」；恢復時按「繼續」。
5. 測試期間可按「產生即時報表」或「下載報表 PDF 文件」。
6. 完成測試時按「結束並產生報表」。
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
