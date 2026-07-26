using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SylphyHorn.Interop;
using NativeMethods = SylphyHorn.Interop.NativeMethods.GlobalHook;

namespace SylphyHorn.Services
{
	internal sealed class KeyboardInterceptor : IDisposable
	{
		public event EventHandler<KeyEventArgs> KeyDown;
		public event EventHandler<KeyEventArgs> KeyUp;

		private readonly NativeMethods.KeyboardHookDelegate _nativeMethodCallback;

		private IntPtr _handle;
		private bool _disposed;

		public KeyboardInterceptor()
		{
			this._nativeMethodCallback = this.HookProcedure;
		}

		public void StartCapturing()
		{
			if (this._disposed)
			{
				throw new ObjectDisposedException(nameof(KeyboardInterceptor));
			}

			if (this._handle != IntPtr.Zero)
			{
				return;
			}

			var hInstance = Marshal.GetHINSTANCE(typeof(KeyboardInterceptor).Assembly.GetModules()[0]);
			if (hInstance == new IntPtr(-1))
			{
				throw new InvalidOperationException("Failed to get the module handle for the keyboard hook.");
			}

			var handle = NativeMethods.SetWindowsHookEx(
				NativeMethods.WH_KEYBOARD_LL,
				this._nativeMethodCallback,
				hInstance,
				0);
			if (handle == IntPtr.Zero)
			{
				var error = Marshal.GetLastWin32Error();
				throw new Win32Exception(error);
			}

			this._handle = handle;
		}

		internal void StopCapturing()
		{
			if (this._handle == IntPtr.Zero)
			{
				return;
			}

			if (!NativeMethods.UnhookWindowsHookEx(this._handle))
			{
				var error = Marshal.GetLastWin32Error();
				throw new Win32Exception(error);
			}

			this._handle = IntPtr.Zero;
		}

		private IntPtr HookProcedure(int nCode, IntPtr wParam, IntPtr lParam)
		{
			try
			{
				if (nCode != NativeMethods.HC_ACTION)
				{
					return CallNextHook(nCode, wParam, lParam);
				}

				var message = (int)wParam;
				var isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
				var isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
				if ((!isDown && !isUp) || lParam == IntPtr.Zero)
				{
					return CallNextHook(nCode, wParam, lParam);
				}

				var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
				if ((data.flags & NativeMethods.LLKHF_INJECTED) != 0)
				{
					return CallNextHook(nCode, wParam, lParam);
				}

				var args = new KeyEventArgs((Keys)data.vkCode);
				if (isDown)
				{
					this.KeyDown?.Invoke(this, args);
				}
				else
				{
					this.KeyUp?.Invoke(this, args);
				}

				if (args.SuppressKeyPress)
				{
					return (IntPtr)1;
				}
			}
			catch
			{
				// Fail open: managed exceptions must not cross the native callback boundary.
			}

			return CallNextHook(nCode, wParam, lParam);
		}

		private static IntPtr CallNextHook(int nCode, IntPtr wParam, IntPtr lParam)
		{
			try
			{
				return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
			}
			catch
			{
				return IntPtr.Zero;
			}
		}

		public void Dispose()
		{
			if (this._disposed)
			{
				return;
			}

			this.StopCapturing();
			this._disposed = true;
		}
	}
}
