using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MetroTrilithon.Lifetime;
using SylphyHorn.UI;
using SylphyHorn.UI.Bindings;

namespace SylphyHorn.Services
{
	internal interface INotificationPresenter
	{
		IDisposable Present(DesktopNotificationRequest request);
		IDisposable Present(PinNotificationRequest request);
		void HideCurrentDesktop();
		IDisposable ToggleCurrentDesktop(DesktopNotificationRequest showRequest);
	}

	internal sealed class NotificationPresenter : INotificationPresenter
	{
		private List<SwitchWindow> _residentWindows = new List<SwitchWindow>();

		public IDisposable Present(DesktopNotificationRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			this.CloseResidentWindows();

			var source = new CancellationTokenSource();
			if (request.Resident)
			{
				this._residentWindows = this.CreateSwitchWindows(request);
				Task.Delay(TimeSpan.FromMilliseconds(request.Duration), source.Token)
					.ContinueWith(_ => this._residentWindows.ForEach(window =>
					{
						window.DataContext = new NotificationWindowViewModel(
							request.Title,
							request.ResidentHeader,
							request.Body,
							request.Visual);
					}), source.Token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
			}
			else
			{
				var windows = this.CreateSwitchWindows(request);
				Task.Delay(TimeSpan.FromMilliseconds(request.Duration), source.Token)
					.ContinueWith(_ => windows.ForEach(window => window.Close()), TaskScheduler.FromCurrentSynchronizationContext());
			}

			return Disposable.Create(() => source.Cancel());
		}

		public IDisposable Present(PinNotificationRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			var source = new CancellationTokenSource();
			var window = new PinWindow(request.Geometry, request.Visual)
			{
				DataContext = new NotificationWindowViewModel(request.Title, request.Header, request.Body, request.Visual),
			};
			window.Show();

			Task.Delay(TimeSpan.FromMilliseconds(request.Duration), source.Token)
				.ContinueWith(_ => window.Close(), TaskScheduler.FromCurrentSynchronizationContext());

			return Disposable.Create(() => source.Cancel());
		}

		public void HideCurrentDesktop() => this.CloseResidentWindows();

		public IDisposable ToggleCurrentDesktop(DesktopNotificationRequest showRequest)
		{
			if (this._residentWindows.Count(window => window.IsVisible) == 0)
			{
				return showRequest == null ? null : this.Present(showRequest);
			}

			this.HideCurrentDesktop();
			return null;
		}

		private List<SwitchWindow> CreateSwitchWindows(DesktopNotificationRequest request)
		{
			var vmodel = new NotificationWindowViewModel(request.Title, request.Header, request.Body, request.Visual);
			var settings = request.Visual.Display;
			Monitor[] targets;
			if (settings == 0)
			{
				targets = new[] { MonitorService.GetCurrentArea() };
			}
			else
			{
				var monitors = MonitorService.GetAreas();
				targets = settings == uint.MaxValue
					? monitors
					: new[] { monitors[settings - 1] };
			}

			return targets.Select(area =>
			{
				var window = new SwitchWindow(area.WorkArea, request.Visual)
				{
					DataContext = vmodel,
				};
				window.Show();
				return window;
			}).ToList();
		}

		private void CloseResidentWindows()
		{
			if (this._residentWindows.Count == 0) return;
			this._residentWindows.ForEach(window => window.Close());
			this._residentWindows.Clear();
		}
	}
}
