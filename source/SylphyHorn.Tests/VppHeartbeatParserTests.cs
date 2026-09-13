using System.Text.Json;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class VppHeartbeatParserTests
	{
		[Fact]
		public void ParsesHeartbeatAndKnownPeerState()
		{
			using var document = JsonDocument.Parse("{\"type\":\"response\",\"result\":{\"mailboxes\":{\"shpc-bc\":{\"connected\":true}},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, "shpc-bc", out var snapshot, out var error), error);
			Assert.Equal(30000, snapshot.IntervalMs);
			Assert.Equal("shpc-bc", snapshot.PeerSocketBox);
			Assert.True(snapshot.PeerConnected);
			Assert.Equal(1, snapshot.RoutablePeerCount);
		}

		[Fact]
		public void DiscoversSingleRoutingScopedPeerWithoutApplicationTraffic()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{\"shpc-bc\":{\"connected\":true}},\"heartbeat\":{\"intervalMs\":15000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, null, out var snapshot, out var error), error);
			Assert.Equal(15000, snapshot.IntervalMs);
			Assert.Equal("shpc-bc", snapshot.PeerSocketBox);
			Assert.True(snapshot.PeerConnected);
			Assert.Equal(1, snapshot.RoutablePeerCount);
		}

		[Fact]
		public void DiscoversSingleOfflineRoutingScopedPeer()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{\"shpc-bc\":{\"connected\":false}},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, null, out var snapshot, out var error), error);
			Assert.Equal("shpc-bc", snapshot.PeerSocketBox);
			Assert.False(snapshot.PeerConnected);
			Assert.Equal(1, snapshot.RoutablePeerCount);
		}

		[Fact]
		public void AllowsNoRoutablePeerWithoutInventingIdentity()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, null, out var snapshot, out var error), error);
			Assert.Null(snapshot.PeerSocketBox);
			Assert.Null(snapshot.PeerConnected);
			Assert.Equal(0, snapshot.RoutablePeerCount);
		}

		[Fact]
		public void DoesNotChooseArbitrarilyBetweenMultipleRoutingScopedPeers()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{\"peer-a\":{\"connected\":true},\"peer-b\":{\"connected\":true}},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, null, out var snapshot, out var error), error);
			Assert.Null(snapshot.PeerSocketBox);
			Assert.Null(snapshot.PeerConnected);
			Assert.Equal(2, snapshot.RoutablePeerCount);
		}

		[Fact]
		public void KeepsKnownPeerWhenMultipleRoutingScopedPeersExist()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{\"peer-a\":{\"connected\":false},\"peer-b\":{\"connected\":true}},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, "peer-b", out var snapshot, out var error), error);
			Assert.Equal("peer-b", snapshot.PeerSocketBox);
			Assert.True(snapshot.PeerConnected);
			Assert.Equal(2, snapshot.RoutablePeerCount);
		}

		[Fact]
		public void RejectsInvalidRoutablePeerState()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{\"shpc-bc\":{}},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.False(VppHeartbeatParser.TryParse(document.RootElement, null, out _, out var error));
			Assert.Contains("shpc-bc", error);
		}

		[Fact]
		public void RejectsInvalidHeartbeatInterval()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{},\"heartbeat\":{\"intervalMs\":1000}}}");
			Assert.False(VppHeartbeatParser.TryParse(document.RootElement, null, out _, out var error));
			Assert.Contains("intervalMs", error);
		}
	}
}
