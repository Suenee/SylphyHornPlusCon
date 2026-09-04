# Install and Upgrade Protocol

SylphyHornPlusCon uses a bootstrap-based Windows maintenance workflow. Existing repositories follow the proven updater architecture documented in `Suenee/FolderHeatMap/UPGRADE.md`: keep `upgrade.cmd` small, always execute the current branch launcher from `%TEMP%`, then execute the current branch version of `upgrade.ps1` from `%TEMP%`, and keep all substantial upgrade logic in that authoritative runner.

A standalone `upgrade.cmd` also supports a minimal fresh-install bootstrap when it is the only file in the target folder. That path exists only to create the initial repository checkout; once the checkout exists, control returns immediately to the normal self-updating upgrade architecture.

## First installation

The full repair/authoritative installer remains:

```cmd
install.cmd
```

`install.cmd` may rebuild tracked checkout state from `origin/devel`, initializes submodules, installs the exact .NET SDK declared in `global.json`, restores dependencies, builds Release x64, runs unit tests, and logs to `logs/install.log`.

For a new empty target folder, it is also valid to place only the current `upgrade.cmd` in that folder and run it:

```cmd
upgrade.cmd
```

The standalone bootstrap MUST:

- require that the target folder contains only `upgrade.cmd`;
- refuse installation when any unrelated file or directory is present;
- run its destructive checkout step from a temporary copy under `%TEMP%`, never from the repository copy that Git is about to replace;
- install Git for Windows through WinGet when Git is missing and WinGet is available;
- register only the exact local/mapped path and, where applicable, its exact UNC equivalent as Git `safe.directory` values;
- create the checkout directly in the current folder, never in a nested repository directory;
- use `devel` as the fresh-install branch;
- restore the bootstrap file when repository creation/download fails before a usable checkout has been materialized;
- hand control to the freshly downloaded repository `upgrade.cmd` immediately after checkout so current self-update, submodules, dependencies, build, tests, version verification, and runtime handling remain authoritative.

Repository/runtime data may live on local, mapped-network, or UNC paths. Git `safe.directory` handling must never use a wildcard trust rule.

## Regular upgrades

Normally run only:

```cmd
upgrade.cmd
```

For an existing repository the upgrade architecture is deliberately split into three execution layers.

### Stage 0 - local `upgrade.cmd`

The repository copy is only an entry point. Before running any substantial bootstrap logic it must transfer control to the current `upgrade.cmd` from the active remote branch.

Stage 0 responsibilities are intentionally limited to:

- resolve and normalize the repository path;
- detect the special standalone fresh-install case before Git repository probing;
- verify Git and Windows PowerShell availability for an existing checkout;
- recover exact Git `safe.directory` when required;
- determine the active supported branch (`main` or `devel`);
- verify/set the expected `origin` URL;
- fetch that explicit branch;
- extract `origin/<branch>:upgrade.cmd` to a raw temporary file;
- normalize that temporary launcher explicitly to CRLF;
- execute the CRLF-normalized current launcher from `%TEMP%` using `--current-bootstrap`;
- return exactly the child launcher's exit code.

Stage 0 must not assume that the repository copy of `upgrade.cmd` is current. This is the core self-update contract.

The fresh-install exception does not weaken this rule: the standalone launcher performs only enough work to create the checkout. It then invokes the repository launcher, which immediately enters the normal Stage-0 self-update path before dependency/build work begins.

### Stage 1 - current remote `upgrade.cmd` in `%TEMP%`

The current launcher now runs independently of the repository copy that Git may replace later.

Its responsibilities are limited to:

- revalidate the repository and exact branch;
- fetch the target branch again;
- extract `origin/<branch>:upgrade.ps1` to a unique file under `%TEMP%`;
- pass repository path and branch through environment variables;
- execute the temporary runner with Windows PowerShell;
- delete the temporary runner and return exactly its exit code.

The launcher must not perform repository synchronization, dependency installation, restore, build, tests, deployment, or self-modifying continuation logic.

Backward compatibility is intentional: `upgrade.cmd` still accepts the legacy `--temp-run <repo>` handoff used by updater revisions 0.14-0.19. A legacy handoff enters Stage 0 so the currently running generation can still transfer control to the latest remote launcher.

### Stage 2 - `upgrade.ps1` authoritative runner

All substantial work belongs here. The runner always comes from the freshly fetched target branch and executes from `%TEMP%`; therefore replacing the repository copies of `upgrade.cmd` or `upgrade.ps1` cannot affect the currently running code.

Current phases are:

```text
SELF-UPDATE
SUBMODULES
DEPENDENCIES
RESTORE
BUILD
TEST
VALIDATION-CLEANUP
COMPLETE
```

The runner:

- verifies the expected remote and explicit target branch;
- fetches the target branch again before synchronization;
- protects tracked local edits outside the two bootstrap files;
- treats `upgrade.cmd` and `upgrade.ps1` as authoritative remote bootstrap state, not user data;
- synchronizes the tracked repository deterministically with `git reset --hard origin/<branch>` only after protected tracked edits have been ruled out;
- never runs `git clean -fd`;
- never stashes untracked runtime/user data;
- verifies `HEAD == origin/<branch>` after synchronization;
- verifies that repository `upgrade.ps1` equals the fetched branch runner;
- synchronizes submodules recursively while preserving untracked build/runtime artifacts;
- reads the exact required .NET SDK version from `global.json`;
- installs the documented stable SDK through WinGet when needed;
- restores the .NET 10 projects with `--force-evaluate` while the repository lock file migration remains transitional;
- builds Release x64;
- runs unit tests;
- restores tracked NuGet lock files after validation so maintenance does not leave the repository dirty;
- checks that validation did not create unexpected tracked changes;
- writes a single-run diagnostic log to `logs/upgrade.log`;
- exits non-zero on failure and always records a final status.

## Why `git reset --hard` is allowed here

A broad hard reset is normally prohibited because it can destroy tracked local work. In this updater it is used only after the runner has explicitly verified that no protected tracked local edits exist. The only excluded files are `upgrade.cmd` and `upgrade.ps1`, which are maintenance-owned bootstrap files whose remote versions are authoritative. `git reset --hard` does not delete untracked runtime data; broad `git clean` remains prohibited.

This is the deterministic synchronization model recommended by the FolderHeatMap buglist for Windows bootstrap files that can remain apparently modified because of line-ending materialization or prior updater generations.

## Network drives and `safe.directory`

Mapped and UNC repositories are supported first-class scenarios, including the standalone fresh-install bootstrap.

For a fresh install, the exact target path is registered before repository creation. If the target is a mapped drive and Windows exposes its `HKCU\Network` remote mapping, the exact UNC equivalent is registered as well. Wildcard trust is prohibited.

If `git rev-parse` on an existing repository fails with `detected dubious ownership`, the launcher must:

1. preserve the exact Git diagnostic;
2. detect that specific failure signature;
3. parse Git's own suggested `safe.directory` value;
4. register only that exact repository with `git config --global --add safe.directory ...`;
5. retry repository detection.

Never use:

```text
safe.directory=*
```

A failed Git repository check must never be interpreted as permission to clone a nested repository until `dubious ownership` has been ruled out. Fresh installation is permitted only by the explicit bootstrap-only-folder check.

The launcher parses Git's own `safe.directory` suggestion directly in CMD. Do not embed a complex `powershell.exe -Command "..."` expression containing parentheses inside a parenthesized CMD block; `cmd.exe` parses block delimiters before PowerShell receives the text and can terminate the block early.

## Line endings

Windows scripts are protocol-controlled by `.gitattributes`:

```gitattributes
*.cmd text eol=crlf
*.bat text eol=crlf
*.ps1 text eol=crlf
```

Git semantics are authoritative. Never compare raw working-tree bytes with Git blobs to decide whether a Windows script changed; CRLF/LF materialization can create false mismatches.

A remote `upgrade.cmd` extracted with `git show` is a Git blob and therefore may contain LF line endings even though `.gitattributes` requires CRLF in a Windows working tree. Stage 0 must never execute that raw blob directly. It must materialize a separate temporary `.cmd` with explicit CRLF normalization first.

The authoritative PowerShell runner is extracted directly from Git and executed as a `.ps1`; it does not use CMD labels and does not require the same batch-label CRLF recovery step.

## Native command rules

Windows PowerShell 5.1 can surface native stderr as PowerShell error records. Stderr alone is not failure.

For Git, WinGet, and .NET commands:

- native process exit code is authoritative;
- capture `$LASTEXITCODE` immediately after the command;
- classify warning/error-looking text only for display;
- do not allow harmless stderr to become a terminating PowerShell exception;
- log enough native output for remote diagnosis.

## Runtime and user data

Upgrade must preserve configuration, logs, credentials, databases, local state, and user-created data. Such data must be ignored, external, or explicitly migrated.

Never use `git stash -u` or broad `git clean -fd` as routine upgrade operations. Untracked files are left untouched by the synchronization model.

Fresh bootstrap is intentionally stricter: it only proceeds when the target contains exactly the known bootstrap `upgrade.cmd`. It must not reinterpret an arbitrary non-repository folder as disposable installation space.

All project-generated logs remain under repository-root `logs/`. `logs/upgrade.log` is single-run and truncated for every runner invocation.

## Final status contract

Every authoritative runner execution must end with exactly one semantic status:

```text
STATUS: SUCCESS - phase=COMPLETE
STATUS: WARNING - phase=COMPLETE
STATUS: FAILED - phase=<PHASE>
```

Process exit code and final status must agree.

## Known updater traps and prevention rules

The following failures are considered regression tests for this project family.

### Standalone installer overwrites an unrelated folder

Symptom: a downloaded `upgrade.cmd` turns an existing ordinary folder into a repository or destroys unrelated files.

Root cause: fresh-install detection is based only on “not a Git repository” rather than on a strict bootstrap-only-folder contract.

Prevention: fresh bootstrap proceeds only when the target contains exactly `upgrade.cmd`. Any other item aborts before repository creation or file deletion.

### Standalone bootstrap overwrites itself while running

Symptom: batch labels disappear, mixed generations execute, or checkout fails because `upgrade.cmd` is an untracked file that would be overwritten.

Root cause: Git checkout replaces the same batch file that `cmd.exe` is actively reading.

Prevention: copy the bootstrap to `%TEMP%`, transfer control there, and only then remove the known target `upgrade.cmd` before materializing the checkout. After checkout, invoke the repository launcher as a new process.

### Old launcher cannot reach the new launcher

Symptom: GitHub contains a fixed `upgrade.cmd`, but running the repository copy still executes the old/broken launcher and never reaches the fixed version.

Root cause: the local launcher tries to perform its own complete bootstrap before transferring control to the current remote launcher. If that local generation contains a parser/bootstrap defect, self-update is impossible.

Prevention: Stage 0 exists only to fetch and execute the current remote `upgrade.cmd` from `%TEMP%`. All changeable bootstrap behavior belongs to the current temporary launcher, not the repository entry-point generation.

Recovery rule: if a pre-Stage-0 launcher is already broken before it can fetch the remote launcher, it cannot repair itself. Replace `upgrade.cmd` once from outside that process (for example with Git from the repository directory), then all subsequent versions must self-update through Stage 0.

### Self-overwriting a running CMD

Symptom: messages from different updater generations appear in one run, impossible labels/variables execute, or the updater reports itself dirty repeatedly.

Root cause: `cmd.exe` continues reading a tracked batch file after Git has replaced it.

Prevention: the current remote `upgrade.cmd` runs from `%TEMP%`; the authoritative `upgrade.ps1` also runs from `%TEMP%`. Repository synchronization may therefore replace both repository copies without affecting executing code.

### Bootstrap file remains dirty after self-update

Symptom: repeated `M upgrade.cmd` even after stash/checkout/restore attempts.

Root cause: bootstrap file materialization, index state, CRLF conversion, or an older updater generation modifies the same tracked file it later validates.

Prevention: exclude only `upgrade.cmd` and `upgrade.ps1` from user-edit protection, then synchronize them deterministically from the fetched branch before build. Do not repeatedly patch the working bootstrap file in place.

### CMD -> PowerShell -> CMD chains

Symptom: broken quoting, trailing-backslash corruption, lost exit codes, or batch-label errors.

Prevention: Stage 0 uses PowerShell only for one narrowly-scoped CRLF materialization step outside parenthesized CMD blocks; Stage 1 launches one authoritative PowerShell runner. Repository path is transported through environment variables and normalized once.

### Inline PowerShell inside a parenthesized CMD block

Symptom: CMD prints a fragment of the PowerShell command followed by `was unexpected at this time.` before PowerShell starts.

Root cause: `cmd.exe` parses parentheses and block structure before invoking `powershell.exe`; parentheses inside a quoted `-Command` string can still break a surrounding CMD `if (...)` block.

Prevention: do not place complex inline PowerShell expressions inside parenthesized CMD blocks. The Stage 0 CRLF conversion command is deliberately a standalone command line outside any parenthesized block.

### Raw remote CMD executed without CRLF materialization

Symptom: `The system cannot find the batch label specified`, random label jumps fail, or execution differs between local checkout and a TEMP launcher extracted from Git.

Root cause: `git show` returns normalized Git blob content, typically LF, while Windows batch execution and label scanning are sensitive to line materialization.

Prevention: extract remote `upgrade.cmd` to a raw temporary file, normalize line endings explicitly to CRLF, and execute only the normalized temporary `.cmd`.

### Git `dubious ownership` on NAS/UNC

Symptom: Git says the repository is unsafe, then bootstrap logic falsely treats it as a non-repository.

Prevention: detect this exact Git error, register Git's exact suggested `safe.directory`, retry, and never wildcard trust.

### Raw hash/byte comparison

Symptom: clean CMD/PS1 appears modified forever.

Root cause: CRLF working-tree bytes differ from normalized Git blob bytes.

Prevention: use Git diff/revision semantics and `HEAD == origin/<branch>`.

### Managed stash captures runtime data

Symptom: configuration/log/state disappears into a stash or is restored unpredictably.

Prevention: current SHPC policy is strict abort for protected tracked edits and no automatic stash. Untracked files are never stashed by the upgrader.

### Broad Git cleanup

Symptom: local configuration or useful files disappear during upgrade.

Prevention: never use broad `git clean -fd`. Remove only known generated paths when a future migration explicitly requires it.

### Wrong branch built

Symptom: successful build from an unintended commit.

Prevention: existing checkouts use an explicit branch limited to `main`/`devel`; fresh bootstrap explicitly installs `devel`; final `HEAD` must equal `origin/<branch>` before build.

### Native stderr treated as fatal

Symptom: CRLF warning, Git progress, or compiler warning aborts the upgrade.

Prevention: `$LASTEXITCODE` decides success/failure; stderr is only classified for presentation.

### `$LASTEXITCODE` read too late

Symptom: an unrelated later command changes the apparent result of Git/WinGet/dotnet.

Prevention: capture it immediately inside the native-command wrapper.

### Generated lock file leaves repository dirty

Symptom: next upgrade aborts because NuGet restore modified a tracked `packages.lock.json`.

Prevention: restore only tracked lock files after restore/build/test validation, then verify no other protected tracked file changed.

### Network path with trailing backslash

Symptom: quoting breaks when a mapped/UNC repository path crosses CMD/PowerShell boundaries.

Prevention: normalize repository path and remove the trailing separator before setting `SHPC_UPGRADE_REPO`.

## Minimum acceptance test for updater changes

Before treating the updater as stable, test at least:

- standalone `upgrade.cmd` as the only file in an empty local target folder;
- standalone `upgrade.cmd` as the only file on a mapped network target;
- standalone fresh install with Git missing and WinGet available;
- standalone fresh install with an unrelated file present must abort without overwriting it;
- standalone fresh-install download failure must leave/recover a usable bootstrap path;
- clean `devel` repository;
- no remote update;
- remote source update;
- older but functional Stage-0 launcher reaching the current remote launcher;
- current remote launcher reaching the current remote runner;
- immediate second run (idempotence);
- protected tracked source modification;
- modified `upgrade.cmd` from an old updater generation;
- CRLF-safe execution of a TEMP `upgrade.cmd` extracted from a Git blob;
- untracked file in repository root;
- untracked build files inside submodules;
- real tracked change inside a submodule;
- repository path with spaces;
- mapped network drive;
- UNC path / Git `dubious ownership` recovery;
- malformed/complex bootstrap command text must not be parsed inside parenthesized CMD blocks;
- missing required .NET SDK with WinGet available;
- restore failure;
- build failure;
- test failure;
- harmless native stderr warning;
- final log status matching the process exit code.

When a new upgrade defect is discovered, add its symptom, root cause, and prevention rule here. The same failure class should occur only once across this project family.
