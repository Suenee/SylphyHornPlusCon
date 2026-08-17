using System;
using SylphyHorn.UI.Bindings;
using WindowsDesktop;

namespace SylphyHorn.UI
{
	partial class SettingsWindow
	{
		public static SettingsWindow Instance { get; set; }

		public SettingsWindow()
		{
			this.InitializeComponent();
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
			(this.DataContext as IDisposable)?.Dispose();
		}
	}
}
