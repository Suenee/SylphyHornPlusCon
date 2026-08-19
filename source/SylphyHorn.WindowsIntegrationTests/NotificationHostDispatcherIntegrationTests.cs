using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SylphyHorn.Services;
using SylphyHorn.UI.Bindings;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	public class NotificationHostDispatcherIntegrationTests
	{
		private const int TimeoutMilliseconds = 10000;

		[Fact(Timeout = TimeoutMilliseconds)]
		[Trait(IntegrationTestExecutionEnvironment.TraitName, IntegrationTestExecutionEnvironment.HostedCI)]
		public void ActualDispatcherRunProcessesFirstRequestShutsDownAndRejectsLatePost()
		{
			var presenter = new RecordingPresenter();
			var host = CreateHost(() => presenter);
			host.EnqueueShow(CreateRequest("first"));
			Assert.True(presenter.Presented.Wait(TimeoutMilliseconds, TestContext.Current.CancellationToken));
			Assert.Equal(ApartmentState.STA, presenter.ApartmentState);
			Assert.IsType<DispatcherSynchronizationContext>(presenter.SynchronizationContext);
			Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, presenter.ThreadId);

			host.Dispose();
			Assert.True(SpinWait.SpinUntil(() => host.State == NotificationHostState.Stopped, TimeoutMilliseconds));
			host.EnqueueShow(CreateRequest("late"));
			Thread.Sleep(50);
			Assert.Equal(1, presenter.Count);
		}

		[Fact(Timeout = TimeoutMilliseconds)]
		[Trait(IntegrationTestExecutionEnvironment.TraitName, IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task DisposeDuringActualThreadStartupCannotPublishRunningDispatcher()
		{
			var entered = new ManualResetEventSlim();
			var release = new ManualResetEventSlim();
			var presenter = new RecordingPresenter();
			var host = CreateHost(() =>
			{
				entered.Set();
				release.Wait();
				return presenter;
			});
			host.EnqueueShow(CreateRequest("first"));
			Assert.True(entered.Wait(TimeoutMilliseconds, TestContext.Current.CancellationToken));
			var dispose = Task.Run(() => host.Dispose(), TestContext.Current.CancellationToken);
			Assert.True(SpinWait.SpinUntil(() => host.State == NotificationHostState.Stopping, TimeoutMilliseconds));
			release.Set();
			var completed = await Task.WhenAny(dispose, Task.Delay(TimeoutMilliseconds, TestContext.Current.CancellationToken));
			Assert.Same(dispose, completed);
			await dispose;
			Assert.True(SpinWait.SpinUntil(() => host.State == NotificationHostState.Stopped, TimeoutMilliseconds));
			Assert.Equal(0, presenter.Count);
		}

		private static NotificationHost CreateHost(Func<INotificationPresenter> presenterFactory)
			=> new NotificationHost(
				presenterFactory,
				new NotificationWorkerThreadFactory(),
				_dispatcher => new NoOpTimerScheduler(),
				_ => { });

		private static DesktopNotificationRequest CreateRequest(string body)
			=> new DesktopNotificationRequest(
				"title", "header", body, "resident", 1000, false,
				new NotificationVisualSettings(
					0, WindowPlacement.Center, 0, 0, 0, 0, "Segoe UI", 12, 20,
					HorizontalAlignment.Center, HorizontalAlignment.Center, 0, false, 500, 200, 400, 100));

		private sealed class RecordingPresenter : INotificationPresenter
		{
			private int _count;
			internal readonly ManualResetEventSlim Presented = new ManualResetEventSlim();
			internal int Count => Volatile.Read(ref this._count);
			internal int ThreadId { get; private set; }
			internal ApartmentState ApartmentState { get; private set; }
			internal SynchronizationContext SynchronizationContext { get; private set; }

			public INotificationPresentation Present(DesktopNotificationRequest request)
			{
				this.ThreadId = Thread.CurrentThread.ManagedThreadId;
				this.ApartmentState = Thread.CurrentThread.GetApartmentState();
				this.SynchronizationContext = SynchronizationContext.Current;
				Interlocked.Increment(ref this._count);
				this.Presented.Set();
				return new Presentation();
			}

			public IDisposable Present(PinNotificationRequest request) => new DelegateDisposable(() => { });
		}

		private sealed class Presentation : INotificationPresentation
		{
			public void Update(string title, string header, string body, NotificationVisualSettings visual) { }
			public void Dispose() { }
		}

		private sealed class NoOpTimerScheduler : INotificationTimerScheduler
		{
			public IDisposable Schedule(TimeSpan dueTime, Action callback) => new DelegateDisposable(() => { });
		}
	}
}
