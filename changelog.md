# Changelog

## 0.19 - 02.09.2026

- Replaced updater source synchronization based on `git merge --ff-only` with `git reset --keep origin/<branch>` after the protected-change safety gate passes.
- This avoids merge/index-state failures while preserving local tracked changes by aborting if an update would overwrite them.
- `upgrade.cmd` remains maintenance-owned and is normalized to the current `HEAD` before branch synchronization.
- The updater now prints the exact Git synchronization error to the console as well as `logs/upgrade.log` if the reset cannot be completed.
- Untracked files are not proactively cleaned or deleted by the updater.

## 0.18 - 02.09.2026

- Rewrote `upgrade.cmd` self-update handling instead of continuing the previous dirty-state patch sequence.
- Declared `upgrade.cmd` a maintenance-owned file: it is intentionally excluded from the protected local-change gate because the updater itself is responsible for replacing it.
- Remote updater code now runs only from `%TEMP%` and no longer modifies the repository copy before the safety check.
- All other tracked working-tree changes, staged changes, and submodule commit changes remain protected and continue to block upgrade.
- Immediately before `git merge --ff-only`, only the repository copy of `upgrade.cmd` is normalized back to the current `HEAD`; the normal fast-forward then updates it together with the rest of the branch.
- Kept untracked submodule build artifacts ignored while preserving detection of real tracked submodule changes.
- This replaces the failed 0.15-0.17 self-update approaches rather than adding another exception to them.

## 0.17 - 02.09.2026

- Fixed the remaining self-update dirty-state loop in `upgrade.cmd`.
- `upgrade.cmd` no longer checks out the remote updater into the index before the safety check.
- The remote updater now runs only from `%TEMP%`, while the tracked working-tree and index copy of `upgrade.cmd` are restored to the current `HEAD` before dirty-state validation.
- The normal `git merge --ff-only` step is now solely responsible for updating the repository copy of `upgrade.cmd` together with the rest of the branch.
- This preserves conservative protection for all real tracked local changes while preventing the updater from staging itself.

## 0.16 - 02.09.2026

- Fixed `upgrade.cmd` self-update so the working-tree copy of `upgrade.cmd` is updated from `origin/<branch>` before control is transferred to the temporary updater.
- Prevented the updater from blocking itself during the subsequent tracked-change safety check.
- The self-update step still changes only `upgrade.cmd`; all other tracked local edits remain protected and continue to block an upgrade.

## 0.15 - 02.09.2026

- Fixed `upgrade.cmd` dirty-state detection for repositories containing Git submodules with untracked build or restore artifacts.
- Replaced `git status --porcelain` as the upgrade gate with tracked-diff checks using `--ignore-submodules=untracked`.
- Upgrade now ignores only untracked submodule content such as generated `bin`/`obj` files while still rejecting tracked source edits, staged edits, and submodule commit changes.
- Restores known generated NuGet lock files before evaluating whether the working tree is safe to upgrade.
- When a real tracked modification blocks upgrade, the exact blocking paths are now printed to the console and written to `logs/upgrade.log`.

## 0.14 - 02.09.2026

- Aligned `upgrade.cmd` with the verified .NET 10 validation flow used by `install.cmd` 0.13.
- Kept upgrade behavior conservative: tracked local changes still stop the update before source synchronization.
- Removed the temporary second `--locked-mode` restore from the updater while repository lock files are still being migrated.
- Upgrade now uses `--force-evaluate` restore, Release x64 build, and .NET 10 unit tests against `net10.0-windows10.0.26100.0`.
- Fixed updater lock-file cleanup so only lock files actually tracked by Git are restored.
- Added explicit updater version reporting and retained `cls`, self-update, mapped-drive/UNC safe-directory support, and exact .NET SDK validation.

## 0.13 - 02.09.2026

- Completed the .NET 10 target alignment for `SylphyHorn.Core`.
- Removed the remaining `net48` target from the core project.
- Aligned `SylphyHorn.Core` with `net10.0-windows10.0.26100.0`, matching the main application and test projects.
- Simplified the core MetroTrilithon binary selection to the .NET 10 build only.
- Fixed install-time NuGet lock-file cleanup so it restores only lock files that are actually tracked by Git.
- Prevented cleanup from failing when `source/SylphyHorn.Tests/packages.lock.json` does not exist.

## 0.12 - 01.09.2026

- Rolled back the repeated dirty-state reconciliation strategy in `install.cmd`.
- `install.cmd` is now authoritative: an existing checkout is reset directly to `origin/devel`, including recursive submodule cleanup.
- Removed install-time rejection of dirty tracked files and dirty submodules left by previous failed installation attempts.
- Kept `upgrade.cmd` conservative: upgrades still stop when tracked local changes are present.
- Temporarily disabled NuGet locked restore during installation while the repository lock files are being migrated after the .NET 10-only conversion.
- Installation now performs a normal `--force-evaluate` restore, build, and test, then restores tracked lock files so the checkout remains clean.
- Both maintenance scripts continue to start with `cls`.

## 0.11 - 01.09.2026

- Added `cls` at startup for both `install.cmd` and `upgrade.cmd`.
- Added a transitional .NET 10 lock-file migration step using `dotnet restore --force-evaluate` after the removal of the legacy `net48` target.
- Immediately verifies the regenerated dependency graph with a second `--locked-mode` restore.
- Restores tracked `packages.lock.json` files after validation so install/upgrade do not leave the working tree dirty while the repository lock files are being migrated permanently.

## 0.10 - 01.09.2026

- Removed the legacy `net48` target from the main SylphyHornPlusCon application project.
- Removed the legacy `net48` target from the unit-test project.
- Simplified MetroTrilithon binary selection to the .NET 10 build only.
- Removed the obsolete .NET Framework-specific `System.Runtime` reference and target conditions.
- Aligned project target frameworks with the existing .NET 10-only NuGet lock file so locked restore can remain enabled.
- Updated `TODO.md` to track only the remaining CI, submodule, and Windows compatibility verification work.

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
- Preserved recursive Git submodule initialization and synchronization.
- Added locked NuGet restore, Release x64 build verification, and unit-test verification.

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
