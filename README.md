# AutoEmuControllerConfig

Automatically assign connected Xbox controllers to P1–P4 in **BizHawk, Eden, and Flycast** before Playnite launches a game. The executable prepares the selected emulator and exits. No background process or controller configuration screen.

The supplied working controller layouts are embedded in the executable. Windows/XInput order determines player order where SDL exposes it; otherwise the app uses SDL's device order and records that fallback in its log. Empty Windows slots are skipped, so a controller in Windows slot 3 can still become P1.

## Install in Playnite

Requires Windows 10/11 x64, existing working emulator configurations, and write access to those configuration files and directories. The release includes .NET and SDL.

1. Extract the Windows ZIP from the latest GitHub release (`AutoEmuControllerConfig-v<version>-win-x64.zip`) and, from that extracted folder, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1`. It copies the application into `%LOCALAPPDATA%\AutoEmuControllerConfig`.
2. In Playnite **Settings → Scripts → Before starting a game**, add:

   ```powershell
   & "$env:LOCALAPPDATA\AutoEmuControllerConfig\Prepare-Playnite.ps1"
   ```

That is the only Playnite integration step. Emulator paths come from Playnite's selected emulator automatically. Ordinary PC games and unsupported emulators skip preparation. Custom Playnite actions that directly launch an executable should use an emulator action to receive this integration.

Playnite waits for preparation to finish. If controllers are still connecting, the app waits briefly for a stable list, up to five seconds. Missing controllers, a running emulator, inaccessible files, or preparation errors stop the launch instead of starting with incomplete assignments. The script shows the error through Playnite's normal script-error handling.

## What changes

| Emulator | Assignment behavior |
| --- | --- |
| BizHawk | Reassigns Xbox button, analog, autofire, and rumble bindings using the supplied layouts. Additional players inherit the supplied P1 controller layout. Updates the existing N64/Ares64 player connection fields. Detects modern SDL input versus older XInput input. |
| Eden | Applies the supplied P1/P2 layouts, with P1's layout for P3/P4. Uses detected SDL GUIDs and per-GUID ports, enables connected players, and disables unused players. Per-game controller-profile selections return to the managed global assignments. |
| Flycast | Assigns SDL devices to console ports A–D and enables occupied controller ports. Preserves existing button mappings, VMUs, and other accessories. |

Graphics, audio, ROM paths, saves, and unrelated settings are preserved. BizHawk console-specific peripherals such as multitaps retain their existing configuration; the available in-game players still depend on the console and game. Controller changes during gameplay are handled by the emulator; this application runs again at the next launch.

The app prefers each emulator's own SDL DLL, with pinned SDL2/SDL3 libraries included for builds that need them. SDL instance IDs are process-local: Flycast preparation expects a fresh enumeration and checks for hotplug gaps. Multiple indistinguishable RawInput controllers use best-effort SDL order. A controller reconnecting between preparation and emulator startup can still change enumeration.

Changed files are backed up before replacement under:

```text
%LOCALAPPDATA%\AutoEmuControllerConfig\backups\
```

Each backup has a companion `.path.txt` identifying its original location. Unchanged files are not rewritten. A failed multi-file update attempts to restore files already replaced. The most recent run is recorded in `last-run.log` beside `backups`.

Files under `Program Files` must be writable by the account running Playnite, just as they must be for the emulator to save settings. The application reports permission failures and does not elevate Playnite.

## Run directly

```powershell
.\AutoEmuControllerConfig.exe
.\AutoEmuControllerConfig.exe --dry-run
.\AutoEmuControllerConfig.exe --emulator-dir 'C:\Program Files\Eden' --dry-run
```

Without arguments, the app checks `BizHawk`, `Eden`, and `Flycast` under Program Files and Program Files (x86). Eden uses `%APPDATA%\eden\config`, or its adjacent `user\config` directory for a portable installation. Playnite supplies its configured installation directory, so its emulator installations can live elsewhere.

Exit codes: **0** prepared or unsupported emulator skipped; **1** preparation failed; **2** invalid arguments or unsupported operating system. `--dry-run` detects controllers and computes changes without changing emulator files.

## Build and test

Requires the .NET 10 SDK. The parsing, assignment, discovery, and file-write tests also run on Linux:

```text
dotnet build AutoEmuControllerConfig.slnx -c Release
dotnet run --project tests/AutoEmuControllerConfig.Tests -c Release
```

Build the Windows release with PowerShell:

```powershell
.\scripts\Publish.ps1
```

This runs the tests, publishes a self-contained Windows x64 executable, verifies the pinned SDL download checksums, checks installation from the ZIP, and creates `artifacts/release/AutoEmuControllerConfig-v<version>-win-x64.zip` with SHA-256 checksums and changelog notes. GitHub Actions runs the same packaging script on Windows, including a startup check of the packaged executable.

Tests cover sparse controller slots, SDL ordering, one-to-four-player transitions, the supplied layouts, N64 connection settings, repeated runs, unrelated settings, backups, concurrent edits, and rollback. Physical Windows/Apollo controller enumeration and end-to-end game launches still need validation on the host.

## Releases

`VERSION` is the single source for the application and release version. To publish a release:

1. Bump `VERSION` (for example, `0.1.1`) and add a matching `## [0.1.1]` section to `CHANGELOG.md`.
2. Push to `main` or `master`. The Release workflow tests and packages the app, creates `v0.1.1`, and publishes the ZIP, full changelog, and `SHA256SUMS.txt`.

Versions such as `0.2.0-beta.1` become GitHub prereleases. Rerunning a released version keeps its tag and assets intact. An existing unreleased tag must point at the current commit. The workflow can also be started manually on `main` or `master`; no additional release token or secret is required. Repository rules must allow the workflow's `GITHUB_TOKEN` to create tags and releases.

Pull requests and other branches run the Build workflow and upload a test package without publishing a release. Main-branch pushes run these same checks in the Release workflow.

## Source material

The original supplied configs and notes are preserved byte-for-byte in `archive/originals/`, with SHA-256 checksums. The archive is ignored by Git. Only controller-specific data extracted from those files is included in `src/AutoEmuControllerConfig/Baselines/`; account settings and ROM histories are not included.

Emulator format references and implementation boundaries are documented in [docs/emulator-formats.md](docs/emulator-formats.md).
