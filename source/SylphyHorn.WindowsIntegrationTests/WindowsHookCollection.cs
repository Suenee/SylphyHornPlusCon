using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	[CollectionDefinition(Name, DisableParallelization = true)]
	public sealed class WindowsHookCollection
	{
		public const string Name = "Windows global hook integration";
	}
}
