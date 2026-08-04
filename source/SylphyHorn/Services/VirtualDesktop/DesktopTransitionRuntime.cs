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
	internal enum DesktopRuntimeInitializationStatus
	{
		Completed,
		Cancelled,
		ShuttingDown,
		Unavailable,
	}

	internal enum DesktopStartupOverrideStatus
	{
		NotRequested,
		Completed,
		CompletedWithFailures,
		Cancelled,
		ShuttingDown,
		Unavailable,
	}

	internal enum DesktopRuntimeShutdownStatus
	{
		Completed,
		ReconciliationUnavailable,
		SaveFailed,
	}

	internal sealed class DesktopRuntimeShutdownResult
	{
		internal DesktopRuntimeShutdownResult(DesktopRuntimeShutdownStatus status, VirtualDesktopReconciliationStatus reconciliationStatus, SettingsSaveResult saveResult)
		{
			this.Status = status;
			this.ReconciliationStatus = reconciliationStatus;
			this.SaveResult = saveResult;
		}
		internal DesktopRuntimeShutdownStatus Status { get; }
		internal VirtualDesktopReconciliationStatus ReconciliationStatus { get; }
		internal SettingsSaveResult SaveResult { get; }
	}

	internal enum DesktopOverrideOperationStatus
	{
		NotStarted,
		Succeeded,
		Failed,
		Unconfirmed,
		Skipped,
	}
	internal enum DesktopStartupTopologyMutationKind
	{
		Create,
		Remove,
	}

	internal sealed class DesktopStartupTopologyMutationResult
	{
		internal DesktopStartupTopologyMutationResult(DesktopStartupTopologyMutationKind kind, Guid? desktopId, DesktopOverrideOperationStatus status)
		{
			this.Kind = kind;
			this.DesktopId = desktopId;
			this.Status = status;
		}
		internal DesktopStartupTopologyMutationKind Kind { get; }
		internal Guid? DesktopId { get; }
		internal DesktopOverrideOperationStatus Status { get; }
		internal bool Succeeded => this.Status == DesktopOverrideOperationStatus.Succeeded;
	}

	internal sealed class DesktopStartupOverridePlan
	{
		private DesktopStartupOverridePlan(Guid planId, int initialCount, int targetCount, IReadOnlyList<Guid> removeIds)
		{
			this.PlanId = planId;
			this.InitialCount = initialCount;
			this.TargetCount = targetCount;
			this.CreateCount = Math.Max(0, targetCount - initialCount);
			this.RemoveIds = removeIds;
		}
		internal Guid PlanId { get; }
		internal int InitialCount { get; }
		internal int TargetCount { get; }
		internal int CreateCount { get; }
		internal IReadOnlyList<Guid> RemoveIds { get; }
		internal static DesktopStartupOverridePlan Create(DesktopRuntimeState state, int targetCount)
		{
			var removals = state.Order.Skip(Math.Max(0, targetCount)).Reverse().ToArray();
			return new DesktopStartupOverridePlan(Guid.NewGuid(), state.Order.Count, targetCount, removals);
		}
	}
	internal sealed class DesktopStartupMutationResult
	{
		internal DesktopStartupMutationResult(Guid desktopId, DesktopPropertyKind property, DesktopOverrideOperationStatus status)
		{
			this.DesktopId = desktopId;
			this.Property = property;
			this.Status = status;
		}
		internal Guid DesktopId { get; }
		internal DesktopPropertyKind Property { get; }
		internal DesktopOverrideOperationStatus Status { get; }
		internal bool Succeeded => this.Status == DesktopOverrideOperationStatus.Succeeded;
	}

	internal sealed class DesktopStartupOverrideResult
	{
		internal DesktopStartupOverrideResult(Guid planId, int targetDesktopCount, DesktopStartupOverrideStatus status, IEnumerable<DesktopStartupTopologyMutationResult> topologyJournal, IEnumerable<DesktopStartupMutationResult> journal)
		{
			this.PlanId = planId;
			this.TargetDesktopCount = targetDesktopCount;
			this.Status = status;
			this.TopologyJournal = new System.Collections.ObjectModel.ReadOnlyCollection<DesktopStartupTopologyMutationResult>(new List<DesktopStartupTopologyMutationResult>(topologyJournal ?? Array.Empty<DesktopStartupTopologyMutationResult>()));
			this.Journal = new System.Collections.ObjectModel.ReadOnlyCollection<DesktopStartupMutationResult>(new List<DesktopStartupMutationResult>(journal ?? Array.Empty<DesktopStartupMutationResult>()));
		}
		internal Guid PlanId { get; }
		internal int TargetDesktopCount { get; }
		internal DesktopStartupOverrideStatus Status { get; }
		internal IReadOnlyList<DesktopStartupTopologyMutationResult> TopologyJournal { get; }
		internal IReadOnlyList<DesktopStartupMutationResult> Journal { get; }
		internal static DesktopStartupOverrideResult NotRequested { get; } = new DesktopStartupOverrideResult(Guid.Empty, 0, DesktopStartupOverrideStatus.NotRequested, null, null);
	}

	internal sealed class DesktopRuntimeInitializationResult
	{
		internal DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus status, VirtualDesktopProviderFailureCategory? failureCategory = null, DesktopStartupOverrideResult startupOverride = null)
		{
			this.Status = status;
			this.FailureCategory = failureCategory;
			this.StartupOverride = startupOverride ?? DesktopStartupOverrideResult.NotRequested;
		}
		internal DesktopRuntimeInitializationStatus Status { get; }
		internal VirtualDesktopProviderFailureCategory? FailureCategory { get; }
		internal DesktopStartupOverrideResult StartupOverride { get; }
		internal bool Succeeded => this.Status == DesktopRuntimeInitializationStatus.Completed;
	}
	internal sealed class DesktopRuntimeStateChanged
	{
		internal DesktopRuntimeStateChanged(DesktopStateChanged change, long settingsRevision)
		{
			this.Change = change ?? throw new ArgumentNullException(nameof(change));
			this.SettingsRevision = settingsRevision;
		}
		internal DesktopStateChanged Change { get; }
		internal long SettingsRevision { get; }
	}

	internal sealed class DesktopRuntimeFault
	{
		internal DesktopRuntimeFault(string category, Type exceptionType, Guid? desktopId = null, long? sequence = null)
			: this(category, exceptionType?.FullName, desktopId, sequence)
		{
		}

		internal DesktopRuntimeFault(string category, string exceptionType, Guid? desktopId = null, long? sequence = null)
		{
			this.Category = category ?? throw new ArgumentNullException(nameof(category));
			this.ExceptionType = exceptionType;
			this.DesktopId = desktopId;
			this.Sequence = sequence;
		}
		internal string Category { get; }
		internal string ExceptionType { get; }
		internal Guid? DesktopId { get; }
		internal long? Sequence { get; }
	}

	internal interface IDesktopProviderClient : IDisposable
	{
		event EventHandler<VirtualDesktopStableBatch> StableBatchPublished;
		event EventHandler<VirtualDesktopCurrentTransition> CurrentTransitioned;
		event EventHandler<VirtualDesktopProviderFault> Faulted;
		Task<VirtualDesktopReconciliationResult> RequestReconciliationAsync(VirtualDesktopStableReason reason, CancellationToken cancellationToken);
	}

	internal interface IDesktopSettingsTransactions
	{
		DesktopStartupSeed CaptureStartupSeed();
		void ApplyProjection(DesktopSettingsProjection projection);
		long SettingsRevision { get; }
		Task<SettingsSaveResult> RequestSaveAsync(long stateRevision);
		Task<StagedSettingsImport> PrepareImportAsync(string path);
		Task<StagedSettingsImport> PrepareResetAsync();
		Task<SettingsImportCommitResult> CommitImportAsync(StagedSettingsImport stage, IDictionary<string, object> dictionary);
		SettingsImportCommitResult DiscardImport(StagedSettingsImport stage);
		void PublishImportCommitted();
	}

	internal interface IDesktopOwnerContext
	{
		bool CheckAccess();
		bool Post(Action action);
	}

	internal interface IDesktopOperations
	{
		void Create();
		void SetName(Guid desktopId, string value);
		void SetWallpaperPath(Guid desktopId, string value);
		void ApplyWallpaper(Guid desktopId, string value, WallpaperPosition position);
		void MoveLeft(Guid desktopId);
		void MoveRight(Guid desktopId);
		void MoveFirst(Guid desktopId);
		void MoveLast(Guid desktopId);
		void Switch(Guid desktopId);
		void Remove(Guid desktopId);
	}

	internal sealed class DesktopPersistenceProtection
	{
		private readonly IReadOnlyDictionary<DesktopProtectionKey, DesktopProtectedValue> _values;
		private DesktopPersistenceProtection(IReadOnlyDictionary<DesktopProtectionKey, DesktopProtectedValue> values) => this._values = values;

		internal static DesktopPersistenceProtection FromSeed(DesktopRuntimeState state, DesktopOperationJournal journal)
		{
			var values = new Dictionary<DesktopProtectionKey, DesktopProtectedValue>();
			foreach (var entry in journal.ProtectionEntries)
			{
				if (!entry.TargetValueSpecified || !state.Records.ContainsKey(entry.DesktopId)) continue;
				values[new DesktopProtectionKey(entry.DesktopId, entry.Property)] = DesktopProtectedValue.Known(entry.TargetValue);
			}
			return values.Count == 0 ? null : new DesktopPersistenceProtection(values);
		}

		internal static DesktopPersistenceProtection FromProjection(DesktopSettingsProjection projection, DesktopRuntimeState state, DesktopOperationJournal journal = null)
		{
			var values = new Dictionary<DesktopProtectionKey, DesktopProtectedValue>();
			var scoped = journal?.ImportProtectionEntries.ToArray();
			for (var index = 0; index < state.Order.Count; index++)
			{
				var id = state.Order[index];
				if (scoped == null || scoped.Any(entry => entry.DesktopId == id && entry.Property == DesktopPropertyKind.Name))
					values[new DesktopProtectionKey(id, DesktopPropertyKind.Name)] = DesktopProtectedValue.FromProjection(projection.Names[index]);
				if (scoped == null || scoped.Any(entry => entry.DesktopId == id && entry.Property == DesktopPropertyKind.WallpaperPath))
					values[new DesktopProtectionKey(id, DesktopPropertyKind.WallpaperPath)] = DesktopProtectedValue.FromProjection(projection.WallpaperPaths[index]);
			}
			return values.Count == 0 ? null : new DesktopPersistenceProtection(values);
		}

		internal DesktopSettingsProjection CreateProjection(DesktopRuntimeState state)
		{
			return new DesktopSettingsProjection(
				state.Order.Select(id => this.GetValue(id, DesktopPropertyKind.Name, state.Records[id].Name)),
				state.Order.Select(id => this.GetValue(id, DesktopPropertyKind.WallpaperPath, state.Records[id].WallpaperPath)),
				state.Order.Select(id => state.Records[id].WallpaperPosition));
		}

		internal DesktopPersistenceProtection Release(Guid desktopId, DesktopPropertyKind property)
		{
			var key = new DesktopProtectionKey(desktopId, property);
			if (!this._values.ContainsKey(key)) return this;
			var values = this._values.ToDictionary(pair => pair.Key, pair => pair.Value);
			values.Remove(key);
			return values.Count == 0 ? null : new DesktopPersistenceProtection(values);
		}

		internal DesktopPersistenceProtection ReleaseNaturalMatches(DesktopRuntimeState state)
		{
			var values = this._values.ToDictionary(pair => pair.Key, pair => pair.Value);
			foreach (var pair in this._values)
			{
				if (!state.Records.TryGetValue(pair.Key.DesktopId, out var record)) { values.Remove(pair.Key); continue; }
				var property = pair.Key.Property == DesktopPropertyKind.Name ? record.Name : record.WallpaperPath;
				if (pair.Value.Matches(property)) values.Remove(pair.Key);
			}
			return values.Count == 0 ? null : new DesktopPersistenceProtection(values);
		}

		private string GetValue(Guid id, DesktopPropertyKind property, DesktopPropertyState runtime)
		{
			return this._values.TryGetValue(new DesktopProtectionKey(id, property), out var value)
				? value.Value
				: runtime.HasValue ? runtime.Value : null;
		}

		private struct DesktopProtectionKey : IEquatable<DesktopProtectionKey>
		{
			internal DesktopProtectionKey(Guid desktopId, DesktopPropertyKind property) { this.DesktopId = desktopId; this.Property = property; }
			internal Guid DesktopId { get; }
			internal DesktopPropertyKind Property { get; }
			public bool Equals(DesktopProtectionKey other) => this.DesktopId == other.DesktopId && this.Property == other.Property;
			public override bool Equals(object obj) => obj is DesktopProtectionKey other && this.Equals(other);
			public override int GetHashCode() => (this.DesktopId.GetHashCode() * 397) ^ (int)this.Property;
		}

		private sealed class DesktopProtectedValue
		{
			private DesktopProtectedValue(bool hasValue, string value) { this.HasValue = hasValue; this.Value = value; }
			internal bool HasValue { get; }
			internal string Value { get; }
			internal static DesktopProtectedValue Known(string value) => new DesktopProtectedValue(true, value);
			internal static DesktopProtectedValue FromProjection(string value) => new DesktopProtectedValue(value != null, value);
			internal bool Matches(DesktopPropertyState runtime) => this.HasValue == runtime.HasValue && (!this.HasValue || this.Value == runtime.Value);
		}
	}	internal sealed class DesktopTransitionRuntime : IDisposable
	{
		private readonly IDesktopProviderClient _provider;
		private readonly IDesktopSettingsTransactions _settings;
		private readonly IDesktopOwnerContext _owner;
		private readonly IDesktopOperations _operations;
		private readonly DesktopStartupSeed _startupSeed;
		private readonly TimeSpan _providerWaitBudget;
		private readonly object _providerWaitGate = new object();
		private readonly List<ProviderWaitRace> _providerWaits = new List<ProviderWaitRace>();
		private DesktopPersistenceProtection _persistenceProtection;
		private readonly Queue<Action> _deferredCommands = new Queue<Action>();
		private readonly Queue<Action> _frozenProviderIngress = new Queue<Action>();
		private DesktopTransitionCoordinator _coordinator;
		private DesktopTransitionCoordinator.DesktopPreparedRuntime _preparedRuntime;
		private StagedSettingsImport _activeSettingsStage;
		private Task<SettingsImportCommitResult> _activeImportCommit;
		private Task<DesktopRuntimeShutdownResult> _shutdownTask;
		private bool _publishing;
		private bool _deferredDrainScheduled;
		private bool _importCommitFrozen;
		private bool _providerEventsSubscribed;
		private bool _suppressPublication;
		private DesktopCoordinatorTransition _suppressedTransition;
		private bool _initialized;
		private bool _stopping;
		private bool _shutdownStarted;
		private int _disposeSignaled;

		internal DesktopTransitionRuntime(IDesktopProviderClient provider, IDesktopSettingsTransactions settings, IDesktopOwnerContext owner, IDesktopOperations operations, TimeSpan? providerWaitBudget = null)
		{
			this._provider = provider ?? throw new ArgumentNullException(nameof(provider));
			this._settings = settings ?? throw new ArgumentNullException(nameof(settings));
			this._owner = owner ?? throw new ArgumentNullException(nameof(owner));
			this._operations = operations ?? throw new ArgumentNullException(nameof(operations));
			this._startupSeed = settings.CaptureStartupSeed();
			this._providerWaitBudget = providerWaitBudget ?? TimeSpan.FromSeconds(30);
			this._coordinator = new DesktopTransitionCoordinator(this._startupSeed);
		}

		internal event EventHandler<DesktopRuntimeStateChanged> StateChanged;
		internal event EventHandler<DesktopRuntimeFault> Faulted;
		internal DesktopRuntimeState State => this._coordinator.State;
		internal bool IsInitialized => this._initialized;

		internal async Task<DesktopRuntimeInitializationResult> InitializeAsync(bool overrideDesktopsOnStartup = false, CancellationToken cancellationToken = default(CancellationToken))
		{
			this.EnsureOwnerAccess();
			if (this._initialized) return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Completed);
			if (this._shutdownStarted || this._stopping) return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.ShuttingDown);

			var targetCount = GetSeedTargetCount(this._startupSeed);
			var overrideEnabled = overrideDesktopsOnStartup && targetCount > 0;
			this._suppressPublication = overrideEnabled;
			this.SubscribeProviderEvents();
			var initial = this.CompleteInitialization(await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.Initialization, cancellationToken));
			if (!initial.Succeeded)
			{
				this.TerminateFailedInitialization();
				return initial;
			}
			if (!overrideEnabled) return initial;

			var plan = DesktopStartupOverridePlan.Create(this.State, targetCount);
			var journal = new DesktopOperationJournal(plan);
			journal.PlanSeedTargets(this._startupSeed, this.State);
			try
			{
				var topologySucceeded = this.ExecuteTopologyPlan(plan, journal);
				if (!topologySucceeded)
					return await this.CompleteFailedStartupOverrideAsync(journal, cancellationToken);

				if (targetCount != this.State.Order.Count)
				{
					var topology = await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
					if (topology.Status != VirtualDesktopReconciliationStatus.Succeeded)
						return await this.CompleteFailedStartupOverrideAsync(journal, cancellationToken);
					this.ApplyStableBatch(topology.Batch);
					if (this.State == null || this.State.Order.Count != targetCount)
						return await this.CompleteFailedStartupOverrideAsync(journal, cancellationToken);
				}

				var prepared = this._coordinator.BeginStagedRuntime();
				this._preparedRuntime = prepared;
				var propertiesSucceeded = this.ApplySeedToPreparedRuntime(prepared, this._startupSeed, true, journal);
				var confirmation = await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
				if (confirmation.Status != VirtualDesktopReconciliationStatus.Succeeded)
				{
					this._preparedRuntime = null;
					return await this.CompleteFailedStartupOverrideAsync(journal, cancellationToken);
				}
				journal.Confirm(confirmation.Batch, this.ReportUnconfirmedOverride);
				this.ApplyStableBatchToPrepared(confirmation.Batch);
				if (!propertiesSucceeded || journal.HasFailures)
					this._persistenceProtection = DesktopPersistenceProtection.FromSeed(prepared.Coordinator.State, journal);
				this._coordinator.CommitStagedRuntime(prepared, false);
				this._preparedRuntime = null;
				this._suppressPublication = false;
				this.PublishInitialization();
				var status = journal.HasFailures ? DesktopStartupOverrideStatus.CompletedWithFailures : DesktopStartupOverrideStatus.Completed;
				return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Completed, null, journal.Complete(status));
			}
			catch (OperationCanceledException)
			{
				this.TerminateFailedInitialization();
				return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Cancelled, null, journal.Complete(DesktopStartupOverrideStatus.Cancelled));
			}
			finally
			{
				this._preparedRuntime = null;
				this._suppressPublication = false;
			}
		}

		private bool ExecuteTopologyPlan(DesktopStartupOverridePlan plan, DesktopOperationJournal journal)
		{
			var failed = false;
			for (var index = 0; index < plan.CreateCount; index++)
			{
				if (failed) { journal.RecordTopology(DesktopStartupTopologyMutationKind.Create, null, DesktopOverrideOperationStatus.Skipped); continue; }
				try { this._operations.Create(); journal.RecordTopology(DesktopStartupTopologyMutationKind.Create, null, DesktopOverrideOperationStatus.Succeeded); }
				catch { journal.RecordTopology(DesktopStartupTopologyMutationKind.Create, null, DesktopOverrideOperationStatus.Failed); failed = true; }
			}
			foreach (var id in plan.RemoveIds)
			{
				if (failed) { journal.RecordTopology(DesktopStartupTopologyMutationKind.Remove, id, DesktopOverrideOperationStatus.Skipped); continue; }
				try { this._operations.Remove(id); journal.RecordTopology(DesktopStartupTopologyMutationKind.Remove, id, DesktopOverrideOperationStatus.Succeeded); }
				catch { journal.RecordTopology(DesktopStartupTopologyMutationKind.Remove, id, DesktopOverrideOperationStatus.Failed); failed = true; }
			}
			return !failed;
		}

		private async Task<DesktopRuntimeInitializationResult> CompleteFailedStartupOverrideAsync(DesktopOperationJournal journal, CancellationToken cancellationToken)
		{
			this._preparedRuntime = null;
			var recovery = await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.Recovery, cancellationToken);
			if (recovery.Status != VirtualDesktopReconciliationStatus.Succeeded)
			{
				this.TerminateFailedInitialization();
				return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Unavailable, recovery.FailureCategory, journal.Complete(DesktopStartupOverrideStatus.Unavailable));
			}
			this.ApplyStableBatch(recovery.Batch);
			this._persistenceProtection = DesktopPersistenceProtection.FromSeed(this.State, journal);
			this._suppressPublication = false;
			this.PublishInitialization();
			return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Completed, recovery.FailureCategory, journal.Complete(DesktopStartupOverrideStatus.CompletedWithFailures));
		}

		private async Task<VirtualDesktopReconciliationResult> RequestProviderWithBudgetAsync(VirtualDesktopStableReason reason, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested) return VirtualDesktopReconciliationResult.Cancelled();
			if (this._providerWaitBudget <= TimeSpan.Zero) return VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable);
			using (var providerCancellation = new CancellationTokenSource())
			using (var timeoutCancellation = new CancellationTokenSource())
			{
				var providerState = this._preparedRuntime?.Coordinator.State ?? this.State;
				var race = new ProviderWaitRace(providerCancellation, providerState?.ProviderEpoch ?? 0, providerState?.ProviderSnapshotRevision ?? 0);
				CancellationTokenRegistration callerCancellation = default(CancellationTokenRegistration);
				this.RegisterProviderWait(race);
				try
				{
					callerCancellation = cancellationToken.Register(race.CancelByCaller);
					_ = CompleteProviderWaitTimeoutAsync(race, this._providerWaitBudget, timeoutCancellation.Token);
					try
					{
						var request = this._provider.RequestReconciliationAsync(reason, providerCancellation.Token);
						_ = CompleteProviderWaitRequestAsync(race, request);
					}
					catch (OperationCanceledException)
					{
						race.TryComplete(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable), false);
					}
					catch
					{
						race.TryComplete(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable), false);
					}
					return await race.Completion;
				}
				finally
				{
					timeoutCancellation.Cancel();
					callerCancellation.Dispose();
					this.UnregisterProviderWait(race);
				}
			}
		}

		private static async Task CompleteProviderWaitRequestAsync(ProviderWaitRace race, Task<VirtualDesktopReconciliationResult> request)
		{
			try { race.TryComplete(await request.ConfigureAwait(false), false); }
			catch (OperationCanceledException) { race.TryComplete(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable), false); }
			catch { race.TryComplete(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable), false); }
		}
		private static async Task CompleteProviderWaitTimeoutAsync(ProviderWaitRace race, TimeSpan budget, CancellationToken cancellationToken)
		{
			try
			{
				await Task.Delay(budget, cancellationToken).ConfigureAwait(false);
				race.TryComplete(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable), true);
			}
			catch (OperationCanceledException) { }
		}
		private void RegisterProviderWait(ProviderWaitRace race) { lock (this._providerWaitGate) this._providerWaits.Add(race); }
		private void UnregisterProviderWait(ProviderWaitRace race) { lock (this._providerWaitGate) this._providerWaits.Remove(race); }
		private void ReserveProviderPublication(VirtualDesktopStableBatch batch)
		{
			ProviderWaitRace[] waits;
			lock (this._providerWaitGate) waits = this._providerWaits.ToArray();
			foreach (var wait in waits) wait.TryReservePublishedSuccess(batch);
		}
		private void CompleteReservedProviderPublications()
		{
			ProviderWaitRace[] waits;
			lock (this._providerWaitGate) waits = this._providerWaits.ToArray();
			foreach (var wait in waits) wait.CompleteReservedSuccess();
		}
		internal Task<VirtualDesktopReconciliationResult> RequestReconciliationAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			if (this._shutdownStarted || this._stopping) return Task.FromResult(VirtualDesktopReconciliationResult.ShuttingDown());
			return this._provider.RequestReconciliationAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
		}

		internal void EditName(Guid desktopId, string value)
			=> this.EnqueueOrRun(() => this.CommitLocalEdit(DesktopLocalEdit.Name(desktopId, value, this.State), () => this._operations.SetName(desktopId, value)));

		internal void EditWallpaperPath(Guid desktopId, string value)
			=> this.EnqueueOrRun(() =>
			{
				var record = this.GetRecord(desktopId);
				var unsupported = record.WallpaperPath.ReadStatus == VirtualDesktopReadStatus.Unsupported;
				this.CommitLocalEdit(
					DesktopLocalEdit.WallpaperPath(desktopId, value, this.State),
					() =>
					{
						if (!unsupported) this._operations.SetWallpaperPath(desktopId, value);
						else if (this.State.CurrentDesktopId == desktopId) this._operations.ApplyWallpaper(desktopId, value, record.WallpaperPosition);
					});
			});

		internal void EditWallpaperPosition(Guid desktopId, WallpaperPosition value)
			=> this.EnqueueOrRun(() =>
			{
				var record = this.GetRecord(desktopId);
				this.CommitLocalEdit(
					DesktopLocalEdit.WallpaperPosition(desktopId, value, this.State),
					() =>
					{
						if (this.State.CurrentDesktopId == desktopId) this._operations.ApplyWallpaper(desktopId, record.WallpaperPath.HasValue ? record.WallpaperPath.Value : null, value);
					});
			});

		internal void MoveLeft(Guid id) => this.EnqueueOrRun(() => this._operations.MoveLeft(id));
		internal void MoveRight(Guid id) => this.EnqueueOrRun(() => this._operations.MoveRight(id));
		internal void MoveFirst(Guid id) => this.EnqueueOrRun(() => this._operations.MoveFirst(id));
		internal void MoveLast(Guid id) => this.EnqueueOrRun(() => this._operations.MoveLast(id));
		internal void Switch(Guid id) => this.EnqueueOrRun(() => this._operations.Switch(id));
		internal void Remove(Guid id) => this.EnqueueOrRun(() => this._operations.Remove(id));

		internal int IndexOf(Guid desktopId)
		{
			var state = this.State;
			return state == null ? -1 : state.Order.IndexOf(desktopId);
		}

		internal DesktopRecord GetRecord(Guid desktopId)
		{
			var state = this.State;
			if (state == null || !state.Records.TryGetValue(desktopId, out var record)) throw new InvalidOperationException("The desktop is not present in the current Coordinator state.");
			return record;
		}

		internal async Task<SettingsImportCommitResult> ImportAsync(string path, bool overrideDesktops, CancellationToken cancellationToken)
		{
			if (this._shutdownStarted || this._stopping) return SettingsImportCommitResult.ShuttingDown();
			var stage = await this._settings.PrepareImportAsync(path);
			return await this.CommitPreparedImportAsync(stage, overrideDesktops, cancellationToken);
		}

		internal async Task<SettingsImportCommitResult> ResetSettingsAsync(CancellationToken cancellationToken)
		{
			if (this._shutdownStarted || this._stopping) return SettingsImportCommitResult.ShuttingDown();
			var stage = await this._settings.PrepareResetAsync();
			return await this.CommitPreparedImportAsync(stage, false, cancellationToken, true);
		}

		internal Task<SettingsImportCommitResult> CommitPreparedImportAsync(StagedSettingsImport stage, bool overrideDesktops, CancellationToken cancellationToken, bool resetPositions = false)
		{
			if (this._activeImportCommit != null && !this._activeImportCommit.IsCompleted) throw new InvalidOperationException("A settings import commit is already active.");
			this._activeImportCommit = this.CommitPreparedImportCoreAsync(stage, overrideDesktops, cancellationToken, resetPositions);
			return this._activeImportCommit;
		}

		private async Task<SettingsImportCommitResult> CommitPreparedImportCoreAsync(StagedSettingsImport stage, bool overrideDesktops, CancellationToken cancellationToken, bool resetPositions)
		{
			this.EnsureOwnerAccess();
			if (stage == null) throw new ArgumentNullException(nameof(stage));
			if (this._stopping)
			{
				this._settings.DiscardImport(stage);
				return SettingsImportCommitResult.ShuttingDown();
			}
			if (cancellationToken.IsCancellationRequested)
			{
				this._settings.DiscardImport(stage);
				return SettingsImportCommitResult.Cancelled();
			}

			var prepared = this._coordinator.BeginStagedRuntime();
			this._preparedRuntime = prepared;
			this._activeSettingsStage = stage;
			var preImportProjection = CreateProjection(this.State);
			var frozen = false;
			DesktopOperationJournal journal = null;
			try
			{
				var seed = SettingsService.CaptureDesktopStartupSeed(stage.Settings);
				if (overrideDesktops && GetSeedTargetCount(seed) > 0)
				{
					var plan = DesktopStartupOverridePlan.Create(prepared.Coordinator.State, GetSeedTargetCount(seed));
					journal = new DesktopOperationJournal(plan);
					if (!this.ExecuteTopologyPlan(plan, journal))
						return await this.RecoverFailedImportAsync(stage, preImportProjection, journal, cancellationToken);
					if (plan.CreateCount != 0 || plan.RemoveIds.Count != 0)
					{
						var topology = await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
						if (topology.Status != VirtualDesktopReconciliationStatus.Succeeded)
							return await this.FailImportWithoutStableStateAsync(stage, topology, preImportProjection, journal);
						this.ApplyStableBatchToPrepared(topology.Batch);
						if (prepared.Coordinator.State.Order.Count != plan.TargetCount)
							return await this.RecoverFailedImportAsync(stage, preImportProjection, journal, cancellationToken);
					}
				}

				var propertiesSucceeded = this.ApplySeedToPreparedRuntime(prepared, seed, overrideDesktops, journal, resetPositions);
				if (overrideDesktops && GetSeedTargetCount(seed) > 0)
				{
					var confirmation = await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
					if (confirmation.Status != VirtualDesktopReconciliationStatus.Succeeded)
						return await this.FailImportWithoutStableStateAsync(stage, confirmation, preImportProjection, journal);
					journal.Confirm(confirmation.Batch, this.ReportUnconfirmedOverride);
					if (!propertiesSucceeded || journal.HasFailures)
						return this.CompleteFailedImportWithStableState(stage, preImportProjection, confirmation.Batch, journal);
					this.ApplyStableBatchToPrepared(confirmation.Batch);
				}
				if (cancellationToken.IsCancellationRequested)
				{
					if (journal?.MayHaveMutated == true) this.ProtectFailedImportPreState(preImportProjection, journal);
					this._settings.DiscardImport(stage);
					return SettingsImportCommitResult.Cancelled();
				}

				this._importCommitFrozen = true;
				frozen = true;
				var frozenState = prepared.Coordinator.State;
				var dictionary = stage.CreateCommitDictionary();
				SettingsService.ApplyDesktopProjection(dictionary, CreateProjection(frozenState));
				var result = await this._settings.CommitImportAsync(stage, dictionary);
				if (!result.Succeeded) return result;
				this.EnsureOwnerAccess();
				this._persistenceProtection = null;
				var transition = this._coordinator.CommitStagedRuntime(prepared, false);
				this._preparedRuntime = null;
				this._settings.PublishImportCommitted();
				this.ApplyTransition(transition, false, false);
				this._importCommitFrozen = false;
				frozen = false;
				this.ReplayFrozenProviderIngress();
				return result;
			}
			catch (OperationCanceledException)
			{
				if (journal?.MayHaveMutated == true) this.ProtectFailedImportPreState(preImportProjection, journal);
				this._settings.DiscardImport(stage);
				return SettingsImportCommitResult.Cancelled();
			}
			catch (Exception ex)
			{
				if (journal?.MayHaveMutated == true) this.ProtectFailedImportPreState(preImportProjection, journal);
				this._settings.DiscardImport(stage);
				this.ReportFault(new DesktopRuntimeFault("SettingsTransaction", ex.GetType()));
				return SettingsImportCommitResult.Failed();
			}
			finally
			{
				if (this._preparedRuntime != null)
				{
					this._preparedRuntime = null;
					if (!this._stopping) _ = this.RequestReconciliationAsync();
				}
				if (frozen)
				{
					this._importCommitFrozen = false;
					this.ReplayFrozenProviderIngress();
				}
				this._activeSettingsStage = null;
				this.ScheduleDeferredCommands();
			}
		}

		private async Task<SettingsImportCommitResult> RecoverFailedImportAsync(StagedSettingsImport stage, DesktopSettingsProjection preImportProjection, DesktopOperationJournal journal, CancellationToken cancellationToken)
		{
			var recovery = await this.RequestProviderWithBudgetAsync(VirtualDesktopStableReason.Recovery, cancellationToken);
			if (recovery.Status != VirtualDesktopReconciliationStatus.Succeeded)
				return await this.FailImportWithoutStableStateAsync(stage, recovery, preImportProjection, journal);
			return this.CompleteFailedImportWithStableState(stage, preImportProjection, recovery.Batch, journal);
		}

		private Task<SettingsImportCommitResult> FailImportWithoutStableStateAsync(StagedSettingsImport stage, VirtualDesktopReconciliationResult result, DesktopSettingsProjection preImportProjection, DesktopOperationJournal journal)
		{
			if (journal?.MayHaveMutated == true) this.ProtectFailedImportPreState(preImportProjection, journal);
			this._settings.DiscardImport(stage);
			this._preparedRuntime = null;
			if (result.Status == VirtualDesktopReconciliationStatus.Cancelled) return Task.FromResult(SettingsImportCommitResult.Cancelled());
			if (result.Status == VirtualDesktopReconciliationStatus.ShuttingDown) return Task.FromResult(SettingsImportCommitResult.ShuttingDown());
			if (result.Status == VirtualDesktopReconciliationStatus.SupersededByReset) return Task.FromResult(SettingsImportCommitResult.SupersededByReset());
			return Task.FromResult(SettingsImportCommitResult.FailedWithoutStableState());
		}

		private SettingsImportCommitResult CompleteFailedImportWithStableState(StagedSettingsImport stage, DesktopSettingsProjection preImportProjection, VirtualDesktopStableBatch batch, DesktopOperationJournal journal)
		{
			this._settings.DiscardImport(stage);
			this._preparedRuntime = null;
			this.ProtectFailedImportPreState(preImportProjection, journal);
			this.ApplyStableBatch(batch);
			return SettingsImportCommitResult.CompletedWithFailures();
		}

		private void ProtectFailedImportPreState(DesktopSettingsProjection preImportProjection, DesktopOperationJournal journal)
		{
			this._persistenceProtection = DesktopPersistenceProtection.FromProjection(preImportProjection, this.State, journal?.HasPropertyOperations == true ? journal : null);
		}
		internal Task<DesktopRuntimeShutdownResult> ShutdownAsync()
		{
			if (this._shutdownTask != null) return this._shutdownTask;
			this._shutdownTask = this.ShutdownCoreAsync();
			return this._shutdownTask;
		}

		private async Task<DesktopRuntimeShutdownResult> ShutdownCoreAsync()
		{
			this._shutdownStarted = true;
			if (this._deferredCommands.Count != 0) this.DrainDeferredCommands();
			if (this._activeImportCommit != null && !this._activeImportCommit.IsCompleted)
				await this._activeImportCommit;
			if (this._activeSettingsStage != null)
			{
				var discard = this._settings.DiscardImport(this._activeSettingsStage);
				if (discard.Status != SettingsImportCommitStatus.Publishing)
				{
					this._activeSettingsStage = null;
					this._preparedRuntime = null;
				}
			}

			var final = await this._provider.RequestReconciliationAsync(VirtualDesktopStableReason.Recovery, CancellationToken.None);
			SettingsSaveResult saveResult = null;
			var shutdownStatus = DesktopRuntimeShutdownStatus.ReconciliationUnavailable;
			if (final.Status == VirtualDesktopReconciliationStatus.Succeeded)
			{
				var transition = this._coordinator.ApplyStableBatch(final.Batch);
				this.ApplyTransition(transition, true, false);
				var state = this.State;
				if (state != null)
				{
					saveResult = await this._settings.RequestSaveAsync(state.StateRevision);
					shutdownStatus = saveResult.Succeeded ? DesktopRuntimeShutdownStatus.Completed : DesktopRuntimeShutdownStatus.SaveFailed;
					if (!saveResult.Succeeded) this.ReportFault(new DesktopRuntimeFault("Shutdown.SettingsSave." + saveResult.ErrorCategory, saveResult.ExceptionType));
				}
			}
			else
			{
				this.ReportFault(new DesktopRuntimeFault("Shutdown." + final.Status, typeof(InvalidOperationException)));
			}

			this._stopping = true;
			this.UnsubscribeProviderEvents();
			this._provider.Dispose();
			return new DesktopRuntimeShutdownResult(shutdownStatus, final.Status, saveResult);
		}
		public void Dispose()
		{
			if (Interlocked.Exchange(ref this._disposeSignaled, 1) != 0) return;
			var shutdown = this.ShutdownAsync();
			if (!this._owner.CheckAccess()) shutdown.GetAwaiter().GetResult();
		}
		private static int GetSeedTargetCount(DesktopStartupSeed seed)
			=> Math.Max(seed.Names.Count, seed.WallpaperPaths.Count);

		private void SubscribeProviderEvents()
		{
			if (this._providerEventsSubscribed) return;
			this._provider.StableBatchPublished += this.OnStableBatchPublished;
			this._provider.CurrentTransitioned += this.OnCurrentTransitioned;
			this._provider.Faulted += this.OnProviderFaulted;
			this._providerEventsSubscribed = true;
		}

		private void UnsubscribeProviderEvents()
		{
			if (!this._providerEventsSubscribed) return;
			this._provider.StableBatchPublished -= this.OnStableBatchPublished;
			this._provider.CurrentTransitioned -= this.OnCurrentTransitioned;
			this._provider.Faulted -= this.OnProviderFaulted;
			this._providerEventsSubscribed = false;
		}

		private void TerminateFailedInitialization()
		{
			this._initialized = false;
			this._stopping = true;
			this._preparedRuntime = null;
			this._frozenProviderIngress.Clear();
			this._deferredCommands.Clear();
			this.UnsubscribeProviderEvents();
			this._provider.Dispose();
		}

		private void ReplayFrozenProviderIngress()
		{
			while (!this._importCommitFrozen && this._frozenProviderIngress.Count != 0 && !this._stopping)
				this._frozenProviderIngress.Dequeue()();
		}
		private DesktopRuntimeInitializationResult CompleteInitialization(VirtualDesktopReconciliationResult result)
		{
			this.EnsureOwnerAccess();
			switch (result.Status)
			{
				case VirtualDesktopReconciliationStatus.Succeeded:
					this.ApplyStableBatch(result.Batch);
					this._initialized = this.State != null;
					return new DesktopRuntimeInitializationResult(this._initialized ? DesktopRuntimeInitializationStatus.Completed : DesktopRuntimeInitializationStatus.Unavailable);
				case VirtualDesktopReconciliationStatus.Cancelled:
					return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Cancelled);
				case VirtualDesktopReconciliationStatus.ShuttingDown:
				case VirtualDesktopReconciliationStatus.SupersededByReset:
					return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.ShuttingDown);
				default:
					return new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Unavailable, result.FailureCategory);
			}
		}

		private void OnStableBatchPublished(object sender, VirtualDesktopStableBatch batch)
		{
			if (this._stopping) return;
			if (this._owner.CheckAccess()) this.ApplyStableBatch(batch, true);
			else if (!this._owner.Post(() => this.ApplyStableBatch(batch, true))) this.ReportFault(new DesktopRuntimeFault("OwnerPostRejected.StableBatch", typeof(InvalidOperationException)));
		}

		private void OnCurrentTransitioned(object sender, VirtualDesktopCurrentTransition transition)
		{
			if (this._stopping) return;
			if (this._owner.CheckAccess()) this.ApplyCurrentTransition(transition);
			else if (!this._owner.Post(() => this.ApplyCurrentTransition(transition))) this.ReportFault(new DesktopRuntimeFault("OwnerPostRejected.CurrentTransition", typeof(InvalidOperationException)));
		}

		private void OnProviderFaulted(object sender, VirtualDesktopProviderFault fault)
			=> this.ReportFault(new DesktopRuntimeFault("Provider." + fault.FailureCategory, fault.ExceptionType, fault.DesktopId, fault.Sequence));

		private void ApplyStableBatch(VirtualDesktopStableBatch batch, bool providerPublication = false)
		{
			this.EnsureOwnerAccess();
			if (this._importCommitFrozen)
			{
				this._frozenProviderIngress.Enqueue(() => this.ApplyStableBatch(batch));
				return;
			}
			var state = this.State;
			if (state != null && batch != null && (batch.ProviderEpoch < state.ProviderEpoch || (batch.ProviderEpoch == state.ProviderEpoch && batch.SnapshotRevision <= state.ProviderSnapshotRevision))) return;
			if (this._preparedRuntime != null)
			{
				this.ApplyStableBatchToPrepared(batch);
				if (providerPublication) this.ReserveProviderPublication(batch);
			}
			else
			{
				var applied = this._coordinator.ApplyStableBatch(batch);
				if (this._persistenceProtection != null && applied?.NewState != null) this._persistenceProtection = this._persistenceProtection.ReleaseNaturalMatches(applied.NewState);
				if (this._suppressPublication)
				{
					this._suppressedTransition = applied;
					if (providerPublication && applied?.Accepted == true) this.ReserveProviderPublication(batch);
				}
				else this.ApplyTransition(applied, transitionCommitted: providerPublication ? (Action)(() => this.ReserveProviderPublication(batch)) : null);
			}
			if (providerPublication) this.CompleteReservedProviderPublications();
		}

		private void ApplyCurrentTransition(VirtualDesktopCurrentTransition transition)
		{
			this.EnsureOwnerAccess();
			if (this._importCommitFrozen)
			{
				this._frozenProviderIngress.Enqueue(() => this.ApplyCurrentTransition(transition));
				return;
			}
			if (this._preparedRuntime != null) this._preparedRuntime.Coordinator.ApplyCurrentTransition(transition);
			else this.ApplyTransition(this._coordinator.ApplyCurrentTransition(transition));
		}

		private void PublishInitialization()
		{
			var state = this.State;
			if (state == null) return;
			var projection = CreateProjection(state);
			var changed = new DesktopStateChanged(DesktopStateChangeKind.Initialized, state, state.Order, null, null, null, null, null, null);
			this.ApplyTransition(DesktopCoordinatorTransition.AcceptedChange(state, projection, changed, true, false, DesktopReconciliationReason.None));
			this._suppressedTransition = null;
		}
		private void ApplyStableBatchToPrepared(VirtualDesktopStableBatch batch)
		{
			if (batch == null || this._preparedRuntime == null) return;
			var state = this._preparedRuntime.Coordinator.State;
			if (state != null && (batch.ProviderEpoch < state.ProviderEpoch || (batch.ProviderEpoch == state.ProviderEpoch && batch.SnapshotRevision <= state.ProviderSnapshotRevision))) return;
			this._preparedRuntime.Coordinator.ApplyStableBatch(batch);
		}

		private bool ApplySeedToPreparedRuntime(DesktopTransitionCoordinator.DesktopPreparedRuntime prepared, DesktopStartupSeed seed, bool overrideDesktops, DesktopOperationJournal journal = null, bool resetPositions = false)
		{
			var state = prepared.Coordinator.State;
			journal?.PlanSeedTargets(seed, state);
			for (var index = 0; index < state.Order.Count; index++)
			{
				var id = state.Order[index];
				if (index < seed.Positions.Count || resetPositions)
					this.CommitPreparedEdit(prepared.Coordinator, DesktopLocalEdit.WallpaperPosition(id, index < seed.Positions.Count ? seed.Positions[index] : WallpaperPosition.Fill, prepared.Coordinator.State), null, null);
			}
			if (!overrideDesktops) return true;

			var failed = false;
			for (var index = 0; index < prepared.Coordinator.State.Order.Count; index++)
			{
				var id = prepared.Coordinator.State.Order[index];
				if (index < seed.Names.Count && seed.Names[index] != null)
				{
					if (failed) journal?.Record(id, DesktopPropertyKind.Name, DesktopOverrideOperationStatus.Skipped, seed.Names[index], true);
					else if (!this.CommitPreparedEdit(prepared.Coordinator, DesktopLocalEdit.Name(id, seed.Names[index], prepared.Coordinator.State), () => this._operations.SetName(id, seed.Names[index]), journal)) failed = true;
				}
				if (index < seed.WallpaperPaths.Count && seed.WallpaperPaths[index] != null)
				{
					if (failed)
					{
						journal?.Record(id, DesktopPropertyKind.WallpaperPath, DesktopOverrideOperationStatus.Skipped, seed.WallpaperPaths[index], true);
						continue;
					}
					var path = seed.WallpaperPaths[index];
					var record = prepared.Coordinator.State.Records[id];
					var applicationAuthoritative = record.WallpaperPath.ReadStatus == VirtualDesktopReadStatus.Unsupported;
					Action operation = applicationAuthoritative
						? prepared.Coordinator.State.CurrentDesktopId == id ? (Action)(() => this._operations.ApplyWallpaper(id, path, record.WallpaperPosition)) : null
						: () => this._operations.SetWallpaperPath(id, path);
					if (!this.CommitPreparedEdit(prepared.Coordinator, DesktopLocalEdit.WallpaperPath(id, path, prepared.Coordinator.State), operation, journal, applicationAuthoritative)) failed = true;
				}
			}
			return !failed;
		}

		private bool CommitPreparedEdit(DesktopTransitionCoordinator coordinator, DesktopLocalEdit command, Action operation, DesktopOperationJournal journal, bool applicationAuthoritative = false)
		{
			var edit = coordinator.PrepareLocalEdit(command);
			if (!edit.Transition.Accepted)
			{
				if (command.Property.HasValue) journal?.Record(command.DesktopId, command.Property.Value, DesktopOverrideOperationStatus.Skipped, command.Value, true);
				return true;
			}
			try
			{
				operation?.Invoke();
				if (command.Property.HasValue)
				{
					if (applicationAuthoritative) journal?.RecordApplicationAuthoritative(command.DesktopId, command.Property.Value, command.Value, operation != null);
					else if (operation != null) journal?.Record(command.DesktopId, command.Property.Value, DesktopOverrideOperationStatus.Succeeded, command.Value, true);
				}
			}
			catch
			{
				if (command.Property.HasValue) journal?.Record(command.DesktopId, command.Property.Value, DesktopOverrideOperationStatus.Failed, command.Value, true);
				if (journal == null) throw;
				return false;
			}
			coordinator.CommitLocalEdit(edit);
			return true;
		}

		private void CommitLocalEdit(DesktopLocalEdit command, Action operation)
		{
			this.EnsureOwnerAccess();
			if (this._stopping || command == null) return;
			var prepared = this._coordinator.PrepareLocalEdit(command);
			if (!prepared.Transition.Accepted) return;
			try { operation(); }
			catch (Exception ex)
			{
				this.ReportFault(new DesktopRuntimeFault("LocalEdit", ex.GetType(), command.DesktopId));
				return;
			}
			if (command.Property.HasValue && this._persistenceProtection != null) this._persistenceProtection = this._persistenceProtection.Release(command.DesktopId, command.Property.Value);
			this.ApplyTransition(this._coordinator.CommitLocalEdit(prepared));
		}

		private void ApplyTransition(DesktopCoordinatorTransition transition, bool applyProjection = true, bool requestSave = true, Action transitionCommitted = null)
		{
			if (transition == null) return;
			if (!transition.Accepted)
			{
				if (transition.RequiresReconciliation) _ = this.RequestReconciliationAsync();
				return;
			}

			if (applyProjection && transition.Projection != null) this._settings.ApplyProjection(this._persistenceProtection?.CreateProjection(transition.NewState) ?? transition.Projection);
			transitionCommitted?.Invoke();
			this._publishing = true;
			try
			{
				if (transition.StateChanged != null)
				{
					var payload = new DesktopRuntimeStateChanged(transition.StateChanged, this._settings.SettingsRevision);
					var handlers = this.StateChanged;
					if (handlers != null)
					{
						foreach (EventHandler<DesktopRuntimeStateChanged> handler in handlers.GetInvocationList())
						{
							try { handler(this, payload); }
							catch (Exception ex) { this.ReportFault(new DesktopRuntimeFault("StateChangedSubscriber", ex.GetType())); }
						}
					}
				}
				if (requestSave && transition.RequiresSave) _ = this.ObserveSaveAsync(this._settings.RequestSaveAsync(transition.NewState.StateRevision));
			}
			finally
			{
				this._publishing = false;
				this.ScheduleDeferredCommands();
			}
			if (transition.RequiresReconciliation) _ = this.RequestReconciliationAsync();
		}

		private void ReportUnconfirmedOverride(Guid desktopId, DesktopPropertyKind property)
		{
			this.ReportFault(new DesktopRuntimeFault("Override.Unconfirmed." + property, typeof(InvalidOperationException), desktopId));
		}
		private async Task ObserveSaveAsync(Task<SettingsSaveResult> task)
		{
			var result = await task.ConfigureAwait(false);
			if (!result.Succeeded) this.ReportFault(new DesktopRuntimeFault("SettingsSave." + result.ErrorCategory, result.ExceptionType == null ? null : typeof(InvalidOperationException)));
		}

		private void EnqueueOrRun(Action command)
		{
			if (command == null || this._shutdownStarted || this._stopping) return;
			if (!this._owner.CheckAccess()) throw new InvalidOperationException("Desktop commands must be submitted on the owner Dispatcher.");
			if (this._publishing || this._preparedRuntime != null || this._deferredDrainScheduled || this._deferredCommands.Count != 0)
			{
				this._deferredCommands.Enqueue(command);
				if (!this._publishing && this._preparedRuntime == null) this.ScheduleDeferredCommands();
				return;
			}
			command();
		}

		private void ScheduleDeferredCommands()
		{
			if (this._deferredDrainScheduled || this._deferredCommands.Count == 0 || this._stopping) return;
			this._deferredDrainScheduled = true;
			if (!this._owner.Post(this.DrainDeferredCommands))
			{
				this._deferredDrainScheduled = false;
				this._deferredCommands.Clear();
				this.ReportFault(new DesktopRuntimeFault("DeferredCommand.Aborted", typeof(InvalidOperationException)));
			}
		}

		private void DrainDeferredCommands()
		{
			this.EnsureOwnerAccess();
			try
			{
				var count = this._deferredCommands.Count;
				while (count-- > 0 && this._deferredCommands.Count != 0 && !this._stopping)
					this._deferredCommands.Dequeue()();
			}
			finally
			{
				this._deferredDrainScheduled = false;
				if (this._deferredCommands.Count != 0 && !this._stopping) this.ScheduleDeferredCommands();
			}
		}

		private void ReportFault(DesktopRuntimeFault fault)
		{
			var handlers = this.Faulted;
			if (handlers == null) return;
			foreach (EventHandler<DesktopRuntimeFault> handler in handlers.GetInvocationList())
			{
				try { handler(this, fault); }
				catch { }
			}
		}

		private void EnsureOwnerAccess()
		{
			if (!this._owner.CheckAccess()) throw new InvalidOperationException("The desktop runtime can only be mutated by its owner Dispatcher.");
		}

		internal static string GetCurrentWallpaperPath(DesktopRuntimeState state)
		{
			if (state?.CurrentDesktopId == null || !state.Records.TryGetValue(state.CurrentDesktopId.Value, out var record)) return null;
			return record.WallpaperPath.HasValue && !string.IsNullOrEmpty(record.WallpaperPath.Value)
				? record.WallpaperPath.Value
				: null;
		}
		private static DesktopSettingsProjection CreateProjection(DesktopRuntimeState state)
		{
			if (state == null) throw new InvalidOperationException("The Coordinator has not accepted an initial stable state.");
			return new DesktopSettingsProjection(
				state.Order.Select(id => state.Records[id].Name.HasValue ? state.Records[id].Name.Value : null),
				state.Order.Select(id => state.Records[id].WallpaperPath.HasValue ? state.Records[id].WallpaperPath.Value : null),
				state.Order.Select(id => state.Records[id].WallpaperPosition));
		}
	}

	internal enum DesktopOperationConfirmationRequirement
	{
		RawStable,
		ApplicationAuthoritative,
	}

	internal sealed class DesktopOperationJournal	{
		private readonly DesktopStartupOverridePlan _plan;
		private readonly List<DesktopStartupTopologyMutationResult> _topologyEntries = new List<DesktopStartupTopologyMutationResult>();
		private readonly List<DesktopOperationJournalEntry> _entries = new List<DesktopOperationJournalEntry>();
		internal DesktopOperationJournal(DesktopStartupOverridePlan plan) => this._plan = plan ?? throw new ArgumentNullException(nameof(plan));
		internal Guid PlanId => this._plan.PlanId;
		internal Guid? FailedDesktopId { get; private set; }
		internal bool HasFailures => this._topologyEntries.Any(entry => entry.Status == DesktopOverrideOperationStatus.Failed || entry.Status == DesktopOverrideOperationStatus.Unconfirmed) || this._entries.Any(entry => entry.Status == DesktopOverrideOperationStatus.Failed || entry.Status == DesktopOverrideOperationStatus.Unconfirmed);
		internal bool HasPropertyOperations => this._entries.Count != 0;
		internal bool MayHaveMutated => this._topologyEntries.Any(entry => entry.Status == DesktopOverrideOperationStatus.Succeeded || entry.Status == DesktopOverrideOperationStatus.Failed || entry.Status == DesktopOverrideOperationStatus.Unconfirmed) || this._entries.Any(entry => entry.OperationAttempted);
		internal IEnumerable<DesktopOperationJournalEntry> ProtectionEntries => this._entries.Where(entry => entry.ProtectSeed);
		internal IEnumerable<DesktopOperationJournalEntry> ImportProtectionEntries => this._entries.Where(entry => entry.OperationAttempted);
		internal void RecordTopology(DesktopStartupTopologyMutationKind kind, Guid? desktopId, DesktopOverrideOperationStatus status)
		{
			this._topologyEntries.Add(new DesktopStartupTopologyMutationResult(kind, desktopId, status));
			if (status == DesktopOverrideOperationStatus.Failed) this.FailedDesktopId = desktopId;
		}
		internal void PlanSeedTargets(DesktopStartupSeed seed, DesktopRuntimeState state)
		{
			for (var index = 0; index < state.Order.Count; index++)
			{
				var id = state.Order[index];
				if (index < seed.Names.Count && seed.Names[index] != null) this.Plan(id, DesktopPropertyKind.Name, seed.Names[index]);
				if (index < seed.WallpaperPaths.Count && seed.WallpaperPaths[index] != null) this.Plan(id, DesktopPropertyKind.WallpaperPath, seed.WallpaperPaths[index]);
			}
		}
		private void Plan(Guid desktopId, DesktopPropertyKind property, string targetValue)
		{
			if (this._entries.Any(entry => entry.DesktopId == desktopId && entry.Property == property)) return;
			this._entries.Add(new DesktopOperationJournalEntry(desktopId, property, targetValue, true, DesktopOverrideOperationStatus.NotStarted, false, true));
		}
		internal void Record(Guid desktopId, DesktopPropertyKind property, DesktopOverrideOperationStatus status, string targetValue = null, bool targetValueSpecified = false)
		{
			var attempted = status == DesktopOverrideOperationStatus.Succeeded || status == DesktopOverrideOperationStatus.Failed || status == DesktopOverrideOperationStatus.Unconfirmed;
			var effectiveStatus = status == DesktopOverrideOperationStatus.Succeeded && targetValueSpecified ? DesktopOverrideOperationStatus.Unconfirmed : status;
			var protectSeed = status == DesktopOverrideOperationStatus.Failed || status == DesktopOverrideOperationStatus.Skipped || effectiveStatus == DesktopOverrideOperationStatus.Unconfirmed;
			var existing = this._entries.FirstOrDefault(entry => entry.DesktopId == desktopId && entry.Property == property && entry.Status == DesktopOverrideOperationStatus.NotStarted);
			if (existing != null)
			{
				existing.Status = effectiveStatus;
				existing.OperationAttempted = attempted;
				existing.ProtectSeed = protectSeed;
			}
			else this._entries.Add(new DesktopOperationJournalEntry(desktopId, property, targetValue, targetValueSpecified, effectiveStatus, attempted, protectSeed));
			if (status == DesktopOverrideOperationStatus.Failed) this.FailedDesktopId = desktopId;
		}
		internal void RecordApplicationAuthoritative(Guid desktopId, DesktopPropertyKind property, string targetValue, bool operationAttempted)
		{
			var existing = this._entries.FirstOrDefault(entry => entry.DesktopId == desktopId && entry.Property == property && entry.Status == DesktopOverrideOperationStatus.NotStarted);
			if (existing != null)
			{
				existing.Status = DesktopOverrideOperationStatus.Succeeded;
				existing.OperationAttempted = operationAttempted;
				existing.ProtectSeed = false;
				existing.ConfirmationRequirement = DesktopOperationConfirmationRequirement.ApplicationAuthoritative;
			}
			else this._entries.Add(new DesktopOperationJournalEntry(desktopId, property, targetValue, true, DesktopOverrideOperationStatus.Succeeded, operationAttempted, false, DesktopOperationConfirmationRequirement.ApplicationAuthoritative));
		}
		internal void Confirm(VirtualDesktopStableBatch batch, Action<Guid, DesktopPropertyKind> reportUnconfirmed)
		{
			foreach (var entry in this._entries.Where(candidate => candidate.Status == DesktopOverrideOperationStatus.Unconfirmed && candidate.OperationAttempted && candidate.ConfirmationRequirement == DesktopOperationConfirmationRequirement.RawStable))
			{
				var desktop = batch?.Desktops.FirstOrDefault(candidate => candidate.Id == entry.DesktopId);
				var readStatus = desktop == null ? VirtualDesktopReadStatus.NotAttempted : entry.Property == DesktopPropertyKind.Name ? desktop.NameReadStatus : desktop.WallpaperPathReadStatus;
				var rawValue = desktop == null ? null : entry.Property == DesktopPropertyKind.Name ? desktop.Name : desktop.WallpaperPath;
				if (readStatus == VirtualDesktopReadStatus.Success && rawValue == entry.TargetValue)
				{
					entry.Status = DesktopOverrideOperationStatus.Succeeded;
					entry.ProtectSeed = false;
				}
				else
				{
					entry.Status = DesktopOverrideOperationStatus.Unconfirmed;
					entry.ProtectSeed = readStatus == VirtualDesktopReadStatus.Failed || readStatus == VirtualDesktopReadStatus.NotAttempted;
					reportUnconfirmed?.Invoke(entry.DesktopId, entry.Property);
				}
			}
		}
		internal DesktopStartupOverrideResult Complete(DesktopStartupOverrideStatus status) => new DesktopStartupOverrideResult(this.PlanId, this._plan.TargetCount, status, this._topologyEntries, this._entries.Select(entry => new DesktopStartupMutationResult(entry.DesktopId, entry.Property, entry.Status)));
	}

	internal sealed class DesktopOperationJournalEntry
	{
		internal DesktopOperationJournalEntry(Guid desktopId, DesktopPropertyKind property, string targetValue, bool targetValueSpecified, DesktopOverrideOperationStatus status, bool operationAttempted, bool protectSeed, DesktopOperationConfirmationRequirement confirmationRequirement = DesktopOperationConfirmationRequirement.RawStable)
		{
			this.DesktopId = desktopId; this.Property = property; this.TargetValue = targetValue; this.TargetValueSpecified = targetValueSpecified; this.Status = status; this.OperationAttempted = operationAttempted; this.ProtectSeed = protectSeed; this.ConfirmationRequirement = confirmationRequirement;
		}
		internal Guid DesktopId { get; }
		internal DesktopPropertyKind Property { get; }
		internal string TargetValue { get; }
		internal bool TargetValueSpecified { get; }
		internal DesktopOverrideOperationStatus Status { get; set; }
		internal bool OperationAttempted { get; set; }
		internal bool ProtectSeed { get; set; }
		internal DesktopOperationConfirmationRequirement ConfirmationRequirement { get; set; }
	}

	internal sealed class ProviderWaitRace
	{
		private readonly CancellationTokenSource _providerCancellation;
		private readonly object _gate = new object();
		private readonly TaskCompletionSource<VirtualDesktopReconciliationResult> _completion = new TaskCompletionSource<VirtualDesktopReconciliationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		private VirtualDesktopReconciliationResult _reservedSuccess;
		private int _terminal;
		internal ProviderWaitRace(CancellationTokenSource providerCancellation, long providerEpoch, long snapshotRevision) { this._providerCancellation = providerCancellation ?? throw new ArgumentNullException(nameof(providerCancellation)); this.ProviderEpoch = providerEpoch; this.SnapshotRevision = snapshotRevision; }
		private long ProviderEpoch { get; }
		private long SnapshotRevision { get; }
		internal Task<VirtualDesktopReconciliationResult> Completion => this._completion.Task;
		internal void CancelByCaller() => this.TryComplete(VirtualDesktopReconciliationResult.Cancelled(), true);
		internal bool TryComplete(VirtualDesktopReconciliationResult result, bool cancelProvider)
		{
			if (result == null) result = VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable);
			lock (this._gate)
			{
				if (this._terminal != 0) return false;
				this._terminal = 2;
			}
			if (cancelProvider) this._providerCancellation.Cancel();
			this._completion.TrySetResult(result);
			return true;
		}
		internal bool TryReservePublishedSuccess(VirtualDesktopStableBatch batch)
		{
			if (batch == null || batch.ProviderEpoch < this.ProviderEpoch || (batch.ProviderEpoch == this.ProviderEpoch && batch.SnapshotRevision <= this.SnapshotRevision)) return false;
			lock (this._gate)
			{
				if (this._terminal != 0) return false;
				this._reservedSuccess = VirtualDesktopReconciliationResult.Succeeded(batch);
				this._terminal = 1;
				return true;
			}
		}
		internal void CompleteReservedSuccess()
		{
			VirtualDesktopReconciliationResult result;
			lock (this._gate)
			{
				if (this._terminal != 1) return;
				this._terminal = 2;
				result = this._reservedSuccess;
			}
			this._completion.TrySetResult(result);
		}
	}

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
