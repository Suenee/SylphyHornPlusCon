using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.UI.Bindings;

namespace SylphyHorn.UI
{
	internal sealed class DesktopSettingsView : UserControl, IDisposable
	{
		private static readonly Color NormalCardBorderColor = Color.FromRgb(63, 69, 79);
		private static readonly Color ActiveCardBorderColor = Color.FromRgb(52, 150, 255);
		private static readonly Color DragTargetBorderColor = Color.FromRgb(92, 169, 255);
		private readonly WrapPanel _desktopStrip;
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
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			this._desktopStrip = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
			var scroll = new ScrollViewer
			{
				Content = this._desktopStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Padding = new Thickness(0, 0, 0, 8),
			};
			Grid.SetRow(scroll, 0); root.Children.Add(scroll);
			this.Content = root;
			this.DataContextChanged += this.OnDataContextChanged;
			this.Loaded += this.OnLoaded;
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
			this.RebuildDesktopStrip();
		}
		private void DetachViewModel() { if (this._viewModel != null) this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged; this._viewModel = null; }
		private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsWindowViewModel.Desktops)) this.RebuildDesktopStrip();
			else if (e.PropertyName == nameof(SettingsWindowViewModel.CurrentDesktop)) this.RefreshActiveDesktopStyles();
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
				Width = 224, Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)), BorderBrush = new SolidColorBrush(NormalCardBorderColor),
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
			this.ApplyDesktopCardStyle(card, desktop);
			return card;
		}

		private void RefreshActiveDesktopStyles()
		{
			foreach (UIElement child in this._desktopStrip.Children)
			{
				if (child is Border card && card.Tag is VirtualDesktopViewModel desktop && !ReferenceEquals(card, this._dragTargetCard)) this.ApplyDesktopCardStyle(card, desktop);
			}
		}

		private void ApplyDesktopCardStyle(Border card, VirtualDesktopViewModel desktop)
		{
			if (card == null || desktop == null) return;
			var active = this._viewModel?.CurrentDesktop?.Id == desktop.Id;
			card.BorderBrush = new SolidColorBrush(active ? ActiveCardBorderColor : NormalCardBorderColor);
			card.BorderThickness = active ? new Thickness(2) : new Thickness(1);
			card.Effect = active
				? new DropShadowEffect
				{
					Color = ActiveCardBorderColor,
					BlurRadius = 14,
					ShadowDepth = 0,
					Opacity = 0.6,
					RenderingBias = RenderingBias.Performance,
				}
				: null;
		}

		private FrameworkElement CreatePreview(VirtualDesktopViewModel desktop)
		{
			var preview = new Border
			{
				Width = 204, Height = 115, Background = new SolidColorBrush(Color.FromRgb(18, 21, 25)), BorderBrush = new SolidColorBrush(Color.FromRgb(79, 85, 95)),
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), ClipToBounds = true,
				Cursor = Cursors.SizeAll, ToolTip = "Drag to move this desktop. Right-click for desktop actions.",
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
				HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Desktop menu",
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
				if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
				this.CancelDrag(); this._dragSource = desktop; this._dragCard = card; this._dragStart = e.GetPosition(this._desktopStrip);
				this._dragTransform = new TranslateTransform(); this._dragCard.RenderTransform = this._dragTransform; this._dragCard.Opacity = 0.92;
				Panel.SetZIndex(this._dragCard, 1000); preview.CaptureMouse(); e.Handled = true;
			};
			preview.PreviewMouseMove += (_, e) =>
			{
				if (this._dragSource == null || this._dragCard != card || e.LeftButton != MouseButtonState.Pressed) return;
				var point = e.GetPosition(this._desktopStrip); var dx = point.X - this._dragStart.X; var dy = point.Y - this._dragStart.Y;
				if (!this._dragMoved && Math.Abs(dx) + Math.Abs(dy) <= 3) return;
				this._dragMoved = true; this._dragTransform.X = dx; this._dragTransform.Y = dy; this.UpdateDragTarget(point); e.Handled = true;
			};
			preview.PreviewMouseLeftButtonUp += (_, e) =>
			{
				if (this._dragSource == null || this._dragCard != card) return;
				var source = this._dragSource; var target = this._dragTargetIndex; var moved = this._dragMoved;
				if (preview.IsMouseCaptured) preview.ReleaseMouseCapture(); this.CancelDrag();
				if (moved && target >= 0 && target != source.Index) this.MoveDesktop(source, target); e.Handled = true;
			};
			preview.LostMouseCapture += (_, _) => { if (this._dragCard == card) this.CancelDrag(); };
		}

		private void UpdateDragTarget(Point pointer)
		{
			Border nearest = null; var nearestDistance = double.MaxValue; var targetIndex = -1;
			foreach (UIElement child in this._desktopStrip.Children)
			{
				if (!(child is Border candidate) || !(candidate.Tag is VirtualDesktopViewModel candidateDesktop) || candidateDesktop.Id == this._dragSource.Id) continue;
				var center = candidate.TranslatePoint(new Point(candidate.ActualWidth / 2, candidate.ActualHeight / 2), this._desktopStrip);
				var dx = pointer.X - center.X; var dy = pointer.Y - center.Y; var distance = dx * dx + dy * dy;
				if (distance >= nearestDistance) continue; nearestDistance = distance; nearest = candidate; targetIndex = candidateDesktop.Index;
			}
			if (!ReferenceEquals(this._dragTargetCard, nearest))
			{
				if (this._dragTargetCard?.Tag is VirtualDesktopViewModel previousDesktop) this.ApplyDesktopCardStyle(this._dragTargetCard, previousDesktop);
				this._dragTargetCard = nearest;
				if (this._dragTargetCard != null)
				{
					this._dragTargetCard.BorderBrush = new SolidColorBrush(DragTargetBorderColor);
					this._dragTargetCard.BorderThickness = new Thickness(2);
				}
			}
			this._dragTargetIndex = targetIndex;
		}

		private void CancelDrag()
		{
			if (this._dragTargetCard?.Tag is VirtualDesktopViewModel targetDesktop) this.ApplyDesktopCardStyle(this._dragTargetCard, targetDesktop);
			if (this._dragCard != null)
			{
				this._dragCard.RenderTransform = Transform.Identity; this._dragCard.Opacity = 1; Panel.SetZIndex(this._dragCard, 0);
				if (this._dragCard.Tag is VirtualDesktopViewModel dragDesktop) this.ApplyDesktopCardStyle(this._dragCard, dragDesktop);
			}
			this._dragTargetCard = null; this._dragCard = null; this._dragSource = null; this._dragTransform = null; this._dragTargetIndex = -1; this._dragMoved = false;
		}

		private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
		{
			while (source != null) { if (source is T match) return match; source = VisualTreeHelper.GetParent(source); }
			return null;
		}

		private ContextMenu CreateDesktopContextMenu(VirtualDesktopViewModel desktop)
		{
			var menu = new ContextMenu();
			menu.Opened += (_, _) =>
			{
				menu.Items.Clear();
				var change = new MenuItem { Header = "Change wallpaper..." }; change.Click += (_, _) => this.ChangeWallpaper(desktop); menu.Items.Add(change);
				var restore = new MenuItem { Header = "Restore...", IsEnabled = Settings.General.ChangeBackgroundEachDesktop.Value && desktop.HasWallpaper, ToolTip = "Restore the preserved Windows wallpaper for this desktop." };
				restore.Click += (_, _) => this.RestoreWallpaper(desktop); menu.Items.Add(restore);
				var fit = new MenuItem { Header = "Fit" };
				foreach (var position in this._viewModel?.WallpaperPositions ?? Array.Empty<DisplayItem<WallpaperPosition>>())
				{
					var value = position.Value; var item = new MenuItem { Header = (position.Display ?? value.ToString()).Trim(), IsCheckable = true, IsChecked = desktop.WallpaperPosition == value };
					item.Click += (_, _) => desktop.WallpaperPosition = value; fit.Items.Add(item);
				}
				menu.Items.Add(fit);
				var order = new MenuItem { Header = "Order", IsEnabled = (this._viewModel?.Desktops?.Length ?? 0) > 1 };
				var count = this._viewModel?.Desktops?.Length ?? 0;
				for (var index = 0; index < count; index++)
				{
					var targetIndex = index; var item = new MenuItem { Header = (index + 1).ToString(CultureInfo.InvariantCulture), IsCheckable = true, IsChecked = desktop.Index == index };
					item.Click += (_, _) => this.MoveDesktop(desktop, targetIndex); order.Items.Add(item);
				}
				menu.Items.Add(order); menu.Items.Add(new Separator());
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
				Width = 224, Height = 249, Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)), BorderBrush = new SolidColorBrush(NormalCardBorderColor),
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9), Margin = new Thickness(0, 0, 12, 12), VerticalAlignment = VerticalAlignment.Top, Child = button,
			};
		}

		private bool ConfirmUnmanagedWallpaperChange()
		{
			if (Settings.General.ChangeBackgroundEachDesktop.Value) return true;
			return this._dialogs.ShowOkCancelConfirmation("Individual desktop wallpaper management is disabled.\n\nSHPC is not currently preserving the wallpaper state. Changing the wallpaper may affect other desktops, and the previous wallpaper may not be recoverable.\n\nDo you want to continue?", "Change wallpaper without SHPC management", MessageBoxImage.Warning);
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

		private void RestoreWallpaper(VirtualDesktopViewModel desktop)
		{
			if (desktop == null || !Settings.General.ChangeBackgroundEachDesktop.Value || !desktop.HasWallpaper) return;
			var original = WallpaperService.Instance.OriginalWallpaperPath;
			desktop.ResetWallpaperPath(original);
			if (Settings.General.OriginalWallpaperCaptured.Value) desktop.WallpaperPosition = WallpaperService.Instance.OriginalWallpaperPosition;
			LoggingService.Instance.Write(LogLevel.Info, "SETTINGS", "WallpaperRestored", "Desktop wallpaper restored to the preserved Windows wallpaper.", desktop.Id.ToString("D"), original);
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
			var desktops = this._viewModel?.Desktops;
			if (desktop == null || desktops == null || desktop.Index == targetIndex) return;
			try { LogicalDesktopOrderService.Instance.Move(desktops, desktop.Index, targetIndex, this._viewModel.IsReorderingSupport); }
			catch (Exception ex)
			{
				LoggingService.Instance.Write(LogLevel.Error, "DESKTOP", "DesktopMoveFailed", "Desktop move failed.", desktop.Id.ToString("D"), ex.ToString());
				MessageBox.Show($"The desktop could not be moved.\n\n{ex.Message}", "Move desktop", MessageBoxButton.OK, MessageBoxImage.Error);
			}
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
