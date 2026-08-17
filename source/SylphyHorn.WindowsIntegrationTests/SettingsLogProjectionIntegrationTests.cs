using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SylphyHorn.Services;
using SylphyHorn.UI.Bindings;
using Xunit;

namespace SylphyHorn.WindowsIntegrationTests
{
	[Collection(WindowsHookCollection.Name)]
	public class SettingsLogProjectionIntegrationTests
	{
		private const int TimeoutMilliseconds = 10000;

		[WpfFact(Timeout = TimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public void BackgroundRegisterAfterCompletedDrainPostsAndAppliesEntry()
		{
			var logs = new ObservableCollection<LogViewModel>();
			using (var projection = new SettingsLogProjection(LoggingService.Instance, Dispatcher.CurrentDispatcher, logs))
			{
				PumpDispatcher();
				var first = new TestLog("dispatcher-completed-drain-first");
				var second = new TestLog("dispatcher-completed-drain-second");

				Assert.True(Task.Run(() => LoggingService.Instance.Register(first)).Wait(TimeoutMilliseconds));
				PumpDispatcher();
				Assert.Equal(1, logs.Count(log => log.Content == first.Content));

				PumpDispatcher();
				Assert.True(Task.Run(() => LoggingService.Instance.Register(second)).Wait(TimeoutMilliseconds));
				PumpDispatcher();
				Assert.Equal(1, logs.Count(log => log.Content == second.Content));
			}
		}

		[WpfFact(Timeout = TimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public void DisposeBeforePostedDrainPreventsLogCollectionMutation()
		{
			var logs = new ObservableCollection<LogViewModel>();
			var projection = new SettingsLogProjection(LoggingService.Instance, Dispatcher.CurrentDispatcher, logs);

			projection.Dispose();
			PumpDispatcher();

			Assert.Empty(logs);
		}

		[WpfFact(Timeout = TimeoutMilliseconds)]
		[Trait(
			IntegrationTestExecutionEnvironment.TraitName,
			IntegrationTestExecutionEnvironment.HostedCI)]
		public void RegisterDuringDispatcherShutdownDoesNotLeakPostException()
		{
			Dispatcher dispatcher = null;
			var dispatcherReady = new ManualResetEventSlim();
			var thread = new Thread(() =>
			{
				dispatcher = Dispatcher.CurrentDispatcher;
				dispatcherReady.Set();
				Dispatcher.Run();
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			Assert.True(dispatcherReady.Wait(TimeoutMilliseconds));
			dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
			Assert.True(thread.Join(TimeoutMilliseconds));
			Assert.True(dispatcher.HasShutdownFinished);

			var logs = new ObservableCollection<LogViewModel>();
			using (var projection = new SettingsLogProjection(LoggingService.Instance, dispatcher, logs))
			{
				LoggingService.Instance.Register(new TestLog("dispatcher-shutdown"));
				Assert.Empty(logs);
			}
		}

		private static void PumpDispatcher()
		{
			Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
		}

		private sealed class TestLog : ILog
		{
			public DateTimeOffset DateTime { get; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

			public string Header { get; }

			public string Content => this.Header;

			internal TestLog(string header)
			{
				this.Header = header;
			}
		}
	}
}
