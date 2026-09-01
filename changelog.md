# Changelog

## 0.09 - 01.09.2026

- Added explicit Git `safe.directory` registration for the repository in protected global configuration.
- Added mapped-drive to UNC path resolution through `HKCU\Network` so Git trusts the exact canonical network path without using `safe.directory=*`.
- Removed reliance on command-local `-c safe.directory=*` overrides for normal repository operations.
- Fixed the CMD self-bootstrap control flow by keeping the TEMP-run `call` and parent `exit /b` on one parsed line, preventing the parent batch from resuming from a repository copy that may have been replaced during checkout.
- Applied the same network-share and self-bootstrap protections to both `install.cmd` and `upgrade.cmd`.

## 0.08 - 01.09.2026

- Replaced the repeated bootstrap-reconciliation strategy with a deterministic existing-repository rebuild.
- Installer now checks tracked changes only by path: known maintenance files are disposable, while any source/user tracked edit stops the install.
- After the safety check, the installer force-checks out `devel` directly from `origin/devel`, eliminating CRLF/hash/self-update reconciliation loops.
- Untracked files are preserved; no broad `git clean` is used.
- Recursive submodules are synchronized with `--force` after the tracked tree is rebuilt.

## 0.07 - 01.09.2026

- Rolled back the overly strict remote-content bootstrap reconciliation introduced in 0.05/0.06.
- Installer now treats only the known maintenance bootstrap files (`install.cmd`, `upgrade.cmd`, and the three legacy PowerShell bootstrap paths) as replaceable during a fresh-install refresh.
- Any tracked change outside those maintenance-owned paths still blocks installation.
- Maintenance paths are restored individually from the current HEAD before the normal `--ff-only` update; the installer does not reset the whole working tree.

## 0.06 - 01.09.2026

- Fixed bootstrap reconciliation for older existing checkouts.
- Removed pre-validation deletion of legacy tracked PowerShell files, which previously created false local modifications before the safety check.
- Installer now accepts tracked bootstrap changes only when each changed path already matches `origin/devel`, including files intentionally deleted on the remote branch.
- Verified bootstrap-only changes are restored to the current HEAD before the normal `--ff-only` update, preserving protection against unrelated local edits.

## 0.05 - 01.09.2026

- Fixed the existing-repository install bootstrap so downloading a fresh tracked `install.cmd` no longer causes a false local-change failure.
- The installer now fetches `origin/devel` first, verifies the downloaded `install.cmd` against the remote Git blob using Git-normalized hashing, restores the tracked copy, and then fast-forwards safely.
- Any other tracked local modification still blocks installation.

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
