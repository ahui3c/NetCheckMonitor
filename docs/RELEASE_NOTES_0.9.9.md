# NetCheckMonitor 0.9.9 更新說明 / Release Notes

## 繁體中文

NetCheckMonitor 0.9.9 增加 Gmail 自寄報表與恢復通知，並修正跨資料目錄的累積與測速報表。

### 1. Gmail 每日報表與通知

- 可登入自己的 Gmail，定時寄送每日 PDF 與 CSV 報表。
- 可在確認斷線後恢復連線時寄送通知，並提供測試郵件功能。
- 寄件者與收件者固定為登入的同一個 Google 帳戶，介面不允許輸入其他地址。
- 只要求 `gmail.send` 寄信權限，不取得讀取信箱內容的權限。
- 登入權杖、設定及斷網期間的待寄通知使用 Windows DPAPI 保護。
- 完全斷網時先保留恢復通知，連線恢復後寄送；暫時失敗會依退避時間自動重試。
- Gmail 與 Google Drive 使用獨立授權，互不影響。

### 2. 累積報表修正

- 同一檔名存在於多個資料位置時，會依實際資料時間、檔案大小及修改時間選擇較新的版本。
- 自動納入目前活動工作階段所在的資料目錄。
- 正確處理停止後又恢復的工作階段，避免報表結束日期停留在較早時間。

### 3. 定時測速報表修正

- 速度趨勢報表會同時讀取目前資料目錄與活動工作階段資料目錄。
- 合併搬移前後的測速 CSV 並排除重複紀錄，避免舊目錄中的日期消失。

## English

NetCheckMonitor 0.9.9 adds self-addressed Gmail reports and recovery notifications, and fixes cumulative and speed reports across moved data directories.

### 1. Gmail daily reports and notifications

- Signs in to the user's own Gmail account to send scheduled daily PDF and CSV reports.
- Sends an optional notification after connectivity recovers from a confirmed outage and provides a test-email action.
- Fixes both sender and recipient to the same signed-in Google account; no other address can be entered.
- Requests only the `gmail.send` scope and cannot read mailbox contents.
- Protects sign-in tokens, settings, and queued offline notifications with Windows DPAPI.
- Queues recovery notices while offline and retries temporary delivery failures with backoff.
- Uses a separate grant from Google Drive, so the two integrations do not affect each other.

### 2. Cumulative report fixes

- When the same CSV name exists in multiple locations, selects the freshest copy using actual record time, file size, and modification time.
- Automatically includes the current active-session data directory.
- Correctly extends resumed sessions after an earlier stop marker instead of ending the report too early.

### 3. Scheduled speed-report fixes

- Reads speed history from both the current data directory and the active-session directory.
- Merges speed CSV records across pre-move and post-move locations while removing duplicates.
