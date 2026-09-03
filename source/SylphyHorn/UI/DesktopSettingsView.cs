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
		private readonly WrapPanel _desktopStrip;
		private readonly TextBlock _hint;
		private readonly ISettingsDialogService _dialogs = new SettingsDialogService();
		private readonly WallpaperPathToImageSourceConverter _wallpaperConverter = new WallpaperPathToImageSourceConverter();
		private SettingsWindowViewModel _viewModel;
		private Point _dragStart;
		private VirtualDesktopViewModel _dragSource;
		private Border _dragCard;
		private Border _dragTargetCard;
		private TranslateTransform _dragTransform;
		private int _dragTargetIndex = -1;
		private bool _dragMoved;
		private bool _disposed;

		internal DesktopSettingsView()
		{
			var root = new Grid { Margin = new Thickness(18, 14, 18, 18) };
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			this._hint = new TextBlock
			{
				Foreground = new SolidColorBrush(Color.FromRgb(183, 190, 200)), FontSize = 12, TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 2, 0, 12),
			};
			Grid.SetRow(this._hint, 0); root.Children.Add(this._hint);

			this._desktopStrip = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
			var scroll = new ScrollViewer
			{
				Content = this._desktopStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Padding = new Thickness(0, 0, 0, 8),
			};
			Grid.SetRow(scroll, 1); root.Children.Add(scroll);

			var options = this.CreateGlobalOptions();
			Grid.SetRow(options, 2); root.Children.Add(options);

			this.Content = root;
			this.DataContextChanged += this.OnDataContextChanged;
			this.Loaded += this.OnLoaded;
		}

		private FrameworkElement CreateGlobalOptions()
		{
			var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
			panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(63, 69, 79)), Margin = new Thickness(0, 0, 0, 10) });
			var row = new StackPanel { Orientation = Orientation.Horizontal };
			var restore = new CheckBox
			{
				Content = "Restore saved desktop configuration on startup", Foreground = Brushes.White,
				IsChecked = Settings.General.OverrideDesktopsOnStartup.Value, Margin = new Thickness(0, 0, 28, 0),
				ToolTip = "Restore the saved SylphyHorn desktop configuration when SHPC starts.",
			};
			restore.Click += (_, _) => Settings.General.OverrideDesktopsOnStartup.Value = restore.IsChecked == true;
			row.Children.Add(restore);

			var wallpaper = new CheckBox
			{
				Content = "Manage individual desktop wallpapers", Foreground = Brushes.White,
				IsChecked = Settings.General.ChangeBackgroundEachDesktop.Value,
				ToolTip = "Let SHPC manage per-desktop wallpapers and preserve the original Windows wallpaper for restoration.",
			};
			wallpaper.Click += (_, _) =>
			{
				WallpaperService.Instance.SetManagementEnabled(wallpaper.IsChecked == true);
				this.RebuildDesktopStrip();
			};
			row.Children.Add(wallpaper);
			panel.Children.Add(row);
			return panel;
		}

		public void Dispose()
		{
			if (this._disposed) return; this._disposed = true;
			this.Loaded -= this.OnLoaded; this.DataContextChanged -= this.OnDataContextChanged; this.DetachViewModel();
		}
		private void OnLoaded(object sender, RoutedEventArgs e) => this.AttachViewModel(this.DataContext as SettingsWindowViewModel);
		private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => this.AttachViewModel(e.NewValue as SettingsWindowViewModel);
		private void AttachViewModel(SettingsWindowViewModel viewModel)
		{
			if (ReferenceEquals(this._viewModel, viewModel)) return;
			this.DetachViewModel(); this._viewModel = viewModel;
			if (this._viewModel != null) this._viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
			this.UpdateHint();
			this.RebuildDesktopStrip();
		}
		private void DetachViewModel() { if (this._viewModel != null) this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged; this._viewModel = null; }
		private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{ if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsWindowViewModel.Desktops)) this.RebuildDesktopStrip(); }

		private void UpdateHint()
		{
			this._hint.Text = this._viewModel?.IsReorderingSupport == true
				? "Drag a desktop preview to reorder it. Right-click a preview or use its menu button for desktop actions."
				: "Right-click a preview or use its menu button for desktop actions. Desktop reordering is not supported by this Windows build.";
		}

		private void RebuildDesktopStrip()
		{
			this.CancelDrag();
			this._desktopStrip.Children.Clear(); if (this._viewModel == null) return;
			foreach (var desktop in this._viewModel.Desktops ?? Array.Empty<VirtualDesktopViewModel>()) this._desktopStrip.Children.Add(this.CreateDesktopCard(desktop));
			this._desktopStrip.Children.Add(this.CreateNewDesktopTile());
		}

		private FrameworkElement CreateDesktopCard(VirtualDesktopViewModel desktop)
		{
			var card = new Border
			{
				Width = 224, Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)), BorderBrush = new SolidColorBrush(Color.FromRgb(63, 69, 79)),
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9), Margin = new Thickness(0, 0, 12, 12),
				VerticalAlignment = VerticalAlignment.Top, DataContext = desktop, Tag = desktop,
			};
			var stack = new StackPanel(); card.Child = stack;
			var preview = this.CreatePreview(desktop);
			stack.Children.Add(preview);
			stack.Children.Add(this.CreateFieldLabel("Title", new Thickness(1, 10, 0, 3)));
			stack.Children.Add(this.CreateTextBox(nameof(VirtualDesktopViewModel.Title), "Display title used by Windows and SylphyHorn. If empty, it is derived from Name."));
			stack.Children.Add(this.CreateFieldLabel("Name", new Thickness(1, 8, 0, 3)));
			stack.Children.Add(this.CreateTextBox(nameof(VirtualDesktopViewModel.CanonicalName), "Unique canonical name. Allowed: a-z, 0-9, hyphen and underscore. Comparison is case-insensitive."));
			this.WireDesktopDrag(card, preview, desktop);
			return card;
		}

		private FrameworkElement CreatePreview(VirtualDesktopViewModel desktop)
		{
			var preview = new Border
			{
				Width = 204, Height = 115, Background = new SolidColorBrush(Color.FromRgb(18, 21, 25)), BorderBrush = new SolidColorBrush(Color.FromRgb(79, 85, 95)),
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), ClipToBounds = true,
				Cursor = this._viewModel?.IsReorderingSupport == true ? Cursors.SizeAll : Cursors.Arrow,
				ToolTip = this._viewModel?.IsReorderingSupport == true ? "Drag to reorder. Right-click for desktop actions." : "Right-click for desktop actions.",
			};
			var grid = new Grid();
			var image = new Image { Stretch = Stretch.UniformToFill, SnapsToDevicePixels = true };
			image.SetBinding(Image.SourceProperty, new Binding(nameof(VirtualDesktopViewModel.WallpaperPathOrDefault)) { Source = desktop, Mode = BindingMode.OneWay, Converter = this._wallpaperConverter });
			grid.Children.Add(image);
			var badge = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(220, 18, 21, 25)), BorderBrush = new SolidColorBrush(Color.FromRgb(91, 98, 108)),
				BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(7, 3, 7, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
			};
			var number = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13 };
			number.SetBinding(TextBlock.TextProperty, new Binding(nameof(VirtualDesktopViewModel.NumberText)) { Source = desktop }); badge.Child = number; grid.Children.Add(badge);

			var menu = this.CreateDesktopContextMenu(desktop);
			var menuButton = new Button
			{
				Content = "⋮", Width = 28, Height = 30, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
				Background = new SolidColorBrush(Color.FromArgb(210, 18, 21, 25)), BorderThickness = new Thickness(0),
				HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(0), Cursor = Cursors.Hand,
				ToolTip = "Desktop menu",
			};
			menuButton.Click += (_, _) => { menu.PlacementTarget = menuButton; menu.IsOpen = true; };
			grid.Children.Add(menuButton);
			preview.Child = grid; preview.ContextMenu = menu;
			return preview;
		}

		private void WireDesktopDrag(Border card, FrameworkElement preview, VirtualDesktopViewModel desktop)
		{
			preview.PreviewMouseLeftButtonDown += (_, e) =>
			{
				if (this._viewModel?.IsReorderingSupport != true || FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
				this.CancelDrag();
				this._dragSource = desktop;
				this._dragCard = card;
				this._dragStart = e.GetPosition(this._desktopStrip);
				this._dragTransform = new TranslateTransform();
				this._dragCard.RenderTransform = this._dragTransform;
				this._dragCard.Opacity = 0.92;
				Panel.SetZIndex(this._dragCard, 1000);
				preview.CaptureMouse();
				e.Handled = true;
			};

			preview.PreviewMouseMove += (_, e) =>
			{
				if (this._dragSource == null || this._dragCard != card || e.LeftButton != MouseButtonState.Pressed) return;
				var point = e.GetPosition(this._desktopStrip);
				var dx = point.X - this._dragStart.X;
				var dy = point.Y - this._dragStart.Y;
				if (!this._dragMoved && Math.Abs(dx) + Math.Abs(dy) <= 3) return;
				this._dragMoved = true;
				this._dragTransform.X = dx;
				this._dragTransform.Y = dy;
				this.UpdateDragTarget(point);
				e.Handled = true;
			};

			preview.PreviewMouseLeftButtonUp += (_, e) =>
			{
				if (this._dragSource == null || this._dragCard != card) return;
				var source = this._dragSource;
				var target = this._dragTargetIndex;
				var moved = this._dragMoved;
				if (preview.IsMouseCaptured) preview.ReleaseMouseCapture();
				this.CancelDrag();
				if (moved && target >= 0 && target != source.Index) this.MoveDesktop(source, target);
				e.Handled = true;
			};

			preview.LostMouseCapture += (_, _) =>
			{
				if (this._dragCard == card) this.CancelDrag();
			};
		}

		private void UpdateDragTarget(Point pointer)
		{
			Border nearest = null;
			var nearestDistance = double.MaxValue;
			var targetIndex = -1;
			foreach (UIElement child in this._desktopStrip.Children)
			{
				if (!(child is Border candidate) || !(candidate.Tag is VirtualDesktopViewModel candidateDesktop) || candidateDesktop.Id == this._dragSource.Id) continue;
				var center = candidate.TranslatePoint(new Point(candidate.ActualWidth / 2, candidate.ActualHeight / 2), this._desktopStrip);
				var dx = pointer.X - center.X;
				var dy = pointer.Y - center.Y;
				var distance = dx * dx + dy * dy;
				if (distance >= nearestDistance) continue;
				nearestDistance = distance;
				nearest = candidate;
				targetIndex = candidateDesktop.Index;
			}
			if (!ReferenceEquals(this._dragTargetCard, nearest))
			{
				if (this._dragTargetCard != null) this._dragTargetCard.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 69, 79));
				this._dragTargetCard = nearest;
				if (this._dragTargetCard != null) this._dragTargetCard.BorderBrush = new SolidColorBrush(Color.FromRgb(92, 169, 255));
			}
			this._dragTargetIndex = targetIndex;
		}

		private void CancelDrag()
		{
			if (this._dragTargetCard != null) this._dragTargetCard.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 69, 79));
			if (this._dragCard != null)
			{
				this._dragCard.RenderTransform = Transform.Identity;
				this._dragCard.Opacity = 1;
				Panel.SetZIndex(this._dragCard, 0);
			}
			this._dragTargetCard = null;
			this._dragCard = null;
			this._dragSource = null;
			this._dragTransform = null;
			this._dragTargetIndex = -1;
			this._dragMoved = false;
		}

		private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
		{
			while (source != null)
			{
				if (source is T match) return match;
				source = VisualTreeHelper.GetParent(source);
			}
			return null;
		}

		private ContextMenu CreateDesktopContextMenu(VirtualDesktopViewModel desktop)
		{
			var menu = new ContextMenu();
			menu.Opened += (_, _) =>
			{
				menu.Items.Clear();
				var change = new MenuItem { Header = "Change wallpaper..." }; change.Click += (_, _) => this.ChangeWallpaper(desktop); menu.Items.Add(change);
				if (Settings.General.ChangeBackgroundEachDesktop.Value && desktop.HasWallpaper)
				{
					var reset = new MenuItem { Header = "Reset..." }; reset.Click += (_, _) => this.ResetWallpaper(desktop); menu.Items.Add(reset);
				}
				var fit = new MenuItem { Header = "Fit" };
				foreach (var position in this._viewModel?.WallpaperPositions ?? Array.Empty<DisplayItem<WallpaperPosition>>())
				{
					var value = position.Value; var item = new MenuItem { Header = (position.Display ?? value.ToString()).Trim(), IsCheckable = true, IsChecked = desktop.WallpaperPosition == value };
					item.Click += (_, _) => desktop.WallpaperPosition = value; fit.Items.Add(item);
				}
				menu.Items.Add(fit); menu.Items.Add(new Separator());
				var remove = new MenuItem { Header = "Remove desktop...", IsEnabled = (this._viewModel?.Desktops?.Length ?? 0) > 1 };
				remove.Click += (_, _) => this.RemoveDesktop(desktop); menu.Items.Add(remove);
			};
			return menu;
		}

		private TextBlock CreateFieldLabel(string text, Thickness margin) => new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(183, 190, 200)), FontSize = 11, Margin = margin };
		private TextBox CreateTextBox(string path, string tooltip)
		{
			var box = new TextBox
			{
				Height = 30, Padding = new Thickness(7, 3, 7, 3), Background = new SolidColorBrush(Color.FromRgb(43, 48, 56)), Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(73, 80, 91)), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tooltip,
			};
			box.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }); return box;
		}

		private FrameworkElement CreateNewDesktopTile()
		{
			var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
			content.Children.Add(new TextBlock { Text = "+", FontSize = 52, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -6) });
			content.Children.Add(new TextBlock { Text = "New desktop", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
			var button = new Button
			{
				Content = content, Foreground = new SolidColorBrush(Color.FromRgb(222, 227, 234)), Background = Brushes.Transparent,
				BorderThickness = new Thickness(0), Padding = new Thickness(12), Cursor = Cursors.Hand,
				HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
			};
			button.Click += (_, _) => this._viewModel?.CreateDesktop();
			return new Border
			{
				Width = 224, Height = 249, Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)), BorderBrush = new SolidColorBrush(Color.FromRgb(63, 69, 79)),
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9), Margin = new Thickness(0, 0, 12, 12),
				VerticalAlignment = VerticalAlignment.Top, Child = button,
			};
		}

		private bool ConfirmUnmanagedWallpaperChange()
		{
			if (Settings.General.ChangeBackgroundEachDesktop.Value) return true;
			return this._dialogs.ShowOkCancelConfirmation(
				"Individual desktop wallpaper management is disabled.\n\nSHPC is not currently preserving the wallpaper state. Changing the wallpaper may affect other desktops, and the previous wallpaper may not be recoverable.\n\nDo you want to continue?",
				"Change wallpaper without SHPC management", MessageBoxImage.Warning);
		}

		private void ChangeWallpaper(VirtualDesktopViewModel desktop)
		{
			if (desktop == null || !this.ConfirmUnmanagedWallpaperChange()) return;
			var initialDirectory = Settings.General.DesktopBackgroundFolderPath.Value;
			if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
			{
				var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); initialDirectory = Directory.Exists(pictures) ? pictures : string.Empty;
			}
			var response = this._dialogs.ShowOpenFileDialog(SylphyHorn.Properties.Resources.Settings_Background_SelectionDialog, initialDirectory, WallpaperService.SupportedFormats, string.Empty);
			if (response == null || response.Length == 0 || string.IsNullOrWhiteSpace(response[0]) || !File.Exists(response[0])) return;
			var filePath = Path.GetFullPath(response[0]); var folder = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(folder)) Settings.General.DesktopBackgroundFolderPath.Value = folder;
			desktop.WallpaperPath = filePath;
			if (!Settings.General.ChangeBackgroundEachDesktop.Value) WallpaperService.Instance.ApplyDesktopWallpaperNow(filePath, desktop.WallpaperPosition);
			LoggingService.Instance.Write(LogLevel.Info, "SETTINGS", "WallpaperChanged", "Desktop wallpaper changed.", desktop.Id.ToString("D"), filePath);
		}

		private void ResetWallpaper(VirtualDesktopViewModel desktop)
		{
			if (desktop == null || !Settings.General.ChangeBackgroundEachDesktop.Value || !desktop.HasWallpaper) return;
			var original = WallpaperService.Instance.OriginalWallpaperPath;
			desktop.ResetWallpaperPath(original);
			if (Settings.General.OriginalWallpaperCaptured.Value) desktop.WallpaperPosition = WallpaperService.Instance.OriginalWallpaperPosition;
			LoggingService.Instance.Write(LogLevel.Info, "SETTINGS", "WallpaperReset", "Desktop wallpaper reset to the preserved Windows wallpaper.", desktop.Id.ToString("D"), original);
		}

		private void RemoveDesktop(VirtualDesktopViewModel desktop)
		{
			if (desktop == null || (this._viewModel?.Desktops?.Length ?? 0) <= 1) return;
			var title = string.IsNullOrWhiteSpace(desktop.Title) ? $"Desktop {desktop.NumberText}" : desktop.Title;
			var confirmed = this._dialogs.ShowOkCancelConfirmation($"Remove desktop \"{title}\"?\n\nWindows will move its open windows to another desktop.\nThis action cannot be undone.", "Remove desktop", MessageBoxImage.Warning);
			if (!confirmed) return;
			desktop.Close(); desktop.ForgetCanonicalName();
			LoggingService.Instance.Write(LogLevel.Info, "DESKTOP", "DesktopRemoveRequested", "Desktop removal requested from Settings.", desktop.Id.ToString("D"), title);
		}

		private void MoveDesktop(VirtualDesktopViewModel desktop, int targetIndex)
		{
			if (desktop == null || this._viewModel?.IsReorderingSupport != true) return;
			var sourceIndex = desktop.Index; if (sourceIndex == targetIndex) return;
			if (sourceIndex < targetIndex) for (var i = sourceIndex; i < targetIndex; i++) desktop.MoveToNext();
			else for (var i = sourceIndex; i > targetIndex; i--) desktop.MoveToPrevious();
			LoggingService.Instance.Write(LogLevel.Info, "DESKTOP", "DesktopReorderRequested", $"Desktop reorder requested: {sourceIndex + 1} -> {targetIndex + 1}.", desktop.Id.ToString("D"));
		}

		private sealed class WallpaperPathToImageSourceConverter : IValueConverter
		{
			public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			{
				var path = value as string; if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
				try
				{
					var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 408;
					bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
				}
				catch { return null; }
			}
			public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
		}
	}
}
