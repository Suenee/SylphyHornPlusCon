using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public class StartupTraceTests
	{
		[Fact]
		public void WritesPhasesInOrderWithoutFreeTextFields()
		{
			var root = CreateTemporaryRoot();
			try
			{
				var elapsed = 100L;
				var trace = new StartupTrace(root, () => new DateTimeOffset(2026, 8, 19, 1, 2, 3, TimeSpan.Zero), () => elapsed += 5, 123, 7, 26100);

				trace.Write(StartupPhase.ProcessStart);
				trace.Write(StartupPhase.ShellReady, StartupTraceResult.Succeeded);
				trace.Write(StartupPhase.ShutdownOrFailure, StartupTraceResult.Failed, typeof(InvalidOperationException), unchecked((int)0x80004005));

				var lines = File.ReadAllLines(trace.TracePath);
				Assert.Equal(3, lines.Length);
				Assert.Contains("phase=ProcessStart", lines[0]);
				Assert.Contains("phase=ShellReady", lines[1]);
				Assert.Contains("phase=ShutdownOrFailure", lines[2]);
				Assert.Contains("exceptionType=InvalidOperationException", lines[2]);
				Assert.True(string.Join("", lines).IndexOf("message=", StringComparison.OrdinalIgnoreCase) < 0);
				Assert.True(string.Join("", lines).IndexOf("stack", StringComparison.OrdinalIgnoreCase) < 0);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Fact]
		public void PublicAndInternalWriteApisDoNotAcceptFreeText()
		{
			var stringParameters = typeof(StartupTrace)
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(method => method.Name == nameof(StartupTrace.Write))
				.SelectMany(method => method.GetParameters())
				.Where(parameter => parameter.ParameterType == typeof(string));

			Assert.Empty(stringParameters);
		}

		[Fact]
		public void SinkFailureDoesNotEscape()
		{
			var root = CreateTemporaryRoot();
			var fileInsteadOfDirectory = System.IO.Path.Combine(root, "blocked");
			File.WriteAllText(fileInsteadOfDirectory, "not a directory");
			try
			{
				var trace = new StartupTrace(fileInsteadOfDirectory, () => DateTimeOffset.UtcNow, () => 0, 1, 1, 1);

				var exception = Record.Exception(() => trace.Write(StartupPhase.ProcessStart));

				Assert.Null(exception);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Fact]
		public void RotationRetainsAtMostTwentyTraceFilesIncludingCurrentStartup()
		{
			var root = CreateTemporaryRoot();
			try
			{
				for (var index = 0; index < 25; index++)
				{
					var path = System.IO.Path.Combine(root, index.ToString("00") + ".log");
					File.WriteAllText(path, index.ToString());
					File.SetLastWriteTimeUtc(path, new DateTime(2026, 8, 1).AddMinutes(index));
				}

				var trace = new StartupTrace(root, () => new DateTimeOffset(2026, 8, 19, 1, 2, 3, TimeSpan.Zero), () => 0, 123, 7, 26100);
				trace.Write(StartupPhase.ProcessStart);

				Assert.Equal(20, Directory.EnumerateFiles(root, "*.log").Count());
				Assert.True(File.Exists(trace.TracePath));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static string CreateTemporaryRoot()
		{
			var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SylphyHornPlus-StartupTrace-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			return root;
		}
	}
}
