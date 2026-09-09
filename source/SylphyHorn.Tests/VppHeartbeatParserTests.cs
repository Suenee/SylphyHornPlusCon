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
			Assert.True(snapshot.PeerConnected);
		}

		[Fact]
		public void AllowsUnknownPeerUntilValidApplicationTrafficLearnsIt()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{\"shpc-bc\":{\"connected\":true}},\"heartbeat\":{\"intervalMs\":15000}}}");
			Assert.True(VppHeartbeatParser.TryParse(document.RootElement, null, out var snapshot, out var error), error);
			Assert.Equal(15000, snapshot.IntervalMs);
			Assert.Null(snapshot.PeerConnected);
		}

		[Fact]
		public void RejectsMissingLearnedPeerState()
		{
			using var document = JsonDocument.Parse("{\"result\":{\"mailboxes\":{},\"heartbeat\":{\"intervalMs\":30000}}}");
			Assert.False(VppHeartbeatParser.TryParse(document.RootElement, "shpc-bc", out _, out var error));
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
