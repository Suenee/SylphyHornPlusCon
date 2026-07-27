using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SylphyHorn.Serialization;
using Xunit;

namespace SylphyHorn.Tests
{
	public class ShortcutkeyPropertyTests
	{
		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public async Task NullOrEmptyListSerializesAsNone(bool useNull)
		{
			var provider = await CreateProviderAsync();
			var property = new ShortcutkeyProperty("Shortcut", provider);

			property.Value = useNull ? null : Array.Empty<int>();

			Assert.True(provider.TryGetValue("Shortcut", out string serialized));
			Assert.Equal("(none)", serialized);
		}

		[Theory]
		[InlineData("(none)")]
		[InlineData("(NONE)")]
		[InlineData("(NoNe)")]
		public async Task NoneDeserializesAsEmptyListIgnoringCase(string serialized)
		{
			var provider = await CreateProviderAsync(new Dictionary<string, object>
			{
				["Shortcut"] = serialized,
			});

			var property = new ShortcutkeyProperty("Shortcut", provider);

			Assert.Empty(property.Value);
		}

		[Fact]
		public async Task EmptyStringDeserializesAsNull()
		{
			var provider = await CreateProviderAsync(new Dictionary<string, object>
			{
				["Shortcut"] = string.Empty,
			});

			var property = new ShortcutkeyProperty("Shortcut", provider);

			Assert.Null(property.Value);
		}

		[Fact]
		public async Task CommaSeparatedValuesDeserializeAsIntegers()
		{
			var provider = await CreateProviderAsync(new Dictionary<string, object>
			{
				["Shortcut"] = "1,2,3",
			});

			var property = new ShortcutkeyProperty("Shortcut", provider);

			Assert.Equal(new[] { 1, 2, 3 }, property.Value);
		}

		[Fact]
		public async Task ValueRoundTripsThroughProvider()
		{
			var provider = await CreateProviderAsync();
			var written = new ShortcutkeyProperty("Shortcut", provider)
			{
				Value = new[] { 1, 2, 3 },
			};

			var read = new ShortcutkeyProperty("Shortcut", provider);

			Assert.Equal(written.Value, read.Value);
		}

		[Fact]
		public async Task SerializationUsesInvariantCulture()
		{
			var originalCulture = CultureInfo.CurrentCulture;
			var originalUiCulture = CultureInfo.CurrentUICulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
				CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
				var provider = await CreateProviderAsync();
				var property = new ShortcutkeyProperty("Shortcut", provider)
				{
					Value = new[] { -1, 2345 },
				};

				Assert.True(provider.TryGetValue("Shortcut", out string serialized));
				Assert.Equal("-1,2345", serialized);
			}
			finally
			{
				CultureInfo.CurrentCulture = originalCulture;
				CultureInfo.CurrentUICulture = originalUiCulture;
			}
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
