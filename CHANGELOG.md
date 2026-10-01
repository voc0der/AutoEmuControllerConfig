# Changelog

## [0.1.0] - 2026-10-01

- Prepare Xbox controller assignments for BizHawk, Eden, and Flycast using the supplied working layouts.
- Assign up to four players in Windows/XInput order, with SDL device order as a best-effort fallback when exact matching is unavailable.
- Run once from Playnite's pre-launch script and wait for completion before starting the emulator.
- Preserve unrelated settings, back up changed files, and restore earlier writes if an update fails.
- Include a self-contained Windows x64 executable, SDL libraries, and an installation script.

Physical controller enumeration through Apollo and end-to-end game launches still need validation on a Windows host.
