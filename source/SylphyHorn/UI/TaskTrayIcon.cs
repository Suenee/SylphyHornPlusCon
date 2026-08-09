using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MetroRadiance.Platform;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services.DesktopTransitions;

namespace SylphyHorn.UI
{
	public class TaskTrayIcon : IDisposable
	{
		private Icon _icon;
		private readonly Icon _darkIcon;
		private readonly Icon _lightIcon;
		private readonly TaskTrayIconItem[] _items;
		private NotifyIcon _notifyIcon;
		private DynamicInfoTrayIcon _infoIcon;
		private DesktopTransitionRuntime _runtime;
		private readonly string _showSettingsMenuName = Resources.TaskTray_Menu_Settings;

		public TaskTrayIcon(Icon darkIcon, Icon lightIcon, TaskTrayIconItem[] items)
		{
			this._darkIcon = darkIcon;
			this._lightIcon = lightIcon;
			this._icon = WindowsTheme.SystemTheme.Current == Theme.Light ? this._lightIcon : this._darkIcon;
			this._items = items;
			WindowsTheme.SystemTheme.Changed += this.OnSystemThemeChanged;
			WindowsTheme.Accent.Changed += this.OnAccentChanged;
			WindowsTheme.ColorPrevalence.Changed += this.OnColorPrevalenceChanged;
		}

		internal void BindDesktopRuntime(DesktopTransitionRuntime runtime)
		{
			if (runtime == null) throw new ArgumentNullException(nameof(runtime));
			if (this._runtime != null)
			{
				if (ReferenceEquals(this._runtime, runtime)) return;
				throw new InvalidOperationException("TaskTrayIcon cannot be rebound to another desktop runtime.");
			}
			this._runtime = runtime;
			runtime.StateChanged += this.OnDesktopStateChanged;
			this.ReloadMenuAvailability();
			this.Reload();
		}

		public void Show()
		{
			if (this._notifyIcon != null) return;
			var menus = this._items.Where(x => x.CanDisplay()).Select(this.CreateMenuItem).ToArray();
			this._notifyIcon = new NotifyIcon
			{
				Text = ProductInfo.Title,
				Icon = this._icon,
				Visible = true,
				ContextMenuStrip = new ContextMenuStrip(),
			};
			this._notifyIcon.ContextMenuStrip.Items.AddRange(menus);
			this._notifyIcon.MouseClick += this.OnIconClick;
		}

		public TaskTrayBaloon CreateBaloon() => new TaskTrayBaloon(this);

		internal void ShowBaloon(TaskTrayBaloon baloon)
		{
			if (this._notifyIcon == null) this.Show();
			this._notifyIcon.ShowBalloonTip((int)baloon.Timespan.TotalMilliseconds, baloon.Title, baloon.Text, ToolTipIcon.None);
		}

		public void Reload()
		{
			if (Settings.General.TrayShowDesktop && this._runtime?.State != null) this.UpdateWithDesktopInfo(this._runtime.State);
			else if (this._icon != this._darkIcon && this._icon != this._lightIcon)
			{
				this._infoIcon = null;
				this.ChangeIcon(WindowsTheme.SystemTheme.Current == Theme.Light ? this._lightIcon : this._darkIcon);
			}
		}

		private void UpdateWithDesktopInfo(DesktopRuntimeState state)
		{
			if (this._notifyIcon == null || state.CurrentDesktopId == null) return;
			var currentIndex = state.Order.IndexOf(state.CurrentDesktopId.Value) + 1;
			if (currentIndex <= 0) return;
			var total = state.Order.Count;
			this.ChangeText(string.Format(Resources.TaskTray_TooltipText_DesktopCount + "\n" + ProductInfo.Title, currentIndex, total));
			if (this._infoIcon == null) this._infoIcon = new DynamicInfoTrayIcon(WindowsTheme.SystemTheme.Current, WindowsTheme.ColorPrevalence.Current);
			this.ChangeIcon(this._infoIcon.GetDesktopInfoIcon(currentIndex, Settings.General.TrayShowOnlyCurrentNumber ? 0 : total));
		}

		private void OnDesktopStateChanged(object sender, DesktopRuntimeStateChanged e) => this.Reload();

		private void OnAccentChanged(object sender, System.Windows.Media.Color e)
		{
			var colorPrevalence = WindowsTheme.ColorPrevalence.Current;
			if (Settings.General.TrayShowDesktop && colorPrevalence)
			{
				this._infoIcon?.UpdateBrush(WindowsTheme.SystemTheme.Current, colorPrevalence);
				this.Reload();
			}
		}

		private void OnColorPrevalenceChanged(object sender, bool e)
		{
			if (!Settings.General.TrayShowDesktop) return;
			this._infoIcon?.UpdateBrush(WindowsTheme.SystemTheme.Current, e);
			this.Reload();
		}

		private void OnSystemThemeChanged(object sender, Theme e)
		{
			if (Settings.General.TrayShowDesktop)
			{
				this._infoIcon?.UpdateBrush(e, WindowsTheme.ColorPrevalence.Current);
				this.Reload();
			}
			else this.ChangeIcon(e == Theme.Light ? this._lightIcon : this._darkIcon);
		}

		private void OnIconClick(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left || this._items == null || this._items.Length == 0) return;
			var item = this._items.FirstOrDefault(i => i.Text == this._showSettingsMenuName);
			if (item?.CanDisplay() == true && item.CanExecute()) item.ClickAction();
		}

		private ToolStripMenuItem CreateMenuItem(TaskTrayIconItem item)
		{
			var menu = new ToolStripMenuItem(item.Text, null, (sender, args) => item.ClickAction())
			{
				Enabled = item.CanExecute(),
				Tag = item,
			};
			return menu;
		}

		private void ReloadMenuAvailability()
		{
			if (this._notifyIcon?.ContextMenuStrip == null) return;
			foreach (ToolStripItem menu in this._notifyIcon.ContextMenuStrip.Items)
				if (menu.Tag is TaskTrayIconItem item) menu.Enabled = item.CanExecute();
		}

		private void ChangeText(string newText)
		{
			if (this._notifyIcon != null) this._notifyIcon.Text = newText;
		}

		private void ChangeIcon(Icon newIcon)
		{
			if (this._icon != this._darkIcon && this._icon != this._lightIcon) this._icon?.Dispose();
			this._icon = newIcon;
			if (this._notifyIcon != null) this._notifyIcon.Icon = newIcon;
		}

		public void Dispose()
		{
			WindowsTheme.SystemTheme.Changed -= this.OnSystemThemeChanged;
			WindowsTheme.Accent.Changed -= this.OnAccentChanged;
			WindowsTheme.ColorPrevalence.Changed -= this.OnColorPrevalenceChanged;
			if (this._runtime != null) this._runtime.StateChanged -= this.OnDesktopStateChanged;
			if (this._notifyIcon != null) this._notifyIcon.MouseClick -= this.OnIconClick;
			this._notifyIcon?.Dispose();
			this._lightIcon?.Dispose();
			this._icon?.Dispose();
		}
	}
	public class TaskTrayIconItem
	{
		public string Text { get; }

		public Action ClickAction { get; }

		public Func<bool> CanDisplay { get; }
		public Func<bool> CanExecute { get; }

		public TaskTrayIconItem(string text, Action clickAction) : this(text, clickAction, () => true, () => true) { }

		public TaskTrayIconItem(string text, Action clickAction, Func<bool> canDisplay) : this(text, clickAction, canDisplay, () => true) { }

		public TaskTrayIconItem(string text, Action clickAction, Func<bool> canDisplay, Func<bool> canExecute)
		{
			this.Text = text;
			this.ClickAction = clickAction;
			this.CanDisplay = canDisplay;
			this.CanExecute = canExecute;
		}
	}

	public class TaskTrayBaloon
	{
		private readonly TaskTrayIcon _icon;

		public string Title { get; set; }

		public string Text { get; set; }

		public TimeSpan Timespan { get; set; }

		internal TaskTrayBaloon(TaskTrayIcon icon)
		{
			this._icon = icon;
		}

		public void Show()
		{
			this._icon.ShowBaloon(this);
		}
	}
}
