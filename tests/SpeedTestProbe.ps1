param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheck-SpeedTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$oldData = $env:NETCHECK_SPEED_DATA_DIR
$oldRoots = $env:NETCHECK_DATA_ROOTS
$oldSession = $env:NETCHECK_SESSION_STATE
$oldLanguage = $env:NETCHECK_UI_LANGUAGE
try {
    $env:NETCHECK_SPEED_DATA_DIR = $testRoot
    $env:NETCHECK_DATA_ROOTS = $null
    $env:NETCHECK_SESSION_STATE = Join-Path $testRoot 'active-session.json'
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
    Write-Host 'Speed-test storage, split-directory report merge, settings, persistent cooldown, 24-hour defaults, and eight-stream profile passed.'
}
finally {
    $env:NETCHECK_SPEED_DATA_DIR = $oldData
    $env:NETCHECK_DATA_ROOTS = $oldRoots
    $env:NETCHECK_SESSION_STATE = $oldSession
    $env:NETCHECK_UI_LANGUAGE = $oldLanguage
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
