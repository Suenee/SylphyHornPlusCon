using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Livet;
using MetroRadiance.UI;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Threading.Tasks;
using SylphyHorn.Interop;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.UI;

namespace SylphyHorn
{
	sealed partial class Application : IDisposableHolder
	{
		private readonly LivetCompositeDisposable _compositeDisposable = new LivetCompositeDisposable();
		private ApplicationPreparation _preparation;
		private int _shutdownStarted;

		internal HookService HookService { get; private set; }

		internal TaskTrayIcon TaskTrayIcon { get; private set; }

		protected override void OnStartup(StartupEventArgs e)
		{
			Args = new CommandLineArgs(e.Args);

			if (Args.Setup)
			{
				this.SetupShortcut();
			}

			if (!this.WaitUntilExplorerStarts())
			{
				MessageBox.Show("This application must start after Explorer is launched.", "Not ready", MessageBoxButton.OK, MessageBoxImage.Stop);
				this.BeginShutdown();
				return;
			}

#if !DEBUG
			var acquireTimeout = Args.Restarted.HasValue ? TimeSpan.FromSeconds(5) : TimeSpan.Zero;
			var appInstance = new SingleInstance(typeof(Application).Assembly, acquireTimeout).AddTo(this);
			if (!appInstance.IsFirst)
			{
				if (Args.Restarted.HasValue)
				{
					var now = DateTimeOffset.Now;
					ReportException(
						"SingleInstance",
						typeof(Application),
						new TimeoutException($@"Failed to acquire the application Mutex within the restart timeout.
Mutex name: {appInstance.MutexName}
Wait time: {acquireTimeout}
Restarted: {Args.Restarted.Value}
PID: {Process.GetCurrentProcess().Id}
Time: {now:O}"));
					return;
				}

				this.BeginShutdown();
				return;
			}
#endif

			if (ProductInfo.OSBuild >= 14393)
			{
				this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
				DispatcherHelper.UIDispatcher = this.Dispatcher;

				this.DispatcherUnhandledException += this.HandleDispatcherUnhandledException;
				TaskLog.Occured += (sender, log) => LoggingService.Instance.Register(log);

				LocalSettingsProvider.Instance.LoadOrMigrateAsync().Wait();

				Settings.General.Culture.Subscribe(x => ResourceService.Current.ChangeCulture(x)).AddTo(this);
				ThemeService.Current.Register(this, Theme.Windows, Accent.Windows);

				this.HookService = new HookService().AddTo(this);

				this._preparation = new ApplicationPreparation(this.HookService, this.BeginShutdown, this);
				var preparation = this._preparation;
				this.TaskTrayIcon = preparation.CreateTaskTrayIcon().AddTo(this);

				if (Settings.General.FirstTime)
				{
					preparation.CreateFirstTimeBaloon().Show();

					Settings.General.FirstTime.Value = false;
					LocalSettingsProvider.Instance.SaveAsync().Forget();
				}

				preparation.VirtualDesktopInitialized += () =>
				{
					this.TaskTrayIcon.Show();
					this.TaskTrayIcon.Reload();
					if (Settings.General.AlwaysShowDesktopNotification)
					{
						NotificationService.Instance.ShowCurrentDesktop();
					}
				};
				preparation.VirtualDesktopInitializationCanceled += () => this.BeginShutdown(); // ToDo
				preparation.VirtualDesktopInitializationFailed += (ex, autoRestart) =>
				{
					this.TaskTrayIcon.Show();
					LoggingService.Instance.Register(ex);

					if ((Args.Restarted == null || Args.Restarted == 0) && autoRestart)
					{
						try
						{
							Restart();
							this.BeginShutdown();
							return;
						}
						catch (Exception ex2)
						{
							LoggingService.Instance.Register(ex2);
						}
					}
					this.RestartOrShutdown("Virtual desktop initialization is failed.", "Virtual Desktop Initialization Failed");
				};
				preparation.PrepareVirtualDesktop();

				NotificationService.Instance.AddTo(this);
				WallpaperService.Instance.AddTo(this);

				base.OnStartup(e);
			}
			else
			{
				MessageBox.Show("This application is supported on Windows 10 Anniversary Update (build 14393) or later.", "Not supported", MessageBoxButton.OK, MessageBoxImage.Stop);
				this.BeginShutdown();
			}
		}

		private async void BeginShutdown()
		{
			if (Interlocked.Exchange(ref this._shutdownStarted, 1) != 0) return;
			try
			{
				if (this._preparation != null) await this._preparation.ShutdownAsync();
			}
			catch (Exception ex)
			{
				LoggingService.Instance.Register(ex);
			}
			base.Shutdown();
		}
		protected override void OnExit(ExitEventArgs e)
		{
			base.OnExit(e);
			((IDisposable)this).Dispose();
		}

		private void SetupShortcut()
		{
			var startup = new Startup();
			if (!startup.IsExists)
			{
				startup.Create();
			}
		}

		private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
		{
			LoggingService.Instance.Register(args.Exception);
			args.Handled = true;
		}

		private bool WaitUntilExplorerStarts()
		{
			const string explorerProcessName = "explorer";
			if (Process.GetProcessesByName(explorerProcessName).Length > 0)
			{
				return true;
			}

			const int tryCount = 5;
			const int timeout = 5000;
			const int interval = timeout / tryCount;
			for (var i = 0; i < tryCount; ++i)
			{
				Thread.Sleep(interval);
				if (Process.GetProcessesByName(explorerProcessName).Length > 0)
				{
					return true;
				}
			}
			return false;
		}

		private void RestartOrShutdown(string message, string caption)
		{
			var result = MessageBox.Show(
				$"{message}\n\nDo you want to restart {ProductInfo.Title} now?",
				caption,
				MessageBoxButton.YesNo,
				MessageBoxImage.Stop
			);
			if (result == MessageBoxResult.Yes)
			{
				try
				{
					Restart();
				}
				catch (Exception ex)
				{
					LoggingService.Instance.Register(ex);
					this.BeginShutdown();
					return;
				}
			}
			this.BeginShutdown();
		}

		#region IDisposable members

		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this._compositeDisposable;

		void IDisposable.Dispose()
		{
			this._compositeDisposable.Dispose();
		}

		#endregion
	}
}
