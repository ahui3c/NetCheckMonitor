param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$ScreenshotPath
)

$ErrorActionPreference = 'Stop'
$env:NETCHECK_UI_LANGUAGE = 'zh-TW'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$settingsType = $assembly.GetType('NetCheck.MonitorTargetSettings', $true)
$formType = $assembly.GetType('NetCheck.MonitorSettingsForm', $true)
$settings = [Activator]::CreateInstance($settingsType, $true)
$settings.CustomTargets = New-Object 'System.Collections.Generic.List[string]'
$form = [Activator]::CreateInstance($formType, [object[]]@($settings, [Action]{ }, [Action]{ }, [Action]{ }, [Action]{ }, [Action]{ }))

try {
    Add-Type -AssemblyName System.Drawing
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = [Drawing.Point]::new(-32000, -32000)
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $speed = $formType.GetField('speedSettingsButton', $flags).GetValue($form)
    $cloud = $formType.GetField('cloudSettingsButton', $flags).GetValue($form)
    $gmail = $formType.GetField('gmailSettingsButton', $flags).GetValue($form)
    $export = $formType.GetField('exportBackupButton', $flags).GetValue($form)
    $clear = $formType.GetField('clearDataButton', $flags).GetValue($form)
    $rebuild = $formType.GetField('rebuildDailyReportsButton', $flags).GetValue($form)
    $save = $formType.GetField('saveButton', $flags).GetValue($form)
    $allText = @($form.Controls | ForEach-Object { $_.Text }) -join "`n"

    $threeRows = @($speed.Top, $cloud.Top, $export.Top | Select-Object -Unique).Count -eq 3 -and
        $cloud.Top -eq $gmail.Top -and $export.Top -eq $clear.Top -and $export.Top -eq $rebuild.Top
    $smallMaintenanceButtons = $clear.Width -lt $export.Width -and $rebuild.Width -lt $export.Width
    $normalMaintenanceFonts = $clear.Font.Size -eq $form.Font.Size -and $rebuild.Font.Size -eq $form.Font.Size
    $lastRowOrdered = $export.Right -lt $clear.Left -and $clear.Right -lt $rebuild.Left -and $rebuild.Right -le $form.ClientSize.Width
    $compactBounds = $form.ClientSize.Height -eq 700 -and ($save.Bottom + 12) -le $form.ClientSize.Height
    $simplifiedCopy = $allText.Contains('選擇使用程式內建或是最多三組自訂目標，監控中變更設定會即時生效持續監控。') -and
        $allText.Contains('設定的目標將依序測試，任一目標成功即判定連線正常。')

    if (-not [String]::IsNullOrWhiteSpace($ScreenshotPath)) {
        $target = [IO.Path]::GetFullPath($ScreenshotPath)
        $directory = Split-Path -Parent $target
        if (-not [String]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
        $bitmap = New-Object Drawing.Bitmap($form.Width, $form.Height)
        try {
            $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height))
            $bitmap.Save($target, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose(); $form.Hide() }
    }

    $result = [PSCustomObject]@{
        SimplifiedCopy = $simplifiedCopy
        ThreeFunctionRows = $threeRows
        SmallMaintenanceButtons = $smallMaintenanceButtons
        NormalMaintenanceButtonFonts = $normalMaintenanceFonts
        LastRowOrderedWithoutOverlap = $lastRowOrdered
        CompactWindowBounds = $compactBounds
        RenderedClientSize = $form.ClientSize.ToString()
        RenderedSaveBottom = $save.Bottom
        Screenshot = $ScreenshotPath
    }
    $result | Format-List
    if ($result.PSObject.Properties.Where({ $_.Value -is [bool] }).Value -contains $false) { throw 'Settings layout probe failed.' }
}
finally {
    $form.Hide()
    $form.Dispose()
    $env:NETCHECK_UI_LANGUAGE = $null
}

Write-Output 'Compact settings copy and three-row function layout passed.'
