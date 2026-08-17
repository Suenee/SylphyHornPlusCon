using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Livet;
using Livet.EventListeners;
using Livet.Messaging;
using Livet.Messaging.IO;
using MetroRadiance.Platform;
using MetroRadiance.UI.Controls;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;
using MetroTrilithon.Threading.Tasks;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using SylphyHorn.UI.Controls;
using WindowsDesktop;

namespace SylphyHorn.UI.Bindings
{
	public class SettingsWindowViewModel : WindowViewModel
	{
		private static bool _restartRequired;
		private static readonly string _defaultCulture = Settings.General.Culture;
		private static string _exportOrImportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		private readonly HookService _hookService;
		private readonly DesktopTransitionRuntime _desktopRuntime;
		private readonly Startup _startup;
		private readonly StartupScheduler _startupScheduler;

		public IReadOnlyCollection<DisplayItem<string>> Cultures { get; }

		public IReadOnlyCollection<DisplayItem<WallpaperPosition>> WallpaperPositions { get; }

		public IReadOnlyCollection<DisplayItem<WindowPlacement>> Placements { get; }

		public IReadOnlyCollection<DisplayItem<BlurWindowThemeMode>> NotificationWindowStyles { get; }

		public IReadOnlyCollection<DisplayItem<BlurWindowCornerMode>> NotificationCornerStyles { get; }

		public IReadOnlyCollection<DisplayItem<HorizontalAlignment>> NotificationTextAlignments { get; }

		public bool IsDisplayEnabled { get; }

		public IReadOnlyCollection<DisplayItem<uint>> Displays { get; }

		public IReadOnlyCollection<LicenseViewModel> Licenses { get; }

		public bool RestartRequired => _restartRequired;

		public bool IsWindows10OrEarlier => !ProductInfo.IsWallpaperSupportBuild;

		public bool IsWindows11OrLater => ProductInfo.IsWindows11OrLater;

		public bool IsNameSupport => ProductInfo.IsNameSupportBuild;

		public bool IsReorderingSupport => ProductInfo.IsReorderingSupportBuild;

		#region HasStartupLink notification property

		private bool _HasStartupLink;

		public bool HasStartupLink
		{
			get => this._HasStartupLink;
			set
			{
				if (this._HasStartupLink != value)
				{
					if (value)
					{
						if (this.HasStartupScheduler == value)
						{
							this.HasStartupScheduler = !value;
							if (this.HasStartupScheduler)
							{
								return;
							}
						}
						this._startup.Create();
					}
					else
					{
						this._startup.Remove();
					}

					this._HasStartupLink = this._startup.IsExists;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region HasStartupScheduler notification property

		private bool _HasStartupScheduler;

		public bool HasStartupScheduler
		{
			get => this._HasStartupScheduler;
			set
			{
				if (this._HasStartupScheduler != value)
				{
					try
					{
						if (value)
						{
							this._startupScheduler.Register();
							if (!this._startupScheduler.IsExists)
							{
								return;
							}
							else if (this.HasStartupLink == value)
							{
								this.HasStartupLink = !value;
							}
						}
						else
						{
							this._startupScheduler.Unregister();
						}
					}
					catch (UnauthorizedAccessException)
					{
						return;
					}
					finally
					{
						this._HasStartupScheduler = this._startupScheduler.IsExists;
						this.RaisePropertyChanged();
					}
				}
			}
		}

		#endregion

		#region Culture notification property

		public string Culture
		{
			get => Settings.General.Culture;
			set
			{
				if (Settings.General.Culture != value)
				{
					Settings.General.Culture.Value = value;
					_restartRequired = value != _defaultCulture;

					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.RestartRequired));
				}
			}
		}

		#endregion

		#region Desktops notification property

		private VirtualDesktopViewModel[] _Desktops;

		public VirtualDesktopViewModel[] Desktops
		{
			get => this._Desktops;
			set
			{
				if (this._Desktops != value)
				{
					this._Desktops = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfSwitchToIndicesLarger));
					this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfMoveToIndicesLarger));
					this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfMoveToIndicesAndSwitchLarger));
					this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfSwapDesktopIndicesLarger));
					this.RaisePropertyChanged(nameof(this.IsMouseOfSwitchToIndicesLarger));
					this.RaisePropertyChanged(nameof(this.IsMouseOfMoveToIndicesLarger));
					this.RaisePropertyChanged(nameof(this.IsMouseOfMoveToIndicesAndSwitchLarger));
					this.RaisePropertyChanged(nameof(this.IsMouseOfSwapDesktopIndicesLarger));
				}
			}
		}

		#endregion

		#region CurrentDesktop notification property

		private VirtualDesktopViewModel _CurrentDesktop;

		public VirtualDesktopViewModel CurrentDesktop
		{
			get => this._CurrentDesktop;
			set
			{
				if (this._CurrentDesktop != value)
				{
					this._CurrentDesktop = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.PreviewNotificationText));
				}
			}
		}

		#endregion

		#region Placement notification property

		public WindowPlacement Placement
		{
			get => (WindowPlacement)Settings.General.Placement.Value;
			set
			{
				if ((WindowPlacement)Settings.General.Placement.Value != value)
				{
					Settings.General.Placement.Value = (uint)value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Display notification property

		public uint Display
		{
			get => Settings.General.Display;
			set
			{
				if (Settings.General.Display != value)
				{
					Settings.General.Display.Value = value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationWindowStyle notification property

		public BlurWindowThemeMode NotificationWindowStyle
		{
			get => (BlurWindowThemeMode)Settings.General.NotificationWindowStyle.Value;
			set
			{
				if ((BlurWindowThemeMode)Settings.General.NotificationWindowStyle.Value != value)
				{
					Settings.General.NotificationWindowStyle.Value = (uint)value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationCornerStyle notification property

		public BlurWindowCornerMode NotificationCornerStyle
		{
			get => (BlurWindowCornerMode)Settings.General.NotificationCornerStyle.Value;
			set
			{
				if ((BlurWindowCornerMode)Settings.General.NotificationCornerStyle.Value != value)
				{
					Settings.General.NotificationCornerStyle.Value = (uint)value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationHeaderAlignment notification property

		public HorizontalAlignment NotificationHeaderAlignment
		{
			get => (HorizontalAlignment)Settings.General.NotificationHeaderAlignment.Value;
			set
			{
				if ((HorizontalAlignment)Settings.General.NotificationHeaderAlignment.Value != value)
				{
					Settings.General.NotificationHeaderAlignment.Value = (uint)value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationBodyAlignment notification property

		public HorizontalAlignment NotificationBodyAlignment
		{
			get => (HorizontalAlignment)Settings.General.NotificationBodyAlignment.Value;
			set
			{
				if ((HorizontalAlignment)Settings.General.NotificationBodyAlignment.Value != value)
				{
					Settings.General.NotificationBodyAlignment.Value = (uint)value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationOffsetX notification property

		public int? NotificationOffsetX
		{
			get => Settings.General.NotificationOffsetX.Value;
			set
			{
				var param = value ?? this.GetInitialNotificationOffsetX();

				if (Settings.General.NotificationOffsetX.Value != param)
				{
					Settings.General.NotificationOffsetX.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationOffsetY notification property

		public int? NotificationOffsetY
		{
			get => Settings.General.NotificationOffsetY.Value;
			set
			{
				var param = value ?? this.GetInitialNotificationOffsetY();

				if (Settings.General.NotificationOffsetY.Value != param)
				{
					Settings.General.NotificationOffsetY.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region PinWindowOffsetX notification property

		public int? PinWindowOffsetX
		{
			get => Settings.General.PinWindowOffsetX.Value;
			set
			{
				var param = value ?? GeneralSettings.PinWindowOffsetXDefaultValue;

				if (Settings.General.PinWindowOffsetX.Value != param)
				{
					Settings.General.PinWindowOffsetX.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region PinWindowOffsetY notification property

		public int? PinWindowOffsetY
		{
			get => Settings.General.PinWindowOffsetY.Value;
			set
			{
				var param = value ?? GeneralSettings.PinWindowOffsetYDefaultValue;

				if (Settings.General.PinWindowOffsetY.Value != param)
				{
					Settings.General.PinWindowOffsetY.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationMinWidth notification property

		public int? NotificationMinWidth
		{
			get => Settings.General.NotificationMinWidth.Value;
			set
			{
				var param = value ?? GeneralSettings.NotificationMinWidthDefaultValue;
				if (param <= 0)
				{
					param = GeneralSettings.NotificationMinWidthDefaultValue;
				}

				if (Settings.General.NotificationMinWidth.Value != param)
				{
					Settings.General.NotificationMinWidth.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SimpleNotificationMinWidth notification property

		public int? SimpleNotificationMinWidth
		{
			get => Settings.General.SimpleNotificationMinWidth.Value;
			set
			{
				var param = value ?? GeneralSettings.SimpleNotificationMinWidthDefaultValue;
				if (param <= 0)
				{
					param = GeneralSettings.SimpleNotificationMinWidthDefaultValue;
				}

				if (Settings.General.SimpleNotificationMinWidth.Value != param)
				{
					Settings.General.SimpleNotificationMinWidth.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region PinWindowMinWidth notification property

		public int? PinWindowMinWidth
		{
			get => Settings.General.PinWindowMinWidth.Value;
			set
			{
				var param = value ?? GeneralSettings.PinWindowMinWidthDefaultValue;
				if (param <= 0)
				{
					param = GeneralSettings.PinWindowMinWidthDefaultValue;
				}

				if (Settings.General.PinWindowMinWidth.Value != param)
				{
					Settings.General.PinWindowMinWidth.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationMinHeight notification property

		public int? NotificationMinHeight
		{
			get => Settings.General.NotificationMinHeight.Value;
			set
			{
				var param = value ?? GeneralSettings.NotificationMinHeightDefaultValue;
				if (param <= 0)
				{
					param = GeneralSettings.NotificationMinHeightDefaultValue;
				}

				if (Settings.General.NotificationMinHeight.Value != param)
				{
					Settings.General.NotificationMinHeight.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationFontFamily notification property

		public string NotificationFontFamily
		{
			get => Settings.General.NotificationFontFamily.Value;
			set
			{
				if (Settings.General.NotificationFontFamily.Value != value)
				{
					Settings.General.NotificationFontFamily.Value = value;

					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.NotificationFontFamilyOrDefault));
				}
			}
		}

		#endregion

		public string NotificationFontFamilyOrDefault
		{
			get
			{
				var fontFamily = Settings.General.NotificationFontFamily.Value;
				var defaultFont = GeneralSettings.NotificationFontFamilyDefaultValue;
				return !string.IsNullOrEmpty(fontFamily)
					? fontFamily + ", " + defaultFont
					: defaultFont;
			}
		}

		#region NotificationHeaderFontSize notification property

		public int? NotificationHeaderFontSize
		{
			get => Settings.General.NotificationHeaderFontSize.Value;
			set
			{
				var param = value ?? GeneralSettings.NotificationHeaderFontSizeDefaultValue;
				if (param <= 0)
				{
					param = GeneralSettings.NotificationHeaderFontSizeDefaultValue;
				}

				if (Settings.General.NotificationHeaderFontSize.Value != param)
				{
					Settings.General.NotificationHeaderFontSize.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationBodyFontSize notification property

		public int? NotificationBodyFontSize
		{
			get => Settings.General.NotificationBodyFontSize.Value;
			set
			{
				var param = value ?? GeneralSettings.NotificationBodyFontSizeDefaultValue;
				if (param <= 0)
				{
					param = GeneralSettings.NotificationBodyFontSizeDefaultValue;
				}

				if (Settings.General.NotificationBodyFontSize.Value != param)
				{
					Settings.General.NotificationBodyFontSize.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region NotificationLineSpacing notification property

		public int? NotificationLineSpacing
		{
			get => Settings.General.NotificationLineSpacing.Value;
			set
			{
				var param = value ?? GeneralSettings.NotificationLineSpacingDefaultValue;
				if (Settings.General.NotificationLineSpacing.Value != param)
				{
					Settings.General.NotificationLineSpacing.Value = param;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public bool HasPreviewWallpaper => !string.IsNullOrEmpty(this.PreviewBackgroundPath);

		#region PreviewBackgroundBrush notification property

		private SolidColorBrush _PreviewBackgroundBrush;

		public SolidColorBrush PreviewBackgroundBrush
		{
			get => this._PreviewBackgroundBrush;
			set
			{
				if (this._PreviewBackgroundBrush != value)
				{
					this._PreviewBackgroundBrush = value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region PreviewBackgroundPath notification property

		private string _PreviewBackgroundPath;

		public string PreviewBackgroundPath
		{
			get => this._PreviewBackgroundPath;
			set
			{
				if (this._PreviewBackgroundPath != value)
				{
					this._PreviewBackgroundPath = value;

					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.HasPreviewWallpaper));
				}
			}
		}

		#endregion

		#region PreviewCornerRadius notification property

		private int _PreviewCornerRadius;

		public int PreviewCornerRadius
		{
			get => this._PreviewCornerRadius;
			set
			{
				if (!this.IsWindows11OrLater)
				{
					value = 0;
				}

				if (this._PreviewCornerRadius != value)
				{
					this._PreviewCornerRadius = value;

					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public Visibility PreviewNotificationHeaderVisibility => Settings.General.SimpleNotification
			? Visibility.Collapsed
			: Visibility.Visible;

		public string PreviewNotificationText
		{
			get
			{
				if (Settings.General.UseDesktopName && this.CurrentDesktop != null)
				{
					return Settings.General.SimpleNotification
						? $"{this.CurrentDesktop.NumberText}. {this.CurrentDesktop.Name}"
						: $"Desktop {this.CurrentDesktop.NumberText}: {this.CurrentDesktop.Name}";
				}
				else
				{
					var numberText = this.CurrentDesktop != null ? this.CurrentDesktop.NumberText : "1";
					return Settings.General.SimpleNotification
						? $"Desktop {numberText}"
						: $"Current Desktop: Desktop {numberText}";
				}
			}
		}

		#region NotificationBackgroundColor notification property

		private Color _NotificationBackgroundColor;

		public Color NotificationBackgroundColor
		{
			get => this._NotificationBackgroundColor;
			set
			{
				if (this._NotificationBackgroundColor != value)
				{
					this._NotificationBackgroundColor = value;

					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.NotificationBackground));
				}
			}
		}

		#endregion

		public Brush NotificationBackground => new SolidColorBrush(this.NotificationBackgroundColor)
		{ Opacity = WindowsTheme.Transparency.Current ? 0.8 : 1.0 };

		#region NotificationForegroundColor notification property

		private Color _NotificationForegroundColor;

		public Color NotificationForegroundColor
		{
			get => this._NotificationForegroundColor;
			set
			{
				if (this._NotificationForegroundColor != value)
				{
					this._NotificationForegroundColor = value;

					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.NotificationForeground));
				}
			}
		}

		#endregion

		public Brush NotificationForeground => new SolidColorBrush(this.NotificationForegroundColor);

		public Brush TaskbarBackground => new SolidColorBrush(WindowsTheme.ColorPrevalence.Current
			? ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemAccentDark1)
			: ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.DarkChromeMedium))
		{ Opacity = WindowsTheme.Transparency.Current ? 0.8 : 1.0 };

		public bool IsShortcutKeyOfSwitchToIndicesLarger => Settings.ShortcutKey.SwitchToIndices.Count > Desktops.Length;

		public bool IsShortcutKeyOfMoveToIndicesLarger => Settings.ShortcutKey.MoveToIndices.Count > Desktops.Length;

		public bool IsShortcutKeyOfMoveToIndicesAndSwitchLarger => Settings.ShortcutKey.MoveToIndicesAndSwitch.Count > Desktops.Length;

		public bool IsShortcutKeyOfSwapDesktopIndicesLarger => Settings.ShortcutKey.SwapDesktopIndices.Count > Desktops.Length;

		public bool IsMouseOfSwitchToIndicesLarger => Settings.MouseShortcut.SwitchToIndices.Count > Desktops.Length;

		public bool IsMouseOfMoveToIndicesLarger => Settings.MouseShortcut.MoveToIndices.Count > Desktops.Length;

		public bool IsMouseOfMoveToIndicesAndSwitchLarger => Settings.MouseShortcut.MoveToIndicesAndSwitch.Count > Desktops.Length;

		public bool IsMouseOfSwapDesktopIndicesLarger => Settings.MouseShortcut.SwapDesktopIndices.Count > Desktops.Length;

		public RelayCommand OpenExportPathDialogCommand { get; }

		public RelayCommand OpenImportPathDialogCommand { get; }

		public RelayCommand ResetSettingsCommand { get; }

		public RelayCommand CreateDesktopCommand { get; }

		public RelayCommand<int> OpenBackgroundPathDialogCommand { get; }

		public RelayCommand<string> AddShortcutListCommand { get; }

		public RelayCommand<string> RemoveLastShortcutListCommand { get; }

		public RelayCommand<string> ResizeShortcutListToFitCommand { get; }

		public RelayCommand<string> AddMouseListCommand { get; }

		public RelayCommand<string> RemoveLastMouseListCommand { get; }

		public RelayCommand<string> ResizeMouseListToFitCommand { get; }
		public ReadOnlyDispatcherCollection<LogViewModel> Logs { get; }

		internal SettingsWindowViewModel(HookService hookService, DesktopTransitionRuntime desktopRuntime)
		{
			this.OpenExportPathDialogCommand = new RelayCommand(this.OpenExportPathDialog);
			this.OpenImportPathDialogCommand = new RelayCommand(this.OpenImportPathDialog);
			this.ResetSettingsCommand = new RelayCommand(this.ResetSettings);
			this.CreateDesktopCommand = new RelayCommand(this.CreateDesktop);
			this.OpenBackgroundPathDialogCommand = new RelayCommand<int>(this.OpenBackgroundPathDialog);
			this.AddShortcutListCommand = new RelayCommand<string>(this.AddShortcutList);
			this.RemoveLastShortcutListCommand = new RelayCommand<string>(this.RemoveLastShortcutList);
			this.ResizeShortcutListToFitCommand = new RelayCommand<string>(this.ResizeShortcutListToFit);
			this.AddMouseListCommand = new RelayCommand<string>(this.AddMouseList);
			this.RemoveLastMouseListCommand = new RelayCommand<string>(this.RemoveLastMouseList);
			this.ResizeMouseListToFitCommand = new RelayCommand<string>(this.ResizeMouseListToFit);
			this._hookService = hookService;
			this._desktopRuntime = desktopRuntime ?? throw new ArgumentNullException(nameof(desktopRuntime));
			ShortcutKeyBox.HookService = hookService;
			MouseShortcutBox.HookService = hookService;

			this._startup = new Startup();
			this._startupScheduler = new StartupScheduler();

			this.Cultures = new[] { new DisplayItem<string> { Display = "(auto)", } }
				.Concat(ResourceService.Current.SupportedCultures
					.Select(x => new DisplayItem<string> { Display = x.NativeName, Value = x.Name, })
					.OrderBy(x => x.Display))
				.ToList();

			this.WallpaperPositions = new[]
			{
				new DisplayItem<WallpaperPosition> { Display = " " + Resources.Settings_Background_Position_Center, Value = WallpaperPosition.Center, },
				new DisplayItem<WallpaperPosition> { Display = " " + Resources.Settings_Background_Position_Tile, Value = WallpaperPosition.Tile, },
				new DisplayItem<WallpaperPosition> { Display = " " + Resources.Settings_Background_Position_Stretch, Value = WallpaperPosition.Stretch, },
				new DisplayItem<WallpaperPosition> { Display = " " + Resources.Settings_Background_Position_Fit, Value = WallpaperPosition.Fit, },
				new DisplayItem<WallpaperPosition> { Display = " " + Resources.Settings_Background_Position_Fill, Value = WallpaperPosition.Fill, },
				new DisplayItem<WallpaperPosition> { Display = " " + Resources.Settings_Background_Position_Span, Value = WallpaperPosition.Span, },
			}.ToList();

			this.Placements = new[]
			{
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_TopLeft, Value = WindowPlacement.TopLeft, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_TopCenter, Value = WindowPlacement.TopCenter, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_TopRight, Value = WindowPlacement.TopRight, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_CenterLeft, Value = WindowPlacement.CenterLeft, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_Center, Value = WindowPlacement.Center, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_CenterRight, Value = WindowPlacement.CenterRight, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_BottomLeft, Value = WindowPlacement.BottomLeft, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_BottomCenter, Value = WindowPlacement.BottomCenter, },
				new DisplayItem<WindowPlacement> { Display = Resources.Settings_NotificationWindowPlacement_BottomRight, Value = WindowPlacement.BottomRight, },
			}.ToList();

			this.NotificationWindowStyles = new[]
			{
				new DisplayItem<BlurWindowThemeMode> { Display = Resources.Settings_NotificationWindowStyle_Apps, Value = BlurWindowThemeMode.Default, },
				new DisplayItem<BlurWindowThemeMode> { Display = Resources.Settings_NotificationWindowStyle_Light, Value = BlurWindowThemeMode.Light, },
				new DisplayItem<BlurWindowThemeMode> { Display = Resources.Settings_NotificationWindowStyle_Dark, Value = BlurWindowThemeMode.Dark, },
				new DisplayItem<BlurWindowThemeMode> { Display = Resources.Settings_NotificationWindowStyle_Accent, Value = BlurWindowThemeMode.Accent, },
				new DisplayItem<BlurWindowThemeMode> { Display = Resources.Settings_NotificationWindowStyle_System, Value = BlurWindowThemeMode.System, },
			}.ToList();

			this.NotificationCornerStyles = new[]
			{
				new DisplayItem<BlurWindowCornerMode> { Display = Resources.Settings_NotificationCornerStyle_NotRounded, Value = BlurWindowCornerMode.NotRounded, },
				new DisplayItem<BlurWindowCornerMode> { Display = Resources.Settings_NotificationCornerStyle_Rounded, Value = BlurWindowCornerMode.Rounded, },
				new DisplayItem<BlurWindowCornerMode> { Display = Resources.Settings_NotificationCornerStyle_SmallRounded, Value = BlurWindowCornerMode.SmallRounded, },
			}.ToList();

			this.NotificationTextAlignments = new[]
			{
				new DisplayItem<HorizontalAlignment> { Display = Resources.Settings_NotificationTextAlignment_Left, Value = HorizontalAlignment.Left, },
				new DisplayItem<HorizontalAlignment> { Display = Resources.Settings_NotificationTextAlignment_Center, Value = HorizontalAlignment.Center, },
				new DisplayItem<HorizontalAlignment> { Display = Resources.Settings_NotificationTextAlignment_Right, Value = HorizontalAlignment.Right, },
			}.ToList();

			this.Displays = new[] { new DisplayItem<uint> { Display = Resources.Settings_MultipleDisplays_CurrentDisplay, Value = 0, } }
				.Concat(MonitorService.GetMonitors()
					.Select((m, i) => new DisplayItem<uint>
					{
						Display = string.Format(Resources.Settings_MultipleDisplays_EachDisplay, i + 1, m.Name),
						Value = (uint)(i + 1),
					}))
				.Concat(new[]
				{
					new DisplayItem<uint>
					{
						Display = Resources.Settings_MultipleDisplays_AllDisplays,
						Value = uint.MaxValue,
					}
				})
				.ToList();
			if (this.Displays.Count > 3) this.IsDisplayEnabled = true;

			this.Licenses = LicenseInfo.All.Select(x => new LicenseViewModel(x)).ToArray();

			this._HasStartupLink = this._startup.IsExists;
			this._HasStartupScheduler = this._startupScheduler.IsExists;

			this.UpdateDesktops(this._desktopRuntime.State);
			EventHandler<DesktopRuntimeStateChanged> desktopStateChanged = (sender, args) =>
			{
				this.UpdateDesktops(args.Change.Snapshot);
				this.UpdatePreviewBackground();
			};
			this._desktopRuntime.StateChanged += desktopStateChanged;
			Disposable.Create(() => this._desktopRuntime.StateChanged -= desktopStateChanged).AddTo(this);
			this._desktopRuntime.RequestReconciliationAsync().Forget();
			var colAndWall = WallpaperService.GetCurrentColorAndWallpaper();
			this.PreviewBackgroundBrush = new SolidColorBrush(colAndWall.Item1);
			this.PreviewBackgroundPath = colAndWall.Item2;
			this.UpdateNotificationColor(this.NotificationWindowStyle);
			this.UpdateNotificationCornerRadius(this.NotificationCornerStyle);

			this.Logs = ViewModelHelper.CreateReadOnlyDispatcherCollection(
				LoggingService.Instance.Logs,
				log => new LogViewModel(log),
				DispatcherHelper.UIDispatcher);

			Settings.General.AlwaysShowDesktopNotification
				.Subscribe(alwaysShow =>
				{
					if (alwaysShow)
					{
						NotificationService.Instance.ShowCurrentDesktop();
					}
					else
					{
						NotificationService.Instance.HideCurrentDesktop();
					}
				})
				.AddTo(this);

			Settings.General.OverrideWindowsDefaultKeyCombination
				.Subscribe(_ => this._hookService.Reload())
				.AddTo(this);
			Settings.General.LoopDesktop
				.Subscribe(_ => this._hookService.Reload())
				.AddTo(this);

			Settings.General.SimpleNotification
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.PreviewNotificationText)))
				.AddTo(this);
			Settings.General.SimpleNotification
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.PreviewNotificationHeaderVisibility)))
				.AddTo(this);
			Settings.General.UseDesktopName
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.PreviewNotificationText)))
				.AddTo(this);
			Settings.General.NotificationWindowStyle
				.Subscribe(mode => this.UpdateNotificationColor((BlurWindowThemeMode)mode))
				.AddTo(this);
			Settings.General.NotificationCornerStyle
				.Subscribe(mode => this.UpdateNotificationCornerRadius((BlurWindowCornerMode)mode))
				.AddTo(this);

			Settings.ShortcutKey.SwitchToIndices
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfSwitchToIndicesLarger)))
				.AddTo(this);
			Settings.ShortcutKey.MoveToIndices
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfMoveToIndicesLarger)))
				.AddTo(this);
			Settings.ShortcutKey.MoveToIndicesAndSwitch
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfMoveToIndicesAndSwitchLarger)))
				.AddTo(this);
			Settings.ShortcutKey.SwapDesktopIndices
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsShortcutKeyOfSwapDesktopIndicesLarger)))
				.AddTo(this);
			Settings.MouseShortcut.SwitchToIndices
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsMouseOfSwitchToIndicesLarger)))
				.AddTo(this);
			Settings.MouseShortcut.MoveToIndices
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsMouseOfMoveToIndicesLarger)))
				.AddTo(this);
			Settings.MouseShortcut.MoveToIndicesAndSwitch
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsMouseOfMoveToIndicesAndSwitchLarger)))
				.AddTo(this);
			Settings.MouseShortcut.SwapDesktopIndices
				.Subscribe(_ => this.RaisePropertyChanged(nameof(this.IsMouseOfSwapDesktopIndicesLarger)))
				.AddTo(this);

			WindowsTheme.ColorPrevalence
				.RegisterListener(_ => this.UpdateNotificationColor(this.NotificationWindowStyle))
				.AddTo(this);
			WindowsTheme.ColorPrevalence
				.RegisterListener(_ => this.RaisePropertyChanged(nameof(this.TaskbarBackground)))
				.AddTo(this);
			WindowsTheme.Transparency
				.RegisterListener(_ => this.UpdateNotificationColor(this.NotificationWindowStyle))
				.AddTo(this);
			WindowsTheme.Transparency
				.RegisterListener(_ => this.RaisePropertyChanged(nameof(this.TaskbarBackground)))
				.AddTo(this);

			Disposable.Create(() => LocalSettingsProvider.Instance.SaveAsync().Forget())
				.AddTo(this);

			Disposable.Create(() => Application.Current.TaskTrayIcon.Reload())
				.AddTo(this);

			Disposable.Create(() => GC.Collect())
				.AddTo(this);
		}

		protected override void InitializeCore()
		{
			base.InitializeCore();
			Disposable.Create(() =>
				{
					ShortcutKeyBox.HookService = null;
					MouseShortcutBox.HookService = null;
				})
				.AddTo(this);
		}

		public void OpenBackgroundPathDialog(int index)
		{
			var message = new OpeningFileSelectionMessage("Window.OpenBackgroundImagesDialog.Open")
			{
				Title = Resources.Settings_Background_SelectionDialog,
				InitialDirectory = Settings.General.DesktopBackgroundFolderPath,
				Filter = WallpaperService.SupportedFormats,
				MultiSelect = false,
			};
			this.Messenger.Raise(message);

			if (message.Response != null && message.Response.Length > 0 && File.Exists(message.Response[0]))
			{
				var filePath = message.Response[0];
				Settings.General.DesktopBackgroundFolderPath.Value = Path.GetDirectoryName(filePath);
				this._Desktops[index].WallpaperPath = filePath;
			}
		}

		public void OpenExportPathDialog()
		{
			var provider = LocalSettingsProvider.Instance;
			var message = new SavingFileSelectionMessage("Window.OpenExportPathDialog.Open")
			{
				Title = Resources.Settings_ManagingSettings_ExportDialog,
				InitialDirectory = _exportOrImportFolder,
				FileName = provider.Filename,
				Filter = LocalSettingsProvider.SupportedFormats,
			};
			this.Messenger.Raise(message);

			if (message.Response != null && message.Response.Length > 0 && !string.IsNullOrEmpty(message.Response[0]))
			{
				var filePath = message.Response[0];
				_exportOrImportFolder = Path.GetDirectoryName(filePath);
				provider.ExportAsync(filePath).Forget();
			}
		}

		public async void OpenImportPathDialog()
		{
			var provider = LocalSettingsProvider.Instance;
			var message = new OpeningFileSelectionMessage("Window.OpenImportPathDialog.Open")
			{
				Title = Resources.Settings_ManagingSettings_ImportDialog,
				InitialDirectory = _exportOrImportFolder,
				FileName = provider.Filename,
				Filter = LocalSettingsProvider.SupportedFormats,
				MultiSelect = false,
			};
			this.Messenger.Raise(message);

			if (message.Response == null || message.Response.Length == 0 || string.IsNullOrEmpty(message.Response[0])) return;
			var hookDisposable = this._hookService?.Suspend();
			try
			{
				var filePath = message.Response[0];
				_exportOrImportFolder = Path.GetDirectoryName(filePath);
				var stage = await provider.PrepareImportAsync(filePath);
				var seed = SettingsService.CaptureDesktopStartupSeed(stage.Settings);
				var overrideDesktops = false;
				if (this.IsNameSupport && (seed.Names.Count > 0 || seed.WallpaperPaths.Count > 0))
				{
					var confirmation = new ConfirmationMessage("", "", "Window.OverrideDesktopsDialog.Confirm")
					{
						Text = Resources.Settings_ManagingSettings_OverrideDesktopsConfirmationMessage,
						Caption = Resources.Settings_ManagingSettings_OverrideDesktopsConfirmationDialog,
						Image = MessageBoxImage.Question,
						Button = MessageBoxButton.OKCancel,
					};
					this.Messenger.Raise(confirmation);
					overrideDesktops = confirmation.Response ?? false;
				}
				var result = await this._desktopRuntime.CommitPreparedImportAsync(stage, overrideDesktops, default(System.Threading.CancellationToken));
				if (result.Succeeded) this.NotifyOfAllPropertiesChanged();
			}
			finally { hookDisposable?.Dispose(); }
		}

		public async void ResetSettings()
		{
			var message = new ConfirmationMessage("", "", "Window.ResetSettingsDialog.Confirm")
			{
				Text = Resources.Settings_ManagingSettings_ResetConfirmationMessage,
				Caption = Resources.Settings_ManagingSettings_ResetConfirmationDialog,
				Image = MessageBoxImage.Warning,
				Button = MessageBoxButton.OKCancel,
			};
			this.Messenger.Raise(message);
			if (!(message.Response ?? false)) return;
			var hookDisposable = this._hookService?.Suspend();
			try
			{
				var result = await this._desktopRuntime.ResetSettingsAsync(default(System.Threading.CancellationToken));
				if (result.Succeeded) this.NotifyOfAllPropertiesChanged();
			}
			finally { hookDisposable?.Dispose(); }
		}

		public void CreateDesktop()
		{
			VirtualDesktop.Create();
		}

		public void AddShortcutList(string propName)
		{
			var propList = this.GetShortcutListFromSettings(Settings.ShortcutKey, propName);

			if (propList == null) return;

			propList.Resize(propList.Count + 1);
		}

		public void RemoveLastShortcutList(string propName)
		{
			var propList = this.GetShortcutListFromSettings(Settings.ShortcutKey, propName);

			if (propList == null || propList.Count == 0) return;

			propList.Resize(propList.Count - 1);
		}

		public void ResizeShortcutListToFit(string propName)
		{
			var propList = this.GetShortcutListFromSettings(Settings.ShortcutKey, propName);
			
			if (propList == null) return;

			var count = VirtualDesktopService.Count;
			propList.Resize(count);
		}

		public void AddMouseList(string propName)
		{
			var propList = this.GetShortcutListFromSettings(Settings.MouseShortcut, propName);

			if (propList == null) return;

			propList.Resize(propList.Count + 1);
		}

		public void RemoveLastMouseList(string propName)
		{
			var propList = this.GetShortcutListFromSettings(Settings.MouseShortcut, propName);

			if (propList == null || propList.Count == 0) return;

			propList.Resize(propList.Count - 1);
		}

		public void ResizeMouseListToFit(string propName)
		{
			var propList = this.GetShortcutListFromSettings(Settings.MouseShortcut, propName);
			
			if (propList == null) return;

			var count = VirtualDesktopService.Count;
			propList.Resize(count);
		}

		private ShortcutkeyPropertyList GetShortcutListFromSettings(ShortcutKeySettings settings, string propName)
		{
			var type = settings.GetType();
			return type.GetProperty(propName).GetValue(settings, null) as ShortcutkeyPropertyList;
		}

		private void UpdatePreviewBackground()
		{
			var state = this._desktopRuntime.State;
			var currentId = state?.CurrentDesktopId;
			this.CurrentDesktop = currentId.HasValue ? this.Desktops.FirstOrDefault(desktop => desktop.Id == currentId.Value) : null;
			this.PreviewBackgroundPath = DesktopTransitionRuntime.GetCurrentWallpaperPath(state);
		}



		private void UpdateDesktops(DesktopRuntimeState state)
		{
			if (state == null) return;
			var existing = (this._Desktops ?? Array.Empty<VirtualDesktopViewModel>()).ToDictionary(desktop => desktop.Id);
			var next = state.Order.Select((id, index) =>
			{
				if (!existing.TryGetValue(id, out var viewModel)) viewModel = new VirtualDesktopViewModel(this._desktopRuntime, index, state.Records[id]);
				else viewModel.Update(index, state.Records[id]);
				return viewModel;
			}).ToArray();
			this.Desktops = next;
			this.CurrentDesktop = state.CurrentDesktopId.HasValue ? next.FirstOrDefault(desktop => desktop.Id == state.CurrentDesktopId.Value) : null;
		}

		private void NotifyOfAllPropertiesChanged()
		{
			var properties = this.GetType().GetProperties();
			foreach (var prop in properties) this.RaisePropertyChanged(prop.Name);
		}
		private void UpdateNotificationColor(BlurWindowThemeMode mode)
		{
			this.GetColorByThemeMode(mode, out var background, out var foreground);
			this.NotificationBackgroundColor = background;
			this.NotificationForegroundColor = foreground;
		}

		private void UpdateNotificationCornerRadius(BlurWindowCornerMode mode)
		{
			if (mode == BlurWindowCornerMode.Rounded)
			{
				this.PreviewCornerRadius = 8;
			}
			else if (mode == BlurWindowCornerMode.SmallRounded)
			{
				this.PreviewCornerRadius = 4;
			}
			else
			{
				this.PreviewCornerRadius = 0;
			}
		}

		private void GetColorByThemeMode(BlurWindowThemeMode themeMode, out Color background, out Color foreground)
		{
			var colorPrevalence = WindowsTheme.ColorPrevalence.Current;
			switch (themeMode)
			{
				case BlurWindowThemeMode.Light:
					background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.LightChromeMedium);
					foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextLightTheme);
					break;

				case BlurWindowThemeMode.Dark:
					background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.DarkChromeMedium);
					foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextDarkTheme);
					break;

				case BlurWindowThemeMode.Accent:
					background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemAccentDark1);
					foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextDarkTheme);
					break;

				case BlurWindowThemeMode.System:
					if (colorPrevalence)
					{
						background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemAccentDark1);
						foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextDarkTheme);
					}
					else if (WindowsTheme.SystemTheme.Current == Theme.Light)
					{
						background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.LightChromeMedium);
						foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextLightTheme);
					}
					else
					{
						background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.DarkChromeMedium);
						foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextDarkTheme);
					}
					break;

				default:
					if (WindowsTheme.Theme.Current == Theme.Dark)
					{
						background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.DarkChromeMedium);
						foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextDarkTheme);
					}
					else
					{
						background = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.LightChromeMedium);
						foreground = ImmersiveColor.GetColorByTypeName(ImmersiveColorNames.SystemTextLightTheme);
					}
					break;
			}
		}

		private int GetInitialNotificationOffsetX()
		{
			if (this.NotificationCornerStyle < BlurWindowCornerMode.Rounded)
			{
				return GeneralSettings.NotificationOffsetXDefaultValue;
			}

			var placement = this.Placement;
			switch (placement)
			{
				case WindowPlacement.TopLeft:
				case WindowPlacement.CenterLeft:
				case WindowPlacement.BottomLeft:
					return GeneralSettings.NotificationOffsetXWithRoundedDefaultValue;

				case WindowPlacement.TopRight:
				case WindowPlacement.CenterRight:
				case WindowPlacement.BottomRight:
					return -GeneralSettings.NotificationOffsetXWithRoundedDefaultValue;

				case WindowPlacement.Center:
				default:
					return GeneralSettings.NotificationOffsetXDefaultValue;
			}
		}

		private int GetInitialNotificationOffsetY()
		{
			if (this.NotificationCornerStyle < BlurWindowCornerMode.Rounded)
			{
				return GeneralSettings.NotificationOffsetYDefaultValue;
			}

			var placement = this.Placement;
			switch (placement)
			{
				case WindowPlacement.TopLeft:
				case WindowPlacement.TopCenter:
				case WindowPlacement.TopRight:
					return -GeneralSettings.NotificationOffsetYWithRoundedDefaultValue;

				case WindowPlacement.BottomLeft:
				case WindowPlacement.BottomCenter:
				case WindowPlacement.BottomRight:
					return GeneralSettings.NotificationOffsetYWithRoundedDefaultValue;

				case WindowPlacement.Center:
				default:
					return GeneralSettings.NotificationOffsetYDefaultValue;
			}
		}
	}
}
