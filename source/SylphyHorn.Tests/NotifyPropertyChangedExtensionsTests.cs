using System;
using System.Collections.Generic;
using System.ComponentModel;
using SylphyHorn.Lifetime;
using Xunit;

namespace SylphyHorn.Tests
{
	public class NotifyPropertyChangedExtensionsTests
	{
		[Fact]
		public void SubscribeForwardsPropertyNamesSynchronouslyWithoutInitialCallback()
		{
			var source = new PropertyChangedSource();
			var propertyNames = new List<string>();
			var subscription = source.Subscribe(propertyNames.Add);

			Assert.Empty(propertyNames);

			source.Raise("Value");

			Assert.Equal(new[] { "Value" }, propertyNames);
			subscription.Dispose();
		}

		[Fact]
		public void SubscribeForwardsNullAndEmptyPropertyNames()
		{
			var source = new PropertyChangedSource();
			var propertyNames = new List<string>();
			using (source.Subscribe(propertyNames.Add))
			{
				source.Raise(null);
				source.Raise(string.Empty);
			}

			Assert.Equal(new string[] { null, string.Empty }, propertyNames);
		}

		[Fact]
		public void DisposeUnsubscribesAndIsIdempotent()
		{
			var source = new PropertyChangedSource();
			var propertyNames = new List<string>();
			var subscription = source.Subscribe(propertyNames.Add);

			subscription.Dispose();
			subscription.Dispose();
			source.Raise("Value");

			Assert.Empty(propertyNames);
		}

		[Fact]
		public void SubscribeRejectsNullArguments()
		{
			var source = new PropertyChangedSource();

			Assert.Throws<ArgumentNullException>(
				() => NotifyPropertyChangedExtensions.Subscribe(null, _ => { }));
			Assert.Throws<ArgumentNullException>(
				() => NotifyPropertyChangedExtensions.Subscribe(source, null));
		}

		private sealed class PropertyChangedSource : INotifyPropertyChanged
		{
			public event PropertyChangedEventHandler PropertyChanged;

			internal void Raise(string propertyName)
			{
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			}
		}
	}
}
