$ErrorActionPreference = 'Stop'
$aecRoot = Split-Path $PSScriptRoot -Parent
$aecTemp = Join-Path ([IO.Path]::GetTempPath()) ('aec-hook-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory $aecTemp
try {
    $aecBin = Join-Path $PSScriptRoot 'AutoEmuControllerConfig.Tests/bin/Release/net10.0'
    Get-ChildItem $aecBin -File | Copy-Item -Destination $aecTemp
    $aecChild = Join-Path $aecBin 'AutoEmuControllerConfig.Tests.exe'
    if (-not (Test-Path $aecChild)) { $aecChild = Join-Path $aecBin 'AutoEmuControllerConfig.Tests' }
    Copy-Item $aecChild (Join-Path $aecTemp 'AutoEmuControllerConfig.exe') -Force
    Copy-Item (Join-Path $aecRoot 'playnite/Prepare-Playnite.ps1') $aecTemp
    $aecHook = Join-Path $aecTemp 'Prepare-Playnite.ps1'
    $aecEmuDir = Join-Path $aecTemp 'Emulator with spaces'
    $null = New-Item -ItemType Directory $aecEmuDir
    $aecMarker = Join-Path $aecTemp 'finished.txt'
    $env:AEC_TEST_CHILD = '1'
    $env:AEC_TEST_DIRECTORY = $aecEmuDir
    $env:AEC_TEST_MARKER = $aecMarker
    $env:AEC_TEST_FAIL = '0'

    $aecDb = [pscustomobject]@{InstallDir = $aecEmuDir}
    $aecDb | Add-Member ScriptMethod Get { param($id) return [pscustomobject]@{InstallDir = $this.InstallDir} }
    $PlayniteApi = [pscustomobject]@{Database = [pscustomobject]@{Emulators = $aecDb}}
    $PlayniteApi | Add-Member ScriptMethod ExpandGameVariables { param($game, $value) return $value }
    $Game = [pscustomobject]@{}
    $SourceAction = [pscustomobject]@{Type = 'Emulator'; EmulatorId = [Guid]::Empty}

    & $aecHook
    if (-not (Test-Path $aecMarker)) { throw 'Pre-launch hook returned before the child finished.' }
    Write-Host 'PASS Playnite waits for completion and quotes paths with spaces'
    Remove-Item $aecMarker

    $aecDb.InstallDir = $aecEmuDir + [IO.Path]::DirectorySeparatorChar
    & $aecHook
    if (-not (Test-Path $aecMarker)) { throw 'Trailing directory separator broke the argument.' }
    Remove-Item $aecMarker
    $aecDb.InstallDir = [IO.Path]::GetPathRoot($aecEmuDir)
    $env:AEC_TEST_DIRECTORY = $aecDb.InstallDir
    & $aecHook
    if (-not (Test-Path $aecMarker)) { throw 'Root directory was not passed correctly.' }
    Remove-Item $aecMarker
    $aecDb.InstallDir = $aecEmuDir
    $env:AEC_TEST_DIRECTORY = $aecEmuDir
    Write-Host 'PASS Playnite preserves root paths and handles trailing separators'

    $env:AEC_TEST_FAIL = '1'
    $aecFailed = $false
    try { & $aecHook } catch {
        $aecFailed = $_.Exception.Message.Contains('test preparation failure')
    }
    if (-not $aecFailed) { throw 'Pre-launch hook did not propagate the child failure.' }
    Write-Host 'PASS Playnite launch fails when preparation fails'
    Remove-Item $aecMarker

    $SourceAction.Type = 'File'
    & $aecHook
    if (Test-Path $aecMarker) { throw 'Ordinary game action ran controller preparation.' }
    Write-Host 'PASS Ordinary PC games skip controller preparation'
} finally {
    Remove-Item $aecTemp -Recurse -Force
    foreach ($aecKey in @('AEC_TEST_CHILD','AEC_TEST_DIRECTORY','AEC_TEST_MARKER','AEC_TEST_FAIL')) {
        Remove-Item "Env:$aecKey" -ErrorAction SilentlyContinue
    }
}
