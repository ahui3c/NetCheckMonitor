param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$startupType = $assembly.GetType('NetCheck.ApplicationStartup', $true)
$settingsType = $assembly.GetType('NetCheck.MonitorTargetSettings', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$configure = $startupType.GetMethod('Configure', $flags)
$shouldResume = $startupType.GetMethod('ShouldResumeWithoutPrompt', $flags)
$settings = [Activator]::CreateInstance($settingsType, $true)

$settings.AutoStartWindows = $true
$settings.AutoStartMonitoring = $true
$configure.Invoke($null, [object[]]@(,[string[]]@('--windows-autostart')))
$bothEnabled = [bool]$shouldResume.Invoke($null, [object[]]@($settings))

$settings.AutoStartMonitoring = $false
$needsMonitoringSetting = -not [bool]$shouldResume.Invoke($null, [object[]]@($settings))
$settings.AutoStartMonitoring = $true
$settings.AutoStartWindows = $false
$needsWindowsSetting = -not [bool]$shouldResume.Invoke($null, [object[]]@($settings))

$settings.AutoStartWindows = $true
$configure.Invoke($null, [object[]]@(,[string[]]@('--resume')))
$manualRecoveryStillPrompts = -not [bool]$shouldResume.Invoke($null, [object[]]@($settings))
$configure.Invoke($null, [object[]]@(,[string[]]@()))
$manualLaunchStillPrompts = -not [bool]$shouldResume.Invoke($null, [object[]]@($settings))

$sessionSource = Get-Content -LiteralPath (Join-Path $root 'SessionRecovery.cs') -Raw
$mainSource = Get-Content -LiteralPath (Join-Path $root 'NetCheck.cs') -Raw
$dedicatedStartupArgument = $sessionSource.Contains('" --windows-autostart"') -and
    $mainSource.Contains('ApplicationStartup.Configure(args);') -and
    $mainSource.Contains('TryOfferSessionResume(resumeWithoutPrompt)')

$result = [PSCustomObject]@{
    BothSettingsAndWindowsStartupResumeSilently = $bothEnabled
    MonitoringSettingRequired = $needsMonitoringSetting
    WindowsStartupSettingRequired = $needsWindowsSetting
    ApplicationRecoveryDoesNotSuppressPrompt = $manualRecoveryStillPrompts
    ManualLaunchDoesNotSuppressPrompt = $manualLaunchStillPrompts
    DedicatedWindowsStartupArgument = $dedicatedStartupArgument
}
$result | Format-List

if ($result.PSObject.Properties.Value -contains $false) {
    throw 'Windows startup resume probe failed.'
}

Write-Output 'Windows sign-in startup resumes unfinished monitoring without confirmation only when both startup settings are enabled.'
