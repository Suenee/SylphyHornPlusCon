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
	public sealed class DesktopRuntimeShutdownTests
	{
		[Fact]
		public async Task ShutdownFlushesLatestStateAndDisposesProvider()
		{
			var harness = await Harness.Initialized();
			harness.Runtime.EditName(A, "latest");
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "final-os", "wall")));
			var saves = harness.Settings.SaveRequests;

			await harness.Runtime.ShutdownAsync();
			var result = await harness.Runtime.RequestReconciliationAsync(TestContext.Current.CancellationToken);

			Assert.True(harness.Provider.Disposed);
			Assert.True(harness.Settings.SaveRequests > saves);
			Assert.Equal("final-os", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal("final-os", harness.Settings.LastProjection.Names[0]);
			Assert.Equal(VirtualDesktopReconciliationStatus.ShuttingDown, result.Status);
		}

		[Fact]
		public async Task ShutdownUnavailablePreservesLastConfirmedProjectionAndDoesNotSave()
		{
			var harness = await Harness.Initialized();
			var projection = harness.Settings.LastProjection;
			var saves = harness.Settings.SaveRequests;

			var result = await harness.Runtime.ShutdownAsync();

			Assert.Equal(DesktopRuntimeShutdownStatus.ReconciliationUnavailable, result.Status);
			Assert.Same(projection, harness.Settings.LastProjection);
			Assert.Equal(saves, harness.Settings.SaveRequests);
			Assert.True(harness.Provider.Disposed);
		}

		[Fact]
		public async Task ShutdownReportsSaveFailureBeforeDisposingProvider()
		{
			var harness = await Harness.Initialized();
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "final", "wall")));
			harness.Settings.Provider.SaveFailure = new IOException("synthetic");

			var result = await harness.Runtime.ShutdownAsync();

			Assert.Equal(DesktopRuntimeShutdownStatus.SaveFailed, result.Status);
			Assert.NotNull(result.SaveResult);
			Assert.False(result.SaveResult.Succeeded);
			Assert.True(harness.Provider.Disposed);
		}

	}
}
