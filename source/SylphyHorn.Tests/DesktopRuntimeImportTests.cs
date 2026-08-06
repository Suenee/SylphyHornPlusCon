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

		[Fact]
		public async Task ImportOutcomeFactoriesRejectInvalidLifecycleCombinations()
		{
			var settings = new FakeSettings(DesktopStartupSeed.Empty);
			var stage = await settings.PrepareImportAsync("synthetic");
			var success = await settings.CommitImportAsync(settings.ClaimImport(stage), stage.CreateCommitDictionary());
			var coordinator = new DesktopTransitionCoordinator(DesktopStartupSeed.Empty);
			var batch = Batch(1, 1, A, Entry(A, 0, "name", "wall"));
			coordinator.ApplyStableBatch(batch);
			var prepared = coordinator.BeginStagedRuntime();
			var completedWithFailures = SettingsImportCommitResult.CompletedWithFailures();
			var cancelled = SettingsImportCommitResult.Cancelled();

			var commit = DesktopImportTransactionOutcome.CommitPreparedRuntime(success, prepared);
			var recovered = DesktopImportTransactionOutcome.ApplyRecoveredState(completedWithFailures, batch, null);
			var discarded = DesktopImportTransactionOutcome.Discarded(cancelled);
			var reconcile = DesktopImportTransactionOutcome.DiscardedAndReconcile(cancelled);

			Assert.Equal(DesktopImportTransactionOutcomeKind.CommitPreparedRuntime, commit.Kind);
			Assert.Same(prepared, commit.PreparedRuntime);
			Assert.Equal(DesktopImportTransactionOutcomeKind.ApplyRecoveredState, recovered.Kind);
			Assert.Same(batch, recovered.RecoveryBatch);
			Assert.Equal(DesktopImportTransactionOutcomeKind.Discarded, discarded.Kind);
			Assert.Equal(DesktopImportTransactionOutcomeKind.DiscardedAndReconcile, reconcile.Kind);
			Assert.Throws<ArgumentNullException>(() => DesktopImportTransactionOutcome.CommitPreparedRuntime(null, prepared));
			Assert.Throws<ArgumentNullException>(() => DesktopImportTransactionOutcome.CommitPreparedRuntime(success, null));
			Assert.Throws<ArgumentException>(() => DesktopImportTransactionOutcome.CommitPreparedRuntime(cancelled, prepared));
			Assert.Throws<ArgumentNullException>(() => DesktopImportTransactionOutcome.ApplyRecoveredState(completedWithFailures, null, null));
			Assert.Throws<ArgumentException>(() => DesktopImportTransactionOutcome.ApplyRecoveredState(cancelled, batch, null));
			Assert.Throws<ArgumentException>(() => DesktopImportTransactionOutcome.Discarded(success));
			Assert.Throws<ArgumentException>(() => DesktopImportTransactionOutcome.Discarded(completedWithFailures));
			Assert.Throws<ArgumentNullException>(() => DesktopImportTransactionOutcome.Discarded(null));
		}

		[Fact]
		public async Task PreparedImportAppliesCurrentIngressOnlyToPreparedRuntime()
		{
			var harness = Harness.Create(Batch(1, 1, A, Entry(A, 0, "a", "wall"), Entry(B, 1, "b", "wall")));
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 2,
				[SettingsService.DesktopNamesKey + "[0]"] = "import-a",
				[SettingsService.DesktopNamesKey + "[1]"] = "import-b",
			};
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			var gate = new TaskCompletionSource<VirtualDesktopReconciliationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			harness.Provider.NextRequest = gate.Task;
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			harness.Provider.PublishCurrent(new VirtualDesktopCurrentTransition(1, 100, 1, B));
			Assert.Equal(A, harness.Runtime.State.CurrentDesktopId);
			gate.SetResult(VirtualDesktopReconciliationResult.Succeeded(
				Batch(1, 1, A, Entry(A, 0, "import-a", "wall"), Entry(B, 1, "import-b", "wall"))));
			var result = await commit;

			Assert.True(result.Succeeded);
			Assert.Equal(B, harness.Runtime.State.CurrentDesktopId);
			Assert.Equal(2, harness.Provider.ReconciliationRequestCount);
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
			Assert.Equal(1, harness.Settings.ImportPublishRequests);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
		}

		[Fact]
		public async Task ImportCommitFreezeReplaysStableAndCurrentIngressInArrivalOrder()
		{
			var harness = Harness.Create(Batch(1, 1, A, Entry(A, 0, "a", "wall"), Entry(B, 1, "b", "wall")));
			await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Settings.BlockCommit = true;
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, false, TestContext.Current.CancellationToken);
			await harness.Settings.CommitStarted.Task;

			harness.Provider.PublishStable(Batch(1, 2, A, Entry(A, 0, "after", "wall"), Entry(B, 1, "b", "wall")));
			harness.Provider.PublishCurrent(new VirtualDesktopCurrentTransition(1, 100, 2, B));
			Assert.Equal("a", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(A, harness.Runtime.State.CurrentDesktopId);
			harness.Settings.CommitRelease.TrySetResult(true);
			var result = await commit;

			Assert.True(result.Succeeded);
			Assert.Equal("after", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(B, harness.Runtime.State.CurrentDesktopId);
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
			Assert.Equal(1, harness.Settings.ImportPublishRequests);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
		}

		[Fact]
		public async Task ShutdownWaitsForActiveImportAndDiscardsSession()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
			};
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Settings.BlockCommit = true;
			var order = new List<string>();
			harness.Operations.BeforeName = () => order.Add("deferred-edit");
			harness.Provider.RequestObserved = count =>
			{
				if (count == 2)
				{
					order.Add("final-reconciliation");
					Assert.Equal("deferred", harness.Runtime.State.Records[A].Name.Value);
				}
			};
			harness.Provider.Disposing = () => order.Add("dispose");
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "deferred", "wall")));
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, false, TestContext.Current.CancellationToken);
			await harness.Settings.CommitStarted.Task;
			harness.Runtime.EditName(A, "deferred");
			var shutdown = harness.Runtime.ShutdownAsync();

			Assert.False(commit.IsCompleted);
			Assert.False(shutdown.IsCompleted);
			Assert.Equal(0, harness.Operations.NameCalls);
			Assert.Equal("name", harness.Runtime.State.Records[A].Name.Value);

			harness.Settings.CommitRelease.TrySetResult(true);
			var importResult = await commit;
			var shutdownResult = await shutdown;

			Assert.True(importResult.Succeeded);
			Assert.Equal(DesktopRuntimeShutdownStatus.Completed, shutdownResult.Status);
			Assert.Equal(new[] { "deferred-edit", "final-reconciliation", "dispose" }, order);
			Assert.Equal(1, harness.Operations.NameCalls);
			Assert.Equal("deferred", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal("deferred", harness.Settings.LastProjection.Names[0]);
			Assert.False(harness.Settings.Provider.ImportTransactionActive);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
			Assert.Equal(1, harness.Settings.ImportPublishRequests);
			Assert.True(harness.Provider.Disposed);
		}
		[Fact]
		public async Task ImportPublishFailureRequestsReconciliationBeforeFrozenIngressReplay()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			harness.Settings.Provider.SaveFailure = new IOException("synthetic");
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Settings.BlockCommit = true;
			string nameWhenReconciliationRequested = null;
			harness.Provider.RequestObserved = count =>
			{
				if (count == 2) nameWhenReconciliationRequested = harness.Runtime.State.Records[A].Name.Value;
			};
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, false, TestContext.Current.CancellationToken);
			await harness.Settings.CommitStarted.Task;
			harness.Provider.PublishStable(Batch(1, 2, A, Entry(A, 0, "after", "wall")));
			harness.Settings.CommitRelease.TrySetResult(true);

			var result = await commit;

			Assert.Equal(SettingsImportCommitStatus.PublishFailed, result.Status);
			Assert.Equal("name", nameWhenReconciliationRequested);
			Assert.Equal("after", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(2, harness.Provider.ReconciliationRequestCount);
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
			Assert.Equal(0, harness.Settings.ImportPublishRequests);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
		}
		[Fact]
		public async Task PreparedImportSessionCannotBeExecutedTwice()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			var session = new DesktopPreparedImportSession(harness.Settings.ClaimImport(stage), false, false, harness.Runtime, harness.Settings);

			var outcome = await session.ExecuteAsync(TestContext.Current.CancellationToken);

			Assert.Equal(DesktopImportTransactionOutcomeKind.CommitPreparedRuntime, outcome.Kind);
			await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(TestContext.Current.CancellationToken));
			Assert.Empty(session.Complete());
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
		}

		[Fact]
		public async Task ForeignPreparedImportStageIsRejectedWithoutRuntimeExchange()
		{
			var harness = await Harness.Initialized();
			var foreignSettings = new FakeSettings(DesktopStartupSeed.Empty);
			foreignSettings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 2,
				[SettingsService.DesktopNamesKey + "[0]"] = "foreign-a",
				[SettingsService.DesktopNamesKey + "[1]"] = "foreign-b",
				[SettingsService.DesktopWallpaperPathsKey + "#Count"] = 2,
				[SettingsService.DesktopWallpaperPathsKey + "[0]"] = "foreign-wall-a",
				[SettingsService.DesktopWallpaperPathsKey + "[1]"] = "foreign-wall-b",
			};
			var foreignStage = await foreignSettings.PrepareImportAsync("synthetic");
			var before = harness.Runtime.State;
			var projectionCount = harness.Settings.ProjectionCount;
			var saveRequests = harness.Settings.SaveRequests;
			var stateChanged = 0;
			harness.Runtime.StateChanged += (_, __) => stateChanged++;

			var result = await harness.Runtime.CommitPreparedImportAsync(foreignStage, true, TestContext.Current.CancellationToken);

			Assert.Equal(SettingsImportCommitStatus.InvalidStage, result.Status);
			Assert.Same(before, harness.Runtime.State);
			Assert.Equal(0, harness.Operations.CreateCalls);
			Assert.Empty(harness.Operations.RemovedIds);
			Assert.Equal(0, harness.Operations.NameCalls);
			Assert.Equal(0, harness.Operations.WallpaperCalls);
			Assert.Equal(0, harness.Settings.ImportCommitRequests);
			Assert.Equal(0, harness.Settings.ImportPublishRequests);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
			Assert.Equal(1, harness.Provider.ReconciliationRequestCount);
			Assert.Equal(projectionCount, harness.Settings.ProjectionCount);
			Assert.Equal(saveRequests, harness.Settings.SaveRequests);
			Assert.Equal(0, stateChanged);
			Assert.True(foreignSettings.Provider.ImportTransactionActive);
			Assert.Equal(SettingsImportCommitStatus.Discarded, foreignSettings.Provider.DiscardStagedImport(foreignStage).Status);
			Assert.Equal(SettingsImportCommitStatus.InvalidStage, foreignSettings.Provider.DiscardStagedImport(foreignStage).Status);
		}

		[Fact]
		public async Task ForeignStageClaimAndOwnerDiscardRaceDoesNotStartImportOperations()
		{
			var harness = await Harness.Initialized();
			var foreignSettings = new FakeSettings(DesktopStartupSeed.Empty);
			foreignSettings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 2,
				[SettingsService.DesktopNamesKey + "[0]"] = "foreign-a",
				[SettingsService.DesktopNamesKey + "[1]"] = "foreign-b",
			};
			var foreignStage = await foreignSettings.PrepareImportAsync("synthetic");
			var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var localAttempt = Task.Run(async () =>
			{
				await release.Task;
				return await harness.Runtime.CommitPreparedImportAsync(foreignStage, true, TestContext.Current.CancellationToken);
			});
			var ownerDiscard = Task.Run(async () =>
			{
				await release.Task;
				return foreignSettings.Provider.DiscardStagedImport(foreignStage);
			});
			release.TrySetResult(true);

			var localResult = await localAttempt;
			var discardResult = await ownerDiscard;

			Assert.Equal(SettingsImportCommitStatus.InvalidStage, localResult.Status);
			Assert.Equal(SettingsImportCommitStatus.Discarded, discardResult.Status);
			Assert.Equal(0, harness.Operations.CreateCalls);
			Assert.Empty(harness.Operations.RemovedIds);
			Assert.Equal(0, harness.Operations.NameCalls);
			Assert.Equal(0, harness.Operations.WallpaperCalls);
			Assert.Equal(0, harness.Settings.ImportCommitRequests);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
			Assert.Equal(0, harness.Settings.ImportPublishRequests);
			Assert.Equal(1, harness.Provider.ReconciliationRequestCount);
		}

		[Fact]
		public async Task ImportStageClaimIsSingleUseAcrossCommitAndDiscard()
		{
			var settings = new FakeSettings(DesktopStartupSeed.Empty);
			settings.Provider.NextImport = new Dictionary<string, object>();
			var discardedStage = await settings.PrepareImportAsync("discard");
			var discardedClaim = settings.ClaimImport(discardedStage);

			Assert.NotNull(discardedClaim);
			Assert.Null(settings.ClaimImport(discardedStage));
			Assert.Equal(SettingsImportCommitStatus.Publishing, settings.Provider.DiscardStagedImport(discardedStage).Status);
			Assert.Equal(SettingsImportCommitStatus.Discarded, settings.DiscardImport(discardedClaim).Status);
			Assert.Equal(SettingsImportCommitStatus.InvalidStage, settings.DiscardImport(discardedClaim).Status);
			Assert.Null(settings.ClaimImport(discardedStage));

			settings.Provider.NextImport = new Dictionary<string, object>();
			var committedStage = await settings.PrepareImportAsync("commit");
			var committedClaim = settings.ClaimImport(committedStage);
			var committed = await settings.CommitImportAsync(committedClaim, committedStage.CreateCommitDictionary());

			Assert.True(committed.Succeeded);
			Assert.Null(settings.ClaimImport(committedStage));
			Assert.Equal(SettingsImportCommitStatus.InvalidStage, (await settings.CommitImportAsync(committedClaim, committedStage.CreateCommitDictionary())).Status);
			Assert.Equal(SettingsImportCommitStatus.InvalidStage, (await settings.Provider.CommitStagedImportAsync(committedStage, committedStage.CreateCommitDictionary())).Status);
		}

		[Fact]
		public async Task PreCommitExceptionReturnsTypedFailureAndReleasesClaim()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>();
			harness.Settings.CommitException = new IOException("synthetic");
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			var before = harness.Runtime.State;
			var faults = new List<DesktopRuntimeFault>();
			harness.Runtime.Faulted += (_, fault) => faults.Add(fault);

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, false, TestContext.Current.CancellationToken);

			Assert.Equal(SettingsImportCommitStatus.PublishFailed, result.Status);
			Assert.Same(before, harness.Runtime.State);
			Assert.False(harness.Settings.Provider.ImportTransactionActive);
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
			Assert.Equal(1, harness.Settings.ImportDiscardRequests);
			Assert.Equal(0, harness.Settings.ImportPublishRequests);
			Assert.Equal(2, harness.Provider.ReconciliationRequestCount);
			Assert.Contains(faults, fault => fault.Category == "SettingsTransaction" && fault.ExceptionType == typeof(IOException).FullName);
		}

		[Fact]
		public async Task PostCommitPublicationExceptionReportsConsistencyWithoutRollbackAndReplaysIngress()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
			};
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Settings.BlockCommit = true;
			harness.Settings.PublishException = new IOException("synthetic");
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "imported", "wall")));
			var faults = new List<DesktopRuntimeFault>();
			harness.Runtime.Faulted += (_, fault) => faults.Add(fault);
			string nameWhenRecoveryRequested = null;
			harness.Provider.RequestObserved = count =>
			{
				if (count == 3) nameWhenRecoveryRequested = harness.Runtime.State.Records[A].Name.Value;
			};
			var commit = harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);
			await harness.Settings.CommitStarted.Task;
			harness.Provider.PublishStable(Batch(1, 3, A, Entry(A, 0, "after", "wall")));
			harness.Settings.CommitRelease.TrySetResult(true);

			var result = await commit;

			Assert.Equal(SettingsImportCommitStatus.CompletedWithFailures, result.Status);
			Assert.NotNull(result.SaveResult);
			Assert.Equal("imported", nameWhenRecoveryRequested);
			Assert.Equal("after", harness.Runtime.State.Records[A].Name.Value);
			Assert.True(harness.Settings.Provider.TryGetValue(SettingsService.DesktopNamesKey + "[0]", out string persisted));
			Assert.Equal("after", persisted);
			Assert.Contains(harness.Settings.Provider.SavedDictionaries, dictionary => dictionary.TryGetValue(SettingsService.DesktopNamesKey + "[0]", out var value) && Equals(value, "imported"));
			Assert.False(harness.Settings.Provider.ImportTransactionActive);
			Assert.Equal(1, harness.Settings.ImportCommitRequests);
			Assert.Equal(1, harness.Settings.ImportPublishRequests);
			Assert.Equal(0, harness.Settings.ImportDiscardRequests);
			Assert.Equal(3, harness.Provider.ReconciliationRequestCount);
			Assert.Contains(faults, fault => fault.Category == "SettingsTransaction.PostCommitConsistency" && fault.ExceptionType == typeof(IOException).FullName);
		}

		[Fact]
		public async Task ImportSubscriberExceptionsDoNotRollbackCommittedState()
		{
			var harness = await Harness.Initialized();
			harness.Settings.Provider.NextImport = new Dictionary<string, object>
			{
				[SettingsService.DesktopNamesKey + "#Count"] = 1,
				[SettingsService.DesktopNamesKey + "[0]"] = "imported",
			};
			var stage = await harness.Settings.PrepareImportAsync("synthetic");
			harness.Provider.EnqueueResult(Batch(1, 2, A, Entry(A, 0, "imported", "wall")));
			var reloaded = 0;
			harness.Settings.Provider.Reloaded += (_, __) => throw new InvalidOperationException("synthetic");
			harness.Settings.Provider.Reloaded += (_, __) => reloaded++;
			var stateChanged = 0;
			harness.Runtime.StateChanged += (_, __) => throw new InvalidOperationException("synthetic");
			harness.Runtime.StateChanged += (_, __) => stateChanged++;
			var faults = new List<DesktopRuntimeFault>();
			harness.Runtime.Faulted += (_, fault) => faults.Add(fault);

			var result = await harness.Runtime.CommitPreparedImportAsync(stage, true, TestContext.Current.CancellationToken);

			Assert.True(result.Succeeded);
			Assert.Equal("imported", harness.Runtime.State.Records[A].Name.Value);
			Assert.Equal(1, reloaded);
			Assert.Equal(1, stateChanged);
			Assert.Contains(faults, fault => fault.Category == "StateChangedSubscriber");
			Assert.False(harness.Settings.Provider.ImportTransactionActive);
		}
	}
}
