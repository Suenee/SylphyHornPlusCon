# Livet 4.0.2 dialog and binding characterization

This document records the U0 characterization input and the U5 replacement mapping for the production dialog call sites and Livet 4.0.2 behavior.

## Dialog action mapping

| Production call | Message key | XAML action | Properties copied by Livet 4.0.2 | Response and cancel | Current consumer |
|---|---|---|---|---|---|
| `OpenBackgroundPathDialog(int)` | `Window.OpenBackgroundImagesDialog.Open` | `OpenFileDialogInteractionMessageAction` | `FileName`, `InitialDirectory`, `AddExtension`, `Filter`, `Title`, `Multiselect` | success: `OpenFileDialog.FileNames`; cancel/indeterminate: `null` | Requires a non-empty first path and `File.Exists`; then updates the folder and desktop wallpaper path. Current call supplies title, current background folder, supported wallpaper filter, and `MultiSelect=false`; `FileName` remains empty and `AddExtension=true`. |
| `OpenImportPathDialog()` | `Window.OpenImportPathDialog.Open` | `OpenFileDialogInteractionMessageAction` | Same open-file mapping | Same open-file response | Requires a non-empty first path; then preserves suspend/prepare/optional confirmation/commit/notify/finally-resume ordering. Current call also supplies `LocalSettingsProvider.Filename`. |
| `OpenExportPathDialog()` | `Window.OpenExportPathDialog.Open` | `SaveFileDialogInteractionMessageAction` | `FileName`, `InitialDirectory`, `AddExtension`, `CreatePrompt`, `Filter`, `OverwritePrompt`, `Title` | success: `SaveFileDialog.FileNames`; cancel/indeterminate: `null` | Requires a non-empty first path; then updates the remembered folder and calls `ExportAsync`. Current defaults are `AddExtension=true`, `CreatePrompt=false`, and `OverwritePrompt=true`. |
| `ResetSettings()` | `Window.ResetSettingsDialog.Confirm` | `ConfirmationDialogInteractionMessageAction` | `Text`, `Caption`, `Button`, `Image`, `DefaultResult` | `OK`/`Yes` -> `true`; `Cancel` -> `null`; all other results -> `false`. The VM maps null to false. | Warning icon, `OKCancel`, default result `OK`; false/null returns without reset. |
| import desktop override | `Window.OverrideDesktopsDialog.Confirm` | `ConfirmationDialogInteractionMessageAction` | Same confirmation mapping | Same confirmation response; the VM maps null to false. | Question icon, `OKCancel`, default result `OK`; value is passed to `CommitPreparedImportAsync`. |

## U5 port mapping

The U5 `ISettingsDialogService` maps the Livet 4.0.2 behavior as follows. Message keys and Livet response objects are removed; the observable dialog configuration and the caller's effective branch result are unchanged.

| Livet 4.0.2 action behavior | U5 `SettingsDialogService` behavior |
|---|---|
| Copies `Title`, `InitialDirectory`, `Filter`, and `FileName` to `OpenFileDialog`; copies the message defaults `AddExtension=true` and `MultiSelect=false`. It does not explicitly set `CheckFileExists`, so the WPF dialog default remains in effect. | Sets the same six values, does not set `CheckFileExists`, and returns `FileNames` only when parameterless `ShowDialog()` returns `true`; otherwise returns `null`. |
| Copies `Title`, `InitialDirectory`, `Filter`, and `FileName` to `SaveFileDialog`; copies the message defaults `AddExtension=true`, `CreatePrompt=false`, and `OverwritePrompt=true`. No other save-dialog property is explicitly set. | Sets the same seven values and returns `FileName` only when parameterless `ShowDialog()` returns `true`; otherwise returns `null`. The former single-element response consumption is represented directly as one string. |
| Calls ownerless `MessageBox.Show(Text, Caption, Button, Image, DefaultResult)` with the current call-site values `OKCancel` and default result `OK`. Livet maps `OK` to `true` and `Cancel` to `null`; both current callers then map null to false. | Calls ownerless `MessageBox.Show(text, caption, OKCancel, image, OK)` and returns true only for `MessageBoxResult.OK`. `Cancel` therefore follows the same effective false/no-op branch as before. |

The selected owner policy is rev.10 option A: the service has a parameterless constructor, stores no `Window`, uses file-dialog `ShowDialog()` without an owner argument, and uses the ownerless `MessageBox.Show` overload. No implicit owner such as `Application.Current.MainWindow` is introduced.

The file actions call parameterless `OpenFileDialog.ShowDialog()` / `SaveFileDialog.ShowDialog()`, and the confirmation action calls the `MessageBox.Show` overload without an owner parameter. In the normal path SettingsWindow is active when the dialog is raised, but Livet does not specify an owner, so a WPF owner relationship with SettingsWindow is not guaranteed. U5 uses option A from the rev.10 plan: preserve the current owner-unspecified behavior with a parameterless `SettingsDialogService`, parameterless file-dialog `ShowDialog()`, and the ownerless `MessageBox.Show` overload. Changing to option B (an explicit SettingsWindow owner) requires explicit user approval and permission for the required real-environment owner smoke covering focus, z-order, owner disabling, and focus restoration after cancel.

All five XAML actions specify `InvokeActionOnlyWhenWindowIsActive="False"`. `Messenger.Raise` invokes the matching action synchronously on the calling UI thread and returns after `Response` has been assigned.

## Unreachable window actions

The pre-U5 XAML triggers for `Window.WindowAction`, `Window.Transition`, and `Window.Transition.Child` had no application raiser. No production call invoked `WindowViewModel.Close`, `Activate`, or `Transition`; the Settings caption uses `metro:SystemButtons`. U5 removes these dead/unreachable triggers rather than preserving them through a compatibility layer.

## Command and XAML inventory

Before U4, `SettingsWindow.xaml` contained 35 `metro2:CallMethodButton` call sites. U4 replaced them with the following observable command mapping, which U5 preserves:

- Window methods without parameters: `OpenExportPathDialog`, `OpenImportPathDialog`, `ResetSettings`, and `CreateDesktop`.
- Desktop item methods without parameters: `Close`, `MoveToPrevious`, `MoveToNext`, `MoveToFirst`, `MoveToLast`, and `Switch`.
- Window method with an integer item parameter: `OpenBackgroundPathDialog` receives `{Binding Index}`.
- Four shortcut-list blocks and four mouse-list blocks each call `Add*List`, `RemoveLast*List`, and `Resize*ListToFit` with one of `SwitchToIndices`, `MoveToIndices`, `MoveToIndicesAndSwitch`, or `SwapDesktopIndices` as a string parameter.

There is no Livet `ViewModelCommand` or `ListenerCommand` consumer and no command `CanExecute` policy. Existing `IsEnabled`, `Visibility`, style, tooltip, content, and parameter bindings are the behavior to retain. U5 replaces the remaining `LivetCallMethodAction` lifecycle calls (`Initialize` and `Dispose`) with `SettingsWindow` code-behind overrides.

## Other observable versus dead paths

- Observable and retained: `Header`/`Content`, log formatting, embedded license values, notification text and computed layout properties, resource culture/resource notification, desktop ID/index/name/wallpaper projection notifications, synchronous property notification, and insertion-order application/window disposal.
- Removed through U5 as dead or unreachable: `BindableTextViewModel`/`HyperlinkViewModel`, `VirtualDesktopViewModel.CreateAll`, the unused `Livet.EventListeners` import, the three window action/transition triggers above, and unused Livet command/converter/folder-selection paths.

## Sources

- Production call sites: `UI/Bindings/SettingsWindowViewModel.cs` and `UI/SettingsWindow.xaml` in this repository.
- Livet tag `v4.0.2`: `OpenFileDialogInteractionMessageAction`, `SaveFileDialogInteractionMessageAction`, `ConfirmationDialogInteractionMessageAction`, `FileSelectionMessage`, `OpeningFileSelectionMessage`, `SavingFileSelectionMessage`, and `ConfirmationMessage`.
- Public message defaults are executable characterization in `LivetDialogMessageCharacterizationTests.cs`.
