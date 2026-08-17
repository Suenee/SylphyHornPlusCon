using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace SylphyHorn.Lifetime
{
	public sealed class DisposableCollection : ICollection<IDisposable>, IDisposable
	{
		private readonly object _gate = new object();
		private readonly List<IDisposable> _items = new List<IDisposable>();
		private bool _disposed;

		public int Count
		{
			get
			{
				lock (this._gate)
				{
					return this._items.Count;
				}
			}
		}

		public bool IsReadOnly => false;

		public void Add(IDisposable item)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));

			lock (this._gate)
			{
				this.ThrowIfDisposed();
				this._items.Add(item);
			}
		}

		public void Clear()
		{
			lock (this._gate)
			{
				this._items.Clear();
			}
		}

		public bool Contains(IDisposable item)
		{
			lock (this._gate)
			{
				return this._items.Contains(item);
			}
		}

		public void CopyTo(IDisposable[] array, int arrayIndex)
		{
			lock (this._gate)
			{
				this._items.CopyTo(array, arrayIndex);
			}
		}

		public bool Remove(IDisposable item)
		{
			lock (this._gate)
			{
				return this._items.Remove(item);
			}
		}

		public IEnumerator<IDisposable> GetEnumerator()
		{
			IDisposable[] items;
			lock (this._gate)
			{
				items = this._items.ToArray();
			}

			return ((IEnumerable<IDisposable>)items).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		public void Dispose()
		{
			IDisposable[] items;
			lock (this._gate)
			{
				if (this._disposed) return;

				this._disposed = true;
				items = this._items.ToArray();
				this._items.Clear();
			}

			ExceptionDispatchInfo firstException = null;
			foreach (var item in items)
			{
				try
				{
					item.Dispose();
				}
				catch (Exception exception)
				{
					if (firstException == null)
					{
						firstException = ExceptionDispatchInfo.Capture(exception);
					}
				}
			}

			firstException?.Throw();
		}

		private void ThrowIfDisposed()
		{
			if (this._disposed) throw new ObjectDisposedException(nameof(DisposableCollection));
		}
	}
}
