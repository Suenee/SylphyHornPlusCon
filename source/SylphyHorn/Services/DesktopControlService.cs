using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services.DesktopTransitions;
using SylphyHorn.UI.Bindings;
using WindowsDesktop;

namespace SylphyHorn.Services
{
	public enum ToggleAction
	{
		Off,
		On,
		Toggle,
	}

	public sealed class DesktopSelector
	{
		public string CName { get; set; }
		public Guid? Id { get; set; }
		public int? Position { get; set; }
	}

	public sealed class CommandResult<T>
	{
		private CommandResult(bool success, string errorCode, string message, T state)
		{
			this.Success = success;
			this.ErrorCode = errorCode;
			this.Message = message;
			this.State = state;
		}

		public bool Success { get; }
		public string ErrorCode { get; }
		public string Message { get; }
		public T State { get; }

		public static CommandResult<T> Succeeded(T state, string message = null)
			=> new CommandResult<T>(true, null, message, state);

		public static CommandResult<T> Failed(string errorCode, string message, T state = default(T))
			=> new CommandResult<T>(false, errorCode, message, state);
	}

	public sealed class DesktopState
	{
		internal DesktopState(
			Guid id,
			string cName,
			string title,
			int position,
			bool isCurrent,
			bool individualWallpaperEnabled,
			string wallpaperPath,
			WallpaperPosition wallpaperPosition)
		{
			this.Id = id;
			this.CName = cName;
			this.Title = title;
			this.Position = position;
			this.IsCurrent = isCurrent;
			this.IndividualWallpaperEnabled = individualWallpaperEnabled;
			this.WallpaperPath = wallpaperPath;
			this.WallpaperPosition = wallpaperPosition;
		}

		public Guid Id { get; }
		public string CName { get; }
		public string Title { get; }
		public int Position { get; }
		public bool IsCurrent { get; }
		public bool IndividualWallpaperEnabled { get; }
		public string WallpaperPath { get; }
		public WallpaperPosition WallpaperPosition { get; }
	}

	public sealed class GlobalWallpaperState
	{
		internal GlobalWallpaperState(bool enabled) => this.Enabled = enabled;
		public bool Enabled { get; }
	}

	public sealed class DesktopWallpaperState
	{
		internal DesktopWallpaperState(Guid id, string cName, int position, bool enabled, string wallpaperPath, WallpaperPosition wallpaperPosition)
		{
			this.Id = id;
			this.CName = cName;
			this.Position = position;
			this.Enabled = enabled;
			this.WallpaperPath = wallpaperPath;
			this.WallpaperPosition = wallpaperPosition;
		}

		public Guid Id { get; }
		public string CName { get; }
		public int Position { get; }
		public bool Enabled { get; }
		public string WallpaperPath { get; }
		public WallpaperPosition WallpaperPosition { get; }
	}

	public sealed class DesktopSystemState
	{
		internal DesktopSystemState(bool enabled, bool individualWallpapersEnabled, Guid? currentDesktopId, string currentCName, int currentPosition, IReadOnlyList<DesktopState> desktops)
		{
			this.Enabled = enabled;
			this.IndividualWallpapersEnabled = individualWallpapersEnabled;
			this.CurrentDesktopId = currentDesktopId;
			this.CurrentCName = currentCName;
			this.CurrentPosition = currentPosition;
			this.Desktops = desktops ?? Array.Empty<DesktopState>();
		}

		public bool Enabled { get; }
		public bool IndividualWallpapersEnabled { get; }
		public Guid? CurrentDesktopId { get; }
		public string CurrentCName { get; }
		public int CurrentPosition { get; }
		public IReadOnlyList<DesktopState> Desktops { get; }
	}

	public sealed class DesktopSystemStateChangedEventArgs : EventArgs
	{
		internal DesktopSystemStateChangedEventArgs(DesktopSystemState state) => this.State = state;
		public DesktopSystemState State { get; }
	}

	public interface IDesktopControlService
	{
		bool Enabled { get; }
		void SetEnabled(bool enabled);

		Task<CommandResult<DesktopState>> ActivateAsync(DesktopSelector selector);
		Task<CommandResult<DesktopState>> AddAsync(int position = 0);
		Task<CommandResult<GlobalWallpaperState>> SetGlobalWallpaperModeAsync(ToggleAction action);
		Task<CommandResult<DesktopWallpaperState>> SetDesktopWallpaperModeAsync(DesktopSelector selector, ToggleAction action);

		DesktopSystemState GetState();
		DesktopState GetDesktop(DesktopSelector selector);
		GlobalWallpaperState GetIndividualWallpapersState();
		DesktopWallpaperState GetDesktopWallpaperState(DesktopSelector selector);

		event EventHandler<DesktopSystemStateChangedEventArgs> StateChanged;
	}

	public sealed class DesktopControlService : IDesktopControlService, IDisposable
	{
		private sealed class RememberedWallpaper
		{
			internal RememberedWallpaper(string path, WallpaperPosition position)
			{
				this.Path = path;
				this.Position = position;
			}
			internal string Path { get; }
			internal WallpaperPosition Position { get; }
		}

		private static readonly DesktopSystemState EmptyState = new DesktopSystemState(false, false, null, null, 0, Array.Empty<DesktopState>());
		private readonly object _gate = new object();
		private readonly Dictionary<Guid, RememberedWallpaper> _disabledWallpapers = new Dictionary<Guid, RememberedWallpaper>();
		private readonly Dictionary<Guid, VirtualDesktopViewModel> _desktopViewModels = new Dictionary<Guid, VirtualDesktopViewModel>();
		private DesktopTransitionRuntime _runtime;
		private Dispatcher _dispatcher;
		private IDisposable _wallpaperSettingSubscription;
		private VirtualDesktopViewModel[] _desktops = Array.Empty<VirtualDesktopViewModel>();
		private DesktopSystemState _snapshot = EmptyState;
		private int _enabled;
		private bool _bound;
		private bool _disposed;

		public static DesktopControlService Instance { get; } = new DesktopControlService();

		private DesktopControlService() { }

		public event EventHandler<DesktopSystemStateChangedEventArgs> StateChanged;

		public bool Enabled => Volatile.Read(ref this._enabled) != 0;

		internal void BindDesktopRuntime(DesktopTransitionRuntime runtime, Dispatcher dispatcher)
		{
			if (runtime == null) throw new ArgumentNullException(nameof(runtime));
			if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
			if (!dispatcher.CheckAccess()) throw new InvalidOperationException("DesktopControlService must be bound on its owner Dispatcher.");

			lock (this._gate)
			{
				if (this._disposed) throw new ObjectDisposedException(nameof(DesktopControlService));
				if (this._bound)
				{
					if (ReferenceEquals(this._runtime, runtime) && ReferenceEquals(this._dispatcher, dispatcher)) return;
					throw new InvalidOperationException("DesktopControlService is already bound to another runtime.");
				}
				this._runtime = runtime;
				this._dispatcher = dispatcher;
				this._bound = true;
			}

			this._runtime.StateChanged += this.OnRuntimeStateChanged;
			this._wallpaperSettingSubscription = Settings.General.ChangeBackgroundEachDesktop.Subscribe(_ => this.RefreshAndPublish());
			this.RefreshViewModels(this._runtime.State);
			this.RefreshSnapshot();
			LoggingService.Instance.Write(LogLevel.Info, "CONTROL", "DesktopControlBound", "Desktop control service bound to the virtual desktop runtime.");
		}

		public void SetEnabled(bool enabled)
		{
			var desired = enabled ? 1 : 0;
			if (Interlocked.Exchange(ref this._enabled, desired) == desired) return;
			LoggingService.Instance.Write(LogLevel.Info, "CONTROL", "DesktopControlAvailabilityChanged", enabled ? "Desktop control service enabled." : "Desktop control service disabled.");
			this.PublishFromAnyThread();
		}

		public DesktopSystemState GetState() => Volatile.Read(ref this._snapshot) ?? EmptyState;

		public DesktopState GetDesktop(DesktopSelector selector)
		{
			var state = this.GetState();
			return ResolveDesktop(state, selector, out _) ;
		}

		public GlobalWallpaperState GetIndividualWallpapersState()
			=> new GlobalWallpaperState(this.GetState().IndividualWallpapersEnabled);

		public DesktopWallpaperState GetDesktopWallpaperState(DesktopSelector selector)
		{
			var desktop = this.GetDesktop(selector);
			return desktop == null ? null : ToWallpaperState(desktop);
		}

		public async Task<CommandResult<DesktopState>> ActivateAsync(DesktopSelector selector)
		{
			var disabled = this.RejectIfDisabled<DesktopState>();
			if (disabled != null) return disabled;
			return await this.InvokeOnOwnerAsync(() =>
			{
				var desktop = this.ResolveLiveDesktop(selector, out var errorCode, out var errorMessage);
				if (desktop == null) return CommandResult<DesktopState>.Failed(errorCode, errorMessage);
				try
				{
					desktop.Switch();
					LoggingService.Instance.Write(LogLevel.Info, "CONTROL", "DesktopActivate", "Desktop activation requested.", desktop.Id.ToString("D"), desktop.CanonicalName);
					return CommandResult<DesktopState>.Succeeded(this.CreateDesktopState(desktop));
				}
				catch (Exception ex)
				{
					return this.Fail<DesktopState>("desktop_activate_failed", "The desktop could not be activated.", ex, desktop.Id);
				}
			});
		}

		public async Task<CommandResult<DesktopState>> AddAsync(int position = 0)
		{
			var disabled = this.RejectIfDisabled<DesktopState>();
			if (disabled != null) return disabled;
			var before = this.GetState();
			if (position < 0 || position > before.Desktops.Count + 1)
				return CommandResult<DesktopState>.Failed("invalid_position", $"Position must be 0 or between 1 and {before.Desktops.Count + 1}.");

			Guid createdId;
			try
			{
				createdId = await this.InvokeOnOwnerAsync(() => VirtualDesktop.Create().Id);
			}
			catch (Exception ex)
			{
				return this.Fail<DesktopState>("desktop_add_failed", "The desktop could not be created.", ex);
			}

			var created = await this.WaitForDesktopAsync(createdId, TimeSpan.FromSeconds(5));
			if (created == null)
				return CommandResult<DesktopState>.Failed("desktop_state_timeout", "The desktop was created, but it did not appear in the runtime state before the timeout.");

			var logicalCName = created.CName;
			if (position != 0 && position != created.Position)
			{
				try
				{
					await this.InvokeOnOwnerAsync(() =>
					{
						var live = this._desktops.FirstOrDefault(item => item.Id == createdId);
						if (live == null) throw new InvalidOperationException("The newly created desktop is no longer present.");
						LogicalDesktopOrderService.Instance.Move(this._desktops, live.Index, position - 1, ProductInfo.IsReorderingSupportBuild);
					});
				}
				catch (Exception ex)
				{
					return this.Fail<DesktopState>("desktop_insert_failed", "The desktop was created, but it could not be moved to the requested position.", ex, createdId);
				}

				created = await this.WaitForDesktopAsync(logicalCName, position, TimeSpan.FromSeconds(5)) ?? this.GetState().Desktops.FirstOrDefault(item => string.Equals(item.CName, logicalCName, StringComparison.OrdinalIgnoreCase));
			}

			LoggingService.Instance.Write(LogLevel.Info, "CONTROL", "DesktopAdded", "Desktop added through the control service.", created?.Id.ToString("D"), $"CName={logicalCName};Position={created?.Position ?? 0}");
			return created == null
				? CommandResult<DesktopState>.Failed("desktop_state_unavailable", "The desktop was created, but its final logical state is unavailable.")
				: CommandResult<DesktopState>.Succeeded(created);
		}

		public async Task<CommandResult<GlobalWallpaperState>> SetGlobalWallpaperModeAsync(ToggleAction action)
		{
			var disabled = this.RejectIfDisabled<GlobalWallpaperState>();
			if (disabled != null) return disabled;
			return await this.InvokeOnOwnerAsync(() =>
			{
				try
				{
					var current = Settings.General.ChangeBackgroundEachDesktop.Value;
					var target = ResolveToggle(action, current);
					if (target != current) WallpaperService.Instance.SetManagementEnabled(target);
					this.RefreshSnapshot();
					var result = new GlobalWallpaperState(Settings.General.ChangeBackgroundEachDesktop.Value);
					this.PublishSnapshot(this.GetState());
					LoggingService.Instance.Write(LogLevel.Info, "CONTROL", "GlobalWallpaperModeChanged", "Global individual wallpaper management changed.", details: $"Enabled={result.Enabled}");
					return CommandResult<GlobalWallpaperState>.Succeeded(result);
				}
				catch (Exception ex)
				{
					return this.Fail<GlobalWallpaperState>("global_wallpaper_failed", "Global individual wallpaper management could not be changed.", ex);
				}
			});
		}

		public async Task<CommandResult<DesktopWallpaperState>> SetDesktopWallpaperModeAsync(DesktopSelector selector, ToggleAction action)
		{
			var disabled = this.RejectIfDisabled<DesktopWallpaperState>();
			if (disabled != null) return disabled;
			return await this.InvokeOnOwnerAsync(() =>
			{
				var desktop = this.ResolveLiveDesktop(selector, out var errorCode, out var errorMessage);
				if (desktop == null) return CommandResult<DesktopWallpaperState>.Failed(errorCode, errorMessage);
				if (!Settings.General.ChangeBackgroundEachDesktop.Value)
					return CommandResult<DesktopWallpaperState>.Failed("global_wallpaper_disabled", "Global individual wallpaper management is disabled.", ToWallpaperState(this.CreateDesktopState(desktop)));

				try
				{
					var isEnabled = !this._disabledWallpapers.ContainsKey(desktop.Id) && desktop.HasWallpaper;
					var target = ResolveToggle(action, isEnabled);
					if (target == isEnabled) return CommandResult<DesktopWallpaperState>.Succeeded(ToWallpaperState(this.CreateDesktopState(desktop)));

					if (!target)
					{
						if (!desktop.HasWallpaper)
							return CommandResult<DesktopWallpaperState>.Succeeded(ToWallpaperState(this.CreateDesktopState(desktop)));
						this._disabledWallpapers[desktop.Id] = new RememberedWallpaper(desktop.WallpaperPath, desktop.WallpaperPosition);
						desktop.ResetWallpaperPath(WallpaperService.Instance.OriginalWallpaperPath);
						if (Settings.General.OriginalWallpaperCaptured.Value)
							desktop.WallpaperPosition = WallpaperService.Instance.OriginalWallpaperPosition;
					}
					else
					{
						if (!this._disabledWallpapers.TryGetValue(desktop.Id, out var remembered) || string.IsNullOrWhiteSpace(remembered.Path))
							return CommandResult<DesktopWallpaperState>.Failed("wallpaper_unavailable", "No previously disabled individual wallpaper is available for this desktop.", ToWallpaperState(this.CreateDesktopState(desktop)));
						desktop.WallpaperPosition = remembered.Position;
						desktop.WallpaperPath = remembered.Path;
						this._disabledWallpapers.Remove(desktop.Id);
					}

					this.RefreshSnapshot();
					var state = ToWallpaperState(this.CreateDesktopState(desktop));
					this.PublishSnapshot(this.GetState());
					LoggingService.Instance.Write(LogLevel.Info, "CONTROL", "DesktopWallpaperModeChanged", "Desktop individual wallpaper mode changed.", desktop.Id.ToString("D"), $"Enabled={state.Enabled};CName={desktop.CanonicalName}");
					return CommandResult<DesktopWallpaperState>.Succeeded(state);
				}
				catch (Exception ex)
				{
					return this.Fail<DesktopWallpaperState>("desktop_wallpaper_failed", "The desktop individual wallpaper mode could not be changed.", ex, desktop.Id);
				}
			});
		}

		private void OnRuntimeStateChanged(object sender, DesktopRuntimeStateChanged e)
		{
			this.RefreshViewModels(e.Change.Snapshot);
			this.RefreshAndPublish();
		}

		private void RefreshViewModels(DesktopRuntimeState state)
		{
			if (state == null) return;
			var nextIds = new HashSet<Guid>(state.Order);
			foreach (var removed in this._desktopViewModels.Keys.Where(id => !nextIds.Contains(id)).ToArray())
			{
				this._desktopViewModels[removed].PropertyChanged -= this.OnDesktopViewModelPropertyChanged;
				this._desktopViewModels.Remove(removed);
				this._disabledWallpapers.Remove(removed);
			}

			var next = new List<VirtualDesktopViewModel>(state.Order.Count);
			for (var index = 0; index < state.Order.Count; index++)
			{
				var id = state.Order[index];
				if (!this._desktopViewModels.TryGetValue(id, out var viewModel))
				{
					viewModel = new VirtualDesktopViewModel(this._runtime, index, state.Records[id]);
					viewModel.PropertyChanged += this.OnDesktopViewModelPropertyChanged;
					this._desktopViewModels[id] = viewModel;
				}
				else viewModel.Update(index, state.Records[id]);
				next.Add(viewModel);
			}
			this._desktops = next.ToArray();
		}

		private void OnDesktopViewModelPropertyChanged(object sender, PropertyChangedEventArgs e) => this.RefreshAndPublish();

		private void RefreshAndPublish()
		{
			if (this._dispatcher != null && !this._dispatcher.CheckAccess())
			{
				this.PublishFromAnyThread();
				return;
			}
			this.RefreshSnapshot();
			this.PublishSnapshot(this.GetState());
		}

		private void PublishFromAnyThread()
		{
			var dispatcher = this._dispatcher;
			if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
			{
				this.RefreshEnabledOnlySnapshot();
				return;
			}
			if (dispatcher.CheckAccess()) this.RefreshAndPublish();
			else _ = dispatcher.BeginInvoke((Action)this.RefreshAndPublish, DispatcherPriority.DataBind);
		}

		private void RefreshEnabledOnlySnapshot()
		{
			var current = this.GetState();
			Volatile.Write(ref this._snapshot, new DesktopSystemState(this.Enabled, current.IndividualWallpapersEnabled, current.CurrentDesktopId, current.CurrentCName, current.CurrentPosition, current.Desktops));
		}

		private void RefreshSnapshot()
		{
			var currentId = this._runtime?.State?.CurrentDesktopId;
			var globalWallpaper = Settings.General.ChangeBackgroundEachDesktop.Value;
			var desktops = this._desktops.Select(desktop => this.CreateDesktopState(desktop, currentId, globalWallpaper)).ToArray();
			var current = currentId.HasValue ? desktops.FirstOrDefault(desktop => desktop.Id == currentId.Value) : null;
			Volatile.Write(ref this._snapshot, new DesktopSystemState(
				this.Enabled,
				globalWallpaper,
				current?.Id,
				current?.CName,
				current?.Position ?? 0,
				desktops));
		}

		private DesktopState CreateDesktopState(VirtualDesktopViewModel desktop)
			=> this.CreateDesktopState(desktop, this._runtime?.State?.CurrentDesktopId, Settings.General.ChangeBackgroundEachDesktop.Value);

		private DesktopState CreateDesktopState(VirtualDesktopViewModel desktop, Guid? currentId, bool globalWallpaper)
		{
			var individualEnabled = globalWallpaper && !this._disabledWallpapers.ContainsKey(desktop.Id) && desktop.HasWallpaper;
			return new DesktopState(desktop.Id, desktop.CanonicalName, desktop.Title, desktop.Index + 1, currentId == desktop.Id, individualEnabled, desktop.WallpaperPath, desktop.WallpaperPosition);
		}

		private VirtualDesktopViewModel ResolveLiveDesktop(DesktopSelector selector, out string errorCode, out string errorMessage)
		{
			errorCode = null;
			errorMessage = null;
			if (!ValidateSelector(selector, out errorCode, out errorMessage)) return null;
			if (selector.Id.HasValue)
			{
				var byId = this._desktops.FirstOrDefault(item => item.Id == selector.Id.Value);
				if (byId != null) return byId;
			}
			else if (!string.IsNullOrWhiteSpace(selector.CName))
			{
				var byName = this._desktops.FirstOrDefault(item => string.Equals(item.CanonicalName, selector.CName.Trim(), StringComparison.OrdinalIgnoreCase));
				if (byName != null) return byName;
			}
			else if (selector.Position.HasValue && selector.Position.Value >= 1 && selector.Position.Value <= this._desktops.Length)
				return this._desktops[selector.Position.Value - 1];

			errorCode = "desktop_not_found";
			errorMessage = "The requested desktop was not found.";
			return null;
		}

		private static DesktopState ResolveDesktop(DesktopSystemState state, DesktopSelector selector, out string errorCode)
		{
			errorCode = null;
			if (!ValidateSelector(selector, out errorCode, out _)) return null;
			DesktopState result = null;
			if (selector.Id.HasValue) result = state.Desktops.FirstOrDefault(item => item.Id == selector.Id.Value);
			else if (!string.IsNullOrWhiteSpace(selector.CName)) result = state.Desktops.FirstOrDefault(item => string.Equals(item.CName, selector.CName.Trim(), StringComparison.OrdinalIgnoreCase));
			else if (selector.Position.HasValue) result = state.Desktops.FirstOrDefault(item => item.Position == selector.Position.Value);
			if (result == null) errorCode = "desktop_not_found";
			return result;
		}

		private static bool ValidateSelector(DesktopSelector selector, out string errorCode, out string errorMessage)
		{
			errorCode = null;
			errorMessage = null;
			if (selector == null)
			{
				errorCode = "invalid_selector";
				errorMessage = "A desktop selector is required.";
				return false;
			}
			var count = (selector.Id.HasValue ? 1 : 0) + (!string.IsNullOrWhiteSpace(selector.CName) ? 1 : 0) + (selector.Position.HasValue ? 1 : 0);
			if (count != 1 || (selector.Position.HasValue && selector.Position.Value <= 0))
			{
				errorCode = "invalid_selector";
				errorMessage = "A selector must contain exactly one valid Id, CName, or one-based Position.";
				return false;
			}
			return true;
		}

		private static DesktopWallpaperState ToWallpaperState(DesktopState desktop)
			=> new DesktopWallpaperState(desktop.Id, desktop.CName, desktop.Position, desktop.IndividualWallpaperEnabled, desktop.WallpaperPath, desktop.WallpaperPosition);

		private static bool ResolveToggle(ToggleAction action, bool current)
		{
			switch (action)
			{
				case ToggleAction.Off: return false;
				case ToggleAction.On: return true;
				case ToggleAction.Toggle: return !current;
				default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
			}
		}

		private CommandResult<T> RejectIfDisabled<T>()
		{
			if (this.Enabled) return null;
			LoggingService.Instance.Write(LogLevel.Warning, "CONTROL", "CommandRejected", "Desktop control command rejected because the service is disabled.");
			return CommandResult<T>.Failed("service_disabled", "Desktop control service is disabled.");
		}

		private CommandResult<T> Fail<T>(string code, string message, Exception ex, Guid? desktopId = null)
		{
			LoggingService.Instance.Write(LogLevel.Error, "CONTROL", "CommandFailed", message, desktopId?.ToString("D"), ex.ToString());
			return CommandResult<T>.Failed(code, message);
		}

		private Task<T> InvokeOnOwnerAsync<T>(Func<T> action)
		{
			var dispatcher = this._dispatcher;
			if (!this._bound || dispatcher == null) throw new InvalidOperationException("Desktop control service is not bound to the desktop runtime.");
			if (dispatcher.CheckAccess()) return Task.FromResult(action());
			return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
		}

		private async Task<DesktopState> WaitForDesktopAsync(Guid id, TimeSpan timeout)
		{
			var end = DateTime.UtcNow + timeout;
			do
			{
				var found = this.GetState().Desktops.FirstOrDefault(item => item.Id == id);
				if (found != null) return found;
				await Task.Delay(25).ConfigureAwait(false);
			}
			while (DateTime.UtcNow < end);
			return null;
		}

		private async Task<DesktopState> WaitForDesktopAsync(string cName, int position, TimeSpan timeout)
		{
			var end = DateTime.UtcNow + timeout;
			do
			{
				var found = this.GetState().Desktops.FirstOrDefault(item => string.Equals(item.CName, cName, StringComparison.OrdinalIgnoreCase) && item.Position == position);
				if (found != null) return found;
				await Task.Delay(25).ConfigureAwait(false);
			}
			while (DateTime.UtcNow < end);
			return null;
		}

		private void PublishSnapshot(DesktopSystemState state)
		{
			var handlers = this.StateChanged;
			if (handlers == null) return;
			var args = new DesktopSystemStateChangedEventArgs(state);
			foreach (EventHandler<DesktopSystemStateChangedEventArgs> handler in handlers.GetInvocationList())
			{
				try { handler(this, args); }
				catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Error, "CONTROL", "StateChangedSubscriberFailed", "A desktop control state subscriber failed.", details: ex.ToString()); }
			}
		}

		public void Dispose()
		{
			lock (this._gate)
			{
				if (this._disposed) return;
				this._disposed = true;
			}
			if (this._runtime != null) this._runtime.StateChanged -= this.OnRuntimeStateChanged;
			this._wallpaperSettingSubscription?.Dispose();
			this._wallpaperSettingSubscription = null;
			foreach (var desktop in this._desktopViewModels.Values) desktop.PropertyChanged -= this.OnDesktopViewModelPropertyChanged;
			this._desktopViewModels.Clear();
			this._desktops = Array.Empty<VirtualDesktopViewModel>();
			this._disabledWallpapers.Clear();
		}
	}
}
