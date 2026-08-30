# WinGet 發行與使用方式

NetCheckMonitor 使用 WinGet 的 ZIP portable 套件模式。WinGet 會下載 GitHub Release 的 `NetCheckMonitor-Portable.zip`、驗證 SHA-256、建立 `NetCheckMonitor` 命令別名，並追蹤安裝版本，因此能統一執行安裝、升級與移除。

## 用戶指令

套件獲 Microsoft WinGet Community Repository 接受後，可使用：

```powershell
winget install --id AHui3C.NetCheckMonitor --exact
winget upgrade --id AHui3C.NetCheckMonitor --exact
winget uninstall --id AHui3C.NetCheckMonitor --exact
```

安裝範圍為目前 Windows 使用者，不需要系統管理員權限。解除安裝會移除由 WinGet 管理的程式檔案；使用者自行產生的監控資料與備份不應由套件管理器主動刪除。

NetCheckMonitor 同時具備程式內建更新功能，因此 manifest 會宣告 `RequireExplicitUpgrade: true`，避免 `winget upgrade --all` 與程式正在進行的自動更新互相干擾。需要由 WinGet 升級時，請使用上方指定套件 ID 的明確升級指令。

## 發行者流程

1. 建置並發布版本專屬的 GitHub Release ZIP，檔名固定為 `NetCheckMonitor-Portable.zip`。
2. 產生該版本的 WinGet manifests：

   ```powershell
   .\scripts\New-WinGetManifest.ps1 -Version 0.9.15 -ReleaseDate 2026-08-07
   ```

3. 驗證本機 manifest 與發行包一致：

   ```powershell
   .\tests\WinGetManifestProbe.ps1 -ManifestDirectory .\packaging\winget\0.9.15
   winget validate --manifest .\packaging\winget\0.9.15
   ```

4. 將該版本目錄的四個 YAML 檔提交到 [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)，路徑為：

   ```text
   manifests/a/AHui3C/NetCheckMonitor/<版本>/
   ```

WinGet Community Repository 的審核與索引完成前，公開 `winget` 來源不會搜尋到新套件。每個新版本都必須先發布不可變的 GitHub Release 資產，再以該資產的實際 SHA-256 產生並提交新 manifests。
