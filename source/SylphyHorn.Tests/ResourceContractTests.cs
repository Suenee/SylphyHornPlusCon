using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using SylphyHorn.Properties;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class ResourceContractTests
	{
		[Fact]
		public void InvariantAndJapaneseResourcesHaveTheSameNonEmptyKeys()
		{
			var invariant = ReadResources(CultureInfo.InvariantCulture);
			var japanese = ReadResources(CultureInfo.GetCultureInfo("ja"));

			Assert.NotEmpty(invariant);
			Assert.Equal(invariant.Keys.OrderBy(key => key), japanese.Keys.OrderBy(key => key));
			Assert.All(invariant, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
			Assert.All(japanese, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
		}

		[Fact]
		public void GeneratedResourcePropertiesMatchTheResourceSchema()
		{
			var resourceKeys = ReadResources(CultureInfo.InvariantCulture).Keys.OrderBy(key => key).ToArray();
			var generatedPropertyNames = typeof(Resources)
				.GetProperties(BindingFlags.Public | BindingFlags.Static)
				.Where(property => property.PropertyType == typeof(string))
				.Select(property => property.Name)
				.OrderBy(name => name)
				.ToArray();

			Assert.Equal(resourceKeys, generatedPropertyNames);
		}

		[Fact]
		public void LocalizedResourcesPreserveCompositeFormatArguments()
		{
			var invariant = ReadResources(CultureInfo.InvariantCulture);
			var japanese = ReadResources(CultureInfo.GetCultureInfo("ja"));

			foreach (var pair in invariant)
			{
				AssertSameFormatArguments(pair.Value, japanese[pair.Key]);
			}
		}

		[Theory]
		[InlineData("{{0}}", new int[0])]
		[InlineData("{{{0}}}", new[] { 0 })]
		[InlineData("{0}}}", new[] { 0 })]
		[InlineData("{0,10}", new[] { 0 })]
		[InlineData("{0,-10}", new[] { 0 })]
		[InlineData("{1,-10:N2}", new[] { 1 })]
		[InlineData("{1} {0} {1}", new[] { 0, 1 })]
		public void CompositeFormatParserRecognizesEscapesAlignmentAndDistinctArguments(string value, int[] expected)
		{
			Assert.Equal(expected, ParseCompositeFormatArgumentIndexes(value));
		}

		[Theory]
		[InlineData("{0,abc}")]
		[InlineData("{0,+10}")]
		[InlineData("{0,١}")]
		[InlineData("{0,１}")]
		[InlineData("{0")]
		[InlineData("}")]
		[InlineData("{{{")]
		public void CompositeFormatParserRejectsInvalidSyntax(string value)
		{
			Assert.Throws<FormatException>(() => ParseCompositeFormatArgumentIndexes(value));
		}

		[Fact]
		public void LocalizedFormatComparisonAllowsReorderingAndDifferentRepetitionCounts()
		{
			AssertSameFormatArguments("{0}: {1}: {0}", "{1}: {0}");
		}

		[Theory]
		[InlineData("{0}", "text")]
		[InlineData("{0}", "{0} {1}")]
		[InlineData("{{{0}}}", "{{0}}")]
		public void LocalizedFormatComparisonRejectsMissingOrAdditionalArguments(string invariant, string localized)
		{
			Assert.ThrowsAny<Exception>(() => AssertSameFormatArguments(invariant, localized));
		}

		private static Dictionary<string, string> ReadResources(CultureInfo culture)
		{
			var resourceSet = Resources.ResourceManager.GetResourceSet(culture, true, false);
			Assert.NotNull(resourceSet);

			return resourceSet.Cast<DictionaryEntry>().ToDictionary(
				entry => Assert.IsType<string>(entry.Key),
				entry => Assert.IsType<string>(entry.Value),
				StringComparer.Ordinal);
		}

		private static void AssertSameFormatArguments(string invariant, string localized)
		{
			Assert.Equal(
				ParseCompositeFormatArgumentIndexes(invariant),
				ParseCompositeFormatArgumentIndexes(localized));
		}

		private static int[] ParseCompositeFormatArgumentIndexes(string value)
		{
			var indexes = new List<int>();
			for (var position = 0; position < value.Length;)
			{
				if (value[position] == '{')
				{
					if (position + 1 < value.Length && value[position + 1] == '{')
					{
						position += 2;
						continue;
					}

					position++;
					indexes.Add(ParseFormatItem(value, ref position));
					continue;
				}

				if (value[position] == '}')
				{
					if (position + 1 >= value.Length || value[position + 1] != '}')
					{
						throw new FormatException("A closing brace must be escaped.");
					}

					position += 2;
					continue;
				}

				position++;
			}

			return indexes.Distinct()
				.OrderBy(index => index)
				.ToArray();
		}

		private static int ParseFormatItem(string value, ref int position)
		{
			var indexStart = position;
			while (position < value.Length && IsAsciiDigit(value[position])) position++;
			if (position == indexStart) throw new FormatException("A format item requires a numeric index.");

			var indexText = value.Substring(indexStart, position - indexStart);
			if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
			{
				throw new FormatException("The format item index is invalid.");
			}

			SkipSpaces(value, ref position);
			if (position < value.Length && value[position] == ',')
			{
				position++;
				SkipSpaces(value, ref position);
				if (position < value.Length && value[position] == '-') position++;

				var alignmentStart = position;
				while (position < value.Length && IsAsciiDigit(value[position])) position++;
				if (position == alignmentStart) throw new FormatException("A format alignment must be an integer.");
				SkipSpaces(value, ref position);
			}

			if (position < value.Length && value[position] == ':')
			{
				position++;
				while (position < value.Length && value[position] != '}')
				{
					if (value[position] == '{') throw new FormatException("An opening brace is not valid inside a format string.");
					position++;
				}
			}

			if (position >= value.Length || value[position] != '}')
			{
				throw new FormatException("The format item is missing its closing brace.");
			}

			position++;
			return index;
		}

		private static void SkipSpaces(string value, ref int position)
		{
			while (position < value.Length && value[position] == ' ') position++;
		}

		private static bool IsAsciiDigit(char value)
		{
			return '0' <= value && value <= '9';
		}
	}
}