using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SylphyHorn.Services;
using SylphyHorn.UI.Bindings;
using WindowsDesktop;
using Xunit;

namespace SylphyHorn.Tests
{
	public class NotificationHostTests
	{
		[Fact]
		public void BlockedPresentationDoesNotBlockEnqueueAndLatestStateWins()
		{
			var presenter = new FakePresenter { BlockNext = true };
			using (var context = CreateHost(presenter))
			{
				context.Host.EnqueueState(CreateDesktop("A"));
				Assert.True(presenter.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

				var stopwatch = Stopwatch.StartNew();
				context.Host.EnqueueState(CreateDesktop("B"));
				context.Host.EnqueueState(CreateDesktop("C"));
				stopwatch.Stop();
				Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), stopwatch.Elapsed.ToString());

				presenter.Release.Set();
				presenter.WaitForDesktopCount(2);
				Assert.Equal(new[] { "A", "C" }, presenter.DesktopBodies);
			}
		}

		[Fact]
		public void ControlCommandsPreserveParityOrderAndOverrideOlderState()
		{
			var presenter = new FakePresenter { BlockNext = true };
			using (var context = CreateHost(presenter))
			{
				context.Host.EnqueueState(CreateDesktop("blocked"));
				Assert.True(presenter.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
				context.Host.EnqueueToggle(CreateDesktop("toggle-one"));
				context.Host.EnqueueToggle(CreateDesktop("toggle-two"));
				context.Host.EnqueueState(CreateDesktop("old-state"));
				context.Host.EnqueueHide();
				context.Host.EnqueueShow(CreateDesktop("show"));
				context.Host.EnqueueHide();
				context.Host.EnqueueToggle(CreateDesktop("final"));
				presenter.Release.Set();

				presenter.WaitForBody("final");
				Assert.Equal("final", presenter.ActiveDesktopBody);
				Assert.Contains("toggle-two", presenter.DesktopBodies);
				Assert.Contains("old-state", presenter.DesktopBodies);
			}
		}

		[Fact]
		public void PresenterCreatesEveryMonitorWindowOnOneExecutionContext()
		{
			var factory = new FakeWindowFactory(3);
			var presenter = new NotificationPresenter(factory);
			using (presenter.Present(CreateDesktop("monitors", display: uint.MaxValue)))
			{
				Assert.Equal(3, factory.Handles.Count);
				Assert.Single(factory.Handles.Select(handle => handle.ShowThreadId).Distinct());
				Assert.All(factory.Handles, handle => Assert.True(handle.Shown));
			}
			Assert.All(factory.Handles, handle => Assert.True(handle.Disposed));
		}

		[Fact]
		public void PresenterClosesEveryCreatedWindowWhenOneShowFails()
		{
			var factory = new FakeWindowFactory(3, failShowIndex: 1);
			var presenter = new NotificationPresenter(factory);

			Assert.Throws<InvalidOperationException>(() => presenter.Present(CreateDesktop("monitors", display: uint.MaxValue)));
			Assert.Equal(2, factory.Handles.Count);
			Assert.All(factory.Handles, handle => Assert.True(handle.Disposed));
		}

		[Fact]
		public void PresenterDisposesCreatedWindowWhenDataContextAssignmentFails()
		{
			var factory = new FakeWindowFactory(3, failDataContextIndex: 1);
			var presenter = new NotificationPresenter(factory);

			Assert.Throws<InvalidOperationException>(() => presenter.Present(CreateDesktop("monitors", display: uint.MaxValue)));
			Assert.Equal(2, factory.Handles.Count);
			Assert.All(factory.Handles, handle => Assert.Equal(1, handle.DisposeCallCount));
		}

		[Fact]
		public void PresenterAttemptsEveryInitialCleanupWhenFirstDisposeFails()
		{
			var factory = new FakeWindowFactory(3, failShowIndex: 2, failDisposeIndex: 0);
			var presenter = new NotificationPresenter(factory);

			var exception = Assert.Throws<AggregateException>(() => presenter.Present(CreateDesktop("monitors", display: uint.MaxValue)));
			Assert.Equal(2, exception.InnerExceptions.Count);
			Assert.Equal(3, factory.Handles.Count);
			Assert.All(factory.Handles, handle => Assert.Equal(1, handle.DisposeCallCount));
		}

		[Fact]
		public void PresentationDisposeAttemptsEveryWindowBeforeRethrowing()
		{
			var factory = new FakeWindowFactory(3, failDisposeIndex: 0);
			var presenter = new NotificationPresenter(factory);
			var presentation = presenter.Present(CreateDesktop("monitors", display: uint.MaxValue));

			Assert.Throws<InvalidOperationException>(() => presentation.Dispose());
			Assert.All(factory.Handles, handle => Assert.Equal(1, handle.DisposeCallCount));
		}

		[Fact]
		public void ResidentTimerUpdatesHeaderAndOldGenerationCannotTouchReplacement()
		{
			var presenter = new FakePresenter();
			using (var context = CreateHost(presenter))
			{
				context.Host.EnqueueShow(CreateDesktop("A", resident: true));
				presenter.WaitForDesktopCount(1);
				var timerA = context.Timers.WaitForTimer(0);
				timerA.Fire();
				Assert.Equal("resident-A", presenter.Presentations[0].LastHeader);

				context.Host.EnqueueShow(CreateDesktop("B"));
				presenter.WaitForDesktopCount(2);
				var timerB = context.Timers.WaitForTimer(1);
				timerA.FireEvenIfDisposed();
				Assert.False(presenter.Presentations[1].Disposed);
				Assert.Null(presenter.Presentations[1].LastHeader);
				timerB.Fire();
				Assert.True(presenter.Presentations[1].Disposed);
			}
		}

		[Fact]
		public void TimerCallbackExceptionClosesFailedGenerationAndHostContinues()
		{
			var presenter = new FakePresenter { ThrowOnUpdateNext = true };
			using (var context = CreateHost(presenter))
			{
				context.Host.EnqueueShow(CreateDesktop("resident", resident: true));
				presenter.WaitForDesktopCount(1);
				context.Timers.WaitForTimer(0).Fire();
				Assert.True(presenter.Presentations[0].Disposed);
				context.Host.EnqueueShow(CreateDesktop("continues"));
				presenter.WaitForBody("continues");
				Assert.Equal(NotificationHostState.Running, context.Host.State);
			}
		}

		[Fact]
		public void PinRequestsAreFifoAndMissingGeometryIsDroppedAndLogged()
		{
			var logs = new List<Exception>();
			var presenter = new FakePresenter();
			using (var context = CreateHost(presenter, logs.Add))
			{
				context.Host.EnqueuePin(CreatePin("pin-1", PinOperations.PinWindow, 1));
				context.Host.EnqueuePin(CreatePin("unpin-1", PinOperations.UnpinWindow, 1));
				context.Host.EnqueuePin(CreatePin("pin-2", PinOperations.PinWindow, 2));
				context.Host.EnqueuePin(CreatePin("drop", PinOperations.PinWindow, null));
				presenter.WaitForPinCount(3);
				Assert.Equal(new[] { "pin-1", "unpin-1", "pin-2" }, presenter.PinBodies);
				Assert.Single(logs);
				Assert.Contains("geometry", logs[0].Message, StringComparison.OrdinalIgnoreCase);
			}
		}

		[Fact]
		public void FirstRequestIsRetainedAndNormalShutdownRejectsLatePosts()
		{
			var presenter = new FakePresenter();
			var context = CreateHost(presenter);
			context.Host.EnqueueShow(CreateDesktop("first"));
			presenter.WaitForBody("first");
			Assert.Equal(NotificationHostState.Running, context.Host.State);
			context.Host.Dispose();
			WaitForState(context.Host, NotificationHostState.Stopped);
			context.Host.EnqueueShow(CreateDesktop("late"));
			Thread.Sleep(50);
			Assert.DoesNotContain("late", presenter.DesktopBodies);
			context.Host.Dispose();
		}

		[Fact]
		public async Task DisposeDuringStartupPreventsRunningTransition()
		{
			var enteredFactory = new ManualResetEventSlim();
			var releaseFactory = new ManualResetEventSlim();
			var presenter = new FakePresenter();
			var timers = new FakeTimerScheduler();
			var host = new NotificationHost(
				() =>
				{
					enteredFactory.Set();
					releaseFactory.Wait();
					return presenter;
				},
				new NotificationWorkerThreadFactory(),
				dispatcher => { timers.Dispatcher = dispatcher; return timers; },
				_ => { });
			host.EnqueueShow(CreateDesktop("first"));
			Assert.True(enteredFactory.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
			var dispose = Task.Run(() => host.Dispose(), TestContext.Current.CancellationToken);
			Assert.True(SpinWait.SpinUntil(() => host.State == NotificationHostState.Stopping, TimeSpan.FromSeconds(5)));
			releaseFactory.Set();
			var completed = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
			Assert.Same(dispose, completed);
			await dispose;
			WaitForState(host, NotificationHostState.Stopped);
			Assert.Empty(presenter.DesktopBodies);
		}

		[Fact]
		public void StartupFailureAndUnexpectedExitAreTerminalAndDoNotRestart()
		{
			var starts = 0;
			var failed = new NotificationHost(
				() => { starts++; throw new InvalidOperationException("startup"); },
				new NotificationWorkerThreadFactory(),
				_dispatcher => new FakeTimerScheduler(),
				_ => { });
			failed.EnqueueShow(CreateDesktop("first"));
			WaitForState(failed, NotificationHostState.Faulted);
			failed.EnqueueShow(CreateDesktop("second"));
			Assert.Equal(1, starts);

			var presenter = new FakePresenter { ShutdownDispatcherOnNext = true };
			using (var context = CreateHost(presenter))
			{
				context.Host.EnqueueShow(CreateDesktop("exit"));
				WaitForState(context.Host, NotificationHostState.Faulted);
				context.Host.EnqueueShow(CreateDesktop("late"));
				Assert.DoesNotContain("late", presenter.DesktopBodies);
			}
		}

		[Fact]
		public void DisposeWhilePresenterIsBlockedReturnsWithinBoundedTimeout()
		{
			var presenter = new FakePresenter { BlockNext = true };
			var context = CreateHost(presenter);
			context.Host.EnqueueShow(CreateDesktop("blocked"));
			Assert.True(presenter.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
			var stopwatch = Stopwatch.StartNew();
			context.Host.Dispose();
			stopwatch.Stop();
			Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), stopwatch.Elapsed.ToString());
			presenter.Release.Set();
		}

		[Fact]
		public void OperationExceptionContinuesPendingAndLoggerExceptionIsContained()
		{
			var presenter = new FakePresenter { ThrowNext = true };
			using (var context = CreateHost(presenter, _ => throw new InvalidOperationException("logger")))
			{
				context.Host.EnqueueShow(CreateDesktop("throws"));
				context.Host.EnqueueShow(CreateDesktop("continues"));
				presenter.WaitForBody("continues");
				Assert.Equal(NotificationHostState.Running, context.Host.State);
			}
		}

		[Fact]
		public void DispatcherUnhandledExceptionFaultsHostAndDropsLaterRequests()
		{
			var presenter = new FakePresenter { PostUnhandledOnNext = true };
			using (var context = CreateHost(presenter))
			{
				context.Host.EnqueueShow(CreateDesktop("fault"));
				WaitForState(context.Host, NotificationHostState.Faulted);
				context.Host.EnqueueShow(CreateDesktop("late"));
				Assert.DoesNotContain("late", presenter.DesktopBodies);
			}
		}

		private static HostContext CreateHost(FakePresenter presenter, Action<Exception> logger = null)
		{
			var timers = new FakeTimerScheduler();
			var host = new NotificationHost(
				() => presenter,
				new NotificationWorkerThreadFactory(),
				dispatcher => { timers.Dispatcher = dispatcher; return timers; },
				logger ?? (_ => { }));
			return new HostContext(host, timers);
		}

		private static DesktopNotificationRequest CreateDesktop(string body, bool resident = false, uint display = 0)
			=> new DesktopNotificationRequest("title", "header-" + body, body, "resident-" + body, 1000, resident, CreateVisual(display));

		private static PinNotificationRequest CreatePin(string body, PinOperations operation, int? geometry)
			=> new PinNotificationRequest(
				"title", "header", body, 1000, operation,
				geometry.HasValue ? new PinTargetGeometry(geometry.Value, 2, 30, 40, 1, 1) : null,
				CreateVisual());

		private static NotificationVisualSettings CreateVisual(uint display = 0)
			=> new NotificationVisualSettings(
				display, WindowPlacement.Center, 0, 0, 0, 0, "Segoe UI", 12, 20,
				HorizontalAlignment.Center, HorizontalAlignment.Center, 0, false, 500, 200, 400, 100);

		private static void WaitForState(NotificationHost host, NotificationHostState expected)
		{
			Assert.True(SpinWait.SpinUntil(() => host.State == expected, TimeSpan.FromSeconds(5)), $"Actual: {host.State}");
		}

		private sealed class HostContext : IDisposable
		{
			internal HostContext(NotificationHost host, FakeTimerScheduler timers) { this.Host = host; this.Timers = timers; }
			internal NotificationHost Host { get; }
			internal FakeTimerScheduler Timers { get; }
			public void Dispose() => this.Host.Dispose();
		}

		private sealed class FakePresenter : INotificationPresenter
		{
			private readonly object _gate = new object();
			private readonly List<string> _desktopBodies = new List<string>();
			private readonly List<string> _pinBodies = new List<string>();
			private readonly List<FakePresentation> _presentations = new List<FakePresentation>();
			internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
			internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
			internal bool BlockNext;
			internal bool ThrowNext;
			internal bool ShutdownDispatcherOnNext;
			internal bool PostUnhandledOnNext;
			internal bool ThrowOnUpdateNext;

			internal string[] DesktopBodies { get { lock (this._gate) return this._desktopBodies.ToArray(); } }
			internal string[] PinBodies { get { lock (this._gate) return this._pinBodies.ToArray(); } }
			internal FakePresentation[] Presentations { get { lock (this._gate) return this._presentations.ToArray(); } }
			internal string ActiveDesktopBody
			{
				get
				{
					lock (this._gate)
					{
						for (var index = this._presentations.Count - 1; index >= 0; index--)
							if (!this._presentations[index].Disposed) return this._presentations[index].Body;
						return null;
					}
				}
			}

			public INotificationPresentation Present(DesktopNotificationRequest request)
			{
				if (this.BlockNext)
				{
					this.BlockNext = false;
					this.Entered.Set();
					this.Release.Wait();
				}
				if (this.ThrowNext)
				{
					this.ThrowNext = false;
					throw new InvalidOperationException("present");
				}
				var presentation = new FakePresentation(request.Body, this.ThrowOnUpdateNext);
				this.ThrowOnUpdateNext = false;
				lock (this._gate)
				{
					this._desktopBodies.Add(request.Body);
					this._presentations.Add(presentation);
				}
				if (this.PostUnhandledOnNext)
				{
					this.PostUnhandledOnNext = false;
					Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("unhandled")));
				}
				if (this.ShutdownDispatcherOnNext)
				{
					this.ShutdownDispatcherOnNext = false;
					Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
				}
				return presentation;
			}

			public IDisposable Present(PinNotificationRequest request)
			{
				lock (this._gate) this._pinBodies.Add(request.Body);
				return new DelegateDisposable(() => { });
			}

			internal void WaitForDesktopCount(int count)
				=> Assert.True(SpinWait.SpinUntil(() => this.DesktopBodies.Length >= count, TimeSpan.FromSeconds(5)));
			internal void WaitForPinCount(int count)
				=> Assert.True(SpinWait.SpinUntil(() => this.PinBodies.Length >= count, TimeSpan.FromSeconds(5)));
			internal void WaitForBody(string body)
				=> Assert.True(SpinWait.SpinUntil(() => this.DesktopBodies.Contains(body), TimeSpan.FromSeconds(5)));
		}

		private sealed class FakePresentation : INotificationPresentation
		{
			private readonly bool _throwOnUpdate;
			internal FakePresentation(string body, bool throwOnUpdate = false) { this.Body = body; this._throwOnUpdate = throwOnUpdate; }
			internal string Body { get; }
			internal string LastHeader { get; private set; }
			internal bool Disposed { get; private set; }
			public void Update(string title, string header, string body, NotificationVisualSettings visual)
			{
				if (this._throwOnUpdate) throw new InvalidOperationException("timer update");
				this.LastHeader = header;
			}
			public void Dispose() => this.Disposed = true;
		}

		private sealed class FakeTimerScheduler : INotificationTimerScheduler
		{
			private readonly object _gate = new object();
			private readonly List<FakeTimer> _timers = new List<FakeTimer>();
			internal Dispatcher Dispatcher { get; set; }
			public IDisposable Schedule(TimeSpan dueTime, Action callback)
			{
				var timer = new FakeTimer(this.Dispatcher, callback);
				lock (this._gate) this._timers.Add(timer);
				return timer;
			}
			internal FakeTimer WaitForTimer(int index)
			{
				Assert.True(SpinWait.SpinUntil(() => { lock (this._gate) return this._timers.Count > index; }, TimeSpan.FromSeconds(5)));
				lock (this._gate) return this._timers[index];
			}
		}

		private sealed class FakeTimer : IDisposable
		{
			private readonly Dispatcher _dispatcher;
			private readonly Action _callback;
			private int _disposed;
			internal FakeTimer(Dispatcher dispatcher, Action callback) { this._dispatcher = dispatcher; this._callback = callback; }
			internal void Fire()
			{
				if (Volatile.Read(ref this._disposed) != 0) return;
				this._dispatcher.Invoke(this._callback);
			}
			internal void FireEvenIfDisposed() => this._dispatcher.Invoke(this._callback);
			public void Dispose() => Interlocked.Exchange(ref this._disposed, 1);
		}

		private sealed class FakeWindowFactory : INotificationWindowFactory
		{
			private readonly Rect[] _areas;
			private readonly int _failShowIndex;
			private readonly int _failDataContextIndex;
			private readonly int _failDisposeIndex;
			internal FakeWindowFactory(int count, int failShowIndex = -1, int failDataContextIndex = -1, int failDisposeIndex = -1)
			{
				this._areas = Enumerable.Range(0, count).Select(i => new Rect(i, i, 100, 100)).ToArray();
				this._failShowIndex = failShowIndex;
				this._failDataContextIndex = failDataContextIndex;
				this._failDisposeIndex = failDisposeIndex;
			}
			internal List<FakeWindowHandle> Handles { get; } = new List<FakeWindowHandle>();
			public IReadOnlyList<Rect> GetSwitchAreas(uint display) => this._areas;
			public INotificationWindowHandle CreateSwitch(Rect area, NotificationVisualSettings visual)
			{
				var index = this.Handles.Count;
				var handle = new FakeWindowHandle(
					index == this._failShowIndex,
					index == this._failDataContextIndex,
					index == this._failDisposeIndex);
				this.Handles.Add(handle);
				return handle;
			}
			public INotificationWindowHandle CreatePin(PinTargetGeometry geometry, NotificationVisualSettings visual) => new FakeWindowHandle();
		}

		private sealed class FakeWindowHandle : INotificationWindowHandle
		{
			private readonly bool _failShow;
			private readonly bool _failDataContext;
			private readonly bool _failDispose;
			private object _dataContext;
			internal FakeWindowHandle(bool failShow = false, bool failDataContext = false, bool failDispose = false)
			{
				this._failShow = failShow;
				this._failDataContext = failDataContext;
				this._failDispose = failDispose;
			}
			public object DataContext
			{
				private get { return this._dataContext; }
				set
				{
					if (this._failDataContext) throw new InvalidOperationException("data context");
					this._dataContext = value;
				}
			}
			internal int ShowThreadId { get; private set; }
			internal bool Shown { get; private set; }
			internal bool Disposed { get; private set; }
			internal int DisposeCallCount { get; private set; }
			public void Show()
			{
				this.ShowThreadId = Thread.CurrentThread.ManagedThreadId;
				if (this._failShow) throw new InvalidOperationException("show");
				this.Shown = true;
			}
			public void Dispose()
			{
				this.DisposeCallCount++;
				if (this._failDispose) throw new InvalidOperationException("dispose");
				this.Disposed = true;
			}
		}
	}
}
