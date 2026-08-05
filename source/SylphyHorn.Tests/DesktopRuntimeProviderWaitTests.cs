using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using WindowsDesktop;
using Xunit;

using static SylphyHorn.Tests.DesktopRuntimeTestData;

namespace SylphyHorn.Tests
{
	public sealed class DesktopRuntimeProviderWaitTests
	{
		[Fact]
		public async Task ProviderWaitBudgetExpiresWhenProviderIgnoresCancellation()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "os", "wall")))
			{
				NextRequest = new TaskCompletionSource<VirtualDesktopReconciliationResult>(TaskCreationOptions.RunContinuationsAsynchronously).Task,
			};
			var runtime = new DesktopTransitionRuntime(provider, new FakeSettings(DesktopStartupSeed.Empty), new FakeOwner(), new FakeOperations(), TimeSpan.FromMilliseconds(20));

			var result = await runtime.InitializeAsync(false, TestContext.Current.CancellationToken);

			Assert.Equal(DesktopRuntimeInitializationStatus.Unavailable, result.Status);
			Assert.True(provider.Disposed);
		}
		[Fact]
		public async Task StablePublicationCommitWinsCancellationFromStateChangedSubscriber()
		{
			var published = Batch(1, 1, A, Entry(A, 0, "published", "wall"));
			var provider = new FakeProvider(published) { PublishSynchronouslyBeforeRequestCompletion = published };
			var settings = new FakeSettings(DesktopStartupSeed.Empty);
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), new FakeOperations(), TimeSpan.FromSeconds(30));
			using (var cancellation = new CancellationTokenSource())
			{
				runtime.StateChanged += (_, __) => cancellation.Cancel();

				var result = await runtime.InitializeAsync(false, cancellation.Token);

				Assert.True(cancellation.IsCancellationRequested);
				Assert.Equal(DesktopRuntimeInitializationStatus.Completed, result.Status);
			}
			Assert.Equal("published", runtime.State.Records[A].Name.Value);
			Assert.Equal("published", settings.LastProjection.Names[0]);
			Assert.Equal(1, settings.SaveRequests);
			Assert.False(provider.Disposed);
		}

		[Fact]
		public async Task CallerCancellationWinsWhenProviderIgnoresTokenAndLateSuccessIsIgnored()
		{
			var late = new TaskCompletionSource<VirtualDesktopReconciliationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "unused", "wall"))) { NextRequest = late.Task };
			var settings = new FakeSettings(DesktopStartupSeed.Empty);
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), new FakeOperations(), TimeSpan.FromSeconds(30));
			using (var cancellation = new CancellationTokenSource())
			{
				var initialize = runtime.InitializeAsync(false, cancellation.Token);
				cancellation.Cancel();
				var result = await initialize;
				Assert.Equal(DesktopRuntimeInitializationStatus.Cancelled, result.Status);
			}
			late.TrySetResult(VirtualDesktopReconciliationResult.Succeeded(Batch(1, 2, A, Entry(A, 0, "late", "wall"))));
			await Task.Yield();
			Assert.Null(runtime.State);
			Assert.Equal(0, settings.ProjectionCount);
			Assert.Equal(0, settings.SaveRequests);
			Assert.True(provider.Disposed);
		}
	}
}
