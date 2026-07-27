using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SylphyHorn.Interop;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public class KeyboardInterceptorTests
	{
		[Fact]
		public void KbdLlHookStructHasPlatformSpecificSize()
		{
			Assert.Equal(IntPtr.Size == 4 ? 20 : 24, Marshal.SizeOf<KBDLLHOOKSTRUCT>());
		}

		[Fact]
		public void KbdLlHookStructHasNativeFieldLayout()
		{
			Assert.Equal(new IntPtr(0), Marshal.OffsetOf<KBDLLHOOKSTRUCT>(nameof(KBDLLHOOKSTRUCT.vkCode)));
			Assert.Equal(new IntPtr(4), Marshal.OffsetOf<KBDLLHOOKSTRUCT>(nameof(KBDLLHOOKSTRUCT.scanCode)));
			Assert.Equal(new IntPtr(8), Marshal.OffsetOf<KBDLLHOOKSTRUCT>(nameof(KBDLLHOOKSTRUCT.flags)));
			Assert.Equal(new IntPtr(12), Marshal.OffsetOf<KBDLLHOOKSTRUCT>(nameof(KBDLLHOOKSTRUCT.time)));
			Assert.Equal(new IntPtr(16), Marshal.OffsetOf<KBDLLHOOKSTRUCT>(nameof(KBDLLHOOKSTRUCT.dwExtraInfo)));
			Assert.Equal(typeof(IntPtr), typeof(KBDLLHOOKSTRUCT).GetField(nameof(KBDLLHOOKSTRUCT.dwExtraInfo))?.FieldType);
		}

		[Theory]
		[InlineData(0x0100, true)]
		[InlineData(0x0104, true)]
		[InlineData(0x0101, false)]
		[InlineData(0x0105, false)]
		public void TryClassifyRecognizesKeyboardMessages(int message, bool expectedIsKeyDown)
		{
			var classified = KeyboardInterceptor.TryClassify(0, (IntPtr)message, out var isKeyDown);

			Assert.True(classified);
			Assert.Equal(expectedIsKeyDown, isKeyDown);
		}

		[Theory]
		[InlineData(0)]
		[InlineData(0x0200)]
		[InlineData(int.MaxValue)]
		public void TryClassifyRejectsUnknownMessages(int message)
		{
			Assert.False(KeyboardInterceptor.TryClassify(0, (IntPtr)message, out _));
		}

		[Theory]
		[InlineData(-1)]
		[InlineData(1)]
		public void TryClassifyRejectsNonActionHookCodes(int nCode)
		{
			Assert.False(KeyboardInterceptor.TryClassify(nCode, (IntPtr)0x0100, out _));
		}

		[Theory]
		[InlineData(0x10)]
		[InlineData(0x12)]
		public void ProcessKeyEventPassesInjectedInputWithoutRaisingEvents(uint flags)
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				var raised = false;
				interceptor.KeyDown += (sender, args) => raised = true;

				var suppressed = interceptor.ProcessKeyEvent(true, CreateData(Keys.A, flags));

				Assert.False(suppressed);
				Assert.False(raised);
			}
		}

		[Theory]
		[InlineData(0x00)]
		[InlineData(0x01)]
		[InlineData(0x02)]
		public void ProcessKeyEventTreatsNonInjectedFlagsAsNormalInput(uint flags)
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				var raised = false;
				interceptor.KeyDown += (sender, args) => raised = true;

				Assert.False(interceptor.ProcessKeyEvent(true, CreateData(Keys.A, flags)));
				Assert.True(raised);
			}
		}

		[Fact]
		public void ProcessKeyEventRaisesOnlyKeyDownForDownMessage()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				var downCount = 0;
				var upCount = 0;
				interceptor.KeyDown += (sender, args) => downCount++;
				interceptor.KeyUp += (sender, args) => upCount++;

				interceptor.ProcessKeyEvent(true, CreateData(Keys.B));

				Assert.Equal(1, downCount);
				Assert.Equal(0, upCount);
			}
		}

		[Fact]
		public void ProcessKeyEventRaisesOnlyKeyUpForUpMessage()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				var downCount = 0;
				var upCount = 0;
				interceptor.KeyDown += (sender, args) => downCount++;
				interceptor.KeyUp += (sender, args) => upCount++;

				interceptor.ProcessKeyEvent(false, CreateData(Keys.B));

				Assert.Equal(0, downCount);
				Assert.Equal(1, upCount);
			}
		}

		[Fact]
		public void ProcessKeyEventReturnsHandlerSuppression()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				interceptor.KeyDown += (sender, args) => args.SuppressKeyPress = true;

				Assert.True(interceptor.ProcessKeyEvent(true, CreateData(Keys.C)));
			}
		}

		[Fact]
		public void ProcessKeyEventPassesInputWhenHandlerDoesNotSuppress()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				interceptor.KeyDown += (sender, args) => { };

				Assert.False(interceptor.ProcessKeyEvent(true, CreateData(Keys.C)));
			}
		}

		[Fact]
		public void ProcessKeyEventPassesInputWithoutHandlers()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				Assert.False(interceptor.ProcessKeyEvent(true, CreateData(Keys.C)));
			}
		}

		[Fact]
		public void ProcessKeyEventProvidesVirtualKeyCode()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				Keys? keyCode = null;
				interceptor.KeyDown += (sender, args) => keyCode = args.KeyCode;

				interceptor.ProcessKeyEvent(true, CreateData(Keys.F24));

				Assert.Equal(Keys.F24, keyCode);
			}
		}

		[Fact]
		public void ProcessKeyEventFailsOpenWhenHandlerThrows()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				interceptor.KeyDown += (sender, args) => throw new InvalidOperationException();

				var exception = Record.Exception(() => interceptor.ProcessKeyEvent(true, CreateData(Keys.D)));

				Assert.Null(exception);
				Assert.False(interceptor.ProcessKeyEvent(true, CreateData(Keys.D)));
			}
		}

		[Fact]
		public void ProcessKeyEventFailsOpenWhenOneOfMultipleHandlersThrows()
		{
			using (var interceptor = new KeyboardInterceptor())
			{
				var laterHandlerCalled = false;
				interceptor.KeyDown += (sender, args) => throw new InvalidOperationException();
				interceptor.KeyDown += (sender, args) => laterHandlerCalled = true;

				Assert.False(interceptor.ProcessKeyEvent(true, CreateData(Keys.E)));
				Assert.False(laterHandlerCalled);
			}
		}

		[Fact]
		public void KeyboardInterceptorDisposeIsIdempotentWithoutStartingHook()
		{
			var interceptor = new KeyboardInterceptor();

			interceptor.Dispose();

			Assert.Null(Record.Exception(interceptor.Dispose));
		}

		[Fact]
		public void KeyboardInterceptorStartAfterDisposeThrowsWithoutStartingHook()
		{
			var interceptor = new KeyboardInterceptor();
			interceptor.Dispose();

			Assert.Throws<ObjectDisposedException>(interceptor.StartCapturing);
		}

		[Fact]
		public void ShortcutKeyDetectorDisposeIsIdempotentWithoutStartingHooks()
		{
			var detector = new ShortcutKeyDetector();

			detector.Dispose();

			Assert.Null(Record.Exception(detector.Dispose));
		}

		[Fact]
		public void ShortcutKeyDetectorStartAfterDisposeThrowsWithoutStartingHooks()
		{
			var detector = new ShortcutKeyDetector();
			detector.Dispose();

			Assert.Throws<ObjectDisposedException>(detector.Start);
		}

		private static KBDLLHOOKSTRUCT CreateData(Keys key, uint flags = 0)
		{
			return new KBDLLHOOKSTRUCT
			{
				vkCode = (uint)key,
				flags = flags,
			};
		}
	}
}
