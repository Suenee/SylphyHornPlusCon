using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SylphyHorn.Serialization;

namespace SylphyHorn.Services
{
	internal static class BuildProfile
	{
		private static readonly string Branch = typeof(BuildProfile).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(x => string.Equals(x.Key, "GitBranch", StringComparison.OrdinalIgnoreCase))?.Value ?? "main";

		internal static bool IsDevelopmentBranch => string.Equals(Branch, "devel", StringComparison.OrdinalIgnoreCase);

		internal static bool IsVppTrafficLoggingEnabled
		{
			get
			{
				var persisted = Settings.General.VppTrafficLogging.Value;
				if (string.Equals(persisted, "on", StringComparison.OrdinalIgnoreCase)) return true;
				if (string.Equals(persisted, "off", StringComparison.OrdinalIgnoreCase)) return false;
				return IsDevelopmentBranch;
			}
		}

		internal static string GetProjectLogPath()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory != null)
			{
				if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
					return Path.Combine(directory.FullName, "logs", "SylphyHornPlusCon.log");
				directory = directory.Parent;
			}
			return Path.Combine(AppContext.BaseDirectory, "logs", "SylphyHornPlusCon.log");
		}
	}
}
