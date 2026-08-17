using System.Windows;
using Livet.Messaging;
using Livet.Messaging.IO;
using Xunit;

namespace SylphyHorn.Tests
{
	public class LivetDialogMessageCharacterizationTests
	{
		[Fact]
		public void OpenFileMessageDefaultsMatchLivet402()
		{
			var message = new OpeningFileSelectionMessage("open");

			Assert.Equal(string.Empty, message.Title);
			Assert.Equal(string.Empty, message.InitialDirectory);
			Assert.Equal(string.Empty, message.Filter);
			Assert.Equal(string.Empty, message.FileName);
			Assert.True(message.AddExtension);
			Assert.False(message.MultiSelect);
			Assert.Null(message.Response);
		}

		[Fact]
		public void SaveFileMessageDefaultsMatchLivet402()
		{
			var message = new SavingFileSelectionMessage("save");

			Assert.Equal(string.Empty, message.Title);
			Assert.Equal(string.Empty, message.InitialDirectory);
			Assert.Equal(string.Empty, message.Filter);
			Assert.Equal(string.Empty, message.FileName);
			Assert.True(message.AddExtension);
			Assert.False(message.CreatePrompt);
			Assert.True(message.OverwritePrompt);
			Assert.Null(message.Response);
		}

		[Fact]
		public void ConfirmationMessageDefaultsMatchLivet402()
		{
			var message = new ConfirmationMessage("", "", "confirm");

			Assert.Equal(string.Empty, message.Text);
			Assert.Equal(string.Empty, message.Caption);
			Assert.Equal(MessageBoxImage.None, message.Image);
			Assert.Equal(MessageBoxButton.OK, message.Button);
			Assert.Equal(MessageBoxResult.OK, message.DefaultResult);
			Assert.Null(message.Response);
		}
	}
}
