# Changelog

## 0.02 - 01.09.2026

- Standardized all project-generated logs under the repository-root `logs/` directory.
- Moved installer diagnostics to `logs/install.log`.
- Moved upgrade diagnostics to `logs/upgrade.log`.
- Added `logs/*` to `.gitignore` while retaining `logs/.gitkeep`.
- Documented the shared log-location rule for future application logging.

## 0.01 - 01.09.2026

- Created the `devel` development branch while keeping `main` stable.
- Added `install.cmd` and `install.ps1` for deterministic first-run environment preparation.
- Added automatic Git for Windows installation through WinGet when Git is missing.
- Added exact .NET SDK installation based on `global.json` using Microsoft's official installer.
- Added recursive Git submodule initialization and synchronization.
- Added locked NuGet restore, Release x64 build verification, and unit-test verification.
- Added `upgrade.cmd` as a minimal self-updating bootstrap launcher.
- Added `upgrade.ps1` as the authoritative safe upgrade runner using fast-forward-only synchronization.
- Added initial installer and upgrade diagnostics.
- Added shared environment bootstrap logic in `scripts/Environment.ps1`.
- Added `UPGRADE.md` documenting the maintenance protocol and known updater traps.
