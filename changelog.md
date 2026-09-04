# Changelog

## 0.39 - 04.09.2026

- Added the VPP v1 translation layer between the transport-independent `DesktopControlService` and the WebSocket client, exposing `activateDesktop`, `addDesktop`, `setIndividualWallpapers`, `setDesktopWallpaper`, `getDesktopState`, and `getDesktop` without leaking VPP concerns into the application API.
- Changed the WebSocket endpoint to the SUB contract `ws://host:port/<socketBox>?apiKey=<apiKey>` and added validation for a 64-character hexadecimal API key and a valid local Socket Box name.
- Added mandatory VPP `registerConnection` admission after authenticated WebSocket transport establishment; `DesktopControlService` is enabled only after SUB returns `status: admitted`.
- Added VPP maximum-connection replacement negotiation, including the `replacementNegotiation` state, existing-connection roster, explicit `replaceConnection` selection, and `cancelConnectionNegotiation` support in the WebSocket settings UI.
- Added UUIDv7 VPP envelopes, request/response correlation, structured VPP errors, fragmented text-message reception, a 1 MiB message safety limit, SUB heartbeat `ping`, and graceful `disconnecting` signaling.
- Added runtime peer discovery from valid application traffic so SHPC does not encode a target Socket Box in its manifest or settings; outgoing desktop-state events are routed only after the peer mailbox is learned.
- Added `desktopStateChanged` feedback sourced from `DesktopControlService.StateChanged` so SUM can expose the current desktop, canonical name, title, position, desktop count, wallpaper-management state, and desktop-list JSON as Companion variables.
- Added `manifest/sylphyhornpluscon.json` as the SUM communication manifest. The manifest defines the SHPC actions, methods, variables, event mapping, and queue policies while intentionally leaving target Socket Box routing to SUM runtime configuration.

## 0.38 - 04.09.2026

- Simplified WebSocket connection failures shown in Settings to the readable `Unable to connect` status instead of exposing raw exception text in the status row.
- Kept full WebSocket failure diagnostics in the structured App log, including the target endpoint, Socket box, and exception details needed for troubleshooting.
- Added structured log entries for invalid WebSocket connection settings and for connection attempts that fail to reach the open state.
- Simplified receive-loop failures to `Connection lost` in the UI while preserving the technical exception in the App log.

## 0.37 - 04.09.2026

- Added a new `WebSocket` settings page with connection status, IP, socket port, `Socket box`, protected API key, and a single `Connect` / `Disconnect` action.
- Added a four-state connection indicator: gray for disconnected, yellow for connecting, green for connected, and red for errors.
- Added a real `ClientWebSocket` connection attempt to the configured endpoint while intentionally leaving VPP payload handling for the next protocol-adapter step.
- Added persistent WebSocket endpoint settings to the existing per-user `Settings.xml` store and protected the API key with Windows DPAPI instead of storing it as plain text.
- Connected WebSocket lifecycle to `DesktopControlService.Enabled`: successful connection unlocks mutating desktop commands; disconnect, remote close, connection error, and application exit lock them again.
- Kept `Socket box` persisted as the future SUB mailbox `cname`; it is not yet consumed by the 0.37 transport-only connection attempt.
- Fixed the 0.36 build failure in `DesktopControlService.AddAsync` by making the dispatcher lambda return a concrete value so generic type inference succeeds.

## 0.36 - 04.09.2026

- Added a transport-independent `DesktopControlService` as the application-facing command/state boundary for future external integrations; it has no JSON, WebSocket, VPP, SUB, or SUM dependency.
- Added desktop selection by unique canonical name, Windows desktop ID, or one-based position, with canonical name intended as the primary stable automation identifier.
- Added commands to activate a desktop and create a new desktop; `position = 0` appends at the end, while an explicit position inserts the new logical desktop at that position by reusing the existing cross-version logical reorder backend.
- Added global `on` / `off` / `toggle` control for individual wallpaper management and per-desktop `on` / `off` / `toggle` control with remembered session wallpaper state for reversible disable/enable behavior.
- Added immutable whole-system and per-desktop state snapshots plus a `StateChanged` event so future protocol adapters can provide feedback without polling the SHPC UI.
- Added uniform `CommandResult<T>` results with stable error codes and returned state for command feedback.
- Added a thread-safe `Enabled` gate. Read-only state remains available while disabled, but mutating control commands return `service_disabled`; the gate is intentionally transport-neutral so a future WebSocket layer can own its lifecycle.
- Marshalled control commands onto the virtual-desktop owner Dispatcher so future callers may safely invoke the service from non-UI threads.
- Bound the control service only after the virtual desktop runtime has initialized and integrated it with the existing application lifecycle and logging service.

## 0.35 - 04.09.2026

- Fixed the desktop ViewModel characterization contract introduced by the 0.34 Title/Name behavior: when the Windows desktop name changes, `Title` now intentionally remains part of the observed `PropertyChanged` sequence because Title is derived from that same backing value.
- Kept the 0.34 runtime behavior unchanged; this release corrects the regression test so it validates the intended notification contract instead of rejecting the newly required Title notification.

## 0.34 - 03.09.2026

- Added one cross-version logical desktop move behavior shared by drag-and-drop and the new `Order > 1..N` desktop context submenu.
- Windows 11 continues to use the native virtual-desktop reorder API, while Windows 10 emulates the same user-visible move by rotating application windows and SHPC desktop metadata between the fixed Windows desktop slots.
- Windows 10 logical moves preserve the moved desktop's windows, Title, unique canonical Name, wallpaper path, wallpaper fit mode, and current-desktop focus; pinned windows and shell surfaces are excluded from window rotation.
- Added rollback of already moved windows when a Windows 10 content-rotation step fails before metadata is committed.
- Enabled LMB drag-and-drop from the wallpaper preview on every supported Windows build and kept the complete desktop card attached to the pointer while dragging, following the VMU live-node drag behavior.
- Removed the explanatory reorder-support text above the desktop cards.
- Renamed the wallpaper action from `Reset...` to `Restore...`; it now remains visible in the context menu and is disabled when there is no managed individual wallpaper to restore.
- Added explicit early tray-icon disposal during application shutdown so Explorer receives the notification-icon delete operation before potentially slow runtime cleanup, preventing stale tray icons after updater-driven restarts.
- Made tray-icon disposal idempotent so normal shutdown and final application disposal can safely share the same cleanup path.
- Extended graceful updater shutdown from 5 to 15 seconds before project-owned process termination is used as a last resort.
- Aligned `upgrade.ps1` console presentation with the shared FolderHeatMap upgrade standard: named phases, default/gray informational output, yellow warnings, red failures, green successful completion, and stable colored final status markers while retaining a plain-text diagnostic log.
- Removed obsolete .NET 10 package references for `Microsoft.CSharp` and `System.Diagnostics.DiagnosticSource`, removed the missing legacy `MinimumRecommendedRules.ruleset` references, and renamed the App log level filter helper to eliminate the known build warnings at their source.

## 0.33 - 03.09.2026

- Refined the Desktops page based on the production VMU drag model: a dragged desktop card stays live under mouse capture, the nearest target card is highlighted, and the desktop list is allowed to refresh only after the mouse is released.
- Kept real desktop reordering delegated to the existing Windows virtual desktop runtime and clearly reports when the current Windows build does not expose native reordering support.
- Changed `Reset...` so it is available only when SHPC individual wallpaper management is enabled and the selected desktop currently has an individual wallpaper override.
- Removed the redundant supported-image-formats line from the Desktops page because the file picker already constrains selectable wallpaper formats.
- Reworked the `+ New desktop` tile to use the same card width, border, background, corner radius, spacing, and visual footprint as existing desktop cards.
- Added deterministic Title/Name fallback behavior: Title is derived from Name when Title is missing, Name is derived from Title when Name is missing, and an otherwise anonymous desktop falls back to `Desktop X` / `desktop-x` based on its current position.
- Kept canonical desktop Name values globally unique, case-insensitive, normalized to lowercase `a-z`, `0-9`, `-`, and `_`, with numeric suffixes used when a generated or edited name would collide.
- Automatic `desktop-x` fallback names now follow the current desktop position when the desktop remains otherwise anonymous; manually assigned names remain stable across reordering.

## 0.32 - 03.09.2026

- Reworked the Desktops page into a responsive wrapping card layout that automatically reflows as the Settings window is resized.
- Moved the global desktop options below the card area, separated them visually, and renamed them to `Restore saved desktop configuration on startup` and `Manage individual desktop wallpapers`.
- Added a visible vertical-ellipsis menu button to every desktop preview while retaining the same right-click context menu.
- Added `Reset...` to the desktop wallpaper menu to return a desktop to the preserved original Windows wallpaper state.
- Added reversible wallpaper management: SHPC captures the original Windows wallpaper state before taking control and restores it when wallpaper management is disabled or SHPC exits normally.
- Added a warning before wallpaper changes made while SHPC wallpaper management is disabled because the previous external wallpaper state may not be recoverable.
- Kept wallpaper-management behavior OS-neutral in the UI so the same workflow applies on supported Windows 10 and Windows 11 systems while the runtime selects the available mechanism.
- Canonical desktop `Name` values are now normalized to lowercase, limited to `a-z`, `0-9`, `-`, and `_`, compared case-insensitively, and made unique with numeric suffixes when needed.
- Enlarged and centered the `+` symbol in the New desktop tile.
- Preserved real Windows desktop reordering through the existing runtime on systems where the Windows desktop API reports reordering support.

## 0.31 - 03.09.2026

- Established the project `x.xx` application-version contract for SylphyHornPlusCon.
- Changed the application package and assembly version from the inherited upstream `4.0.0` version to the project version `0.31`.
- Updated the About page version source so the application displays exactly the same `x.xx` version announced for an upgrade and recorded in this changelog.
- Removed the inherited `beta.16` suffix from the displayed application version.
- Future application-changing upgrades must increment the project version and keep the announced version, built application version, and changelog version identical.

## 0.30 - 03.09.2026

- Replaced the legacy vertical Desktop settings form with a horizontal desktop strip that mirrors the real Windows virtual desktop order.
- Added 16:9 wallpaper preview cards with the desktop number retained in the upper-left corner and horizontally scrollable layout for larger desktop sets.
- Added `Title` and `Name` fields below each preview: Title continues to use the Windows/SylphyHorn display name while Name is a stable canonical identifier persisted by desktop GUID for future automation and protocol integrations.
- Added drag-and-drop desktop reordering that delegates to the existing virtual desktop move operations instead of maintaining a UI-only order.
- Added a right-click desktop context menu with direct wallpaper selection, wallpaper fit mode, and desktop removal.
- Reworked wallpaper selection to target the selected desktop object directly instead of relying on the legacy index-based command path; on Windows 10 selecting a wallpaper automatically enables per-desktop background handling.
- Added destructive-action confirmation before removing a desktop and disabled removal when only one desktop remains.
- Replaced the full-width New desktop button with a compact `+ New desktop` tile at the end of the desktop strip.
- Added structured log events for wallpaper changes, desktop reorder requests, and desktop removal requests.
- Extended the settings schema contract with persisted canonical desktop names while preserving the existing Windows desktop-name behavior and characterization contract.

## 0.29 - 03.09.2026

- Fixed a false `run.cmd` launch failure observed when SylphyHorn starts successfully from a mapped LAN repository but the CMD `FOR /F` PowerShell-output capture fails to return the matching process ID.
- Bumped `run.cmd` to version 0.05.
- Replaced fragile command-substitution PID capture with a temporary PID handoff file while preserving exact executable-path matching for an already-running instance.
- `run.cmd` now launches SylphyHorn through `Start-Process -PassThru`, records the PID of the process it actually started, then validates that exact PID and executable path after the startup delay.
- Confirmed the authoritative PowerShell upgrade runner already performs exact-path process detection directly in PowerShell and does not depend on the fragile CMD output-capture path, so no duplicate workaround was added there.

## 0.28 - 03.09.2026

- Removed the redundant `SETTINGS` navigation heading from the modern Settings sidebar.
- Removed the redundant `SylphyHornPlusCon / Settings` footer block so the window caption remains the single Settings title.
- Moved the existing desktop-switch notification controls from General to `Notifications > Behavior`, including the master enable toggle, notification duration, and always-show option.
- Reparented the original WPF controls instead of recreating them so their existing bindings, localization, and behavior remain unchanged.

## 0.27 - 03.09.2026

- Reworked the legacy in-memory App log into a central structured logging service while preserving the existing snapshot/live subscription contract.
- Added structured log metadata for level, service, event, object identifier, message, and details while retaining compatibility with the original `ILog` error sources.
- Added `off`, `single`, and `all` persistent logging modes, following the logging convention already used by Socket Universe Bridge.
- Added UTF-8 JSONL persistence under the application LocalAppData `Logs` directory without introducing another third-party dependency.
- `single` keeps only the current application run, `all` restores previous persisted history, and `off` keeps the live UI feed without writing the normal persistent log.
- Added structured startup, shutdown, desktop-runtime, settings, exception, and task-failure entries to the central logging path.
- Replaced the old expandable exception list in Settings with a structured App log view containing a sortable table, level filters, full-text search, automatic tailing, selected-entry detail preview, clear, and filtered TXT export.
- Added a persistent `LoggingMode` application setting with `single` as the default and extended the settings contract test accordingly.
- Preserved the existing `StartupTrace` and emergency `ErrorReports` paths as bootstrap/crash fallbacks while the central logger becomes the normal operational diagnostic path.
- Fixed sequence normalization when `all` mode combines persisted history with events already captured during the current startup.

## 0.26 - 03.09.2026

- Refactored the Settings window into a modern navigation shell while preserving the existing settings bindings and application behavior.
- Made `Desktops` the default Settings page and grouped legacy numbered pages into semantic sections for notifications, keyboard shortcuts, and mouse gestures.
- Added top-level navigation for Desktops, General, Notifications, Keyboard shortcuts, Mouse gestures, App log, and About.
- Increased the Settings workspace and retained all original settings pages behind the new shell to minimize functional regression risk during the first UI refactor.
- Updated `run.cmd` process detection to identify the running application by its exact executable path instead of assuming the Windows process image name.
- Updated the upgrade runner to use the same executable-path runtime detection so a running application can be stopped before replacement and restored after a successful upgrade.

## 0.25 - 02.09.2026

- Fixed `run.cmd` launch verification so it no longer trusts the inherited CMD `ERRORLEVEL` left by the preceding process-detection pipeline.
- `run.cmd` now starts `SylphyHorn.exe`, waits briefly, and verifies the actual `SylphyHorn` process instead of treating `start` as authoritative evidence of launch success.
- Added a startup-diagnostics hint when the process does not remain running after launch.
- Bumped `run.cmd` to version 0.03.

## 0.24 - 02.09.2026

- Fixed `run.cmd` repository normalization after the launcher was initially written through the GitHub Contents API with CRLF bytes already stored in the Git blob.
- Rewrote the tracked `run.cmd` blob with normalized LF content so `.gitattributes` can materialize CRLF correctly in Windows working trees.
- Added `run.cmd` to the pre-synchronization maintenance-owned path set so an already-dirty launcher from the previous malformed blob can be recovered automatically.
- Kept `run.cmd` subject to final post-build tracked-tree validation so launcher changes are not silently ignored after repository synchronization.
- Bumped the upgrade runner to `0.24-run-launcher-normalization`.

## 0.23 - 02.09.2026

- Added `run.cmd` as a simple development launcher for the Release x64 build.
- `run.cmd` now acts as a restart command: if `SylphyHorn.exe` is already running it is stopped first and then started again; if it is not running it is started normally.
- Updated the authoritative upgrade runner so it records whether SylphyHorn was running before upgrade, stops it before repository/build work, and restores the previous running state afterward.
- Added graceful-close waiting with a bounded forced-termination fallback so running binaries cannot keep build outputs locked indefinitely.
- If an upgrade fails after SylphyHorn was stopped, the runner attempts to restore the last existing executable so an upgrade failure does not unnecessarily leave the desktop manager offline.
- Successful runtime restoration is verified by checking that the `SylphyHorn` process remains running after restart.

## 0.22 - 02.09.2026

- Added a true two-stage `upgrade.cmd` self-update bootstrap.
- Stage 0 now always fetches the explicit active branch, extracts the current remote `upgrade.cmd`, normalizes the temporary launcher to CRLF, and transfers control to that current launcher before any substantial bootstrap logic runs.
- Stage 1, running from `%TEMP%`, then extracts and runs the authoritative current `upgrade.ps1` from the same branch.
- Preserved legacy `--temp-run <repo>` compatibility by routing it through Stage 0 instead of directly into a possibly stale launcher implementation.
- Added explicit CRLF materialization for temporary remote `.cmd` execution so Git blob LF normalization cannot break batch labels.
- Documented the one-time recovery rule for already-broken pre-Stage-0 launchers: they cannot repair themselves and must have `upgrade.cmd` replaced once from outside the broken process; subsequent versions self-update through Stage 0.
- Extended `UPGRADE.md` with the three-layer bootstrap contract and regression tests for stale-launcher and raw-LF batch failures.

## 0.21 - 02.09.2026

- Fixed a CMD parser failure in the `dubious ownership` bootstrap recovery path.
- Removed the complex inline `powershell.exe -Command` expression from inside a parenthesized CMD block; `cmd.exe` could parse PowerShell parentheses as batch block delimiters and abort with `was unexpected at this time.` before PowerShell started.
- The launcher now parses Git's own suggested `safe.directory` value directly from the captured Git diagnostic and registers only that exact path.
- Kept wildcard `safe.directory=*` prohibited.
- Moved repository-detection recovery into simple bootstrap labels so the launcher stays predictable while the authoritative upgrade logic remains in temporary `upgrade.ps1`.
- Added this failure signature, root cause, prevention rule, and regression test to `UPGRADE.md`.

## 0.20 - 02.09.2026

- Replaced the monolithic self-modifying CMD updater with the proven bootstrap architecture documented in `FolderHeatMap/UPGRADE.md`.
- `upgrade.cmd` is now a small launcher that fetches the explicit active branch, extracts the authoritative `upgrade.ps1` to `%TEMP%`, passes repository/branch state through environment variables, executes the runner, and returns its exact exit code.
- Added backward-compatible handling for the legacy `--temp-run <repo>` handoff so updater revisions 0.14-0.19 can reach the new architecture once.
- Added `upgrade.ps1` as the authoritative upgrade runner for repository synchronization, dependency checks, submodules, .NET restore, Release x64 build, unit tests, logging, and final status reporting.
- Bootstrap files `upgrade.cmd` and `upgrade.ps1` are treated as authoritative remote maintenance state and excluded from user-edit protection; all other tracked local edits still abort the upgrade.
- After protected tracked edits are ruled out, the runner synchronizes deterministically with `git reset --hard origin/<branch>` while leaving untracked runtime/user data untouched and never using broad `git clean -fd`.
- Added exact Git `dubious ownership` recovery based on Git's own suggested `safe.directory` value, without wildcard trust, for mapped/UNC repositories.
- Added PowerShell native-command handling where process exit code is authoritative and harmless stderr does not become a false upgrade failure.
- Added immediate `$LASTEXITCODE` capture, explicit branch/remote verification, `HEAD == origin/<branch>` verification, and authoritative runner blob verification.
- Added final tracked-tree validation after restore/build/test and cleanup of tracked NuGet lock files generated during validation.
- Reworked `UPGRADE.md` with the bootstrap architecture, network-drive rules, known updater failure signatures, prevention rules, and minimum acceptance test matrix.

## 0.19 - 02.09.2026

- Replaced updater source synchronization based on `git merge --ff-only` with `git reset --keep origin/<branch>` after the protected-change safety gate passes.
- This avoids merge/index-state failures while preserving local tracked changes by aborting if an update would overwrite them.
- `upgrade.cmd` remains maintenance-owned and is normalized to the current `HEAD` before source synchronization.
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

- Fixed `upgrade.cmd` self-update so the working-tree copy of `upgrade.cmd` is updated from `origin/devel` before control is transferred to the temporary updater.
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
- Any tracked change outside those maintenance paths still blocks installation.
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