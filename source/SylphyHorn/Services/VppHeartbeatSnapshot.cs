using System;
using System.Text.Json;

namespace SylphyHorn.Services
{
	internal readonly struct VppHeartbeatSnapshot
	{
		internal VppHeartbeatSnapshot(int intervalMs, bool? peerConnected)
		{
			this.IntervalMs = intervalMs;
			this.PeerConnected = peerConnected;
		}

		internal int IntervalMs { get; }
		internal bool? PeerConnected { get; }
	}

	internal static class VppHeartbeatParser
	{
		internal static bool TryParse(JsonElement response, string peerSocketBox, out VppHeartbeatSnapshot snapshot, out string error)
		{
			snapshot = default;
			error = null;
			if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
			{
				error = "missing or invalid result object";
				return false;
			}
			if (!result.TryGetProperty("heartbeat", out var heartbeat) || heartbeat.ValueKind != JsonValueKind.Object ||
				!heartbeat.TryGetProperty("intervalMs", out var intervalValue) || intervalValue.ValueKind != JsonValueKind.Number ||
				!intervalValue.TryGetInt32(out var intervalMs) || intervalMs < 5000 || intervalMs > 3600000)
			{
				error = "missing or invalid heartbeat.intervalMs";
				return false;
			}
			if (!result.TryGetProperty("mailboxes", out var mailboxes) || mailboxes.ValueKind != JsonValueKind.Object)
			{
				error = "missing or invalid mailboxes object";
				return false;
			}

			bool? peerConnected = null;
			if (!string.IsNullOrWhiteSpace(peerSocketBox))
			{
				JsonElement peer = default;
				var found = false;
				foreach (var property in mailboxes.EnumerateObject())
				{
					if (!string.Equals(property.Name, peerSocketBox, StringComparison.OrdinalIgnoreCase)) continue;
					peer = property.Value;
					found = true;
					break;
				}
				if (!found || peer.ValueKind != JsonValueKind.Object || !peer.TryGetProperty("connected", out var connected) ||
					(connected.ValueKind != JsonValueKind.True && connected.ValueKind != JsonValueKind.False))
				{
					error = $"missing or invalid mailbox state for peer '{peerSocketBox}'";
					return false;
				}
				peerConnected = connected.GetBoolean();
			}

			snapshot = new VppHeartbeatSnapshot(intervalMs, peerConnected);
			return true;
		}
	}
}
