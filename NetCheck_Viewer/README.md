# NetCheck Viewer

NetCheck Viewer 是 NetCheckMonitor 的獨立多電腦集中檢視工具。它唯讀掃描共用備份資料夾，不會修改原始 CSV，也不需要連線到各台受監控電腦。

## 主要功能

- 遞迴讀取 Google Drive、NAS 或一般資料夾中的 NetCheckMonitor 備份。
- 依電腦名稱與 8 碼識別碼合併每日監控資料和定時測速資料。
- 顯示每台電腦最後備份回傳時間、最後資料時間及最後連線狀態。
- 統計檢查連線率、確認失敗、疑似異常、平均延遲、斷線事件與最長斷線。
- 首頁「需要處理」集中列出備份逾期、24 小時連線率過低、定時測速連續失敗、Viewer 設定等待套用，以及 CSV 損壞、日期中斷或無法讀取等異常。
- 異常分為「嚴重、注意、資訊」，可標記完成並選擇是否顯示已標記項目；各項門檻可由 Viewer 內調整。
- 提供 7／30／90 天連線率、平均與最高延遲、下載／上傳／測速延遲、備份回傳延遲，以及每小時斷線熱點圖。
- 趨勢分析可同時勾選多台電腦疊加比較，另保留每日歷史、斷線事件、定時測速及來源檔案明細。
- 建立增量索引保存檔案路徑、大小、修改時間與最後解析位置，只重新解析新增或變更的 CSV。
- 使用 `FileSystemWatcher` 在同步檔案出現或變更後自動更新；每五分鐘檢查一次，並依設定週期執行完整校正避免漏掉事件。
- 自動記住上次成功使用的備份資料夾；只有路徑失效或沒有可分析資料時才提醒。
- 自動排除不同備份檔案中重複的監控與測速紀錄。
- 對已建立 `NetCheck_Control.json` 的新版 NetCheckMonitor，可從 Viewer 修改該電腦的監控間隔與每日資料備份時間。
- 遠端設定只允許白名單欄位與安全範圍，監控程式套用後會把實際值、時間與成功／拒絕原因寫回控制檔。

## 遠端設定運作方式

NetCheckMonitor 仍使用既有的 Google Drive `drive.file` 權限，在每台電腦自己的 `Net_Check / <電腦名稱>` 資料夾建立 `NetCheck_Control.json`。Viewer 修改 Google Drive 電腦版同步下來的同一檔案；同步回雲端後，監控程式通常會在 5 分鐘內讀取並套用。

舊版或尚未完成同步的電腦不會由 Viewer 自行建立控制檔，以免越過監控程式的授權範圍。請先更新 NetCheckMonitor、連接 Google Drive，並等待完成一次同步。

## 建置

在 Windows PowerShell 執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

建置結果：

- `dist/NetCheck_Viewer-Portable/NetCheck_Viewer.exe`
- `dist/NetCheck_Viewer-Portable.zip`

## 測試

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\NetCheckViewerProbe.ps1
```

測試會建立三台模擬電腦資料，驗證多機分組、回傳逾期、每日統計、斷線事件、測速解析、重複排除、增量快取重用與單檔更新、異常標記保存、趨勢資料、資料夾設定保存、空資料判斷及遠端控制檔安全寫入。

## 系統需求

- Windows 10 / 11
- .NET Framework 4.8
