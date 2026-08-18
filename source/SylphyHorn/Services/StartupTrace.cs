using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SylphyHorn.Properties;

namespace SylphyHorn.Services
{
	internal enum StartupPhase
	{
		ProcessStart = 1,
		SingleInstance = 2,
		ShellReady = 3,
		SettingsLoaded = 4,
		ProviderInitStarted = 5,
		ProviderInitCompleted = 6,
		RuntimeInitialized = 7,
		HookStarted = 8,
		TrayShown = 9,
		ShutdownOrFailure = 10,
	}

	internal enum StartupTraceResult
	{
		None = 0,
		Succeeded = 1,
		TimedOut = 2,
		Cancelled = 3,
		Failed = 4,
		NotSupported = 5,
		DuplicateInstance = 6,
	}

	internal sealed class StartupTrace
	{
		private const int RetainedFileCount = 20;
		private static readonly DateTime _processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
		private readonly string _path;
		private readonly Func<DateTimeOffset> _utcNow;
		private readonly Func<long> _elapsedMilliseconds;
		private readonly int _processId;
		private readonly int _sessionId;
		private readonly int _osBuild;

		internal StartupTrace()
			: this(
				System.IO.Path.Combine(Directories.LocalAppData.FullName, "StartupTrace"),
				() => DateTimeOffset.UtcNow,
				() => (long)(DateTime.UtcNow - _processStartUtc).TotalMilliseconds,
				Process.GetCurrentProcess().Id,
				Process.GetCurrentProcess().SessionId,
				ProductInfo.OSBuild)
		{
		}

		internal StartupTrace(
			string directoryPath,
			Func<DateTimeOffset> utcNow,
			Func<long> elapsedMilliseconds,
			int processId,
			int sessionId,
			int osBuild)
		{
			this._utcNow = utcNow;
			this._elapsedMilliseconds = elapsedMilliseconds;
			this._processId = processId;
			this._sessionId = sessionId;
			this._osBuild = osBuild;
			this._path = System.IO.Path.Combine(directoryPath, utcNow().ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + processId + ".log");

			try
			{
				Directory.CreateDirectory(directoryPath);
				foreach (var path in Directory.EnumerateFiles(directoryPath, "*.log")
					.OrderByDescending(File.GetLastWriteTimeUtc)
					.Skip(RetainedFileCount - 1))
				{
					File.Delete(path);
				}
			}
			catch
			{
				// Startup diagnostics must never affect startup.
			}
		}

		internal string TracePath => this._path;

		internal void Write(StartupPhase phase, StartupTraceResult result = StartupTraceResult.None)
		{
			this.Write(phase, result, null, 0);
		}

		internal void Write(StartupPhase phase, StartupTraceResult result, Type exceptionType, int hresult)
		{
			try
			{
				var line = new StringBuilder()
					.Append(this._utcNow().ToString("O", CultureInfo.InvariantCulture))
					.Append(" phase=").Append(phase)
					.Append(" result=").Append((int)result)
					.Append(" elapsedMs=").Append(this._elapsedMilliseconds().ToString(CultureInfo.InvariantCulture))
					.Append(" pid=").Append(this._processId.ToString(CultureInfo.InvariantCulture))
					.Append(" sessionId=").Append(this._sessionId.ToString(CultureInfo.InvariantCulture))
					.Append(" osBuild=").Append(this._osBuild.ToString(CultureInfo.InvariantCulture));
				if (exceptionType != null)
				{
					line.Append(" exceptionType=").Append(exceptionType.Name)
						.Append(" hresult=").Append(hresult.ToString(CultureInfo.InvariantCulture));
				}

				line.AppendLine();
				File.AppendAllText(this._path, line.ToString(), new UTF8Encoding(false));
			}
			catch
			{
				// This trace shows only the last completed phase; it cannot establish a root cause.
			}
		}
	}
}
