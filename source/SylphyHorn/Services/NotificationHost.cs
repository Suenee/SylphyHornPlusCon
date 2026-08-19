using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace SylphyHorn.Services
{
	internal enum NotificationHostState
	{
		NotStarted,
		Starting,
		Running,
		Stopping,
		Stopped,
		Faulted,
	}

	internal interface INotificationHost : IDisposable
	{
		void EnqueueState(DesktopNotificationRequest request);
		void EnqueueShow(DesktopNotificationRequest request);
		void EnqueueHide();
		void EnqueueToggle(DesktopNotificationRequest request);
		void EnqueuePin(PinNotificationRequest request);
	}

	internal interface INotificationTimerScheduler
	{
		IDisposable Schedule(TimeSpan dueTime, Action callback);
	}

	internal interface INotificationWorkerThread
	{
		bool IsCurrent { get; }
		void Start();
	}

	internal interface INotificationWorkerThreadFactory
	{
		INotificationWorkerThread Create(ThreadStart start);
	}

	internal sealed class NotificationHost : INotificationHost
	{
		internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

		private readonly object _gate = new object();
		private readonly LinkedList<NotificationOperation> _pending = new LinkedList<NotificationOperation>();
		private readonly Func<INotificationPresenter> _presenterFactory;
		private readonly INotificationWorkerThreadFactory _threadFactory;
		private readonly Func<Dispatcher, INotificationTimerScheduler> _timerFactory;
		private readonly Action<Exception> _logger;
		private readonly ManualResetEventSlim _threadExited = new ManualResetEventSlim();
		private LinkedListNode<NotificationOperation> _pendingState;
		private INotificationWorkerThread _thread;
		private Dispatcher _dispatcher;
		private INotificationPresenter _presenter;
		private INotificationTimerScheduler _timers;
		private INotificationPresentation _desktopPresentation;
		private IDisposable _desktopTimer;
		private readonly HashSet<IDisposable> _pinPresentations = new HashSet<IDisposable>();
		private readonly HashSet<IDisposable> _pinTimers = new HashSet<IDisposable>();
		private long _desktopGeneration;
		private bool _drainScheduled;
		private NotificationHostState _state;

		internal NotificationHost()
			: this(
				() => new NotificationPresenter(),
				new NotificationWorkerThreadFactory(),
				dispatcher => new DispatcherNotificationTimerScheduler(dispatcher),
				LoggingService.Instance.Register)
		{
		}

		internal NotificationHost(
			Func<INotificationPresenter> presenterFactory,
			INotificationWorkerThreadFactory threadFactory,
			Func<Dispatcher, INotificationTimerScheduler> timerFactory,
			Action<Exception> logger)
		{
			this._presenterFactory = presenterFactory ?? throw new ArgumentNullException(nameof(presenterFactory));
			this._threadFactory = threadFactory ?? throw new ArgumentNullException(nameof(threadFactory));
			this._timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
			this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		internal NotificationHostState State
		{
			get { lock (this._gate) return this._state; }
		}

		public void EnqueueState(DesktopNotificationRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			this.Enqueue(NotificationOperation.State(request), true);
		}

		public void EnqueueShow(DesktopNotificationRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			this.Enqueue(NotificationOperation.Show(request), false);
		}

		public void EnqueueHide() => this.Enqueue(NotificationOperation.Hide(), false);

		public void EnqueueToggle(DesktopNotificationRequest request) => this.Enqueue(NotificationOperation.Toggle(request), false);

		public void EnqueuePin(PinNotificationRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (request.Geometry == null)
			{
				this.LogBestEffort(new InvalidOperationException("Pin notification was dropped because target geometry could not be captured."));
				return;
			}
			this.Enqueue(NotificationOperation.CreatePin(request), false);
		}

		private void Enqueue(NotificationOperation operation, bool coalesceState)
		{
			INotificationWorkerThread threadToStart = null;
			Exception failure = null;
			lock (this._gate)
			{
				if (this.IsTerminalOrStopping()) return;

				if (coalesceState && this._pendingState != null)
				{
					this._pending.Remove(this._pendingState);
					this._pendingState = null;
				}
				var node = this._pending.AddLast(operation);
				if (coalesceState) this._pendingState = node;

				if (this._state == NotificationHostState.NotStarted)
				{
					this._state = NotificationHostState.Starting;
					try
					{
						this._thread = this._threadFactory.Create(this.ThreadMain);
						threadToStart = this._thread;
					}
					catch (Exception ex)
					{
						this.TransitionToFaultedLocked();
						failure = ex;
					}
				}
				else if (this._state == NotificationHostState.Running)
				{
					failure = this.ScheduleDrainLocked();
				}
			}

			if (failure != null) this.LogBestEffort(failure);
			if (threadToStart == null) return;
			try
			{
				threadToStart.Start();
			}
			catch (Exception ex)
			{
				lock (this._gate) this.TransitionToFaultedLocked();
				this._threadExited.Set();
				this.LogBestEffort(ex);
			}
		}

		private void ThreadMain()
		{
			Dispatcher dispatcher = null;
			try
			{
				dispatcher = Dispatcher.CurrentDispatcher;
				SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
				dispatcher.UnhandledException += this.OnDispatcherUnhandledException;

				lock (this._gate)
				{
					if (this._state != NotificationHostState.Starting) return;
				}

				var presenter = this._presenterFactory();
				var timers = this._timerFactory(dispatcher);
				Exception scheduleFailure = null;
				lock (this._gate)
				{
					if (this._state != NotificationHostState.Starting) return;
					this._dispatcher = dispatcher;
					this._presenter = presenter;
					this._timers = timers;
					this._state = NotificationHostState.Running;
					scheduleFailure = this.ScheduleDrainLocked();
				}
				if (scheduleFailure != null)
				{
					this.LogBestEffort(scheduleFailure);
					dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
				}

				Dispatcher.Run();

				Exception unexpected = null;
				lock (this._gate)
				{
					if (this._state == NotificationHostState.Stopping)
						this._state = NotificationHostState.Stopped;
					else if (this._state != NotificationHostState.Faulted)
					{
						this.TransitionToFaultedLocked();
						unexpected = new InvalidOperationException("Notification Dispatcher exited unexpectedly.");
					}
				}
				if (unexpected != null) this.LogBestEffort(unexpected);
			}
			catch (Exception ex)
			{
				lock (this._gate) this.TransitionToFaultedLocked();
				this.LogBestEffort(ex);
			}
			finally
			{
				this.CleanupPresentationsBestEffort();
				if (dispatcher != null) dispatcher.UnhandledException -= this.OnDispatcherUnhandledException;
				lock (this._gate)
				{
					this._dispatcher = null;
					this._presenter = null;
					this._timers = null;
					if (this._state == NotificationHostState.Stopping) this._state = NotificationHostState.Stopped;
				}
				this._threadExited.Set();
			}
		}

		private Exception ScheduleDrainLocked()
		{
			if (this._drainScheduled || this._pending.Count == 0 || this._dispatcher == null) return null;
			try
			{
				this._drainScheduled = true;
				this._dispatcher.BeginInvoke(new Action(this.Drain), DispatcherPriority.Normal);
				return null;
			}
			catch (Exception ex)
			{
				this._drainScheduled = false;
				this.TransitionToFaultedLocked();
				return ex;
			}
		}

		private void Drain()
		{
			while (true)
			{
				NotificationOperation operation;
				lock (this._gate)
				{
					if (this._state != NotificationHostState.Running || this._pending.Count == 0)
					{
						this._drainScheduled = false;
						return;
					}
					var first = this._pending.First;
					operation = first.Value;
					if (ReferenceEquals(first, this._pendingState)) this._pendingState = null;
					this._pending.RemoveFirst();
				}

				try
				{
					this.Process(operation);
				}
				catch (Exception ex)
				{
					if (operation.Kind != NotificationOperationKind.Pin) this.InvalidateDesktopBestEffort();
					this.LogBestEffort(ex);
				}
			}
		}

		private void Process(NotificationOperation operation)
		{
			switch (operation.Kind)
			{
				case NotificationOperationKind.State:
				case NotificationOperationKind.Show:
					this.ShowDesktop(operation.Desktop);
					break;
				case NotificationOperationKind.Hide:
					this.InvalidateDesktopBestEffort();
					break;
				case NotificationOperationKind.Toggle:
					if (this._desktopPresentation != null) this.InvalidateDesktopBestEffort();
					else if (operation.Desktop != null) this.ShowDesktop(operation.Desktop);
					break;
				case NotificationOperationKind.Pin:
					this.ShowPin(operation.Pin);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void ShowDesktop(DesktopNotificationRequest request)
		{
			this.InvalidateDesktopBestEffort();
			var generation = this._desktopGeneration;
			INotificationPresentation presentation = null;
			try
			{
				presentation = this._presenter.Present(request)
					?? throw new InvalidOperationException("The notification presenter returned no desktop presentation.");
				this._desktopPresentation = presentation;
				this._desktopTimer = this._timers.Schedule(
					TimeSpan.FromMilliseconds(request.Duration),
					() => this.OnDesktopTimer(generation, request));
			}
			catch
			{
				this._desktopPresentation = null;
				if (presentation != null) this.DisposeBestEffort(presentation);
				this._desktopGeneration++;
				throw;
			}
		}

		private void OnDesktopTimer(long generation, DesktopNotificationRequest request)
		{
			try
			{
				if (generation != this._desktopGeneration || this._desktopPresentation == null) return;
				if (request.Resident)
					this._desktopPresentation.Update(request.Title, request.ResidentHeader, request.Body, request.Visual);
				else
					this.InvalidateDesktopBestEffort();
			}
			catch (Exception ex)
			{
				this.InvalidateDesktopBestEffort();
				this.LogBestEffort(ex);
			}
		}

		private void ShowPin(PinNotificationRequest request)
		{
			var presentation = this._presenter.Present(request)
				?? throw new InvalidOperationException("The notification presenter returned no pin presentation.");
			this._pinPresentations.Add(presentation);
			try
			{
				IDisposable timer = null;
				timer = this._timers.Schedule(TimeSpan.FromMilliseconds(request.Duration), () =>
				{
					try
					{
						this._pinTimers.Remove(timer);
						if (this._pinPresentations.Remove(presentation)) presentation.Dispose();
					}
					catch (Exception ex)
					{
						this.LogBestEffort(ex);
					}
				});
				this._pinTimers.Add(timer);
			}
			catch
			{
				this._pinPresentations.Remove(presentation);
				this.DisposeBestEffort(presentation);
				throw;
			}
		}

		private void InvalidateDesktopBestEffort()
		{
			this._desktopGeneration++;
			var timer = this._desktopTimer;
			this._desktopTimer = null;
			if (timer != null) this.DisposeBestEffort(timer);
			var presentation = this._desktopPresentation;
			this._desktopPresentation = null;
			if (presentation != null) this.DisposeBestEffort(presentation);
		}

		private void CleanupPresentationsBestEffort()
		{
			this.InvalidateDesktopBestEffort();
			foreach (var timer in this._pinTimers) this.DisposeBestEffort(timer);
			this._pinTimers.Clear();
			foreach (var presentation in this._pinPresentations) this.DisposeBestEffort(presentation);
			this._pinPresentations.Clear();
		}

		private void DisposeBestEffort(IDisposable disposable)
		{
			try { disposable.Dispose(); }
			catch (Exception ex) { this.LogBestEffort(ex); }
		}

		private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			e.Handled = true;
			lock (this._gate) this.TransitionToFaultedLocked();
			this.CleanupPresentationsBestEffort();
			this.LogBestEffort(e.Exception);
			try { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
			catch (Exception ex) { this.LogBestEffort(ex); }
		}

		private bool IsTerminalOrStopping()
		{
			return this._state == NotificationHostState.Stopping
				|| this._state == NotificationHostState.Stopped
				|| this._state == NotificationHostState.Faulted;
		}

		private void TransitionToFaultedLocked()
		{
			if (this._state == NotificationHostState.Stopped || this._state == NotificationHostState.Faulted) return;
			this._state = NotificationHostState.Faulted;
			this._pending.Clear();
			this._pendingState = null;
			this._drainScheduled = false;
		}

		private void LogBestEffort(Exception exception)
		{
			try { this._logger(exception); }
			catch { }
		}

		public void Dispose()
		{
			Dispatcher dispatcher = null;
			INotificationWorkerThread thread = null;
			lock (this._gate)
			{
				if (this._state == NotificationHostState.Stopped || this._state == NotificationHostState.Faulted) return;
				if (this._state == NotificationHostState.NotStarted)
				{
					this._state = NotificationHostState.Stopped;
					this._pending.Clear();
					this._threadExited.Set();
					return;
				}
				if (this._state != NotificationHostState.Stopping) this._state = NotificationHostState.Stopping;
				this._pending.Clear();
				this._pendingState = null;
				dispatcher = this._dispatcher;
				thread = this._thread;
			}

			if (dispatcher != null)
			{
				try
				{
					dispatcher.BeginInvoke(new Action(() =>
					{
						this.CleanupPresentationsBestEffort();
						dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
					}), DispatcherPriority.Send);
				}
				catch (Exception ex) { this.LogBestEffort(ex); }
			}

			if (thread == null || !thread.IsCurrent) this._threadExited.Wait(ShutdownTimeout);
		}

		private enum NotificationOperationKind { State, Show, Hide, Toggle, Pin }

		private sealed class NotificationOperation
		{
			internal NotificationOperationKind Kind { get; }
			internal DesktopNotificationRequest Desktop { get; }
			internal PinNotificationRequest Pin { get; }

			private NotificationOperation(NotificationOperationKind kind, DesktopNotificationRequest desktop, PinNotificationRequest pin)
			{
				this.Kind = kind;
				this.Desktop = desktop;
				this.Pin = pin;
			}

			internal static NotificationOperation State(DesktopNotificationRequest request) => new NotificationOperation(NotificationOperationKind.State, request, null);
			internal static NotificationOperation Show(DesktopNotificationRequest request) => new NotificationOperation(NotificationOperationKind.Show, request, null);
			internal static NotificationOperation Hide() => new NotificationOperation(NotificationOperationKind.Hide, null, null);
			internal static NotificationOperation Toggle(DesktopNotificationRequest request) => new NotificationOperation(NotificationOperationKind.Toggle, request, null);
			internal static NotificationOperation CreatePin(PinNotificationRequest request) => new NotificationOperation(NotificationOperationKind.Pin, null, request);
		}
	}

	internal sealed class NotificationWorkerThreadFactory : INotificationWorkerThreadFactory
	{
		public INotificationWorkerThread Create(ThreadStart start) => new NotificationWorkerThread(start);
	}

	internal sealed class NotificationWorkerThread : INotificationWorkerThread
	{
		private readonly Thread _thread;

		internal NotificationWorkerThread(ThreadStart start)
		{
			this._thread = new Thread(start)
			{
				Name = "SylphyHorn notification host",
				IsBackground = true,
			};
			this._thread.SetApartmentState(ApartmentState.STA);
		}

		public bool IsCurrent => Thread.CurrentThread == this._thread;
		public void Start() => this._thread.Start();
	}

	internal sealed class DispatcherNotificationTimerScheduler : INotificationTimerScheduler
	{
		private readonly Dispatcher _dispatcher;

		internal DispatcherNotificationTimerScheduler(Dispatcher dispatcher)
		{
			this._dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		}

		public IDisposable Schedule(TimeSpan dueTime, Action callback)
		{
			if (callback == null) throw new ArgumentNullException(nameof(callback));
			var timer = new DispatcherTimer(DispatcherPriority.Normal, this._dispatcher) { Interval = dueTime };
			EventHandler handler = null;
			handler = (sender, e) =>
			{
				timer.Stop();
				timer.Tick -= handler;
				callback();
			};
			timer.Tick += handler;
			timer.Start();
			return new DelegateDisposable(() =>
			{
				timer.Stop();
				timer.Tick -= handler;
			});
		}
	}

	internal sealed class DelegateDisposable : IDisposable
	{
		private Action _dispose;
		internal DelegateDisposable(Action dispose) { this._dispose = dispose; }
		public void Dispose() => Interlocked.Exchange(ref this._dispose, null)?.Invoke();
	}
}
