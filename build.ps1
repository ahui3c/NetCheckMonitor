param([string]$OutputName = 'NetCheckMonitor.exe')

$outputName = [System.IO.Path]::GetFileName($OutputName)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root 'NetCheck-Portable'
$iconPath = Join-Path $root 'assets\NetCheckMonitor.ico'
$oauthPath = Join-Path $root 'google-oauth-client.local.json'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (-not (Test-Path -LiteralPath $iconPath)) { throw "Application icon not found: $iconPath" }

$clientSecret = [Environment]::GetEnvironmentVariable('NETCHECK_GOOGLE_CLIENT_SECRET')
if ([String]::IsNullOrWhiteSpace($clientSecret) -and (Test-Path -LiteralPath $oauthPath)) {
    $oauthJson = Get-Content -LiteralPath $oauthPath -Raw | ConvertFrom-Json
    if ($null -ne $oauthJson.installed) { $clientSecret = [string]$oauthJson.installed.client_secret }
    elseif ($null -ne $oauthJson.client_secret) { $clientSecret = [string]$oauthJson.client_secret }
}
$secretSource = Join-Path $root 'tmp\GoogleOAuthBuildSecrets.g.cs'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $secretSource) | Out-Null
$secretBase64 = if ([String]::IsNullOrWhiteSpace($clientSecret)) { '' } else { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($clientSecret)) }
$secretCode = @"
using System;
using System.Text;
namespace NetCheck
{
    internal static class GoogleOAuthBuildSecrets
    {
        private const string EncodedClientSecret = "$secretBase64";
        internal static bool IsConfigured { get { return EncodedClientSecret.Length > 0; } }
        internal static string ClientSecret { get { return IsConfigured ? Encoding.UTF8.GetString(Convert.FromBase64String(EncodedClientSecret)) : null; } }
    }
}
"@
[IO.File]::WriteAllText($secretSource, $secretCode, (New-Object Text.UTF8Encoding($false)))

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Security.dll',
    'System.IO.Compression.dll',
    'System.Web.Extensions.dll'
)

$sources = @(
    $secretSource,
    (Join-Path $root 'PortableSettings.cs'),
    (Join-Path $root 'Localization.cs'),
    (Join-Path $root 'NetworkStatus.cs'),
    (Join-Path $root 'AdvancedDiagnostics.cs'),
    (Join-Path $root 'EventNotes.cs'),
    (Join-Path $root 'SpeedTest.cs'),
    (Join-Path $root 'SpeedReport.cs'),
    (Join-Path $root 'MonitorSettings.cs'),
    (Join-Path $root 'SessionRecovery.cs'),
    (Join-Path $root 'NetCheck.cs'),
    (Join-Path $root 'DataReport.cs'),
    (Join-Path $root 'CloudBackup.cs')
)
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters = New-Object System.CodeDom.Compiler.CompilerParameters
foreach ($reference in $refs) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$parameters.GenerateExecutable = $true
$parameters.GenerateInMemory = $false
$parameters.IncludeDebugInformation = $false
$parameters.OutputAssembly = Join-Path $outDir $outputName
$parameters.CompilerOptions = "/target:winexe /optimize+ /codepage:65001 /win32icon:`"$iconPath`""
try { $results = $provider.CompileAssemblyFromFile($parameters, [string[]]$sources) }
finally { $provider.Dispose(); Remove-Item -LiteralPath $secretSource -Force -ErrorAction SilentlyContinue }
if ($results.Errors.HasErrors) {
    $messages = @($results.Errors | ForEach-Object { "$($_.FileName)($($_.Line),$($_.Column)): $($_.ErrorNumber): $($_.ErrorText)" })
    throw "C# build failed:`n$($messages -join "`n")"
}

Copy-Item -LiteralPath (Join-Path $root '使用說明.txt') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'User_Guide_EN.txt') -Destination $outDir -Force
Write-Host "Build complete: $outDir\$outputName"
if ([String]::IsNullOrWhiteSpace($clientSecret)) { Write-Warning 'Google Drive OAuth is not configured in this build.' }
