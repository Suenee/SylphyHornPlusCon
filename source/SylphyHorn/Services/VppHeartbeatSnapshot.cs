using System;
using System.Text.Json;

namespace SylphyHorn.Services
{
	internal readonly struct VppHeartbeatSnapshot
	{
		internal VppHeartbeatSnapshot(int intervalMs, string peerSocketBox, bool? peerConnected, int routablePeerCount)
		{
			this.IntervalMs = intervalMs;
			this.PeerSocketBox = peerSocketBox;
			this.PeerConnected = peerConnected;
			this.RoutablePeerCount = routablePeerCount;
		}

		internal int IntervalMs { get; }
		internal string PeerSocketBox { get; }
		internal bool? PeerConnected { get; }
		internal int RoutablePeerCount { get; }
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

			string selectedPeer = null;
			bool? selectedConnected = null;
			var routablePeerCount = 0;
			string solePeer = null;
			bool soleConnected = false;

			foreach (var property in mailboxes.EnumerateObject())
			{
				routablePeerCount++;
				if (property.Value.ValueKind != JsonValueKind.Object || !property.Value.TryGetProperty("connected", out var connected) ||
					(connected.ValueKind != JsonValueKind.True && connected.ValueKind != JsonValueKind.False))
				{
					error = $"missing or invalid mailbox state for routable peer '{property.Name}'";
					return false;
				}

				var isConnected = connected.GetBoolean();
				if (routablePeerCount == 1)
				{
					solePeer = property.Name;
					soleConnected = isConnected;
				}

				if (!string.IsNullOrWhiteSpace(peerSocketBox) && string.Equals(property.Name, peerSocketBox, StringComparison.OrdinalIgnoreCase))
				{
					selectedPeer = property.Name;
					selectedConnected = isConnected;
				}
			}

			if (selectedPeer == null && routablePeerCount == 1)
			{
				selectedPeer = solePeer;
				selectedConnected = soleConnected;
			}

			snapshot = new VppHeartbeatSnapshot(intervalMs, selectedPeer, selectedConnected, routablePeerCount);
			return true;
		}
	}
}
