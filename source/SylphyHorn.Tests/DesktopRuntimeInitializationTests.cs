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
	public sealed class DesktopRuntimeInitializationTests
	{
		[Fact]
		public async Task InitializationProjectsBeforePublishingAndRequestsSave()
		{
			var harness = Harness.Create(Batch(1, 1, A, Entry(A, 0, "name", "wall")));
			var observed = new List<string>();
			harness.Settings.ProjectionApplied = () => observed.Add("projection");
			harness.Runtime.StateChanged += (_, e) =>
			{
				observed.Add("event");
				Assert.Equal(harness.Settings.Revision, e.SettingsRevision);
				Assert.Equal("name", harness.Settings.LastProjection.Names[0]);
			};

			var result = await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(new[] { "projection", "event" }, observed);
			Assert.Equal(1, harness.Settings.SaveRequests);
			Assert.Equal(A, harness.Runtime.State.CurrentDesktopId);
			var assemblyPath = typeof(DesktopRuntimeInitializationTests).Assembly.Location;
			var expects64Bit = assemblyPath.IndexOf(Path.DirectorySeparatorChar + "x64" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
			Assert.Equal(expects64Bit, Environment.Is64BitProcess);
		}

		[Fact]
		public async Task StartupOverrideMutatesBeforeFirstPublication()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "os", "wall")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "seed", "wall")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "seed" }, new[] { "wall" }, new[] { WallpaperPosition.Fill }));
			var operations = new FakeOperations();
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);
			var events = 0;
			runtime.StateChanged += (_, __) => events++;

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(DesktopStartupOverrideStatus.Completed, result.StartupOverride.Status);
			Assert.NotEqual(Guid.Empty, result.StartupOverride.PlanId);
			Assert.Equal(2, result.StartupOverride.Journal.Count);
			Assert.All(result.StartupOverride.Journal, mutation => Assert.True(mutation.Succeeded));
			Assert.Equal(1, events);
			Assert.Equal("seed", runtime.State.Records[A].Name.Value);
			Assert.Equal("seed", settings.LastProjection.Names[0]);
			Assert.Equal(1, settings.SaveRequests);
		}

		[Fact]
		public async Task StartupOverrideFailureReturnsJournalAndPublishesRecoveredOsState()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "os", "wall")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "recovered", "wall")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "seed" }, null, null));
			var operations = new FakeOperations { NameFailure = new InvalidOperationException("synthetic") };
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(DesktopStartupOverrideStatus.CompletedWithFailures, result.StartupOverride.Status);
			Assert.Single(result.StartupOverride.Journal);
			Assert.False(result.StartupOverride.Journal[0].Succeeded);
			Assert.Equal(A, result.StartupOverride.Journal[0].DesktopId);
			Assert.Equal("recovered", runtime.State.Records[A].Name.Value);
			Assert.Equal("seed", settings.LastProjection.Names[0]);
		}

		[Fact]
		public async Task StartupOverrideCreatesDesktopsToSeedCountBeforeProperties()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "a", "wa")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "a", "wa"), Entry(B, 1, "b", "wb"), Entry(C, 2, "c", "wc")));
			provider.EnqueueResult(Batch(1, 3, A, Entry(A, 0, "sa", "wa"), Entry(B, 1, "sb", "wb"), Entry(C, 2, "sc", "wc")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "sa", "sb", "sc" }, null, new[] { WallpaperPosition.Fill, WallpaperPosition.Fill, WallpaperPosition.Fill }));
			var operations = new FakeOperations();
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(2, operations.CreateCalls);
			Assert.Equal(3, result.StartupOverride.TargetDesktopCount);
			Assert.Equal(2, result.StartupOverride.TopologyJournal.Count);
			Assert.Equal(new[] { A, B, C }, runtime.State.Order);
			Assert.Equal(new[] { "sa", "sb", "sc" }, operations.NameValues);
		}

		[Fact]
		public async Task StartupOverrideRemovesTrailingDesktopsToSeedCount()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "a", "w"), Entry(B, 1, "b", "w"), Entry(C, 2, "c", "w")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "a", "w")));
			provider.EnqueueResult(Batch(1, 3, A, Entry(A, 0, "seed", "w")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "seed" }, null, new[] { WallpaperPosition.Fill }));
			var operations = new FakeOperations();
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(new[] { C, B }, operations.RemovedIds);
			Assert.Single(runtime.State.Order);
		}

		[Fact]
		public async Task StartupOverrideUnavailableDoesNotPublishProjectionOrRemainSubscribed()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "os", "wall")));
			provider.EnqueueResult(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "seed" }, null, null));
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), new FakeOperations());
			var events = 0;
			runtime.StateChanged += (_, __) => events++;

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);
			provider.PublishStable(Batch(1, 2, A, Entry(A, 0, "late", "wall")));

			Assert.Equal(DesktopRuntimeInitializationStatus.Unavailable, result.Status);
			Assert.Equal(DesktopStartupOverrideStatus.Unavailable, result.StartupOverride.Status);
			Assert.False(runtime.IsInitialized);
			Assert.True(provider.Disposed);
			Assert.Equal(0, events);
			Assert.Equal(0, settings.ProjectionCount);
			Assert.Equal(0, settings.SaveRequests);
		}

		[Theory]
		[InlineData("sa")]
		[InlineData("sb")]
		[InlineData("sc")]
		public async Task StartupOverridePreservesFailedSeedAtAnyMutationPosition(string failedValue)
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "a", "w"), Entry(B, 1, "b", "w"), Entry(C, 2, "c", "w")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "oa", "w"), Entry(B, 1, "ob", "w"), Entry(C, 2, "oc", "w")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "sa", "sb", "sc" }, null, null));
			var operations = new FakeOperations { FailNameValue = failedValue };
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(DesktopStartupOverrideStatus.CompletedWithFailures, result.StartupOverride.Status);
			var seeds = new[] { "sa", "sb", "sc" };
			var failedIndex = Array.IndexOf(seeds, failedValue);
			Assert.Equal(failedIndex + 1, operations.NameCalls);
			Assert.Equal(seeds.Take(failedIndex + 1), operations.NameValues);
			Assert.Equal(new[] { "oa", "ob", "oc" }, runtime.State.Order.Select(id => runtime.State.Records[id].Name.Value));
			var expectedProjection = new[] { "oa", "ob", "oc" };
			for (var index = failedIndex; index < expectedProjection.Length; index++) expectedProjection[index] = seeds[index];
			Assert.Equal(expectedProjection, settings.LastProjection.Names);
			Assert.Single(result.StartupOverride.Journal, entry => entry.Status == DesktopOverrideOperationStatus.Failed);
			Assert.Equal(2 - failedIndex, result.StartupOverride.Journal.Count(entry => entry.Status == DesktopOverrideOperationStatus.Skipped));
		}

		[Fact]
		public async Task InitialReconciliationFailureTerminatesRuntimeAndIgnoresLaterBatch()
		{
			var provider = new FakeProvider(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable));
			var settings = new FakeSettings(DesktopStartupSeed.Empty);
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), new FakeOperations());
			var events = 0;
			runtime.StateChanged += (_, __) => events++;

			var result = await runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			provider.PublishStable(Batch(1, 1, A, Entry(A, 0, "late", "wall")));

			Assert.Equal(DesktopRuntimeInitializationStatus.Unavailable, result.Status);
			Assert.True(provider.Disposed);
			Assert.False(runtime.IsInitialized);
			Assert.Equal(0, events);
			Assert.Equal(0, settings.ProjectionCount);
			Assert.Equal(0, settings.SaveRequests);
		}

		[Fact]
		public async Task StartupOverrideIgnoresPositionsOnlyForTopology()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "os", "wall")));
			var settings = new FakeSettings(new DesktopStartupSeed(null, null, new[] { WallpaperPosition.Tile, WallpaperPosition.Center, WallpaperPosition.Stretch }));
			var operations = new FakeOperations();
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(DesktopStartupOverrideStatus.NotRequested, result.StartupOverride.Status);
			Assert.Equal(0, operations.CreateCalls);
			Assert.Empty(operations.RemovedIds);
		}

		[Fact]
		public async Task StartupConfirmationMismatchUsesRawValueAndReportsUnconfirmed()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "old", "wall")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "raw-other", "wall")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "target" }, null, null));
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), new FakeOperations());
			var faults = new List<DesktopRuntimeFault>();
			runtime.Faulted += (_, fault) => faults.Add(fault);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.Equal(DesktopStartupOverrideStatus.CompletedWithFailures, result.StartupOverride.Status);
			Assert.Equal(DesktopOverrideOperationStatus.Unconfirmed, result.StartupOverride.Journal.Single().Status);
			Assert.Equal("raw-other", runtime.State.Records[A].Name.Value);
			Assert.Equal("raw-other", settings.LastProjection.Names[0]);
			Assert.Contains(faults, fault => fault.Category == "Override.Unconfirmed.Name" && fault.DesktopId == A);
		}

		[Fact]
		public async Task StartupProtectionScopesFailedAndUnconfirmedPropertiesOnly()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "old-a", "wall-a"), Entry(B, 1, "old-b", "wall-b")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "raw-a", "raw-wall-a"), Entry(B, 1, "raw-b", "raw-wall-b")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "target-a", "target-b" }, new string[] { null, "target-wall-b" }, null));
			var operations = new FakeOperations { FailNameValue = "target-b" };
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.Equal(new[] { "raw-a", "target-b" }, settings.LastProjection.Names);
			Assert.Equal(new[] { "raw-wall-a", "target-wall-b" }, settings.LastProjection.WallpaperPaths);
		}

		[Theory]
		[InlineData(VirtualDesktopReadStatus.Failed)]
		[InlineData(VirtualDesktopReadStatus.NotAttempted)]
		public async Task StartupConfirmationUnreadableIsUnconfirmedPerProperty(VirtualDesktopReadStatus wallpaperStatus)
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "old", "old-wall")));
			provider.EnqueueResult(new VirtualDesktopStableBatch(1, 2, A, VirtualDesktopReadStatus.Success, new[]
			{
				new VirtualDesktopStableEntry(A, 0, "target-name", VirtualDesktopReadStatus.Success, null, wallpaperStatus),
			}, VirtualDesktopStableReason.ExplicitReconciliation));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "target-name" }, new[] { "target-wall" }, null));
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), new FakeOperations());

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.Equal(DesktopOverrideOperationStatus.Succeeded, result.StartupOverride.Journal.Single(entry => entry.Property == DesktopPropertyKind.Name).Status);
			Assert.Equal(DesktopOverrideOperationStatus.Unconfirmed, result.StartupOverride.Journal.Single(entry => entry.Property == DesktopPropertyKind.WallpaperPath).Status);
			Assert.Equal("target-name", settings.LastProjection.Names[0]);
			Assert.Equal("target-wall", settings.LastProjection.WallpaperPaths[0]);
		}
	}
}
