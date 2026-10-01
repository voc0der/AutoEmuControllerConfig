$ErrorActionPreference = 'Stop'
$aecScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/Get-ReleaseInfo.ps1'
$aecFixture = Join-Path ([IO.Path]::GetTempPath()) ('aec-release-info-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $aecFixture
try {
    Set-Content (Join-Path $aecFixture 'VERSION') '1.2.3-beta.1'
    Set-Content (Join-Path $aecFixture 'CHANGELOG.md') "# Changelog`n`n## [Unreleased]`n`nFuture work.`n`n## [1.2.3-beta.1] - 2026-10-01`n`nRelease content.`n`n### Fixed`n`nA fix.`n`n## [1.2.2]`n`nOlder release."
    $aecInfo = & $aecScript -RepositoryRoot $aecFixture
    if ($aecInfo.Tag -ne 'v1.2.3-beta.1' -or -not $aecInfo.Prerelease -or
        $aecInfo.Notes -notmatch 'A fix\.' -or $aecInfo.Notes -match 'Future work|Older release') {
        throw 'Release notes did not select exactly the requested version.'
    }
    Write-Host 'PASS Release metadata selects the matching changelog section and prerelease version'
    foreach ($aecVersion in @('01.2.3', '1.2', '1.2.3-beta.01', '1.2.3/invalid', '1.2.3')) {
        Set-Content (Join-Path $aecFixture 'VERSION') $aecVersion
        $aecFailed = $false
        try { $null = & $aecScript -RepositoryRoot $aecFixture } catch { $aecFailed = $true }
        if (-not $aecFailed) { throw "Invalid or undocumented release was accepted: $aecVersion" }
    }
    Write-Host 'PASS Release metadata rejects invalid versions and missing release notes'
    foreach ($aecNotes in @("## [1.2.3]`n`n", "## [1.2.3]`nFirst`n## [1.2.3]`nSecond")) {
        Set-Content (Join-Path $aecFixture 'CHANGELOG.md') $aecNotes
        $aecFailed = $false
        try { $null = & $aecScript -RepositoryRoot $aecFixture } catch { $aecFailed = $true }
        if (-not $aecFailed) { throw 'Empty or duplicate release notes were accepted.' }
    }
    Write-Host 'PASS Release metadata rejects empty and duplicate changelog sections'
} finally {
    Remove-Item -LiteralPath $aecFixture -Recurse -Force
}
