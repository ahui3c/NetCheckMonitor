# NetCheckMonitor 0.9.16 更新說明 / Release Notes

NetCheckMonitor 0.9.16 新增透過既有 Google Drive 備份資料夾與 NetCheck Viewer 安全交換設定的能力，並首次提供多電腦集中檢視工具 NetCheck Viewer。

## 主要更新

- NetCheckMonitor 會在每台電腦自己的 `Net_Check / <電腦名稱>` 資料夾建立 `NetCheck_Control.json`。
- NetCheck Viewer 可透過 Google Drive 電腦版同步資料夾，修改該電腦的監控檢查間隔與每日資料備份時間。
- 監控程式每 5 分鐘輪詢控制檔；成功套用後會回寫實際值、套用時間與結果。
- 控制檔只接受白名單欄位：監控間隔限制為 10～3600 秒，備份時間限制為 00:00～23:59，不能傳送命令、程式、路徑或任意網址。
- Viewer 以原地覆寫方式更新控制檔，保留 Google Drive 檔案身分與既有 `drive.file` 授權範圍。
- 設定套用成功、拒絕或同步失敗會寫入 Delivery CSV，服務欄位為 `VIEWER_CONTROL`，方便後續追蹤。
- 監控檢查間隔現在會保存於可攜式設定檔，重新啟動後仍維持 Viewer 或使用者最後設定的值。

## NetCheck Viewer 0.2.0

- 集中掃描 Google Drive、NAS 或一般資料夾中的多台 NetCheckMonitor 備份。
- 顯示每台電腦最後回傳、最後監控資料、連線率、平均延遲、斷線事件與最近測速。
- 提供每日歷史、斷線事件、定時測速與來源檔案明細。
- 對支援控制檔的電腦顯示「修改遠端設定」；舊版會提供明確升級提示。
- 每 5 分鐘自動重新掃描，並保留手動重新掃描功能。

## 驗證

- NetCheckMonitor 完整回歸測試
- Google Drive 控制協定、加密設定與可攜式設定 probes
- NetCheck Viewer 多電腦分析、控制檔原地寫入與 UI render 測試
- 兩個可攜版 ZIP 的版本、檔案清單與 SHA-256 驗證

---

NetCheckMonitor 0.9.16 adds a safe settings channel through the existing Google Drive backup folder and introduces NetCheck Viewer for centralized multi-computer monitoring analysis.

## Highlights

- NetCheckMonitor creates `NetCheck_Control.json` inside each computer's `Net_Check / <computer name>` folder.
- NetCheck Viewer can request changes to the monitoring interval and daily backup time through the locally synchronized Google Drive folder.
- The monitor polls every five minutes and writes back the applied values, timestamp, and result.
- Only allowlisted settings are accepted: 10–3600 seconds for monitoring and 00:00–23:59 for daily backup. Commands, executables, paths, and arbitrary URLs are never accepted.
- Viewer updates the existing file in place to preserve its Google Drive identity and the existing `drive.file` authorization boundary.
- Applied, rejected, and failed synchronization outcomes are recorded in the Delivery CSV under `VIEWER_CONTROL`.
- The monitoring interval is now persisted in the portable settings file.

## NetCheck Viewer 0.2.0

- Consolidates backups from multiple computers stored in Google Drive, a NAS, or a regular folder.
- Shows last return time, last monitoring data, availability, average latency, outages, and recent speed tests.
- Provides daily history, outage, scheduled speed-test, and source-file views.
- Offers remote settings for supported computers and a clear upgrade notice for older versions.
