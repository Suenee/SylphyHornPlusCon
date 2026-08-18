using System;
using SylphyHorn.UI;
using Xunit;

namespace SylphyHorn.Tests
{
	public class SettingsDialogContractTests
	{
		[Fact]
		public void OpenFileDialogContractReturnsMultiplePaths()
		{
			var method = typeof(ISettingsDialogService).GetMethod(nameof(ISettingsDialogService.ShowOpenFileDialog));

			Assert.NotNull(method);
			Assert.Equal(typeof(string[]), method.ReturnType);
			Assert.Equal(new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, GetParameterTypes(method));
		}

		[Fact]
		public void SaveFileDialogContractReturnsSinglePath()
		{
			var method = typeof(ISettingsDialogService).GetMethod(nameof(ISettingsDialogService.ShowSaveFileDialog));

			Assert.NotNull(method);
			Assert.Equal(typeof(string), method.ReturnType);
			Assert.Equal(new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, GetParameterTypes(method));
		}

		[Fact]
		public void ConfirmationDialogContractReturnsBooleanDecision()
		{
			var method = typeof(ISettingsDialogService).GetMethod(nameof(ISettingsDialogService.ShowOkCancelConfirmation));

			Assert.NotNull(method);
			Assert.Equal(typeof(bool), method.ReturnType);
			Assert.Equal(new[] { typeof(string), typeof(string), typeof(System.Windows.MessageBoxImage) }, GetParameterTypes(method));
		}

		private static Type[] GetParameterTypes(System.Reflection.MethodInfo method)
		{
			var parameters = method.GetParameters();
			var types = new Type[parameters.Length];
			for (var index = 0; index < parameters.Length; index++)
			{
				types[index] = parameters[index].ParameterType;
			}

			return types;
		}
	}
}
