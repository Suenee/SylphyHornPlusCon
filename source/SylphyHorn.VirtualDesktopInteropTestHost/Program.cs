using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using WindowsDesktop;

namespace SylphyHorn.VirtualDesktopInteropTestHost
{
	internal static class Program
	{
		private const int FailureExitCode = 20;

		private static async Task<int> Main(string[] args)
		{
			if (args.Length != 1)
			{
				Console.Error.WriteLine("Usage: SylphyHorn.VirtualDesktopInteropTestHost <cache-directory>");
				return FailureExitCode;
			}

			var cacheDirectory = Path.GetFullPath(args[0]);
			var provider = new VirtualDesktopProvider
			{
				AutoRestart = false,
				ComInterfaceAssemblyPath = cacheDirectory,
			};

			try
			{
				VirtualDesktop.Provider = provider;
				await provider.Initialize(TaskScheduler.Default);

				var osBuild = Environment.OSVersion.Version.Build;
				var assemblyPath = Path.Combine(
					cacheDirectory,
					$"VirtualDesktop.{osBuild.ToString(CultureInfo.InvariantCulture)}.generated.dll");
				var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);

				Console.WriteLine($"OS_BUILD={osBuild.ToString(CultureInfo.InvariantCulture)}");
				Console.WriteLine($"ASSEMBLY_PATH={assemblyPath}");
				Console.WriteLine($"ASSEMBLY_VERSION={assemblyName.Version}");
				Console.WriteLine($"INTERFACE_BUILD={assemblyName.Version.Revision.ToString(CultureInfo.InvariantCulture)}");
				Console.WriteLine("COM_INITIALIZED=true");
				return 0;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				return FailureExitCode;
			}
			finally
			{
				provider.Dispose();
				VirtualDesktop.Provider = null;
			}
		}
	}
}
