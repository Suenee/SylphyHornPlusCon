using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SylphyHorn.Serialization;
using SylphyHorn.Services;

namespace SylphyHorn.UI
{
	internal sealed class WebSocketSettingsView : UserControl, IDisposable
	{
		private readonly Ellipse _statusLight;
		private readonly TextBlock _statusText;
		private readonly TextBox _addressBox;
		private readonly TextBox _portBox;
		private readonly TextBox _socketBox;
		private readonly TextBox _apiKeyBox;
		private readonly Button _connectButton;
		private bool _negotiationDialogOpen;
		private bool _disposed;

		internal WebSocketSettingsView()
		{
			var root = new Grid { Margin = new Thickness(24) };
			root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
			root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
			for (var i = 0; i < 6; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			this._statusLight = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 9, 0) };
			this._statusText = new TextBlock { Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
			statusPanel.Children.Add(this._statusLight);
			statusPanel.Children.Add(this._statusText);
			AddLabel(root, "Status:", 0);
			Grid.SetRow(statusPanel, 0);
			Grid.SetColumn(statusPanel, 1);
			root.Children.Add(statusPanel);

			this._addressBox = CreateTextBox(Settings.General.WebSocketAddress.Value ?? string.Empty);
			this._addressBox.TextChanged += (_, _) => Settings.General.WebSocketAddress.Value = this._addressBox.Text.Trim();
			AddRow(root, "IP:", this._addressBox, 1);

			this._portBox = CreateTextBox(Settings.General.WebSocketPort.Value > 0 ? Settings.General.WebSocketPort.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
			this._portBox.TextChanged += (_, _) =>
			{
				Settings.General.WebSocketPort.Value = int.TryParse(this._portBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0;
			};
			AddRow(root, "Socket port:", this._portBox, 2);

			this._socketBox = CreateTextBox(Settings.General.WebSocketSocketBox.Value ?? string.Empty);
			this._socketBox.TextChanged += (_, _) => Settings.General.WebSocketSocketBox.Value = this._socketBox.Text.Trim();
			AddRow(root, "Socket box:", this._socketBox, 3);

			this._apiKeyBox = CreateTextBox(WebSocketConnectionService.UnprotectApiKey(Settings.General.WebSocketApiKeyProtected.Value));
			this._apiKeyBox.TextChanged += (_, _) => Settings.General.WebSocketApiKeyProtected.Value = WebSocketConnectionService.ProtectApiKey(this._apiKeyBox.Text);
			AddRow(root, "API KEY:", this._apiKeyBox, 4);

			this._connectButton = new Button
			{
				Width = 130,
				Height = 36,
				Margin = new Thickness(0, 18, 0, 0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Foreground = Brushes.White,
				Background = new SolidColorBrush(Color.FromRgb(40, 91, 136)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(65, 108, 148)),
				BorderThickness = new Thickness(1),
			};
			this._connectButton.Click += this.OnConnectClick;
			Grid.SetRow(this._connectButton, 5);
			Grid.SetColumn(this._connectButton, 1);
			root.Children.Add(this._connectButton);

			this.Content = root;
			WebSocketConnectionService.Instance.StateChanged += this.OnConnectionStateChanged;
			WebSocketConnectionService.Instance.ReplacementNegotiationRequested += this.OnReplacementNegotiationRequested;
			this.ApplyState(WebSocketConnectionService.Instance.State, WebSocketConnectionService.Instance.StatusMessage);
		}

		private async void OnConnectClick(object sender, RoutedEventArgs e)
		{
			try
			{
				var service = WebSocketConnectionService.Instance;
				if (service.IsConnected) { await service.DisconnectAsync(); return; }
				if (service.IsNegotiating) { await service.CancelConnectionNegotiationAsync(); return; }
				if (!int.TryParse(this._portBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port)) port = 0;
				await service.ConnectAsync(this._addressBox.Text, port, this._socketBox.Text, this._apiKeyBox.Text);
			}
			catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "UiCommandFailed", "WebSocket UI command failed.", details: ex.ToString()); }
		}

		private void OnConnectionStateChanged(object sender, WebSocketConnectionStateChangedEventArgs e)
		{
			if (this.Dispatcher.CheckAccess()) this.ApplyState(e.State, e.Message);
			else _ = this.Dispatcher.BeginInvoke((Action)(() => this.ApplyState(e.State, e.Message)));
		}

		private void OnReplacementNegotiationRequested(object sender, ReplacementNegotiationEventArgs e)
		{
			if (this.Dispatcher.CheckAccess()) _ = this.ShowReplacementNegotiationAsync(e);
			else _ = this.Dispatcher.BeginInvoke((Action)(() => _ = this.ShowReplacementNegotiationAsync(e)));
		}

		private async System.Threading.Tasks.Task ShowReplacementNegotiationAsync(ReplacementNegotiationEventArgs e)
		{
			if (this._disposed || this._negotiationDialogOpen) return;
			this._negotiationDialogOpen = true;
			try
			{
				var dialog = new ConnectionReplacementDialog(e) { Owner = Window.GetWindow(this) };
				var accepted = dialog.ShowDialog() == true;
				if (accepted && dialog.SelectedConnection != null) await WebSocketConnectionService.Instance.ReplaceConnectionAsync(dialog.SelectedConnection.ConnectionId);
				else if (WebSocketConnectionService.Instance.IsNegotiating) await WebSocketConnectionService.Instance.CancelConnectionNegotiationAsync();
			}
			finally { this._negotiationDialogOpen = false; }
		}

		private void ApplyState(WebSocketConnectionState state, string message)
		{
			this._statusText.Text = string.IsNullOrWhiteSpace(message) ? state.ToString() : message;
			this._connectButton.Content = state == WebSocketConnectionState.Connected ? "Disconnect" : state == WebSocketConnectionState.Negotiating ? "Cancel" : "Connect";
			this._connectButton.IsEnabled = state != WebSocketConnectionState.Connecting;
			switch (state)
			{
				case WebSocketConnectionState.Connected: this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(61, 190, 105)); break;
				case WebSocketConnectionState.Connecting:
				case WebSocketConnectionState.Negotiating: this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(235, 184, 54)); break;
				case WebSocketConnectionState.Error: this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(220, 74, 74)); break;
				default: this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(122, 128, 138)); break;
			}
		}

		private static TextBox CreateTextBox(string value) => new TextBox
		{
			Text = value, Height = 32, Padding = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)),
			Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(72, 79, 89)), BorderThickness = new Thickness(1),
		};

		private static void AddLabel(Grid grid, string text, int row)
		{
			var label = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(210, 215, 222)), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 12) };
			Grid.SetRow(label, row); Grid.SetColumn(label, 0); grid.Children.Add(label);
		}

		private static void AddRow(Grid grid, string label, Control control, int row)
		{
			AddLabel(grid, label, row); control.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(control, row); Grid.SetColumn(control, 1); grid.Children.Add(control);
		}

		public void Dispose()
		{
			if (this._disposed) return;
			this._disposed = true;
			WebSocketConnectionService.Instance.StateChanged -= this.OnConnectionStateChanged;
			WebSocketConnectionService.Instance.ReplacementNegotiationRequested -= this.OnReplacementNegotiationRequested;
			this._connectButton.Click -= this.OnConnectClick;
		}

		private sealed class ConnectionReplacementDialog : Window
		{
			private readonly ListBox _connections;
			internal ConnectionReplacementDialog(ReplacementNegotiationEventArgs negotiation)
			{
				this.Title = "Connection limit reached"; this.Width = 560; this.Height = 390; this.WindowStartupLocation = WindowStartupLocation.CenterOwner; this.ResizeMode = ResizeMode.NoResize;
				this.Background = new SolidColorBrush(Color.FromRgb(24, 27, 32)); this.Foreground = Brushes.White;
				var root = new DockPanel { Margin = new Thickness(22) };
				var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom);
				var cancel = new Button { Content = "Cancel", Width = 100, Height = 34, Margin = new Thickness(8, 0, 0, 0) };
				var replace = new Button { Content = "Replace selected", Width = 140, Height = 34, IsDefault = true };
				cancel.Click += (_, _) => { this.DialogResult = false; this.Close(); };
				replace.Click += (_, _) => { if (this._connections.SelectedItem is SocketBoxConnectionInfo) { this.DialogResult = true; this.Close(); } };
				buttons.Children.Add(replace); buttons.Children.Add(cancel); root.Children.Add(buttons);
				var content = new StackPanel();
				content.Children.Add(new TextBlock { Text = $"This Socket Box allows a maximum of {negotiation.MaxConnections} connection(s). Select the existing connection that may be replaced.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = Brushes.White });
				if (!string.IsNullOrWhiteSpace(negotiation.ExpiresAt)) content.Children.Add(new TextBlock { Text = $"Negotiation expires: {negotiation.ExpiresAt}", Foreground = new SolidColorBrush(Color.FromRgb(190, 195, 202)), Margin = new Thickness(0, 0, 0, 12) });
				this._connections = new ListBox { Height = 220, Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(72, 79, 89)), DisplayMemberPath = nameof(SocketBoxConnectionInfo.DisplayName), ItemsSource = negotiation.Connections.ToArray() };
				if (negotiation.Connections.Count > 0) this._connections.SelectedIndex = 0;
				content.Children.Add(this._connections); root.Children.Add(content); this.Content = root;
			}
			internal SocketBoxConnectionInfo SelectedConnection => this._connections.SelectedItem as SocketBoxConnectionInfo;
		}
	}
}
