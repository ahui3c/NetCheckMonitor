# NetCheckMonitor 0.9.7 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.7 修正 Google Drive 登入，並強化設定與監控資料的可攜性。

### 1. 修正 Google Drive 登入問題

- 修正瀏覽器完成 Google 授權後，程式顯示 `client_secret is missing` 而無法完成登入的問題。
- 授權碼交換與 refresh token 更新都會帶入 Desktop OAuth 用戶端所需憑證。
- 保留 PKCE、隨機 state 驗證及 `drive.file` 最小權限；使用者仍只需登入自己的 Google 帳號並授權，不需自行下載憑證。
- 舊設定若已有相同 OAuth 用戶端的 refresh token，程式會自動補用新版內建憑證。
- 建置憑證不提交到公開原始碼，登入權杖仍由 Windows DPAPI 依目前 Windows 帳號加密保存。

### 2. 可攜式設定檔

- 監控目標、語言、啟動行為、電源保護、Google Drive 備份排程及其他程式設定改為保存在執行檔旁的設定檔。
- 攜帶整個程式資料夾到其他 Windows 電腦時，設定可一併保留。
- 偵測到舊版本位於 AppData 的設定時，會在背景執行一次性移轉，不需要手動複製。
- Google 帳號 refresh token 仍受 Windows DPAPI 保護，換到另一個 Windows 帳號或電腦後需重新登入 Google Drive。

### 3. 匯出全部紀錄備份 ZIP

- 設定頁新增「匯出全部紀錄備份 ZIP」。
- 可將所有尚未清除的 CSV 原始資料、HTML 報表及 PDF 報表打包為單一 ZIP 檔。
- ZIP 依資料類型分類，並包含檔案清單、匯出時間與電腦名稱，方便保存、轉移與叫修歸檔。

## English

NetCheckMonitor 0.9.7 fixes Google Drive sign-in and improves portability for settings and monitoring data.

### 1. Google Drive sign-in fix

- Fixed the `client_secret is missing` error shown after completing browser authorization.
- Both authorization-code exchange and refresh-token renewal now include the credential required by the Desktop OAuth client.
- PKCE, random state validation, and the limited `drive.file` scope remain enabled. Users still only sign in with their own Google account and grant access; no credential download is required.
- Existing settings with a refresh token for the same OAuth client automatically use the corrected built-in release credential.
- Build credentials are not committed to the public source, and Windows DPAPI continues to protect the saved sign-in token for the current Windows account.

### 2. Portable settings file

- Monitoring targets, language, startup behavior, power protection, Google Drive schedule, and other settings are stored beside the executable.
- Settings travel with the application folder when it is moved to another Windows computer.
- Previous AppData settings are migrated once in the background without manual copying.
- Google refresh tokens remain protected by Windows DPAPI, so Google Drive must be signed in again under another Windows account or computer.

### 3. Export all monitoring data as ZIP

- Added **Export All Data Backup ZIP** to Settings.
- Packages all uncleared raw CSV data plus HTML and PDF reports into one ZIP file.
- The archive groups files by type and includes a manifest with export time and computer name for transfer, support evidence, and long-term storage.
