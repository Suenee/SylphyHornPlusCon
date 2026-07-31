using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	internal static class IntegrationTestExecutionEnvironment
	{
		internal const string TraitName = "ExecutionEnvironment";
		internal const string HostedCI = "HostedCI";
		internal const string InteractiveDesktop = "InteractiveDesktop";
		internal const string PhysicalInput = "PhysicalInput";
	}

	public sealed class IntegrationTestExecutionEnvironmentTests
	{
		[Fact]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public void EveryIntegrationTestHasExactlyOneExecutionEnvironment()
		{
			var allowedValues = new[]
			{
				IntegrationTestExecutionEnvironment.HostedCI,
				IntegrationTestExecutionEnvironment.InteractiveDesktop,
				IntegrationTestExecutionEnvironment.PhysicalInput,
			};
			var testMethods = typeof(IntegrationTestExecutionEnvironmentTests)
				.Assembly
				.GetTypes()
				.SelectMany(type => type.GetMethods(
					BindingFlags.Instance |
					BindingFlags.Public |
					BindingFlags.NonPublic |
					BindingFlags.Static))
				.Where(method => method.GetCustomAttributes<FactAttribute>(true).Any())
				.ToArray();

			Assert.NotEmpty(testMethods);
			foreach (var method in testMethods)
			{
				var environmentTraits = method
					.GetCustomAttributes<TraitAttribute>(true)
					.Where(attribute => string.Equals(
						attribute.Name,
						IntegrationTestExecutionEnvironment.TraitName,
						StringComparison.Ordinal))
					.ToArray();

				var testName = $"{method.DeclaringType?.FullName}.{method.Name}";
				Assert.True(
					environmentTraits.Length == 1,
					$"{testName} must declare exactly one {IntegrationTestExecutionEnvironment.TraitName} trait.");
				Assert.Contains(environmentTraits[0].Value, allowedValues);
			}
		}
	}
}
