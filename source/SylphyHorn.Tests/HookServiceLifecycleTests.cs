using System;
using System.Threading;
using System.Threading.Tasks;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public class HookServiceLifecycleTests
	{
		[Fact]
		public void ConstructionDoesNotStartDetectorAndStartIsIdempotent()
		{
			var detector = new FakeHookDetector();
			using (var service = new HookService(detector))
			{
				Assert.Equal(0, detector.StartCount);

				service.Start();
				service.Start();

				Assert.Equal(1, detector.StartCount);
			}
		}

		[Fact]
		public void SuspendAndResumeBeforeStartDoNotStartDetector()
		{
			var detector = new FakeHookDetector();
			using (var service = new HookService(detector))
			using (var suspension = service.Suspend())
			{
				suspension.Dispose();

				Assert.Equal(0, detector.StartCount);
				Assert.Equal(0, detector.StopCount);
			}
		}

		[Fact]
		public void StartWhileSuspendedWaitsForResume()
		{
			var detector = new FakeHookDetector();
			using (var service = new HookService(detector))
			{
				var suspension = service.Suspend();
				service.Start();
				Assert.Equal(0, detector.StartCount);

				suspension.Dispose();
				Assert.Equal(1, detector.StartCount);
			}
		}

		[Fact]
		public void DisposeBeforeStartNeverStartsAndRejectsLaterStart()
		{
			var detector = new FakeHookDetector();
			var service = new HookService(detector);

			service.Dispose();

			Assert.Equal(0, detector.StartCount);
			Assert.Equal(1, detector.DisposeCount);
			Assert.Throws<ObjectDisposedException>(service.Start);
		}

		[Fact]
		public void SuccessfulPreparationStartsHookBeforePublishingInitializedEvent()
		{
			var detector = new FakeHookDetector();
			using (var service = new HookService(detector))
			{
				var preparation = new ApplicationPreparation(service, () => { }, null);
				var startCountObservedByEvent = 0;
				preparation.VirtualDesktopInitialized += () => startCountObservedByEvent = detector.StartCount;

				preparation.CompleteSuccessfulInitialization();

				Assert.Equal(1, detector.StartCount);
				Assert.Equal(1, startCountObservedByEvent);
			}
		}

		[Fact]
		public void CanceledProviderInitializationDoesNotStartHook()
		{
			var detector = new FakeHookDetector();
			using (var service = new HookService(detector))
			using (var cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();
				var preparation = new ApplicationPreparation(service, () => { }, null);
				var canceled = 0;
				preparation.VirtualDesktopInitializationCanceled += () => canceled++;

				preparation.CompleteProviderInitialization(Task.FromCanceled(cancellation.Token), null);

				Assert.Equal(1, canceled);
				Assert.Equal(0, detector.StartCount);
			}
		}

		[Fact]
		public void FailedProviderInitializationDoesNotStartHook()
		{
			var detector = new FakeHookDetector();
			using (var service = new HookService(detector))
			{
				var preparation = new ApplicationPreparation(service, () => { }, null);
				var failed = 0;
				preparation.VirtualDesktopInitializationFailed += (exception, autoRestart) => failed++;

				preparation.CompleteProviderInitialization(Task.FromException(new InvalidOperationException()), null);

				Assert.Equal(1, failed);
				Assert.Equal(0, detector.StartCount);
			}
		}

		private sealed class FakeHookDetector : HookService.IHookDetector
		{
			public event EventHandler<ShortcutKeyPressedEventArgs> KeyPressed
			{
				add { }
				remove { }
			}

			public event EventHandler<ShortcutKeyPressedEventArgs> KeyUp
			{
				add { }
				remove { }
			}

			public event EventHandler<ShortcutKeyPressedEventArgs> ButtonPressed
			{
				add { }
				remove { }
			}

			public event EventHandler<ShortcutKeyPressedEventArgs> ButtonUp
			{
				add { }
				remove { }
			}

			internal int StartCount { get; private set; }
			internal int StopCount { get; private set; }
			internal int DisposeCount { get; private set; }

			public void Start() => this.StartCount++;

			public void Stop() => this.StopCount++;

			public void Dispose() => this.DisposeCount++;
		}
	}
}
