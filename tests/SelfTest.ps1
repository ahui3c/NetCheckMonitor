param([string]$ExecutableName = 'NetCheckMonitor.exe')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root '.selftest'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root ('NetCheck-Portable\' + $ExecutableName)) -Destination (Join-Path $testRoot 'NetCheckMonitor.exe') -Force
$env:NETCHECK_BACKUP_DIR = Join-Path $testRoot 'Backup'
$env:NETCHECK_DATA_ROOTS = (Join-Path $testRoot 'NetCheck_Data') + ';' + $env:NETCHECK_BACKUP_DIR
$env:NETCHECK_CLOUD_SETTINGS = Join-Path $testRoot 'Cloud\settings.dat'
$env:NETCHECK_MONITOR_SETTINGS = Join-Path $testRoot 'Settings\monitor.json'
$env:NETCHECK_UI_STATE = Join-Path $testRoot 'Settings\ui-state.dat'
$env:NETCHECK_UI_LANGUAGE_FILE = Join-Path $testRoot 'Settings\language.dat'
$env:NETCHECK_SESSION_STATE = Join-Path $testRoot 'State\active-session.json'
$env:NETCHECK_UI_LANGUAGE = 'zh-TW'
$env:NETCHECK_INSTANCE_NAME = 'Local\NetCheckMonitor-SelfTest-' + [guid]::NewGuid().ToString('N')

$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $testRoot 'NetCheckMonitor.exe'))
$type = $assembly.GetType('NetCheck.MainForm', $true)
$singleInstanceType = $assembly.GetType('NetCheck.SingleInstance', $true)
$tryAcquireInstance = $singleInstanceType.GetMethod('TryAcquire', [Reflection.BindingFlags]'Static,NonPublic')
$firstMutexArgs = [object[]]@($null)
$secondMutexArgs = [object[]]@($null)
$firstInstanceAcquired = $tryAcquireInstance.Invoke($null, $firstMutexArgs)
$secondInstanceAcquired = $tryAcquireInstance.Invoke($null, $secondMutexArgs)
$singleInstanceGuard = $firstInstanceAcquired -and -not $secondInstanceAcquired -and $null -ne $firstMutexArgs[0]
if ($null -ne $firstMutexArgs[0]) { $firstMutexArgs[0].ReleaseMutex(); $firstMutexArgs[0].Dispose() }
$form = [Activator]::CreateInstance($type, $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic'
$dataButton = $type.GetField('dataButton', $flags).GetValue($form)
$clearDataButton = $type.GetField('clearDataButton', $flags).GetValue($form)
$exitButton = $type.GetField('exitButton', $flags).GetValue($form)
$cloudButton = $type.GetField('cloudButton', $flags).GetValue($form)
$aboutButton = $type.GetField('aboutButton', $flags).GetValue($form)
$settingsButton = $type.GetField('settingsButton', $flags).GetValue($form)
$eventNoteButton = $type.GetField('eventNoteButton', $flags).GetValue($form)
$versionLabel = $type.GetField('versionLabel', $flags).GetValue($form)
$networkInfoLabel = $type.GetField('networkInfoLabel', $flags).GetValue($form)
$networkStatusType = $assembly.GetType('NetCheck.NetworkStatusReader', $true)
$networkSnapshot = $networkStatusType.GetMethod('Capture', [Reflection.BindingFlags]'Static,Public').Invoke($null, @())
$networkStatusCapture = @('Wired', 'WiFi', 'VPN', 'Other', 'Disconnected').Contains($networkSnapshot.TypeCode) -and $networkSnapshot.WifiSignal -ge -1 -and $networkSnapshot.WifiSignal -le 100 -and $networkSnapshot.UiText.Contains('目前網卡：') -and $networkSnapshot.UiText.Contains('Wi-Fi 訊號：')
$trayIcon = $type.GetField('trayIcon', $flags).GetValue($form)
$trayStateType = $assembly.GetType('NetCheck.TrayConnectionState', $true)
$setTrayState = $type.GetMethod('SetTrayConnectionState', $flags)
$setTrayState.Invoke($form, [object[]]@([Enum]::Parse($trayStateType, 'Online'), $true))
$onlineTrayBitmap = $trayIcon.Icon.ToBitmap()
$onlineTrayPixel = $onlineTrayBitmap.GetPixel(16, 16)
$onlineTrayStatus = $trayIcon.Visible -and $trayIcon.Text.Contains('對外連線正常') -and $onlineTrayPixel.G -gt $onlineTrayPixel.R
$setTrayState.Invoke($form, [object[]]@([Enum]::Parse($trayStateType, 'Offline'), $true))
$offlineTrayBitmap = $trayIcon.Icon.ToBitmap()
$offlineTrayPixel = $offlineTrayBitmap.GetPixel(16, 16)
$offlineTrayStatus = $trayIcon.Visible -and $trayIcon.Text.Contains('對外連線中斷') -and $offlineTrayPixel.R -gt $offlineTrayPixel.G
$setTrayState.Invoke($form, [object[]]@([Enum]::Parse($trayStateType, 'Idle'), $false))
$onlineTrayBitmap.Dispose()
$offlineTrayBitmap.Dispose()
$downloadButtonLabel = $dataButton.Text -eq '下載報表 PDF 文件'
$clearButtonLayout = $clearDataButton.Width -le 130 -and $clearDataButton.Height -le 28 -and $clearDataButton.Top -ge 495
$exitButtonLayout = $exitButton.Text -eq '關閉程式' -and $exitButton.Width -le 120 -and $exitButton.Height -le 28 -and $exitButton.Top -ge 495 -and $exitButton.Left -gt $clearDataButton.Left
$reportFormType = $assembly.GetType('NetCheck.DataReportForm', $true)
$reportForm = [Activator]::CreateInstance($reportFormType, [object[]]@([Environment]::MachineName, 'A1B2C3D4'))
$clearRemovedFromPdfDialog = $null -eq $reportFormType.GetField('clearButton', $flags)
$cloudButtonLayout = $cloudButton.Text -eq 'Google Drive 備份設定' -and $cloudButton.Width -le 180 -and $cloudButton.Height -le 28 -and $cloudButton.Top -ge 495
$aboutButtonLayout = $aboutButton.Text -eq '關於' -and $aboutButton.Width -le 80 -and $aboutButton.Top -ge 495
$settingsButtonLayout = $settingsButton.Text -eq '設定' -and $settingsButton.Width -le 90 -and $settingsButton.Top -ge 495
$eventNoteButtonLayout = $eventNoteButton.Text -eq '事件註記' -and $eventNoteButton.Width -le 130 -and $eventNoteButton.Top -ge 495 -and $eventNoteButton.Left -gt $settingsButton.Left
$homeVersionLabel = $versionLabel.Text -eq 'v0.9.7' -and $versionLabel.Font.Size -le 8.5 -and $versionLabel.ForeColor -eq [Drawing.Color]::DarkGray
$monitorSettingsType = $assembly.GetType('NetCheck.MonitorSettingsStore', $true)
$monitorSettingsStorageMethod = $monitorSettingsType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,Public')
$monitorSettingsStorage = $monitorSettingsStorageMethod.Invoke($null, [object[]]@($env:NETCHECK_MONITOR_SETTINGS))
$portableSettingsType = $assembly.GetType('NetCheck.PortableSettingsStore', $true)
[string]$portableMigrationRoot = Join-Path $testRoot 'PortableMigration'
$portableMigration = $portableSettingsType.GetMethod('RunMigrationSelfTest', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($portableMigrationRoot))
$uiPreferenceType = $assembly.GetType('NetCheck.UiPreferenceStore', $true)
$uiPreferenceStorage = $uiPreferenceType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($env:NETCHECK_UI_STATE))
$uiPreferenceType.GetMethod('MarkCloseToTrayNoticeShown', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())
$monitorSettingsValue = $type.GetField('monitorSettings', $flags).GetValue($form)
$settingsFormType = $assembly.GetType('NetCheck.MonitorSettingsForm', $true)
$builtInTargets = $type.GetField('TestUrls', $staticFlags).GetValue($null)
$settingsForm = [Activator]::CreateInstance($settingsFormType, [object[]]@($monitorSettingsValue))
$settingsFormText = @($settingsForm.Controls | ForEach-Object { $_.Text }) -join "`n"
$settingsPageContent = $settingsForm.Text -eq '監控目標設定' -and $settingsFormText.Contains('使用內建測試目標（建議）') -and $settingsFormText.Contains('使用自訂測試目標') -and $settingsFormText.Contains('目標 1') -and $settingsFormText.Contains('目標 2') -and $settingsFormText.Contains('目標 3') -and $settingsFormText.Contains('HTTPS 失敗時執行進階分層連線診斷（選用）') -and $settingsFormText.Contains('監控期間防止電腦進入休眠（建議）') -and $settingsFormText.Contains('監控期間阻止 Windows 關機或重新啟動（請先停止監控）') -and $settingsFormText.Contains('登入 Windows 後自動啟動程式') -and $settingsFormText.Contains('程式啟動後自動開始監控') -and $settingsFormText.Contains('介面語言') -and $settingsFormText.Contains('下次啟動程式時套用') -and $settingsFormText.Contains('匯出全部紀錄備份 ZIP') -and $settingsFormText.Contains('強制重製每日詳細報表')
$settingsHidesBuiltInTargets = @($builtInTargets | Where-Object { $settingsFormText.Contains($_) }).Count -eq 0
$settingsCustomRadio = $settingsFormType.GetField('customRadio', $flags).GetValue($settingsForm)
$settingsSaveButton = $settingsFormType.GetField('saveButton', $flags).GetValue($settingsForm)
$settingsLanguageBox = $settingsFormType.GetField('languageBox', $flags).GetValue($settingsForm)
$settingsCompactLayout = $settingsForm.ClientSize.Height -le 670 -and $settingsCustomRadio.Top -le 180 -and ($settingsSaveButton.Bottom + 12) -le $settingsForm.ClientSize.Height
$settingsLanguageSelection = $settingsLanguageBox.Items.Count -eq 2 -and $settingsLanguageBox.Items[0] -eq '繁體中文' -and $settingsLanguageBox.Items[1] -eq 'English'
$eventNoteFormType = $assembly.GetType('NetCheck.EventNoteForm', $true)
$eventNoteForm = [Activator]::CreateInstance($eventNoteFormType)
$eventNoteFormText = @($eventNoteForm.Controls | ForEach-Object { $_.Text }) -join "`n"
$eventNoteDialogContent = $eventNoteForm.Text -eq '插入事件註記' -and $eventNoteFormText.Contains('重開數據機') -and $eventNoteFormText.Contains('重開無線路由') -and $eventNoteFormText.Contains('電腦重新開機') -and $eventNoteFormText.Contains('下雨') -and $eventNoteFormText.Contains('打雷')
$advancedDiagnosticsType = $assembly.GetType('NetCheck.AdvancedNetworkDiagnostics', $true)
$advancedDiagnosticsClassification = $advancedDiagnosticsType.GetMethod('RunClassificationSelfTest', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())
$diagnosticToggleBefore = [Activator]::CreateInstance($assembly.GetType('NetCheck.MonitorTargetSettings', $true), $true)
$diagnosticToggleAfter = [Activator]::CreateInstance($assembly.GetType('NetCheck.MonitorTargetSettings', $true), $true)
$diagnosticToggleBefore.UseCustomTargets = $false
$diagnosticToggleAfter.UseCustomTargets = $false
$diagnosticToggleBefore.AdvancedDiagnosticsEnabled = $false
$diagnosticToggleAfter.AdvancedDiagnosticsEnabled = $true
$diagnosticToggleBefore.PreventSleepWhileMonitoring = $false
$diagnosticToggleAfter.PreventSleepWhileMonitoring = $true
$diagnosticToggleBefore.PreventShutdownWhileMonitoring = $false
$diagnosticToggleAfter.PreventShutdownWhileMonitoring = $true
$advancedToggleNoRestart = -not $type.GetMethod('MonitoringTargetsChanged', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($diagnosticToggleBefore, $diagnosticToggleAfter))
$type.GetField('monitorSettings', $flags).SetValue($form, $diagnosticToggleAfter)
$type.GetField('running', $flags).SetValue($form, $true)
$shutdownBlockDecision = $type.GetMethod('ShouldBlockWindowsShutdown', $flags).Invoke($form, @())
$type.GetField('running', $flags).SetValue($form, $false)
$type.GetField('monitorSettings', $flags).SetValue($form, $monitorSettingsValue)
$sessionStateType = $assembly.GetType('NetCheck.SessionStateStore', $true)
$sessionStateStorageMethod = $sessionStateType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,Public')
[string]$sessionStateTest = Join-Path $testRoot 'State\storage-selftest.json'
$sessionStateStorage = $sessionStateStorageMethod.Invoke($null, [object[]]@($sessionStateTest))
$aboutFormType = $assembly.GetType('NetCheck.AboutForm', $true)
$aboutForm = [Activator]::CreateInstance($aboutFormType)
$checkVersionButton = $aboutFormType.GetField('checkVersionButton', $flags).GetValue($aboutForm)
$isNewerVersionMethod = $aboutFormType.GetMethod('IsNewerVersion', [Reflection.BindingFlags]'Static,NonPublic')
$versionComparison = $isNewerVersionMethod.Invoke($null, @('v0.9.8')) -and -not $isNewerVersionMethod.Invoke($null, @('v0.9.7')) -and -not $isNewerVersionMethod.Invoke($null, @('v0.9.6'))
$aboutText = @($aboutForm.Controls | ForEach-Object { $_.Text }) -join "`n"
$aboutPageContent = $aboutForm.Text -eq '關於 NetCheckMonitor' -and $aboutText.Contains('NetCheckMonitor') -and $aboutText.Contains('版本 0.9.7') -and $aboutText.Contains('可定時監控對外網路連線，紀錄斷線並產生圖文報表，並支援網路硬碟備份，PDF 下載，程式完全免費開源無廣告。') -and $aboutText.Contains('廖阿輝') -and $aboutText.Contains('chehui@gmail.com') -and $aboutText.Contains('https://ahui3c.com') -and $aboutText.Contains('https://github.com/ahui3c/NetCheckMonitor') -and $checkVersionButton.Text -eq '檢查新版本'
$aboutLabels = @($aboutForm.Controls | Where-Object { $_ -is [Windows.Forms.Label] })
$aboutLinks = @($aboutForm.Controls | Where-Object { $_ -is [Windows.Forms.LinkLabel] })
$aboutWebsiteLink = @($aboutLinks | Where-Object { $_.Text -eq 'https://ahui3c.com' })
$aboutGitHubLink = @($aboutLinks | Where-Object { $_.Text -eq 'https://github.com/ahui3c/NetCheckMonitor' })
$aboutUrlLinkScope = [bool]($aboutLabels | Where-Object { $_.Text -eq '網站：' }) -and
    [bool]($aboutLabels | Where-Object { $_.Text -eq 'GitHub 專案：' }) -and
    $aboutWebsiteLink.Count -eq 1 -and $aboutWebsiteLink[0].LinkArea.Start -eq 0 -and $aboutWebsiteLink[0].LinkArea.Length -eq $aboutWebsiteLink[0].Text.Length -and
    $aboutGitHubLink.Count -eq 1 -and $aboutGitHubLink[0].LinkArea.Start -eq 0 -and $aboutGitHubLink[0].LinkArea.Length -eq $aboutGitHubLink[0].Text.Length
$programIdentity = $form.Text -eq '對外網路連線能力監控程式' -and $assembly.GetName().Version.ToString() -eq '0.9.7.0'
$applicationRecoveryType = $assembly.GetType('NetCheck.ApplicationRecovery', $true)
$applicationRestartRegistered = $null -ne $applicationRecoveryType.GetMethod('Register', [Reflection.BindingFlags]'Static,Public')
$embeddedIcon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $testRoot 'NetCheckMonitor.exe'))
$iconStream = New-Object IO.MemoryStream
$defaultIconStream = New-Object IO.MemoryStream
$embeddedIcon.ToBitmap().Save($iconStream, [Drawing.Imaging.ImageFormat]::Png)
[Drawing.SystemIcons]::Application.ToBitmap().Save($defaultIconStream, [Drawing.Imaging.ImageFormat]::Png)
$customIconEmbedded = [Convert]::ToBase64String($iconStream.ToArray()) -ne [Convert]::ToBase64String($defaultIconStream.ToArray()) -and $null -ne $form.Icon
$iconStream.Dispose(); $defaultIconStream.Dispose(); $embeddedIcon.Dispose()
$testUrls = $builtInTargets
for ($i = 0; $i -lt $testUrls.Length; $i++) { $testUrls[$i] = 'http://127.0.0.1:9/' }

$historyDir = Join-Path $testRoot 'NetCheck_Data'
New-Item -ItemType Directory -Force -Path $historyDir | Out-Null
$historyStart = (Get-Date).Date.AddDays(-2).AddHours(10)
$historyCsv = Join-Path $historyDir 'NetCheck_HISTORY-PC-A1B2C3D4_20200101_100000.csv'
$historyLines = @(
    'Timestamp,Type,Status,LatencyMs,Target,Detail',
    ('"' + $historyStart.ToString('o') + '",MARKER,COMPUTER,,,"HISTORY-PC [A1B2C3D4]"'),
    ('"' + $historyStart.ToString('o') + '",MARKER,STARTED,,,"Historical session"'),
    ('"' + $historyStart.AddMinutes(1).ToString('o') + '",CHECK,ONLINE,12,"https://example.com/","Historical online check"'),
    ('"' + $historyStart.AddMinutes(2).ToString('o') + '",CHECK,ONLINE,14,"https://example.com/","Historical online check"'),
    ('"' + $historyStart.AddMinutes(3).ToString('o') + '",MARKER,STOPPED,,,"Historical session stopped"')
)
[IO.File]::WriteAllLines($historyCsv, $historyLines, (New-Object Text.UTF8Encoding($true)))
$emptyStart = (Get-Date).Date.AddDays(-30)
$emptyCsv = Join-Path $historyDir 'NetCheck_NO-CHECK-B1C2D3E4_20200101_100000.csv'
$emptyLines = @(
    'Timestamp,Type,Status,LatencyMs,Target,Detail',
    ('"' + $emptyStart.ToString('o') + '",MARKER,COMPUTER,,,"NO-CHECK [B1C2D3E4]"'),
    ('"' + $emptyStart.ToString('o') + '",MARKER,STARTED,,,"No checks"'),
    ('"' + $emptyStart.AddDays(20).ToString('o') + '",MARKER,STOPPED,,,"No checks"')
)
[IO.File]::WriteAllLines($emptyCsv, $emptyLines, (New-Object Text.UTF8Encoding($true)))

$type.GetMethod('StartMonitoring', $flags).Invoke($form, @())
$type.GetMethod('AddEventNote', $flags).Invoke($form, @('測試事件：重開數據機'))
$activeStateCreated = Test-Path -LiteralPath $env:NETCHECK_SESSION_STATE
$activeTargets = $type.GetField('activeTestUrls', $flags).GetValue($form)
$customTargetSequence = $activeTargets.Length -eq 3 -and $activeTargets[0] -eq 'http://127.0.0.1:9/' -and $activeTargets[1] -eq 'http://127.0.0.1:8/' -and $activeTargets[2] -eq 'http://127.0.0.1:7/'
$settingsAvailableDuringMonitoring = $settingsButton.Enabled
$eventNoteAvailableDuringMonitoring = $eventNoteButton.Enabled
Start-Sleep -Seconds 18
$closingArgs = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::UserClosing, $false)
$closingArgs = $closingArgs.PSObject.BaseObject
$closingMethod = $type.GetMethods($flags) | Where-Object { $_.Name -eq 'OnFormClosing' -and $_.DeclaringType -eq $type } | Select-Object -First 1
$closingMethod.Invoke($form, [object[]]@($form, $closingArgs))
$type.GetMethod('TogglePause', $flags).Invoke($form, @())
Start-Sleep -Seconds 1
$type.GetMethod('TogglePause', $flags).Invoke($form, @())
Start-Sleep -Seconds 18
$type.GetMethod('CreateLiveReport', $flags).Invoke($form, @($false))
$liveHtml = Get-ChildItem -LiteralPath (Join-Path $testRoot 'NetCheck_Data') -Filter '*_Live.html' | Select-Object -First 1
$exitSaveCompleted = $type.GetMethod('SaveAndFinalizeForExit', $flags).Invoke($form, @())
$activeStateCleared = -not (Test-Path -LiteralPath $env:NETCHECK_SESSION_STATE)
$settingsReenabledAfterMonitoring = $settingsButton.Enabled
$idleClosingArgs = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::UserClosing, $false)
$idleClosingArgs = $idleClosingArgs.PSObject.BaseObject
$closingMethod.Invoke($form, [object[]]@($form, $idleClosingArgs))
$idleCloseWasBlocked = $idleClosingArgs.Cancel -and $trayIcon.Visible
$type.GetMethod('ShowFromTray', $flags).Invoke($form, @())

$dataDir = Join-Path $testRoot 'NetCheck_Data'
$csv = Get-ChildItem -LiteralPath $dataDir -Filter '*.csv' | Select-Object -First 1
$html = Get-ChildItem -LiteralPath $dataDir -Filter '*.html' | Select-Object -First 1
$rows = Import-Csv -LiteralPath $csv.FullName
$checkTimes = @($rows | Where-Object Type -eq 'CHECK' | ForEach-Object { [DateTime]$_.Timestamp } | Sort-Object)
$fastRetrySpacing = $false
for ($i = 1; $i -lt $checkTimes.Count; $i++) { $seconds = ($checkTimes[$i] - $checkTimes[$i - 1]).TotalSeconds; if ($seconds -ge 3 -and $seconds -le 8) { $fastRetrySpacing = $true } }
$sourceText = Get-Content -LiteralPath (Join-Path $root 'NetCheck.cs') -Raw
$closeReminderText = $sourceText.Contains('按下右上角 X 只會將程式縮小到系統匣') -and $sourceText.Contains('主畫面右下角的「關閉程式」按鈕') -and $sourceText.Contains('This message is shown only once.')
$boundedOutageBackoff = $sourceText -match 'consecutiveFailures\s*<=\s*FastRetryLimit' -and
    $sourceText -match 'Math\.Min\(checkIntervalSeconds,\s*OutageBackoffSeconds\)'
$powerProtectionIntegration = $sourceText.Contains('SetThreadExecutionState(preventSleep ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS)') -and $sourceText.Contains('message.Msg == WM_QUERYENDSESSION && ShouldBlockWindowsShutdown()') -and $sourceText.Contains('ShutdownBlockReasonCreate')
$tls12UpdateCheck = $sourceText.Contains('ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072') -and $null -ne $aboutFormType.GetMethod('ReadLatestRelease', [Reflection.BindingFlags]'Static,NonPublic')
$htmlText = Get-Content -LiteralPath $html.FullName -Raw
$reportHasChineseProductName = $htmlText.Contains('對外網路連線能力累積監控報表')
$cumulativeIncludesHistory = $htmlText.Contains('HISTORY-PC') -and $htmlText.Contains('來源檔案：2')
$effectiveMatch = [regex]::Match($htmlText, '<span>有效監控</span><b[^>]*>([^<]+)</b>')
$cumulativeExcludesUnrecordedTime = $effectiveMatch.Success -and -not $effectiveMatch.Groups[1].Value.Contains('天') -and -not $htmlText.Contains('NO-CHECK')
$reportHasEnhancedSummary = $htmlText.Contains('最長斷線') -and $htmlText.Contains('平均斷線') -and $htmlText.Contains('最短斷線') -and $htmlText.Contains('第 95 百分位延遲') -and $htmlText.Contains('平均延遲變動')
$reportHasNetworkInfo = $htmlText.Contains('目前網卡') -and $htmlText.Contains('連線類型') -and $htmlText.Contains('Wi-Fi 訊號')
$reportHasAdvancedDiagnostics = $htmlText.Contains('進階分層連線診斷') -and $htmlText.Contains('診斷標示') -and $htmlText.Contains('分層證據') -and $htmlText.Contains('Findings=')
$reportHasEventNotes = $htmlText.Contains('斷線事件與事件註記') -and $htmlText.Contains('內容 / 檢查') -and $htmlText.Contains('測試事件：重開數據機') -and $htmlText.Contains("fill='#8e44ad'")
$backupCsv = Get-ChildItem -LiteralPath $env:NETCHECK_BACKUP_DIR -Filter '*.csv' | Select-Object -First 1
$dailyPattern = [regex]::Escape((Get-Date).ToString('yyyy/MM/dd')) + "</td><td>[^<]*</td><td class='bad'>[^<]*</td><td class='bad'>([0-9.]+)%"
$dailyMatch = [regex]::Match($htmlText, $dailyPattern)
$dailyPercent = if ($dailyMatch.Success) { [double]::Parse($dailyMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { 0 }
$archiveType = $assembly.GetType('NetCheck.ArchiveReport', $true)
$loadedArchiveSessions = $archiveType.GetMethod('LoadSessions', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())
$archiveHtmlText = $archiveType.GetMethod('BuildHtml', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($loadedArchiveSessions, [DateTime]::Today, [DateTime]::Today.AddDays(1), $true))
$archiveReportHasNetworkInfo = $archiveHtmlText.Contains('測試電腦與網路介面') -and $archiveHtmlText.Contains('目前網卡') -and $archiveHtmlText.Contains('Wi-Fi 訊號') -and ([String]::IsNullOrEmpty($networkSnapshot.AdapterName) -or $archiveHtmlText.Contains($networkSnapshot.AdapterName))
$archiveReportHasAdvancedDiagnostics = $archiveHtmlText.Contains('進階分層連線診斷') -and $archiveHtmlText.Contains('診斷標示') -and $archiveHtmlText.Contains('分層證據')
$archiveReportHasEventNotes = $archiveHtmlText.Contains('事件註記') -and $archiveHtmlText.Contains('測試事件：重開數據機')
$archiveReportHasDailyDetails = $archiveHtmlText.Contains('每日完整測試記錄') -and $archiveHtmlText.Contains('完整測試內容') -and $archiveHtmlText.Contains('測試目標')
$exportMethod = $archiveType.GetMethod('ExportPdf', [Reflection.BindingFlags]'Static,Public')
[string]$allPdf = Join-Path $testRoot 'AllData.pdf'
[string]$datePdf = Join-Path $testRoot 'SelectedDate.pdf'
$allArgs = [object[]]::new(4); $allArgs[0] = $allPdf; $allArgs[1] = [bool]$true; $allArgs[2] = [DateTime]::Today; $allArgs[3] = [DateTime]::Today
$dateArgs = [object[]]::new(4); $dateArgs[0] = $datePdf; $dateArgs[1] = [bool]$false; $dateArgs[2] = [DateTime]::Today; $dateArgs[3] = [DateTime]::Today
$exportMethod.Invoke($null, $allArgs)
$exportMethod.Invoke($null, $dateArgs)
$dailyArtifactMethod = $archiveType.GetMethod('ExportDailyArtifacts', [Reflection.BindingFlags]'Static,Public')
[string]$cloudArtifactDir = Join-Path $testRoot 'CloudArtifacts'
$artifactArgs = [object[]]::new(4); $artifactArgs[0] = $cloudArtifactDir; $artifactArgs[1] = [Environment]::MachineName; $artifactArgs[2] = 'A1B2C3D4'; $artifactArgs[3] = [DateTime]::Today
$dailyArtifacts = $dailyArtifactMethod.Invoke($null, $artifactArgs)
$cloudPdf = @($dailyArtifacts | Where-Object { $_ -like '*.pdf' } | Select-Object -First 1)
$cloudCsv = @($dailyArtifacts | Where-Object { $_ -like '*.csv' } | Select-Object -First 1)
$cloudManagerType = $assembly.GetType('NetCheck.CloudBackupManager', $true)
$storageMethod = $cloudManagerType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,Public')
[string]$cloudStorageTest = Join-Path $testRoot 'Cloud\storage-selftest.dat'
$cloudStorageProtected = $storageMethod.Invoke($null, [object[]]@($cloudStorageTest))
$embeddedCredentialsMethod = $cloudManagerType.GetMethod('EmbeddedCredentials', [Reflection.BindingFlags]'Static,NonPublic')
$embeddedCredentials = $embeddedCredentialsMethod.Invoke($null, @())
$oauthBuiltInClient = $embeddedCredentials.ClientId -match '^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$' -and -not [string]::IsNullOrWhiteSpace($embeddedCredentials.ClientSecret) -and $embeddedCredentials.TokenUri -eq 'https://oauth2.googleapis.com/token'
$oauthRequestForms = $cloudManagerType.GetMethod('RunOAuthRequestSelfTest', [Reflection.BindingFlags]'Static,Public').Invoke($null, @())
$cloudFormType = $assembly.GetType('NetCheck.CloudBackupForm', $true)
$cloudManager = [Activator]::CreateInstance($cloudManagerType, [object[]]@([Environment]::MachineName, 'A1B2C3D4'))
$cloudForm = [Activator]::CreateInstance($cloudFormType, [object[]]@($cloudManager))
$cloudConnectButton = $cloudFormType.GetField('connect', $flags).GetValue($cloudForm)
$oauthLoginOnlyUi = $cloudConnectButton.Text -eq '登入 Google Drive' -and $null -eq $cloudFormType.GetMethod('Open', [Reflection.BindingFlags]'Static,NonPublic')
$languageType = $assembly.GetType('NetCheck.L', $true)
$languageMethod = $languageType.GetMethod('IsTraditionalChineseLanguage', [Reflection.BindingFlags]'Static,NonPublic')
$languageRouting = $languageMethod.Invoke($null, @('zh-TW')) -and $languageMethod.Invoke($null, @('zh-HK')) -and $languageMethod.Invoke($null, @('zh-MO')) -and $languageMethod.Invoke($null, @('zh-Hant')) -and $languageMethod.Invoke($null, @('zh-Hant-TW')) -and -not $languageMethod.Invoke($null, @('zh-CN')) -and -not $languageMethod.Invoke($null, @('en-US')) -and -not $languageMethod.Invoke($null, @('ja-JP'))
$languageStoreType = $assembly.GetType('NetCheck.LanguagePreferenceStore', $true)
$languageStorage = $languageStoreType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($env:NETCHECK_UI_LANGUAGE_FILE))
$languageSelectionType = $assembly.GetType('NetCheck.LanguageSelectionForm', $true)
$languageSelectionForm = [Activator]::CreateInstance($languageSelectionType, $true)
$languageSelectionText = @($languageSelectionForm.Controls | ForEach-Object { $_.Text }) -join "`n"
$firstRunLanguageSelection = $languageSelectionForm.Text.Contains('Choose Language') -and $languageSelectionText.Contains('繁體中文') -and $languageSelectionText.Contains('English') -and $languageSelectionText.Contains('之後可以在設定頁面變更')
$languageProbe = Join-Path $root 'tests\LanguageProbe.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $languageProbe -Executable (Join-Path $testRoot 'NetCheckMonitor.exe') -Language en-US
$englishUi = $LASTEXITCODE -eq 0
$allPdfHeader = if (Test-Path $allPdf) { [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($allPdf), 0, 5) } else { '' }
$datePdfHeader = if (Test-Path $datePdf) { [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($datePdf), 0, 5) } else { '' }
$recoverySourceForm = [Activator]::CreateInstance($type, $true)
$type.GetMethod('StartMonitoring', $flags).Invoke($recoverySourceForm, @())
Start-Sleep -Seconds 1
$type.GetMethod('PrepareForSystemRestart', $flags).Invoke($recoverySourceForm, @())
$stateLoadMethod = $sessionStateType.GetMethod('Load', [Reflection.BindingFlags]'Static,Public')
$resumeState = $stateLoadMethod.Invoke($null, @())
$recoveryTargetForm = [Activator]::CreateInstance($type, $true)
$type.GetMethod('ResumeMonitoring', $flags).Invoke($recoveryTargetForm, [object[]]@($resumeState))
Start-Sleep -Seconds 1
$recoveryCsvPath = $type.GetField('csvPath', $flags).GetValue($recoveryTargetForm)
$recoveryRunning = $type.GetField('running', $flags).GetValue($recoveryTargetForm)
$recoverySaved = $type.GetMethod('SaveAndFinalizeForExit', $flags).Invoke($recoveryTargetForm, @())
$recoveryRows = Import-Csv -LiteralPath $recoveryCsvPath
$sessionResumeIntegration = $recoveryRunning -and $recoverySaved -and [bool]($recoveryRows | Where-Object Status -eq 'INTERRUPTED') -and [bool]($recoveryRows | Where-Object Status -eq 'SESSION_RESUMED') -and -not (Test-Path -LiteralPath $env:NETCHECK_SESSION_STATE)
$recoveryTargetForm.Dispose()
$recoverySourceForm.Dispose()
$settingsRestartForm = [Activator]::CreateInstance($type, $true)
$type.GetMethod('StartMonitoring', $flags).Invoke($settingsRestartForm, @())
$oldSettingsCsv = $type.GetField('csvPath', $flags).GetValue($settingsRestartForm)
$updatedSettingsType = $assembly.GetType('NetCheck.MonitorTargetSettings', $true)
$updatedSettings = [Activator]::CreateInstance($updatedSettingsType, $true)
$updatedSettings.UseCustomTargets = $false
$updatedSettings.CustomTargets = New-Object 'System.Collections.Generic.List[string]'
$updatedSettings.AutoStartWindows = $true
$updatedSettings.AutoStartMonitoring = $true
$settingsRestarted = $type.GetMethod('ApplyMonitorSettings', $flags).Invoke($settingsRestartForm, [object[]]@($updatedSettings))
$newSettingsCsv = $type.GetField('csvPath', $flags).GetValue($settingsRestartForm)
$restartSettingsButton = $type.GetField('settingsButton', $flags).GetValue($settingsRestartForm)
$settingsRestartIntegration = $settingsRestarted -and $type.GetField('running', $flags).GetValue($settingsRestartForm) -and $restartSettingsButton.Enabled -and $oldSettingsCsv -ne $newSettingsCsv -and (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($oldSettingsCsv, '.html')))
$settingsRestartSaved = $type.GetMethod('SaveAndFinalizeForExit', $flags).Invoke($settingsRestartForm, @())
$settingsRestartForm.Dispose()
$autoStartForm = [Activator]::CreateInstance($type, $true)
$type.GetMethod('HandleStartupMonitoring', $flags).Invoke($autoStartForm, @())
Start-Sleep -Seconds 1
$autoStartMonitoring = $type.GetField('running', $flags).GetValue($autoStartForm) -and (Test-Path -LiteralPath $env:NETCHECK_SESSION_STATE)
$autoStartSaved = $type.GetMethod('SaveAndFinalizeForExit', $flags).Invoke($autoStartForm, @())
$autoStartForm.Dispose()
$recoveryBeforeAutoStart = $sourceText.Contains('if (TryOfferSessionResume() || running) return;')
$duplicateLaunchShowsExisting = $sourceText.Contains('message.Msg == SingleInstance.ShowWindowMessage') -and $sourceText.Contains('SingleInstance.ShowExistingWindow();')
$clearMethod = $archiveType.GetMethod('ClearAllData', [Reflection.BindingFlags]'Static,Public')
$clearArgs = [object[]]@(0)
$clearFailures = $clearMethod.Invoke($null, $clearArgs)
$managedFilesLeft = @(Get-ChildItem -LiteralPath (Join-Path $testRoot 'NetCheck_Data') -File -ErrorAction SilentlyContinue) + @(Get-ChildItem -LiteralPath $env:NETCHECK_BACKUP_DIR -File -ErrorAction SilentlyContinue)

$result = [PSCustomObject]@{
    CsvCreated       = [bool]$csv
    HtmlCreated      = [bool]$html
    LiveHtmlCreated  = [bool]$liveHtml
    BackupCsvCreated = [bool]$backupCsv
    CloseWasBlocked  = $closingArgs.Cancel
    IdleCloseWasBlocked = $idleCloseWasBlocked
    CloseReminderText = $closeReminderText
    CheckRows        = @($rows | Where-Object Type -eq 'CHECK').Count
    PauseMarker      = [bool]($rows | Where-Object Status -eq 'PAUSED')
    ResumeMarker     = [bool]($rows | Where-Object Status -eq 'RESUMED')
    ReportHasTimeline = $htmlText.Contains('24 小時時間軸')
    ReportHasChineseProductName = $reportHasChineseProductName
    ReportHasOutages = $htmlText.Contains('斷線事件')
    ReportMarksPause = $htmlText.Contains('#9aa0a6')
    ReportHasDailyStats = $htmlText.Contains('每日斷線統計') -and $htmlText.Contains('斷線百分比')
    CumulativeIncludesHistory = $cumulativeIncludesHistory
    CumulativeExcludesUnrecordedTime = $cumulativeExcludesUnrecordedTime
    ReportHasEnhancedSummary = $reportHasEnhancedSummary
    ReportHasNetworkInfo = $reportHasNetworkInfo
    ReportHasAdvancedDiagnostics = $reportHasAdvancedDiagnostics
    ReportHasEventNotes = $reportHasEventNotes
    ArchiveReportHasNetworkInfo = $archiveReportHasNetworkInfo
    ArchiveReportHasAdvancedDiagnostics = $archiveReportHasAdvancedDiagnostics
    ArchiveReportHasEventNotes = $archiveReportHasEventNotes
    ArchiveReportHasDailyDetails = $archiveReportHasDailyDetails
    ReportHasComputer = $htmlText.Contains([Environment]::MachineName)
    ComputerMarker = [bool]($rows | Where-Object Status -eq 'COMPUTER')
    TargetMarker = [bool]($rows | Where-Object Status -eq 'TARGETS')
    NetworkMarker = [bool]($rows | Where-Object Status -eq 'NETWORK')
    PowerProtectionMarker = [bool]($rows | Where-Object Status -eq 'POWER_PROTECTION')
    EventNoteMarker = [bool]($rows | Where-Object { $_.Status -eq 'EVENT_NOTE' -and $_.Detail -eq '測試事件：重開數據機' })
    SuspectedCheck = [bool]($rows | Where-Object Status -eq 'SUSPECTED')
    ConfirmedOfflineCheck = [bool]($rows | Where-Object Status -eq 'OFFLINE')
    OutageConfirmedMarker = [bool]($rows | Where-Object Status -eq 'OUTAGE_CONFIRMED')
    FastRetrySpacing = $fastRetrySpacing
    BoundedOutageBackoff = $boundedOutageBackoff
    UniqueFileName = $csv.BaseName -match '^NetCheck_.+-[0-9A-F]{8}_\d{8}_\d{6}(?:_\d{2})?$'
    DailyOutageCalculated = $dailyPercent -gt 0
    AllPdfCreated = $allPdfHeader -eq '%PDF-'
    DatePdfCreated = $datePdfHeader -eq '%PDF-'
    ClearAllPassed = ([int]$clearArgs[0] -gt 0) -and ($clearFailures.Count -eq 0) -and ($managedFilesLeft.Count -eq 0)
    DownloadButtonLabel = $downloadButtonLabel
    ClearButtonLayout = $clearButtonLayout
    ExitButtonLayout = $exitButtonLayout
    ExitSaveCompleted = [bool]$exitSaveCompleted
    ClearRemovedFromPdfDialog = $clearRemovedFromPdfDialog
    CloudButtonLayout = $cloudButtonLayout
    AboutButtonLayout = $aboutButtonLayout
    SettingsButtonLayout = $settingsButtonLayout
    EventNoteButtonLayout = $eventNoteButtonLayout
    HomeVersionLabel = $homeVersionLabel
    NetworkStatusCapture = $networkStatusCapture
    NetworkInfoLabel = $networkInfoLabel.Text.Contains('目前網卡：') -and $networkInfoLabel.Text.Contains('連線類型：') -and $networkInfoLabel.Text.Contains('Wi-Fi 訊號：')
    OnlineTrayStatus = $onlineTrayStatus
    OfflineTrayStatus = $offlineTrayStatus
    MonitorSettingsStorage = [bool]$monitorSettingsStorage
    PortableSettingsMigration = [bool]$portableMigration
    AdvancedDiagnosticsClassification = [bool]$advancedDiagnosticsClassification
    AdvancedToggleNoRestart = [bool]$advancedToggleNoRestart
    PowerProtectionIntegration = [bool]$powerProtectionIntegration
    ShutdownBlockDecision = [bool]$shutdownBlockDecision
    CloseReminderStoredOnce = [bool]$uiPreferenceStorage
    SessionStateStorage = [bool]$sessionStateStorage
    ActiveStateCreated = [bool]$activeStateCreated
    ActiveStateCleared = [bool]$activeStateCleared
    ApplicationRestartRegistered = $applicationRestartRegistered
    SingleInstanceGuard = $singleInstanceGuard
    DuplicateLaunchShowsExisting = $duplicateLaunchShowsExisting
    SessionResumeIntegration = $sessionResumeIntegration
    AutoStartMonitoring = $autoStartMonitoring -and $autoStartSaved
    RecoveryBeforeAutoStart = $recoveryBeforeAutoStart
    SettingsPageContent = $settingsPageContent
    SettingsLanguageSelection = $settingsLanguageSelection
    SettingsHidesBuiltInTargets = $settingsHidesBuiltInTargets
    SettingsCompactLayout = $settingsCompactLayout
    CustomTargetSequence = $customTargetSequence
    SettingsAvailableDuringMonitoring = $settingsAvailableDuringMonitoring
    EventNoteAvailableDuringMonitoring = $eventNoteAvailableDuringMonitoring
    EventNoteDialogContent = $eventNoteDialogContent
    SettingsRestartIntegration = $settingsRestartIntegration -and $settingsRestartSaved
    SettingsReenabledAfterMonitoring = $settingsReenabledAfterMonitoring
    AboutPageContent = $aboutPageContent
    AboutUrlLinkScope = $aboutUrlLinkScope
    UpdateVersionComparison = $versionComparison
    Tls12UpdateCheck = $tls12UpdateCheck
    ProgramIdentity = $programIdentity
    CustomIconEmbedded = $customIconEmbedded
    CloudDailyPdf = ($cloudPdf.Count -eq 1) -and (Test-Path $cloudPdf[0]) -and ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($cloudPdf[0]), 0, 5) -eq '%PDF-')
    CloudDailyCsv = ($cloudCsv.Count -eq 1) -and (Test-Path $cloudCsv[0]) -and ((Get-Content -LiteralPath $cloudCsv[0] -TotalCount 1) -eq 'Timestamp,Type,Status,LatencyMs,Target,Detail') -and ((Split-Path $cloudCsv[0] -Leaf) -match '^NetCheck_.+-A1B2C3D4_\d{8}_Raw\.csv$')
    CloudStorageProtected = [bool]$cloudStorageProtected
    OAuthBuiltInClient = $oauthBuiltInClient
    OAuthRequestForms = [bool]$oauthRequestForms
    OAuthLoginOnlyUi = $oauthLoginOnlyUi
    LanguageRouting = [bool]$languageRouting
    LanguageStorage = [bool]$languageStorage
    FirstRunLanguageSelection = [bool]$firstRunLanguageSelection
    EnglishUi = [bool]$englishUi
}

$cloudForm.Dispose()
$languageSelectionForm.Dispose()
$cloudManager.Dispose()
$reportForm.Dispose()
$settingsForm.Dispose()
$eventNoteForm.Dispose()
$aboutForm.Dispose()
$form.Dispose()
$result | Format-List

if (-not $result.CsvCreated -or -not $result.HtmlCreated -or -not $result.LiveHtmlCreated -or -not $result.BackupCsvCreated -or
    -not $result.CloseWasBlocked -or -not $result.IdleCloseWasBlocked -or -not $result.CloseReminderText -or $result.CheckRows -lt 1 -or
    -not $result.PauseMarker -or -not $result.ResumeMarker -or
    -not $result.ReportHasTimeline -or -not $result.ReportHasChineseProductName -or -not $result.ReportHasOutages -or -not $result.ReportMarksPause -or -not $result.CumulativeIncludesHistory -or -not $result.CumulativeExcludesUnrecordedTime -or
    -not $result.ReportHasDailyStats -or -not $result.ReportHasEnhancedSummary -or -not $result.ReportHasNetworkInfo -or -not $result.ReportHasAdvancedDiagnostics -or -not $result.ReportHasEventNotes -or -not $result.ArchiveReportHasNetworkInfo -or -not $result.ArchiveReportHasAdvancedDiagnostics -or -not $result.ArchiveReportHasEventNotes -or -not $result.ArchiveReportHasDailyDetails -or -not $result.ReportHasComputer -or -not $result.ComputerMarker -or -not $result.TargetMarker -or -not $result.NetworkMarker -or -not $result.PowerProtectionMarker -or -not $result.EventNoteMarker -or -not $result.SuspectedCheck -or -not $result.ConfirmedOfflineCheck -or -not $result.OutageConfirmedMarker -or -not $result.FastRetrySpacing -or -not $result.BoundedOutageBackoff -or -not $result.UniqueFileName -or
    -not $result.DailyOutageCalculated -or -not $result.AllPdfCreated -or -not $result.DatePdfCreated -or -not $result.ClearAllPassed -or
    -not $result.DownloadButtonLabel -or -not $result.ClearButtonLayout -or -not $result.ExitButtonLayout -or -not $result.ExitSaveCompleted -or -not $result.ClearRemovedFromPdfDialog -or
    -not $result.CloudButtonLayout -or -not $result.AboutButtonLayout -or -not $result.SettingsButtonLayout -or -not $result.EventNoteButtonLayout -or -not $result.HomeVersionLabel -or -not $result.NetworkStatusCapture -or -not $result.NetworkInfoLabel -or -not $result.OnlineTrayStatus -or -not $result.OfflineTrayStatus -or -not $result.MonitorSettingsStorage -or -not $result.PortableSettingsMigration -or -not $result.AdvancedDiagnosticsClassification -or -not $result.AdvancedToggleNoRestart -or -not $result.PowerProtectionIntegration -or -not $result.ShutdownBlockDecision -or -not $result.CloseReminderStoredOnce -or -not $result.SessionStateStorage -or -not $result.ActiveStateCreated -or -not $result.ActiveStateCleared -or -not $result.ApplicationRestartRegistered -or -not $result.SingleInstanceGuard -or -not $result.DuplicateLaunchShowsExisting -or -not $result.SessionResumeIntegration -or -not $result.AutoStartMonitoring -or -not $result.RecoveryBeforeAutoStart -or -not $result.SettingsPageContent -or -not $result.SettingsLanguageSelection -or -not $result.SettingsHidesBuiltInTargets -or -not $result.SettingsCompactLayout -or -not $result.CustomTargetSequence -or -not $result.SettingsAvailableDuringMonitoring -or -not $result.EventNoteAvailableDuringMonitoring -or -not $result.EventNoteDialogContent -or -not $result.SettingsRestartIntegration -or -not $result.SettingsReenabledAfterMonitoring -or -not $result.AboutPageContent -or -not $result.AboutUrlLinkScope -or -not $result.UpdateVersionComparison -or -not $result.Tls12UpdateCheck -or -not $result.ProgramIdentity -or -not $result.CustomIconEmbedded -or -not $result.CloudDailyPdf -or -not $result.CloudDailyCsv -or -not $result.CloudStorageProtected -or
    -not $result.OAuthBuiltInClient -or -not $result.OAuthRequestForms -or -not $result.OAuthLoginOnlyUi -or -not $result.LanguageRouting -or -not $result.LanguageStorage -or -not $result.FirstRunLanguageSelection -or -not $result.EnglishUi) {
    throw 'NetCheck self-test failed.'
}
