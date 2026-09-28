using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SylphyHorn.Services;
using SylphyHorn.UI.Bindings;

namespace SylphyHorn.UI
{
	internal sealed class ImportExportSettingsView : UserControl
	{
		private readonly SettingsWindowViewModel _viewModel;
		private readonly TabControl _tabs = new() { Margin = new Thickness(10) };
		private readonly StackPanel _exportDesktops = new() { Margin = new Thickness(24, 4, 0, 8) };
		private readonly StackPanel _importDesktops = new() { Margin = new Thickness(24, 4, 0, 8) };
		private readonly CheckBox _exportDesktopSection = Box("Desktop settings and order", true);
		private readonly CheckBox _exportWallpapers = Box("Include wallpaper image files and wallpaper settings", true);
		private readonly CheckBox _exportWebSocket = Box("WebSocket connection", true);
		private readonly CheckBox _exportGeneral = Box("General Settings", true);
		private readonly CheckBox _importDesktopSection = Box("Desktop settings and order", true);
		private readonly CheckBox _importWallpapers = Box("Import wallpaper image files and wallpaper settings", true);
		private readonly CheckBox _importWebSocket = Box("WebSocket connection", true);
		private readonly CheckBox _importGeneral = Box("General Settings", true);
		private readonly TextBlock _status = new() { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
		private string _importPath;
		private PortablePackageManifest _importManifest;

		internal ImportExportSettingsView(SettingsWindowViewModel viewModel)
		{
			this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
			this.Content = this._tabs;
			this.Build();
		}

		private void Build()
		{
			var export = new StackPanel { Margin = new Thickness(18) };
			export.Children.Add(Note("Create one portable .shpc package. The package is reopened and fully validated before the export is reported as successful."));
			export.Children.Add(this._exportDesktopSection);
			export.Children.Add(this._exportDesktops);
			export.Children.Add(this._exportWallpapers);
			export.Children.Add(this._exportWebSocket);
			export.Children.Add(this._exportGeneral);
			this.PopulateExportDesktops();
			var exportButton = Button("Export package...");
			exportButton.Click += async (_, _) => await this.ExportAsync();
			export.Children.Add(exportButton);
			this._tabs.Items.Add(new TabItem { Header = "Export", Content = export });

			var import = new StackPanel { Margin = new Thickness(18) };
			import.Children.Add(Note("Import is all-or-nothing. SHPC validates the complete package first, creates an automatic backup, stages all requested changes, and rolls back if any step fails."));
			var chooseButton = Button("Choose package...");
			chooseButton.Click += async (_, _) => await this.ChooseImportAsync();
			import.Children.Add(chooseButton);
			import.Children.Add(this._importDesktopSection);
			import.Children.Add(this._importDesktops);
			import.Children.Add(this._importWallpapers);
			import.Children.Add(this._importWebSocket);
			import.Children.Add(this._importGeneral);
			var importButton = Button("Import selected sections");
			importButton.Click += async (_, _) => await this.ImportAsync();
			import.Children.Add(importButton);
			import.Children.Add(this._status);
			this._tabs.Items.Add(new TabItem { Header = "Import", Content = import });

			this._exportDesktopSection.Checked += (_, _) => this.UpdateDesktopSectionState(false);
			this._exportDesktopSection.Unchecked += (_, _) => this.UpdateDesktopSectionState(false);
			this._importDesktopSection.Checked += (_, _) => this.UpdateDesktopSectionState(true);
			this._importDesktopSection.Unchecked += (_, _) => this.UpdateDesktopSectionState(true);
			this.SetImportControls(false);
			this.UpdateDesktopSectionState(false);
		}

		private void UpdateDesktopSectionState(bool import)
		{
			var section = import ? this._importDesktopSection : this._exportDesktopSection;
			var panel = import ? this._importDesktops : this._exportDesktops;
			var wallpapers = import ? this._importWallpapers : this._exportWallpapers;
			var enabled = section.IsEnabled && section.IsChecked == true;
			panel.IsEnabled = enabled;
			wallpapers.IsEnabled = enabled && (!import || this._importManifest?.IncludesWallpapers == true);
		}

		private void PopulateExportDesktops()
		{
			this._exportDesktops.Children.Clear();
			var row = new StackPanel { Orientation = Orientation.Horizontal };
			var all = SmallButton("Select all"); all.Click += (_, _) => SetChecks(this._exportDesktops, true);
			var none = SmallButton("Unselect"); none.Click += (_, _) => SetChecks(this._exportDesktops, false);
			row.Children.Add(all); row.Children.Add(none); this._exportDesktops.Children.Add(row);
			foreach (var desktop in this._viewModel.Desktops.OrderBy(item => item.Index))
				this._exportDesktops.Children.Add(new CheckBox { Content = $"{desktop.Index + 1}. {desktop.Title}  [{desktop.CanonicalName}]", Tag = desktop.CanonicalName, IsChecked = true, Margin = new Thickness(0, 3, 0, 3) });
		}

		private async System.Threading.Tasks.Task ExportAsync()
		{
			try
			{
				this._status.Text = "Preparing export...";
				var dialog = new SaveFileDialog { Filter = PortableSettingsPackageService.Filter, DefaultExt = ".shpc", AddExtension = true, FileName = $"SHPC-{DateTime.Now:yyyyMMdd-HHmmss}.shpc" };
				if (dialog.ShowDialog() != true) return;
				var options = new PortableExportOptions { Desktops = this._exportDesktopSection.IsChecked == true, Wallpapers = this._exportWallpapers.IsChecked == true, WebSocket = this._exportWebSocket.IsChecked == true, General = this._exportGeneral.IsChecked == true };
				foreach (var item in this._exportDesktops.Children.OfType<CheckBox>().Where(item => item.IsChecked == true)) options.DesktopCNames.Add((string)item.Tag);
				await this._viewModel.ExportPortablePackageAsync(dialog.FileName, options);
				this._status.Text = $"Export completed and validated: {dialog.FileName}";
			}
			catch (Exception ex) { this._status.Text = "Export failed. No valid package was produced. " + ex.Message; }
		}

		private async System.Threading.Tasks.Task ChooseImportAsync()
		{
			try
			{
				var dialog = new OpenFileDialog { Filter = PortableSettingsPackageService.Filter, CheckFileExists = true, Multiselect = false };
				if (dialog.ShowDialog() != true) return;
				this._importManifest = await this._viewModel.InspectPortablePackageAsync(dialog.FileName);
				this._importPath = dialog.FileName;
				this._importDesktopSection.IsChecked = this._importManifest.IncludesDesktops;
				this._importDesktopSection.IsEnabled = this._importManifest.IncludesDesktops;
				this._importWallpapers.IsChecked = this._importManifest.IncludesWallpapers;
				this._importWallpapers.IsEnabled = this._importManifest.IncludesWallpapers;
				this._importWebSocket.IsChecked = this._importManifest.IncludesWebSocket;
				this._importWebSocket.IsEnabled = this._importManifest.IncludesWebSocket;
				this._importGeneral.IsChecked = this._importManifest.IncludesGeneral;
				this._importGeneral.IsEnabled = this._importManifest.IncludesGeneral;
				this._importDesktops.Children.Clear();
				if (this._importManifest.IncludesDesktops)
				{
					var row = new StackPanel { Orientation = Orientation.Horizontal };
					var all = SmallButton("Select all"); all.Click += (_, _) => SetChecks(this._importDesktops, true);
					var none = SmallButton("Select none"); none.Click += (_, _) => SetChecks(this._importDesktops, false);
					row.Children.Add(all); row.Children.Add(none); this._importDesktops.Children.Add(row);
					foreach (var desktop in this._importManifest.Desktops.OrderBy(item => item.Position))
						this._importDesktops.Children.Add(new CheckBox { Content = $"{desktop.Position}. {desktop.Title ?? desktop.CName}  [{desktop.CName}]", Tag = desktop.CName, IsChecked = true, Margin = new Thickness(0, 3, 0, 3) });
				}
				this.SetImportControls(true);
				this.UpdateDesktopSectionState(true);
				this._status.Text = $"Validated package: {Path.GetFileName(dialog.FileName)}";
			}
			catch (Exception ex) { this._importPath = null; this._importManifest = null; this.SetImportControls(false); this._status.Text = "Package validation failed. " + ex.Message; }
		}

		private async System.Threading.Tasks.Task ImportAsync()
		{
			if (string.IsNullOrWhiteSpace(this._importPath) || this._importManifest == null) return;
			try
			{
				this._status.Text = "Validating, backing up and staging import...";
				var options = new PortableImportOptions { Desktops = this._importDesktopSection.IsChecked == true, Wallpapers = this._importWallpapers.IsChecked == true, WebSocket = this._importWebSocket.IsChecked == true, General = this._importGeneral.IsChecked == true };
				foreach (var item in this._importDesktops.Children.OfType<CheckBox>().Where(item => item.IsChecked == true)) options.DesktopCNames.Add((string)item.Tag);
				await this._viewModel.ImportPortablePackageAsync(this._importPath, options);
				this.PopulateExportDesktops();
				this._status.Text = "Import completed successfully.";
			}
			catch (Exception ex) { this._status.Text = "Import failed. " + ex.Message; }
		}

		private void SetImportControls(bool enabled)
		{
			this._importDesktopSection.IsEnabled = enabled && this._importManifest?.IncludesDesktops == true;
			this._importWebSocket.IsEnabled = enabled && this._importManifest?.IncludesWebSocket == true;
			this._importGeneral.IsEnabled = enabled && this._importManifest?.IncludesGeneral == true;
			this.UpdateDesktopSectionState(true);
		}
		private static void SetChecks(StackPanel panel, bool value) { foreach (var item in panel.Children.OfType<CheckBox>()) item.IsChecked = value; }
		private static TextBlock Header(string text) => new() { Text = text, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) };
		private static TextBlock Note(string text) => new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(173, 180, 190)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
		private static CheckBox Box(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
		private static Button Button(string text) => new() { Content = text, Padding = new Thickness(14, 7, 14, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 4) };
		private static Button SmallButton(string text) => new() { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 5) };
	}
}