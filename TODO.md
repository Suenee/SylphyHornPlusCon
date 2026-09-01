# TODO

- [ ] Remove the legacy .NET Framework 4.8 target and migrate the entire solution to .NET 10 only.
  - Audit all projects in the solution and the `VirtualDesktop` submodule for `net48` dependencies.
  - Remove `net48` from all project target frameworks where it is not strictly required.
  - Remove the .NET Framework 4.8 Developer Pack prerequisite from `install.cmd` and `upgrade.cmd`.
  - Update CI, build, restore, test, and release commands to target .NET 10 only.
  - Verify Windows 10 and Windows 11 compatibility after the migration.
