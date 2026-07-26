using System;
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace SylphyHorn.Interop
{
	[StructLayout(LayoutKind.Sequential)]
	public struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;
		public uint scanCode;
		public uint flags;
		public uint time;
		public IntPtr dwExtraInfo;
	}
}
