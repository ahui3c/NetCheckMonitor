param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Language = 'en-US'
)

$ErrorActionPreference = 'Stop'
$env:NETCHECK_UI_LANGUAGE = $Language
$env:NETCHECK_CLOUD_SETTINGS = Join-Path ([IO.Path]::GetTempPath()) ('NetCheck-LanguageProbe-' + [guid]::NewGuid().ToString('N') + '.dat')
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

try {
    $mainText = @($main.Controls | ForEach-Object { $_.Text }) -join "`n"
    $aboutText = @($about.Controls | ForEach-Object { $_.Text }) -join "`n"
    $reportText = @($report.Controls | ForEach-Object { $_.Text }) -join "`n"
    $cloudText = @($cloud.Controls | ForEach-Object { $_.Text }) -join "`n"
    $archiveType = $assembly.GetType('NetCheck.ArchiveReport', $true)
    $sessionType = $assembly.GetType('NetCheck.ArchiveReport+Session', $true)
    $session = [Activator]::CreateInstance($sessionType, $true)
    $allInstance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $sessionType.GetField('Start', $allInstance).SetValue($session, [DateTime]::Now.AddMinutes(-1))
    $sessionType.GetField('End', $allInstance).SetValue($session, [DateTime]::Now)
    $sessionType.GetField('Stopped', $allInstance).SetValue($session, $true)
    $sessionType.GetField('MachineName', $allInstance).SetValue($session, 'TEST-PC')
    $sessionType.GetField('MachineId', $allInstance).SetValue($session, 'A1B2C3D4')
    $listType = [Collections.Generic.List``1].MakeGenericType($sessionType)
    $sessions = [Activator]::CreateInstance($listType)
    $sessions.Add($session)
    $buildHtml = $archiveType.GetMethod('BuildHtml', [Reflection.BindingFlags]'Static,NonPublic')
    $reportHtml = $buildHtml.Invoke($null, [object[]]@($sessions, [DateTime]::Today, [DateTime]::Today.AddDays(1), $true))
    $englishReport = $reportHtml.Contains("<html lang='en'>") -and $reportHtml.Contains('Daily Outage Statistics') -and $reportHtml.Contains('Outage Events') -and -not $reportHtml.Contains('每日斷線統計')
    $ok = $main.Text -eq 'NetCheckMonitor Network Monitor' -and
        $mainText.Contains('Start') -and $mainText.Contains('Download PDF Report') -and $mainText.Contains('About') -and
        $about.Text -eq 'About NetCheckMonitor' -and $aboutText.Contains('Version 0.9.1') -and $aboutText.Contains('Scheduled monitoring') -and $aboutText.Contains('廖阿輝') -and
        $report.Text -eq 'Download NetCheckMonitor PDF Report' -and $reportText.Contains('All Saved Data') -and
        $cloud.Text -eq 'Google Drive Daily Backup' -and $cloudText.Contains('Sign in to Google Drive') -and $englishReport
    if (-not $ok) { throw 'English UI probe failed.' }
    Write-Output 'English UI probe passed.'
}
finally {
    $cloud.Dispose()
    $manager.Dispose()
    $report.Dispose()
    $about.Dispose()
    $main.Dispose()
    Remove-Item -LiteralPath $env:NETCHECK_CLOUD_SETTINGS -Force -ErrorAction SilentlyContinue
}
