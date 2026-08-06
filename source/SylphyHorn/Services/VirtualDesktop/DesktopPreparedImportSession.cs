using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SylphyHorn.Serialization;
using WindowsDesktop;

namespace SylphyHorn.Services.DesktopTransitions
{
	internal enum DesktopImportSessionPhase
	{
		Applying,
		CommitFrozen,
		Completed,
	}

	internal enum DesktopImportTransactionOutcomeKind
	{
		CommitPreparedRuntime,
		ApplyRecoveredState,
		Discarded,
		DiscardedAndReconcile,
	}

	internal interface IDesktopImportTransactionRuntime
	{
		DesktopRuntimeState State { get; }
		DesktopTransitionCoordinator.DesktopPreparedRuntime BeginPreparedRuntime();
		Task<VirtualDesktopReconciliationResult> RequestProviderAsync(VirtualDesktopStableReason reason, CancellationToken cancellationToken);
		bool ExecuteTopologyPlan(DesktopStartupOverridePlan plan, DesktopOperationJournal journal);
		bool ApplySeed(DesktopTransitionCoordinator.DesktopPreparedRuntime prepared, DesktopStartupSeed seed, bool overrideDesktops, DesktopOperationJournal journal, bool resetPositions);
		bool CanCommitPreparedRuntime(DesktopTransitionCoordinator.DesktopPreparedRuntime prepared);
		void ReportUnconfirmedOverride(Guid desktopId, DesktopPropertyKind property);
		void ReportFault(Type exceptionType);
	}

	internal sealed class DesktopImportTransactionOutcome
	{
		private DesktopImportTransactionOutcome(
			DesktopImportTransactionOutcomeKind kind,
			SettingsImportCommitResult result,
			DesktopTransitionCoordinator.DesktopPreparedRuntime preparedRuntime = null,
			VirtualDesktopStableBatch recoveryBatch = null,
			DesktopPersistenceProtection persistenceProtection = null)
		{
			this.Kind = kind;
			this.Result = result ?? throw new ArgumentNullException(nameof(result));
			this.PreparedRuntime = preparedRuntime;
			this.RecoveryBatch = recoveryBatch;
			this.PersistenceProtection = persistenceProtection;
		}

		internal DesktopImportTransactionOutcomeKind Kind { get; }
		internal SettingsImportCommitResult Result { get; }
		internal DesktopTransitionCoordinator.DesktopPreparedRuntime PreparedRuntime { get; }
		internal VirtualDesktopStableBatch RecoveryBatch { get; }
		internal DesktopPersistenceProtection PersistenceProtection { get; }

		internal static DesktopImportTransactionOutcome CommitPreparedRuntime(
			SettingsImportCommitResult result,
			DesktopTransitionCoordinator.DesktopPreparedRuntime preparedRuntime)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			if (!result.Succeeded) throw new ArgumentException("Only a successfully published settings stage can activate a prepared runtime.", nameof(result));
			if (preparedRuntime == null) throw new ArgumentNullException(nameof(preparedRuntime));
			return new DesktopImportTransactionOutcome(DesktopImportTransactionOutcomeKind.CommitPreparedRuntime, result, preparedRuntime);
		}

		internal static DesktopImportTransactionOutcome ApplyRecoveredState(
			SettingsImportCommitResult result,
			VirtualDesktopStableBatch recoveryBatch,
			DesktopPersistenceProtection persistenceProtection)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			if (result.Status != SettingsImportCommitStatus.CompletedWithFailures)
				throw new ArgumentException("A recovered import must report CompletedWithFailures.", nameof(result));
			if (recoveryBatch == null) throw new ArgumentNullException(nameof(recoveryBatch));
			return new DesktopImportTransactionOutcome(
				DesktopImportTransactionOutcomeKind.ApplyRecoveredState,
				result,
				recoveryBatch: recoveryBatch,
				persistenceProtection: persistenceProtection);
		}

		internal static DesktopImportTransactionOutcome Discarded(
			SettingsImportCommitResult result,
			DesktopPersistenceProtection persistenceProtection = null)
			=> CreateDiscarded(DesktopImportTransactionOutcomeKind.Discarded, result, persistenceProtection);

		internal static DesktopImportTransactionOutcome DiscardedAndReconcile(
			SettingsImportCommitResult result,
			DesktopPersistenceProtection persistenceProtection = null)
			=> CreateDiscarded(DesktopImportTransactionOutcomeKind.DiscardedAndReconcile, result, persistenceProtection);

		private static DesktopImportTransactionOutcome CreateDiscarded(
			DesktopImportTransactionOutcomeKind kind,
			SettingsImportCommitResult result,
			DesktopPersistenceProtection persistenceProtection)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			if (result.Succeeded || result.Status == SettingsImportCommitStatus.CompletedWithFailures || result.Status == SettingsImportCommitStatus.Publishing)
				throw new ArgumentException("A non-terminal import cannot be represented as discarded.", nameof(result));
			return new DesktopImportTransactionOutcome(kind, result, persistenceProtection: persistenceProtection);
		}
	}

	internal sealed class DesktopImportProviderIngress
	{
		private DesktopImportProviderIngress(VirtualDesktopStableBatch stableBatch, VirtualDesktopCurrentTransition currentTransition)
		{
			this.StableBatch = stableBatch;
			this.CurrentTransition = currentTransition;
		}

		internal VirtualDesktopStableBatch StableBatch { get; }
		internal VirtualDesktopCurrentTransition CurrentTransition { get; }
		internal static DesktopImportProviderIngress Stable(VirtualDesktopStableBatch batch)
			=> new DesktopImportProviderIngress(batch ?? throw new ArgumentNullException(nameof(batch)), null);
		internal static DesktopImportProviderIngress Current(VirtualDesktopCurrentTransition transition)
			=> new DesktopImportProviderIngress(null, transition ?? throw new ArgumentNullException(nameof(transition)));
	}

	internal sealed class DesktopPreparedImportSession
	{
		private readonly IDesktopImportTransactionRuntime _runtime;
		private readonly IDesktopSettingsTransactions _settings;
		private readonly DesktopSettingsImportClaim _claim;
		private readonly bool _overrideDesktops;
		private readonly bool _resetPositions;
		private readonly Queue<DesktopImportProviderIngress> _frozenIngress = new Queue<DesktopImportProviderIngress>();
		private readonly DesktopSettingsProjection _preImportProjection;
		private bool _executed;

		internal DesktopPreparedImportSession(
			DesktopSettingsImportClaim claim,
			bool overrideDesktops,
			bool resetPositions,
			IDesktopImportTransactionRuntime runtime,
			IDesktopSettingsTransactions settings)
		{
			this._claim = claim ?? throw new ArgumentNullException(nameof(claim));
			this._runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			this._settings = settings ?? throw new ArgumentNullException(nameof(settings));
			this._overrideDesktops = overrideDesktops;
			this._resetPositions = resetPositions;
			this._preImportProjection = DesktopRuntimeProjection.Create(runtime.State);
			this.PreparedRuntime = runtime.BeginPreparedRuntime();
			this.Phase = DesktopImportSessionPhase.Applying;
		}

		internal DesktopImportSessionPhase Phase { get; private set; }
		internal DesktopTransitionCoordinator.DesktopPreparedRuntime PreparedRuntime { get; }
		internal DesktopRuntimeState PreparedState => this.PreparedRuntime.Coordinator.State;

		internal bool RouteStable(VirtualDesktopStableBatch batch)
		{
			if (this.Phase == DesktopImportSessionPhase.Completed) return false;
			if (this.Phase == DesktopImportSessionPhase.CommitFrozen)
			{
				this._frozenIngress.Enqueue(DesktopImportProviderIngress.Stable(batch));
				return true;
			}
			this.ApplyStableBatch(batch);
			return true;
		}

		internal bool RouteCurrent(VirtualDesktopCurrentTransition transition)
		{
			if (this.Phase == DesktopImportSessionPhase.Completed) return false;
			if (this.Phase == DesktopImportSessionPhase.CommitFrozen)
			{
				this._frozenIngress.Enqueue(DesktopImportProviderIngress.Current(transition));
				return true;
			}
			this.PreparedRuntime.Coordinator.ApplyCurrentTransition(transition);
			return true;
		}

		internal IReadOnlyList<DesktopImportProviderIngress> Complete()
		{
			if (this.Phase == DesktopImportSessionPhase.Completed) return Array.Empty<DesktopImportProviderIngress>();
			this.Phase = DesktopImportSessionPhase.Completed;
			var ingress = this._frozenIngress.ToArray();
			this._frozenIngress.Clear();
			return ingress;
		}

		internal SettingsImportCommitResult DiscardForShutdown()
			=> this.DiscardStage();

		internal async Task<DesktopImportTransactionOutcome> ExecuteAsync(CancellationToken cancellationToken)
		{
			if (this._executed) throw new InvalidOperationException("The prepared import session has already been consumed.");
			this._executed = true;
			DesktopOperationJournal journal = null;
			try
			{
				var seed = SettingsService.CaptureDesktopStartupSeed(this._claim.Stage.Settings);
				var targetCount = DesktopStartupOverridePlan.GetTargetCount(seed);
				if (this._overrideDesktops && targetCount > 0)
				{
					var plan = DesktopStartupOverridePlan.Create(this.PreparedState, targetCount);
					journal = new DesktopOperationJournal(plan);
					if (!this._runtime.ExecuteTopologyPlan(plan, journal))
						return await this.RecoverAsync(journal, cancellationToken);
					if (plan.CreateCount != 0 || plan.RemoveIds.Count != 0)
					{
						var topology = await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
						if (topology.Status != VirtualDesktopReconciliationStatus.Succeeded)
							return this.DiscardWithoutStableState(topology, journal);
						this.ApplyStableBatch(topology.Batch);
						if (this.PreparedState.Order.Count != plan.TargetCount)
							return await this.RecoverAsync(journal, cancellationToken);
					}
				}

				var propertiesSucceeded = this._runtime.ApplySeed(this.PreparedRuntime, seed, this._overrideDesktops, journal, this._resetPositions);
				if (this._overrideDesktops && targetCount > 0)
				{
					var confirmation = await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
					if (confirmation.Status != VirtualDesktopReconciliationStatus.Succeeded)
						return this.DiscardWithoutStableState(confirmation, journal);
					journal.Confirm(confirmation.Batch, this._runtime.ReportUnconfirmedOverride);
					if (!propertiesSucceeded || journal.HasFailures)
						return this.DiscardWithRecoveredState(confirmation.Batch, journal);
					this.ApplyStableBatch(confirmation.Batch);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					var protection = this.CreateProtection(journal);
					this.DiscardStage();
					return DesktopImportTransactionOutcome.DiscardedAndReconcile(SettingsImportCommitResult.Cancelled(), protection);
				}

				if (!this._runtime.CanCommitPreparedRuntime(this.PreparedRuntime))
				{
					this.DiscardStage();
					return DesktopImportTransactionOutcome.DiscardedAndReconcile(SettingsImportCommitResult.Failed());
				}

				this.Phase = DesktopImportSessionPhase.CommitFrozen;
				var dictionary = this._claim.Stage.CreateCommitDictionary();
				SettingsService.ApplyDesktopProjection(dictionary, DesktopRuntimeProjection.Create(this.PreparedState));
				var result = await this._settings.CommitImportAsync(this._claim, dictionary);
				if (!result.Succeeded)
					return DesktopImportTransactionOutcome.DiscardedAndReconcile(result);
				return DesktopImportTransactionOutcome.CommitPreparedRuntime(result, this.PreparedRuntime);
			}
			catch (OperationCanceledException)
			{
				var protection = this.CreateProtection(journal);
				this.DiscardStage();
				return DesktopImportTransactionOutcome.DiscardedAndReconcile(SettingsImportCommitResult.Cancelled(), protection);
			}
			catch (Exception ex)
			{
				var protection = this.CreateProtection(journal);
				this.DiscardStage();
				this._runtime.ReportFault(ex.GetType());
				return DesktopImportTransactionOutcome.DiscardedAndReconcile(SettingsImportCommitResult.Failed(), protection);
			}
		}

		private async Task<DesktopImportTransactionOutcome> RecoverAsync(DesktopOperationJournal journal, CancellationToken cancellationToken)
		{
			var recovery = await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.Recovery, cancellationToken);
			if (recovery.Status != VirtualDesktopReconciliationStatus.Succeeded)
				return this.DiscardWithoutStableState(recovery, journal);
			return this.DiscardWithRecoveredState(recovery.Batch, journal);
		}

		private DesktopImportTransactionOutcome DiscardWithoutStableState(VirtualDesktopReconciliationResult result, DesktopOperationJournal journal)
		{
			var protection = this.CreateProtection(journal);
			this.DiscardStage();
			if (result.Status == VirtualDesktopReconciliationStatus.Cancelled)
				return DesktopImportTransactionOutcome.Discarded(SettingsImportCommitResult.Cancelled(), protection);
			if (result.Status == VirtualDesktopReconciliationStatus.ShuttingDown)
				return DesktopImportTransactionOutcome.Discarded(SettingsImportCommitResult.ShuttingDown(), protection);
			if (result.Status == VirtualDesktopReconciliationStatus.SupersededByReset)
				return DesktopImportTransactionOutcome.Discarded(SettingsImportCommitResult.SupersededByReset(), protection);
			return DesktopImportTransactionOutcome.Discarded(SettingsImportCommitResult.FailedWithoutStableState(), protection);
		}

		private DesktopImportTransactionOutcome DiscardWithRecoveredState(VirtualDesktopStableBatch batch, DesktopOperationJournal journal)
		{
			var protection = this.CreateProtection(journal);
			this.DiscardStage();
			return DesktopImportTransactionOutcome.ApplyRecoveredState(
				SettingsImportCommitResult.CompletedWithFailures(),
				batch,
				protection);
		}

		private DesktopPersistenceProtection CreateProtection(DesktopOperationJournal journal)
		{
			if (journal?.MayHaveMutated != true) return null;
			return DesktopPersistenceProtection.FromProjection(
				this._preImportProjection,
				this._runtime.State,
				journal.HasPropertyOperations ? journal : null);
		}

		private SettingsImportCommitResult DiscardStage()
			=> this._settings.DiscardImport(this._claim);

		private void ApplyStableBatch(VirtualDesktopStableBatch batch)
		{
			if (batch == null) return;
			var state = this.PreparedState;
			if (state != null && (batch.ProviderEpoch < state.ProviderEpoch || (batch.ProviderEpoch == state.ProviderEpoch && batch.SnapshotRevision <= state.ProviderSnapshotRevision))) return;
			this.PreparedRuntime.Coordinator.ApplyStableBatch(batch);
		}
	}

	internal static class DesktopRuntimeProjection
	{
		internal static DesktopSettingsProjection Create(DesktopRuntimeState state)
		{
			if (state == null) throw new InvalidOperationException("The Coordinator has not accepted an initial stable state.");
			return new DesktopSettingsProjection(
				state.Order.Select(id => state.Records[id].Name.HasValue ? state.Records[id].Name.Value : null),
				state.Order.Select(id => state.Records[id].WallpaperPath.HasValue ? state.Records[id].WallpaperPath.Value : null),
				state.Order.Select(id => state.Records[id].WallpaperPosition));
		}
	}
}
