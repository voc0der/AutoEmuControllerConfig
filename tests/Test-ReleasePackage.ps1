param([Parameter(Mandatory)][string]$Archive)

$ErrorActionPreference = 'Stop'
$aecTemp = Join-Path ([IO.Path]::GetTempPath()) ('aec-package-' + [Guid]::NewGuid().ToString('N'))
$aecOriginalLocalAppData = $env:LOCALAPPDATA
try {
    $aecExtract = Join-Path $aecTemp 'extracted'
    Expand-Archive -LiteralPath $Archive -DestinationPath $aecExtract
    $aecRequired = @('AutoEmuControllerConfig.exe', 'SDL2.dll', 'SDL3.dll', 'Prepare-Playnite.ps1',
        'Install.ps1', 'VERSION', 'CHANGELOG.md', 'README.md', 'THIRD-PARTY-NOTICES.txt',
        'SDL2-README-SDL.txt', 'SDL3-LICENSE.txt', 'DOTNET-LICENSE.TXT', 'DOTNET-THIRD-PARTY-NOTICES.TXT')
    foreach ($aecName in $aecRequired) {
        if (-not (Test-Path -LiteralPath (Join-Path $aecExtract $aecName) -PathType Leaf)) {
            throw "Release package is missing $aecName."
        }
    }
    $env:LOCALAPPDATA = Join-Path $aecTemp 'Local App Data'
    & (Join-Path $aecExtract 'Install.ps1')
    $aecInstalled = Join-Path $env:LOCALAPPDATA 'AutoEmuControllerConfig'
    foreach ($aecFile in Get-ChildItem -LiteralPath $aecExtract -File) {
        if ($aecFile.Name -eq 'Install.ps1') { continue }
        $aecExpected = (Get-FileHash -LiteralPath $aecFile.FullName).Hash
        $aecActual = (Get-FileHash -LiteralPath (Join-Path $aecInstalled $aecFile.Name)).Hash
        if ($aecActual -ne $aecExpected) { throw "Installation changed or omitted $($aecFile.Name)." }
    }
    Write-Host 'PASS Release ZIP includes its dependencies and installs without changing file contents'
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        & (Join-Path $aecInstalled 'AutoEmuControllerConfig.exe') --help
        if ($LASTEXITCODE -ne 0) { throw 'The packaged Windows executable could not start.' }
        Write-Host 'PASS Packaged Windows executable starts successfully'
    }
} finally {
    $env:LOCALAPPDATA = $aecOriginalLocalAppData
    if (Test-Path -LiteralPath $aecTemp) { Remove-Item -LiteralPath $aecTemp -Recurse -Force }
}
