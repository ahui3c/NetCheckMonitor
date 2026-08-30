param([Parameter(Mandatory = $true)][string]$PortableDirectory)

$ErrorActionPreference = 'Stop'
$portable = (Resolve-Path -LiteralPath $PortableDirectory).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheckUpdater_' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'source'
$target = Join-Path $testRoot 'target'
$health = Join-Path $testRoot 'health.txt'
$log = Join-Path $testRoot 'NetCheck_Update.csv'
New-Item -ItemType Directory -Force -Path $source,$target,(Join-Path $target 'NetCheck_Data') | Out-Null
try {
    foreach ($name in @('NetCheckMonitor.exe','NetCheckUpdater.exe','update-manifest.json','使用說明.txt','User_Guide_EN.txt')) {
        Copy-Item -LiteralPath (Join-Path $portable $name) -Destination (Join-Path $source $name)
    }
    [IO.File]::WriteAllText((Join-Path $target 'NetCheckMonitor.exe'), 'old-main')
    [IO.File]::WriteAllText((Join-Path $target 'NetCheckUpdater.exe'), 'old-updater')
    [IO.File]::WriteAllText((Join-Path $target '使用說明.txt'), 'old-zh')
    [IO.File]::WriteAllText((Join-Path $target 'User_Guide_EN.txt'), 'old-en')
    [IO.File]::WriteAllText((Join-Path $target 'NetCheck_Data\keep.txt'), 'keep-user-data')

    $oldTestMode = $env:NETCHECK_UPDATER_TEST_NO_LAUNCH
    $env:NETCHECK_UPDATER_TEST_NO_LAUNCH = '1'
    try {
        $manifestDigest = (Get-FileHash -LiteralPath (Join-Path $source 'update-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        $arguments = @('--source', $source, '--target', $target, '--main', 'NetCheckMonitor.exe', '--version', '0.9.19', '--wait-pid', '2147483000', '--wait-start', '0', '--health', $health, '--log', $log, '--manifest-digest', $manifestDigest, '--resume', '0')
        $process = Start-Process -FilePath (Join-Path $source 'NetCheckUpdater.exe') -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
        $exitCode = $process.ExitCode
    } finally { $env:NETCHECK_UPDATER_TEST_NO_LAUNCH = $oldTestMode }

    $filesReplaced = $exitCode -eq 0
    foreach ($name in @('NetCheckMonitor.exe','NetCheckUpdater.exe','使用說明.txt','User_Guide_EN.txt')) {
        $filesReplaced = $filesReplaced -and ((Get-FileHash -LiteralPath (Join-Path $source $name)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $target $name)).Hash)
    }
    $manifestInstalled = Test-Path -LiteralPath (Join-Path $target 'update-manifest.json')
    $userDataPreserved = (Get-Content -LiteralPath (Join-Path $target 'NetCheck_Data\keep.txt') -Raw) -eq 'keep-user-data'
    $healthReported = Test-Path -LiteralPath $health
    $successLogged = (Get-Content -LiteralPath $log -Raw) -like '*"COMPLETE","SUCCESS"*'

    $invalidSource = Join-Path $testRoot 'invalid-source'
    $invalidTarget = Join-Path $testRoot 'invalid-target'
    New-Item -ItemType Directory -Force -Path $invalidSource,$invalidTarget | Out-Null
    foreach ($name in @('NetCheckMonitor.exe','NetCheckUpdater.exe','update-manifest.json','使用說明.txt','User_Guide_EN.txt')) {
        Copy-Item -LiteralPath (Join-Path $portable $name) -Destination (Join-Path $invalidSource $name)
    }
    [IO.File]::AppendAllText((Join-Path $invalidSource 'NetCheckMonitor.exe'), 'tampered')
    [IO.File]::WriteAllText((Join-Path $invalidTarget 'NetCheckMonitor.exe'), 'still-old')
    $invalidLog = Join-Path $testRoot 'invalid-update.csv'
    $invalidManifestDigest = (Get-FileHash -LiteralPath (Join-Path $invalidSource 'update-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidArguments = @('--source', $invalidSource, '--target', $invalidTarget, '--main', 'NetCheckMonitor.exe', '--version', '0.9.19', '--wait-pid', '2147483000', '--wait-start', '0', '--health', (Join-Path $testRoot 'invalid-health.txt'), '--log', $invalidLog, '--manifest-digest', $invalidManifestDigest, '--resume', '0')
    $invalidProcess = Start-Process -FilePath (Join-Path $invalidSource 'NetCheckUpdater.exe') -ArgumentList $invalidArguments -PassThru -Wait -WindowStyle Hidden
    $invalidPackageRejected = $invalidProcess.ExitCode -eq 2 -and (Get-Content -LiteralPath (Join-Path $invalidTarget 'NetCheckMonitor.exe') -Raw) -eq 'still-old' -and (Get-Content -LiteralPath $invalidLog -Raw) -like '*verification failed*'

    [PSCustomObject]@{
        FilesReplaced = $filesReplaced
        ManifestInstalled = $manifestInstalled
        UserDataPreserved = $userDataPreserved
        HealthReported = $healthReported
        SuccessLogged = $successLogged
        InvalidPackageRejected = $invalidPackageRejected
    }
    if (-not ($filesReplaced -and $manifestInstalled -and $userDataPreserved -and $healthReported -and $successLogged -and $invalidPackageRejected)) { throw 'Updater replacement integration failed.' }
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
