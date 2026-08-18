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
	public sealed class DesktopRuntimeLocalEditTests
	{
		[Fact]
		public async Task LocalEditCallsOperationBeforeCommitAndPublishesOneSnapshot()
		{
			var harness = await Harness.Initialized();
			var observed = new List<string>();
			harness.Operations.BeforeName = () =>
			{
				observed.Add("operation");
				Assert.Equal("name", harness.Runtime.State.Records[A].Name.Value);
			};
			harness.Runtime.StateChanged += (_, e) =>
			{
				if (e.Change.Kind != DesktopStateChangeKind.LocalEdit) return;
				observed.Add("event");
				Assert.Equal("edited", e.Change.Snapshot.Records[A].Name.Value);
			};
			var savesBefore = harness.Settings.SaveRequests;

			harness.Runtime.EditName(A, "edited");

			Assert.Equal(new[] { "operation", "event" }, observed);
			Assert.Equal("edited", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(savesBefore + 1, harness.Settings.SaveRequests);
			Assert.Equal(1, harness.Operations.NameCalls);
		}

		[Fact]
		public async Task FailedLocalEditDoesNotCommitProjectionOrSave()
		{
			var harness = await Harness.Initialized();
			harness.Operations.NameFailure = new InvalidOperationException("synthetic");
			var state = harness.Runtime.State;
			var saves = harness.Settings.SaveRequests;
			var events = 0;
			harness.Runtime.StateChanged += (_, __) => events++;

			harness.Runtime.EditName(A, "rejected");

			Assert.Same(state, harness.Runtime.State);
			Assert.Equal("name", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(saves, harness.Settings.SaveRequests);
			Assert.Equal(0, events);
			Assert.Equal(1, harness.Operations.NameCalls);
		}

		[Fact]
		public async Task SupportedWallpaperRejectsEmptyLocalEditWithoutOperationOrPublication()
		{
			var harness = await Harness.Initialized();
			var state = harness.Runtime.State;
			var saves = harness.Settings.SaveRequests;
			var events = 0;
			harness.Runtime.StateChanged += (_, __) => events++;

			harness.Runtime.EditWallpaperPath(A, string.Empty);

			Assert.Same(state, harness.Runtime.State);
			Assert.Equal("wall", harness.Runtime.State.Records[A].WallpaperPath.Value);
			Assert.Equal(0, harness.Operations.WallpaperCalls);
			Assert.Equal(saves, harness.Settings.SaveRequests);
			Assert.Equal(0, events);
		}

		[Fact]
		public async Task UnsupportedWallpaperPreservesEmptyApplicationAuthoritativeEdit()
		{
			var initial = new VirtualDesktopStableBatch(1, 1, A, VirtualDesktopReadStatus.Success, new[]
			{
				new VirtualDesktopStableEntry(A, 0, "name", VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported),
			}, VirtualDesktopStableReason.Initialization);
			var harness = Harness.Create(initial);
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);

			harness.Runtime.EditWallpaperPath(A, string.Empty);

			Assert.Equal(string.Empty, harness.Runtime.State.Records[A].WallpaperPath.Value);
			Assert.Equal(DesktopPropertyAuthority.ApplicationAuthoritative, harness.Runtime.State.Records[A].WallpaperPath.Authority);
			Assert.Equal(0, harness.Operations.WallpaperCalls);
			Assert.Equal(new[] { string.Empty }, harness.Operations.AppliedWallpaperValues);
		}

		[Fact]
		public async Task CurrentOnlyUsesIdAndDoesNotProjectOrSave()
		{
			var harness = Harness.Create(Batch(1, 1, A, Entry(A, 0, "a", "wa"), Entry(B, 1, "b", "wb")));
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			var projections = harness.Settings.ProjectionCount;
			var saves = harness.Settings.SaveRequests;
			DesktopRuntimeStateChanged change = null;
			harness.Runtime.StateChanged += (_, e) => change = e;

			harness.Provider.PublishCurrent(new VirtualDesktopCurrentTransition(1, 1, 1, B));

			Assert.Equal(B, harness.Runtime.State.CurrentDesktopId);
			Assert.Equal(DesktopStateChangeKind.CurrentChanged, change.Change.Kind);
			Assert.Equal(projections, harness.Settings.ProjectionCount);
			Assert.Equal(saves, harness.Settings.SaveRequests);
		}

		[Fact]
		public async Task ReentrantEditIsDeferredToNextOwnerTurn()
		{
			var harness = await Harness.Initialized();
			var callsDuringPublication = 0;
			harness.Runtime.StateChanged += (_, e) =>
			{
				if (e.Change.Kind != DesktopStateChangeKind.LocalEdit || e.Change.Snapshot.Records[A].Name.Value != "b") return;
				harness.Runtime.EditName(A, "c");
				callsDuringPublication = harness.Operations.NameCalls;
			};

			harness.Runtime.EditName(A, "b");

			Assert.Equal(1, callsDuringPublication);
			Assert.Equal("b", harness.Runtime.State.Records[A].Name.Value);
			Assert.Single(harness.Owner.Posted);
			harness.Owner.Drain();
			Assert.Equal("c", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(2, harness.Operations.NameCalls);
		}

		[Fact]
		public async Task DeferredCommandsRemainFifoUntilPostedDrainCompletes()
		{
			var harness = await Harness.Initialized();
			var injected = false;
			harness.Runtime.StateChanged += (_, __) =>
			{
				if (injected) return;
				injected = true;
				harness.Runtime.EditName(A, "C");
			};

			harness.Runtime.EditName(A, "B");
			harness.Runtime.EditName(A, "D");
			harness.Owner.Drain();

			Assert.Equal(new[] { "B", "C", "D" }, harness.Operations.NameValues);
			Assert.Equal("D", harness.Runtime.State.Records[A].Name.Value);
		}

		[Fact]
		public void PreviewBackgroundIsClearedForEmptyUnknownAndMissingCurrent()
		{
			var coordinator = new DesktopTransitionCoordinator(DesktopStartupSeed.Empty);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, "a", "path")));
			Assert.Equal("path", DesktopTransitionRuntime.GetCurrentWallpaperPath(coordinator.State));

			coordinator.ApplyStableBatch(Batch(1, 2, A, Entry(A, 0, "a", "")));
			Assert.Null(DesktopTransitionRuntime.GetCurrentWallpaperPath(coordinator.State));

			var unknown = new VirtualDesktopStableEntry(B, 1, "b", VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Failed);
			coordinator.ApplyStableBatch(Batch(1, 3, B, Entry(A, 0, "a", ""), unknown));
			Assert.Null(DesktopTransitionRuntime.GetCurrentWallpaperPath(coordinator.State));
			Assert.Null(DesktopTransitionRuntime.GetCurrentWallpaperPath(null));
		}
		[Fact]
		public async Task ReentrantDeferredCommandRequiresAnotherOwnerTurn()
		{
			var harness = await Harness.Initialized();
			harness.Runtime.StateChanged += (_, __) =>
			{
				var value = harness.Runtime.State.Records[A].Name.Value;
				if (value == "outer") harness.Runtime.EditName(A, "B");
				else if (value == "B") harness.Runtime.EditName(A, "C");
			};
			harness.Runtime.EditName(A, "outer");

			harness.Owner.DrainOne();
			Assert.Equal("B", harness.Runtime.State.Records[A].Name.Value);
			Assert.Single(harness.Owner.Posted);
			harness.Owner.DrainOne();
			Assert.Equal("C", harness.Runtime.State.Records[A].Name.Value);
		}

		[Fact]
		public async Task RejectedOwnerPostClassifiesDeferredCommandAsAborted()
		{
			var harness = await Harness.Initialized();
			var faults = new List<DesktopRuntimeFault>();
			harness.Runtime.Faulted += (_, fault) => faults.Add(fault);
			harness.Owner.RejectPost = true;
			harness.Runtime.StateChanged += (_, __) => harness.Runtime.EditName(A, "reentrant");

			harness.Runtime.EditName(A, "outer");

			Assert.Equal(new[] { "outer" }, harness.Operations.NameValues);
			Assert.Contains(faults, fault => fault.Category == "DeferredCommand.Aborted");
			Assert.Empty(harness.Owner.Posted);
		}

		[Fact]
		public async Task NullSeedIsUnspecifiedWhenDifferentPropertyFails()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "old-name", "old-wall")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "raw-name", "raw-wall")));
			var settings = new FakeSettings(new DesktopStartupSeed(new string[] { null }, new[] { "target-wall" }, null));
			var operations = new FakeOperations { FailWallpaperValue = "target-wall" };
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);

			await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.Equal("raw-name", settings.LastProjection.Names[0]);
			Assert.Equal("target-wall", settings.LastProjection.WallpaperPaths[0]);
		}

		[Fact]
		public async Task ProtectedUnknownReleasesAfterUnknownNaturalMatch()
		{
			var initial = new VirtualDesktopStableBatch(1, 1, A, VirtualDesktopReadStatus.Success, new[]
			{
				new VirtualDesktopStableEntry(A, 0, null, VirtualDesktopReadStatus.Failed, "wall", VirtualDesktopReadStatus.Success),
			}, VirtualDesktopStableReason.Initialization);
			var harness = Harness.Create(initial);
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
			};
			harness.Operations.FailNameValue = "imported";
			harness.Provider.EnqueueResult(new VirtualDesktopStableBatch(1, 2, A, VirtualDesktopReadStatus.Success, new[]
			{
				new VirtualDesktopStableEntry(A, 0, null, VirtualDesktopReadStatus.Failed, "wall", VirtualDesktopReadStatus.Success),
			}, VirtualDesktopStableReason.Recovery));
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			harness.Provider.PublishStable(Batch(1, 3, A, Entry(A, 0, "later-raw", "wall")));

			Assert.Equal("later-raw", harness.Settings.LastProjection.Names[0]);
		}
		[Fact]
		public async Task FailedLocalEditKeepsProtectionAndSuccessfulEditReleasesOnlyEditedProperty()
		{
			var provider = new FakeProvider(Batch(1, 1, A, Entry(A, 0, "old", "old-wall")));
			provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "raw", "raw-wall")));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "protected-name" }, new[] { "protected-wall" }, null));
			var operations = new FakeOperations { FailNameValue = "protected-name" };
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);
			await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);
			runtime.EditName(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "rejected");
			provider.PublishStable(Batch(1, 3, A, Entry(A, 0, "rejected-later", "rejected-wall")));
			Assert.Equal("protected-name", settings.LastProjection.Names[0]);
			Assert.Equal("protected-wall", settings.LastProjection.WallpaperPaths[0]);

			operations.FailNameValue = "failed-edit";
			runtime.EditName(A, "failed-edit");
			provider.PublishStable(Batch(1, 4, A, Entry(A, 0, "later-raw", "later-wall")));
			Assert.Equal("protected-name", settings.LastProjection.Names[0]);
			Assert.Equal("protected-wall", settings.LastProjection.WallpaperPaths[0]);

			operations.FailNameValue = null;
			runtime.EditName(A, "explicit-name");
			Assert.Equal("explicit-name", settings.LastProjection.Names[0]);
			Assert.Equal("protected-wall", settings.LastProjection.WallpaperPaths[0]);
		}

		[Fact]
		public async Task UnsupportedWallpaperStartupIsApplicationAuthoritativeButUnsupportedNameIsNotSet()
		{
			var initial = Batch(1, 1, A,
				new VirtualDesktopStableEntry(A, 0, null, VirtualDesktopReadStatus.Unsupported, null, VirtualDesktopReadStatus.Unsupported),
				new VirtualDesktopStableEntry(B, 1, null, VirtualDesktopReadStatus.Unsupported, null, VirtualDesktopReadStatus.Unsupported));
			var provider = new FakeProvider(initial);
			provider.EnqueueResult(Batch(1, 2, A,
				new VirtualDesktopStableEntry(A, 0, null, VirtualDesktopReadStatus.Unsupported, null, VirtualDesktopReadStatus.Unsupported),
				new VirtualDesktopStableEntry(B, 1, null, VirtualDesktopReadStatus.Unsupported, null, VirtualDesktopReadStatus.Unsupported)));
			var settings = new FakeSettings(new DesktopStartupSeed(new[] { "name-a", "name-b" }, new[] { "wall-a", "wall-b" }, null));
			var operations = new FakeOperations();
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);
			var faults = new List<DesktopRuntimeFault>();
			runtime.Faulted += (_, fault) => faults.Add(fault);

			var result = await runtime.InitializeAsync(true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(DesktopStartupOverrideStatus.Completed, result.StartupOverride.Status);
			Assert.Equal(0, operations.NameCalls);
			Assert.Equal(0, operations.WallpaperCalls);
			Assert.Equal(new[] { A }, operations.AppliedWallpaperIds);
			Assert.Equal("wall-a", runtime.State.Records[A].WallpaperPath.Value);
			Assert.Equal("wall-b", runtime.State.Records[B].WallpaperPath.Value);
			Assert.All(result.StartupOverride.Journal.Where(entry => entry.Property == DesktopPropertyKind.WallpaperPath), entry => Assert.Equal(DesktopOverrideOperationStatus.Succeeded, entry.Status));
			Assert.All(result.StartupOverride.Journal.Where(entry => entry.Property == DesktopPropertyKind.Name), entry => Assert.Equal(DesktopOverrideOperationStatus.Skipped, entry.Status));
			Assert.DoesNotContain(faults, fault => fault.Category.StartsWith("Override.Unconfirmed.", StringComparison.Ordinal));
		}

	}
}
