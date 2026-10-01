param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$aecVersion = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'VERSION') -Raw).Trim()
if ($aecVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$') {
    throw "VERSION must be a version such as 1.2.3 or 1.2.3-beta.1. Found: '$aecVersion'."
}
if ($Matches[4]) {
    foreach ($aecIdentifier in $Matches[4].Split('.')) {
        if ($aecIdentifier -match '^0[0-9]+$') { throw 'Numeric prerelease identifiers must not contain leading zeroes.' }
    }
}
$aecVersionPattern = [regex]::Escape($aecVersion)
$aecChangelog = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'CHANGELOG.md') -Raw
$aecSections = [regex]::Matches($aecChangelog, "(?ms)^## \[$aecVersionPattern\](?: - [^\r\n]+)?\r?\n(?<notes>.*?)(?=^## |\z)")
if ($aecSections.Count -ne 1 -or [string]::IsNullOrWhiteSpace($aecSections[0].Groups['notes'].Value)) {
    throw "CHANGELOG.md must have exactly one nonempty '## [$aecVersion]' section."
}
[pscustomobject]@{
    Version = $aecVersion
    Tag = "v$aecVersion"
    Prerelease = $aecVersion.Contains('-')
    Notes = $aecSections[0].Value.Trim()
    ArchiveName = "AutoEmuControllerConfig-v$aecVersion-win-x64.zip"
    ChangelogName = "CHANGELOG-v$aecVersion.md"
}
