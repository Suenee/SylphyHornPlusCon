using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SylphyHorn.Lifetime;
using Xunit;

namespace SylphyHorn.Tests
{
	public class DisposableCollectionTests
	{
		[Fact]
		public void DisposeDisposesItemsInRegistrationOrderOnlyOnce()
		{
			var order = new List<int>();
			var collection = new DisposableCollection
			{
				new RecordingDisposable(1, order),
				new RecordingDisposable(2, order),
				new RecordingDisposable(3, order),
			};

			collection.Dispose();
			collection.Dispose();

			Assert.Equal(new[] { 1, 2, 3 }, order);
			Assert.Empty(collection);
		}

		[Fact]
		public void AddAfterDisposeThrowsWithoutDisposingOrAddingItem()
		{
			var order = new List<int>();
			var collection = new DisposableCollection();
			var item = new RecordingDisposable(1, order);
			collection.Dispose();

			Assert.Throws<ObjectDisposedException>(() => collection.Add(item));
			Assert.Empty(order);
			Assert.Empty(collection);
			Assert.DoesNotContain(item, collection);
		}

		[Fact]
		public void CollectionMembersOperateOnAConsistentSnapshot()
		{
			var order = new List<int>();
			var first = new RecordingDisposable(1, order);
			var second = new RecordingDisposable(2, order);
			var third = new RecordingDisposable(3, order);
			var collection = new DisposableCollection { first, second, third };

			Assert.False(collection.IsReadOnly);
			Assert.Equal(3, collection.Count);
			Assert.Contains(second, collection);
			Assert.Equal(new IDisposable[] { first, second, third }, collection.ToArray());
			Assert.Equal(new IDisposable[] { first, second, third }, ((IEnumerable)collection).Cast<IDisposable>().ToArray());

			var copy = new IDisposable[5];
			collection.CopyTo(copy, 1);
			Assert.Equal(new IDisposable[] { null, first, second, third, null }, copy);

			Assert.True(collection.Remove(second));
			Assert.False(collection.Remove(second));
			Assert.Equal(new IDisposable[] { first, third }, collection.ToArray());

			collection.Clear();
			Assert.Empty(collection);
			Assert.Empty(order);
		}

		[Fact]
		public void AddRejectsNull()
		{
			var collection = new DisposableCollection();

			Assert.Throws<ArgumentNullException>(() => collection.Add(null));
			Assert.Empty(collection);
		}

		[Fact]
		public void DisposeContinuesAfterExceptionsAndRethrowsTheFirstOnlyOnce()
		{
			var order = new List<int>();
			var firstException = new InvalidOperationException("first");
			var laterException = new ApplicationException("later");
			var collection = new DisposableCollection
			{
				new CallbackDisposable(() =>
				{
					order.Add(1);
					throw firstException;
				}),
				new CallbackDisposable(() => order.Add(2)),
				new CallbackDisposable(() =>
				{
					order.Add(3);
					throw laterException;
				}),
			};

			var thrown = Assert.Throws<InvalidOperationException>(() => collection.Dispose());

			Assert.Same(firstException, thrown);
			Assert.Equal(new[] { 1, 2, 3 }, order);
			Assert.Empty(collection);

			collection.Dispose();
			Assert.Equal(new[] { 1, 2, 3 }, order);
		}

		[Fact]
		public async Task DisposeLinearizesBeforeInvokingItems()
		{
			var disposeEntered = new ManualResetEventSlim();
			var releaseDispose = new ManualResetEventSlim();
			var rejectedItemDisposed = false;
			var collection = new DisposableCollection
			{
				new CallbackDisposable(() =>
				{
					disposeEntered.Set();
					releaseDispose.Wait(TestContext.Current.CancellationToken);
				}),
			};
			var rejectedItem = new CallbackDisposable(() => rejectedItemDisposed = true);
			var disposeTask = Task.Run(() => collection.Dispose(), TestContext.Current.CancellationToken);

			try
			{
				Assert.True(disposeEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
				Assert.Throws<ObjectDisposedException>(() => collection.Add(rejectedItem));
				Assert.False(rejectedItemDisposed);
				Assert.Empty(collection);
			}
			finally
			{
				releaseDispose.Set();
				await disposeTask;
			}
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

		private sealed class CallbackDisposable : IDisposable
		{
			private readonly Action _callback;

			internal CallbackDisposable(Action callback)
			{
				this._callback = callback;
			}

			public void Dispose()
			{
				this._callback();
			}
		}
	}
}
