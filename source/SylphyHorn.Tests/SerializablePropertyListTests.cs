using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SylphyHorn.Serialization;
using Xunit;

namespace SylphyHorn.Tests
{
	public class SerializablePropertyListTests
	{
		[Fact]
		public async Task ResizeExpandsListAndWritesCountMetadata()
		{
			var provider = await CreateProviderAsync();
			var properties = new DesktopNamePropertyList("Names", 2, provider);

			properties.Resize(4);

			Assert.Equal(4, properties.Count);
			Assert.True(provider.TryGetValue("Names#Count", out int count));
			Assert.Equal(4, count);
		}

		[Fact]
		public async Task ResizeShrinksListAndRemovesDiscardedKeys()
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B", "C");

			properties.Resize(1);

			Assert.Equal(new[] { "A" }, Values(properties));
			Assert.False(provider.TryGetValue<string>("Names[1]", out _));
			Assert.False(provider.TryGetValue<string>("Names[2]", out _));
			Assert.True(provider.TryGetValue("Names#Count", out int count));
			Assert.Equal(1, count);
		}

		[Fact]
		public async Task ResizeIfEmptyPreservesLastNonEmptyValue()
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B", string.Empty, null);

			properties.ResizeIfEmpty(1);

			Assert.Equal(new[] { "A", "B" }, Values(properties));
			Assert.False(provider.TryGetValue<string>("Names[2]", out _));
			Assert.False(provider.TryGetValue<string>("Names[3]", out _));
		}

		[Fact]
		public async Task StretchToOnlyExpands()
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B");

			properties.StretchTo(1);
			Assert.Equal(2, properties.Count);

			properties.StretchTo(4);
			Assert.Equal(4, properties.Count);
			Assert.Equal(new[] { "A", "B", null, null }, Values(properties));
		}

		[Theory]
		[InlineData(-1, 0)]
		[InlineData(0, -1)]
		[InlineData(3, 0)]
		[InlineData(0, 3)]
		public async Task MoveRejectsIndicesOutsideList(int fromIndex, int toIndex)
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B", "C");

			Assert.Throws<ArgumentOutOfRangeException>(() => properties.Move(fromIndex, toIndex));
		}

		[Fact]
		public async Task MoveWithSameIndexDoesNothing()
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B", "C");

			properties.Move(1, 1);

			Assert.Equal(new[] { "A", "B", "C" }, Values(properties));
		}

		[Fact]
		public async Task MoveForwardShiftsValuesTowardStart()
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B", "C", "D");

			properties.Move(0, 2);

			Assert.Equal(new[] { "B", "C", "A", "D" }, Values(properties));
		}

		[Fact]
		public async Task MoveBackwardShiftsValuesTowardEnd()
		{
			var provider = await CreateProviderAsync();
			var properties = CreateNames("Names", provider, "A", "B", "C", "D");

			properties.Move(3, 1);

			Assert.Equal(new[] { "A", "D", "B", "C" }, Values(properties));
		}

		[Fact]
		public async Task CountMetadataCreatesMissingListEntries()
		{
			var provider = await CreateProviderAsync(new Dictionary<string, object>
			{
				["Names#Count"] = 3,
			});

			var properties = new DesktopNamePropertyList("Names", provider);

			Assert.Equal(3, properties.Count);
			Assert.Equal(new string[] { null, null, null }, Values(properties));
		}

		[Fact]
		public async Task ProviderReloadReloadsCountAndValues()
		{
			var provider = await CreateProviderAsync(new Dictionary<string, object>
			{
				["Names#Count"] = 2,
				["Names[0]"] = "A",
				["Names[1]"] = "B",
			});
			var properties = new DesktopNamePropertyList("Names", provider);

			await provider.ReloadAsync(new Dictionary<string, object>
			{
				["Names#Count"] = 3,
				["Names[0]"] = "C",
				["Names[1]"] = "D",
				["Names[2]"] = "E",
			});

			Assert.Equal(new[] { "C", "D", "E" }, Values(properties));
		}

		private static DesktopNamePropertyList CreateNames(
			string key,
			MemoryDictionaryProvider provider,
			params string[] values)
		{
			var properties = new DesktopNamePropertyList(key, values.Length, provider);
			for (var index = 0; index < values.Length; index++)
			{
				properties.Value[index].Value = values[index];
			}

			return properties;
		}

		private static string[] Values(DesktopNamePropertyList properties)
		{
			return properties.Value.Select(x => x.Value).ToArray();
		}

		private static async Task<MemoryDictionaryProvider> CreateProviderAsync(
			IDictionary<string, object> initialValues = null)
		{
			var provider = new MemoryDictionaryProvider(initialValues);
			await provider.InitializeAsync();
			return provider;
		}
	}
}
