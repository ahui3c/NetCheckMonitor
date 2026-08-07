param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheckDeliveryAudit_' + [Guid]::NewGuid().ToString('N'))
$oldDirectory = $env:NETCHECK_DELIVERY_LOG_DIR
$oldCloudSettings = $env:NETCHECK_CLOUD_SETTINGS
$oldGmailSettings = $env:NETCHECK_GMAIL_SETTINGS
try {
    $env:NETCHECK_DELIVERY_LOG_DIR = $testRoot
    $env:NETCHECK_CLOUD_SETTINGS = Join-Path $testRoot 'cloud-settings.dat'
    $env:NETCHECK_GMAIL_SETTINGS = Join-Path $testRoot 'gmail-settings.dat'
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable))
    $type = $assembly.GetType('NetCheck.DeliveryAuditLog', $true)
    $method = $type.GetMethod('Record', [Reflection.BindingFlags]'Static,NonPublic')
    $method.Invoke($null, [object[]]@('OFFICE/PC', 'A1B2C3D4', 'GMAIL', 'DAILY_REPORT', 'SUCCESS', 'DataDate=2026-08-07;Attachments=4'))
    $method.Invoke($null, [object[]]@('OFFICE/PC', 'A1B2C3D4', 'GOOGLE_DRIVE', 'DAILY_BACKUP', 'FAILED', "Error=temporary failure`r`nsecond line"))
    $method.Invoke($null, [object[]]@('OFFICE/PC', 'A1B2C3D4', 'GMAIL', 'TEST_EMAIL', 'SKIPPED', 'Reason=AlreadyRunning'))

    $cloudType = $assembly.GetType('NetCheck.CloudBackupManager', $true)
    $cloudManager = [Activator]::CreateInstance($cloudType, [object[]]@('OFFICE/PC', 'A1B2C3D4'))
    $cloudType.GetMethod('BeginBackup').Invoke($cloudManager, [object[]]@([DateTime]'2026-08-07', $null))
    $gmailType = $assembly.GetType('NetCheck.GmailNotificationManager', $true)
    $gmailManager = [Activator]::CreateInstance($gmailType, [object[]]@('OFFICE/PC', 'A1B2C3D4'))
    $gmailType.GetMethod('BeginTestEmail').Invoke($gmailManager, [object[]]@($null))

    $files = @(Get-ChildItem -LiteralPath $testRoot -Filter 'NetCheck_Delivery_*.csv')
    if ($files.Count -ne 1) { throw 'Delivery audit file was not created.' }
    $rows = @(Import-Csv -LiteralPath $files[0].FullName)
    if ($rows.Count -ne 5 -or $rows[0].Type -ne 'DELIVERY' -or $rows[0].Status -ne 'SUCCESS' -or $rows[0].Target -ne 'GMAIL' -or $rows[1].Status -ne 'FAILED' -or $rows[1].Target -ne 'GOOGLE_DRIVE' -or $rows[2].Status -ne 'SKIPPED' -or $rows[3].Status -ne 'FAILED' -or $rows[3].Detail -notmatch 'Action=DAILY_BACKUP' -or $rows[4].Status -ne 'FAILED' -or $rows[4].Detail -notmatch 'Action=TEST_EMAIL') { throw 'Delivery audit rows are incorrect.' }
    if ($rows[1].Detail.Contains("`r") -or $rows[1].Detail.Contains("`n") -or -not $rows[1].Detail.Contains('second line')) { throw 'Delivery audit detail was not sanitized.' }
    Write-Host 'Delivery audit success, failure, skipped, CSV format, and single-line sanitization passed.'
}
finally {
    if ($null -ne $gmailManager) { $gmailManager.Dispose() }
    if ($null -ne $cloudManager) { $cloudManager.Dispose() }
    $env:NETCHECK_DELIVERY_LOG_DIR = $oldDirectory
    $env:NETCHECK_CLOUD_SETTINGS = $oldCloudSettings
    $env:NETCHECK_GMAIL_SETTINGS = $oldGmailSettings
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
