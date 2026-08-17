using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	[Collection(WindowsHookCollection.Name)]
	public class KeyboardHookIntegrationTests
	{
		private const int TestTimeoutMilliseconds = 10000;
		private const ushort VkF24 = 0x87;
		private const uint InputKeyboard = 1;
		private const uint KeyEventKeyUp = 0x0002;

		// These tests verify that lifecycle APIs complete against the real Win32 APIs.
		// Injected input cannot prove that the installed callback ran because production
		// intentionally passes LLKHF_INJECTED events through. Physical-input hook
		// verification remains an explicit machine smoke test.
		[WpfFact(Timeout = TestTimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.PhysicalInput)]
		public async Task KeyboardInterceptorLifecycleSmokePassesInjectedInputToIsolatedWindow()
		{
			await RunWithCleanup(async cleanup =>
			{
				var received = new TaskCompletionSource<bool>();
				var window = CreateIsolatedInputWindow();
				cleanup.Add(window.Close);
				window.PreviewKeyDown += (sender, args) =>
				{
					if (args.Key == Key.F24)
					{
						received.TrySetResult(true);
					}
				};
				ShowAndActivate(window);

				var interceptor = new KeyboardInterceptor();
				cleanup.Add(interceptor.Dispose);
				interceptor.StartCapturing();
				interceptor.StartCapturing();

				Assert.Equal(1u, SendKeyboardInput(VkF24, false));
				cleanup.Add(() => SendKeyUpWithFocusRecovery(window));
				var completed = await Task.WhenAny(received.Task, Task.Delay(TestTimeoutMilliseconds / 2));

				Assert.Same(received.Task, completed);
				Assert.True(await received.Task);
			});
		}

		[WpfFact(Timeout = TestTimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task ShortcutKeyDetectorLifecycleApiSmokeCompletes()
		{
			await RunWithCleanup(async cleanup =>
			{
				var detector = new ShortcutKeyDetector();
				cleanup.Add(detector.Dispose);
				detector.Start();
				detector.Start();
				await Dispatcher.Yield();
			});
		}

		[WpfFact(Timeout = TestTimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task HookServiceConstructionAndDisposeApiSmokeCompletes()
		{
			await RunWithCleanup(async cleanup =>
			{
				var service = new HookService();
				cleanup.Add(service.Dispose);
				await Dispatcher.Yield();
			});
		}

		[WpfFact(Timeout = TestTimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task HookServiceNestedSuspendTokensResumeAfterLastTokenInEitherOrder()
		{
			await RunWithCleanup(async cleanup =>
			{
				var service = new HookService();
				cleanup.Add(service.Dispose);
				var reloadCount = 0;
				service.Reload = () => reloadCount++;

				var first = service.Suspend();
				cleanup.Add(first.Dispose);
				var second = service.Suspend();
				cleanup.Add(second.Dispose);
				second.Dispose();
				Assert.Equal(0, reloadCount);
				first.Dispose();
				Assert.Equal(1, reloadCount);

				first = service.Suspend();
				second = service.Suspend();
				first.Dispose();
				Assert.Equal(1, reloadCount);
				second.Dispose();
				Assert.Equal(2, reloadCount);
				await Dispatcher.Yield();
			});
		}

		[WpfFact(Timeout = TestTimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task HookServiceDoesNotResumeWhenSuspendTokenReturnsAfterDispose()
		{
			await RunWithCleanup(async cleanup =>
			{
				var service = new HookService();
				cleanup.Add(service.Dispose);
				var reloadCount = 0;
				service.Reload = () => reloadCount++;
				var token = service.Suspend();
				cleanup.Add(token.Dispose);

				service.Dispose();
				token.Dispose();

				Assert.Equal(0, reloadCount);
				await Dispatcher.Yield();
			});
		}

		[WpfFact(Timeout = 30000)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public async Task ShortcutKeyDetectorRepeatedLifecycleApiSmokeCompletes()
		{
			await RunWithCleanup(async cleanup =>
			{
				for (var index = 0; index < 100; index++)
				{
					var detector = new ShortcutKeyDetector();
					cleanup.Add(detector.Dispose);
					detector.Start();
					detector.Dispose();
				}

				await Dispatcher.Yield();
			});
		}

		private static Window CreateIsolatedInputWindow()
		{
			return new Window
			{
				Title = "SylphyHorn keyboard integration test",
				Width = 1,
				Height = 1,
				Left = -32000,
				Top = -32000,
				Opacity = 0.01,
				ShowActivated = true,
				ShowInTaskbar = false,
				WindowStyle = WindowStyle.ToolWindow,
			};
		}

		private static void ShowAndActivate(Window window)
		{
			window.Show();
			var handle = new WindowInteropHelper(window).Handle;
			SetForegroundWindow(handle);
			window.Activate();
			Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

			if (GetForegroundWindow() != handle)
			{
				throw new InvalidOperationException("The isolated test window could not acquire focus; no input was injected.");
			}
		}

		private static uint SendKeyboardInput(ushort virtualKey, bool keyUp)
		{
			var input = new INPUT
			{
				type = InputKeyboard,
				union = new INPUTUNION
				{
					keyboard = new KEYBDINPUT
					{
						virtualKey = virtualKey,
						flags = keyUp ? KeyEventKeyUp : 0,
					},
				},
			};

			var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
			if (sent == 0)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			return sent;
		}

		private static void TryActivateForCleanup(Window window)
		{
			if (window == null)
			{
				return;
			}

			var handle = new WindowInteropHelper(window).Handle;
			if (handle != IntPtr.Zero)
			{
				SetForegroundWindow(handle);
				window.Activate();
				Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
			}
		}

		private static void SendKeyUpWithFocusRecovery(Window window)
		{
			Exception focusFailure = null;
			try
			{
				TryActivateForCleanup(window);
			}
			catch (Exception ex)
			{
				focusFailure = ex;
			}

			Exception keyUpFailure = null;
			try
			{
				SendKeyboardInput(VkF24, true);
			}
			catch (Exception ex)
			{
				keyUpFailure = ex;
			}

			if (focusFailure != null && keyUpFailure != null)
			{
				throw new AggregateException("Focus recovery and F24 key-up cleanup both failed.", focusFailure, keyUpFailure);
			}

			if (keyUpFailure != null)
			{
				ExceptionDispatchInfo.Capture(keyUpFailure).Throw();
			}

			if (focusFailure != null)
			{
				ExceptionDispatchInfo.Capture(focusFailure).Throw();
			}
		}

		private static async Task RunWithCleanup(Func<CleanupContext, Task> test)
		{
			var cleanup = new CleanupContext();
			ExceptionDispatchInfo testFailure = null;
			IReadOnlyList<Exception> cleanupFailures = null;
			try
			{
				await test(cleanup);
			}
			catch (Exception ex)
			{
				testFailure = ExceptionDispatchInfo.Capture(ex);
			}
			finally
			{
				cleanupFailures = cleanup.Run();
			}

			if (testFailure == null && cleanupFailures.Count == 0)
			{
				return;
			}

			if (testFailure != null && cleanupFailures.Count == 0)
			{
				testFailure.Throw();
			}

			var failures = new List<Exception>();
			if (testFailure != null)
			{
				failures.Add(testFailure.SourceException);
			}

			failures.AddRange(cleanupFailures);
			throw new AggregateException("The integration test or one or more cleanup operations failed.", failures);
		}

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetForegroundWindow(IntPtr window);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

		[StructLayout(LayoutKind.Sequential)]
		private struct INPUT
		{
			public uint type;
			public INPUTUNION union;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct INPUTUNION
		{
			[FieldOffset(0)]
			public KEYBDINPUT keyboard;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct KEYBDINPUT
		{
			public ushort virtualKey;
			public ushort scanCode;
			public uint flags;
			public uint time;
			public IntPtr extraInfo;
		}

		private sealed class CleanupContext
		{
			private readonly List<Action> _actions = new List<Action>();

			public void Add(Action action)
			{
				this._actions.Add(action);
			}

			public IReadOnlyList<Exception> Run()
			{
				var failures = new List<Exception>();
				for (var index = this._actions.Count - 1; index >= 0; index--)
				{
					try
					{
						this._actions[index]();
					}
					catch (Exception ex)
					{
						failures.Add(ex);
					}
				}

				return failures;
			}
		}
	}
}
