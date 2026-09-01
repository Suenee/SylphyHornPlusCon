# TODO

- [x] Remove the legacy .NET Framework 4.8 target from the SylphyHornPlusCon application and test projects.
  - [x] Remove `net48` from `source/SylphyHorn/SylphyHorn.csproj`.
  - [x] Remove `net48` from `source/SylphyHorn.Tests/SylphyHorn.Tests.csproj`.
  - [x] Remove the .NET Framework 4.8 Developer Pack prerequisite from `install.cmd` and `upgrade.cmd`.
  - [ ] Audit CI and release workflows for any remaining explicit `net48` matrix entries or assumptions.
  - [ ] Verify the external `VirtualDesktop` submodule remains compatible with the .NET 10-only parent build.
  - [ ] Verify Windows 10 and Windows 11 compatibility after the migration.
