using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SylphyHorn.Services
{
	internal sealed class VppDispatchResult
	{
		private VppDispatchResult(bool success, object result, string errorCode, string errorMessage, object errorDetails)
		{
			this.Success = success;
			this.Result = result;
			this.ErrorCode = errorCode;
			this.ErrorMessage = errorMessage;
			this.ErrorDetails = errorDetails;
		}

		internal bool Success { get; }
		internal object Result { get; }
		internal string ErrorCode { get; }
		internal string ErrorMessage { get; }
		internal object ErrorDetails { get; }

		internal static VppDispatchResult Ok(object result) => new VppDispatchResult(true, result, null, null, null);
		internal static VppDispatchResult Fail(string code, string message, object details = null) => new VppDispatchResult(false, null, code, message, details);
	}

	internal sealed class VppDesktopAdapter
	{
		private readonly IDesktopControlService _desktopControl;

		internal VppDesktopAdapter(IDesktopControlService desktopControl)
		{
			this._desktopControl = desktopControl ?? throw new ArgumentNullException(nameof(desktopControl));
		}

		internal async Task<VppDispatchResult> DispatchAsync(string method, JsonElement args)
		{
			if (string.IsNullOrWhiteSpace(method)) return VppDispatchResult.Fail("INVALID_MESSAGE", "Call method is required.");
			if (args.ValueKind != JsonValueKind.Object) return VppDispatchResult.Fail("INVALID_ARGUMENT", "args must be a JSON object.");

			switch (method)
			{
				case "activateDesktop":
					return await this.ActivateDesktopAsync(args).ConfigureAwait(false);
				case "addDesktop":
					return await this.AddDesktopAsync(args).ConfigureAwait(false);
				case "setIndividualWallpapers":
					return await this.SetIndividualWallpapersAsync(args).ConfigureAwait(false);
				case "setDesktopWallpaper":
					return await this.SetDesktopWallpaperAsync(args).ConfigureAwait(false);
				case "getDesktopState":
					return GetState(args, this._desktopControl);
				case "getDesktop":
					return GetDesktop(args, this._desktopControl);
				default:
					return VppDispatchResult.Fail("UNKNOWN_METHOD", $"Unknown SHPC VPP method '{method}'.");
			}
		}

		internal object CreateStateEventArgs(DesktopSystemState state)
		{
			state ??= this._desktopControl.GetState();
			var current = state.Desktops.FirstOrDefault(item => item.IsCurrent);
			return new
			{
				enabled = state.Enabled,
				individualWallpapersEnabled = state.IndividualWallpapersEnabled,
				currentDesktopId = state.CurrentDesktopId?.ToString("D") ?? string.Empty,
				currentCName = state.CurrentCName ?? string.Empty,
				currentTitle = current?.Title ?? string.Empty,
				currentPosition = state.CurrentPosition,
				desktopCount = state.Desktops.Count,
				desktopsJson = JsonSerializer.Serialize(state.Desktops.Select(ToDesktopDto).ToArray()),
			};
		}

		private async Task<VppDispatchResult> ActivateDesktopAsync(JsonElement args)
		{
			if (!TryReadSelector(args, out var selector, out var error)) return error;
			if (!HasOnlyKeys(args, "cname", "id", "position")) return UnknownArgument();
			return Translate(await this._desktopControl.ActivateAsync(selector).ConfigureAwait(false), state => new { desktop = ToDesktopDto(state) });
		}

		private async Task<VppDispatchResult> AddDesktopAsync(JsonElement args)
		{
			if (!HasOnlyKeys(args, "position")) return UnknownArgument();
			var position = 0;
			if (args.TryGetProperty("position", out var value))
			{
				if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out position)) return VppDispatchResult.Fail("INVALID_ARGUMENT", "position must be an integer.");
			}
			return Translate(await this._desktopControl.AddAsync(position).ConfigureAwait(false), state => new { desktop = ToDesktopDto(state) });
		}

		private async Task<VppDispatchResult> SetIndividualWallpapersAsync(JsonElement args)
		{
			if (!HasOnlyKeys(args, "state")) return UnknownArgument();
			if (!TryReadToggle(args, out var action, out var error)) return error;
			return Translate(await this._desktopControl.SetGlobalWallpaperModeAsync(action).ConfigureAwait(false), state => new { enabled = state.Enabled });
		}

		private async Task<VppDispatchResult> SetDesktopWallpaperAsync(JsonElement args)
		{
			if (!HasOnlyKeys(args, "cname", "id", "position", "state")) return UnknownArgument();
			if (!TryReadSelector(args, out var selector, out var selectorError)) return selectorError;
			if (!TryReadToggle(args, out var action, out var toggleError)) return toggleError;
			return Translate(await this._desktopControl.SetDesktopWallpaperModeAsync(selector, action).ConfigureAwait(false), state => new { desktop = ToDesktopWallpaperDto(state) });
		}

		private static VppDispatchResult GetState(JsonElement args, IDesktopControlService desktopControl)
		{
			if (!HasOnlyKeys(args)) return UnknownArgument();
			return VppDispatchResult.Ok(new { state = ToSystemStateDto(desktopControl.GetState()) });
		}

		private static VppDispatchResult GetDesktop(JsonElement args, IDesktopControlService desktopControl)
		{
			if (!HasOnlyKeys(args, "cname", "id", "position")) return UnknownArgument();
			if (!TryReadSelector(args, out var selector, out var error)) return error;
			var desktop = desktopControl.GetDesktop(selector);
			return desktop == null
				? VppDispatchResult.Fail("INVALID_ARGUMENT", "The requested desktop does not exist.")
				: VppDispatchResult.Ok(new { desktop = ToDesktopDto(desktop) });
		}

		private static bool TryReadSelector(JsonElement args, out DesktopSelector selector, out VppDispatchResult error)
		{
			selector = new DesktopSelector();
			error = null;
			var selected = 0;

			if (args.TryGetProperty("cname", out var cname))
			{
				if (cname.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cname.GetString()))
				{
					error = VppDispatchResult.Fail("INVALID_ARGUMENT", "cname must be a non-empty string.");
					return false;
				}
				selector.CName = cname.GetString().Trim();
				selected++;
			}

			if (args.TryGetProperty("id", out var id))
			{
				if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var parsed))
				{
					error = VppDispatchResult.Fail("INVALID_ARGUMENT", "id must be a valid desktop GUID.");
					return false;
				}
				selector.Id = parsed;
				selected++;
			}

			if (args.TryGetProperty("position", out var position))
			{
				if (position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out var parsed) || parsed < 1)
				{
					error = VppDispatchResult.Fail("INVALID_ARGUMENT", "position must be a one-based positive integer.");
					return false;
				}
				selector.Position = parsed;
				selected++;
			}

			if (selected != 1)
			{
				error = VppDispatchResult.Fail("INVALID_ARGUMENT", "Exactly one desktop selector must be supplied: cname, id, or position.");
				return false;
			}
			return true;
		}

		private static bool TryReadToggle(JsonElement args, out ToggleAction action, out VppDispatchResult error)
		{
			action = ToggleAction.Toggle;
			error = null;
			if (!args.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String)
			{
				error = VppDispatchResult.Fail("INVALID_ARGUMENT", "state must be on, off, or toggle.");
				return false;
			}
			switch (state.GetString())
			{
				case "on": action = ToggleAction.On; return true;
				case "off": action = ToggleAction.Off; return true;
				case "toggle": action = ToggleAction.Toggle; return true;
				default:
					error = VppDispatchResult.Fail("INVALID_ARGUMENT", "state must be on, off, or toggle.");
					return false;
			}
		}

		private static VppDispatchResult Translate<T>(CommandResult<T> result, Func<T, object> selector)
		{
			if (result == null) return VppDispatchResult.Fail("COMMAND_FAILED", "The SHPC command returned no result.");
			if (result.Success) return VppDispatchResult.Ok(selector(result.State));
			var details = new { serviceErrorCode = result.ErrorCode ?? string.Empty, state = result.State };
			return VppDispatchResult.Fail(MapErrorCode(result.ErrorCode), result.Message ?? "The SHPC command failed.", details);
		}

		private static string MapErrorCode(string serviceErrorCode)
		{
			if (string.IsNullOrWhiteSpace(serviceErrorCode)) return "COMMAND_FAILED";
			if (serviceErrorCode.StartsWith("invalid_", StringComparison.OrdinalIgnoreCase) || serviceErrorCode.EndsWith("_not_found", StringComparison.OrdinalIgnoreCase)) return "INVALID_ARGUMENT";
			return "COMMAND_FAILED";
		}

		private static VppDispatchResult UnknownArgument() => VppDispatchResult.Fail("UNKNOWN_ARGUMENT", "The call contains an unsupported argument.");

		private static bool HasOnlyKeys(JsonElement args, params string[] allowed)
		{
			foreach (var property in args.EnumerateObject())
			{
				if (!allowed.Contains(property.Name, StringComparer.Ordinal)) return false;
			}
			return true;
		}

		private static object ToSystemStateDto(DesktopSystemState state)
			=> new
			{
				enabled = state.Enabled,
				individualWallpapersEnabled = state.IndividualWallpapersEnabled,
				currentDesktopId = state.CurrentDesktopId?.ToString("D") ?? string.Empty,
				currentCName = state.CurrentCName ?? string.Empty,
				currentPosition = state.CurrentPosition,
				desktops = state.Desktops.Select(ToDesktopDto).ToArray(),
			};

		private static object ToDesktopDto(DesktopState state)
			=> state == null ? null : new
			{
				id = state.Id.ToString("D"),
				cname = state.CName ?? string.Empty,
				title = state.Title ?? string.Empty,
				position = state.Position,
				isCurrent = state.IsCurrent,
				individualWallpaperEnabled = state.IndividualWallpaperEnabled,
				wallpaperPath = state.WallpaperPath ?? string.Empty,
				wallpaperPosition = state.WallpaperPosition.ToString(),
			};

		private static object ToDesktopWallpaperDto(DesktopWallpaperState state)
			=> state == null ? null : new
			{
				id = state.Id.ToString("D"),
				cname = state.CName ?? string.Empty,
				position = state.Position,
				enabled = state.Enabled,
				wallpaperPath = state.WallpaperPath ?? string.Empty,
				wallpaperPosition = state.WallpaperPosition.ToString(),
			};
	}
}
