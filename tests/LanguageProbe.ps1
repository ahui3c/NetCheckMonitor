param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Language = 'en-US'
)

$ErrorActionPreference = 'Stop'
$env:NETCHECK_UI_LANGUAGE = $Language
$env:NETCHECK_CLOUD_SETTINGS = Join-Path ([IO.Path]::GetTempPath()) ('NetCheck-LanguageProbe-' + [guid]::NewGuid().ToString('N') + '.dat')
$env:NETCHECK_MONITOR_SETTINGS = Join-Path ([IO.Path]::GetTempPath()) ('NetCheck-MonitorLanguageProbe-' + [guid]::NewGuid().ToString('N') + '.json')
$assembly = [Reflection.Assembly]::LoadFrom($Executable)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'

$mainType = $assembly.GetType('NetCheck.MainForm', $true)
$main = [Activator]::CreateInstance($mainType, $true)
$aboutType = $assembly.GetType('NetCheck.AboutForm', $true)
$about = [Activator]::CreateInstance($aboutType)
$reportType = $assembly.GetType('NetCheck.DataReportForm', $true)
$report = [Activator]::CreateInstance($reportType, [object[]]@('TEST-PC', 'A1B2C3D4'))
$managerType = $assembly.GetType('NetCheck.CloudBackupManager', $true)
$manager = [Activator]::CreateInstance($managerType, [object[]]@('TEST-PC', 'A1B2C3D4'))
$cloudType = $assembly.GetType('NetCheck.CloudBackupForm', $true)
$cloud = [Activator]::CreateInstance($cloudType, [object[]]@($manager))
$settingsType = $assembly.GetType('NetCheck.MonitorSettingsForm', $true)
$settingsValue = $mainType.GetField('monitorSettings', $flags).GetValue($main)
$settings = [Activator]::CreateInstance($settingsType, [object[]]@($settingsValue))
$eventNoteType = $assembly.GetType('NetCheck.EventNoteForm', $true)
$eventNote = [Activator]::CreateInstance($eventNoteType)

try {
    $mainText = @($main.Controls | ForEach-Object { $_.Text }) -join "`n"
    $aboutText = @($about.Controls | ForEach-Object { $_.Text }) -join "`n"
    $reportText = @($report.Controls | ForEach-Object { $_.Text }) -join "`n"
    $cloudText = @($cloud.Controls | ForEach-Object { $_.Text }) -join "`n"
    $settingsText = @($settings.Controls | ForEach-Object { $_.Text }) -join "`n"
    $eventNoteText = @($eventNote.Controls | ForEach-Object { $_.Text }) -join "`n"
    $archiveType = $assembly.GetType('NetCheck.ArchiveReport', $true)
    $sessionType = $assembly.GetType('NetCheck.ArchiveReport+Session', $true)
    $session = [Activator]::CreateInstance($sessionType, $true)
    $allInstance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $sessionType.GetField('Start', $allInstance).SetValue($session, [DateTime]::Now.AddMinutes(-1))
    $sessionType.GetField('End', $allInstance).SetValue($session, [DateTime]::Now)
    $sessionType.GetField('Stopped', $allInstance).SetValue($session, $true)
    $sessionType.GetField('MachineName', $allInstance).SetValue($session, 'TEST-PC')
    $sessionType.GetField('MachineId', $allInstance).SetValue($session, 'A1B2C3D4')
    $recordType = $assembly.GetType('NetCheck.ArchiveReport+Record', $true)
    $record = [Activator]::CreateInstance($recordType, $true)
    $recordType.GetField('Time', $allInstance).SetValue($record, [DateTime]::Now.AddSeconds(-30))
    $recordType.GetField('Online', $allInstance).SetValue($record, $true)
    $recordType.GetField('Status', $allInstance).SetValue($record, 'ONLINE')
    $recordType.GetField('Latency', $allInstance).SetValue($record, [long]10)
    $sessionType.GetField('Records', $allInstance).GetValue($session).Add($record)
    $listType = [Collections.Generic.List``1].MakeGenericType($sessionType)
    $sessions = [Activator]::CreateInstance($listType)
    $sessions.Add($session)
    $buildHtml = $archiveType.GetMethod('BuildHtml', [Reflection.BindingFlags]'Static,NonPublic')
    $reportHtml = $buildHtml.Invoke($null, [object[]]@($sessions, [DateTime]::Today, [DateTime]::Today.AddDays(1), $true))
    $englishReport = $reportHtml.Contains("<html lang='en'>") -and $reportHtml.Contains('Daily Outage Statistics') -and $reportHtml.Contains('Outage Events') -and $reportHtml.Contains('Current Network Adapter') -and $reportHtml.Contains('Wi-Fi Signal') -and -not $reportHtml.Contains('每日斷線統計')
    $ok = $main.Text -eq 'NetCheckMonitor Network Monitor' -and
        $mainText.Contains('Start') -and $mainText.Contains('Download PDF Report') -and $mainText.Contains('About') -and $mainText.Contains('Settings') -and $mainText.Contains('v0.9.6') -and
        $about.Text -eq 'About NetCheckMonitor' -and $aboutText.Contains('Version 0.9.6') -and $aboutText.Contains('Scheduled monitoring') -and $aboutText.Contains('廖阿輝') -and $aboutText.Contains('Website:') -and $aboutText.Contains('https://ahui3c.com') -and $aboutText.Contains('GitHub project:') -and $aboutText.Contains('https://github.com/ahui3c/NetCheckMonitor') -and $aboutText.Contains('Check for Updates') -and
        $report.Text -eq 'Download NetCheckMonitor PDF Report' -and $reportText.Contains('All Saved Data') -and
        $cloud.Text -eq 'Google Drive Daily Backup' -and $cloudText.Contains('Sign in to Google Drive') -and
        $settings.Text -eq 'Monitoring Target Settings' -and $settingsText.Contains('Use built-in test targets (recommended)') -and $settingsText.Contains('The app automatically uses its default connectivity targets.') -and $settingsText.Contains('Use custom test targets') -and $settingsText.Contains('Target 3') -and $settingsText.Contains('Run advanced layered diagnostics after an HTTPS failure (optional)') -and $settingsText.Contains('Prevent the computer from sleeping while monitoring (recommended)') -and $settingsText.Contains('Block Windows shutdown or restart while monitoring (stop monitoring first)') -and $settingsText.Contains('Start the app after Windows sign-in') -and $settingsText.Contains('Start monitoring automatically when the app opens') -and $settingsText.Contains('Interface language') -and $settingsText.Contains('Applied the next time the app starts') -and
        $eventNote.Text -eq 'Add Event Note' -and $eventNoteText.Contains('Restarted modem') -and $eventNoteText.Contains('Restarted wireless router') -and $eventNoteText.Contains('Restarted computer') -and $eventNoteText.Contains('Rain') -and $eventNoteText.Contains('Thunder') -and $englishReport
    if (-not $ok) { throw 'English UI probe failed.' }
    Write-Output 'English UI probe passed.'
}
finally {
    $cloud.Dispose()
    $settings.Dispose()
    $eventNote.Dispose()
    $manager.Dispose()
    $report.Dispose()
    $about.Dispose()
    $main.Dispose()
    Remove-Item -LiteralPath $env:NETCHECK_CLOUD_SETTINGS -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $env:NETCHECK_MONITOR_SETTINGS -Force -ErrorAction SilentlyContinue
}
