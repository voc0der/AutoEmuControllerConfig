$ErrorActionPreference = 'Stop'
$aecRoot = Split-Path $PSScriptRoot -Parent
$aecRelease = & (Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1')
$aecOutput = Join-Path $aecRoot 'artifacts\AutoEmuControllerConfig-win-x64'
$aecDownloads = Join-Path $aecRoot 'artifacts\downloads'
$aecReleaseOutput = Join-Path $aecRoot 'artifacts\release'
$null = New-Item -ItemType Directory -Path $aecDownloads -Force

dotnet run --project (Join-Path $aecRoot 'tests\AutoEmuControllerConfig.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
& (Join-Path $aecRoot 'tests\Test-PlayniteHook.ps1')
& (Join-Path $aecRoot 'tests\Test-ReleaseInfo.ps1')
foreach ($aecDirectory in @($aecOutput, $aecReleaseOutput)) {
    if (Test-Path -LiteralPath $aecDirectory) { Remove-Item -LiteralPath $aecDirectory -Recurse -Force }
    $null = New-Item -ItemType Directory -Path $aecDirectory
}
dotnet publish (Join-Path $aecRoot 'src\AutoEmuControllerConfig') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $aecOutput
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$aecDependencies = Get-Content (Join-Path $PSScriptRoot 'native-dependencies.json') -Raw | ConvertFrom-Json
foreach ($aecDependency in $aecDependencies) {
    $aecZip = Join-Path $aecDownloads ([IO.Path]::GetFileName($aecDependency.url))
    if (-not (Test-Path $aecZip)) { Invoke-WebRequest -UseBasicParsing -Uri $aecDependency.url -OutFile $aecZip }
    if ((Get-FileHash $aecZip -Algorithm SHA256).Hash -ne $aecDependency.sha256) { throw "Checksum mismatch: $aecZip" }
    $aecExtract = Join-Path $aecDownloads ([IO.Path]::GetFileNameWithoutExtension($aecZip))
    Expand-Archive -LiteralPath $aecZip -DestinationPath $aecExtract -Force
    Get-ChildItem $aecExtract -Filter '*.dll' | Copy-Item -Destination $aecOutput -Force
    $aecSdlName = if ($aecDependency.url.Contains('SDL3-')) { 'SDL3' } else { 'SDL2' }
    foreach ($aecNotice in @('LICENSE.txt', 'README-SDL.txt')) {
        $aecNoticePath = Join-Path $aecExtract $aecNotice
        if (Test-Path $aecNoticePath) {
            Copy-Item $aecNoticePath (Join-Path $aecOutput "$aecSdlName-$aecNotice") -Force
        }
    }
}
$aecPacks = dotnet msbuild (Join-Path $aecRoot 'src\AutoEmuControllerConfig') -t:ResolveFrameworkReferences -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -getItem:ResolvedRuntimePack | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not locate .NET runtime notices.' }
$aecRuntime = $aecPacks.Items.ResolvedRuntimePack | Where-Object { $_.FrameworkName -eq 'Microsoft.NETCore.App' } | Select-Object -First 1
if ($null -eq $aecRuntime) { throw 'The .NET runtime package could not be identified.' }
foreach ($aecNotice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item (Join-Path $aecRuntime.PackageDirectory $aecNotice) (Join-Path $aecOutput "DOTNET-$aecNotice") -Force
}
Copy-Item (Join-Path $aecRoot 'playnite\Prepare-Playnite.ps1') $aecOutput -Force
Copy-Item (Join-Path $PSScriptRoot 'Install.ps1') $aecOutput -Force
Copy-Item (Join-Path $aecRoot 'README.md') $aecOutput -Force
Copy-Item (Join-Path $aecRoot 'THIRD-PARTY-NOTICES.txt') $aecOutput -Force
Copy-Item (Join-Path $aecRoot 'VERSION') $aecOutput -Force
Copy-Item (Join-Path $aecRoot 'CHANGELOG.md') $aecOutput -Force
$aecArchive = Join-Path $aecReleaseOutput $aecRelease.ArchiveName
Compress-Archive -Path (Join-Path $aecOutput '*') -DestinationPath $aecArchive -Force
Copy-Item (Join-Path $aecRoot 'CHANGELOG.md') (Join-Path $aecReleaseOutput $aecRelease.ChangelogName)
Set-Content -LiteralPath (Join-Path $aecReleaseOutput 'release-notes.md') -Value $aecRelease.Notes -Encoding utf8
$aecChecksums = foreach ($aecName in @($aecRelease.ArchiveName, $aecRelease.ChangelogName)) {
    $aecHash = (Get-FileHash (Join-Path $aecReleaseOutput $aecName) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$aecHash  $aecName"
}
Set-Content -LiteralPath (Join-Path $aecReleaseOutput 'SHA256SUMS.txt') -Value $aecChecksums -Encoding utf8
& (Join-Path $aecRoot 'tests\Test-ReleasePackage.ps1') -Archive $aecArchive
Write-Host "Release: $aecArchive"
