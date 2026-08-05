using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SylphyHorn.Serialization;
using WindowsDesktop;

namespace SylphyHorn.Services.DesktopTransitions
{
	internal sealed class VirtualDesktopProviderClient : IDesktopProviderClient	{
		private readonly VirtualDesktopProvider _provider;
		internal VirtualDesktopProviderClient(VirtualDesktopProvider provider) => this._provider = provider ?? throw new ArgumentNullException(nameof(provider));
		public event EventHandler<VirtualDesktopStableBatch> StableBatchPublished { add => this._provider.StableBatchPublished += value; remove => this._provider.StableBatchPublished -= value; }
		public event EventHandler<VirtualDesktopCurrentTransition> CurrentTransitioned { add => this._provider.CurrentTransitioned += value; remove => this._provider.CurrentTransitioned -= value; }
		public event EventHandler<VirtualDesktopProviderFault> Faulted { add => this._provider.EventDispatchFaulted += value; remove => this._provider.EventDispatchFaulted -= value; }
		public Task<VirtualDesktopReconciliationResult> RequestReconciliationAsync(VirtualDesktopStableReason reason, CancellationToken cancellationToken) => this._provider.RequestReconciliationAsync(reason, cancellationToken);
		public void Dispose() => this._provider.Dispose();
	}

	internal sealed class ApplicationDesktopSettingsTransactions : IDesktopSettingsTransactions
	{
		private readonly LocalSettingsProvider _provider;
		internal ApplicationDesktopSettingsTransactions(LocalSettingsProvider provider) => this._provider = provider ?? throw new ArgumentNullException(nameof(provider));
		public DesktopStartupSeed CaptureStartupSeed() => SettingsService.CaptureDesktopStartupSeed();
		public void ApplyProjection(DesktopSettingsProjection projection) => SettingsService.ApplyDesktopProjection(projection);
		public long SettingsRevision => this._provider.SettingsRevision;
		public Task<SettingsSaveResult> RequestSaveAsync(long stateRevision) => this._provider.SaveWithResultAsync(stateRevision);
		public Task<StagedSettingsImport> PrepareImportAsync(string path) => this._provider.PrepareImportAsync(path);
		public Task<StagedSettingsImport> PrepareResetAsync() => this._provider.PrepareResetAsync();
		public Task<SettingsImportCommitResult> CommitImportAsync(StagedSettingsImport stage, IDictionary<string, object> dictionary) => this._provider.CommitStagedImportAsync(stage, dictionary);
		public SettingsImportCommitResult DiscardImport(StagedSettingsImport stage) => this._provider.DiscardStagedImport(stage);
		public void PublishImportCommitted() => this._provider.PublishCommittedImport();
	}

	internal sealed class DispatcherDesktopOwnerContext : IDesktopOwnerContext
	{
		private readonly Dispatcher _dispatcher;
		internal DispatcherDesktopOwnerContext(Dispatcher dispatcher) => this._dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		public bool CheckAccess() => this._dispatcher.CheckAccess();
		public bool Post(Action action)
		{
			if (action == null) throw new ArgumentNullException(nameof(action));
			if (this._dispatcher.HasShutdownStarted || this._dispatcher.HasShutdownFinished) return false;
			this._dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
			return true;
		}
	}

	internal sealed class VirtualDesktopOperations : IDesktopOperations
	{
		public void Create() => VirtualDesktop.Create();
		public void SetName(Guid desktopId, string value) => Resolve(desktopId).Name = value;
		public void SetWallpaperPath(Guid desktopId, string value) => Resolve(desktopId).WallpaperPath = value;
		public void ApplyWallpaper(Guid desktopId, string value, WallpaperPosition position) => WallpaperService.ApplyDesktopWallpaper(value, position);
		public void MoveLeft(Guid desktopId) => Resolve(desktopId).MoveToLeft();
		public void MoveRight(Guid desktopId) => Resolve(desktopId).MoveToRight();
		public void MoveFirst(Guid desktopId) => Resolve(desktopId).MoveToFirst();
		public void MoveLast(Guid desktopId) => Resolve(desktopId).MoveToLast();
		public void Switch(Guid desktopId) => Resolve(desktopId).Switch();
		public void Remove(Guid desktopId) => Resolve(desktopId).Remove();
		private static VirtualDesktop Resolve(Guid desktopId) => VirtualDesktop.FromId(desktopId) ?? throw new InvalidOperationException("The virtual desktop is no longer active.");
	}

	internal static class ReadOnlyListExtensions
	{
		internal static int IndexOf<T>(this IReadOnlyList<T> values, T value)
		{
			for (var index = 0; index < values.Count; index++) if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
			return -1;
		}
	}
}
