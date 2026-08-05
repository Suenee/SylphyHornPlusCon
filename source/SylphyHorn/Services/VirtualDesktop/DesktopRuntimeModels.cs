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

}
