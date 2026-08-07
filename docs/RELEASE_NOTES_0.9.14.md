# NetCheckMonitor 0.9.14 更新說明 / Release Notes

NetCheckMonitor 0.9.14 改善 Windows 登入後的自動接續流程，並精簡監控目標設定畫面。

## 主要更新

- 同時啟用「登入 Windows 後自動啟動程式」與「程式啟動後自動開始監控」時，由 Windows 登入啟動的程式會免確認直接接續未完成工作階段。
- 手動啟動程式或 Windows 應用程式復原啟動時，仍保留原本的接續確認，避免非預期自動操作。
- 若自動接續失敗，會保留原本 CSV 並建立新的監控工作階段，確保自動監控仍能開始。
- Windows 登入啟動改用專用 `--windows-autostart` 參數，與一般應用程式復原明確區分。
- 監控目標設定畫面的說明文字縮短，視窗高度由 780 縮為 700。
- 下方功能按鈕由四行整理為三行；清除資料與重製每日報表縮小並移至最後一行。
- 所有原有設定、資料清除確認、備份及報表重製保護機制維持不變。

## 驗證

- Windows 登入自動接續條件測試
- 設定畫面三行配置、按鈕邊界與中文實際渲染測試
- 中英文介面、可攜設定與自動更新回歸測試

---

NetCheckMonitor 0.9.14 improves unattended monitoring recovery after Windows sign-in and streamlines the monitoring-target Settings window.

## Highlights

- When both Windows sign-in startup and automatic monitoring are enabled, a Windows-launched instance resumes an unfinished session without confirmation.
- Manual launches and Windows application-recovery launches still ask before resuming.
- If automatic resume fails, the previous CSV is preserved and a new monitoring session is started.
- A dedicated `--windows-autostart` argument distinguishes sign-in startup from general application recovery.
- Shorter guidance and a 700-pixel Settings window reduce vertical space.
- Function buttons are reorganized from four rows into three, with compact Clear Data and Rebuild Daily Reports buttons on the final row.
- Existing confirmation, backup, clear-data, and report-rebuild safeguards remain unchanged.
