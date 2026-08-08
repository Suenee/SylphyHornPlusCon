using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class NotificationTextFormatterTests
	{
		[Theory]
		[InlineData(false, "Virtual Desktop", "Virtual Desktop Switched")]
		[InlineData(true, "", "")]
		public void DesktopHeadersFollowSimpleNotificationMode(bool simple, string resident, string switched)
		{
			Assert.Equal(resident, NotificationTextFormatter.CreateResidentHeader(simple));
			Assert.Equal(switched, NotificationTextFormatter.CreateSwitchedHeader(simple));
		}

		[Theory]
		[InlineData(false, "Desktop 2 Moved to Desktop 5")]
		[InlineData(true, "Desktop 2 => Desktop 5")]
		public void MovedHeaderPreservesOldAndNewDesktopOrder(bool simple, string expected)
			=> Assert.Equal(expected, NotificationTextFormatter.CreateMovedHeader(2, 5, simple));

		[Theory]
		[InlineData(null, true, false, false, "Current Desktop: Desktop 3")]
		[InlineData("", true, false, true, "Reordered Current Desktop: Desktop 3")]
		[InlineData("Named", false, false, false, "Current Desktop: Desktop 3")]
		[InlineData("Named", true, false, false, "Desktop 3: Named")]
		[InlineData("Named", true, false, true, "Reordered Desktop 3: Named")]
		[InlineData(null, true, true, false, "Desktop 3")]
		[InlineData("", true, true, true, "Desktop 3")]
		[InlineData("Named", false, true, true, "Desktop 3")]
		[InlineData("Named", true, true, false, "3. Named")]
		[InlineData("Named", true, true, true, "3. Named")]
		[InlineData(" ", true, false, false, "Desktop 3:  ")]
		[InlineData(" ", true, true, false, "3.  ")]
		public void DesktopBodyPreservesNameAndModeContracts(string name, bool useDesktopName, bool simple, bool moved, string expected)
			=> Assert.Equal(expected, NotificationTextFormatter.CreateDesktopBody(3, name, useDesktopName, simple, moved));

		[Theory]
		[InlineData((int)PinOperations.PinWindow, false, "Virtual Desktop", "Pinned this window")]
		[InlineData((int)PinOperations.UnpinWindow, false, "Virtual Desktop", "Unpinned this window")]
		[InlineData((int)PinOperations.PinApp, false, "Virtual Desktop", "Pinned this application")]
		[InlineData((int)PinOperations.UnpinApp, false, "Virtual Desktop", "Unpinned this application")]
		[InlineData((int)PinOperations.PinWindow, true, "", "Window Pinned")]
		[InlineData((int)PinOperations.UnpinWindow, true, "", "Window Unpinned")]
		[InlineData((int)PinOperations.PinApp, true, "", "Application Pinned")]
		[InlineData((int)PinOperations.UnpinApp, true, "", "Application Unpinned")]
		[InlineData((int)PinOperations.Pin, false, "Virtual Desktop", "Pinned this application")]
		[InlineData((int)PinOperations.Pin, true, "", "Application Pinned")]
		[InlineData((int)PinOperations.Window, false, "Virtual Desktop", "Unpinned this window")]
		[InlineData((int)PinOperations.Window, true, "", "Window Unpinned")]
		[InlineData(0, false, "Virtual Desktop", "Unpinned this application")]
		[InlineData(0, true, "", "Application Unpinned")]
		[InlineData((int)PinOperations.PinWindow | 0x10, false, "Virtual Desktop", "Pinned this window")]
		[InlineData((int)PinOperations.PinWindow | 0x10, true, "", "Window Pinned")]
		[InlineData(0x10, false, "Virtual Desktop", "Unpinned this application")]
		[InlineData(0x10, true, "", "Application Unpinned")]
		public void PinTextPreservesOperationTargetAndSimpleMode(int operationValue, bool simple, string header, string body)
		{
			var operation = (PinOperations)operationValue;
			Assert.Equal(header, NotificationTextFormatter.CreatePinHeader(simple));
			Assert.Equal(body, NotificationTextFormatter.CreatePinBody(operation, simple));
		}
	}
}
