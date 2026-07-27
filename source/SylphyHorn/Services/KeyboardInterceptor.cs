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

			var hInstance = NativeMethods.GetModuleHandle(null);
			if (hInstance == IntPtr.Zero)
			{
				var error = Marshal.GetLastWin32Error();
				throw new Win32Exception(error);
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
				if (TryClassify(nCode, wParam, out var isKeyDown) && lParam != IntPtr.Zero)
				{
					var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
					if (this.ProcessKeyEvent(isKeyDown, in data))
					{
						return (IntPtr)1;
					}
				}
			}
			catch
			{
				// Fail open: managed exceptions must not cross the native callback boundary.
			}

			try
			{
				return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
			}
			catch
			{
				return IntPtr.Zero;
			}
		}

		internal static bool TryClassify(int nCode, IntPtr wParam, out bool isKeyDown)
		{
			isKeyDown = false;
			if (nCode != NativeMethods.HC_ACTION)
			{
				return false;
			}

			var message = (int)wParam;
			if (message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN)
			{
				isKeyDown = true;
				return true;
			}

			return message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
		}

		internal bool ProcessKeyEvent(bool isKeyDown, in KBDLLHOOKSTRUCT data)
		{
			try
			{
				if ((data.flags & NativeMethods.LLKHF_INJECTED) != 0)
				{
					return false;
				}

				var args = new KeyEventArgs((Keys)data.vkCode);
				if (isKeyDown)
				{
					this.KeyDown?.Invoke(this, args);
				}
				else
				{
					this.KeyUp?.Invoke(this, args);
				}

				return args.SuppressKeyPress;
			}
			catch
			{
				// Fail open: event-handler failures must not suppress input.
				return false;
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
