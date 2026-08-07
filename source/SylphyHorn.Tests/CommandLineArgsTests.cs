using System;
using Xunit;

namespace SylphyHorn.Tests
{
	public class CommandLineArgsTests
	{
		[Fact]
		public void DefaultsPreserveOriginalArgumentsAndExposeNoOptions()
		{
			var original = new string[0];

			var args = new CommandLineArgs(original);

			Assert.Equal(original, args.OriginalArgs);
			Assert.False(args.Setup);
			Assert.True(args.CanSettings);
			Assert.Null(args.Restarted);
			Assert.Empty(args.Options);
		}

		[Fact]
		public void ParsesBooleanFlagsExplicitBooleanAndNullableInteger()
		{
			var args = new CommandLineArgs(new[]
			{
				"-Setup",
				"-CanSettings=false",
				"-Restarted=3",
			});

			Assert.True(args.Setup);
			Assert.False(args.CanSettings);
			Assert.Equal(3, args.Restarted);
			Assert.Equal(3, args.Options.Length);
		}

		[Fact]
		public void OptionKeysAreCaseInsensitive()
		{
			var args = new CommandLineArgs(new[]
			{
				"-sEtUp",
				"-cAnSeTtInGs=FaLsE",
				"-rEsTaRtEd=4",
			});

			Assert.True(args.Setup);
			Assert.False(args.CanSettings);
			Assert.Equal(4, args.Restarted);
		}

		[Fact]
		public void CaseVariantDuplicatesUseLastArgument()
		{
			var args = new CommandLineArgs(new[]
			{
				"-CanSettings=false",
				"-CANSETTINGS=true",
				"-Restarted=1",
				"-RESTARTED=5",
			});

			Assert.True(args.CanSettings);
			Assert.Equal(5, args.Restarted);
			Assert.Equal(2, args.Options.Length);
		}

		[Fact]
		public void InvalidAndUnknownArgumentsAreIgnored()
		{
			var original = new[]
			{
				"",
				"-Unknown=value",
				"-Setup=not-a-boolean",
				"-CanSettings=not-a-boolean",
				"-Restarted=not-an-integer",
			};

			var args = new CommandLineArgs(original);

			Assert.Equal(original, args.OriginalArgs);
			Assert.False(args.Setup);
			Assert.True(args.CanSettings);
			Assert.Null(args.Restarted);
			Assert.Empty(args.Options);
		}

		[Fact]
		public void MissingNonBooleanValueIsIgnored()
		{
			var args = new CommandLineArgs(new[] { "-Restarted" });

			Assert.Null(args.Restarted);
			Assert.Empty(args.Options);
		}

		[Fact]
		public void OptionsExposeParsedMetadataAndValues()
		{
			var args = new CommandLineArgs(new[] { "-Setup", "-Restarted=7" });

			var setup = Assert.Single(args.Options, x => x.Key == nameof(CommandLineArgs.Setup));
			Assert.Equal(typeof(bool), setup.Type);
			Assert.Null(setup.ValueString);
			Assert.Equal(true, setup.Value);
			Assert.Equal("-", setup.KeyPrefix);
			Assert.Equal("=", setup.Separator);
			Assert.Equal("-Setup", setup.ToString());

			var restarted = Assert.Single(args.Options, x => x.Key == nameof(CommandLineArgs.Restarted));
			Assert.Equal(typeof(int), restarted.Type);
			Assert.Equal("7", restarted.ValueString);
			Assert.Equal(7, restarted.Value);
			Assert.Equal("-Restarted=7", restarted.ToString());
		}

		[Fact]
		public void GetKeyAndCreateOptionUseDeclaredOptionFormat()
		{
			var args = new CommandLineArgs();

			Assert.Equal("Restarted", args.GetKey(nameof(CommandLineArgs.Restarted)));

			var option = args.CreateOption(nameof(CommandLineArgs.Restarted), "8");
			Assert.Equal("Restarted", option.Key);
			Assert.Equal(typeof(int), option.Type);
			Assert.Equal("8", option.ValueString);
			Assert.Equal(8, option.Value);
			Assert.Null(option.ConvertException);
			Assert.Equal("-Restarted=8", option.ToString());
		}

		[Theory]
		[InlineData("-RESTARTED=not-an-integer")]
		[InlineData("-RESTARTED")]
		public void InvalidLastDuplicateDoesNotFallbackToEarlierValidValue(string lastArgument)
		{
			var args = new CommandLineArgs(new[] { "-Restarted=2", lastArgument });

			Assert.Null(args.Restarted);
			Assert.Empty(args.Options);
		}

		[Fact]
		public void OptionsContainOnlyTheLastSpecifiedValueForEachRecognizedProperty()
		{
			var args = new CommandLineArgs(new[]
			{
				"-Restarted=2",
				"-Unknown=3",
				"-Restarted=6",
			});

			var option = Assert.Single(args.Options);
			Assert.Equal("Restarted", option.Key);
			Assert.Equal("6", option.ValueString);
			Assert.Equal(6, option.Value);
		}
	}
}
