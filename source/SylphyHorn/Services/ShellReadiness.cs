using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SylphyHorn.Services
{
	internal enum ShellReadinessResult
	{
		Ready,
		TimedOut,
		Cancelled,
	}

	internal sealed class ShellReadiness
	{
		private readonly IShellReadinessProbe _probe;
		private readonly Func<TimeSpan, CancellationToken, Task> _delay;

		internal ShellReadiness()
			: this(new ShellReadinessProbe(), Task.Delay)
		{
		}

		internal ShellReadiness(IShellReadinessProbe probe, Func<TimeSpan, CancellationToken, Task> delay)
		{
			this._probe = probe ?? throw new ArgumentNullException(nameof(probe));
			this._delay = delay ?? throw new ArgumentNullException(nameof(delay));
		}

		internal async Task<ShellReadinessResult> WaitAsync(
			TimeSpan timeout,
			TimeSpan interval,
			int requiredConsecutiveObservations,
			CancellationToken cancellationToken)
		{
			if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
			if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
			if (requiredConsecutiveObservations <= 0) throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveObservations));

			var maximumObservations = (int)Math.Floor(timeout.TotalMilliseconds / interval.TotalMilliseconds) + 1;
			var consecutiveObservations = 0;
			for (var observation = 0; observation < maximumObservations; observation++)
			{
				if (cancellationToken.IsCancellationRequested) return ShellReadinessResult.Cancelled;

				if (this._probe.IsCurrentSessionShellReady())
				{
					consecutiveObservations++;
					if (consecutiveObservations >= requiredConsecutiveObservations) return ShellReadinessResult.Ready;
				}
				else
				{
					consecutiveObservations = 0;
				}

				if (observation + 1 >= maximumObservations) break;
				try
				{
					await this._delay(interval, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return ShellReadinessResult.Cancelled;
				}
			}

			return ShellReadinessResult.TimedOut;
		}

		internal async Task<ShellReadinessResult> WaitAndContinueAsync(
			TimeSpan timeout,
			TimeSpan interval,
			int requiredConsecutiveObservations,
			Func<Task> continuation,
			CancellationToken cancellationToken)
		{
			if (continuation == null) throw new ArgumentNullException(nameof(continuation));

			var result = await this.WaitAsync(timeout, interval, requiredConsecutiveObservations, cancellationToken);
			if (result == ShellReadinessResult.Ready)
			{
				await continuation();
			}

			return result;
		}
	}

	internal interface IShellReadinessProbe
	{
		bool IsCurrentSessionShellReady();
	}

	internal sealed class ShellReadinessProbe : IShellReadinessProbe
	{
		private readonly Func<IntPtr> _findShellWindow;
		private readonly Func<IntPtr, int> _getOwnerProcessId;
		private readonly Func<int, ShellOwner> _getOwner;
		private readonly int _currentSessionId;

		internal ShellReadinessProbe()
			: this(
				() => FindWindow("Shell_TrayWnd", null),
				GetOwnerProcessId,
				GetOwner,
				Process.GetCurrentProcess().SessionId)
		{
		}

		internal ShellReadinessProbe(
			Func<IntPtr> findShellWindow,
			Func<IntPtr, int> getOwnerProcessId,
			Func<int, ShellOwner> getOwner,
			int currentSessionId)
		{
			this._findShellWindow = findShellWindow;
			this._getOwnerProcessId = getOwnerProcessId;
			this._getOwner = getOwner;
			this._currentSessionId = currentSessionId;
		}

		public bool IsCurrentSessionShellReady()
		{
			var shellWindow = this._findShellWindow();
			if (shellWindow == IntPtr.Zero) return false;

			try
			{
				var owner = this._getOwner(this._getOwnerProcessId(shellWindow));
				return string.Equals(owner.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)
					&& owner.SessionId == this._currentSessionId;
			}
			catch (ArgumentException)
			{
				return false;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
			catch (Win32Exception)
			{
				return false;
			}
		}

		private static int GetOwnerProcessId(IntPtr window)
		{
			GetWindowThreadProcessId(window, out var processId);
			return unchecked((int)processId);
		}

		private static ShellOwner GetOwner(int processId)
		{
			using (var process = Process.GetProcessById(processId))
			{
				return new ShellOwner(process.ProcessName, process.SessionId);
			}
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr FindWindow(string className, string windowName);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
	}

	internal sealed class ShellOwner
	{
		internal ShellOwner(string processName, int sessionId)
		{
			this.ProcessName = processName;
			this.SessionId = sessionId;
		}

		internal string ProcessName { get; }

		internal int SessionId { get; }
	}
}
