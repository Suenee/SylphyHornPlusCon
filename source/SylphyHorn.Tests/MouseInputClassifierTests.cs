using System.Windows.Forms;
using SylphyHorn.Services.Mouse;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class MouseInputClassifierTests
	{
		[Theory]
		[InlineData(0x0200U, 0U, Stroke.Move)]
		[InlineData(0x0201U, 0U, Stroke.LeftDown)]
		[InlineData(0x0202U, 0U, Stroke.LeftUp)]
		[InlineData(0x0204U, 0U, Stroke.RightDown)]
		[InlineData(0x0205U, 0U, Stroke.RightUp)]
		[InlineData(0x0207U, 0U, Stroke.MiddleDown)]
		[InlineData(0x0208U, 0U, Stroke.MiddleUp)]
		[InlineData(0x020BU, 1U << 16, Stroke.X1Down)]
		[InlineData(0x020BU, 2U << 16, Stroke.X2Down)]
		[InlineData(0x020CU, 1U << 16, Stroke.X1Up)]
		[InlineData(0x020CU, 2U << 16, Stroke.X2Up)]
		[InlineData(0x020BU, 0U, Stroke.Unknown)]
		[InlineData(0x020CU, 3U << 16, Stroke.Unknown)]
		[InlineData(0x9999U, 0U, Stroke.Unknown)]
		public void MouseMessagesMapToStableStrokes(uint message, uint mouseData, Stroke expected)
		{
			Assert.Equal(expected, MouseInputClassifier.Classify(message, mouseData));
		}

		[Theory]
		[InlineData(120, Stroke.WheelUp)]
		[InlineData(1, Stroke.WheelUp)]
		[InlineData(0, Stroke.WheelDown)]
		[InlineData(-1, Stroke.WheelDown)]
		[InlineData(-120, Stroke.WheelDown)]
		public void WheelDeltaUsesTheSignedHighWord(short delta, Stroke expected)
		{
			var mouseData = unchecked((uint)(ushort)delta) << 16;

			Assert.Equal(expected, MouseInputClassifier.Classify(0x020A, mouseData));
		}

		[Theory]
		[InlineData(0x020BU, (1U << 16) | 0x0001U, Stroke.X1Down)]
		[InlineData(0x020CU, (2U << 16) | 0xFFFFU, Stroke.X2Up)]
		[InlineData(0x020AU, (120U << 16) | 0x0001U, Stroke.WheelUp)]
		[InlineData(0x020AU, 0xFFFFU, Stroke.WheelDown)]
		[InlineData(0x020AU, (0xFF88U << 16) | 0x1234U, Stroke.WheelDown)]
		public void LowWordDoesNotAffectXButtonOrWheelClassification(
			uint message,
			uint mouseData,
			Stroke expected)
		{
			Assert.Equal(expected, MouseInputClassifier.Classify(message, mouseData));
		}

		[Theory]
		[InlineData(Stroke.LeftDown, Keys.LButton, StrokeDirection.Down)]
		[InlineData(Stroke.LeftUp, Keys.LButton, StrokeDirection.Up)]
		[InlineData(Stroke.RightDown, Keys.RButton, StrokeDirection.Down)]
		[InlineData(Stroke.RightUp, Keys.RButton, StrokeDirection.Up)]
		[InlineData(Stroke.MiddleDown, Keys.MButton, StrokeDirection.Down)]
		[InlineData(Stroke.MiddleUp, Keys.MButton, StrokeDirection.Up)]
		[InlineData(Stroke.X1Down, Keys.XButton1, StrokeDirection.Down)]
		[InlineData(Stroke.X1Up, Keys.XButton1, StrokeDirection.Up)]
		[InlineData(Stroke.X2Down, Keys.XButton2, StrokeDirection.Down)]
		[InlineData(Stroke.X2Up, Keys.XButton2, StrokeDirection.Up)]
		[InlineData(Stroke.WheelDown, (Keys)Stroke.WheelDown, StrokeDirection.None)]
		[InlineData(Stroke.WheelUp, (Keys)Stroke.WheelUp, StrokeDirection.None)]
		public void ActionableStrokesMapToKeysAndDirections(
			Stroke stroke,
			Keys expectedKey,
			StrokeDirection expectedDirection)
		{
			Assert.True(MouseInputClassifier.TryGetKeyAndDirection(stroke, out var keyCode, out var direction));
			Assert.Equal(expectedKey, keyCode);
			Assert.Equal(expectedDirection, direction);
		}

		[Theory]
		[InlineData(Stroke.Move)]
		[InlineData(Stroke.Unknown)]
		public void NonActionableStrokesDoNotProduceAKey(Stroke stroke)
		{
			Assert.False(MouseInputClassifier.TryGetKeyAndDirection(stroke, out var keyCode, out var direction));
			Assert.Equal(Keys.None, keyCode);
			Assert.Equal(StrokeDirection.None, direction);
		}
	}
}
