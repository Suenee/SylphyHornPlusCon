using System;
using System.Collections.Generic;

namespace SylphyHorn.Services
{
	internal enum WebSocketReconnectDecision
	{
		Retry,
		Suspend,
		Ignore,
	}

	internal static class WebSocketReconnectPolicy
	{
		private static readonly TimeSpan[] RetrySchedule =
		{
			TimeSpan.FromSeconds(2),
			TimeSpan.FromSeconds(5),
			TimeSpan.FromSeconds(15),
			TimeSpan.FromSeconds(30),
			TimeSpan.FromSeconds(60),
		};

		internal static IReadOnlyList<TimeSpan> RetryDelays => RetrySchedule;

		internal static WebSocketReconnectDecision ForUnexpectedTransportLoss()
			=> WebSocketReconnectDecision.Retry;

		internal static WebSocketReconnectDecision ForServerDisconnectReason(string reason)
		{
			if (string.Equals(reason, "shutdown", StringComparison.Ordinal)
				|| string.Equals(reason, "restart", StringComparison.Ordinal)
				|| string.Equals(reason, "exit", StringComparison.Ordinal))
				return WebSocketReconnectDecision.Retry;

			if (string.Equals(reason, "replaced", StringComparison.Ordinal)
				|| string.Equals(reason, "negotiationTimeout", StringComparison.Ordinal))
				return WebSocketReconnectDecision.Suspend;

			return WebSocketReconnectDecision.Suspend;
		}

		internal static bool IsClientDisconnectReason(string reason)
			=> string.Equals(reason, "user", StringComparison.Ordinal);
	}
}
