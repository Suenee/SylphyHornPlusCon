using System;
using System.Threading;
using System.Threading.Tasks;
using WindowsDesktop;

namespace SylphyHorn.Services.DesktopTransitions
{
	internal enum DesktopStartupPublicationMode
	{
		Immediate,
		DeferredUntilOverrideCompletes,
	}

	internal enum DesktopStartupTransactionOutcomeKind
	{
		AlreadyPublished,
		CommitPreparedAndPublish,
		PublishRecovered,
		Terminate,
	}

	internal interface IDesktopStartupTransactionRuntime
	{
		DesktopRuntimeState State { get; }
		Task<VirtualDesktopReconciliationResult> RequestProviderAsync(VirtualDesktopStableReason reason, CancellationToken cancellationToken);
		DesktopRuntimeInitializationResult CommitInitialReconciliation(VirtualDesktopReconciliationResult result, DesktopStartupPublicationMode publicationMode);
		void ApplyStableBatch(VirtualDesktopStableBatch batch);
		bool ExecuteTopologyPlan(DesktopStartupOverridePlan plan, DesktopOperationJournal journal);
		DesktopTransitionCoordinator.DesktopPreparedRuntime BeginPreparedRuntime();
		void ClearPreparedRuntime();
		bool ApplySeed(DesktopTransitionCoordinator.DesktopPreparedRuntime prepared, DesktopStartupSeed seed, DesktopOperationJournal journal);
		void ApplyStableBatchToPrepared(VirtualDesktopStableBatch batch);
		void ReportUnconfirmedOverride(Guid desktopId, DesktopPropertyKind property);
	}

	internal sealed class DesktopStartupTransactionOutcome
	{
		private DesktopStartupTransactionOutcome(
			DesktopStartupTransactionOutcomeKind kind,
			DesktopRuntimeInitializationResult result,
			DesktopTransitionCoordinator.DesktopPreparedRuntime preparedRuntime = null,
			DesktopPersistenceProtection persistenceProtection = null)
		{
			this.Kind = kind;
			this.Result = result ?? throw new ArgumentNullException(nameof(result));
			this.PreparedRuntime = preparedRuntime;
			this.PersistenceProtection = persistenceProtection;
		}

		internal DesktopStartupTransactionOutcomeKind Kind { get; }
		internal DesktopRuntimeInitializationResult Result { get; }
		internal DesktopTransitionCoordinator.DesktopPreparedRuntime PreparedRuntime { get; }
		internal DesktopPersistenceProtection PersistenceProtection { get; }
		internal static DesktopStartupTransactionOutcome AlreadyPublished(DesktopRuntimeInitializationResult result)
		{
			if (!result.Succeeded) throw new ArgumentException("Only a successful initialization can already be published.", nameof(result));
			return new DesktopStartupTransactionOutcome(DesktopStartupTransactionOutcomeKind.AlreadyPublished, result);
		}
		internal static DesktopStartupTransactionOutcome CommitPreparedAndPublish(
			DesktopRuntimeInitializationResult result,
			DesktopTransitionCoordinator.DesktopPreparedRuntime preparedRuntime,
			DesktopPersistenceProtection persistenceProtection)
		{
			if (preparedRuntime == null) throw new ArgumentNullException(nameof(preparedRuntime));
			if (!result.Succeeded) throw new ArgumentException("A prepared startup runtime can only be committed for a successful initialization.", nameof(result));
			return new DesktopStartupTransactionOutcome(
				DesktopStartupTransactionOutcomeKind.CommitPreparedAndPublish,
				result,
				preparedRuntime,
				persistenceProtection);
		}
		internal static DesktopStartupTransactionOutcome PublishRecovered(
			DesktopRuntimeInitializationResult result,
			DesktopPersistenceProtection persistenceProtection)
		{
			if (!result.Succeeded) throw new ArgumentException("A recovered startup state can only be published for a successful initialization.", nameof(result));
			return new DesktopStartupTransactionOutcome(
				DesktopStartupTransactionOutcomeKind.PublishRecovered,
				result,
				persistenceProtection: persistenceProtection);
		}
		internal static DesktopStartupTransactionOutcome Terminate(DesktopRuntimeInitializationResult result)
		{
			if (result.Succeeded) throw new ArgumentException("A successful initialization cannot terminate the runtime.", nameof(result));
			return new DesktopStartupTransactionOutcome(DesktopStartupTransactionOutcomeKind.Terminate, result);
		}
	}

	internal sealed class DesktopStartupTransaction
	{
		private readonly DesktopStartupSeed _seed;
		private readonly bool _overrideEnabled;
		private readonly DesktopStartupPublicationMode _publicationMode;
		private readonly IDesktopStartupTransactionRuntime _runtime;

		internal DesktopStartupTransaction(DesktopStartupSeed seed, bool overrideEnabled, IDesktopStartupTransactionRuntime runtime)
		{
			this._seed = seed ?? throw new ArgumentNullException(nameof(seed));
			this._overrideEnabled = overrideEnabled;
			this._publicationMode = overrideEnabled
				? DesktopStartupPublicationMode.DeferredUntilOverrideCompletes
				: DesktopStartupPublicationMode.Immediate;
			this._runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		}

		internal async Task<DesktopStartupTransactionOutcome> ExecuteAsync(CancellationToken cancellationToken)
		{
			var targetCount = DesktopStartupOverridePlan.GetTargetCount(this._seed);
			var initial = this._runtime.CommitInitialReconciliation(
				await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.Initialization, cancellationToken),
				this._publicationMode);
			if (!initial.Succeeded)
				return DesktopStartupTransactionOutcome.Terminate(initial);
			if (!this._overrideEnabled)
				return DesktopStartupTransactionOutcome.AlreadyPublished(initial);

			var plan = DesktopStartupOverridePlan.Create(this._runtime.State, targetCount);
			var journal = new DesktopOperationJournal(plan);
			journal.PlanSeedTargets(this._seed, this._runtime.State);
			try
			{
				if (!this._runtime.ExecuteTopologyPlan(plan, journal))
					return await this.RecoverAsync(journal, cancellationToken);

				if (targetCount != this._runtime.State.Order.Count)
				{
					var topology = await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
					if (topology.Status != VirtualDesktopReconciliationStatus.Succeeded)
						return await this.RecoverAsync(journal, cancellationToken);
					this._runtime.ApplyStableBatch(topology.Batch);
					if (this._runtime.State == null || this._runtime.State.Order.Count != targetCount)
						return await this.RecoverAsync(journal, cancellationToken);
				}

				var prepared = this._runtime.BeginPreparedRuntime();
				var propertiesSucceeded = this._runtime.ApplySeed(prepared, this._seed, journal);
				var confirmation = await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.ExplicitReconciliation, cancellationToken);
				if (confirmation.Status != VirtualDesktopReconciliationStatus.Succeeded)
				{
					this._runtime.ClearPreparedRuntime();
					return await this.RecoverAsync(journal, cancellationToken);
				}
				journal.Confirm(confirmation.Batch, this._runtime.ReportUnconfirmedOverride);
				this._runtime.ApplyStableBatchToPrepared(confirmation.Batch);
				var protection = !propertiesSucceeded || journal.HasFailures
					? DesktopPersistenceProtection.FromSeed(prepared.Coordinator.State, journal)
					: null;
				var status = journal.HasFailures ? DesktopStartupOverrideStatus.CompletedWithFailures : DesktopStartupOverrideStatus.Completed;
				return DesktopStartupTransactionOutcome.CommitPreparedAndPublish(
					new DesktopRuntimeInitializationResult(DesktopRuntimeInitializationStatus.Completed, null, journal.Complete(status)),
					prepared,
					protection);
			}
			catch (OperationCanceledException)
			{
				return DesktopStartupTransactionOutcome.Terminate(
					new DesktopRuntimeInitializationResult(
						DesktopRuntimeInitializationStatus.Cancelled,
						null,
						journal.Complete(DesktopStartupOverrideStatus.Cancelled)));
			}
		}

		private async Task<DesktopStartupTransactionOutcome> RecoverAsync(DesktopOperationJournal journal, CancellationToken cancellationToken)
		{
			this._runtime.ClearPreparedRuntime();
			var recovery = await this._runtime.RequestProviderAsync(VirtualDesktopStableReason.Recovery, cancellationToken);
			if (recovery.Status != VirtualDesktopReconciliationStatus.Succeeded)
			{
				return DesktopStartupTransactionOutcome.Terminate(
					new DesktopRuntimeInitializationResult(
						DesktopRuntimeInitializationStatus.Unavailable,
						recovery.FailureCategory,
						journal.Complete(DesktopStartupOverrideStatus.Unavailable)));
			}
			this._runtime.ApplyStableBatch(recovery.Batch);
			return DesktopStartupTransactionOutcome.PublishRecovered(
				new DesktopRuntimeInitializationResult(
					DesktopRuntimeInitializationStatus.Completed,
					recovery.FailureCategory,
					journal.Complete(DesktopStartupOverrideStatus.CompletedWithFailures)),
				DesktopPersistenceProtection.FromSeed(this._runtime.State, journal));
		}
	}
}
