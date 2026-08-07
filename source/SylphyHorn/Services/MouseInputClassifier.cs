using System.Windows.Forms;

namespace SylphyHorn.Services.Mouse
{
	internal static class MouseInputClassifier
	{
		internal static Stroke Classify(uint message, uint mouseData)
		{
			switch (message)
			{
				case 0x0200: return Stroke.Move;
				case 0x0201: return Stroke.LeftDown;
				case 0x0202: return Stroke.LeftUp;
				case 0x0204: return Stroke.RightDown;
				case 0x0205: return Stroke.RightUp;
				case 0x0207: return Stroke.MiddleDown;
				case 0x0208: return Stroke.MiddleUp;
				case 0x020A:
					return unchecked((short)(mouseData >> 16)) > 0
						? Stroke.WheelUp
						: Stroke.WheelDown;
				case 0x020B:
					return ClassifyXButton(mouseData, Stroke.X1Down, Stroke.X2Down);
				case 0x020C:
					return ClassifyXButton(mouseData, Stroke.X1Up, Stroke.X2Up);
				default:
					return Stroke.Unknown;
			}
		}

		internal static bool TryGetKeyAndDirection(
			Stroke stroke,
			out Keys keyCode,
			out StrokeDirection direction)
		{
			if (stroke == Stroke.Move || stroke == Stroke.Unknown)
			{
				keyCode = Keys.None;
				direction = StrokeDirection.None;
				return false;
			}

			if (stroke == Stroke.WheelDown || stroke == Stroke.WheelUp)
			{
				keyCode = (Keys)stroke;
				direction = StrokeDirection.None;
				return true;
			}

			if ((int)stroke % 2 != 0)
			{
				keyCode = (Keys)(((int)stroke >> 1) + 1);
				direction = StrokeDirection.Down;
				return true;
			}

			keyCode = (Keys)((int)stroke >> 1);
			direction = StrokeDirection.Up;
			return true;
		}

		private static Stroke ClassifyXButton(uint mouseData, Stroke x1, Stroke x2)
		{
			switch (mouseData >> 16)
			{
				case 1: return x1;
				case 2: return x2;
				default: return Stroke.Unknown;
			}
		}
	}
}
