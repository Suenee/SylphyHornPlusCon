using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.UI.Bindings;

namespace SylphyHorn.UI
{
	internal sealed class DesktopSettingsView : UserControl, IDisposable
	{
		private readonly StackPanel _desktopStrip;
		private readonly ISettingsDialogService _dialogs = new SettingsDialogService();
		private readonly WallpaperPathToImageSourceConverter _wallpaperConverter = new WallpaperPathToImageSourceConverter();
		private SettingsWindowViewModel _viewModel;
		private Point _dragStart;
		private VirtualDesktopViewModel _dragSource;
		private bool _disposed;

		internal DesktopSettingsView()
		{
			var root = new Grid { Margin = new Thickness(18, 14, 18, 18) };
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

			var globalSettings = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
			var overrideDesktops = new CheckBox
			{
				Content = "Override virtual desktops with SylphyHorn settings on startup",
				Foreground = Brushes.White,
				Margin = new Thickness(0, 0, 0, 5),
			};
			overrideDesktops.SetBinding(CheckBox.IsCheckedProperty, new Binding("Value")
			{
				Source = Settings.General.OverrideDesktopsOnStartup,
				Mode = BindingMode.TwoWay,
			});
			globalSettings.Children.Add(overrideDesktops);

			var changeBackground = new CheckBox
			{
				Content = "Change the background for each desktop",
				Foreground = Brushes.White,
				Margin = new Thickness(0, 0, 0, 5),
			};
			changeBackground.SetBinding(CheckBox.IsCheckedProperty, new Binding("Value")
			{
				Source = Settings.General.ChangeBackgroundEachDesktop,
				Mode = BindingMode.TwoWay,
			});
			globalSettings.Children.Add(changeBackground);

			var supported = new TextBlock
			{
				Text = "Supported image formats: " + string.Join(", ", WallpaperService.SupportedFileTypes),
				Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 184)),
				FontSize = 12,
				Margin = new Thickness(0, 3, 0, 0),
			};
			globalSettings.Children.Add(supported);
			Grid.SetRow(globalSettings, 0);
			root.Children.Add(globalSettings);

			var hint = new TextBlock
			{
				Text = "Drag desktop previews to reorder them. Right-click a preview to change its wallpaper or remove the desktop.",
				Foreground = new SolidColorBrush(Color.FromRgb(183, 190, 200)),
				FontSize = 12,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 2, 0, 12),
			};
			Grid.SetRow(hint, 1);
			root.Children.Add(hint);

			this._desktopStrip = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Top,
			};
			var scroll = new ScrollViewer
			{
				Content = this._desktopStrip,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
				CanContentScroll = false,
				Padding = new Thickness(0, 0, 0, 8),
			};
			Grid.SetRow(scroll, 2);
			root.Children.Add(scroll);

			this.Content = root;
			this.DataContextChanged += this.OnDataContextChanged;
			this.Loaded += this.OnLoaded;
		}

		public void Dispose()
		{
			if (this._disposed) return;
			this._disposed = true;
			this.Loaded -= this.OnLoaded;
			this.DataContextChanged -= this.OnDataContextChanged;
			this.DetachViewModel();
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			this.AttachViewModel(this.DataContext as SettingsWindowViewModel);
		}

		private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			this.AttachViewModel(e.NewValue as SettingsWindowViewModel);
		}

		private void AttachViewModel(SettingsWindowViewModel viewModel)
		{
			if (ReferenceEquals(this._viewModel, viewModel)) return;
			this.DetachViewModel();
			this._viewModel = viewModel;
			if (this._viewModel != null) this._viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
			this.RebuildDesktopStrip();
		}

		private void DetachViewModel()
		{
			if (this._viewModel != null) this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
			this._viewModel = null;
		}

		private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsWindowViewModel.Desktops))
				this.RebuildDesktopStrip();
		}

		private void RebuildDesktopStrip()
		{
			this._desktopStrip.Children.Clear();
			if (this._viewModel == null) return;

			foreach (var desktop in this._viewModel.Desktops ?? Array.Empty<VirtualDesktopViewModel>())
				this._desktopStrip.Children.Add(this.CreateDesktopCard(desktop));

			this._desktopStrip.Children.Add(this.CreateNewDesktopTile());
		}

		private FrameworkElement CreateDesktopCard(VirtualDesktopViewModel desktop)
		{
			var card = new Border
			{
				Width = 224,
				Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(63, 69, 79)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(7),
				Padding = new Thickness(9),
				Margin = new Thickness(0, 0, 12, 0),
				VerticalAlignment = VerticalAlignment.Top,
				DataContext = desktop,
			};

			var stack = new StackPanel();
			card.Child = stack;

			var preview = this.CreatePreview(desktop);
			stack.Children.Add(preview);

			stack.Children.Add(this.CreateFieldLabel("Title", new Thickness(1, 10, 0, 3)));
			var title = this.CreateTextBox(nameof(VirtualDesktopViewModel.Name), "Display title used by Windows and SylphyHorn.");
			stack.Children.Add(title);

			stack.Children.Add(this.CreateFieldLabel("Name", new Thickness(1, 8, 0, 3)));
			var canonicalName = this.CreateTextBox(nameof(VirtualDesktopViewModel.CanonicalName), "Stable canonical desktop name for automation and future integrations.");
			stack.Children.Add(canonicalName);

			return card;
		}

		private FrameworkElement CreatePreview(VirtualDesktopViewModel desktop)
		{
			var preview = new Border
			{
				Width = 204,
				Height = 115,
				Background = new SolidColorBrush(Color.FromRgb(18, 21, 25)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(79, 85, 95)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(3),
				ClipToBounds = true,
				AllowDrop = true,
				Cursor = this._viewModel?.IsReorderingSupport == true ? Cursors.SizeAll : Cursors.Arrow,
				ToolTip = "Drag to reorder. Right-click for desktop actions.",
			};

			var grid = new Grid();
			var image = new Image
			{
				Stretch = Stretch.UniformToFill,
				SnapsToDevicePixels = true,
			};
			image.SetBinding(Image.SourceProperty, new Binding(nameof(VirtualDesktopViewModel.WallpaperPathOrDefault))
			{
				Source = desktop,
				Mode = BindingMode.OneWay,
				Converter = this._wallpaperConverter,
			});
			grid.Children.Add(image);

			var badge = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(220, 18, 21, 25)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(91, 98, 108)),
				BorderThickness = new Thickness(0, 0, 1, 1),
				Padding = new Thickness(7, 3, 7, 3),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
			};
			var number = new TextBlock
			{
				Foreground = Brushes.White,
				FontWeight = FontWeights.SemiBold,
				FontSize = 13,
			};
			number.SetBinding(TextBlock.TextProperty, new Binding(nameof(VirtualDesktopViewModel.NumberText)) { Source = desktop });
			badge.Child = number;
			grid.Children.Add(badge);

			preview.Child = grid;
			preview.ContextMenu = this.CreateDesktopContextMenu(desktop);
			preview.PreviewMouseLeftButtonDown += (_, e) =>
			{
				this._dragStart = e.GetPosition(preview);
				this._dragSource = desktop;
			};
			preview.MouseMove += (_, e) =>
			{
				if (this._viewModel?.IsReorderingSupport != true || e.LeftButton != MouseButtonState.Pressed || this._dragSource == null) return;
				var point = e.GetPosition(preview);
				if (Math.Abs(point.X - this._dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
					Math.Abs(point.Y - this._dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
				var source = this._dragSource;
				this._dragSource = null;
				DragDrop.DoDragDrop(preview, source, DragDropEffects.Move);
			};
			preview.DragOver += (_, e) =>
			{
				var source = e.Data.GetData(typeof(VirtualDesktopViewModel)) as VirtualDesktopViewModel;
				e.Effects = this._viewModel?.IsReorderingSupport == true && source != null && source.Id != desktop.Id
					? DragDropEffects.Move
					: DragDropEffects.None;
				e.Handled = true;
			};
			preview.Drop += (_, e) =>
			{
				var source = e.Data.GetData(typeof(VirtualDesktopViewModel)) as VirtualDesktopViewModel;
				if (source == null || source.Id == desktop.Id || this._viewModel?.IsReorderingSupport != true) return;
				this.MoveDesktop(source, desktop.Index);
				e.Handled = true;
			};

			return preview;
		}

		private ContextMenu CreateDesktopContextMenu(VirtualDesktopViewModel desktop)
		{
			var menu = new ContextMenu();
			menu.Opened += (_, _) =>
			{
				menu.Items.Clear();

				var changeWallpaper = new MenuItem { Header = "Change wallpaper..." };
				changeWallpaper.Click += (_, _) => this.ChangeWallpaper(desktop);
				menu.Items.Add(changeWallpaper);

				var fit = new MenuItem { Header = "Fit" };
				foreach (var position in this._viewModel?.WallpaperPositions ?? Array.Empty<DisplayItem<WallpaperPosition>>())
				{
					var value = position.Value;
					var fitItem = new MenuItem
					{
						Header = (position.Display ?? value.ToString()).Trim(),
						IsCheckable = true,
						IsChecked = desktop.WallpaperPosition == value,
					};
					fitItem.Click += (_, _) => desktop.WallpaperPosition = value;
					fit.Items.Add(fitItem);
				}
				menu.Items.Add(fit);
				menu.Items.Add(new Separator());

				var remove = new MenuItem
				{
					Header = "Remove desktop...",
					IsEnabled = (this._viewModel?.Desktops?.Length ?? 0) > 1,
				};
				remove.Click += (_, _) => this.RemoveDesktop(desktop);
				menu.Items.Add(remove);
			};
			return menu;
		}

		private TextBlock CreateFieldLabel(string text, Thickness margin)
		{
			return new TextBlock
			{
				Text = text,
				Foreground = new SolidColorBrush(Color.FromRgb(183, 190, 200)),
				FontSize = 11,
				Margin = margin,
			};
		}

		private TextBox CreateTextBox(string path, string tooltip)
		{
			var textBox = new TextBox
			{
				Height = 30,
				Padding = new Thickness(7, 3, 7, 3),
				Background = new SolidColorBrush(Color.FromRgb(43, 48, 56)),
				Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(73, 80, 91)),
				BorderThickness = new Thickness(1),
				VerticalContentAlignment = VerticalAlignment.Center,
				ToolTip = tooltip,
			};
			textBox.SetBinding(TextBox.TextProperty, new Binding(path)
			{
				Mode = BindingMode.TwoWay,
				UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
			});
			return textBox;
		}

		private FrameworkElement CreateNewDesktopTile()
		{
			var button = new Button
			{
				Width = 164,
				Height = 221,
				Content = "+\nNew desktop",
				FontSize = 15,
				Foreground = new SolidColorBrush(Color.FromRgb(222, 227, 234)),
				Background = new SolidColorBrush(Color.FromRgb(31, 36, 43)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(70, 77, 88)),
				BorderThickness = new Thickness(1),
				Margin = new Thickness(0, 0, 8, 0),
				Padding = new Thickness(12),
				VerticalContentAlignment = VerticalAlignment.Center,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				Cursor = Cursors.Hand,
			};
			button.Click += (_, _) => this._viewModel?.CreateDesktop();
			return button;
		}

		private void ChangeWallpaper(VirtualDesktopViewModel desktop)
		{
			if (desktop == null) return;
			var initialDirectory = Settings.General.DesktopBackgroundFolderPath.Value;
			if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
			{
				var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
				initialDirectory = Directory.Exists(pictures) ? pictures : string.Empty;
			}

			var response = this._dialogs.ShowOpenFileDialog(
				Resources.Settings_Background_SelectionDialog,
				initialDirectory,
				WallpaperService.SupportedFormats,
				string.Empty);
			if (response == null || response.Length == 0 || string.IsNullOrWhiteSpace(response[0]) || !File.Exists(response[0])) return;

			var filePath = Path.GetFullPath(response[0]);
			var folder = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(folder)) Settings.General.DesktopBackgroundFolderPath.Value = folder;
			if (!ProductInfo.IsWallpaperSupportBuild) Settings.General.ChangeBackgroundEachDesktop.Value = true;
			desktop.WallpaperPath = filePath;
			LoggingService.Instance.Write(LogLevel.Info, "SETTINGS", "WallpaperChanged", "Desktop wallpaper changed.", desktop.Id.ToString("D"), filePath);
		}

		private void RemoveDesktop(VirtualDesktopViewModel desktop)
		{
			if (desktop == null || (this._viewModel?.Desktops?.Length ?? 0) <= 1) return;
			var title = string.IsNullOrWhiteSpace(desktop.Name) ? $"Desktop {desktop.NumberText}" : desktop.Name;
			var confirmed = this._dialogs.ShowOkCancelConfirmation(
				$"Remove desktop \"{title}\"?\n\nWindows will move its open windows to another desktop.\nThis action cannot be undone.",
				"Remove desktop",
				MessageBoxImage.Warning);
			if (!confirmed) return;

			desktop.Close();
			desktop.ForgetCanonicalName();
			LoggingService.Instance.Write(LogLevel.Info, "DESKTOP", "DesktopRemoveRequested", "Desktop removal requested from Settings.", desktop.Id.ToString("D"), title);
		}

		private void MoveDesktop(VirtualDesktopViewModel desktop, int targetIndex)
		{
			if (desktop == null || this._viewModel?.IsReorderingSupport != true) return;
			var sourceIndex = desktop.Index;
			if (sourceIndex == targetIndex) return;

			if (sourceIndex < targetIndex)
			{
				for (var i = sourceIndex; i < targetIndex; i++) desktop.MoveToNext();
			}
			else
			{
				for (var i = sourceIndex; i > targetIndex; i--) desktop.MoveToPrevious();
			}
			LoggingService.Instance.Write(LogLevel.Info, "DESKTOP", "DesktopReorderRequested", $"Desktop reorder requested: {sourceIndex + 1} -> {targetIndex + 1}.", desktop.Id.ToString("D"));
		}

		private sealed class WallpaperPathToImageSourceConverter : IValueConverter
		{
			public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			{
				var path = value as string;
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
				try
				{
					var bitmap = new BitmapImage();
					bitmap.BeginInit();
					bitmap.CacheOption = BitmapCacheOption.OnLoad;
					bitmap.DecodePixelWidth = 408;
					bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
					bitmap.EndInit();
					bitmap.Freeze();
					return bitmap;
				}
				catch
				{
					return null;
				}
			}

			public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
				=> Binding.DoNothing;
		}
	}
}
