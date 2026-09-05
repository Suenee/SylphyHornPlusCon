using System;
using System.Linq;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class WebSocketReconnectPolicyTests
	{
		[Theory]
		[InlineData("shutdown")]
		[InlineData("restart")]
		[InlineData("exit")]
		public void ServerRestartReasonsKeepReconnectActive(string reason)
		{
			Assert.Equal(WebSocketReconnectDecision.Retry, WebSocketReconnectPolicy.ForServerDisconnectReason(reason));
		}

		[Theory]
		[InlineData("replaced")]
		[InlineData("negotiationTimeout")]
		[InlineData("unknown")]
		[InlineData("")]
		public void ServerReasonsThatCouldLoopSuspendAutomaticReconnect(string reason)
		{
			Assert.Equal(WebSocketReconnectDecision.Suspend, WebSocketReconnectPolicy.ForServerDisconnectReason(reason));
		}

		[Fact]
		public void UnexpectedTransportLossUsesBoundedReconnect()
		{
			Assert.Equal(WebSocketReconnectDecision.Retry, WebSocketReconnectPolicy.ForUnexpectedTransportLoss());
			Assert.Equal(new[] { 2d, 5d, 15d, 30d, 60d }, WebSocketReconnectPolicy.RetryDelays.Select(delay => delay.TotalSeconds));
		}

		[Fact]
		public void ClientGracefulDisconnectReasonIsUser()
		{
			Assert.True(WebSocketReconnectPolicy.IsClientDisconnectReason("user"));
			Assert.False(WebSocketReconnectPolicy.IsClientDisconnectReason("restart"));
		}
	}
}
