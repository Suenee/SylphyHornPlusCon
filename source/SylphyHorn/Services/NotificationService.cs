using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MetroTrilithon.Lifetime;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services.DesktopTransitions;
using SylphyHorn.UI;
using SylphyHorn.UI.Bindings;
using WindowsDesktop;

namespace SylphyHorn.Services
{
	public class NotificationService : IDisposable
	{
		public static NotificationService Instance { get; } = new NotificationService();
		private static string ResidentHeader => NotificationTextFormatter.CreateResidentHeader(Settings.General.SimpleNotification);
		private static string SwitchedHeader => NotificationTextFormatter.CreateSwitchedHeader(Settings.General.SimpleNotification);
		private static List<SwitchWindow> _residentWindows = new List<SwitchWindow>();
		private readonly SerialDisposable _notificationWindow = new SerialDisposable();
		private DesktopTransitionRuntime _runtime;

		private NotificationService()
		{
			VirtualDesktopService.WindowPinned += this.VirtualDesktopServiceOnWindowPinned;
		}

		internal void BindDesktopRuntime(DesktopTransitionRuntime runtime)
		{
			if (runtime == null) throw new ArgumentNullException(nameof(runtime));
			if (this._runtime != null)
			{
				if (ReferenceEquals(this._runtime, runtime)) return;
				throw new InvalidOperationException("NotificationService cannot be rebound to another desktop runtime.");
			}
			this._runtime = runtime;
			runtime.StateChanged += this.OnDesktopStateChanged;
		}

		public void ShowCurrentDesktop()
		{
			var state = this._runtime?.State;
			if (!TryGetCurrent(state, out var number, out var record)) return;
			this._notificationWindow.Disposable = ShowDesktopWindow(number, ResidentHeader, record);
		}

		public void HideCurrentDesktop() => CloseResidentWindows();

		public void ToggleCurrentDesktop()
		{
			if (_residentWindows.Count(window => window.IsVisible) == 0) this.ShowCurrentDesktop();
			else this.HideCurrentDesktop();
		}

		private void OnDesktopStateChanged(object sender, DesktopRuntimeStateChanged e)
		{
			var state = e.Change.Snapshot;
			if (e.Change.Moves.Count == 1)
			{
				var move = e.Change.Moves[0];
				if (TryGetCurrent(state, out var currentNumber, out var currentRecord))
					this.ShowMoved(currentNumber, move.NewIndex + 1, move.OldIndex + 1, currentRecord);
				return;
			}
			if (e.Change.Kind != DesktopStateChangeKind.CurrentChanged && e.Change.Kind != DesktopStateChangeKind.Initialized && e.Change.Kind != DesktopStateChangeKind.Reset) return;
			if (!Settings.General.NotificationWhenSwitchedDesktop)
			{
				if (Settings.General.AlwaysShowDesktopNotification) this.ShowCurrentDesktop();
				return;
			}
			if (TryGetCurrent(state, out var number, out var record)) this._notificationWindow.Disposable = ShowDesktopWindow(number, SwitchedHeader, record);
		}

		private void ShowMoved(int currentNumber, int newNumber, int oldNumber, DesktopRecord currentRecord)
		{
			if (!Settings.General.NotificationWhenSwitchedDesktop)
			{
				if (Settings.General.AlwaysShowDesktopNotification) this.ShowCurrentDesktop();
				return;
			}
			var header = NotificationTextFormatter.CreateMovedHeader(oldNumber, newNumber, Settings.General.SimpleNotification);
			this._notificationWindow.Disposable = ShowDesktopWindow(header, CreateNotificationBody(currentNumber, currentRecord, true));
		}

		private void VirtualDesktopServiceOnWindowPinned(object sender, WindowPinnedEventArgs e)
		{
			VisualHelper.InvokeOnUIDispatcher(() => this._notificationWindow.Disposable = ShowPinWindow(e.Target, e.PinOperation));
		}

		private static IDisposable ShowDesktopWindow(int number, string header, DesktopRecord record)
			=> ShowDesktopWindow(header, CreateNotificationBody(number, record, false));

		private static string CreateNotificationBody(int number, DesktopRecord record, bool moved)
		{
			var name = record?.Name.HasValue == true ? record.Name.Value : null;
			return NotificationTextFormatter.CreateDesktopBody(
				number,
				name,
				Settings.General.UseDesktopName,
				Settings.General.SimpleNotification,
				moved);
		}

		private static bool TryGetCurrent(DesktopRuntimeState state, out int number, out DesktopRecord record)
		{
			number = 0;
			record = null;
			if (state?.CurrentDesktopId == null || !state.Records.TryGetValue(state.CurrentDesktopId.Value, out record)) return false;
			number = state.Order.IndexOf(state.CurrentDesktopId.Value) + 1;
			return number > 0;
		}
		private static IDisposable ShowDesktopWindow(string header, string body)
		{
			CloseResidentWindows();

			var source = new CancellationTokenSource();

			if (Settings.General.AlwaysShowDesktopNotification)
			{
				_residentWindows = CreateSwitchWindows(header, body);

				Task.Delay(TimeSpan.FromMilliseconds(Settings.General.NotificationDuration), source.Token)
					.ContinueWith(_ => _residentWindows.ForEach(window =>
					{
						window.DataContext = new NotificationWindowViewModel
						{
							Title = ProductInfo.Title,
							Header = ResidentHeader,
							Body = body,
						};
					}), source.Token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());

				return Disposable.Create(() => source.Cancel());
			}
			else
			{
				var windows = CreateSwitchWindows(header, body);

				Task.Delay(TimeSpan.FromMilliseconds(Settings.General.NotificationDuration), source.Token)
					.ContinueWith(_ => windows.ForEach(window => window.Close()), TaskScheduler.FromCurrentSynchronizationContext());

				return Disposable.Create(() => source.Cancel());
			}
		}

		private static List<SwitchWindow> CreateSwitchWindows(string header, string body)
		{
			var vmodel = new NotificationWindowViewModel
			{
				Title = ProductInfo.Title,
				Header = header,
				Body = body,
			};

			var settings = Settings.General.Display.Value;
			Monitor[] targets;
			if (settings == 0)
			{
				targets = new[] { MonitorService.GetCurrentArea() };
			}
			else
			{
				var monitors = MonitorService.GetAreas();
				if (settings == uint.MaxValue)
				{
					targets = monitors;
				}
				else
				{
					targets = new[] { monitors[settings - 1] };
				}
			}

			return targets.Select(area =>
			{
				var window = new SwitchWindow(area.WorkArea)
				{
					DataContext = vmodel,
				};
				window.Show();
				return window;
			}).ToList();
		}

		private static void CloseResidentWindows()
		{
			if (_residentWindows.Count > 0)
			{
				_residentWindows.ForEach(window => window.Close());
				_residentWindows.Clear();
			}
		}

		private static IDisposable ShowPinWindow(IntPtr hWnd, PinOperations operation)
		{
			var simple = Settings.General.SimpleNotification;
			var vmodel = new NotificationWindowViewModel
			{
				Title = ProductInfo.Title,
				Header = NotificationTextFormatter.CreatePinHeader(simple),
				Body = NotificationTextFormatter.CreatePinBody(operation, simple),
			};
			var source = new CancellationTokenSource();
			var window = new PinWindow(hWnd)
			{
				DataContext = vmodel,
			};
			window.Show();

			Task.Delay(TimeSpan.FromMilliseconds(Settings.General.NotificationDuration), source.Token)
				.ContinueWith(_ => window.Close(), TaskScheduler.FromCurrentSynchronizationContext());

			return Disposable.Create(() => source.Cancel());
		}

		public void Dispose()
		{
			if (this._runtime != null) this._runtime.StateChanged -= this.OnDesktopStateChanged;
			this._runtime = null;
			VirtualDesktopService.WindowPinned -= this.VirtualDesktopServiceOnWindowPinned;
			this._notificationWindow.Dispose();
		}
	}
}
