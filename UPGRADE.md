# Install and Upgrade Protocol

SylphyHornPlusCon uses a CMD-only Windows maintenance workflow. No PowerShell runner or helper script is part of the install or upgrade path.

## First installation

Run:

```cmd
install.cmd
```

`install.cmd` is a self-contained bootstrap installer. It copies itself to `%TEMP%` before modifying the target directory, so Git can safely replace the repository copy during the first checkout.

The installer:

- works from a folder containing only `install.cmd` (and optionally `logs/`);
- installs Git for Windows through WinGet when Git is missing;
- creates or updates the `devel` Git working copy of `Suenee/SylphyHornPlusCon`;
- initializes and updates all Git submodules recursively;
- reads the exact required .NET SDK version from `global.json`;
- installs that exact SDK with the official `Microsoft.DotNet.SDK.10` WinGet package when necessary;
- ensures the .NET Framework developer pack needed by the `net48` target is available;
- restores NuGet packages in locked mode;
- builds the Release x64 configuration;
- runs unit tests before reporting success;
- records diagnostics in `logs/install.log`.

## Regular updates

Normally run only:

```cmd
upgrade.cmd
```

`upgrade.cmd` also copies itself to `%TEMP%` before doing any repository update. This means the tracked repository copy may be replaced safely while the temporary copy remains the running process.

The updater:

- supports only `main` and `devel` branches;
- fetches the active branch before any update;
- checks whether `upgrade.cmd` itself changed upstream and transfers control to the remote version when necessary;
- compares the updater using Git-normalized blob hashes rather than raw working-tree bytes, avoiding CRLF false positives;
- refuses to destroy tracked local modifications;
- leaves untracked user/runtime data untouched;
- uses fast-forward-only Git synchronization;
- verifies that local `HEAD` exactly matches `origin/<branch>`;
- synchronizes and updates submodules recursively;
- installs missing required build dependencies through WinGet;
- restores dependencies, builds Release x64, and runs unit tests;
- writes diagnostics to `logs/upgrade.log`;
- returns a non-zero process exit code on failure.

## Logs

All project-generated logs belong under the repository-root `logs/` directory. Runtime log contents are never versioned. The repository tracks only `logs/.gitkeep` so the directory exists in a fresh checkout.

Current maintenance logs are:

- `logs/install.log`
- `logs/upgrade.log`

Future application logging must use the same `logs/` root and follow the project's `off` / `single` / `all` logging modes when application logging is introduced.

## Line endings

Windows maintenance scripts are explicitly CRLF-controlled by `.gitattributes`:

```gitattributes
*.cmd text eol=crlf
*.bat text eol=crlf
```

Git semantics, rather than raw working-tree byte comparison, are authoritative when deciding whether tracked CMD files changed.

## Safety rules

Do not reintroduce these updater failure patterns:

- PowerShell-based install or upgrade runners;
- self-overwriting a currently executing repository CMD file;
- routine `git reset --hard` synchronization;
- broad `git clean -fd` cleanup;
- stashing or deleting untracked user/runtime data;
- overwriting user configuration;
- direct CRLF-versus-Git-blob byte comparisons;
- writing generated logs outside `logs/`;
- reporting success before restore, build, and tests complete.

When a new updater defect is discovered, document its symptom, root cause, and prevention rule here so the same class of failure is not repeated.
