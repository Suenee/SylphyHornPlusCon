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
	public sealed class DesktopRuntimeImportTests
	{
		[Fact]
		public async Task PreparedImportKeepsActiveStateUntilAtomicCommit()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
				[SettingsService.DesktopPositionsKey + "#Count"] = 1,
				[SettingsService.DesktopPositionsKey + "[0]"] = (byte)WallpaperPosition.Tile,
			};
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			var gate = new TaskCompletionSource<VirtualDesktopReconciliationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			harness.Provider.NextRequest = gate.Task;
			var before = harness.Runtime.State;
			var transaction = harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);
			var reloads = 0;
			harness.Settings.Provider.Reloaded += (_, __) => { reloads++; Assert.Equal("imported", harness.Runtime.State.Records[A].Name.Value); };

			Assert.Same(before, harness.Runtime.State);
			Assert.False(transaction.IsCompleted);
			Assert.Equal("name", harness.Runtime.State.Records[A].Name.Value);
			harness.Provider.PublishStable(Batch(1, 2, A, Entry(A, 0, "imported", "wall")));
			gate.SetResult(VirtualDesktopReconciliationResult.Succeeded(Batch(1, 2, A, Entry(A, 0, "imported", "wall"))));
			var result = await transaction;

			Assert.True(result.Succeeded);
			Assert.Equal("imported", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(WallpaperPosition.Tile, harness.Runtime.State.Records[A].WallpaperPosition);
			Assert.Equal(1, harness.Operations.NameCalls);
			Assert.Equal(1, reloads);
		}

		[Fact]
		public async Task CancelledPreparedImportDoesNotChangeActiveState()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			var before = harness.Runtime.State;
			using (var cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();
				var result = await harness.Runtime.CommitPreparedImportAsync(stage, false, cancellation.Token);
				Assert.Equal(SettingsImportCommitStatus.Cancelled, result.Status);
			}
			Assert.Same(before, harness.Runtime.State);
			Assert.False(harness.Settings.Provider.ImportTransactionActive);
		}

		[Fact]
		public async Task ResetUsesSameStagedCommitAndKeepsDesktopProjection()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.SetValue("Unrelated", "old");

			var result = await harness.Runtime.ResetSettingsAsync(TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.False(harness.Settings.Provider.TryGetValue("Unrelated", out string _));
			Assert.True(harness.Settings.Provider.TryGetValue(SettingsService.DesktopNamesKey + "#Count", out int count));
			Assert.Equal(1, count);
		}

		[Fact]
		public async Task ImportConflictPreservesActiveDictionaryAndSuppressesReload()
		{
			var provider = new TestDictionaryProvider { ContentHash = "before", NextImport = new Dictionary<string, object> { ["Imported"] = "value" } };
			await provider.InitializeAsync();
			provider.SetValue("Active", "original");
			var reloads = 0;
			provider.Reloaded += (_, __) => reloads++;
			var stage = await provider.PrepareImportAsync("synthetic");
			provider.ContentHash = "changed";
			var result = await provider.CommitStagedImportAsync(stage, stage.CreateCommitDictionary());
			Assert.Equal(SettingsImportCommitStatus.Conflict, result.Status);
			Assert.True(provider.TryGetValue("Active", out string active));
			Assert.Equal("original", active);
			Assert.False(provider.TryGetValue("Imported", out string _));
			Assert.Equal(0, reloads);
		}

		[Fact]
		public async Task ImportPublishFailurePreservesActiveDictionaryAndSuppressesReload()
		{
			var provider = new TestDictionaryProvider { NextImport = new Dictionary<string, object> { ["Imported"] = "value" }, SaveFailure = new IOException("synthetic") };
			await provider.InitializeAsync();
			provider.SetValue("Active", "original");
			var reloads = 0;
			provider.Reloaded += (_, __) => reloads++;
			var stage = await provider.PrepareImportAsync("synthetic");
			var result = await provider.CommitStagedImportAsync(stage, stage.CreateCommitDictionary());
			Assert.Equal(SettingsImportCommitStatus.PublishFailed, result.Status);
			Assert.True(provider.TryGetValue("Active", out string active));
			Assert.Equal("original", active);
			Assert.False(provider.TryGetValue("Imported", out string _));
			Assert.Equal(0, reloads);
		}

		[Fact]
		public async Task ImportFreezesDiskStateAndReplaysStableIngressAfterCommit()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.SavedDictionaries.Clear();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Settings.BlockCommit = true;
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, false, TestContext.Current.CancellationToken);
			await harness.Settings.CommitStarted.Task;
			harness.Provider.PublishStable(Batch(1, 2, A, Entry(A, 0, "after", "wall")));
			harness.Settings.CommitRelease.TrySetResult(true);

			var result = await commit;

			Assert.True(result.Succeeded);
			Assert.Equal("after", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal("after", harness.Settings.LastProjection.Names[0]);
			Assert.True(harness.Settings.Provider.SavedDictionaries.Count >= 2);
			var last = SettingsService.CaptureDesktopStartupSeed(new Dictionary<string, object>(harness.Settings.Provider.SavedDictionaries.Last()));
			Assert.Equal("after", last.Names[0]);
		}

		[Fact]
		public async Task ResetRestoresEveryWallpaperPositionToFillWithoutDesktopMutation()
		{
			var harness = await Harness.Initialized();
			harness.Runtime.EditWallpaperPosition(A, WallpaperPosition.Tile);
			var result = await harness.Runtime.ResetSettingsAsync(TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(WallpaperPosition.Fill, harness.Runtime.State.Records[A].WallpaperPosition);
			var resetSeed = SettingsService.CaptureDesktopStartupSeed(new Dictionary<string, object>(harness.Settings.Provider.SavedDictionaries.Last()));
			Assert.Equal(WallpaperPosition.Fill, resetSeed.Positions[0]);
			Assert.Equal(0, harness.Operations.NameCalls);
			Assert.Equal(0, harness.Operations.CreateCalls);
			Assert.Empty(harness.Operations.RemovedIds);
		}

		[Fact]
		public async Task ImportReplaysCurrentTransitionAfterFrozenStateCommit()
		{
			var harness = Harness.Create(Batch(1, 1, A, Entry(A, 0, "a", "w"), Entry(B, 1, "b", "w")));
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Settings.BlockCommit = true;
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, false, TestContext.Current.CancellationToken);
			await harness.Settings.CommitStarted.Task;
			harness.Provider.PublishCurrent(new VirtualDesktopCurrentTransition(1, 1, 1, B));
			Assert.Equal(A, harness.Runtime.State.CurrentDesktopId);
			harness.Settings.CommitRelease.TrySetResult(true);

			var result = await commit;

			Assert.True(result.Succeeded);
			Assert.Equal(B, harness.Runtime.State.CurrentDesktopId);
		}
		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public async Task ImportOverrideReusesTopologyPlanForLargerAndSmallerTargets(bool grow)
		{
			var initial = grow
				? Batch(1, 1, A, Entry(A, 0, "a", "w"))
				: Batch(1, 1, A, Entry(A, 0, "a", "w"), Entry(B, 1, "b", "w"));
			var harness = Harness.Create(initial);
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			var names = grow ? new[] { "ia", "ib" } : new[] { "ia" };
			var imported = new Dictionary<string, object> { [SettingsService.DesktopNamesKey + "#Count"] = names.Length };
			for (var index = 0; index < names.Length; index++) imported[SettingsService.DesktopNamesKey + "[" + index + "]"] = names[index];
			harness.Settings.Provider.NextImport = imported;
			var topology = grow
				? Batch(1, 2, A, Entry(A, 0, "a", "w"), Entry(B, 1, "b", "w"))
				: Batch(1, 2, A, Entry(A, 0, "a", "w"));
			var confirmed = grow
				? Batch(1, 3, A, Entry(A, 0, "ia", "w"), Entry(B, 1, "ib", "w"))
				: Batch(1, 3, A, Entry(A, 0, "ia", "w"));
			harness.Provider.EnqueueResult(topology);
			harness.Provider.EnqueueResult(confirmed);
			var stage = await harness.Settings.PrepareImportAsync("synthetic");

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal(names.Length, harness.Runtime.State.Order.Count);
			Assert.Equal(grow ? 1 : 0, harness.Operations.CreateCalls);
			Assert.Equal(grow ? 0 : 1, harness.Operations.RemovedIds.Count);
		}
		[Fact]
		public async Task ImportMutationFailureUsesStableOsRuntimeAndProtectsPreImportProjection()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
			};
			harness.Operations.FailNameValue = "imported";
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "os-after", "wall")));
			var stage = await harness.Settings.PrepareImportAsync("synthetic");

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			Assert.Equal(SettingsImportCommitStatus.CompletedWithFailures, result.Status);
			Assert.Equal("os-after", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal("name", harness.Settings.LastProjection.Names[0]);
			Assert.False(harness.Settings.Provider.ImportTransactionActive);
		}

		[Fact]
		public async Task PublishingImportCannotBeDiscardedOrSupersededByAnotherStage()
		{
			var provider = new ControlledSaveProvider();
			await provider.LoadAsync();
			var stage = await provider.PrepareImportAsync("synthetic");
			var dictionary = stage.CreateCommitDictionary();
			dictionary["Value"] = "published";
			var commit = provider.CommitStagedImportAsync(stage, dictionary);
			var write = await provider.NextWriteAsync();

			Assert.Equal(SettingsImportCommitStatus.Publishing, provider.DiscardStagedImport(stage).Status);
			await Assert.ThrowsAsync<InvalidOperationException>(() => provider.PrepareImportAsync("second"));
			write.Complete();
			var result = await commit;

			Assert.Equal(SettingsImportCommitStatus.Completed, result.Status);
			Assert.True(provider.TryGetValue("Value", out string value));
			Assert.Equal("published", value);
			Assert.False(provider.ImportTransactionActive);
		}

		[Fact]
		public async Task FailedImportProtectsAllAttemptedPreImportPropertiesButRuntimeUsesRawOsState()
		{
			var harness = Harness.Create(Batch(1, 1, A, Entry(A, 0, "pre-a", "wall-a"), Entry(B, 1, "pre-b", "wall-b")));
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 2,
				[SettingsService.DesktopNamesKey + "[0]"] = "import-a",
				[SettingsService.DesktopNamesKey + "[1]"] = "import-b",
			};
			harness.Operations.FailNameValue = "import-b";
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "import-a", "wall-a"), Entry(B, 1, "raw-b", "wall-b")));
			var stage = await harness.Settings.PrepareImportAsync("synthetic");

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			Assert.Equal(SettingsImportCommitStatus.CompletedWithFailures, result.Status);
			Assert.Equal(new[] { "import-a", "raw-b" }, harness.Runtime.State.Order.Select(id => harness.Runtime.State.Records[id].Name.Value));
			Assert.Equal(new[] { "pre-a", "pre-b" }, harness.Settings.LastProjection.Names);
		}
		[Fact]
		public async Task TopologyMutationSupersededByResetProtectsOnlySameGuidInNewEpoch()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 2,
				[SettingsService.DesktopNamesKey + "[0]"] = "import-a",
				[SettingsService.DesktopNamesKey + "[1]"] = "import-b",
			};
			harness.Provider.EnqueueResult(VirtualDesktopReconciliationResult.SupersededByReset(2));
			var stage = await harness.Settings.PrepareImportAsync("synthetic");

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);
			harness.Provider.PublishStable(Batch(2, 1, A, Entry(A, 0, "new-a", "new-wall-a"), Entry(B, 1, "new-b", "new-wall-b")));

			Assert.Equal(SettingsImportCommitStatus.SupersededByReset, result.Status);
			Assert.Equal(new[] { "name", "new-b" }, harness.Settings.LastProjection.Names);
			Assert.Equal(new[] { "wall", "new-wall-b" }, harness.Settings.LastProjection.WallpaperPaths);
		}
		[Fact]
		public async Task FailedImportWithoutRecoveryProtectsPreImportProjectionFromLaterStableBatch()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
			};
			harness.Provider.EnqueueResult(VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable));
			var stage = await harness.Settings.PrepareImportAsync("synthetic");

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);
			harness.Provider.PublishStable(Batch(1, 2, A, Entry(A, 0, "imported", "wall")));

			Assert.Equal(SettingsImportCommitStatus.FailedWithoutStableState, result.Status);
			Assert.Equal("imported", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal("name", harness.Settings.LastProjection.Names[0]);
			await harness.Settings.RequestSaveAsync(harness.Runtime.State.StateRevision);
			var saved = SettingsService.CaptureDesktopStartupSeed(new Dictionary<string, object>(harness.Settings.Provider.SavedDictionaries.Last()));
			Assert.Equal("name", saved.Names[0]);
		}

		[Fact]
		public async Task UnsupportedWallpaperImportCommitsAllTargetsAndAppliesOnlyCurrentDesktop()
		{
			var provider = new FakeProvider(Batch(1, 1, A, WallpaperUnsupported(A, 0, "a"), WallpaperUnsupported(B, 1, "b")));
			var settings = new FakeSettings(DesktopStartupSeed.Empty);
			var operations = new FakeOperations();
			var runtime = new DesktopTransitionRuntime(provider, settings, new FakeOwner(), operations);
			await runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopWallpaperPathsKey + "#Count"] = 2,
				[SettingsService.DesktopWallpaperPathsKey + "[0]"] = "import-a",
				[SettingsService.DesktopWallpaperPathsKey + "[1]"] = "import-b",
			};
			provider.EnqueueResult(Batch(1, 2, A, WallpaperUnsupported(A, 0, "a"), WallpaperUnsupported(B, 1, "b")));
			var faults = new List<DesktopRuntimeFault>();
			runtime.Faulted += (_, fault) => faults.Add(fault);
			var stage = await settings.PrepareImportAsync("synthetic");

			var result = await runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			Assert.Equal(SettingsImportCommitStatus.Completed, result.Status);
			Assert.Equal("import-a", runtime.State.Records[A].WallpaperPath.Value);
			Assert.Equal("import-b", runtime.State.Records[B].WallpaperPath.Value);
			Assert.True(settings.Provider.TryGetValue(SettingsService.DesktopWallpaperPathsKey + "#Count", out int wallpaperCount));
			Assert.Equal(2, wallpaperCount);
			Assert.True(settings.Provider.TryGetValue(SettingsService.DesktopWallpaperPathsKey + "[0]", out string persistedA));
			Assert.True(settings.Provider.TryGetValue(SettingsService.DesktopWallpaperPathsKey + "[1]", out string persistedB));
			Assert.Equal("import-a", persistedA);
			Assert.Equal("import-b", persistedB);
			Assert.Equal(0, operations.WallpaperCalls);
			Assert.Equal(new[] { A }, operations.AppliedWallpaperIds);
			Assert.Equal(new[] { "import-a" }, operations.AppliedWallpaperValues);
			Assert.DoesNotContain(faults, fault => fault.Category.StartsWith("Override.Unconfirmed.", StringComparison.Ordinal));

			provider.PublishCurrent(new VirtualDesktopCurrentTransition(1, 100, runtime.State.ProviderSnapshotRevision, B));
			Assert.Equal(B, runtime.State.CurrentDesktopId);
			Assert.Equal("import-b", runtime.State.Records[B].WallpaperPath.Value);
		}

	}
}
