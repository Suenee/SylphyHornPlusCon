using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace SylphyHorn.Interop
{
	/// <summary>
	/// Provides the application's mutex-only single-instance ownership.
	/// </summary>
	internal sealed class SingleInstance : IDisposable
	{
		private readonly Mutex _mutex;
		private readonly bool _owned;
		private bool _disposed;

		internal bool IsFirst => this._owned;

		internal string MutexName { get; }

		internal SingleInstance(Assembly identityAssembly, TimeSpan acquireTimeout)
		{
			var guid = ((GuidAttribute)Attribute.GetCustomAttribute(identityAssembly, typeof(GuidAttribute))).Value;

			// Keep the historical MetroTrilithon name so existing instances remain compatible.
			this.MutexName = "MetroTrilithon.Desktop.ApplicationInstance_" + guid;
			this._mutex = new Mutex(initiallyOwned: false, name: this.MutexName);
			try
			{
				this._owned = this._mutex.WaitOne(acquireTimeout);
			}
			catch (AbandonedMutexException)
			{
				this._owned = true;
			}
		}

		public void Dispose()
		{
			if (this._disposed)
			{
				return;
			}

			try
			{
				if (this._owned)
				{
					this._mutex.ReleaseMutex();
				}
			}
			finally
			{
				this._mutex.Dispose();
				this._disposed = true;
			}
		}
	}
}
