#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WindowsDesktop;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	[Collection(WindowsHookCollection.Name)]
	public sealed class VirtualDesktopInteropIntegrationTests
	{
		[WpfFact(Timeout = 30000)]
		public async Task IsPinnedWindowMarshalsApplicationView()
		{
			var cacheDirectory = Path.Combine(
				Path.GetTempPath(),
				$"SylphyHornPlus-VirtualDesktopInterop-{Guid.NewGuid():N}");
			var provider = new VirtualDesktopProvider
			{
				AutoRestart = false,
				ComInterfaceAssemblyPath = cacheDirectory,
			};
			var window = new Window
			{
				ShowActivated = false,
				ShowInTaskbar = false,
				WindowStyle = WindowStyle.ToolWindow,
				Width = 1,
				Height = 1,
			};

			try
			{
				VirtualDesktop.Provider = provider;
				await provider.Initialize();

				window.Show();
				var handle = new WindowInteropHelper(window).Handle;

				_ = VirtualDesktop.IsPinnedWindow(handle);
			}
			finally
			{
				window.Close();
				provider.Dispose();
				VirtualDesktop.Provider = null;

				try
				{
					Directory.Delete(cacheDirectory, recursive: true);
				}
				catch (IOException)
				{
					// The generated assembly remains loaded until the test process exits.
				}
				catch (UnauthorizedAccessException)
				{
					// The generated assembly remains loaded until the test process exits.
				}
			}
		}
	}
}
#endif
