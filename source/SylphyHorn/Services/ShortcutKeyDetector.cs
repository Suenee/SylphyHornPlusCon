using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using SylphyHorn.Services.Mouse;

namespace SylphyHorn.Services
{
	/// <summary>
	/// Provides the function to detect a shortcut key ([modifier key(s)] + [key] style) by use of global key hook.
	/// </summary>
	public class ShortcutKeyDetector : IDisposable
	{
		private readonly HashSet<Keys> _pressedModifiers = new HashSet<Keys>();
		private readonly HashSet<Keys> _pressedMouseButtons = new HashSet<Keys>();
		private readonly KeyboardInterceptor _keyInterceptor = new KeyboardInterceptor();
		private readonly MouseInterceptor _mouseInterceptor = new MouseInterceptor();

		private bool _started;
		private bool _suspended;
		private bool _disposed;

		/// <summary>
		/// Occurs when detects a shortcut key.
		/// </summary>
		public event EventHandler<ShortcutKeyPressedEventArgs> KeyPressed;
		public event EventHandler<ShortcutKeyPressedEventArgs> KeyUp;
		public event EventHandler<ShortcutKeyPressedEventArgs> ButtonPressed;
		public event EventHandler<ShortcutKeyPressedEventArgs> ButtonUp;

		public ShortcutKeyDetector()
		{
			this._keyInterceptor.KeyDown += this.InterceptorOnKeyDown;
			this._keyInterceptor.KeyUp += this.InterceptorOnKeyUp;
			this._mouseInterceptor.MouseDown += this.InterceptorOnMouseDown;
			this._mouseInterceptor.MouseUp += this.InterceptorOnMouseUp;
			this._mouseInterceptor.WheelDown += this.InterceptorOnMouseWheel;
			this._mouseInterceptor.WheelUp += this.InterceptorOnMouseWheel;
		}

		public void Start()
		{
			if (this._disposed)
			{
				throw new ObjectDisposedException(nameof(ShortcutKeyDetector));
			}

			if (!this._started)
			{
				this._keyInterceptor.StartCapturing();
				try
				{
					this._mouseInterceptor.StartCapturing();
				}
				catch (Exception startException)
				{
					try
					{
						this._keyInterceptor.StopCapturing();
					}
					catch (Exception rollbackException)
					{
						try
						{
							startException.Data["KeyboardHookRollbackException"] = rollbackException;
						}
						catch
						{
							// Never replace the original mouse hook failure.
						}
					}

					throw;
				}

				this._started = true;
			}

			this._suspended = false;
		}

		public void Stop()
		{
			this._suspended = true;
			this._pressedModifiers.Clear();
			this._pressedMouseButtons.Clear();
		}

		private void InterceptorOnKeyDown(object sender, KeyEventArgs args)
		{
			if (this._suspended) return;

			if (args.KeyCode.IsModifyKey())
			{
				this._pressedModifiers.Add(args.KeyCode);
			}
			else
			{
				var pressedEventArgs = new ShortcutKeyPressedEventArgs(args.KeyCode, this._pressedModifiers);
				this.KeyPressed?.Invoke(this, pressedEventArgs);
				if (pressedEventArgs.Handled) args.SuppressKeyPress = true;
			}
		}

		private void InterceptorOnKeyUp(object sender, KeyEventArgs args)
		{
			if (this._suspended) return;

			//if (this._pressedModifiers.Count == 0) return;

			if (args.KeyCode.IsModifyKey())
			{
				this._pressedModifiers.Remove(args.KeyCode);
			}
			else
			{
				var pressedEventArgs = new ShortcutKeyPressedEventArgs(args.KeyCode, this._pressedModifiers);
				this.KeyUp?.Invoke(this, pressedEventArgs);
				if (pressedEventArgs.Handled) args.SuppressKeyPress = true;
			}
		}

		private void InterceptorOnMouseDown(ref MouseState state)
		{
			if (this._suspended) return;

			var keyCode = state.KeyCode;
			if (keyCode < Keys.LButton || keyCode > Keys.XButton2 || keyCode == Keys.Cancel) return;

			var pressedEventArgs = new ShortcutKeyPressedEventArgs(keyCode, this._pressedMouseButtons);
			this.ButtonPressed?.Invoke(this, pressedEventArgs);
			state.Handled = pressedEventArgs.Handled;

			this._pressedMouseButtons.Add(keyCode);
		}

		private void InterceptorOnMouseUp(ref MouseState state)
		{
			if (this._suspended) return;

			var keyCode = state.KeyCode;
			if (keyCode < Keys.LButton || keyCode > Keys.XButton2 || keyCode == Keys.Cancel) return;

			this._pressedMouseButtons.Remove(keyCode);

			var pressedEventArgs = new ShortcutKeyPressedEventArgs(keyCode, this._pressedMouseButtons);
			this.ButtonUp?.Invoke(this, pressedEventArgs);
			state.Handled = pressedEventArgs.Handled;

			if (this._pressedMouseButtons.Count > 0)
			{
				this._pressedMouseButtons.Remove((Keys)Stroke.WheelDown);
				this._pressedMouseButtons.Remove((Keys)Stroke.WheelUp);
			}
		}

		private void InterceptorOnMouseWheel(ref MouseState state)
		{
			if (this._suspended) return;

			if (this._pressedMouseButtons.Count == 0) return;

			var stroke = state.Stroke;
			if (stroke != Stroke.WheelDown && stroke != Stroke.WheelUp) return;

			var keyCode = state.KeyCode;
			var pressedEventArgs = new ShortcutKeyPressedEventArgs(keyCode, this._pressedMouseButtons);
			this.ButtonPressed?.Invoke(this, pressedEventArgs);
			state.Handled = pressedEventArgs.Handled;

			this._pressedMouseButtons.Add(keyCode);
		}

		public void Dispose()
		{
			if (this._disposed)
			{
				return;
			}

			Exception exception = null;
			try
			{
				this._keyInterceptor.Dispose();
			}
			catch (Exception ex)
			{
				exception = ex;
			}

			try
			{
				this._mouseInterceptor.Dispose();
			}
			catch (Exception ex)
			{
				if (exception == null)
				{
					exception = ex;
				}
			}

			if (exception != null)
			{
				ExceptionDispatchInfo.Capture(exception).Throw();
			}

			this._started = false;
			this._suspended = true;
			this._pressedModifiers.Clear();
			this._pressedMouseButtons.Clear();
			this._disposed = true;
		}
	}
}
