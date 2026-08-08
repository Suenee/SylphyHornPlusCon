using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SylphyHorn.Services.Mouse;

namespace SylphyHorn.Services
{
	internal sealed class MouseShortcutState
	{
		private readonly HashSet<Keys> _pressedButtons = new HashSet<Keys>();

		internal bool TryProcessButtonDown(
			Keys keyCode,
			Func<Keys, ICollection<Keys>, bool> publish,
			ref bool handled)
		{
			if (!IsButton(keyCode))
			{
				return false;
			}

			handled = publish(keyCode, this._pressedButtons);
			this._pressedButtons.Add(keyCode);
			return true;
		}

		internal bool TryProcessButtonUp(
			Keys keyCode,
			Func<Keys, ICollection<Keys>, bool> publish,
			ref bool handled)
		{
			if (!IsButton(keyCode))
			{
				return false;
			}

			this._pressedButtons.Remove(keyCode);
			handled = publish(keyCode, this._pressedButtons);

			if (this._pressedButtons.Count > 0)
			{
				this._pressedButtons.Remove((Keys)Stroke.WheelDown);
				this._pressedButtons.Remove((Keys)Stroke.WheelUp);
			}

			return true;
		}

		internal bool TryProcessWheel(
			Stroke stroke,
			Keys keyCode,
			Func<Keys, ICollection<Keys>, bool> publish,
			ref bool handled)
		{
			if (this._pressedButtons.Count == 0
				|| (stroke != Stroke.WheelDown && stroke != Stroke.WheelUp))
			{
				return false;
			}

			handled = publish(keyCode, this._pressedButtons);
			this._pressedButtons.Add(keyCode);
			return true;
		}

		internal void Clear()
		{
			this._pressedButtons.Clear();
		}

		private static bool IsButton(Keys keyCode)
		{
			return Keys.LButton <= keyCode
				&& keyCode <= Keys.XButton2
				&& keyCode != Keys.Cancel;
		}
	}
}
