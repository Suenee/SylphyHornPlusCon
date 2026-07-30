using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class SettingsSerializationTests
	{
		[Fact]
		public async Task DataContractSettingsRoundTripPreservesKeysTypesAndValues()
		{
			var values = new Dictionary<string, object>
			{
				["Boolean"] = true,
				["Integer"] = 42,
				["Integers"] = new[] { 1, 2, 3 },
				["String"] = "value",
			};

			await AssertRoundTrip(values);
		}

		private static async Task AssertRoundTrip(IDictionary<string, object> values)
		{
			var directory = Path.Combine(
				Path.GetTempPath(),
				$"SylphyHornPlus-SettingsSerialization-{Guid.NewGuid():N}");
			var firstPath = Path.Combine(directory, "first.xml");
			var secondPath = Path.Combine(directory, "second.xml");

			try
			{
				var writer = new FileDictionaryProvider(firstPath);
				await writer.InitializeAsync();
				foreach (var pair in values)
				{
					SetValue(writer, pair);
				}
				await writer.SaveAsync();

				Assert.True(File.Exists(firstPath));
				Assert.True(new FileInfo(firstPath).Length > 0);

				var reader = new FileDictionaryProvider(firstPath);
				await reader.InitializeAsync();
				AssertValues(reader, values);

				await reader.ExportAsync(secondPath);
				Assert.True(File.Exists(secondPath));
				Assert.True(new FileInfo(secondPath).Length > 0);

				var secondReader = new FileDictionaryProvider(secondPath);
				await secondReader.InitializeAsync();
				AssertValues(secondReader, values);
			}
			finally
			{
				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, recursive: true);
				}
			}
		}

		private static void SetValue(FileDictionaryProvider provider, KeyValuePair<string, object> pair)
		{
			provider.SetValue<object>(pair.Key, pair.Value);
		}

		private static void AssertValues(
			FileDictionaryProvider provider,
			IDictionary<string, object> expected)
		{
			Assert.Equal(expected.Count, provider.LastReadValues.Count);
			foreach (var pair in expected)
			{
				Assert.True(provider.LastReadValues.TryGetValue(pair.Key, out var actual));
				if (pair.Value == null)
				{
					Assert.Null(actual);
					continue;
				}

				Assert.NotNull(actual);
				Assert.Equal(pair.Value.GetType(), actual.GetType());
				if (pair.Value is Array expectedArray)
				{
					Assert.True(
						StructuralComparisons.StructuralEqualityComparer.Equals(expectedArray, actual));
				}
				else
				{
					Assert.Equal(pair.Value, actual);
				}
			}
		}
	}
}
