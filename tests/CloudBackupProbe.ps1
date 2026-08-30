param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('NetCheckCloudProbe_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$env:NETCHECK_CLOUD_SETTINGS = Join-Path $testRoot 'cloud-settings.dat'

try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable))
    $managerType = $assembly.GetType('NetCheck.CloudBackupManager', $true)
    $publicStatic = [Reflection.BindingFlags]'Static,Public'
    [string]$storagePath = Join-Path $testRoot 'storage-selftest.dat'
    $protectedStorage = $managerType.GetMethod('RunStorageSelfTest', $publicStatic).Invoke($null, [object[]]@($storagePath))
    $computerFolder = $managerType.GetMethod('RunComputerFolderSelfTest', $publicStatic).Invoke($null, @())
    $artifactContentTypes = $managerType.GetMethod('RunArtifactContentTypeSelfTest', $publicStatic).Invoke($null, @())
    $controlType = $assembly.GetType('NetCheck.ViewerControlProtocol', $true)
    $controlProtocol = $controlType.GetMethod('RunSelfTest', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())

    $manager = [Activator]::CreateInstance($managerType, [object[]]@('OFFICE-PC', 'A1B2C3D4'))
    $formType = $assembly.GetType('NetCheck.CloudBackupForm', $true)
    $form = [Activator]::CreateInstance($formType, [object[]]@($manager))
    $texts = @($form.Controls | ForEach-Object { $_.Text }) -join "`n"
    $computerFolderUi = $texts.Contains('Drive / Net_Check / 電腦名稱')

    [PSCustomObject]@{
        ProtectedStorage = [bool]$protectedStorage
        ComputerFolder = [bool]$computerFolder
        ComputerFolderUi = [bool]$computerFolderUi
        ArtifactContentTypes = [bool]$artifactContentTypes
        ViewerControlProtocol = [bool]$controlProtocol
    } | Format-List

    if (-not $protectedStorage -or -not $computerFolder -or -not $computerFolderUi -or -not $artifactContentTypes -or -not $controlProtocol) {
        throw 'Cloud backup probe failed.'
    }
}
finally {
    if ($null -ne $form) { $form.Dispose() }
    if ($null -ne $manager) { $manager.Dispose() }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
