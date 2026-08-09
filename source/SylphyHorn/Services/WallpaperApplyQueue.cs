using System;

using System.Runtime.ExceptionServices;

namespace SylphyHorn.Services
{
	internal sealed class WallpaperApplyQueue : IDisposable
	{
		private readonly object _gate = new object();
		private readonly Action<string, WallpaperPosition> _apply;
		private readonly Action<Exception> _reportError;
		private readonly Action<Action> _schedule;
		private readonly IWallpaperApplyWaiter _waiter;
		private WallpaperApplyRequest _pending;
		private long _generation;
		private bool _workerScheduled;
		private bool _applying;
		private bool _disposed;

		internal WallpaperApplyQueue(Action<string, WallpaperPosition> apply, Action<Exception> reportError, Action<Action> schedule, IWallpaperApplyWaiter waiter = null)
		{
			this._apply = apply ?? throw new ArgumentNullException(nameof(apply));
			this._reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
			this._schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
			this._waiter = waiter ?? WallpaperApplyWaiter.Instance;
		}

		internal void Enqueue(string path, WallpaperPosition position)
		{
			var scheduleWorker = false;
			long generation;
			lock (this._gate)
			{
				if (this._disposed) return;
				generation = ++this._generation;
				this._pending = new WallpaperApplyRequest(path, position, generation);
				if (!this._workerScheduled)
				{
					this._workerScheduled = true;
					scheduleWorker = true;
				}
			}

			if (scheduleWorker) this.ScheduleWorker(generation, true);
		}

		internal void ApplyNow(string path, WallpaperPosition position)
		{
			WallpaperApplyRequest invalidatedRequest;
			long invalidationGeneration;
			lock (this._gate)
			{
				this.ThrowIfDisposed();
				invalidationGeneration = ++this._generation;
				invalidatedRequest = this._pending;
				this._pending = null;
				while (this._applying && !this._disposed) this._waiter.Wait(this._gate, WallpaperApplyWaitReason.SynchronousApply);
				this.ThrowIfDisposed();
				this._applying = true;
			}

			Exception error = null;
			try { this._apply(path, position); }
			catch (Exception ex) { error = ex; }
			finally { this.CompleteApply(); }

			if (error == null) return;

			var scheduleWorker = false;
			if (invalidatedRequest != null)
			{
				lock (this._gate)
				{
					if (!this._disposed && this._generation == invalidationGeneration && this._pending == null)
					{
						this._pending = new WallpaperApplyRequest(invalidatedRequest.Path, invalidatedRequest.Position, invalidationGeneration);
						if (!this._workerScheduled)
						{
							this._workerScheduled = true;
							scheduleWorker = true;
						}
					}
				}
			}
			if (scheduleWorker) this.ScheduleWorker(invalidationGeneration, true);
			ExceptionDispatchInfo.Capture(error).Throw();
		}

		private void ScheduleWorker(long attemptGeneration, bool allowHandoffRetry)
		{
			try { this._schedule(this.Process); }
			catch (Exception ex)
			{
				var retry = false;
				long retryGeneration = 0;
				lock (this._gate)
				{
					this._workerScheduled = false;
					if (!this._disposed && this._pending != null)
					{
						if (allowHandoffRetry && this._pending.Generation != attemptGeneration)
						{
							retryGeneration = this._pending.Generation;
							this._workerScheduled = true;
							retry = true;
						}
						else
						{
							this._pending = null;
						}
					}
				}
				this.ReportError(ex);
				if (retry) this.ScheduleWorker(retryGeneration, false);
			}
		}

		private void Process()
		{
			while (true)
			{
				WallpaperApplyRequest request;
				lock (this._gate)
				{
					while (this._applying && !this._disposed) this._waiter.Wait(this._gate, WallpaperApplyWaitReason.AutomaticApply);
					if (this._disposed)
					{
						this._pending = null;
						this._workerScheduled = false;
						return;
					}

					request = this._pending;
					this._pending = null;
					if (request == null)
					{
						this._workerScheduled = false;
						return;
					}
					if (request.Generation != this._generation) continue;
					this._applying = true;
				}

				Exception error = null;
				try { this._apply(request.Path, request.Position); }
				catch (Exception ex) { error = ex; }
				finally { this.CompleteApply(); }
				if (error != null) this.ReportError(error);
			}
		}

		private void CompleteApply()
		{
			lock (this._gate)
			{
				this._applying = false;
				this._waiter.PulseAll(this._gate);
			}
		}

		private void ReportError(Exception exception)
		{
			try { this._reportError(exception); }
			catch { }
		}

		private void ThrowIfDisposed()
		{
			if (this._disposed) throw new ObjectDisposedException(nameof(WallpaperApplyQueue));
		}

		public void Dispose()
		{
			lock (this._gate)
			{
				if (this._disposed) return;
				this._disposed = true;
				this._generation++;
				this._pending = null;
				this._waiter.PulseAll(this._gate);
				while (this._applying) this._waiter.Wait(this._gate, WallpaperApplyWaitReason.Dispose);
			}
		}

		private sealed class WallpaperApplyRequest
		{
			internal WallpaperApplyRequest(string path, WallpaperPosition position, long generation)
			{
				this.Path = path;
				this.Position = position;
				this.Generation = generation;
			}

			internal string Path { get; }
			internal WallpaperPosition Position { get; }
			internal long Generation { get; }
		}
	}

	internal enum WallpaperApplyWaitReason
	{
		AutomaticApply,
		SynchronousApply,
		Dispose,
	}

	internal interface IWallpaperApplyWaiter
	{
		void Wait(object gate, WallpaperApplyWaitReason reason);
		void PulseAll(object gate);
	}

	internal sealed class WallpaperApplyWaiter : IWallpaperApplyWaiter
	{
		internal static WallpaperApplyWaiter Instance { get; } = new WallpaperApplyWaiter();
		private WallpaperApplyWaiter() { }
		public void Wait(object gate, WallpaperApplyWaitReason reason) => System.Threading.Monitor.Wait(gate);
		public void PulseAll(object gate) => System.Threading.Monitor.PulseAll(gate);
	}
}
