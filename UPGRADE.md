# Install and Upgrade Protocol

SylphyHornPlusCon follows the shared Windows maintenance pattern used by related Suenee projects.

## First installation

Run:

```cmd
install.cmd
```

`install.cmd` is intentionally a minimal launcher. `install.ps1` is the authoritative first-run installer.

The installer:

- requires a Git working copy of `Suenee/SylphyHornPlusCon`;
- installs Git for Windows through WinGet when Git is missing;
- reads the exact required .NET SDK from `global.json`;
- installs that exact SDK from Microsoft's official `dotnet-install.ps1` when necessary;
- initializes and updates all Git submodules recursively;
- restores NuGet packages in locked mode;
- builds the Release x64 configuration;
- runs unit tests before reporting success;
- records the complete first-run transcript in `install.log`.

The .NET SDK installed by this project is placed in the current user's `%LocalAppData%\Microsoft\dotnet` directory and is added to the user's PATH. Administrator elevation is not required for the .NET SDK installation.

## Regular updates

Normally run only:

```cmd
upgrade.cmd
```

`upgrade.cmd` is a small bootstrap launcher. Before doing anything else it downloads the current `upgrade.ps1` from the active `main` or `devel` branch and executes that temporary runner. The running CMD launcher is never overwritten or reconstructed.

The upgrade runner:

- refuses to destroy tracked local modifications;
- leaves untracked user/runtime data untouched;
- uses fast-forward-only Git synchronization;
- verifies that local `HEAD` exactly matches `origin/<branch>` after synchronization;
- synchronizes and updates submodules recursively;
- reloads the environment helper after Git synchronization so dependency logic self-updates too;
- installs the exact .NET SDK required by the current `global.json` when necessary;
- restores dependencies, builds Release x64, and runs unit tests;
- writes a single-run diagnostic transcript to root `upgrade.log`;
- returns a non-zero process exit code on failure.

## Stable diagnostic phases

Upgrade failures identify one of these phases:

- `SELF-UPDATE`
- `DEPENDENCIES`
- `SYNC`
- `SUBMODULES`
- `VERIFY`
- `COMPLETE`

The final status is always one of:

```text
STATUS: SUCCESS - phase=COMPLETE
STATUS: FAILED - phase=<PHASE>
```

## Line endings

Windows maintenance scripts are explicitly CRLF-controlled by `.gitattributes`:

```gitattributes
*.cmd text eol=crlf
*.bat text eol=crlf
*.ps1 text eol=crlf
```

Do not reconstruct or rewrite a running CMD launcher through PowerShell text pipelines. Git semantics, rather than raw working-tree byte hashes, are authoritative when deciding whether tracked files changed.

## Safety rules

Do not reintroduce these updater failure patterns:

- large label-heavy logic inside `upgrade.cmd`;
- self-overwriting a currently executing CMD file;
- `CMD -> PowerShell -> CMD` updater chains;
- routine `git reset --hard` synchronization;
- broad `git clean -fd` cleanup;
- stashing or deleting untracked user/runtime data;
- overwriting user configuration;
- direct CRLF-versus-Git-blob byte comparisons;
- reporting success before restore, build, and tests complete.

When a new updater defect is discovered, document its symptom, root cause, and prevention rule here so the same class of failure is not repeated.
