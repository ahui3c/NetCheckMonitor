param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheck-SpeedTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$oldData = $env:NETCHECK_SPEED_DATA_DIR
$oldRoots = $env:NETCHECK_DATA_ROOTS
$oldSession = $env:NETCHECK_SESSION_STATE
$oldLanguage = $env:NETCHECK_UI_LANGUAGE
$oldMonitorSettings = $env:NETCHECK_MONITOR_SETTINGS
try {
    $env:NETCHECK_SPEED_DATA_DIR = $testRoot
    $env:NETCHECK_DATA_ROOTS = $null
    $env:NETCHECK_SESSION_STATE = Join-Path $testRoot 'active-session.json'
    $env:NETCHECK_MONITOR_SETTINGS = Join-Path $testRoot 'monitor-settings.json'
    $env:NETCHECK_UI_LANGUAGE = 'en'
    $asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $Executable))
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $resultType = $asm.GetType('NetCheck.SpeedTestResult', $true)
    $levelType = $asm.GetType('NetCheck.SpeedTestLevel', $true)
    $storage = $asm.GetType('NetCheck.SpeedTestStorage', $true)
    $report = $asm.GetType('NetCheck.SpeedTrendReport', $true)
    $result = [Activator]::CreateInstance($resultType, $true)
    $instanceFields = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $resultType.GetField('Time', $instanceFields).SetValue($result, [DateTime]'2026-07-20T08:00:00')
    $resultType.GetField('Status', $instanceFields).SetValue($result, 'COMPLETED')
    $resultType.GetField('Scheduled', $instanceFields).SetValue($result, $true)
    $resultType.GetField('Level', $instanceFields).SetValue($result, [Enum]::Parse($levelType, 'Standard'))
    $resultType.GetField('DownloadMbps', $instanceFields).SetValue($result, [double]321.5)
    $resultType.GetField('UploadMbps', $instanceFields).SetValue($result, [double]88.2)
    $resultType.GetField('IdleLatencyMs', $instanceFields).SetValue($result, [double]12.4)
    $resultType.GetField('JitterMs', $instanceFields).SetValue($result, [double]1.8)
    $resultType.GetField('DownloadBytes', $instanceFields).SetValue($result, [long]85000000)
    $resultType.GetField('UploadBytes', $instanceFields).SetValue($result, [long]40000000)
    $storage.GetMethod('Append', $flags).Invoke($null, @('SPEED-PC', 'A1B2C3D4', $result))
    $htmlPath = $report.GetMethod('Create', $flags).Invoke($null, @('SPEED-PC', 'A1B2C3D4'))
    $csv = Get-ChildItem -LiteralPath $testRoot -Filter 'NetCheck_Speed_*.csv'
    $html = [IO.File]::ReadAllText($htmlPath)
    if (@($csv).Count -ne 1 -or $html -notmatch '321.5 Mbps' -or $html -notmatch '88.2 Mbps' -or $html -match 'Estimated Outage') { throw 'Speed storage/report isolation test failed.' }

    $activeData = Join-Path $testRoot 'PreviousActiveData'
    New-Item -ItemType Directory -Force -Path $activeData | Out-Null
    $sessionType = $asm.GetType('NetCheck.ActiveSessionState', $true)
    $session = [Activator]::CreateInstance($sessionType, $true)
    $session.Active = $true
    $session.MachineId = 'A1B2C3D4'
    $session.CsvPath = Join-Path $activeData 'NetCheck_SPEED-PC-A1B2C3D4_20260720_080000.csv'
    $sessionStateStore = $asm.GetType('NetCheck.SessionStateStore', $true)
    $sessionStateStore.GetMethod('Save', [Reflection.BindingFlags]'Static,Public').Invoke($null, @($session))
    $resultType.GetField('Time', $instanceFields).SetValue($result, [DateTime]'2026-07-24T08:00:00')
    $env:NETCHECK_SPEED_DATA_DIR = $activeData
    $storage.GetMethod('Append', $flags).Invoke($null, @('SPEED-PC', 'A1B2C3D4', $result))
    $resultType.GetField('Time', $instanceFields).SetValue($result, [DateTime]'2026-07-29T08:00:00')
    $env:NETCHECK_SPEED_DATA_DIR = $testRoot
    $storage.GetMethod('Append', $flags).Invoke($null, @('SPEED-PC', 'A1B2C3D4', $result))
    $loadedSpeedItems = @($report.GetMethod('Load', $flags).Invoke($null, @('A1B2C3D4')))
    $speedItemType = $asm.GetType('NetCheck.SpeedTrendReport+Item', $true)
    $speedTimeField = $speedItemType.GetField('Time', [Reflection.BindingFlags]'Instance,NonPublic')
    $loadedSpeedDates = @($loadedSpeedItems | ForEach-Object { ([DateTime]$speedTimeField.GetValue($_)).ToString('yyyy-MM-dd') })
    if ($loadedSpeedItems.Count -ne 3 -or -not $loadedSpeedDates.Contains('2026-07-20') -or -not $loadedSpeedDates.Contains('2026-07-24') -or -not $loadedSpeedDates.Contains('2026-07-29')) { throw 'Speed report did not merge the current and active-session data directories.' }

    $dailyOutput = Join-Path $testRoot 'DailyDelivery'
    $dailySpeedArgs = [object[]]@([string]$dailyOutput, [string]'SPEED-PC', [string]'A1B2C3D4', [DateTime]'2026-07-24')
    $dailySpeedArtifacts = @($report.GetMethod('ExportDailyArtifacts', $flags).Invoke($null, $dailySpeedArgs))
    if ($dailySpeedArtifacts.Count -ne 2 -or [IO.Path]::GetExtension($dailySpeedArtifacts[0]) -ne '.html' -or [IO.Path]::GetExtension($dailySpeedArtifacts[1]) -ne '.csv') { throw 'Daily scheduled speed-test artifacts were not created.' }
    $dailySpeedHtml = [IO.File]::ReadAllText($dailySpeedArtifacts[0])
    $dailySpeedCsv = [IO.File]::ReadAllText($dailySpeedArtifacts[1])
    if ($dailySpeedHtml -notmatch '2026/07/24' -or $dailySpeedHtml -match '2026/07/20' -or $dailySpeedCsv -notmatch '2026-07-24' -or $dailySpeedCsv -match '2026-07-20') { throw 'Daily scheduled speed-test artifacts include the wrong date range.' }

    $monitorStore = $asm.GetType('NetCheck.MonitorSettingsStore', $true)
    $monitorSettings = $monitorStore.GetMethod('Load', [Reflection.BindingFlags]'Static,Public').Invoke($null, @())
    $monitorSettings.SpeedTest.ScheduledEnabled = $false
    $monitorStore.GetMethod('Save', [Reflection.BindingFlags]'Static,Public').Invoke($null, @($monitorSettings))
    $archiveReport = $asm.GetType('NetCheck.ArchiveReport', $true)
    $publicStatic = [Reflection.BindingFlags]'Static,Public'
    $disabledDeliveryArgs = [object[]]@([string](Join-Path $testRoot 'DisabledDelivery'), [string]'SPEED-PC', [string]'A1B2C3D4', [DateTime]'2026-07-24')
    $withoutScheduledSpeed = @($archiveReport.GetMethod('ExportScheduledSpeedArtifactsIfEnabled', $publicStatic).Invoke($null, $disabledDeliveryArgs))
    $monitorSettings.SpeedTest.ScheduledEnabled = $true
    $monitorStore.GetMethod('Save', [Reflection.BindingFlags]'Static,Public').Invoke($null, @($monitorSettings))
    $enabledDeliveryArgs = [object[]]@([string](Join-Path $testRoot 'EnabledDelivery'), [string]'SPEED-PC', [string]'A1B2C3D4', [DateTime]'2026-07-24')
    $withScheduledSpeed = @($archiveReport.GetMethod('ExportScheduledSpeedArtifactsIfEnabled', $publicStatic).Invoke($null, $enabledDeliveryArgs))
    if ($withoutScheduledSpeed.Count -ne 0 -or $withScheduledSpeed.Count -ne 2) { throw 'Daily delivery did not follow the scheduled speed-test setting.' }

    $settingsType = $asm.GetType('NetCheck.SpeedTestOptions', $true)
    $defaults = $settingsType.GetMethod('Defaults', $flags).Invoke($null, @())
    if ($defaults.IntervalHours -ne 24 -or $defaults.ScheduledEnabled -or $defaults.Level -ne 'Standard') { throw 'Speed-test defaults are incorrect.' }
    $now = [DateTime]::UtcNow
    $defaults.LastAttemptUtc = $now
    $mainType = $asm.GetType('NetCheck.MainForm', $true)
    $cooldownMethod = $mainType.GetMethod('GetSpeedTestBlockedUntilUtc', $flags)
    $blockedUntil = [DateTime]$cooldownMethod.Invoke($null, @($defaults))
    if ([Math]::Abs(($blockedUntil - $now.AddMinutes(15)).TotalSeconds) -gt 2) { throw 'The persistent 15-minute speed-test cooldown is incorrect.' }
    $defaults.ServerCooldownUntilUtc = $now.AddHours(1)
    $blockedUntil = [DateTime]$cooldownMethod.Invoke($null, @($defaults))
    if ([Math]::Abs(($blockedUntil - $now.AddHours(1)).TotalSeconds) -gt 2) { throw 'The server cooldown does not override the normal cooldown.' }
    $formType = $asm.GetType('NetCheck.SpeedTestSettingsForm', $true)
    $form = [Activator]::CreateInstance($formType, [Reflection.BindingFlags]'Instance,NonPublic', $null, @($defaults), $null)
    $fields = [Reflection.BindingFlags]'Instance,NonPublic'
    $levelBox = $formType.GetField('levelBox', $fields).GetValue($form)
    $intervalBox = $formType.GetField('intervalBox', $fields).GetValue($form)
    if ($levelBox.Enabled -or $intervalBox.Enabled) { throw 'Scheduled speed controls must be disabled while scheduled testing is off.' }
    $form.Dispose()
    $engine = $asm.GetType('NetCheck.CloudflareSpeedTest', $true)
    $profileArgs = [object[]]@([Enum]::Parse($levelType, 'Standard'), $null, $null)
    $engine.GetMethod('GetProfile', $flags).Invoke($null, $profileArgs)
    $batchFields = [Reflection.BindingFlags]'Instance,NonPublic'
    $standardDown = $profileArgs[1]; $standardUp = $profileArgs[2]
    if ($standardDown.Count -ne 2 -or $standardUp.Count -ne 2 -or $standardDown[1].GetType().GetField('Count', $batchFields).GetValue($standardDown[1]) -ne 8 -or $standardUp[1].GetType().GetField('Count', $batchFields).GetValue($standardUp[1]) -ne 8) { throw 'Standard multi-stream profile is incorrect.' }
    Write-Host 'Speed-test storage, split-directory report merge, daily delivery artifacts, settings, persistent cooldown, 24-hour defaults, and eight-stream profile passed.'
}
finally {
    $env:NETCHECK_SPEED_DATA_DIR = $oldData
    $env:NETCHECK_DATA_ROOTS = $oldRoots
    $env:NETCHECK_SESSION_STATE = $oldSession
    $env:NETCHECK_UI_LANGUAGE = $oldLanguage
    $env:NETCHECK_MONITOR_SETTINGS = $oldMonitorSettings
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
