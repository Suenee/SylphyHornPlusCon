# Install and Upgrade Protocol

SylphyHornPlusCon uses one supported maintenance entry point for both first installation and later updates:

```cmd
upgrade.cmd
```

There is no separate installer. `install.cmd` was retired after the standalone bootstrap became part of `upgrade.cmd`. This avoids two maintenance implementations drifting apart.

The workflow follows the proven bootstrap architecture documented in `Suenee/FolderHeatMap/UPGRADE.md`: keep `upgrade.cmd` small, execute current remote maintenance code from `%TEMP%`, keep substantial repository/dependency/build work in the authoritative `upgrade.ps1`, and never overwrite a running bootstrap in place.

## First installation

Create the target folder and place only the current `upgrade.cmd` in it. Then run:

```cmd
upgrade.cmd
```

The standalone bootstrap MUST:

- require that the target folder contains only `upgrade.cmd`;
- refuse installation when any unrelated file or directory is present;
- run its destructive checkout step from a temporary copy under `%TEMP%`, never from the target copy that Git is about to replace;
- install Git for Windows through WinGet when Git is missing and WinGet is available;
- register only the exact local/mapped path and, where applicable, its exact UNC equivalent as Git `safe.directory` values;
- create the checkout directly in the current folder, never in a nested repository directory;
- use `devel` as the fresh-install branch;
- restore the bootstrap file when repository creation/download fails before a usable checkout has been materialized;
- hand control to the freshly downloaded repository `upgrade.cmd` immediately after checkout.

After checkout, the normal current updater is responsible for submodules, dependencies, restore, Release x64 build, tests, application-version verification, runtime handling, and final status.

Repository/runtime data may live on local, mapped-network, or UNC paths. Wildcard Git trust is prohibited.

## Existing repository upgrade

Run:

```cmd
upgrade.cmd
```

For an existing repository the architecture has three execution layers.

### Stage 0 - local `upgrade.cmd`

The repository copy is only an entry point. Before substantial upgrade work it transfers control to the current `upgrade.cmd` from the active remote branch.

Responsibilities:

- resolve and normalize the repository path;
- detect the strict standalone fresh-install case before Git repository probing;
- verify Git and Windows PowerShell availability for an existing checkout;
- recover exact Git `safe.directory` when required;
- determine the active supported branch (`main` or `devel`);
- verify/set the expected `origin` URL;
- fetch that explicit branch;
- extract `origin/<branch>:upgrade.cmd` to a raw temporary file;
- normalize that temporary launcher explicitly to CRLF;
- execute the current launcher from `%TEMP%` using `--current-bootstrap`;
- return exactly the child launcher's exit code.

Stage 0 must not assume that the repository copy of `upgrade.cmd` is current.

The fresh-install exception performs only enough work to create the checkout. It then invokes the downloaded repository launcher, which enters the normal self-update path before dependency/build work begins.

### Stage 1 - current remote `upgrade.cmd` in `%TEMP%`

Responsibilities:

- revalidate the repository and exact branch;
- fetch the target branch again;
- extract `origin/<branch>:upgrade.ps1` to a unique file under `%TEMP%`;
- pass repository path and branch through environment variables;
- execute the temporary runner with Windows PowerShell;
- delete the temporary runner and return exactly its exit code.

The launcher must not contain the normal repository synchronization, dependency restore, build, test, or deployment implementation.

Backward compatibility is intentional: `upgrade.cmd` still accepts the legacy `--temp-run <repo>` handoff so an older functional launcher can reach the current generation.

### Stage 2 - `upgrade.ps1` authoritative runner

All substantial upgrade work belongs here. The runner comes from the freshly fetched target branch and executes from `%TEMP%`.

The runner:

- verifies the expected remote and explicit target branch;
- fetches the target branch before synchronization;
- migrates explicitly allow-listed retired maintenance files only when they are still tracked by the local HEAD but are no longer tracked by `origin/<branch>`;
- protects all other tracked local edits outside maintenance-owned launchers;
- treats `upgrade.cmd` and `upgrade.ps1` as authoritative remote bootstrap state;
- synchronizes the tracked repository deterministically only after protected tracked edits have been ruled out;
- never runs broad `git clean -fd`;
- never stashes untracked runtime/user data;
- verifies `HEAD == origin/<branch>`;
- synchronizes submodules recursively;
- reads the exact required .NET SDK version from `global.json`;
- installs the documented stable SDK through WinGet when needed;
- restores .NET projects;
- builds Release x64;
- runs unit tests;
- restores tracked generated NuGet lock files after validation;
- verifies that maintenance did not leave unexpected tracked changes;
- verifies the built application version against the project version;
- writes `logs/upgrade.log` as a single-run diagnostic log;
- exits non-zero on failure and records a final semantic status.

## Tracked local changes

Upgrade is deliberately conservative. Protected tracked local changes stop the update before repository synchronization. This prevents an updater from destroying source edits that have not been committed.

Maintenance-owned bootstrap files may be replaced by the authoritative remote state. User/runtime data must not be treated as disposable maintenance state.

A narrow exception exists for explicitly retired maintenance files. If an older local HEAD still tracks such a file, the target `origin/<branch>` no longer tracks it, and that old file has local edits, the updater may discard those edits before the protected-change gate so the branch transition can complete. This is permitted only for paths explicitly listed by the updater as retired maintenance files; it is never inferred merely because an arbitrary remote file was deleted.

The first recorded case is `install.cmd` during the 0.42/0.43 transition. A locally modified `install.cmd` previously caused the old checkout to stop in `SELF-UPDATE` before it could reach the remote commit that removed the obsolete installer. From runner 0.30 onward, `install.cmd` is migrated automatically only when `origin/<branch>` confirms that it is no longer tracked.

## Network drives and `safe.directory`

Mapped and UNC repositories are supported first-class scenarios, including fresh installation.

For a fresh install, register the exact target path before repository creation. If the target is a mapped drive and Windows exposes its `HKCU\Network` mapping, register the exact UNC equivalent as well.

If Git on an existing repository reports `detected dubious ownership`, the launcher must:

1. preserve the exact Git diagnostic;
2. detect that specific failure signature;
3. parse Git's own suggested `safe.directory` value;
4. register only that exact repository;
5. retry repository detection.

Never use:

```text
safe.directory=*
```

A failed repository check must never be interpreted as permission to install into an arbitrary directory. Fresh installation is allowed only by the strict bootstrap-only-folder check.

## Line endings

Windows scripts are controlled by `.gitattributes`:

```gitattributes
*.cmd text eol=crlf
*.bat text eol=crlf
*.ps1 text eol=crlf
```

Git semantics are authoritative. Never compare raw working-tree bytes with Git blobs to determine whether a Windows script changed.

`git show` returns normalized Git blob content, normally LF. A remote `upgrade.cmd` extracted this way must be explicitly materialized as CRLF before CMD executes it. The PowerShell runner does not require the same batch-label materialization workaround.

## Native command rules

Windows PowerShell 5.1 can surface native stderr as PowerShell error records. Stderr alone is not failure.

For Git, WinGet, and .NET commands:

- native process exit code is authoritative;
- capture `$LASTEXITCODE` immediately;
- warning/error-looking text is presentation, not the success criterion;
- harmless stderr must not become a terminating PowerShell exception;
- log enough native output for diagnosis.

## Runtime and user data

Upgrade must preserve configuration, logs, credentials, databases, local state, and user-created data. Such data must be ignored, external, or explicitly migrated.

Never use `git stash -u` or broad `git clean -fd` as routine maintenance. Untracked files in an existing repository are preserved.

Fresh bootstrap is intentionally stricter: it proceeds only when the target contains exactly the known `upgrade.cmd` bootstrap.

All project-generated logs remain under repository-root `logs/`. `logs/upgrade.log` is truncated for each runner invocation.

## Final status contract

Every authoritative runner execution ends with exactly one semantic status:

```text
STATUS: SUCCESS - phase=COMPLETE
STATUS: WARNING - phase=COMPLETE
STATUS: FAILED - phase=<PHASE>
```

Successful application upgrades also print:

```text
Application version: x.xx
```

Process exit code and final status must agree.

## Known updater traps and prevention rules

### Parallel install and upgrade implementations

Symptom: first installation behaves differently from upgrade, fixes land in one script but not the other, or an obsolete installer blocks later upgrades as a tracked local change.

Root cause: separate `install.cmd` and `upgrade.cmd` implementations evolve independently.

Prevention: `upgrade.cmd` is the only supported entry point. Fresh-install logic is limited to repository bootstrap; after checkout it immediately hands control to the same authoritative upgrade workflow used by existing installations.

### Retired maintenance file blocks its own removal

Symptom: after a maintenance file is deleted on the target branch, an older checkout reports that the same locally modified file is a protected tracked change and stops in `SELF-UPDATE`. The updater therefore cannot reach the commit that deletes the obsolete file. The concrete SHPC case was `M install.cmd` after `install.cmd` had already been retired remotely.

Root cause: protected-change validation runs against the older local HEAD, where the obsolete maintenance file is still tracked, before the later `git reset --hard origin/<branch>` can remove it.

Prevention: after fetching the target branch but before general tracked-change validation, inspect only an explicit allow-list of retired maintenance paths. A path may be normalized to the local HEAD only when it is still locally tracked and `origin/<branch>` confirms that the path no longer exists there. The normal remote reset then removes it. Never generalize this exception to arbitrary files deleted upstream.

Regression rule: test a locally modified retired `install.cmd` against a target branch where `install.cmd` is absent. The upgrade must continue without manual `git restore`. A modified ordinary source file must still block the upgrade.

### Standalone bootstrap overwrites an unrelated folder

Symptom: a downloaded launcher turns an ordinary folder into a repository or destroys unrelated files.

Prevention: fresh bootstrap proceeds only when the target contains exactly `upgrade.cmd`. Any other item aborts before repository creation or deletion.

### Standalone bootstrap overwrites itself while running

Symptom: batch labels disappear, mixed generations execute, or checkout fails because the bootstrap would overwrite itself.

Prevention: copy the launcher to `%TEMP%`, transfer control there, remove only the known target bootstrap, then materialize the checkout and invoke the downloaded repository launcher.

### Old launcher cannot reach the new launcher

Symptom: GitHub contains a fixed launcher but an older local launcher never reaches it.

Prevention: Stage 0 exists primarily to fetch and execute the current remote `upgrade.cmd` from `%TEMP%`. If a pre-Stage-0 launcher is already broken before it can fetch remote code, replace `upgrade.cmd` once externally; later generations must remain self-updating.

### Self-overwriting a running CMD

Symptom: messages from different updater generations appear in one run or impossible labels execute.

Prevention: execute current remote CMD and PowerShell maintenance code from `%TEMP%`, not from files Git is replacing.

### Bootstrap file remains dirty after self-update

Symptom: repeated `M upgrade.cmd` after update.

Prevention: treat bootstrap files as maintenance-owned authoritative remote state and synchronize them deterministically instead of repeatedly patching the running working-tree file.

### Inline PowerShell inside a parenthesized CMD block

Symptom: CMD reports `was unexpected at this time.` before PowerShell starts.

Root cause: CMD parses block delimiters before PowerShell receives the quoted expression.

Prevention: do not place complex inline PowerShell expressions containing parentheses inside parenthesized CMD blocks.

### Raw remote CMD executed without CRLF materialization

Symptom: `The system cannot find the batch label specified` or random label jumps fail.

Prevention: normalize the extracted Git blob explicitly to CRLF before executing the temporary `.cmd`.

### Git `dubious ownership` on NAS/UNC

Symptom: Git rejects the repository and bootstrap logic falsely interprets it as a non-repository.

Prevention: detect this exact error, register Git's exact suggested `safe.directory`, retry, and never wildcard trust.

### Managed stash captures runtime data

Symptom: configuration/log/state disappears into a stash or is restored unpredictably.

Prevention: strict abort for protected tracked edits; never automatically stash untracked data.

### Broad Git cleanup

Symptom: local configuration or useful files disappear.

Prevention: never use broad `git clean -fd` as routine maintenance.

### Wrong branch built

Symptom: successful build from an unintended commit.

Prevention: existing checkouts use an explicit `main` or `devel` branch; fresh bootstrap installs `devel`; final `HEAD` must equal `origin/<branch>`.

### Native stderr treated as fatal

Symptom: Git progress or compiler warnings abort maintenance.

Prevention: native exit code is authoritative.

### `$LASTEXITCODE` read too late

Symptom: an unrelated command changes the apparent result of Git/WinGet/dotnet.

Prevention: capture it immediately after each native process.

### Generated lock file leaves repository dirty

Symptom: the next upgrade aborts because restore modified tracked lock files.

Prevention: restore only tracked generated lock files after validation and verify the remaining tracked tree.

### Network path with trailing backslash

Symptom: quoting breaks across CMD/PowerShell boundaries.

Prevention: normalize the repository path and remove the trailing separator before handoff.

## Minimum acceptance test

Before treating an updater change as stable, test at least:

- standalone `upgrade.cmd` as the only file in an empty local target folder;
- standalone `upgrade.cmd` on a mapped network target;
- fresh install with Git missing and WinGet available;
- fresh install with an unrelated file present must abort without overwriting it;
- fresh-install download failure must leave/recover a usable bootstrap path;
- clean `devel` repository;
- no remote update;
- remote source update;
- older functional Stage-0 launcher reaching the current remote launcher;
- current remote launcher reaching the current runner;
- immediate second run;
- protected tracked source modification;
- modified maintenance launcher from an older updater generation;
- locally modified retired `install.cmd` while the target branch no longer tracks it; this must migrate automatically;
- an ordinary modified tracked file must still block even when unrelated remote deletions exist;
- CRLF-safe execution of a temporary CMD extracted from a Git blob;
- untracked file in repository root;
- untracked build files inside submodules;
- real tracked change inside a submodule;
- repository path with spaces;
- mapped network drive;
- UNC path / Git `dubious ownership` recovery;
- missing required .NET SDK with WinGet available;
- restore failure;
- build failure;
- test failure;
- harmless native stderr warning;
- application-version verification;
- final log status matching the process exit code.

When a new upgrade defect is discovered, add its symptom, root cause, prevention rule, and regression scenario here. The same failure class should be solved once across this project family.