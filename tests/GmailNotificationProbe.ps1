param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheckGmailProbe_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$env:NETCHECK_GMAIL_SETTINGS = Join-Path $testRoot 'gmail-settings.dat'

try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable))
    $managerType = $assembly.GetType('NetCheck.GmailNotificationManager', $true)
    $publicStatic = [Reflection.BindingFlags]'Static,Public'
    [string]$storagePath = Join-Path $testRoot 'storage-selftest.dat'
    $storage = $managerType.GetMethod('RunStorageSelfTest', $publicStatic).Invoke($null, [object[]]@($storagePath))
    $mime = $managerType.GetMethod('RunMimeSelfTest', $publicStatic).Invoke($null, @())
    $largePayload = $managerType.GetMethod('RunLargePayloadSelfTest', $publicStatic).Invoke($null, @())
    $oauth = $managerType.GetMethod('RunOAuthRequestSelfTest', $publicStatic).Invoke($null, @())

    $manager = [Activator]::CreateInstance($managerType, [object[]]@('GMAIL-PROBE', 'A1B2C3D4'))
    $formType = $assembly.GetType('NetCheck.GmailNotificationForm', $true)
    $form = [Activator]::CreateInstance($formType, [object[]]@($manager))
    $texts = @($form.Controls | ForEach-Object { $_.Text }) -join "`n"
    $fixedRecipientUi = $form.Text -eq 'Gmail 報表與恢復通知' -and
        $texts.Contains('寄件者與收件者固定為登入的同一個 Google 帳戶') -and
        $texts.Contains('每日寄送 PDF 與 CSV 報表') -and
        $texts.Contains('網路恢復後寄送通知') -and
        $texts.Contains('寄送測試郵件') -and
        @($form.Controls | Where-Object { $_ -is [Windows.Forms.TextBox] }).Count -eq 0

    [PSCustomObject]@{
        ProtectedStorage = [bool]$storage
        SelfRecipientMime = [bool]$mime
        LargeReportPayload = [bool]$largePayload
        OAuthPkceAndScope = [bool]$oauth
        FixedRecipientUi = [bool]$fixedRecipientUi
    } | Format-List

    if (-not $storage -or -not $mime -or -not $largePayload -or -not $oauth -or -not $fixedRecipientUi) {
        throw 'Gmail notification probe failed.'
    }
}
finally {
    if ($null -ne $form) { $form.Dispose() }
    if ($null -ne $manager) { $manager.Dispose() }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
