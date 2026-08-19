using System;
using System.Diagnostics;
using System.Windows.Threading;
using MetroTrilithon.Lifetime;
using SylphyHorn.Serialization;
using SylphyHorn.Services.DesktopTransitions;
using WindowsDesktop;

namespace SylphyHorn.Services
{
	public class NotificationService : IDisposable
	{
		public static NotificationService Instance { get; } = new NotificationService();
		private readonly SerialDisposable _notificationWindow = new SerialDisposable();
		private readonly INotificationPresenter _presenter;
		private Dispatcher _dispatcher;
		private DesktopTransitionRuntime _runtime;

		private NotificationService() : this(new NotificationPresenter())
		{
		}

		internal NotificationService(INotificationPresenter presenter)
		{
			this._presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
			VirtualDesktopService.WindowPinned += this.VirtualDesktopServiceOnWindowPinned;
		}

		internal void BindDesktopRuntime(DesktopTransitionRuntime runtime, Dispatcher dispatcher)
		{
			if (runtime == null) throw new ArgumentNullException(nameof(runtime));
			if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
			if (this._runtime != null)
			{
				if (ReferenceEquals(this._runtime, runtime)) return;
				throw new InvalidOperationException("NotificationService cannot be rebound to another desktop runtime.");
			}
			this._dispatcher = dispatcher;
			this._runtime = runtime;
			runtime.StateChanged += this.OnDesktopStateChanged;
		}

		public void ShowCurrentDesktop()
		{
			var request = this.CreateCurrentDesktopRequest();
			if (request != null) this._notificationWindow.Disposable = this._presenter.Present(request);
		}

		public void HideCurrentDesktop() => this._presenter.HideCurrentDesktop();

		public void ToggleCurrentDesktop()
		{
			var request = this.CreateCurrentDesktopRequest();
			var disposable = this._presenter.ToggleCurrentDesktop(request);
			if (disposable != null) this._notificationWindow.Disposable = disposable;
		}

		private void OnDesktopStateChanged(object sender, DesktopRuntimeStateChanged e)
		{
			var state = e.Change.Snapshot;
			if (e.Change.Moves.Count == 1)
			{
				var settings = NotificationSettingsSnapshot.Capture(Settings.General);
				var move = e.Change.Moves[0];
				if (TryGetCurrent(state, out var currentNumber, out var currentRecord))
					this.ShowMoved(currentNumber, move.NewIndex + 1, move.OldIndex + 1, GetName(currentRecord), settings);
				return;
			}
			if (e.Change.Kind != DesktopStateChangeKind.CurrentChanged && e.Change.Kind != DesktopStateChangeKind.Initialized && e.Change.Kind != DesktopStateChangeKind.Reset) return;
			var notificationSettings = NotificationSettingsSnapshot.Capture(Settings.General);
			if (!notificationSettings.NotificationWhenSwitchedDesktop)
			{
				if (notificationSettings.AlwaysShowDesktopNotification) this.ShowCurrentDesktop();
				return;
			}
			if (TryGetCurrent(state, out var number, out var record))
				this._notificationWindow.Disposable = this._presenter.Present(
					NotificationRequestMaterializer.CreateSwitched(number, GetName(record), notificationSettings));
		}

		private void ShowMoved(int currentNumber, int newNumber, int oldNumber, string currentName, NotificationSettingsSnapshot settings)
		{
			if (!settings.NotificationWhenSwitchedDesktop)
			{
				if (settings.AlwaysShowDesktopNotification) this.ShowCurrentDesktop();
				return;
			}
			this._notificationWindow.Disposable = this._presenter.Present(
				NotificationRequestMaterializer.CreateMoved(currentNumber, currentName, oldNumber, newNumber, settings));
		}

		private void VirtualDesktopServiceOnWindowPinned(object sender, WindowPinnedEventArgs e)
		{
			var dispatcher = this._dispatcher;
			Debug.Assert(dispatcher != null, "NotificationService must be bound before a window can be pinned.");
			if (dispatcher == null) return;
			dispatcher.BeginInvoke(
				new Action(() =>
				{
					var settings = NotificationSettingsSnapshot.Capture(Settings.General);
					var geometry = NotificationRequestMaterializer.CapturePinGeometry(e.Target);
					var request = NotificationRequestMaterializer.CreatePin(e.PinOperation, geometry, settings);
					this._notificationWindow.Disposable = this._presenter.Present(request);
				}),
				DispatcherPriority.Normal);
		}

		private DesktopNotificationRequest CreateCurrentDesktopRequest()
		{
			var state = this._runtime?.State;
			if (!TryGetCurrent(state, out var number, out var record)) return null;
			return NotificationRequestMaterializer.CreateCurrent(
				number,
				GetName(record),
				NotificationSettingsSnapshot.Capture(Settings.General));
		}

		private static string GetName(DesktopRecord record) => record?.Name.HasValue == true ? record.Name.Value : null;

		private static bool TryGetCurrent(DesktopRuntimeState state, out int number, out DesktopRecord record)
		{
			number = 0;
			record = null;
			if (state?.CurrentDesktopId == null || !state.Records.TryGetValue(state.CurrentDesktopId.Value, out record)) return false;
			number = state.Order.IndexOf(state.CurrentDesktopId.Value) + 1;
			return number > 0;
		}
		public void Dispose()
		{
			if (this._runtime != null) this._runtime.StateChanged -= this.OnDesktopStateChanged;
			this._runtime = null;
			this._dispatcher = null;
			VirtualDesktopService.WindowPinned -= this.VirtualDesktopServiceOnWindowPinned;
			this._notificationWindow.Dispose();
		}
	}
}
