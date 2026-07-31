#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
		private const int ProcessTimeoutMilliseconds = 30000;

		[Fact(Timeout = 120000)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.InteractiveDesktop)]
		public async Task GeneratedInterfaceAssemblyMissHitAndCorruptionRecoveryWorkAcrossProcesses()
		{
			var cacheDirectory = Path.Combine(
				Path.GetTempPath(),
				$"SylphyHornPlus-VirtualDesktopCache-{Guid.NewGuid():N}");
			Directory.CreateDirectory(cacheDirectory);

			try
			{
				var miss = await RunTestHost(cacheDirectory);
				var assemblyPath = miss["ASSEMBLY_PATH"];
				Assert.True(File.Exists(assemblyPath));
				Assert.Equal("true", miss["COM_INITIALIZED"]);
				var missFile = ReadFileState(assemblyPath);

				var hit = await RunTestHost(cacheDirectory);
				Assert.Equal(assemblyPath, hit["ASSEMBLY_PATH"]);
				Assert.Equal(miss["INTERFACE_BUILD"], hit["INTERFACE_BUILD"]);
				Assert.Equal(missFile, ReadFileState(assemblyPath));

				File.WriteAllText(assemblyPath, "not a managed assembly", Encoding.UTF8);
				var corruptFile = ReadFileState(assemblyPath);

				var recovered = await RunTestHost(cacheDirectory);
				var recoveredFile = ReadFileState(assemblyPath);
				Assert.Equal(assemblyPath, recovered["ASSEMBLY_PATH"]);
				Assert.Equal(miss["INTERFACE_BUILD"], recovered["INTERFACE_BUILD"]);
				Assert.NotEqual(corruptFile.Hash, recoveredFile.Hash);
				Assert.True(recoveredFile.Length > corruptFile.Length);
				Assert.Equal("true", recovered["COM_INITIALIZED"]);
			}
			finally
			{
				await DeleteDirectoryAsync(cacheDirectory);
			}
		}

		[WpfFact(Timeout = 30000)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.InteractiveDesktop)]
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

		private static async Task DeleteDirectoryAsync(string path)
		{
			Exception lastException = null;
			for (var attempt = 0; attempt < 5; attempt++)
			{
				try
				{
					Directory.Delete(path, recursive: true);
					return;
				}
				catch (IOException ex)
				{
					lastException = ex;
				}
				catch (UnauthorizedAccessException ex)
				{
					lastException = ex;
				}

				await Task.Delay(100);
			}

			Assert.False(
				Directory.Exists(path),
				$"Temporary COM cache remains at '{path}': {lastException}");
		}

		private static async Task<IDictionary<string, string>> RunTestHost(string cacheDirectory)
		{
			var targetPath = File.ReadAllText(
				Path.Combine(AppContext.BaseDirectory, "VirtualDesktopInteropTestHost.path")).Trim();
			var startInfo = new ProcessStartInfo
			{
				FileName = string.Equals(Path.GetExtension(targetPath), ".dll", StringComparison.OrdinalIgnoreCase)
					? "dotnet"
					: targetPath,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			if (string.Equals(Path.GetExtension(targetPath), ".dll", StringComparison.OrdinalIgnoreCase))
			{
				startInfo.ArgumentList.Add(targetPath);
			}
			startInfo.ArgumentList.Add(cacheDirectory);

			using (var process = Process.Start(startInfo))
			{
				Assert.NotNull(process);
				var standardOutput = process.StandardOutput.ReadToEndAsync();
				var standardError = process.StandardError.ReadToEndAsync();
				var waitForExit = process.WaitForExitAsync();
				var completed = await Task.WhenAny(
					waitForExit,
					Task.Delay(ProcessTimeoutMilliseconds));
				if (completed != waitForExit)
				{
					process.Kill(entireProcessTree: true);
					await process.WaitForExitAsync();
					throw new TimeoutException($"TestHost PID {process.Id} did not exit.");
				}

				var output = await standardOutput;
				var error = await standardError;
				Assert.True(process.ExitCode == 0, error);
				return ParseOutput(output);
			}
		}

		private static IDictionary<string, string> ParseOutput(string output)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var line in output.Split(
				new[] { "\r\n", "\n" },
				StringSplitOptions.RemoveEmptyEntries))
			{
				var separator = line.IndexOf('=');
				Assert.True(separator > 0, $"Unexpected TestHost output: {line}");
				result.Add(line.Substring(0, separator), line.Substring(separator + 1));
			}

			return result;
		}

		private static FileState ReadFileState(string path)
		{
			var file = new FileInfo(path);
			using (var algorithm = SHA256.Create())
			using (var stream = file.OpenRead())
			{
				var hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
				file.Refresh();
				return new FileState(hash, file.Length, file.LastWriteTimeUtc);
			}
		}

		private sealed class FileState : IEquatable<FileState>
		{
			internal FileState(string hash, long length, DateTime lastWriteTimeUtc)
			{
				this.Hash = hash;
				this.Length = length;
				this.LastWriteTimeUtc = lastWriteTimeUtc;
			}

			internal string Hash { get; }

			internal long Length { get; }

			internal DateTime LastWriteTimeUtc { get; }

			public bool Equals(FileState other)
			{
				return other != null
					&& string.Equals(this.Hash, other.Hash, StringComparison.Ordinal)
					&& this.Length == other.Length
					&& this.LastWriteTimeUtc == other.LastWriteTimeUtc;
			}

			public override bool Equals(object obj)
			{
				return this.Equals(obj as FileState);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					var hashCode = this.Hash.GetHashCode();
					hashCode = (hashCode * 397) ^ this.Length.GetHashCode();
					hashCode = (hashCode * 397) ^ this.LastWriteTimeUtc.GetHashCode();
					return hashCode;
				}
			}
		}
	}
}
#endif
