# Settings dialog contract

## Scope

[`ISettingsDialogService`](../SylphyHorn/UI/SettingsDialogService.cs) is the port through which the settings UI requests file selection and confirmation. [`SettingsDialogContractTests`](SettingsDialogContractTests.cs) fixes only the public interface shape: method names, parameter types, and return types.

Dialog options, owner policy, and success or cancellation result mappings are maintained by this document and production source review. The interface-shape tests do not execute dialogs and do not prove these observable behaviors.

## Owner policy

- `SettingsDialogService` is parameterless and does not store a `Window` or owner.
- `OpenFileDialog.ShowDialog()` is called without an owner argument.
- `SaveFileDialog.ShowDialog()` is called without an owner argument.
- Confirmation uses the `MessageBox.Show` overload without an owner argument.
- Adding an owner can affect focus, z-order, and modal ownership. Such a change is an observable UX change, not an internal implementation detail.

## Open file dialog

The inputs are copied without transformation:

| Input | Dialog property |
|---|---|
| `fileName` | `FileName` |
| `initialDirectory` | `InitialDirectory` |
| `filter` | `Filter` |
| `title` | `Title` |

The fixed options are:

- `AddExtension = true`
- `Multiselect = false`

When `ShowDialog() == true`, the service returns `FileNames` in its original order. When the result is `false` or `null`, it returns `null`. Cancellation is not converted to an empty array, and the return type remains `string[]` even when only one file can be selected.

## Save file dialog

The inputs are copied without transformation:

| Input | Dialog property |
|---|---|
| `fileName` | `FileName` |
| `initialDirectory` | `InitialDirectory` |
| `filter` | `Filter` |
| `title` | `Title` |

The fixed options are:

- `AddExtension = true`
- `CreatePrompt = false`
- `OverwritePrompt = true`

When `ShowDialog() == true`, the service returns `FileName`. When the result is `false` or `null`, it returns `null`. Cancellation is not converted to an empty string.

## Confirmation dialog

The service passes `text` as the message text, `caption` as the caption, and `image` without transformation. It uses `MessageBoxButton.OKCancel` and `MessageBoxResult.OK` as the default result.

The service returns `true` only for `MessageBoxResult.OK`. Every other result, including `Cancel`, maps to `false`.

## Change discipline

- Changes to the fixed options, owner policy, or success and cancellation mappings are observable UX changes.
- Passing the interface-shape tests alone is not proof of behavioral compatibility.
- Do not add test-only production APIs, dialog factories, wrappers, or reflection seams.
- This U7 review correction does not change production code.
