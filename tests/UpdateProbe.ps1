param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$type = $assembly.GetType('NetCheck.AboutForm', $true)
$method = $type.GetMethod('ReadLatestRelease', [Reflection.BindingFlags]'Static,NonPublic')
if ($null -eq $method) { throw 'ReadLatestRelease was not found.' }
$values = [object[]]@($null, $null)
$method.Invoke($null, $values)
[PSCustomObject]@{
    Tag = $values[0]
    ReleaseUrl = $values[1]
    TlsProtocol = [Net.ServicePointManager]::SecurityProtocol
} | Format-List
