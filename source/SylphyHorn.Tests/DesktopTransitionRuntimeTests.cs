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

namespace SylphyHorn.Tests
{
	public class DesktopTransitionRuntimeTests
	{
		private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		private static readonly Guid C = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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
			var assemblyPath = typeof(DesktopTransitionRuntimeTests).Assembly.Location;
			var expects64Bit = assemblyPath.IndexOf(Path.DirectorySeparatorChar + "x64" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
			Assert.Equal(expects64Bit, Environment.Is64BitProcess);
		}

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
		public async Task SaveRequestsCoalesceWithoutWritingOlderSnapshotAfterNewer()
		{
			var provider = new ControlledSaveProvider();
			await provider.LoadAsync();
			provider.SetValue("Value", "first");
			var first = provider.SaveWithResultAsync(1);
			var firstWrite = await provider.NextWriteAsync();
			provider.SetValue("Value", "second");
			var second = provider.SaveWithResultAsync(2);
			firstWrite.Complete();
			var secondWrite = await provider.NextWriteAsync();
			secondWrite.Complete();
			var results = await Task.WhenAll(first, second);
			Assert.All(results, result => Assert.True(result.Succeeded));
			Assert.Equal(new[] { "first", "second" }, provider.WrittenValues);
			Assert.True(results[1].SaveRevision > results[0].SaveRevision);
		}
		[Fact]
		public async Task AtomicSettingsFileReplacesExistingFile()
		{
			var root = Path.Combine(Path.GetTempPath(), "SylphyHorn.Task3D." + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var file = new FileInfo(Path.Combine(root, "Settings.xml"));
				File.WriteAllText(file.FullName, "old");
				await AtomicSettingsFile.WriteAsync(new Dictionary<string, object> { ["Value"] = "new" }, file, new[] { typeof(bool), typeof(int[]) });
				var loaded = await AtomicSettingsFile.ReadAsync(file, new[] { typeof(bool), typeof(int[]) });
				Assert.Equal("new", loaded["Value"]);
				Assert.Empty(Directory.GetFiles(root, "*.tmp"));
			}
			finally { Directory.Delete(root, true); }
		}

		[Fact]
		public async Task AtomicSettingsFileSerializationFailurePreservesExistingFile()
		{
			var root = Path.Combine(Path.GetTempPath(), "SylphyHorn.Task3D." + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var file = new FileInfo(Path.Combine(root, "Settings.xml"));
				File.WriteAllText(file.FullName, "known-good");
				await Assert.ThrowsAnyAsync<Exception>(() => AtomicSettingsFile.WriteAsync(new Dictionary<string, object> { ["Bad"] = new NonSerializableValue() }, file, Array.Empty<Type>()));
				Assert.Equal("known-good", File.ReadAllText(file.FullName));
				Assert.Empty(Directory.GetFiles(root, "*.tmp"));
			}
			finally { Directory.Delete(root, true); }
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

		[Fact]
		public async Task AtomicSettingsFileSupportsTwoWritesWhenTargetInitiallyMissing()
		{
			var root = Path.Combine(Path.GetTempPath(), "SylphyHorn.Task3D." + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var file = new FileInfo(Path.Combine(root, "Settings.xml"));
				await AtomicSettingsFile.WriteAsync(new Dictionary<string, object> { ["Value"] = "first" }, file, new[] { typeof(bool), typeof(int[]) });
				await AtomicSettingsFile.WriteAsync(new Dictionary<string, object> { ["Value"] = "second" }, file, new[] { typeof(bool), typeof(int[]) });
				var loaded = await AtomicSettingsFile.ReadAsync(file, new[] { typeof(bool), typeof(int[]) });
				var hash = await AtomicSettingsFile.HashAsync(file);
				Assert.Equal("second", loaded["Value"]);
				Assert.False(string.IsNullOrEmpty(hash));
				Assert.Empty(Directory.GetFiles(root, "*.tmp"));
			}
			finally { Directory.Delete(root, true); }
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
		private static int ExpectedArchitecture() => Environment.Is64BitProcess ? 64 : 32;
		private static VirtualDesktopStableBatch Batch(long epoch, long revision, Guid current, params VirtualDesktopStableEntry[] entries)
			=> new VirtualDesktopStableBatch(epoch, revision, current, VirtualDesktopReadStatus.Success, entries, VirtualDesktopStableReason.ExplicitReconciliation);
		private static VirtualDesktopStableEntry Entry(Guid id, int index, string name, string wallpaper)
			=> new VirtualDesktopStableEntry(id, index, name, VirtualDesktopReadStatus.Success, wallpaper, VirtualDesktopReadStatus.Success);
		private static VirtualDesktopStableEntry WallpaperUnsupported(Guid id, int index, string name)
			=> new VirtualDesktopStableEntry(id, index, name, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported);

		private sealed class NonSerializableValue { public Action Callback => () => { }; }

		private sealed class Harness
		{
			private Harness(FakeProvider provider, FakeSettings settings, FakeOwner owner, FakeOperations operations)
			{
				this.Provider = provider;
				this.Settings = settings;
				this.Owner = owner;
				this.Operations = operations;
				this.Runtime = new DesktopTransitionRuntime(provider, settings, owner, operations);
			}
			internal FakeProvider Provider { get; }
			internal FakeSettings Settings { get; }
			internal FakeOwner Owner { get; }
			internal FakeOperations Operations { get; }
			internal DesktopTransitionRuntime Runtime { get; }
			internal static Harness Create(VirtualDesktopStableBatch batch) => new Harness(new FakeProvider(batch), new FakeSettings(DesktopStartupSeed.Empty), new FakeOwner(), new FakeOperations());
			internal static async Task<Harness> Initialized()
			{
				var harness = Create(Batch(1, 1, A, Entry(A, 0, "name", "wall")));
				await harness.Runtime.InitializeAsync(false, TestContext.Current.CancellationToken);
				return harness;
			}
		}

		private sealed class FakeProvider : IDesktopProviderClient
		{
			private readonly Queue<VirtualDesktopReconciliationResult> _results = new Queue<VirtualDesktopReconciliationResult>();
			internal FakeProvider(VirtualDesktopStableBatch initial) => this._results.Enqueue(VirtualDesktopReconciliationResult.Succeeded(initial));
			internal FakeProvider(VirtualDesktopReconciliationResult initial) => this._results.Enqueue(initial);
			public event EventHandler<VirtualDesktopStableBatch> StableBatchPublished;
			public event EventHandler<VirtualDesktopCurrentTransition> CurrentTransitioned;
			public event EventHandler<VirtualDesktopProviderFault> Faulted;
			internal Task<VirtualDesktopReconciliationResult> NextRequest { get; set; }
			internal VirtualDesktopStableBatch PublishSynchronouslyBeforeRequestCompletion { get; set; }
			internal bool Disposed { get; private set; }
			internal void EnqueueResult(VirtualDesktopStableBatch batch) => this._results.Enqueue(VirtualDesktopReconciliationResult.Succeeded(batch));
			internal void EnqueueResult(VirtualDesktopReconciliationResult result) => this._results.Enqueue(result);
			internal void PublishStable(VirtualDesktopStableBatch batch) => this.StableBatchPublished?.Invoke(this, batch);
			internal void PublishCurrent(VirtualDesktopCurrentTransition transition) => this.CurrentTransitioned?.Invoke(this, transition);
			internal void PublishFault(VirtualDesktopProviderFault fault) => this.Faulted?.Invoke(this, fault);
			public Task<VirtualDesktopReconciliationResult> RequestReconciliationAsync(VirtualDesktopStableReason reason, CancellationToken cancellationToken)
			{
				if (this.Disposed) return Task.FromResult(VirtualDesktopReconciliationResult.ShuttingDown());
				if (this.PublishSynchronouslyBeforeRequestCompletion != null)
				{
					var published = this.PublishSynchronouslyBeforeRequestCompletion;
					this.PublishSynchronouslyBeforeRequestCompletion = null;
					this.PublishStable(published);
					return Task.FromResult(VirtualDesktopReconciliationResult.Succeeded(published));
				}
				if (this.NextRequest != null) { var result = this.NextRequest; this.NextRequest = null; return result; }
				return Task.FromResult(this._results.Count == 0 ? VirtualDesktopReconciliationResult.Unavailable(VirtualDesktopProviderFailureCategory.ReconciliationUnavailable) : this._results.Dequeue());
			}
			public void Dispose() => this.Disposed = true;
		}

		private sealed class FakeSettings : IDesktopSettingsTransactions
		{
			internal FakeSettings(DesktopStartupSeed seed)
			{
				this.Seed = seed;
				this.Provider = new TestDictionaryProvider();
				this.Provider.InitializeAsync().GetAwaiter().GetResult();
			}
			internal DesktopStartupSeed Seed { get; }
			internal TestDictionaryProvider Provider { get; }
			internal DesktopSettingsProjection LastProjection { get; private set; }
			internal int ProjectionCount { get; private set; }
			internal int SaveRequests { get; private set; }
			internal long Revision { get; private set; }
			internal Action ProjectionApplied { get; set; }
			internal bool BlockCommit { get; set; }
			internal TaskCompletionSource<bool> CommitStarted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal TaskCompletionSource<bool> CommitRelease { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			public DesktopStartupSeed CaptureStartupSeed() => this.Seed;
			public void ApplyProjection(DesktopSettingsProjection projection)
			{
				this.LastProjection = projection;
				this.ProjectionCount++;
				this.Revision++;
				var dictionary = new Dictionary<string, object>();
				SettingsService.ApplyDesktopProjection(dictionary, projection);
				foreach (var pair in dictionary) this.Provider.SetValue(pair.Key, pair.Value);
				this.ProjectionApplied?.Invoke();
			}
			public long SettingsRevision => Math.Max(this.Revision, this.Provider.SettingsRevision);
			public Task<SettingsSaveResult> RequestSaveAsync(long stateRevision) { this.SaveRequests++; return this.Provider.SaveWithResultAsync(stateRevision); }
			public Task<StagedSettingsImport> PrepareImportAsync(string path) => this.Provider.PrepareImportAsync(path);
			public Task<StagedSettingsImport> PrepareResetAsync() => this.Provider.PrepareResetAsync();
			public async Task<SettingsImportCommitResult> CommitImportAsync(StagedSettingsImport stage, IDictionary<string, object> dictionary)
			{
				if (this.BlockCommit)
				{
					this.CommitStarted.TrySetResult(true);
					await this.CommitRelease.Task;
				}
				return await this.Provider.CommitStagedImportAsync(stage, dictionary);
			}
			public SettingsImportCommitResult DiscardImport(StagedSettingsImport stage) => this.Provider.DiscardStagedImport(stage);
			public void PublishImportCommitted() => this.Provider.PublishCommittedImport();
		}

		private sealed class TestDictionaryProvider : DictionaryProvider
		{
			internal IDictionary<string, object> NextImport { get; set; } = new Dictionary<string, object>();
			internal string ContentHash { get; set; }
			internal Exception SaveFailure { get; set; }
			internal List<IDictionary<string, object>> SavedDictionaries { get; } = new List<IDictionary<string, object>>();
			internal Task InitializeAsync() => this.LoadAsync();
			protected override Task SaveAsyncCore(IDictionary<string, object> dic)
			{
				if (this.SaveFailure != null) return Task.FromException(this.SaveFailure);
				this.SavedDictionaries.Add(new Dictionary<string, object>(dic));
				return Task.CompletedTask;
			}
			protected override Task SaveAsyncCore(IDictionary<string, object> dic, string path) => this.SaveAsyncCore(dic);
			protected override Task<IDictionary<string, object>> LoadAsyncCore() => Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
			protected override Task<IDictionary<string, object>> LoadAsyncCore(string path) => Task.FromResult(this.NextImport);
			protected override Task<string> GetContentHashAsyncCore() => Task.FromResult(this.ContentHash);
		}

		private sealed class ControlledSaveProvider : DictionaryProvider
		{
			private readonly Queue<ControlledWrite> _started = new Queue<ControlledWrite>();
			private readonly Queue<TaskCompletionSource<ControlledWrite>> _waiters = new Queue<TaskCompletionSource<ControlledWrite>>();
			internal List<string> WrittenValues { get; } = new List<string>();
			internal Task<ControlledWrite> NextWriteAsync()
			{
				lock (this._started)
				{
					if (this._started.Count != 0) return Task.FromResult(this._started.Dequeue());
					var waiter = new TaskCompletionSource<ControlledWrite>(TaskCreationOptions.RunContinuationsAsynchronously);
					this._waiters.Enqueue(waiter);
					return waiter.Task;
				}
			}
			protected override Task SaveAsyncCore(IDictionary<string, object> dic)
			{
				var write = new ControlledWrite();
				this.WrittenValues.Add((string)dic["Value"]);
				lock (this._started)
				{
					if (this._waiters.Count != 0) this._waiters.Dequeue().TrySetResult(write);
					else this._started.Enqueue(write);
				}
				return write.Completion.Task;
			}
			protected override Task SaveAsyncCore(IDictionary<string, object> dic, string path) => this.SaveAsyncCore(dic);
			protected override Task<IDictionary<string, object>> LoadAsyncCore() => Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
			protected override Task<IDictionary<string, object>> LoadAsyncCore(string path) => this.LoadAsyncCore();
		}

		private sealed class ControlledWrite
		{
			internal TaskCompletionSource<bool> Completion { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal void Complete() => this.Completion.TrySetResult(true);
		}
		private sealed class FakeOwner : IDesktopOwnerContext
		{
			internal Queue<Action> Posted { get; } = new Queue<Action>();
			internal bool RejectPost { get; set; }
			public bool CheckAccess() => true;
			public bool Post(Action action) { if (this.RejectPost) return false; this.Posted.Enqueue(action); return true; }
			internal void DrainOne() { if (this.Posted.Count != 0) this.Posted.Dequeue()(); }
			internal void Drain() { while (this.Posted.Count != 0) this.DrainOne(); }
		}

		private sealed class FakeOperations : IDesktopOperations
		{
			internal int NameCalls { get; private set; }
			internal int CreateCalls { get; private set; }
			internal List<Guid> RemovedIds { get; } = new List<Guid>();
			internal Exception NameFailure { get; set; }
			internal string FailNameValue { get; set; }
			internal string FailWallpaperValue { get; set; }
			internal int WallpaperCalls { get; private set; }
			internal List<Guid> AppliedWallpaperIds { get; } = new List<Guid>();
			internal List<string> AppliedWallpaperValues { get; } = new List<string>();
			internal Action BeforeName { get; set; }
			internal List<string> NameValues { get; } = new List<string>();
			public void Create() => this.CreateCalls++;
			public void SetName(Guid desktopId, string value) { this.NameCalls++; this.NameValues.Add(value); this.BeforeName?.Invoke(); if (this.NameFailure != null || this.FailNameValue == value) throw this.NameFailure ?? new InvalidOperationException("synthetic"); }
			public void SetWallpaperPath(Guid desktopId, string value) { this.WallpaperCalls++; if (this.FailWallpaperValue == value) throw new InvalidOperationException("synthetic"); }
			public void ApplyWallpaper(Guid desktopId, string value, WallpaperPosition position) { this.AppliedWallpaperIds.Add(desktopId); this.AppliedWallpaperValues.Add(value); }
			public void MoveLeft(Guid desktopId) { }
			public void MoveRight(Guid desktopId) { }
			public void MoveFirst(Guid desktopId) { }
			public void MoveLast(Guid desktopId) { }
			public void Switch(Guid desktopId) { }
			public void Remove(Guid desktopId) => this.RemovedIds.Add(desktopId);
		}
	}
}
