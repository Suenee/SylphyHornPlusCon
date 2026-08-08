namespace SylphyHorn.Services
{
	internal static class NotificationTextFormatter
	{
		internal static string CreateResidentHeader(bool simple) => simple ? string.Empty : "Virtual Desktop";
		internal static string CreateSwitchedHeader(bool simple) => simple ? string.Empty : "Virtual Desktop Switched";

		internal static string CreateMovedHeader(int oldNumber, int newNumber, bool simple)
			=> simple ? $"Desktop {oldNumber} => Desktop {newNumber}" : $"Desktop {oldNumber} Moved to Desktop {newNumber}";

		internal static string CreateDesktopBody(int number, string name, bool useDesktopName, bool simple, bool moved)
		{
			if (!useDesktopName || string.IsNullOrEmpty(name))
			{
				var prefix = simple ? string.Empty : moved ? "Reordered Current Desktop: " : "Current Desktop: ";
				return prefix + "Desktop " + number;
			}
			if (simple) return $"{number}. {name}";
			return moved ? $"Reordered Desktop {number}: {name}" : $"Desktop {number}: {name}";
		}

		internal static string CreatePinHeader(bool simple) => simple ? string.Empty : "Virtual Desktop";

		internal static string CreatePinBody(PinOperations operation, bool simple)
		{
			var target = operation.HasFlag(PinOperations.Window) ? "window" : "application";
			var action = operation.HasFlag(PinOperations.Pin) ? "Pinned" : "Unpinned";
			return simple
				? $"{char.ToUpperInvariant(target[0])}{target.Substring(1)} {action}"
				: $"{action} this {target}";
		}
	}
}
