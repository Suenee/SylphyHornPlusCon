using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindowsDesktop;
using MetroTrilithon.Lifetime;
using SylphyHorn.Interop;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using SylphyHorn.UI;
using SylphyHorn.UI.Bindings;

namespace SylphyHorn
{
	using ActionRegister = Func<Func<ShortcutKey>, Action<IntPtr>, IDisposable>;

	public class ApplicationPreparation
	{
		private readonly HookService _hookService;
		private readonly Action _shutdownAction;
		private readonly IDisposableHolder _disposable;
		private readonly StartupTrace _startupTrace;
		private TaskTrayIcon _taskTrayIcon;
		private DesktopTransitionRuntime _desktopRuntime;

		public event Action VirtualDesktopInitialized;
		public event Action VirtualDesktopInitializationCanceled;
		public event Action<Exception, bool> VirtualDesktopInitializationFailed;

		public ApplicationPreparation(HookService hookService, Action shutdownAction, IDisposableHolder disposable)
			: this(hookService, shutdownAction, disposable, null)
		{
		}

		internal ApplicationPreparation(HookService hookService, Action shutdownAction, IDisposableHolder disposable, StartupTrace startupTrace)
		{
			this._hookService = hookService;
			this._shutdownAction = shutdownAction;
			this._disposable = disposable;
			this._startupTrace = startupTrace;
			this._hookService.Reload = this.RegisterActions;
		}

		public void RegisterActions()
		{
			this.RegisterActions(Settings.ShortcutKey, this._hookService.RegisterKeyAction);
			this.RegisterActions(Settings.MouseShortcut, this._hookService.RegisterMouseAction);
		}

		public TaskTrayIcon CreateTaskTrayIcon()
		{
			if (this._taskTrayIcon == null)
			{
				const string iconUri = "pack://application:,,,/SylphyHorn;Component/.assets/tasktray.dark.ico";
				const string lightIconUri = "pack://application:,,,/SylphyHorn;Component/.assets/tasktray.light.ico";
				if (!Uri.TryCreate(iconUri, UriKind.Absolute, out var uri)) return null;
				if (!Uri.TryCreate(lightIconUri, UriKind.Absolute, out var lightUri)) return null;
				var darkIcon = IconHelper.GetIconFromResource(uri);
				var lightIcon = IconHelper.GetIconFromResource(lightUri);
				var menus = new[]
				{
					new TaskTrayIconItem(
						Resources.TaskTray_Menu_Settings,
						this.ShowSettings,
						() => Application.Args.CanSettings,
						() => this._desktopRuntime?.IsInitialized == true),
					new TaskTrayIconItem(Resources.TaskTray_Menu_Exit, this._shutdownAction),
#if DEBUG
					new TaskTrayIconItem("Tasktray Icon Test", () => new TaskTrayTestWindow().Show()),
#endif
				};
				this._taskTrayIcon = new TaskTrayIcon(darkIcon, lightIcon, menus);
			}
			return this._taskTrayIcon;
		}

		private void ShowSettings()
		{
			if (this._desktopRuntime == null || !this._desktopRuntime.IsInitialized) return;
			if (SettingsWindow.Instance != null) SettingsWindow.Instance.Activate();
			else
			{
				var window = new SettingsWindow();
				var dialogService = new SettingsDialogService();
				window.DataContext = new SettingsWindowViewModel(this._hookService, this._desktopRuntime, dialogService);
				SettingsWindow.Instance = window;
				window.ShowDialog();
				SettingsWindow.Instance = null;
			}
		}

		public TaskTrayBaloon CreateFirstTimeBaloon()
		{
			var baloon = this.CreateTaskTrayIcon().CreateBaloon();
			baloon.Title = ProductInfo.Title;
			baloon.Text = Resources.TaskTray_FirstTimeMessage;
			baloon.Timespan = TimeSpan.FromMilliseconds(5000);
			return baloon;
		}

		public void PrepareVirtualDesktop()
		{
			var provider = new VirtualDesktopProvider { ComInterfaceAssemblyPath = Path.Combine(Directories.LocalAppData.FullName, "assemblies") };
			provider.EnableDispatcherEventScheduling(Application.Current.Dispatcher);
			VirtualDesktop.Provider = provider;
			provider.Initialize().ContinueWith(task => this.CompleteProviderInitialization(task, provider), TaskScheduler.FromCurrentSynchronizationContext());
		}

		internal async void CompleteProviderInitialization(Task initialization, VirtualDesktopProvider provider)
		{
			var runtimeInitialized = false;
			if (initialization.IsCanceled)
			{
				this._startupTrace?.Write(StartupPhase.ProviderInitCompleted, StartupTraceResult.Cancelled);
				this.VirtualDesktopInitializationCanceled?.Invoke();
				return;
			}
			if (initialization.IsFaulted)
			{
				var exception = initialization.Exception;
				this._startupTrace?.Write(StartupPhase.ProviderInitCompleted, StartupTraceResult.Failed, exception?.GetType(), exception?.HResult ?? 0);
				this.VirtualDesktopInitializationFailed?.Invoke(initialization.Exception, false);
				return;
			}

			try
			{
				this._startupTrace?.Write(StartupPhase.ProviderInitCompleted, StartupTraceResult.Succeeded);
				var runtime = new DesktopTransitionRuntime(
					new VirtualDesktopProviderClient(provider),
					new ApplicationDesktopSettingsTransactions(LocalSettingsProvider.Instance),
					new DispatcherDesktopOwnerContext(Application.Current.Dispatcher),
					new VirtualDesktopOperations());
				runtime.Faulted += (sender, fault) => LoggingService.Instance.Register(new DesktopRuntimeLog(fault));
				var result = await runtime.InitializeAsync(Settings.General.OverrideDesktopsOnStartup, CancellationToken.None);
				if (!result.Succeeded)
				{
					this._startupTrace?.Write(
						StartupPhase.RuntimeInitialized,
						result.Status == DesktopRuntimeInitializationStatus.Cancelled || result.Status == DesktopRuntimeInitializationStatus.ShuttingDown
							? StartupTraceResult.Cancelled
							: StartupTraceResult.Failed);
					if (result.Status == DesktopRuntimeInitializationStatus.Cancelled || result.Status == DesktopRuntimeInitializationStatus.ShuttingDown) this.VirtualDesktopInitializationCanceled?.Invoke();
					else this.VirtualDesktopInitializationFailed?.Invoke(new InvalidOperationException("Virtual desktop runtime initialization did not produce a stable state."), false);
					return;
				}
				this._startupTrace?.Write(StartupPhase.RuntimeInitialized, StartupTraceResult.Succeeded);
				runtimeInitialized = true;

				this._desktopRuntime = runtime;
				runtime.AddTo(this._disposable);
				this.CreateTaskTrayIcon().BindDesktopRuntime(runtime);
				NotificationService.Instance.BindDesktopRuntime(runtime, Application.Current.Dispatcher);
				WallpaperService.Instance.BindDesktopRuntime(runtime);
				DesktopControlService.Instance.BindDesktopRuntime(runtime, Application.Current.Dispatcher);
				DesktopControlService.Instance.AddTo(this._disposable);
				SettingsService.StretchShortcutListsTo(runtime.State.Order.Count);
				this.RegisterActions();
				this.CompleteSuccessfulInitialization();
			}
			catch (Exception ex)
			{
				if (!runtimeInitialized)
				{
					this._startupTrace?.Write(StartupPhase.RuntimeInitialized, StartupTraceResult.Failed, ex.GetType(), ex.HResult);
				}
				this.VirtualDesktopInitializationFailed?.Invoke(ex, false);
			}
		}

		internal void CompleteSuccessfulInitialization()
		{
			this._hookService.Start();
			this._startupTrace?.Write(StartupPhase.HookStarted, StartupTraceResult.Succeeded);
			this.VirtualDesktopInitialized?.Invoke();
		}

		internal Task ShutdownAsync()
		{
			return this._desktopRuntime?.ShutdownAsync() ?? Task.CompletedTask;
		}
		private sealed class DesktopRuntimeLog : ILog
		{
			internal DesktopRuntimeLog(DesktopRuntimeFault fault)
			{
				this.DateTime = DateTimeOffset.Now;
				this.Header = fault.Category;
				this.Content = "ExceptionType=" + (fault.ExceptionType ?? "none") + ";DesktopId=" + (fault.DesktopId.HasValue ? fault.DesktopId.Value.ToString("N").Substring(0, 8) : "none") + ";Sequence=" + (fault.Sequence?.ToString() ?? "none");
			}
			public DateTimeOffset DateTime { get; }
			public string Header { get; }
			public string Content { get; }
		}
		private void RegisterActions(ShortcutKeySettings settings, ActionRegister register)
		{
			register(() => settings.MoveLeft.ToShortcutKey(), hWnd => hWnd.MoveToLeft())
				.AddTo(this._disposable);

			register(() => settings.MoveLeftAndSwitch.ToShortcutKey(), hWnd => hWnd.MoveToLeft()?.Switch())
				.AddTo(this._disposable);

			register(() => settings.MoveRight.ToShortcutKey(), hWnd => hWnd.MoveToRight())
				.AddTo(this._disposable);

			register(() => settings.MoveRightAndSwitch.ToShortcutKey(), hWnd => hWnd.MoveToRight()?.Switch())
				.AddTo(this._disposable);

			register(() => settings.MoveNew.ToShortcutKey(), hWnd => hWnd.MoveToNew())
				.AddTo(this._disposable);

			register(() => settings.MoveNewAndSwitch.ToShortcutKey(), hWnd => hWnd.MoveToNew()?.Switch())
				.AddTo(this._disposable);

			register(() => settings.MoveToPrevious.ToShortcutKey(), hWnd => hWnd.MoveToPrevious())
				.AddTo(this._disposable);

			register(() => settings.MoveToPreviousAndSwitch.ToShortcutKey(), hWnd => hWnd.MoveToPrevious()?.Switch())
				.AddTo(this._disposable);

			var isKeyboardSettings = settings as MouseShortcutSettings == null;
			if (isKeyboardSettings)
			{
				if (Settings.General.OverrideWindowsDefaultKeyCombination)
				{
					register(() => settings.SwitchToLeftWithDefault.ToShortcutKey(), _ => { })
						.AddTo(this._disposable);

					register(() => settings.SwitchToRightWithDefault.ToShortcutKey(), _ => { })
						.AddTo(this._disposable);
				}
				else if (Settings.General.LoopDesktop)
				{
					register(
							() => settings.SwitchToLeftWithDefault.ToShortcutKey(),
							_ => VirtualDesktopService.GetLeft()?.Switch())
						.AddTo(this._disposable);

					register(
							() => settings.SwitchToRightWithDefault.ToShortcutKey(),
							_ => VirtualDesktopService.GetRight()?.Switch())
						.AddTo(this._disposable);
				}

				register(() => settings.SwitchToLeft.ToShortcutKey(), _ => VirtualDesktopService.GetLeft()?.Switch())
					.AddTo(this._disposable);

				register(() => settings.SwitchToRight.ToShortcutKey(), _ => VirtualDesktopService.GetRight()?.Switch())
					.AddTo(this._disposable);

				register(() => settings.SwitchToPrevious.ToShortcutKey(), _ => VirtualDesktopService.GetPrevious()?.Switch())
					.AddTo(this._disposable);
			}
			else
			{
				register(() => settings.SwitchToLeft.ToShortcutKey(), _ => VirtualDesktopService.GetLeft()?.Switch())
					.AddTo(this._disposable);

				register(() => settings.SwitchToRight.ToShortcutKey(), _ => VirtualDesktopService.GetRight()?.Switch())
					.AddTo(this._disposable);

				register(() => settings.SwitchToPrevious.ToShortcutKey(), _ => VirtualDesktopService.GetPrevious()?.Switch())
					.AddTo(this._disposable);
			}

			register(() => settings.CloseCurrent.ToShortcutKey(), _ => VirtualDesktopService.CloseCurrent())
				.AddTo(this._disposable);

			register(() => settings.CreateNew.ToShortcutKey(), _ => VirtualDesktop.Create())
				.AddTo(this._disposable);

			for (var i = 0; i < settings.SwitchToIndices.Count; i++)
			{
				var index = i;
				register(() => settings.SwitchToIndices[index].ToShortcutKey(), _ => VirtualDesktopService.GetFromIndex(index)?.Switch())
					.AddTo(this._disposable);
			}

			for (var i = 0; i < settings.MoveToIndices.Count; i++)
			{
				var index = i;
				register(() => settings.MoveToIndices[index].ToShortcutKey(), hWnd => hWnd.MoveToDesktop(VirtualDesktopService.GetFromIndex(index)))
					.AddTo(this._disposable);
			}

			for (var i = 0; i < settings.MoveToIndicesAndSwitch.Count; i++)
			{
				var index = i;
				register(() => settings.MoveToIndicesAndSwitch[index].ToShortcutKey(), hWnd => hWnd.MoveToDesktop(VirtualDesktopService.GetFromIndex(index))?.Switch())
					.AddTo(this._disposable);
			}

			for (var i = 0; i < settings.SwapDesktopIndices.Count; i++)
			{
				var index = i;
				register(() => settings.SwapDesktopIndices[index].ToShortcutKey(), _ => VirtualDesktopService.SwapDesktop(VirtualDesktopService.GetFromIndex(index)))
					.AddTo(this._disposable);
			}
		}
	}
}
