#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	[Collection(WindowsHookCollection.Name)]
	public sealed class SingleInstanceIntegrationTests
	{
		private const int ProcessTimeoutMilliseconds = 10000;
		private const string TestHostGuid = "8F10182E-1C1B-4C65-9E8D-5C019E847B5E";

		[Fact(Timeout = 30000)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task RestartWaitsForOwnershipAndNoNonOwnerRemainsResident()
		{
			var directory = Path.Combine(Path.GetTempPath(), $"SylphyHornPlus-SingleInstance-{Guid.NewGuid():N}");
			Directory.CreateDirectory(directory);
			var children = new List<TestHostProcess>();

			try
			{
				var owner = StartHost(directory, children, TimeSpan.Zero);
				await owner.WaitForReady();
				var ownerResult = await owner.WaitForResult();
				AssertResult(ownerResult, "ACQUIRED");
				AssertTestMutexIsIsolated(ownerResult);

				var restarted = StartHost(directory, children, TimeSpan.FromSeconds(5));
				await restarted.WaitForReady();
				Assert.False(await restarted.ResultAppearsWithin(TimeSpan.FromMilliseconds(300)));

				owner.Release();
				Assert.Equal(0, await owner.WaitForExit());

				var restartedResult = await restarted.WaitForResult();
				AssertResult(restartedResult, "ACQUIRED");

				var third = StartHost(directory, children, TimeSpan.Zero);
				await third.WaitForReady();
				AssertResult(await third.WaitForResult(), "NOT_ACQUIRED");
				Assert.Equal(10, await third.WaitForExit());

				restarted.Release();
				Assert.Equal(0, await restarted.WaitForExit());

				var next = StartHost(directory, children, TimeSpan.Zero);
				await next.WaitForReady();
				AssertResult(await next.WaitForResult(), "ACQUIRED");
				next.Release();
				Assert.Equal(0, await next.WaitForExit());
			}
			finally
			{
				await Cleanup(children, directory);
			}
		}

		[Fact(Timeout = 30000)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task TimedOutWaiterExitsWithoutOwnership()
		{
			var directory = Path.Combine(Path.GetTempPath(), $"SylphyHornPlus-SingleInstance-{Guid.NewGuid():N}");
			Directory.CreateDirectory(directory);
			var children = new List<TestHostProcess>();

			try
			{
				var owner = StartHost(directory, children, TimeSpan.Zero);
				await owner.WaitForReady();
				AssertResult(await owner.WaitForResult(), "ACQUIRED");

				var waiter = StartHost(directory, children, TimeSpan.FromMilliseconds(250));
				await waiter.WaitForReady();
				AssertResult(await waiter.WaitForResult(), "NOT_ACQUIRED");
				Assert.Equal(10, await waiter.WaitForExit());
				Assert.True(waiter.Process.HasExited);

				owner.Release();
				Assert.Equal(0, await owner.WaitForExit());
			}
			finally
			{
				await Cleanup(children, directory);
			}
		}

		private static TestHostProcess StartHost(
			string directory,
			ICollection<TestHostProcess> children,
			TimeSpan acquireTimeout)
		{
			var child = TestHostProcess.Start(directory, acquireTimeout, TimeSpan.FromSeconds(10));
			children.Add(child);
			return child;
		}

		private static void AssertResult(string result, string expected)
		{
			Assert.StartsWith(expected + Environment.NewLine, result, StringComparison.Ordinal);
		}

		private static void AssertTestMutexIsIsolated(string result)
		{
			var testMutexName = $"MetroTrilithon.Desktop.ApplicationInstance_{TestHostGuid}";
			var productionGuid = typeof(Application).Assembly.GetCustomAttribute<GuidAttribute>().Value;
			var productionMutexName = $"MetroTrilithon.Desktop.ApplicationInstance_{productionGuid}";

			Assert.Contains($"MUTEX_NAME={testMutexName}", result, StringComparison.OrdinalIgnoreCase);
			Assert.False(string.Equals(productionMutexName, testMutexName, StringComparison.OrdinalIgnoreCase));
		}

		private static async Task Cleanup(IEnumerable<TestHostProcess> children, string directory)
		{
			foreach (var child in children)
			{
				try
				{
					child.Release();
					if (!child.Process.HasExited)
					{
						var waitForExit = child.Process.WaitForExitAsync();
						var exited = await Task.WhenAny(
							waitForExit,
							Task.Delay(ProcessTimeoutMilliseconds));
						if (exited != waitForExit)
						{
							child.Process.Kill(entireProcessTree: true);
							await child.Process.WaitForExitAsync();
						}
					}
				}
				finally
				{
					child.Dispose();
				}
			}

			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}

		private sealed class TestHostProcess : IDisposable
		{
			private readonly string _readyPath;
			private readonly string _resultPath;
			private readonly string _releasePath;

			private TestHostProcess(Process process, string readyPath, string resultPath, string releasePath)
			{
				this.Process = process;
				this._readyPath = readyPath;
				this._resultPath = resultPath;
				this._releasePath = releasePath;
			}

			internal Process Process { get; }

			internal static TestHostProcess Start(string directory, TimeSpan acquireTimeout, TimeSpan holdTimeout)
			{
				var id = Guid.NewGuid().ToString("N");
				var readyPath = Path.Combine(directory, $"{id}.ready");
				var resultPath = Path.Combine(directory, $"{id}.result");
				var releasePath = Path.Combine(directory, $"{id}.release");
				var targetPath = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SingleInstanceTestHost.path")).Trim();
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
				startInfo.ArgumentList.Add("--timeout-ms");
				startInfo.ArgumentList.Add(((int)acquireTimeout.TotalMilliseconds).ToString());
				startInfo.ArgumentList.Add("--hold-timeout-ms");
				startInfo.ArgumentList.Add(((int)holdTimeout.TotalMilliseconds).ToString());
				startInfo.ArgumentList.Add("--ready-file");
				startInfo.ArgumentList.Add(readyPath);
				startInfo.ArgumentList.Add("--result-file");
				startInfo.ArgumentList.Add(resultPath);
				startInfo.ArgumentList.Add("--release-file");
				startInfo.ArgumentList.Add(releasePath);

				var process = Process.Start(startInfo);
				Assert.NotNull(process);
				return new TestHostProcess(process, readyPath, resultPath, releasePath);
			}

			internal Task WaitForReady()
			{
				return WaitForFile(this._readyPath);
			}

			internal async Task<string> WaitForResult()
			{
				await WaitForFile(this._resultPath);
				return File.ReadAllText(this._resultPath);
			}

			internal async Task<bool> ResultAppearsWithin(TimeSpan timeout)
			{
				return await FileAppearsWithin(this._resultPath, timeout);
			}

			internal void Release()
			{
				if (!File.Exists(this._releasePath))
				{
					File.WriteAllText(this._releasePath, "RELEASE");
				}
			}

			internal async Task<int> WaitForExit()
			{
				var waitForExit = this.Process.WaitForExitAsync();
				var completed = await Task.WhenAny(
					waitForExit,
					Task.Delay(ProcessTimeoutMilliseconds));
				if (completed != waitForExit)
				{
					throw new TimeoutException($"TestHost PID {this.Process.Id} did not exit.");
				}

				return this.Process.ExitCode;
			}

			private static async Task WaitForFile(string path)
			{
				if (!await FileAppearsWithin(path, TimeSpan.FromMilliseconds(ProcessTimeoutMilliseconds)))
				{
					throw new TimeoutException($"TestHost signal was not created: {path}");
				}
			}

			private static async Task<bool> FileAppearsWithin(string path, TimeSpan timeout)
			{
				var deadline = DateTime.UtcNow + timeout;
				while (DateTime.UtcNow < deadline)
				{
					if (File.Exists(path))
					{
						return true;
					}

					await Task.Delay(20);
				}

				return File.Exists(path);
			}

			public void Dispose()
			{
				this.Process.Dispose();
			}
		}
	}
}
#endif
