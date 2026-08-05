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

}
