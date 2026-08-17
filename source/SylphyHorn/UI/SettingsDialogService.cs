using System.Windows;
using Microsoft.Win32;

namespace SylphyHorn.UI
{
	public interface ISettingsDialogService
	{
		string[] ShowOpenFileDialog(string title, string initialDirectory, string filter, string fileName);

		string ShowSaveFileDialog(string title, string initialDirectory, string filter, string fileName);

		bool ShowOkCancelConfirmation(string text, string caption, MessageBoxImage image);
	}

	public sealed class SettingsDialogService : ISettingsDialogService
	{
		public string[] ShowOpenFileDialog(string title, string initialDirectory, string filter, string fileName)
		{
			var dialog = new OpenFileDialog
			{
				FileName = fileName,
				InitialDirectory = initialDirectory,
				AddExtension = true,
				Filter = filter,
				Title = title,
				Multiselect = false,
			};

			return dialog.ShowDialog() == true ? dialog.FileNames : null;
		}

		public string ShowSaveFileDialog(string title, string initialDirectory, string filter, string fileName)
		{
			var dialog = new SaveFileDialog
			{
				FileName = fileName,
				InitialDirectory = initialDirectory,
				AddExtension = true,
				CreatePrompt = false,
				Filter = filter,
				OverwritePrompt = true,
				Title = title,
			};

			return dialog.ShowDialog() == true ? dialog.FileName : null;
		}

		public bool ShowOkCancelConfirmation(string text, string caption, MessageBoxImage image)
		{
			return MessageBox.Show(text, caption, MessageBoxButton.OKCancel, image, MessageBoxResult.OK) == MessageBoxResult.OK;
		}
	}
}
