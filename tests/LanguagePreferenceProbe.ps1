param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root ('.language-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$env:NETCHECK_UI_LANGUAGE = $null
$env:NETCHECK_UI_LANGUAGE_FILE = Join-Path $testRoot 'language.dat'
$env:NETCHECK_MONITOR_SETTINGS = Join-Path $testRoot 'monitor.json'

try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Executable))
    $storeType = $assembly.GetType('NetCheck.LanguagePreferenceStore', $true)
    [Threading.Thread]::CurrentThread.CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo('zh-TW')
    $systemLanguageIgnored = $null -eq $storeType.GetMethod('Load', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())
    $env:NETCHECK_UI_LANGUAGE = 'zh-TW'
    $storageOk = $storeType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($env:NETCHECK_UI_LANGUAGE_FILE))

    $selectionType = $assembly.GetType('NetCheck.LanguageSelectionForm', $true)
    $selectionForm = [Activator]::CreateInstance($selectionType, $true)
    $selectionText = @($selectionForm.Controls | ForEach-Object { $_.Text }) -join "`n"
    $selectionOk = $selectionText.Contains('繁體中文') -and $selectionText.Contains('English') -and $selectionText.Contains('之後可以在設定頁面變更')

    $settingsType = $assembly.GetType('NetCheck.MonitorSettingsForm', $true)
    $settingsValueType = $assembly.GetType('NetCheck.MonitorTargetSettings', $true)
    $settingsValue = [Activator]::CreateInstance($settingsValueType)
    $settingsForm = [Activator]::CreateInstance($settingsType, [object[]]@($settingsValue))
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $languageBox = $settingsType.GetField('languageBox', $flags).GetValue($settingsForm)
    $languageBox.SelectedIndex = 1
    $settingsType.GetMethod('ValidateAndClose', $flags).Invoke($settingsForm, @())
    $settingsOk = $settingsForm.SelectedLanguage -eq 'en-US' -and $languageBox.Items.Count -eq 2

    $selectionForm.Dispose()
    $settingsForm.Dispose()
    if (-not ($systemLanguageIgnored -and $storageOk -and $selectionOk -and $settingsOk)) { throw 'Manual language feature probe failed.' }
    Write-Output 'First-run choice, language storage, and Settings language selector passed.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolvedRoot) -ne [IO.Path]::GetFullPath($root)) { throw 'Unsafe language test path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
