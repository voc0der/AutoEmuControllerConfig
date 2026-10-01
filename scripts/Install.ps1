$ErrorActionPreference = 'Stop'
$aecDestination = Join-Path $env:LOCALAPPDATA 'AutoEmuControllerConfig'
$aecRequired = @('AutoEmuControllerConfig.exe', 'SDL2.dll', 'SDL3.dll', 'Prepare-Playnite.ps1')
foreach ($aecName in $aecRequired) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $aecName))) {
        throw "Incomplete release package: missing $aecName."
    }
}
$null = New-Item -ItemType Directory -Path $aecDestination -Force
foreach ($aecFile in Get-ChildItem -LiteralPath $PSScriptRoot -File) {
    if ($aecFile.Name -eq 'Install.ps1') { continue }
    Copy-Item -LiteralPath $aecFile.FullName -Destination $aecDestination -Force
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        Unblock-File -LiteralPath (Join-Path $aecDestination $aecFile.Name)
    }
}
Write-Host "Installed to $aecDestination"
Write-Host 'In Playnite: Settings > Scripts > Before starting a game, add:'
Write-Host '& "$env:LOCALAPPDATA\AutoEmuControllerConfig\Prepare-Playnite.ps1"'
