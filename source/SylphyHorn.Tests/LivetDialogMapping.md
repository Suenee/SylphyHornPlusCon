# Livet 4.0.2 dialog and binding characterization

This is the U0 input for the later U5 dialog replacement. It records current production call sites and Livet 4.0.2 behavior; it does not define or add a production API.

## Dialog action mapping

| Production call | Message key | XAML action | Properties copied by Livet 4.0.2 | Response and cancel | Current consumer |
|---|---|---|---|---|---|
| `OpenBackgroundPathDialog(int)` | `Window.OpenBackgroundImagesDialog.Open` | `OpenFileDialogInteractionMessageAction` | `FileName`, `InitialDirectory`, `AddExtension`, `Filter`, `Title`, `Multiselect` | success: `OpenFileDialog.FileNames`; cancel/indeterminate: `null` | Requires a non-empty first path and `File.Exists`; then updates the folder and desktop wallpaper path. Current call supplies title, current background folder, supported wallpaper filter, and `MultiSelect=false`; `FileName` remains empty and `AddExtension=true`. |
| `OpenImportPathDialog()` | `Window.OpenImportPathDialog.Open` | `OpenFileDialogInteractionMessageAction` | Same open-file mapping | Same open-file response | Requires a non-empty first path; then preserves suspend/prepare/optional confirmation/commit/notify/finally-resume ordering. Current call also supplies `LocalSettingsProvider.Filename`. |
| `OpenExportPathDialog()` | `Window.OpenExportPathDialog.Open` | `SaveFileDialogInteractionMessageAction` | `FileName`, `InitialDirectory`, `AddExtension`, `CreatePrompt`, `Filter`, `OverwritePrompt`, `Title` | success: `SaveFileDialog.FileNames`; cancel/indeterminate: `null` | Requires a non-empty first path; then updates the remembered folder and calls `ExportAsync`. Current defaults are `AddExtension=true`, `CreatePrompt=false`, and `OverwritePrompt=true`. |
| `ResetSettings()` | `Window.ResetSettingsDialog.Confirm` | `ConfirmationDialogInteractionMessageAction` | `Text`, `Caption`, `Button`, `Image`, `DefaultResult` | `OK`/`Yes` -> `true`; `Cancel` -> `null`; all other results -> `false`. The VM maps null to false. | Warning icon, `OKCancel`, default result `OK`; false/null returns without reset. |
| import desktop override | `Window.OverrideDesktopsDialog.Confirm` | `ConfirmationDialogInteractionMessageAction` | Same confirmation mapping | Same confirmation response; the VM maps null to false. | Question icon, `OKCancel`, default result `OK`; value is passed to `CommitPreparedImportAsync`. |

The file actions call parameterless `OpenFileDialog.ShowDialog()` / `SaveFileDialog.ShowDialog()`, and the confirmation action calls the `MessageBox.Show` overload without an owner parameter. In the normal path SettingsWindow is active when the dialog is raised, but Livet does not specify an owner, so a WPF owner relationship with SettingsWindow is not guaranteed. U5 defaults to option A from the rev.10 plan: preserve the current owner-unspecified behavior with a parameterless `SettingsDialogService`, parameterless file-dialog `ShowDialog()`, and the ownerless `MessageBox.Show` overload. Changing to option B (an explicit SettingsWindow owner) requires explicit user approval and permission for the required real-environment owner smoke covering focus, z-order, owner disabling, and focus restoration after cancel.

All five XAML actions specify `InvokeActionOnlyWhenWindowIsActive="False"`. `Messenger.Raise` invokes the matching action synchronously on the calling UI thread and returns after `Response` has been assigned.

## Unreachable window actions

The XAML triggers for `Window.WindowAction`, `Window.Transition`, and `Window.Transition.Child` have no application raiser. No production call invokes `WindowViewModel.Close`, `Activate`, or `Transition`; the Settings caption uses `metro:SystemButtons`. These triggers are dead/unreachable behavior and are intentionally removed later rather than preserved through a compatibility layer.

## Command and XAML inventory

`SettingsWindow.xaml` contains 35 `metro2:CallMethodButton` call sites. The observable command mapping to preserve is:

- Window methods without parameters: `OpenExportPathDialog`, `OpenImportPathDialog`, `ResetSettings`, and `CreateDesktop`.
- Desktop item methods without parameters: `Close`, `MoveToPrevious`, `MoveToNext`, `MoveToFirst`, `MoveToLast`, and `Switch`.
- Window method with an integer item parameter: `OpenBackgroundPathDialog` receives `{Binding Index}`.
- Four shortcut-list blocks and four mouse-list blocks each call `Add*List`, `RemoveLast*List`, and `Resize*ListToFit` with one of `SwitchToIndices`, `MoveToIndices`, `MoveToIndicesAndSwitch`, or `SwapDesktopIndices` as a string parameter.

There is no Livet `ViewModelCommand` or `ListenerCommand` consumer and no command `CanExecute` policy. Existing `IsEnabled`, `Visibility`, style, tooltip, content, and parameter bindings are the behavior to retain. `LivetCallMethodAction` remains separately used for window lifecycle (`Initialize` and `Dispose`) until the later lifecycle unit.

## Other observable versus dead paths

- Observable and retained: `Header`/`Content`, log formatting, embedded license values, notification text and computed layout properties, resource culture/resource notification, desktop ID/index/name/wallpaper projection notifications, synchronous property notification, and insertion-order application/window disposal.
- Intentionally removed later as dead or unreachable: `BindableTextViewModel`/`HyperlinkViewModel`, `VirtualDesktopViewModel.CreateAll`, unused `Livet.EventListeners` import, the three window action/transition triggers above, and unused Livet command/converter/folder-selection paths.

## Sources

- Production call sites: `UI/Bindings/SettingsWindowViewModel.cs` and `UI/SettingsWindow.xaml` in this repository.
- Livet tag `v4.0.2`: `OpenFileDialogInteractionMessageAction`, `SaveFileDialogInteractionMessageAction`, `ConfirmationDialogInteractionMessageAction`, `FileSelectionMessage`, `OpeningFileSelectionMessage`, `SavingFileSelectionMessage`, and `ConfirmationMessage`.
- Public message defaults are executable characterization in `LivetDialogMessageCharacterizationTests.cs`.
