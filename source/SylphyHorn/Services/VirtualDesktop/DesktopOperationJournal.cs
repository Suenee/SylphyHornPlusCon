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

}
