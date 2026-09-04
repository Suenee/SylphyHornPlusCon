using System;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SylphyHorn.Serialization;

namespace SylphyHorn.Services
{
	public enum WebSocketConnectionState
	{
		Disconnected,
		Connecting,
		Connected,
		Error,
	}

	public sealed class WebSocketConnectionStateChangedEventArgs : EventArgs
	{
		internal WebSocketConnectionStateChangedEventArgs(WebSocketConnectionState state, string message)
		{
			this.State = state;
			this.Message = message;
		}

		public WebSocketConnectionState State { get; }
		public string Message { get; }
	}

	public sealed class WebSocketConnectionService : IDisposable
	{
		private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
		private ClientWebSocket _client;
		private CancellationTokenSource _lifetimeCts;
		private Task _receiveTask;
		private WebSocketConnectionState _state = WebSocketConnectionState.Disconnected;
		private string _statusMessage = "Disconnected";
		private bool _disposed;

		public static WebSocketConnectionService Instance { get; } = new WebSocketConnectionService();

		private WebSocketConnectionService()
		{
			if (Application.Current != null) Application.Current.Exit += this.OnApplicationExit;
		}

		public event EventHandler<WebSocketConnectionStateChangedEventArgs> StateChanged;

		public WebSocketConnectionState State => this._state;
		public string StatusMessage => this._statusMessage;
		public bool IsConnected => this._state == WebSocketConnectionState.Connected;

		public async Task ConnectAsync(string address, int port, string socketBox, string apiKey)
		{
			await this._gate.WaitAsync().ConfigureAwait(false);
			try
			{
				if (this._disposed) throw new ObjectDisposedException(nameof(WebSocketConnectionService));
				if (this.IsConnected || this._state == WebSocketConnectionState.Connecting) return;
				if (string.IsNullOrWhiteSpace(address))
				{
					this.SetState(WebSocketConnectionState.Error, "IP is required.");
					return;
				}
				if (port < 1 || port > 65535)
				{
					this.SetState(WebSocketConnectionState.Error, "Socket port must be between 1 and 65535.");
					return;
				}

				this.CleanupClient();
				this.SetState(WebSocketConnectionState.Connecting, "Connecting...");
				this._lifetimeCts = new CancellationTokenSource();
				this._client = new ClientWebSocket();
				var uri = BuildUri(address.Trim(), port);
				try
				{
					await this._client.ConnectAsync(uri, this._lifetimeCts.Token).ConfigureAwait(false);
					if (this._client.State != WebSocketState.Open)
					{
						this.SetState(WebSocketConnectionState.Error, "WebSocket did not reach the open state.");
						this.CleanupClient();
						return;
					}

					DesktopControlService.Instance.SetEnabled(true);
					this.SetState(WebSocketConnectionState.Connected, "Connected");
					LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "Connected", "WebSocket transport connected.", details: $"Endpoint={uri};SocketBox={socketBox ?? string.Empty}");
					this._receiveTask = Task.Run(() => this.ReceiveLoopAsync(this._client, this._lifetimeCts.Token));
				}
				catch (OperationCanceledException)
				{
					DesktopControlService.Instance.SetEnabled(false);
					this.SetState(WebSocketConnectionState.Disconnected, "Disconnected");
					this.CleanupClient();
				}
				catch (Exception ex)
				{
					DesktopControlService.Instance.SetEnabled(false);
					this.SetState(WebSocketConnectionState.Error, ex.Message);
					LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "ConnectFailed", "WebSocket connection failed.", details: ex.ToString());
					this.CleanupClient();
				}
			}
			finally
			{
				this._gate.Release();
			}
		}

		public async Task DisconnectAsync()
		{
			await this._gate.WaitAsync().ConfigureAwait(false);
			try
			{
				DesktopControlService.Instance.SetEnabled(false);
				this._lifetimeCts?.Cancel();
				var client = this._client;
				if (client != null && (client.State == WebSocketState.Open || client.State == WebSocketState.CloseReceived))
				{
					try
					{
						using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
						await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", timeout.Token).ConfigureAwait(false);
					}
					catch { }
				}
				this.CleanupClient();
				this.SetState(WebSocketConnectionState.Disconnected, "Disconnected");
				LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "Disconnected", "WebSocket transport disconnected.");
			}
			finally
			{
				this._gate.Release();
			}
		}

		private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationToken cancellationToken)
		{
			var buffer = new byte[1024];
			try
			{
				while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
				{
					var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
					if (result.MessageType == WebSocketMessageType.Close) break;
					// VPP payload handling is intentionally not implemented in 0.37.
				}
			}
			catch (OperationCanceledException) { return; }
			catch (Exception ex)
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ReceiveStopped", "WebSocket receive loop stopped.", details: ex.ToString());
					this.SetState(WebSocketConnectionState.Error, ex.Message);
				}
			}
			finally
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					DesktopControlService.Instance.SetEnabled(false);
					if (this._state != WebSocketConnectionState.Error) this.SetState(WebSocketConnectionState.Disconnected, "Disconnected by remote endpoint");
					this.CleanupClient(client);
				}
			}
		}

		private static Uri BuildUri(string address, int port)
		{
			if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
			{
				var supplied = new Uri(address, UriKind.Absolute);
				var builder = new UriBuilder(supplied) { Port = port };
				return builder.Uri;
			}
			return new UriBuilder("ws", address, port, "/").Uri;
		}

		private void SetState(WebSocketConnectionState state, string message)
		{
			this._state = state;
			this._statusMessage = string.IsNullOrWhiteSpace(message) ? state.ToString() : message;
			var handlers = this.StateChanged;
			if (handlers == null) return;
			var args = new WebSocketConnectionStateChangedEventArgs(this._state, this._statusMessage);
			foreach (EventHandler<WebSocketConnectionStateChangedEventArgs> handler in handlers.GetInvocationList())
			{
				try { handler(this, args); }
				catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "StateSubscriberFailed", "A WebSocket state subscriber failed.", details: ex.ToString()); }
			}
		}

		private void CleanupClient(ClientWebSocket expected = null)
		{
			if (expected != null && !ReferenceEquals(this._client, expected)) return;
			try { this._client?.Dispose(); } catch { }
			this._client = null;
			try { this._lifetimeCts?.Dispose(); } catch { }
			this._lifetimeCts = null;
			this._receiveTask = null;
		}

		public static string ProtectApiKey(string value)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return Dpapi.Protect(value);
		}

		public static string UnprotectApiKey(string value)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;
			try { return Dpapi.Unprotect(value); }
			catch { return string.Empty; }
		}

		private void OnApplicationExit(object sender, ExitEventArgs e) => this.Dispose();

		public void Dispose()
		{
			if (this._disposed) return;
			this._disposed = true;
			DesktopControlService.Instance.SetEnabled(false);
			try { this._lifetimeCts?.Cancel(); } catch { }
			this.CleanupClient();
			this._gate.Dispose();
			if (Application.Current != null) Application.Current.Exit -= this.OnApplicationExit;
		}

		private static class Dpapi
		{
			[StructLayout(LayoutKind.Sequential)]
			private struct DataBlob
			{
				public int Size;
				public IntPtr Data;
			}

			[DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

			[DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

			[DllImport("Kernel32.dll", SetLastError = true)]
			private static extern IntPtr LocalFree(IntPtr memory);

			internal static string Protect(string value)
			{
				var bytes = Encoding.UTF8.GetBytes(value);
				var input = CreateBlob(bytes);
				try
				{
					if (!CryptProtectData(ref input, "SylphyHornPlusCon WebSocket API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
						throw new InvalidOperationException("Windows could not protect the API key.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
					try
					{
						var protectedBytes = new byte[output.Size];
						Marshal.Copy(output.Data, protectedBytes, 0, output.Size);
						return Convert.ToBase64String(protectedBytes);
					}
					finally { if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
				}
				finally { if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data); }
			}

			internal static string Unprotect(string value)
			{
				var bytes = Convert.FromBase64String(value);
				var input = CreateBlob(bytes);
				try
				{
					if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
						throw new InvalidOperationException("Windows could not unprotect the API key.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
					try
					{
						var plainBytes = new byte[output.Size];
						Marshal.Copy(output.Data, plainBytes, 0, output.Size);
						return Encoding.UTF8.GetString(plainBytes);
					}
					finally { if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
				}
				finally { if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data); }
			}

			private static DataBlob CreateBlob(byte[] bytes)
			{
				var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
				Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
				return blob;
			}
		}
	}
}
