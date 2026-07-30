using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using SylphyHorn.Interop;

namespace SylphyHorn.SingleInstanceTestHost
{
	internal static class Program
	{
		private const int SuccessExitCode = 0;
		private const int NotAcquiredExitCode = 10;
		private const int FailureExitCode = 20;

		private static int Main(string[] args)
		{
			try
			{
				var options = ParseOptions(args);
				var timeout = TimeSpan.FromMilliseconds(int.Parse(options["timeout-ms"], CultureInfo.InvariantCulture));
				var holdTimeout = TimeSpan.FromMilliseconds(int.Parse(options["hold-timeout-ms"], CultureInfo.InvariantCulture));
				var readyPath = Path.GetFullPath(options["ready-file"]);
				var resultPath = Path.GetFullPath(options["result-file"]);
				var releasePath = Path.GetFullPath(options["release-file"]);

				WriteSignal(readyPath, $"READY{Environment.NewLine}PID={Process.GetCurrentProcess().Id}");

				using (var instance = new SingleInstance(typeof(Program).Assembly, timeout))
				{
					if (!instance.IsFirst)
					{
						WriteSignal(resultPath, $"NOT_ACQUIRED{Environment.NewLine}MUTEX_NAME={instance.MutexName}");
						return NotAcquiredExitCode;
					}

					WriteSignal(resultPath, $"ACQUIRED{Environment.NewLine}MUTEX_NAME={instance.MutexName}");
					if (!WaitForRelease(releasePath, holdTimeout))
					{
						Console.Error.WriteLine($"Release instruction was not received within {holdTimeout}.");
						return FailureExitCode;
					}
				}

				return SuccessExitCode;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				return FailureExitCode;
			}
		}

		private static Dictionary<string, string> ParseOptions(string[] args)
		{
			var options = new Dictionary<string, string>(StringComparer.Ordinal);
			for (var index = 0; index < args.Length; index += 2)
			{
				if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
				{
					throw new ArgumentException("Options must be supplied as --name value pairs.");
				}

				options.Add(args[index].Substring(2), args[index + 1]);
			}

			return options;
		}

		private static void WriteSignal(string path, string content)
		{
			var directory = Path.GetDirectoryName(path);
			Directory.CreateDirectory(directory);
			var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(temporaryPath, content);
				File.Move(temporaryPath, path);
			}
			finally
			{
				if (File.Exists(temporaryPath))
				{
					File.Delete(temporaryPath);
				}
			}
		}

		private static bool WaitForRelease(string releasePath, TimeSpan timeout)
		{
			if (File.Exists(releasePath))
			{
				return true;
			}

			var directory = Path.GetDirectoryName(releasePath);
			var fileName = Path.GetFileName(releasePath);
			using (var released = new ManualResetEventSlim())
			using (var watcher = new FileSystemWatcher(directory, fileName))
			{
				watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
				watcher.Created += (sender, args) => released.Set();
				watcher.EnableRaisingEvents = true;

				return File.Exists(releasePath) || released.Wait(timeout);
			}
		}
	}
}
