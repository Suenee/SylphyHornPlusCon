using System;
using System.Collections.Generic;
using Livet;
using Xunit;

namespace SylphyHorn.Tests
{
	public class LivetCharacterizationTests
	{
		[Fact]
		public void CompositeDisposableDisposesInInsertionOrderOnlyOnce()
		{
			var order = new List<int>();
			var composite = new LivetCompositeDisposable
			{
				new RecordingDisposable(1, order),
				new RecordingDisposable(2, order),
				new RecordingDisposable(3, order),
			};

			composite.Dispose();
			composite.Dispose();

			Assert.Equal(new[] { 1, 2, 3 }, order);
		}

		[Fact]
		public void CompositeDisposableRejectsAddAfterDisposeWithoutDisposingItem()
		{
			var order = new List<int>();
			var composite = new LivetCompositeDisposable();
			var item = new RecordingDisposable(1, order);
			composite.Dispose();

			Assert.Throws<ObjectDisposedException>(() => composite.Add(item));
			Assert.Empty(order);
		}

		private sealed class RecordingDisposable : IDisposable
		{
			private readonly int _value;
			private readonly ICollection<int> _order;

			internal RecordingDisposable(int value, ICollection<int> order)
			{
				this._value = value;
				this._order = order;
			}

			public void Dispose()
			{
				this._order.Add(this._value);
			}
		}
	}
}
