# Changelog

## 0.04 - 01.09.2026

- Removed the .NET Framework 4.8 Developer Pack requirement from `install.cmd` and `upgrade.cmd`.
- Changed maintenance validation to restore, build, and test only `net10.0-windows10.0.26100.0`.
- Kept removal of the remaining `net48` project targets tracked separately in `TODO.md`.

## 0.03 - 01.09.2026

- Replaced the PowerShell-based maintenance prototype with a CMD-only implementation.
- Reworked `install.cmd` as a self-contained first-run bootstrap that executes from `%TEMP%`.
- Reworked `upgrade.cmd` as a self-updating CMD-only updater that also executes from `%TEMP%`.
- Removed `install.ps1`, `upgrade.ps1`, and `scripts/Environment.ps1`.
- Added exact .NET SDK installation through WinGet using the version declared in `global.json`.
- Added .NET Framework developer-pack verification for the `net48` build target.
- Added Git-normalized `upgrade.cmd` self-update comparison to avoid CRLF false positives.
- Preserved recursive submodule synchronization, locked restore, Release x64 build verification, and unit tests.

## 0.02 - 01.09.2026

- Standardized all project-generated logs under the repository-root `logs/` directory.
- Moved installer diagnostics to `logs/install.log`.
- Moved upgrade diagnostics to `logs/upgrade.log`.
- Added `logs/*` to `.gitignore` while retaining `logs/.gitkeep`.
- Documented the shared log-location rule for future application logging.

## 0.01 - 01.09.2026

- Created the `devel` development branch while keeping `main` stable.
- Added the first deterministic install/upgrade bootstrap prototype.
- Added recursive Git submodule initialization and synchronization.
- Added locked NuGet restore, Release x64 build verification, and unit-test verification.
- Added shared maintenance documentation in `UPGRADE.md`.
