# NetCheckMonitor 0.9.19 更新說明 / Release Notes

NetCheckMonitor 0.9.19 主要更新 NetCheck Viewer。Viewer 版本提升至 0.3.0，加入集中異常處理、增量掃描與多電腦圖形趨勢，讓大量備份資料能更快整理並更容易找出需要處理的電腦。

## 異常事件中心

- 首頁新增「需要處理」，集中顯示備份逾期、24 小時連線率低於門檻、定時測速連續失敗、Viewer 遠端設定長時間等待套用，以及 CSV 損壞、日期中斷或無法讀取。
- 異常分為嚴重、注意、資訊三級，並可標記完成或取消標記。
- 可調整備份時限、連線率、測速失敗次數、遠端設定等待時間與完整校正週期。

## 增量掃描

- 保存 CSV 路徑、大小、修改時間及最後解析位置。
- 未變更檔案直接使用快取，只重新解析新增或變更的檔案。
- Google Drive、NAS 或本機同步檔案出現時，由 FileSystemWatcher 觸發更新。
- 掃描期間收到的同步事件會延後重試，不會遺失。
- 依設定週期完整校正所有檔案，補足檔案系統事件可能遺漏的變化。

## 圖形趨勢

- 7／30／90 天連線率，以及平均、最高延遲趨勢。
- 下載、上傳與測速延遲趨勢。
- 每小時斷線分布熱點與備份回傳延遲。
- 可同時勾選多台電腦疊加比較。

## 驗證

- NetCheck Viewer 增量快取、單檔更新、CSV 損壞列、異常標記與趨勢資料自我測試。
- NetCheck Viewer 既有探針與 1440×940 畫面渲染。
- NetCheckMonitor 完整自我測試、語言、更新器、自動更新、可攜設定、雲端、Gmail 與測速回歸測試。
- 兩個可攜版 ZIP、版本與 SHA-256 驗證。

---

NetCheckMonitor 0.9.19 focuses on NetCheck Viewer 0.3.0. The Viewer now provides a centralized incident queue, incremental CSV indexing, file-system-triggered refresh, periodic full reconciliation, and multi-computer trend charts for availability, latency, speed tests, backup delay, and outage hours.
