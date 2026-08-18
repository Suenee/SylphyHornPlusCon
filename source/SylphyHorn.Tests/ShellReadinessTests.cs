using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public class ShellReadinessTests
	{
		[Fact]
		public async Task MissingShellWindowTimesOutWithoutContinuingStartup()
		{
			var providerStarts = 0;
			var hookStarts = 0;
			var trayShows = 0;
			var readiness = CreateReadiness(false, false, false, false);

			var result = await readiness.WaitAndContinueAsync(
				TimeSpan.FromMilliseconds(3),
				TimeSpan.FromMilliseconds(1),
				3,
				() =>
				{
					providerStarts++;
					hookStarts++;
					trayShows++;
					return Task.CompletedTask;
				},
				CancellationToken.None);

			Assert.Equal(ShellReadinessResult.TimedOut, result);
			Assert.Equal(0, providerStarts);
			Assert.Equal(0, hookStarts);
			Assert.Equal(0, trayShows);
		}

		[Fact]
		public void ShellOwnedByAnotherSessionIsRejected()
		{
			var probe = new ShellReadinessProbe(
				() => new IntPtr(1),
				window => 42,
				processId => new ShellOwner("explorer", 8),
				7);

			Assert.False(probe.IsCurrentSessionShellReady());
		}

		[Fact]
		public void CurrentSessionExplorerOwnedShellIsAccepted()
		{
			var probe = new ShellReadinessProbe(
				() => new IntPtr(1),
				window => 42,
				processId => new ShellOwner("EXPLORER", 7),
				7);

			Assert.True(probe.IsCurrentSessionShellReady());
		}

		[Fact]
		public async Task ConsecutiveObservationsResetAfterReadinessBreaks()
		{
			var probe = new SequenceProbe(true, true, false, true, true, true);
			var readiness = new ShellReadiness(probe, (delay, token) => Task.CompletedTask);

			var result = await readiness.WaitAsync(
				TimeSpan.FromMilliseconds(5),
				TimeSpan.FromMilliseconds(1),
				3,
				CancellationToken.None);

			Assert.Equal(ShellReadinessResult.Ready, result);
			Assert.Equal(6, probe.ObservationCount);
		}

		[Fact]
		public async Task CancellationReturnsWithoutWaitingForBudget()
		{
			using (var cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();
				var probe = new SequenceProbe(false);
				var readiness = new ShellReadiness(probe, (delay, token) => Task.Delay(delay, token));

				var result = await readiness.WaitAsync(
					TimeSpan.FromSeconds(30),
					TimeSpan.FromMilliseconds(250),
					3,
					cancellation.Token);

				Assert.Equal(ShellReadinessResult.Cancelled, result);
				Assert.Equal(0, probe.ObservationCount);
			}
		}

		private static ShellReadiness CreateReadiness(params bool[] observations)
		{
			return new ShellReadiness(new SequenceProbe(observations), (delay, token) => Task.CompletedTask);
		}

		private sealed class SequenceProbe : IShellReadinessProbe
		{
			private readonly Queue<bool> _observations;

			internal SequenceProbe(params bool[] observations)
			{
				this._observations = new Queue<bool>(observations);
			}

			internal int ObservationCount { get; private set; }

			public bool IsCurrentSessionShellReady()
			{
				this.ObservationCount++;
				return this._observations.Count > 0 && this._observations.Dequeue();
			}
		}
	}
}
