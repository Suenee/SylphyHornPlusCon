using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SylphyHorn.UI.Bindings;
using WindowsDesktop;

namespace SylphyHorn.UI
{
	partial class SettingsWindow
	{
		private readonly Dictionary<Button, int[]> _navigationTargets = new();
		private readonly Dictionary<Button, string> _navigationDescriptions = new();
		private readonly Dictionary<Button, string[]> _navigationSubPages = new();
		private readonly List<Button> _primaryNavigationButtons = new();
		private TabControl _legacySettingsTabs;
		private StackPanel _secondaryNavigation;
		private TextBlock _pageTitle;
		private TextBlock _pageDescription;
		private Button _selectedPrimaryButton;
		private AppLogView _appLogView;

		public static SettingsWindow Instance { get; set; }

		public SettingsWindow()
		{
			this.InitializeComponent();
			this.BuildModernSettingsShell();
		}

		protected override void OnContentRendered(EventArgs e)
		{
			base.OnContentRendered(e);
			(this.DataContext as SettingsWindowViewModel)?.Initialize();
			this.Pin();
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			this._appLogView?.Dispose();
			(this.DataContext as IDisposable)?.Dispose();
		}

		private void BuildModernSettingsShell()
		{
			if (this.Content is not DockPanel root)
			{
				return;
			}

			this._legacySettingsTabs = root.Children.OfType<TabControl>().FirstOrDefault();
			if (this._legacySettingsTabs == null || this._legacySettingsTabs.Items.Count < 14)
			{
				return;
			}

			this.Width = 1180;
			this.Height = 760;
			this.MinWidth = 980;
			this.MinHeight = 640;
			this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

			this._appLogView = new AppLogView();
			if (this._legacySettingsTabs.Items[12] is TabItem logTab)
			{
				logTab.Content = this._appLogView;
			}

			root.Children.Remove(this._legacySettingsTabs);
			this.HideLegacyTabHeaders();

			var shell = new Grid
			{
				Background = new SolidColorBrush(Color.FromRgb(30, 34, 40)),
			};
			shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
			shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
			shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var navigation = this.CreateNavigationPanel();
			Grid.SetColumn(navigation, 0);
			shell.Children.Add(navigation);

			var divider = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(55, 60, 68)),
			};
			Grid.SetColumn(divider, 1);
			shell.Children.Add(divider);

			var content = this.CreateContentPanel();
			Grid.SetColumn(content, 2);
			shell.Children.Add(content);

			root.Children.Add(shell);

			// A desktop manager should open on the desktop overview, not on generic settings.
			this.SelectPrimaryNavigation(this._primaryNavigationButtons[0]);
		}

		private Border CreateNavigationPanel()
		{
			var panel = new DockPanel
			{
				LastChildFill = true,
				Background = new SolidColorBrush(Color.FromRgb(23, 26, 31)),
			};

			var footer = new Border
			{
				Padding = new Thickness(18, 14, 18, 16),
				BorderBrush = new SolidColorBrush(Color.FromRgb(48, 53, 61)),
				BorderThickness = new Thickness(0, 1, 0, 0),
			};
			DockPanel.SetDock(footer, Dock.Bottom);
			footer.Child = new StackPanel
			{
				Children =
				{
					new TextBlock
					{
						Text = "SylphyHornPlusCon",
						Foreground = new SolidColorBrush(Color.FromRgb(225, 229, 235)),
						FontSize = 13,
						FontWeight = FontWeights.SemiBold,
					},
					new TextBlock
					{
						Text = "Settings",
						Foreground = new SolidColorBrush(Color.FromRgb(135, 143, 154)),
						FontSize = 12,
						Margin = new Thickness(0, 2, 0, 0),
					},
				},
			};
			panel.Children.Add(footer);

			var scroll = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			};
			var stack = new StackPanel { Margin = new Thickness(12, 20, 12, 16) };

			var heading = new TextBlock
			{
				Text = "SETTINGS",
				Foreground = new SolidColorBrush(Color.FromRgb(122, 132, 145)),
				FontSize = 11,
				FontWeight = FontWeights.SemiBold,
				Margin = new Thickness(10, 0, 0, 12),
			};
			stack.Children.Add(heading);

			this.AddNavigationItem(stack, "Desktops", "Manage virtual desktops and per-desktop settings.", new[] { 1 });
			this.AddNavigationItem(stack, "General", "Desktop switching, tray behavior, startup, language and settings management.", new[] { 0 });
			this.AddNavigationItem(stack, "Notifications", "Configure desktop switch notifications, layout and appearance.", new[] { 2, 3 }, new[] { "Appearance", "Behavior" });
			this.AddNavigationItem(stack, "Keyboard shortcuts", "Configure global keyboard shortcuts by task instead of numbered pages.", new[] { 4, 5, 6, 7 }, new[] { "Desktop switching", "Move windows", "Reorder desktops", "Window actions" });
			this.AddNavigationItem(stack, "Mouse gestures", "Configure rocker, wheel and mouse gestures by task.", new[] { 8, 9, 10, 11 }, new[] { "Desktop switching", "Move windows", "Reorder desktops", "Window actions" });
			this.AddNavigationItem(stack, "App log", "Search, filter and inspect structured application diagnostics.", new[] { 12 });
			this.AddNavigationItem(stack, "About", "Version, source code, upstream credits and licenses.", new[] { 13 });

			scroll.Content = stack;
			panel.Children.Add(scroll);

			return new Border
			{
				Background = panel.Background,
				Child = panel,
			};
		}

		private Grid CreateContentPanel()
		{
			var grid = new Grid
			{
				Margin = new Thickness(34, 24, 34, 26),
			};
			grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

			var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
			header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			this._pageTitle = new TextBlock
			{
				Foreground = Brushes.White,
				FontFamily = new FontFamily("Segoe UI Semibold"),
				FontSize = 28,
				Text = "Desktops",
			};
			this._pageDescription = new TextBlock
			{
				Foreground = new SolidColorBrush(Color.FromRgb(173, 180, 190)),
				FontSize = 14,
				Margin = new Thickness(1, 5, 0, 0),
				Text = "Manage virtual desktops and per-desktop settings.",
			};
			Grid.SetRow(this._pageTitle, 0);
			Grid.SetRow(this._pageDescription, 1);
			header.Children.Add(this._pageTitle);
			header.Children.Add(this._pageDescription);
			Grid.SetRow(header, 0);
			grid.Children.Add(header);

			this._secondaryNavigation = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(0, 0, 0, 14),
			};
			Grid.SetRow(this._secondaryNavigation, 1);
			grid.Children.Add(this._secondaryNavigation);

			var contentCard = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(36, 40, 47)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(61, 66, 75)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(8),
				Child = this._legacySettingsTabs,
			};
			Grid.SetRow(contentCard, 2);
			grid.Children.Add(contentCard);

			return grid;
		}

		private void AddNavigationItem(StackPanel parent, string title, string description, int[] targetIndices, string[] subPages = null)
		{
			var button = new Button
			{
				Content = title,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				Height = 46,
				Padding = new Thickness(14, 0, 12, 0),
				Margin = new Thickness(0, 2, 0, 2),
				BorderThickness = new Thickness(0),
				Background = Brushes.Transparent,
				Foreground = new SolidColorBrush(Color.FromRgb(224, 228, 234)),
				FontSize = 14,
				Cursor = System.Windows.Input.Cursors.Hand,
			};
			button.Click += (_, _) => this.SelectPrimaryNavigation(button);
			button.MouseEnter += (_, _) =>
			{
				if (button != this._selectedPrimaryButton)
				{
					button.Background = new SolidColorBrush(Color.FromRgb(34, 40, 48));
				}
			};
			button.MouseLeave += (_, _) =>
			{
				if (button != this._selectedPrimaryButton)
				{
					button.Background = Brushes.Transparent;
				}
			};

			this._primaryNavigationButtons.Add(button);
			this._navigationTargets[button] = targetIndices;
			this._navigationDescriptions[button] = description;
			if (subPages != null)
			{
				this._navigationSubPages[button] = subPages;
			}
			parent.Children.Add(button);
		}

		private void SelectPrimaryNavigation(Button button)
		{
			if (!this._navigationTargets.TryGetValue(button, out var targets) || targets.Length == 0)
			{
				return;
			}

			foreach (var navButton in this._primaryNavigationButtons)
			{
				navButton.Background = Brushes.Transparent;
				navButton.Foreground = new SolidColorBrush(Color.FromRgb(224, 228, 234));
				navButton.FontWeight = FontWeights.Normal;
			}

			this._selectedPrimaryButton = button;
			button.Background = new SolidColorBrush(Color.FromRgb(31, 72, 109));
			button.Foreground = Brushes.White;
			button.FontWeight = FontWeights.SemiBold;

			this._pageTitle.Text = button.Content?.ToString() ?? string.Empty;
			this._pageDescription.Text = this._navigationDescriptions.TryGetValue(button, out var description)
				? description
				: string.Empty;

			this._secondaryNavigation.Children.Clear();
			if (targets.Length > 1)
			{
				this._navigationSubPages.TryGetValue(button, out var labels);
				for (var i = 0; i < targets.Length; i++)
				{
					var pageIndex = targets[i];
					var label = labels != null && i < labels.Length ? labels[i] : $"Page {i + 1}";
					var secondaryButton = this.CreateSecondaryNavigationButton(label, pageIndex, i == 0);
					this._secondaryNavigation.Children.Add(secondaryButton);
				}
			}

			this._legacySettingsTabs.SelectedIndex = targets[0];
		}

		private Button CreateSecondaryNavigationButton(string label, int pageIndex, bool selected)
		{
			var button = new Button
			{
				Content = label,
				Padding = new Thickness(14, 7, 14, 7),
				Margin = new Thickness(0, 0, 8, 0),
				BorderThickness = new Thickness(1),
				BorderBrush = new SolidColorBrush(Color.FromRgb(65, 72, 82)),
				Background = selected ? new SolidColorBrush(Color.FromRgb(40, 91, 136)) : new SolidColorBrush(Color.FromRgb(31, 35, 41)),
				Foreground = Brushes.White,
				FontSize = 13,
				Cursor = System.Windows.Input.Cursors.Hand,
			};

			button.Click += (_, _) =>
			{
				this._legacySettingsTabs.SelectedIndex = pageIndex;
				foreach (Button sibling in this._secondaryNavigation.Children.OfType<Button>())
				{
					sibling.Background = new SolidColorBrush(Color.FromRgb(31, 35, 41));
					sibling.FontWeight = FontWeights.Normal;
				}
				button.Background = new SolidColorBrush(Color.FromRgb(40, 91, 136));
				button.FontWeight = FontWeights.SemiBold;
			};

			if (selected)
			{
				button.FontWeight = FontWeights.SemiBold;
			}
			return button;
		}

		private void HideLegacyTabHeaders()
		{
			var template = new ControlTemplate(typeof(TabControl));
			var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
			presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectedContent")
			{
				RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
			});
			presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectedContentTemplate")
			{
				RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
			});
			presenter.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, new Binding("SelectedContentTemplateSelector")
			{
				RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
			});
			template.VisualTree = presenter;
			this._legacySettingsTabs.Template = template;
			this._legacySettingsTabs.Background = Brushes.Transparent;
			this._legacySettingsTabs.BorderThickness = new Thickness(0);
		}
	}
}
