# Called synchronously from Playnite's global "Before starting a game" script.
# Use Playnite's selected action so ordinary PC games and unsupported emulators skip.
if ($null -eq $SourceAction -or $SourceAction.Type.ToString() -ne 'Emulator') {
    return
}

$aecEmulator = $PlayniteApi.Database.Emulators.Get($SourceAction.EmulatorId)
if ($null -eq $aecEmulator) {
    throw 'AutoEmuControllerConfig: Playnite could not resolve the selected emulator.'
}
$aecDirectory = $PlayniteApi.ExpandGameVariables($Game, [string]$aecEmulator.InstallDir)
$aecDirectory = [Environment]::ExpandEnvironmentVariables($aecDirectory)
if ([string]::IsNullOrWhiteSpace($aecDirectory) -or $aecDirectory.Contains('"')) {
    throw 'AutoEmuControllerConfig: the emulator installation directory is invalid.'
}
$aecDirectory = [IO.Path]::GetFullPath($aecDirectory)
if ($aecDirectory.Length -gt [IO.Path]::GetPathRoot($aecDirectory).Length) {
    $aecDirectory = $aecDirectory.TrimEnd([char[]]'\/')
}
# Windows argument quoting doubles trailing backslashes before a closing quote.
$aecQuotedDirectory = $aecDirectory -replace '(\\+)$', '$1$1'
$aecExecutable = Join-Path $PSScriptRoot 'AutoEmuControllerConfig.exe'
if (-not (Test-Path -LiteralPath $aecExecutable -PathType Leaf)) {
    throw "AutoEmuControllerConfig is missing: $aecExecutable"
}

$aecStart = New-Object System.Diagnostics.ProcessStartInfo
$aecStart.FileName = $aecExecutable
$aecStart.Arguments = '--emulator-dir "' + $aecQuotedDirectory + '"'
$aecStart.UseShellExecute = $false
$aecStart.CreateNoWindow = $true
$aecStart.RedirectStandardOutput = $true
$aecStart.RedirectStandardError = $true
$aecProcess = New-Object System.Diagnostics.Process
$aecProcess.StartInfo = $aecStart
try {
    if (-not $aecProcess.Start()) { throw 'AutoEmuControllerConfig could not start.' }
    # Drain both pipes asynchronously to avoid blocking on a full output buffer.
    $aecOutput = $aecProcess.StandardOutput.ReadToEndAsync()
    $aecErrors = $aecProcess.StandardError.ReadToEndAsync()
    if (-not $aecProcess.WaitForExit(30000)) {
        $aecProcess.Kill()
        $aecProcess.WaitForExit()
        throw 'AutoEmuControllerConfig timed out. The game was not launched.'
    }
    $aecProcess.WaitForExit()
    $aecErrorText = $aecErrors.GetAwaiter().GetResult()
    $null = $aecOutput.GetAwaiter().GetResult()
    if ($aecProcess.ExitCode -ne 0) {
        # Playnite cancels startup when its pre-launch script throws.
        throw "AutoEmuControllerConfig: $aecErrorText"
    }
} finally {
    $aecProcess.Dispose()
}
