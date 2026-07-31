using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using MetroTrilithon.Desktop;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	public class StartupShortcutIntegrationTests
	{
		[Fact]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public void ShellLinkTargetsExplicitExecutablePath()
		{
			var directory = Path.Combine(
				Path.GetTempPath(),
				$"SylphyHornPlus-StartupShortcut-{Guid.NewGuid():N}");
			Directory.CreateDirectory(directory);

			try
			{
				var shortcutPath = Path.Combine(directory, "SylphyHorn.lnk");
				var executablePath = Process.GetCurrentProcess().MainModule.FileName;

				ShellLink.Create(shortcutPath, executablePath);

				Assert.True(File.Exists(shortcutPath));
				Assert.Equal(
					Path.GetFullPath(executablePath),
					Path.GetFullPath(ReadShortcutTarget(shortcutPath)),
					StringComparer.OrdinalIgnoreCase);
			}
			finally
			{
				Directory.Delete(directory, recursive: true);
			}
		}

		private static string ReadShortcutTarget(string shortcutPath)
		{
			var shellLink = (IShellLink)new ShellLinkObject();
			try
			{
				((IPersistFile)shellLink).Load(shortcutPath, 0);

				var targetPath = new StringBuilder(32768);
				shellLink.GetPath(targetPath, targetPath.Capacity, IntPtr.Zero, 4);
				return targetPath.ToString();
			}
			finally
			{
				Marshal.FinalReleaseComObject(shellLink);
			}
		}

		[ComImport]
		[Guid("00021401-0000-0000-C000-000000000046")]
		private class ShellLinkObject
		{
		}

		[ComImport]
		[Guid("000214F9-0000-0000-C000-000000000046")]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellLink
		{
			void GetPath(
				[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
				int maximumPathLength,
				IntPtr findData,
				uint flags);
		}
	}
}
