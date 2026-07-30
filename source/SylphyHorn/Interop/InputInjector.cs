using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SylphyHorn.Interop
{
	internal static class InputInjector
	{
		public const ushort VK_TAB = 0x09;
		public const ushort VK_SHIFT = 0x10;
		public const ushort VK_CONTROL = 0x11;
		public const ushort VK_MENU = 0x12;
		public const ushort VK_LWIN = 0x5B;
		public const ushort VK_RWIN = 0x5C;

		private const uint INPUT_KEYBOARD = 1;
		private const uint KEYEVENTF_KEYUP = 0x0002;

		private static readonly ushort[] ModifierKeys =
		{
			VK_CONTROL,
			VK_SHIFT,
			VK_MENU,
			VK_LWIN,
			VK_RWIN,
		};

		public static void ReleaseModifiersAndSendChord(params ushort[] chord)
		{
			if (chord == null) throw new ArgumentNullException(nameof(chord));
			if (chord.Length == 0) throw new ArgumentException("At least one key is required.", nameof(chord));

			var inputs = new List<INPUT>(ModifierKeys.Length + (chord.Length * 2));

			foreach (var key in ModifierKeys)
			{
				inputs.Add(CreateKeyboardInput(key, keyUp: true));
			}

			foreach (var key in chord)
			{
				inputs.Add(CreateKeyboardInput(key, keyUp: false));
			}

			for (var i = chord.Length - 1; i >= 0; i--)
			{
				inputs.Add(CreateKeyboardInput(chord[i], keyUp: true));
			}

			var inputArray = inputs.ToArray();
			var sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf(typeof(INPUT)));
			if (sent == (uint)inputArray.Length) return;

			throw new Win32Exception(
				Marshal.GetLastWin32Error(),
				$"SendInput inserted {sent} of {inputArray.Length} keyboard events.");
		}

		private static INPUT CreateKeyboardInput(ushort virtualKey, bool keyUp)
		{
			return new INPUT
			{
				Type = INPUT_KEYBOARD,
				Union = new INPUTUNION
				{
					Keyboard = new KEYBDINPUT
					{
						VirtualKey = virtualKey,
						Flags = keyUp ? KEYEVENTF_KEYUP : 0,
					},
				},
			};
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern uint SendInput(uint inputCount, [In] INPUT[] inputs, int inputSize);

		[StructLayout(LayoutKind.Sequential)]
		private struct INPUT
		{
			public uint Type;
			public INPUTUNION Union;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct INPUTUNION
		{
			[FieldOffset(0)]
			public MOUSEINPUT Mouse;

			[FieldOffset(0)]
			public KEYBDINPUT Keyboard;

			[FieldOffset(0)]
			public HARDWAREINPUT Hardware;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MOUSEINPUT
		{
			public int X;
			public int Y;
			public uint MouseData;
			public uint Flags;
			public uint Time;
			public UIntPtr ExtraInfo;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct KEYBDINPUT
		{
			public ushort VirtualKey;
			public ushort ScanCode;
			public uint Flags;
			public uint Time;
			public UIntPtr ExtraInfo;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct HARDWAREINPUT
		{
			public uint Message;
			public ushort ParameterLow;
			public ushort ParameterHigh;
		}
	}
}
