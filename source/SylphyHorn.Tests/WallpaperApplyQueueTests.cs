using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public class WallpaperApplyQueueTests
	{
		[Fact]
		public void EnqueueSchedulesWithoutApplyingInline()
		{
			Action worker = null;
			var applied = false;
			using (var queue = Queue((_, __) => applied = true, action => worker = action))
			{
				queue.Enqueue("wallpaper", WallpaperPosition.Fill);
				Assert.False(applied);
				Assert.NotNull(worker);
				worker();
				Assert.True(applied);
			}
		}

		[Fact]
		public void PendingRequestsCoalesceToLatestValue()
		{
			Action worker = null;
			var applied = new List<Tuple<string, WallpaperPosition>>();
			using (var queue = Queue((path, position) => applied.Add(Tuple.Create(path, position)), action => worker = action))
			{
				queue.Enqueue("first", WallpaperPosition.Center);
				queue.Enqueue("second", WallpaperPosition.Fill);
				queue.Enqueue("latest", WallpaperPosition.Span);
				worker();
				var request = Assert.Single(applied);
				Assert.Equal("latest", request.Item1);
				Assert.Equal(WallpaperPosition.Span, request.Item2);
			}
		}

		[Fact]
		public void RequestArrivingDuringApplyRunsNextOnSameWorker()
		{
			Action worker = null;
			WallpaperApplyQueue queue = null;
			var applied = new List<string>();
			try
			{
				queue = Queue((path, _) =>
				{
					applied.Add(path);
					if (path == "first") queue.Enqueue("second", WallpaperPosition.Tile);
				}, action => worker = action);
				queue.Enqueue("first", WallpaperPosition.Center);
				worker();
				Assert.Equal(new[] { "first", "second" }, applied);
			}
			finally { queue?.Dispose(); }
		}

		[Fact]
		public void ApplyFailureAndErrorReporterFailureDoNotStopLaterRequest()
		{
			Action worker = null;
			WallpaperApplyQueue queue = null;
			var applied = new List<string>();
			try
			{
				queue = new WallpaperApplyQueue((path, _) =>
				{
					applied.Add(path);
					if (path == "first")
					{
						queue.Enqueue("second", WallpaperPosition.Fit);
						throw new InvalidOperationException("synthetic apply failure");
					}
				}, _ => throw new InvalidOperationException("synthetic reporter failure"), action => worker = action);
				queue.Enqueue("first", WallpaperPosition.Center);
				worker();
				Assert.Equal(new[] { "first", "second" }, applied);
			}
			finally { queue?.Dispose(); }
		}

		[Fact]
		public void SchedulerFailureWithReentrantNewerRequestDoesNotStrandLatest()
		{
			Action worker = null;
			WallpaperApplyQueue queue = null;
			var attempts = 0;
			var applied = new List<string>();
			var errors = new List<Exception>();
			try
			{
				queue = new WallpaperApplyQueue((path, _) => applied.Add(path), errors.Add, action =>
				{
					attempts++;
					if (attempts == 1)
					{
						queue.Enqueue("latest", WallpaperPosition.Span);
						throw new InvalidOperationException("synthetic scheduler failure");
					}
					worker = action;
				});

				queue.Enqueue("first", WallpaperPosition.Center);
				Assert.Equal(2, attempts);
				worker();

				Assert.Equal(new[] { "latest" }, applied);
				Assert.IsType<InvalidOperationException>(Assert.Single(errors));
			}
			finally { queue?.Dispose(); }
		}

		[Fact]
		public void RepeatedSchedulerFailureDropsRequestAfterBoundedHandoff()
		{
			WallpaperApplyQueue queue = null;
			var attempts = 0;
			var applied = 0;
			var errors = 0;
			try
			{
				queue = new WallpaperApplyQueue((_, __) => applied++, _ => errors++, _ =>
				{
					attempts++;
					if (attempts == 1) queue.Enqueue("latest", WallpaperPosition.Fill);
					throw new InvalidOperationException("synthetic scheduler failure");
				});
				queue.Enqueue("first", WallpaperPosition.Center);

				Assert.Equal(2, attempts);
				Assert.Equal(2, errors);
				Assert.Equal(0, applied);
			}
			finally { queue?.Dispose(); }
		}

		[Fact]
		public void SynchronousApplyInvalidatesPendingAutomaticApply()
		{
			Action worker = null;
			var applied = new List<string>();
			using (var queue = Queue((path, _) => applied.Add(path), action => worker = action))
			{
				queue.Enqueue("automatic", WallpaperPosition.Center);
				queue.ApplyNow("local", WallpaperPosition.Fill);
				worker();

				Assert.Equal(new[] { "local" }, applied);
			}
		}

		[Fact]
		public async Task SynchronousApplyWaitsForRunningAutomaticApplyAndWins()
		{
			Action worker = null;
			var automaticStarted = new ManualResetEventSlim();
			var releaseAutomatic = new ManualResetEventSlim();
			var localApplied = new ManualResetEventSlim();
			var localWaiting = new ManualResetEventSlim();
			var applied = new List<string>();
			var waiter = new ObservableWallpaperApplyWaiter(WallpaperApplyWaitReason.SynchronousApply, localWaiting);
			using (var queue = Queue((path, _) =>
			{
				lock (applied) applied.Add(path);
				if (path == "automatic")
				{
					automaticStarted.Set();
					releaseAutomatic.Wait(TestContext.Current.CancellationToken);
				}
				else localApplied.Set();
			}, action => worker = action, waiter))
			{
				queue.Enqueue("automatic", WallpaperPosition.Center);
				var automatic = Task.Run(worker, TestContext.Current.CancellationToken);
				Assert.True(automaticStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				var local = Task.Run(() =>
				{
					queue.ApplyNow("local", WallpaperPosition.Fill);
				}, TestContext.Current.CancellationToken);
				Assert.True(localWaiting.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				Assert.False(localApplied.IsSet);

				releaseAutomatic.Set();
				await Task.WhenAll(automatic, local);
				Assert.Equal(new[] { "automatic", "local" }, applied);
			}
		}

		[Fact]
		public async Task DisposeWaitsForRunningApplyAndDropsPending()
		{
			Action worker = null;
			var applyStarted = new ManualResetEventSlim();
			var releaseApply = new ManualResetEventSlim();
			var disposeWaiting = new ManualResetEventSlim();
			var applied = new List<string>();
			var waiter = new ObservableWallpaperApplyWaiter(WallpaperApplyWaitReason.Dispose, disposeWaiting);
			var queue = Queue((path, _) =>
			{
				applied.Add(path);
				applyStarted.Set();
				releaseApply.Wait(TestContext.Current.CancellationToken);
			}, action => worker = action, waiter);
			try
			{
				queue.Enqueue("running", WallpaperPosition.Center);
				var automatic = Task.Run(worker, TestContext.Current.CancellationToken);
				Assert.True(applyStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				queue.Enqueue("pending", WallpaperPosition.Fill);
				var dispose = Task.Run(() => queue.Dispose(), TestContext.Current.CancellationToken);
				Assert.True(disposeWaiting.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				Assert.False(dispose.IsCompleted);

				releaseApply.Set();
				await Task.WhenAll(automatic, dispose);
				queue.Enqueue("later", WallpaperPosition.Span);
				Assert.Equal(new[] { "running" }, applied);
			}
			finally
			{
				releaseApply.Set();
				queue.Dispose();
			}
		}

		[Fact]
		public void FailedSynchronousApplyRestoresInvalidatedPendingRequest()
		{
			Action worker = null;
			var applied = new List<string>();
			using (var queue = Queue((path, _) =>
			{
				applied.Add(path);
				if (path == "local") throw new InvalidOperationException("synthetic synchronous failure");
			}, action => worker = action))
			{
				queue.Enqueue("automatic", WallpaperPosition.Center);
				var error = Assert.Throws<InvalidOperationException>(() => queue.ApplyNow("local", WallpaperPosition.Fill));
				Assert.Equal("synthetic synchronous failure", error.Message);

				worker();
				Assert.Equal(new[] { "local", "automatic" }, applied);
			}
		}

		[Fact]
		public void FailedSynchronousApplyDoesNotRestoreOlderRequestOverNewerAutomaticRequest()
		{
			Action worker = null;
			WallpaperApplyQueue queue = null;
			var applied = new List<string>();
			try
			{
				queue = Queue((path, _) =>
				{
					applied.Add(path);
					if (path == "local")
					{
						queue.Enqueue("newer", WallpaperPosition.Span);
						throw new InvalidOperationException("synthetic synchronous failure");
					}
				}, action => worker = action);
				queue.Enqueue("older", WallpaperPosition.Center);

				Assert.Throws<InvalidOperationException>(() => queue.ApplyNow("local", WallpaperPosition.Fill));
				worker();

				Assert.Equal(new[] { "local", "newer" }, applied);
			}
			finally { queue?.Dispose(); }
		}

		[Fact]
		public async Task FailedSynchronousApplyDoesNotRestoreInvalidatedRequestDuringDispose()
		{
			Action worker = null;
			var applyStarted = new ManualResetEventSlim();
			var releaseApply = new ManualResetEventSlim();
			var disposeWaiting = new ManualResetEventSlim();
			var applied = new List<string>();
			var waiter = new ObservableWallpaperApplyWaiter(WallpaperApplyWaitReason.Dispose, disposeWaiting);
			var queue = Queue((path, _) =>
			{
				lock (applied) applied.Add(path);
				if (path == "local")
				{
					applyStarted.Set();
					releaseApply.Wait(TestContext.Current.CancellationToken);
					throw new InvalidOperationException("synthetic synchronous failure");
				}
			}, action => worker = action, waiter);
			try
			{
				queue.Enqueue("automatic", WallpaperPosition.Center);
				var local = Task.Run(() => Assert.Throws<InvalidOperationException>(() => queue.ApplyNow("local", WallpaperPosition.Fill)), TestContext.Current.CancellationToken);
				Assert.True(applyStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				var dispose = Task.Run(() => queue.Dispose(), TestContext.Current.CancellationToken);
				Assert.True(disposeWaiting.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				releaseApply.Set();
				await Task.WhenAll(local, dispose);

				worker();
				Assert.Equal(new[] { "local" }, applied);
			}
			finally
			{
				releaseApply.Set();
				queue.Dispose();
			}
		}

		[Fact]
		public void ConcurrentEnqueuesScheduleAtMostOneWorker()
		{
			Action worker = null;
			var schedules = 0;
			using (var queue = Queue((_, __) => { }, action =>
			{
				Interlocked.Increment(ref schedules);
				worker = action;
			}))
			{
				Parallel.For(0, 32, index => queue.Enqueue(index.ToString(), WallpaperPosition.Fill));
				Assert.Equal(1, schedules);
				worker();
			}
		}

		[Fact]
		public async Task DisposePreventsWaitingAutomaticWorkerFromStartingApply()
		{
			Action worker = null;
			var synchronousStarted = new ManualResetEventSlim();
			var releaseSynchronous = new ManualResetEventSlim();
			var workerCalling = new ManualResetEventSlim();
			var disposeWaiting = new ManualResetEventSlim();
			var waiter = new ObservableWallpaperApplyWaiter(WallpaperApplyWaitReason.Dispose, disposeWaiting);
			var applied = new List<string>();
			var queue = Queue((path, _) =>
			{
				lock (applied) applied.Add(path);
				if (path == "synchronous")
				{
					synchronousStarted.Set();
					releaseSynchronous.Wait(TestContext.Current.CancellationToken);
				}
			}, action => worker = action, waiter);
			try
			{
				var synchronous = Task.Run(() => queue.ApplyNow("synchronous", WallpaperPosition.Center), TestContext.Current.CancellationToken);
				Assert.True(synchronousStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				queue.Enqueue("automatic", WallpaperPosition.Fill);
				var automatic = Task.Run(() =>
				{
					workerCalling.Set();
					worker();
				}, TestContext.Current.CancellationToken);
				Assert.True(workerCalling.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				var dispose = Task.Run(() => queue.Dispose(), TestContext.Current.CancellationToken);
				Assert.True(disposeWaiting.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

				releaseSynchronous.Set();
				await Task.WhenAll(synchronous, automatic, dispose);
				Assert.Equal(new[] { "synchronous" }, applied);
			}
			finally
			{
				releaseSynchronous.Set();
				queue.Dispose();
			}
		}


		[Fact]
		public void ApplyNowAfterDisposeIsRejected()
		{
			var queue = Queue((_, __) => { }, _ => { });
			queue.Dispose();

			Assert.Throws<ObjectDisposedException>(() => queue.ApplyNow("value", WallpaperPosition.Fill));
		}

		private static WallpaperApplyQueue Queue(Action<string, WallpaperPosition> apply, Action<Action> schedule, IWallpaperApplyWaiter waiter = null)
			=> new WallpaperApplyQueue(apply, _ => { }, schedule, waiter);

		private sealed class ObservableWallpaperApplyWaiter : IWallpaperApplyWaiter
		{
			private readonly WallpaperApplyWaitReason _observedReason;
			private readonly ManualResetEventSlim _waitStarted;

			internal ObservableWallpaperApplyWaiter(WallpaperApplyWaitReason observedReason, ManualResetEventSlim waitStarted)
			{
				this._observedReason = observedReason;
				this._waitStarted = waitStarted;
			}

			public void Wait(object gate, WallpaperApplyWaitReason reason)
			{
				if (reason == this._observedReason) this._waitStarted.Set();
				System.Threading.Monitor.Wait(gate);
			}

			public void PulseAll(object gate) => System.Threading.Monitor.PulseAll(gate);
		}
	}
}
