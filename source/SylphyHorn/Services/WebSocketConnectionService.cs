using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
		Negotiating,
		Connected,
		Error,
	}

	public sealed class WebSocketConnectionStateChangedEventArgs : EventArgs
	{
		internal WebSocketConnectionStateChangedEventArgs(WebSocketConnectionState state, string message) { this.State = state; this.Message = message; }
		public WebSocketConnectionState State { get; }
		public string Message { get; }
	}

	public sealed class SocketBoxConnectionInfo
	{
		internal SocketBoxConnectionInfo(string connectionId, string socketBox, string hostName, string ip, string service, string connectedAt)
		{
			this.ConnectionId = connectionId; this.SocketBox = socketBox; this.HostName = hostName; this.Ip = ip; this.Service = service; this.ConnectedAt = connectedAt;
		}
		public string ConnectionId { get; }
		public string SocketBox { get; }
		public string HostName { get; }
		public string Ip { get; }
		public string Service { get; }
		public string ConnectedAt { get; }
		public string DisplayName => !string.IsNullOrWhiteSpace(this.HostName) ? $"{this.HostName} ({this.Ip})" : this.Ip;
	}

	public sealed class ReplacementNegotiationEventArgs : EventArgs
	{
		internal ReplacementNegotiationEventArgs(int maxConnections, string expiresAt, IReadOnlyList<SocketBoxConnectionInfo> connections)
		{
			this.MaxConnections = maxConnections; this.ExpiresAt = expiresAt; this.Connections = connections ?? Array.Empty<SocketBoxConnectionInfo>();
		}
		public int MaxConnections { get; }
		public string ExpiresAt { get; }
		public IReadOnlyList<SocketBoxConnectionInfo> Connections { get; }
	}

	public sealed class WebSocketConnectionService : IDisposable
	{
		private enum ConnectionAttemptResult
		{
			Failed,
			Admitted,
			Negotiating,
		}

		private const int VppVersion = 1;
		private const string ServerSocketBox = "server";
		private const int DefaultHeartbeatMs = 30000;
		private const int HeartbeatGraceMs = 5000;
		private const int MaxMessageBytes = 1024 * 1024;
		private static readonly string AppVersion = GetApplicationVersion();
		private static readonly Regex ApiKeyRegex = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
		private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>(StringComparer.Ordinal);
		private readonly VppDesktopAdapter _desktopAdapter = new VppDesktopAdapter(DesktopControlService.Instance);
		private readonly object _reconnectSync = new object();
		private ClientWebSocket _client;
		private CancellationTokenSource _lifetimeCts;
		private CancellationTokenSource _reconnectCts;
		private Task _receiveTask;
		private Task _heartbeatTask;
		private Task _reconnectTask;
		private WebSocketConnectionState _state = WebSocketConnectionState.Disconnected;
		private string _statusMessage = "Disconnected";
		private string _socketBox;
		private string _peerSocketBox;
		private string _serverDisconnectReason;
		private string _lastAddress;
		private int _lastPort;
		private string _lastSocketBox;
		private string _lastApiKey;
		private int _heartbeatIntervalMs = DefaultHeartbeatMs;
		private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
		private bool _disposed;

		public static WebSocketConnectionService Instance { get; } = new WebSocketConnectionService();
		private WebSocketConnectionService()
		{
			if (Application.Current != null) Application.Current.Exit += this.OnApplicationExit;
			DesktopControlService.Instance.StateChanged += this.OnDesktopStateChanged;
		}

		public event EventHandler<WebSocketConnectionStateChangedEventArgs> StateChanged;
		public event EventHandler<ReplacementNegotiationEventArgs> ReplacementNegotiationRequested;
		public WebSocketConnectionState State => this._state;
		public string StatusMessage => this._statusMessage;
		public bool IsConnected => this._state == WebSocketConnectionState.Connected;
		public bool IsNegotiating => this._state == WebSocketConnectionState.Negotiating;

		public async Task ConnectAsync(string address, int port, string socketBox, string apiKey)
		{
			this.CancelReconnectSeries();
			this.RememberConnectionSettings(address, port, socketBox, apiKey);
			var result = await this.ConnectCoreAsync(address, port, socketBox, apiKey, CancellationToken.None).ConfigureAwait(false);
			if (result == ConnectionAttemptResult.Admitted)
				await this.PersistConnectionPreferenceAsync(true, address, port, socketBox, apiKey).ConfigureAwait(false);
		}

		public async Task RestoreDesiredConnectionAsync()
		{
			if (this._disposed || !Settings.General.WebSocketAutoConnect.Value) return;

			var address = Settings.General.WebSocketAddress.Value ?? string.Empty;
			var port = Settings.General.WebSocketPort.Value;
			var socketBox = Settings.General.WebSocketSocketBox.Value ?? string.Empty;
			var apiKey = UnprotectApiKey(Settings.General.WebSocketApiKeyProtected.Value);
			this.RememberConnectionSettings(address, port, socketBox, apiKey);

			if (!TryValidateConnectionSettings(address, port, socketBox, apiKey, out var validationError))
			{
				this.SetState(WebSocketConnectionState.Error, "Unable to reconnect");
				LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "AutoConnectSkipped", "Saved WebSocket connection cannot be restored because its settings are invalid.", details: validationError);
				return;
			}

			LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "AutoConnectStarted", "Restoring the previously desired SUB connection.", details: $"Endpoint={GetDisplayUri(BuildUri(address.Trim(), port, socketBox.Trim(), apiKey.Trim()))};SocketBox={socketBox.Trim()}");
			var result = await this.ConnectCoreAsync(address, port, socketBox, apiKey, CancellationToken.None).ConfigureAwait(false);
			if (result == ConnectionAttemptResult.Admitted)
			{
				LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "AutoConnectRestored", "Saved SUB connection restored successfully.");
				return;
			}
			if (result == ConnectionAttemptResult.Failed) this.StartReconnectSeries("startup");
		}

		public async Task ReplaceConnectionAsync(string connectionId)
		{
			if (string.IsNullOrWhiteSpace(connectionId)) return;
			await this._gate.WaitAsync().ConfigureAwait(false);
			try
			{
				if (this._state != WebSocketConnectionState.Negotiating || this._lifetimeCts == null) return;
				var response = await this.SendServerCallAsync("replaceConnection", new { connectionId = connectionId.Trim() }, TimeSpan.FromSeconds(10), this._lifetimeCts.Token).ConfigureAwait(false);
				var result = await this.ApplyAdmissionResponseAsync(response).ConfigureAwait(false);
				if (result == ConnectionAttemptResult.Admitted && this.HasRememberedConnectionSettings())
					await this.PersistConnectionPreferenceAsync(true, this._lastAddress, this._lastPort, this._lastSocketBox, this._lastApiKey).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				this.SetState(WebSocketConnectionState.Error, "Unable to connect");
				LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "ReplacementFailed", "VPP connection replacement failed.", details: ex.ToString());
				this.CleanupClient();
			}
			finally { this._gate.Release(); }
		}

		public async Task CancelConnectionNegotiationAsync()
		{
			await this._gate.WaitAsync().ConfigureAwait(false);
			try
			{
				if (this._state == WebSocketConnectionState.Negotiating && this._lifetimeCts != null)
				{
					try { await this.SendServerCallAsync("cancelConnectionNegotiation", new { }, TimeSpan.FromSeconds(5), this._lifetimeCts.Token).ConfigureAwait(false); }
					catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "NegotiationCancelFailed", "Connection negotiation cancel request failed.", details: ex.ToString()); }
				}
			}
			finally { this._gate.Release(); }
			await this.DisconnectAsync().ConfigureAwait(false);
		}

		public async Task DisconnectAsync()
		{
			this.CancelReconnectSeries();
			await this.PersistConnectionPreferenceAsync(false, null, 0, null, null).ConfigureAwait(false);
			await this.DisconnectCoreAsync().ConfigureAwait(false);
		}

		public async Task DisconnectForShutdownAsync()
		{
			this.CancelReconnectSeries();
			await this.DisconnectCoreAsync().ConfigureAwait(false);
		}

		private async Task DisconnectCoreAsync()
		{
			await this._gate.WaitAsync().ConfigureAwait(false);
			try
			{
				DesktopControlService.Instance.SetEnabled(false);
				this._serverDisconnectReason = null;
				var client = this._client;
				var cts = this._lifetimeCts;
				if (client != null && client.State == WebSocketState.Open && !string.IsNullOrWhiteSpace(this._peerSocketBox))
				{
					try { await this.SendEventAsync("disconnecting", new { reason = "user" }, this._peerSocketBox, false, cts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
					catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Warning, "VPP", "DisconnectingSendFailed", "Graceful VPP disconnect notification could not be sent.", details: ex.Message); }
				}
				if (client != null && (client.State == WebSocketState.Open || client.State == WebSocketState.CloseReceived))
				{
					try
					{
						using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
						await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", timeout.Token).ConfigureAwait(false);
					}
					catch { }
				}
				try { cts?.Cancel(); } catch { }
				this.CleanupClient();
				this.SetState(WebSocketConnectionState.Disconnected, "Disconnected");
				LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "Disconnected", "WebSocket/VPP session disconnected.");
			}
			finally { this._gate.Release(); }
		}

		private async Task<ConnectionAttemptResult> ConnectCoreAsync(string address, int port, string socketBox, string apiKey, CancellationToken cancellationToken)
		{
			await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (this._disposed) throw new ObjectDisposedException(nameof(WebSocketConnectionService));
				if (this._state == WebSocketConnectionState.Connected) return ConnectionAttemptResult.Admitted;
				if (this._state == WebSocketConnectionState.Connecting || this._state == WebSocketConnectionState.Negotiating) return ConnectionAttemptResult.Failed;
				if (!TryValidateConnectionSettings(address, port, socketBox, apiKey, out var validationError))
				{
					this.SetState(WebSocketConnectionState.Error, "Unable to connect");
					LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "ConnectValidationFailed", "WebSocket connection validation failed.", details: validationError);
					return ConnectionAttemptResult.Failed;
				}

				this.CleanupClient();
				this._socketBox = socketBox.Trim();
				this._peerSocketBox = null;
				this._serverDisconnectReason = null;
				this._heartbeatIntervalMs = DefaultHeartbeatMs;
				this._lastActivity = DateTimeOffset.UtcNow;
				this.SetState(WebSocketConnectionState.Connecting, "Connecting...");
				this._lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				this._client = new ClientWebSocket();
				var uri = BuildUri(address.Trim(), port, this._socketBox, apiKey.Trim());
				LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "ConnectStarted", "Starting SUB WebSocket connection.", details: $"Endpoint={GetDisplayUri(uri)};SocketBox={this._socketBox};ApiKeyPresent={!string.IsNullOrWhiteSpace(apiKey)}");
				try
				{
					await this._client.ConnectAsync(uri, this._lifetimeCts.Token).ConfigureAwait(false);
					if (this._client.State != WebSocketState.Open)
					{
						this.SetState(WebSocketConnectionState.Error, "Unable to connect");
						LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "ConnectFailed", "WebSocket did not reach the open state.", details: $"Endpoint={GetDisplayUri(uri)};State={this._client.State};SocketBox={this._socketBox}");
						this.CleanupClient();
						return ConnectionAttemptResult.Failed;
					}

					this._receiveTask = Task.Run(() => this.ReceiveLoopAsync(this._client, this._lifetimeCts.Token));
					LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "TransportConnected", "Authenticated WebSocket transport connected; starting VPP admission.", details: $"Endpoint={GetDisplayUri(uri)};SocketBox={this._socketBox}");
					var registration = await this.SendServerCallAsync("registerConnection", new { hostName = Environment.MachineName }, TimeSpan.FromSeconds(10), this._lifetimeCts.Token).ConfigureAwait(false);
					return await this.ApplyAdmissionResponseAsync(registration).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					DesktopControlService.Instance.SetEnabled(false);
					this.SetState(WebSocketConnectionState.Disconnected, "Disconnected");
					this.CleanupClient();
					return ConnectionAttemptResult.Failed;
				}
				catch (Exception ex)
				{
					DesktopControlService.Instance.SetEnabled(false);
					this.SetState(WebSocketConnectionState.Error, "Unable to connect");
					LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "ConnectFailed", "WebSocket/VPP connection failed.", details: $"Endpoint={GetDisplayUri(uri)};SocketBox={this._socketBox}{Environment.NewLine}{ex}");
					this.CleanupClient();
					return ConnectionAttemptResult.Failed;
				}
			}
			finally { this._gate.Release(); }
		}

		private async Task<ConnectionAttemptResult> ApplyAdmissionResponseAsync(JsonElement response)
		{
			if (!TryGetResult(response, out var result) || !result.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String)
				throw new InvalidOperationException("SUB returned an invalid registerConnection result.");
			var status = statusElement.GetString();
			if (string.Equals(status, "admitted", StringComparison.Ordinal))
			{
				this._serverDisconnectReason = null;
				this.SetState(WebSocketConnectionState.Connected, "Connected");
				DesktopControlService.Instance.SetEnabled(true);
				this.StartHeartbeat();
				LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "VppAdmitted", "VPP connection admitted by SUB.", details: $"SocketBox={this._socketBox}");
				return ConnectionAttemptResult.Admitted;
			}
			if (string.Equals(status, "replacementNegotiation", StringComparison.Ordinal))
			{
				DesktopControlService.Instance.SetEnabled(false);
				this.SetState(WebSocketConnectionState.Negotiating, "Connection limit reached");
				var maxConnections = TryReadInt(result, "maxConnections");
				var expiresAt = TryReadString(result, "expiresAt");
				var connections = ParseConnectionRoster(result);
				LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ReplacementNegotiation", "SUB requires explicit connection replacement before admission.", details: $"SocketBox={this._socketBox};MaxConnections={maxConnections};Connections={connections.Count};ExpiresAt={expiresAt}");
				this.RaiseReplacementNegotiation(new ReplacementNegotiationEventArgs(maxConnections, expiresAt, connections));
				return ConnectionAttemptResult.Negotiating;
			}
			throw new InvalidOperationException($"SUB returned unsupported admission status '{status}'.");
		}

		private async Task<JsonElement> SendServerCallAsync(string method, object args, TimeSpan timeout, CancellationToken cancellationToken)
		{
			var id = Guid.CreateVersion7().ToString("D");
			var message = CreateEnvelope("call", ServerSocketBox, id, new Dictionary<string, object> { ["method"] = method, ["args"] = args, ["expectsResponse"] = true });
			var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!this._pendingRequests.TryAdd(id, tcs)) throw new InvalidOperationException("Duplicate VPP request id.");
			try
			{
				await this.SendJsonAsync(message, cancellationToken).ConfigureAwait(false);
				using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeoutCts.CancelAfter(timeout);
				using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));
				var terminal = await tcs.Task.ConfigureAwait(false);
				if (terminal.TryGetProperty("type", out var type) && type.GetString() == "error")
				{
					var error = terminal.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object ? errorElement : default;
					var code = error.ValueKind == JsonValueKind.Object ? TryReadString(error, "code") : string.Empty;
					var messageText = error.ValueKind == JsonValueKind.Object ? TryReadString(error, "message") : "SUB returned a VPP error.";
					throw new InvalidOperationException(string.IsNullOrWhiteSpace(code) ? messageText : $"{code}: {messageText}");
				}
				return terminal;
			}
			finally { this._pendingRequests.TryRemove(id, out _); }
		}

		private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationToken cancellationToken)
		{
			try
			{
				while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
				{
					var raw = await ReceiveTextMessageAsync(client, cancellationToken).ConfigureAwait(false);
					if (raw == null) break;
					await this.HandleIncomingAsync(raw, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) { return; }
			catch (Exception ex)
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ReceiveStopped", "WebSocket receive loop stopped.", details: ex.ToString());
					this.SetState(WebSocketConnectionState.Error, "Connection lost");
				}
			}
			finally
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					DesktopControlService.Instance.SetEnabled(false);
					if (this._state != WebSocketConnectionState.Error) this.SetState(WebSocketConnectionState.Disconnected, "Disconnected");
					var serverReason = this.TakeServerDisconnectReason();
					this.CleanupClient(client);
					this.HandleTransportEnded(serverReason, "receive-loop");
				}
			}
		}

		private async Task HandleIncomingAsync(string raw, CancellationToken cancellationToken)
		{
			JsonDocument document;
			try { document = JsonDocument.Parse(raw); }
			catch (Exception ex)
			{
				LoggingService.Instance.Write(LogLevel.Warning, "VPP", "InvalidJson", "Invalid JSON received from SUB.", details: ex.Message);
				return;
			}
			using (document)
			{
				var message = document.RootElement;
				if (!TryValidateEnvelope(message, out var validationError))
				{
					LoggingService.Instance.Write(LogLevel.Warning, "VPP", "InvalidEnvelope", "Invalid VPP envelope received.", details: validationError);
					return;
				}
				this._lastActivity = DateTimeOffset.UtcNow;
				var type = message.GetProperty("type").GetString();
				if ((type == "response" || type == "error") && message.TryGetProperty("correlationId", out var correlationId) && correlationId.ValueKind == JsonValueKind.String)
				{
					var id = correlationId.GetString();
					if (!string.IsNullOrWhiteSpace(id) && this._pendingRequests.TryGetValue(id, out var pending)) pending.TrySetResult(message.Clone());
					return;
				}

				var recipient = message.GetProperty("recipient").GetString();
				var from = message.GetProperty("from").GetString();
				var learnedPeer = false;
				if (this._state == WebSocketConnectionState.Connected && string.Equals(recipient, this._socketBox, StringComparison.Ordinal) && !string.Equals(from, ServerSocketBox, StringComparison.Ordinal))
				{
					if (string.IsNullOrWhiteSpace(this._peerSocketBox))
					{
						this._peerSocketBox = from;
						learnedPeer = true;
					}
					else if (!string.Equals(this._peerSocketBox, from, StringComparison.Ordinal))
					{
						LoggingService.Instance.Write(LogLevel.Warning, "VPP", "PeerBindingPreserved", "Ignored an alternate peer for unsolicited state routing because a peer is already bound.", details: $"BoundPeer={this._peerSocketBox};IncomingPeer={from}");
					}
				}

				if (type == "event") await this.HandleEventAsync(message, cancellationToken).ConfigureAwait(false);
				else if (type == "call") await this.HandleCallAsync(message, cancellationToken).ConfigureAwait(false);

				if (learnedPeer)
				{
					LoggingService.Instance.Write(LogLevel.Info, "VPP", "PeerLearned", "Learned the runtime peer Socket Box from admitted VPP traffic.", details: $"Peer={this._peerSocketBox}");
					await this.SendDesktopStateEventSafeAsync(DesktopControlService.Instance.GetState(), cancellationToken).ConfigureAwait(false);
				}
			}
		}

		private async Task HandleCallAsync(JsonElement message, CancellationToken cancellationToken)
		{
			var id = message.GetProperty("id").GetString();
			var from = message.GetProperty("from").GetString();
			var recipient = message.GetProperty("recipient").GetString();
			var expectsResponse = message.TryGetProperty("expectsResponse", out var expects) && expects.ValueKind == JsonValueKind.True;
			if (!string.Equals(recipient, this._socketBox, StringComparison.Ordinal))
			{
				if (expectsResponse) await this.SendErrorAsync(from, id, "INVALID_ROUTING", "The VPP call is not addressed to this SHPC Socket Box.", null, cancellationToken).ConfigureAwait(false);
				return;
			}
			var method = message.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : null;
			var args = message.TryGetProperty("args", out var argsElement) ? argsElement : default;
			var result = await this._desktopAdapter.DispatchAsync(method, args).ConfigureAwait(false);
			LoggingService.Instance.Write(result.Success ? LogLevel.Info : LogLevel.Warning, "VPP", "CallProcessed", result.Success ? "VPP desktop call completed." : "VPP desktop call failed.", details: $"Method={method};Success={result.Success};ErrorCode={result.ErrorCode ?? string.Empty}");
			if (!expectsResponse) return;
			if (result.Success) await this.SendResponseAsync(from, id, result.Result, cancellationToken).ConfigureAwait(false);
			else await this.SendErrorAsync(from, id, result.ErrorCode, result.ErrorMessage, result.ErrorDetails, cancellationToken).ConfigureAwait(false);
		}

		private async Task HandleEventAsync(JsonElement message, CancellationToken cancellationToken)
		{
			var eventName = message.TryGetProperty("event", out var eventElement) && eventElement.ValueKind == JsonValueKind.String ? eventElement.GetString() : null;
			var from = message.GetProperty("from").GetString();
			var recipient = message.GetProperty("recipient").GetString();
			var id = message.GetProperty("id").GetString();
			var expectsResponse = message.TryGetProperty("expectsResponse", out var expects) && expects.ValueKind == JsonValueKind.True;
			if (eventName == "disconnecting" && string.Equals(recipient, this._socketBox, StringComparison.Ordinal))
			{
				var reason = message.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object ? TryReadString(args, "reason") : string.Empty;
				if (string.Equals(from, ServerSocketBox, StringComparison.Ordinal))
				{
					this._serverDisconnectReason = reason;
					DesktopControlService.Instance.SetEnabled(false);
					var decision = WebSocketReconnectPolicy.ForServerDisconnectReason(reason);
					var status = decision == WebSocketReconnectDecision.Retry ? "SUB disconnecting..." : "Disconnected by SUB";
					this.SetState(WebSocketConnectionState.Disconnected, status);
					LoggingService.Instance.Write(LogLevel.Info, "VPP", "ServerDisconnecting", "SUB announced graceful disconnection.", details: $"Reason={reason};ReconnectDecision={decision}");
				}
				else
				{
					if (!WebSocketReconnectPolicy.IsClientDisconnectReason(reason))
						LoggingService.Instance.Write(LogLevel.Warning, "VPP", "PeerDisconnectReasonUnexpected", "Application peer sent an unrecognized client disconnect reason.", details: $"From={from};Reason={reason}");
					LoggingService.Instance.Write(LogLevel.Info, "VPP", "PeerDisconnecting", "Application peer announced graceful disconnection.", details: $"From={from};Reason={reason}");
					if (string.Equals(from, this._peerSocketBox, StringComparison.Ordinal)) this._peerSocketBox = null;
				}
			}
			if (expectsResponse) await this.SendResponseAsync(from, id, new { success = true }, cancellationToken).ConfigureAwait(false);
		}

		private void StartHeartbeat()
		{
			if (this._heartbeatTask != null || this._lifetimeCts == null) return;
			this._heartbeatTask = Task.Run(() => this.HeartbeatLoopAsync(this._lifetimeCts.Token));
		}

		private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
		{
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(Math.Max(5000, this._heartbeatIntervalMs), cancellationToken).ConfigureAwait(false);
					if (DateTimeOffset.UtcNow - this._lastActivity < TimeSpan.FromMilliseconds(this._heartbeatIntervalMs)) continue;
					var ping = await this.SendServerCallAsync("ping", new { }, TimeSpan.FromMilliseconds(HeartbeatGraceMs), cancellationToken).ConfigureAwait(false);
					this.ApplyHeartbeatPolicy(ping);
					this._lastActivity = DateTimeOffset.UtcNow;
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				if (!cancellationToken.IsCancellationRequested)
				{
					LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "HeartbeatFailed", "VPP heartbeat failed; connection is unhealthy.", details: ex.ToString());
					DesktopControlService.Instance.SetEnabled(false);
					this.SetState(WebSocketConnectionState.Error, "Connection lost");
					var client = this._client;
					var serverReason = this.TakeServerDisconnectReason();
					try { this._lifetimeCts?.Cancel(); } catch { }
					this.CleanupClient(client);
					this.HandleTransportEnded(serverReason, "heartbeat");
				}
			}
		}

		private void ApplyHeartbeatPolicy(JsonElement response)
		{
			if (!TryGetResult(response, out var result) || !result.TryGetProperty("heartbeat", out var heartbeat) || heartbeat.ValueKind != JsonValueKind.Object) return;
			var interval = TryReadInt(heartbeat, "intervalMs");
			if (interval >= 5000 && interval <= 3600000) this._heartbeatIntervalMs = interval;
		}

		private void HandleTransportEnded(string serverReason, string trigger)
		{
			if (this._disposed || !Settings.General.WebSocketAutoConnect.Value) return;
			var decision = string.IsNullOrWhiteSpace(serverReason)
				? WebSocketReconnectPolicy.ForUnexpectedTransportLoss()
				: WebSocketReconnectPolicy.ForServerDisconnectReason(serverReason);
			if (decision == WebSocketReconnectDecision.Retry)
			{
				this.StartReconnectSeries(string.IsNullOrWhiteSpace(serverReason) ? trigger : $"server:{serverReason}");
				return;
			}
			LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ReconnectSuspended", "Automatic reconnect was suspended for this run to avoid a connection loop.", details: $"Trigger={trigger};ServerReason={serverReason}");
		}

		private void StartReconnectSeries(string trigger)
		{
			if (this._disposed || !Settings.General.WebSocketAutoConnect.Value || !this.HasRememberedConnectionSettings()) return;
			lock (this._reconnectSync)
			{
				if (this._reconnectTask != null && !this._reconnectTask.IsCompleted) return;
				this._reconnectCts = new CancellationTokenSource();
				var owner = this._reconnectCts;
				this._reconnectTask = Task.Run(() => this.ReconnectLoopAsync(trigger, owner));
			}
		}

		private async Task ReconnectLoopAsync(string trigger, CancellationTokenSource owner)
		{
			try
			{
				var delays = WebSocketReconnectPolicy.RetryDelays;
				for (var index = 0; index < delays.Count; index++)
				{
					if (owner.IsCancellationRequested || !Settings.General.WebSocketAutoConnect.Value) return;
					var delay = delays[index];
					this.SetState(WebSocketConnectionState.Connecting, $"Reconnecting in {(int)delay.TotalSeconds} s...");
					LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "ReconnectScheduled", "A bounded reconnect attempt was scheduled.", details: $"Trigger={trigger};Attempt={index + 1}/{delays.Count};DelaySeconds={(int)delay.TotalSeconds}");
					await Task.Delay(delay, owner.Token).ConfigureAwait(false);
					if (owner.IsCancellationRequested || !Settings.General.WebSocketAutoConnect.Value) return;
					LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "ReconnectAttempt", "Trying to restore the SUB connection.", details: $"Attempt={index + 1}/{delays.Count}");
					var result = await this.ConnectCoreAsync(this._lastAddress, this._lastPort, this._lastSocketBox, this._lastApiKey, owner.Token).ConfigureAwait(false);
					if (result == ConnectionAttemptResult.Admitted)
					{
						LoggingService.Instance.Write(LogLevel.Info, "WEBSOCKET", "ReconnectSucceeded", "SUB connection restored.", details: $"Attempt={index + 1}/{delays.Count}");
						return;
					}
					if (result == ConnectionAttemptResult.Negotiating)
					{
						LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ReconnectSuspended", "Reconnect reached VPP replacement negotiation and will not loop automatically.");
						return;
					}
				}
				if (!owner.IsCancellationRequested)
				{
					this.SetState(WebSocketConnectionState.Error, "Unable to reconnect");
					LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ReconnectExhausted", "Automatic reconnect attempts were exhausted; SHPC will remain running without SUB until a manual retry or the next application start.");
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				if (!owner.IsCancellationRequested)
				{
					this.SetState(WebSocketConnectionState.Error, "Unable to reconnect");
					LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "ReconnectFailed", "Reconnect coordinator failed.", details: ex.ToString());
				}
			}
			finally
			{
				lock (this._reconnectSync)
				{
					if (ReferenceEquals(this._reconnectCts, owner))
					{
						this._reconnectCts = null;
						this._reconnectTask = null;
					}
				}
				owner.Dispose();
			}
		}

		private void CancelReconnectSeries()
		{
			CancellationTokenSource cts;
			lock (this._reconnectSync)
			{
				cts = this._reconnectCts;
				this._reconnectCts = null;
				this._reconnectTask = null;
			}
			try { cts?.Cancel(); } catch { }
		}

		private void RememberConnectionSettings(string address, int port, string socketBox, string apiKey)
		{
			this._lastAddress = address?.Trim() ?? string.Empty;
			this._lastPort = port;
			this._lastSocketBox = socketBox?.Trim() ?? string.Empty;
			this._lastApiKey = apiKey?.Trim() ?? string.Empty;
		}

		private bool HasRememberedConnectionSettings()
			=> TryValidateConnectionSettings(this._lastAddress, this._lastPort, this._lastSocketBox, this._lastApiKey, out _);

		private async Task PersistConnectionPreferenceAsync(bool desired, string address, int port, string socketBox, string apiKey)
		{
			if (desired)
			{
				Settings.General.WebSocketAddress.Value = address?.Trim();
				Settings.General.WebSocketPort.Value = port;
				Settings.General.WebSocketSocketBox.Value = socketBox?.Trim();
				Settings.General.WebSocketApiKeyProtected.Value = ProtectApiKey(apiKey?.Trim());
			}
			Settings.General.WebSocketAutoConnect.Value = desired;
			try { await LocalSettingsProvider.Instance.SaveAsync().ConfigureAwait(false); }
			catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Warning, "WEBSOCKET", "ConnectionPreferenceSaveFailed", "WebSocket desired connection state could not be persisted.", details: ex.ToString()); }
		}

		private string TakeServerDisconnectReason()
		{
			var reason = this._serverDisconnectReason;
			this._serverDisconnectReason = null;
			return reason;
		}

		private void OnDesktopStateChanged(object sender, DesktopSystemStateChangedEventArgs e)
		{
			if (this._state != WebSocketConnectionState.Connected || this._lifetimeCts == null || string.IsNullOrWhiteSpace(this._peerSocketBox)) return;
			_ = this.SendDesktopStateEventSafeAsync(e.State, this._lifetimeCts.Token);
		}

		private async Task SendDesktopStateEventSafeAsync(DesktopSystemState state, CancellationToken cancellationToken)
		{
			try { await this.SendDesktopStateEventAsync(state, cancellationToken).ConfigureAwait(false); }
			catch (OperationCanceledException) { }
			catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Warning, "VPP", "StateEventFailed", "Desktop state event could not be sent.", details: ex.ToString()); }
		}

		private Task SendDesktopStateEventAsync(DesktopSystemState state, CancellationToken cancellationToken)
		{
			var peer = this._peerSocketBox;
			return string.IsNullOrWhiteSpace(peer) ? Task.CompletedTask : this.SendEventAsync("desktopStateChanged", this._desktopAdapter.CreateStateEventArgs(state), peer, false, cancellationToken);
		}

		private Task SendResponseAsync(string recipient, string correlationId, object result, CancellationToken cancellationToken)
			=> this.SendJsonAsync(CreateEnvelope("response", recipient, Guid.CreateVersion7().ToString("D"), new Dictionary<string, object> { ["correlationId"] = correlationId, ["result"] = result }), cancellationToken);

		private Task SendErrorAsync(string recipient, string correlationId, string code, string message, object details, CancellationToken cancellationToken)
			=> this.SendJsonAsync(CreateEnvelope("error", recipient, Guid.CreateVersion7().ToString("D"), new Dictionary<string, object> { ["correlationId"] = correlationId, ["error"] = new { code = code ?? "COMMAND_FAILED", message = message ?? "The command failed.", details } }), cancellationToken);

		private Task SendEventAsync(string eventName, object args, string recipient, bool expectsResponse, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(recipient)) return Task.CompletedTask;
			return this.SendJsonAsync(CreateEnvelope("event", recipient, Guid.CreateVersion7().ToString("D"), new Dictionary<string, object> { ["event"] = eventName, ["args"] = args, ["expectsResponse"] = expectsResponse }), cancellationToken);
		}

		private Dictionary<string, object> CreateEnvelope(string type, string recipient, string id, IDictionary<string, object> extra)
		{
			var message = new Dictionary<string, object> { ["protocolVersion"] = VppVersion, ["id"] = id, ["type"] = type, ["from"] = this._socketBox, ["recipient"] = recipient };
			if (extra != null) foreach (var pair in extra) message[pair.Key] = pair.Value;
			message["source"] = new { app = "SylphyHornPlusCon", version = AppVersion };
			message["timestamp"] = DateTimeOffset.Now.ToString("O");
			return message;
		}

		private async Task SendJsonAsync(object message, CancellationToken cancellationToken)
		{
			var client = this._client;
			if (client == null || client.State != WebSocketState.Open) throw new InvalidOperationException("SUB WebSocket is not open.");
			var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
			await this._sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try { await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
			finally { this._sendGate.Release(); }
		}

		private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket client, CancellationToken cancellationToken)
		{
			var buffer = new byte[4096];
			using var stream = new MemoryStream();
			while (true)
			{
				var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close) return null;
				if (result.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("Only VPP text frames are supported.");
				stream.Write(buffer, 0, result.Count);
				if (stream.Length > MaxMessageBytes) throw new InvalidOperationException("Incoming VPP message exceeds the 1 MiB safety limit.");
				if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
			}
		}

		private bool TryValidateEnvelope(JsonElement message, out string error)
		{
			error = null;
			if (message.ValueKind != JsonValueKind.Object) { error = "VPP message must be an object."; return false; }
			if (!message.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var parsedVersion) || parsedVersion != VppVersion) { error = "Unsupported or missing protocolVersion."; return false; }
			foreach (var name in new[] { "id", "type", "from", "recipient", "timestamp" })
			{
				if (!message.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) { error = $"Missing or invalid {name}."; return false; }
			}
			if (!message.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object) { error = "Missing source object."; return false; }
			return true;
		}

		private static bool TryValidateConnectionSettings(string address, int port, string socketBox, string apiKey, out string error)
		{
			if (string.IsNullOrWhiteSpace(address)) { error = "IP is required."; return false; }
			if (port < 1 || port > 65535) { error = "Socket port must be between 1 and 65535."; return false; }
			if (string.IsNullOrWhiteSpace(socketBox)) { error = "Socket box is required."; return false; }
			if (socketBox.Trim().Contains("/") || socketBox.Trim().Contains("?") || string.Equals(socketBox.Trim(), ServerSocketBox, StringComparison.OrdinalIgnoreCase)) { error = "Socket box contains an invalid reserved/path character or name."; return false; }
			if (string.IsNullOrWhiteSpace(apiKey) || !ApiKeyRegex.IsMatch(apiKey.Trim())) { error = "API KEY must contain exactly 64 hexadecimal characters."; return false; }
			error = null; return true;
		}

		private static Uri BuildUri(string address, int port, string socketBox, string apiKey)
		{
			UriBuilder builder;
			if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
			{
				var supplied = new Uri(address, UriKind.Absolute); builder = new UriBuilder(supplied) { Port = port };
			}
			else builder = new UriBuilder("ws", address, port);
			builder.Path = "/mailbox/" + Uri.EscapeDataString(socketBox);
			builder.Query = "apiKey=" + Uri.EscapeDataString(apiKey);
			return builder.Uri;
		}

		private static string GetDisplayUri(Uri uri) => uri == null ? string.Empty : $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}";
		private static string GetApplicationVersion()
		{
			var version = typeof(WebSocketConnectionService).Assembly.GetName().Version;
			return version == null ? "unknown" : $"{version.Major}.{version.Minor:00}";
		}
		private static bool TryGetResult(JsonElement response, out JsonElement result)
		{
			if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("result", out result) && result.ValueKind == JsonValueKind.Object) return true;
			result = default; return false;
		}
		private static int TryReadInt(JsonElement obj, string property) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : 0;
		private static string TryReadString(JsonElement obj, string property) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
		private static IReadOnlyList<SocketBoxConnectionInfo> ParseConnectionRoster(JsonElement result)
		{
			if (!result.TryGetProperty("connections", out var list) || list.ValueKind != JsonValueKind.Array) return Array.Empty<SocketBoxConnectionInfo>();
			return list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(TryReadString(item, "connectionId"))).Select(item => new SocketBoxConnectionInfo(TryReadString(item, "connectionId"), TryReadString(item, "socketBox"), TryReadString(item, "hostName"), TryReadString(item, "ip"), TryReadString(item, "service"), TryReadString(item, "connectedAt"))).ToArray();
		}

		private void RaiseReplacementNegotiation(ReplacementNegotiationEventArgs args)
		{
			var handlers = this.ReplacementNegotiationRequested; if (handlers == null) return;
			foreach (EventHandler<ReplacementNegotiationEventArgs> handler in handlers.GetInvocationList())
			{
				try { handler(this, args); }
				catch (Exception ex) { LoggingService.Instance.Write(LogLevel.Error, "WEBSOCKET", "NegotiationSubscriberFailed", "A replacement negotiation subscriber failed.", details: ex.ToString()); }
			}
		}

		private void SetState(WebSocketConnectionState state, string message)
		{
			this._state = state; this._statusMessage = string.IsNullOrWhiteSpace(message) ? state.ToString() : message;
			var handlers = this.StateChanged; if (handlers == null) return;
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
			foreach (var pending in this._pendingRequests.Values) pending.TrySetCanceled();
			this._pendingRequests.Clear();
			try { this._client?.Dispose(); } catch { }
			this._client = null;
			try { this._lifetimeCts?.Dispose(); } catch { }
			this._lifetimeCts = null;
			this._receiveTask = null;
			this._heartbeatTask = null;
			this._socketBox = null;
			this._peerSocketBox = null;
		}

		public static string ProtectApiKey(string value) { if (string.IsNullOrEmpty(value)) return null; return Dpapi.Protect(value); }
		public static string UnprotectApiKey(string value) { if (string.IsNullOrEmpty(value)) return string.Empty; try { return Dpapi.Unprotect(value); } catch { return string.Empty; } }
		private void OnApplicationExit(object sender, ExitEventArgs e) => this.Dispose();
		public void Dispose()
		{
			if (this._disposed) return; this._disposed = true;
			this.CancelReconnectSeries();
			DesktopControlService.Instance.StateChanged -= this.OnDesktopStateChanged;
			DesktopControlService.Instance.SetEnabled(false);
			try { this._lifetimeCts?.Cancel(); } catch { }
			this.CleanupClient(); this._sendGate.Dispose(); this._gate.Dispose();
			if (Application.Current != null) Application.Current.Exit -= this.OnApplicationExit;
		}

		private static class Dpapi
		{
			[StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }
			[DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);
			[DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);
			[DllImport("Kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr memory);
			internal static string Protect(string value)
			{
				var bytes = Encoding.UTF8.GetBytes(value); var input = CreateBlob(bytes);
				try
				{
					if (!CryptProtectData(ref input, "SylphyHornPlusCon WebSocket API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output)) throw new InvalidOperationException("Windows could not protect the API key.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
					try { var protectedBytes = new byte[output.Size]; Marshal.Copy(output.Data, protectedBytes, 0, output.Size); return Convert.ToBase64String(protectedBytes); }
					finally { if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
				}
				finally { if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data); }
			}
			internal static string Unprotect(string value)
			{
				var bytes = Convert.FromBase64String(value); var input = CreateBlob(bytes);
				try
				{
					if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output)) throw new InvalidOperationException("Windows could not unprotect the API key.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
					try { var plainBytes = new byte[output.Size]; Marshal.Copy(output.Data, plainBytes, 0, output.Size); return Encoding.UTF8.GetString(plainBytes); }
					finally { if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
				}
				finally { if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data); }
			}
			private static DataBlob CreateBlob(byte[] bytes) { var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Marshal.Copy(bytes, 0, blob.Data, bytes.Length); return blob; }
		}
	}
}
