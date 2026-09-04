using System;
using System.Globalization;
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
		private readonly PasswordBox _apiKeyBox;
		private readonly Button _connectButton;
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

			this._apiKeyBox = new PasswordBox
			{
				Height = 32,
				Padding = new Thickness(8, 4, 8, 4),
				Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)),
				Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(72, 79, 89)),
				BorderThickness = new Thickness(1),
			};
			this._apiKeyBox.Password = WebSocketConnectionService.UnprotectApiKey(Settings.General.WebSocketApiKeyProtected.Value);
			this._apiKeyBox.PasswordChanged += (_, _) => Settings.General.WebSocketApiKeyProtected.Value = WebSocketConnectionService.ProtectApiKey(this._apiKeyBox.Password);
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
			this.ApplyState(WebSocketConnectionService.Instance.State, WebSocketConnectionService.Instance.StatusMessage);
		}

		private async void OnConnectClick(object sender, RoutedEventArgs e)
		{
			try
			{
				if (WebSocketConnectionService.Instance.IsConnected)
				{
					await WebSocketConnectionService.Instance.DisconnectAsync();
					return;
				}

				if (!int.TryParse(this._portBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port)) port = 0;
				await WebSocketConnectionService.Instance.ConnectAsync(
					this._addressBox.Text,
					port,
					this._socketBox.Text,
					this._apiKeyBox.Password);
			}
			catch (Exception ex)
			{
				LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "UiCommandFailed", "WebSocket UI command failed.", details: ex.ToString());
			}
		}

		private void OnConnectionStateChanged(object sender, WebSocketConnectionStateChangedEventArgs e)
		{
			if (this.Dispatcher.CheckAccess()) this.ApplyState(e.State, e.Message);
			else _ = this.Dispatcher.BeginInvoke((Action)(() => this.ApplyState(e.State, e.Message)));
		}

		private void ApplyState(WebSocketConnectionState state, string message)
		{
			this._statusText.Text = string.IsNullOrWhiteSpace(message) ? state.ToString() : message;
			this._connectButton.Content = state == WebSocketConnectionState.Connected ? "Disconnect" : "Connect";
			this._connectButton.IsEnabled = state != WebSocketConnectionState.Connecting;
			switch (state)
			{
				case WebSocketConnectionState.Connected:
					this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(61, 190, 105));
					break;
				case WebSocketConnectionState.Connecting:
					this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(235, 184, 54));
					break;
				case WebSocketConnectionState.Error:
					this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(220, 74, 74));
					break;
				default:
					this._statusLight.Fill = new SolidColorBrush(Color.FromRgb(122, 128, 138));
					break;
			}
		}

		private static TextBox CreateTextBox(string value)
			=> new TextBox
			{
				Text = value,
				Height = 32,
				Padding = new Thickness(8, 4, 8, 4),
				Background = new SolidColorBrush(Color.FromRgb(28, 32, 38)),
				Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(72, 79, 89)),
				BorderThickness = new Thickness(1),
			};

		private static void AddLabel(Grid grid, string text, int row)
		{
			var label = new TextBlock
			{
				Text = text,
				Foreground = new SolidColorBrush(Color.FromRgb(210, 215, 222)),
				FontSize = 14,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 16, 12),
			};
			Grid.SetRow(label, row);
			Grid.SetColumn(label, 0);
			grid.Children.Add(label);
		}

		private static void AddRow(Grid grid, string label, Control control, int row)
		{
			AddLabel(grid, label, row);
			control.Margin = new Thickness(0, 0, 0, 12);
			Grid.SetRow(control, row);
			Grid.SetColumn(control, 1);
			grid.Children.Add(control);
		}

		public void Dispose()
		{
			if (this._disposed) return;
			this._disposed = true;
			WebSocketConnectionService.Instance.StateChanged -= this.OnConnectionStateChanged;
			this._connectButton.Click -= this.OnConnectClick;
		}
	}
}
