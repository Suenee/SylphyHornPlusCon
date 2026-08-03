using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using WindowsDesktop;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class DesktopTransitionCoordinatorTests
	{
		private static readonly Guid A = new Guid("11111111-1111-1111-1111-111111111111");
		private static readonly Guid B = new Guid("22222222-2222-2222-2222-222222222222");
		private static readonly Guid C = new Guid("33333333-3333-3333-3333-333333333333");
		private static readonly Guid D = new Guid("44444444-4444-4444-4444-444444444444");
		private readonly ITestOutputHelper _output;

		public DesktopTransitionCoordinatorTests(ITestOutputHelper output) => this._output = output;

		[Fact]
		public void TestsReportProcessArchitecture()
		{
			this._output.WriteLine($"ProcessArchitecture={RuntimeInformation.ProcessArchitecture};PointerSize={IntPtr.Size}");
			Assert.True((RuntimeInformation.ProcessArchitecture == Architecture.X86 && IntPtr.Size == 4) || (RuntimeInformation.ProcessArchitecture == Architecture.X64 && IntPtr.Size == 8));
		}

		[Fact]
		public void EqualLengthSeedIsAssignedOnceAndPreservedByFailedReads()
		{
			var coordinator = Coordinator(new[] { "A", "B" }, new[] { "wa", "wb" }, new[] { WallpaperPosition.Center, WallpaperPosition.Tile });

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Failed(A, 0), Failed(B, 1)));

			Assert.True(result.Accepted);
			Assert.Equal(new[] { "A", "B" }, result.Projection.Names);
			Assert.Equal(new[] { "wa", "wb" }, result.Projection.WallpaperPaths);
			Assert.Equal(DesktopRecordOrigin.SeededExistingRecord, result.NewState.Records[A].Origin);
			Assert.False(result.RequiresSave);
			Assert.True(result.RequiresReconciliation);
		}

		[Fact]
		public void UnevenSeedListsProduceCompleteProjectionWithoutIndexInference()
		{
			var coordinator = Coordinator(new[] { "A" }, new[] { "wa", "wb", "wc" }, new[] { WallpaperPosition.Center, WallpaperPosition.Tile });

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Failed(A, 0), Failed(B, 1), Failed(C, 2)));

			Assert.Equal(3, result.Projection.Names.Count);
			Assert.Equal(new string[] { "A", null, null }, result.Projection.Names);
			Assert.Equal(new[] { "wa", "wb", "wc" }, result.Projection.WallpaperPaths);
			Assert.Equal(new[] { WallpaperPosition.Center, WallpaperPosition.Tile, WallpaperPosition.Fill }, result.Projection.Positions);
			Assert.True(result.RequiresSave);
		}

		[Fact]
		public void EmptySeedCreatesTrulyNewRecordsAndAcceptsConfirmedEmpty()
		{
			var coordinator = Coordinator();

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Success(A, 0, string.Empty, string.Empty)));

			var record = result.NewState.Records[A];
			Assert.Equal(DesktopRecordOrigin.TrulyNewRecord, record.Origin);
			Assert.True(record.Name.HasValue);
			Assert.Equal(string.Empty, record.Name.Value);
			Assert.True(record.Name.IsConfirmed);
			Assert.Equal(WallpaperPosition.Fill, record.WallpaperPosition);
		}

		[Fact]
		public void ProviderNonEmptyOverridesPersistedSeedIndependentlyPerProperty()
		{
			var coordinator = Coordinator(new[] { "seed" }, new[] { "seed-wall" }, null);

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Success(A, 0, "provider", "provider-wall")));

			Assert.Equal("provider", result.NewState.Records[A].Name.Value);
			Assert.Equal("provider-wall", result.NewState.Records[A].WallpaperPath.Value);
			Assert.Equal(DesktopPropertyAuthority.ProviderStableValue, result.NewState.Records[A].Name.Authority);
			Assert.True(result.RequiresSave);
		}

		[Fact]
		public void FirstProviderEmptyPreservesNonEmptySeedAndRequestsConfirmation()
		{
			var coordinator = Coordinator(new[] { "seed" }, null, null);

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			Assert.Equal("seed", result.NewState.Records[A].Name.Value);
			Assert.True(result.RequiresReconciliation);
			Assert.Equal(DesktopReconciliationReason.SeedEmptyConfirmation, result.ReconciliationReason);
		}

		[Fact]
		public void SecondAcceptedEmptyAdoptsConfirmedEmpty()
		{
			var coordinator = Coordinator(new[] { "seed" }, null, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			Assert.Equal(string.Empty, result.NewState.Records[A].Name.Value);
			Assert.True(result.RequiresSave);
		}

		[Theory]
		[InlineData(VirtualDesktopReadStatus.Failed)]
		[InlineData(VirtualDesktopReadStatus.NotAttempted)]
		public void IncompleteReadPreservesSeedEmptyCandidateWithoutCounting(VirtualDesktopReadStatus status)
		{
			var coordinator = Coordinator(new[] { "seed" }, null, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			var incomplete = coordinator.ApplyStableBatch(Batch(1, 2, A, Entry(A, 0, null, status, null, VirtualDesktopReadStatus.NotAttempted)));
			var confirmed = coordinator.ApplyStableBatch(Batch(1, 3, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			Assert.Equal("seed", incomplete.NewState.Records[A].Name.Value);
			Assert.Equal(string.Empty, confirmed.NewState.Records[A].Name.Value);
		}

		[Fact]
		public void NonEmptyReadCancelsSeedEmptyCandidate()
		{
			var coordinator = Coordinator(new[] { "seed" }, null, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Entry(A, 0, "new", VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported)));

			Assert.Equal("new", result.NewState.Records[A].Name.Value);
			Assert.False(result.RequiresReconciliation);
		}

		[Fact]
		public void UnrelatedDesktopChangeDoesNotConsumeSeedEmptyConfirmation()
		{
			var coordinator = Coordinator(new[] { "seed", null }, null, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A,
				Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported),
				Success(B, 1)));

			coordinator.ApplyStableBatch(Batch(1, 2, A,
				Entry(A, 0, null, VirtualDesktopReadStatus.Failed, null, VirtualDesktopReadStatus.Unsupported),
				Success(B, 1, "changed", "wall")));
			var confirmed = coordinator.ApplyStableBatch(Batch(1, 3, A,
				Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported),
				Success(B, 1, "changed", "wall")));

			Assert.Equal(string.Empty, confirmed.NewState.Records[A].Name.Value);
		}

		[Fact]
		public void UnsupportedPropertiesPreserveOnlyTheirApplicableAuthorityWithoutRetry()
		{
			var coordinator = Coordinator(new[] { "name" }, new[] { "wall" }, null);

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Unsupported(A, 0)));

			Assert.Equal("name", result.NewState.Records[A].Name.Value);
			Assert.Equal(DesktopPropertyAuthority.PersistedLastKnownGood, result.NewState.Records[A].Name.Authority);
			Assert.Equal("wall", result.NewState.Records[A].WallpaperPath.Value);
			Assert.Equal(DesktopPropertyAuthority.ApplicationAuthoritative, result.NewState.Records[A].WallpaperPath.Authority);
			Assert.False(result.RequiresReconciliation);
		}

		[Fact]
		public void TrulyNewUnknownRemainsNullWhileOtherPropertySucceeds()
		{
			var coordinator = Coordinator();

			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, null, VirtualDesktopReadStatus.Failed, "wall", VirtualDesktopReadStatus.Success)));

			Assert.False(result.NewState.Records[A].Name.HasValue);
			Assert.Null(result.Projection.Names[0]);
			Assert.Equal("wall", result.NewState.Records[A].WallpaperPath.Value);
			Assert.True(result.RequiresReconciliation);
		}

		[Theory]
		[InlineData(VirtualDesktopReadStatus.Failed)]
		[InlineData(VirtualDesktopReadStatus.NotAttempted)]
		public void IncompletePropertyReadPreservesProviderLastKnownGood(VirtualDesktopReadStatus status)
		{
			var coordinator = Initialized(A);

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Entry(A, 0, null, status, "new-wall", VirtualDesktopReadStatus.Success)));

			Assert.Equal(A.ToString(), result.NewState.Records[A].Name.Value);
			Assert.Equal(status, result.NewState.Records[A].Name.ReadStatus);
			Assert.Equal("new-wall", result.NewState.Records[A].WallpaperPath.Value);
			Assert.True(result.RequiresReconciliation);
		}

		[Fact]
		public void RebaseKeepsKnownIdentityAndAddsAndRemovesByGuid()
		{
			var coordinator = Initialized(A, B);
			var oldA = coordinator.State.Records[A];

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 0, "A2", "wa"), Success(C, 1, "C", "wc")));

			Assert.Equal(new[] { A, C }, result.NewState.Order);
			Assert.Equal(new[] { C }, result.StateChanged.AddedIds);
			Assert.Equal(new[] { B }, result.StateChanged.RemovedIds);
			Assert.Equal(oldA.WallpaperPosition, result.NewState.Records[A].WallpaperPosition);
			Assert.Equal(DesktopRecordOrigin.TrulyNewRecord, result.NewState.Records[C].Origin);
			Assert.Equal(DesktopStateChangeKind.Reset, result.StateChanged.Kind);
		}

		[Theory]
		[InlineData(0)]
		[InlineData(1)]
		[InlineData(2)]
		public void DeletionAtAnyIndexDoesNotTransferIdentity(int removedIndex)
		{
			var coordinator = Initialized(A, B, C);
			var ids = new[] { A, B, C }.Where((id, index) => index != removedIndex).ToArray();

			var result = coordinator.ApplyStableBatch(Batch(1, 2, ids[0], ids.Select((id, index) => Success(id, index, id.ToString(), "w")).ToArray()));

			Assert.Equal(ids, result.NewState.Order);
			Assert.DoesNotContain(new[] { A, B, C }[removedIndex], result.NewState.Records.Keys);
		}

		[Fact]
		public void SingleDesktopDeletionProducesEmptyConsistentState()
		{
			var coordinator = Initialized(A);

			var result = coordinator.ApplyStableBatch(Batch(1, 2, null));

			Assert.Empty(result.NewState.Order);
			Assert.Empty(result.NewState.Records);
			Assert.Null(result.NewState.CurrentDesktopId);
		}

		[Fact]
		public void NonAdjacentSingleMoveIsReportedWithAuthoritativeIndices()
		{
			var coordinator = Initialized(A, B, C, D);

			var result = coordinator.ApplyStableBatch(Batch(1, 2, B, Success(B, 0), Success(C, 1), Success(A, 2), Success(D, 3)));

			var move = Assert.Single(result.StateChanged.Moves);
			Assert.Equal(A, move.Id);
			Assert.Equal(0, move.OldIndex);
			Assert.Equal(2, move.NewIndex);
		}

		[Fact]
		public void AmbiguousAdjacentSwapIsResetWithoutInventedMove()
		{
			var coordinator = Initialized(A, B, C);

			var result = coordinator.ApplyStableBatch(Batch(1, 2, B, Success(B, 0), Success(A, 1), Success(C, 2)));

			Assert.Equal(DesktopStateChangeKind.Reset, result.StateChanged.Kind);
			Assert.Empty(result.StateChanged.Moves);
		}

		[Fact]
		public void RenameMoveWallpaperDestroyPreserveIdentityAndPosition()
		{
			var coordinator = Coordinator(new[] { "A", "B" }, new[] { "wa", "wb" }, new[] { WallpaperPosition.Tile, WallpaperPosition.Span });
			coordinator.ApplyStableBatch(Batch(1, 1, A, Failed(A, 0), Failed(B, 1)));
			coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 0, "renamed", "wa"), Failed(B, 1)));
			coordinator.ApplyStableBatch(Batch(1, 3, A, Failed(B, 0), Success(A, 1, "renamed", "new-wall")));

			var result = coordinator.ApplyStableBatch(Batch(1, 4, A, Success(A, 0, "renamed", "new-wall")));

			Assert.Equal("renamed", result.NewState.Records[A].Name.Value);
			Assert.Equal("new-wall", result.NewState.Records[A].WallpaperPath.Value);
			Assert.Equal(WallpaperPosition.Tile, result.NewState.Records[A].WallpaperPosition);
			Assert.DoesNotContain(B, result.NewState.Records.Keys);
		}

		[Fact]
		public void SameIndexReplacementNeverReceivesRemovedDesktopValues()
		{
			var coordinator = Coordinator(new[] { "seed" }, new[] { "wall" }, new[] { WallpaperPosition.Span });
			coordinator.ApplyStableBatch(Batch(1, 1, A, Failed(A, 0)));

			var result = coordinator.ApplyStableBatch(Batch(1, 2, C, Entry(C, 0, null, VirtualDesktopReadStatus.Failed, null, VirtualDesktopReadStatus.NotAttempted)));

			Assert.False(result.NewState.Records[C].Name.HasValue);
			Assert.False(result.NewState.Records[C].WallpaperPath.HasValue);
			Assert.Equal(WallpaperPosition.Fill, result.NewState.Records[C].WallpaperPosition);
			Assert.Equal(DesktopStateChangeKind.Reset, result.StateChanged.Kind);
		}

		[Fact]
		public void ProviderEpochResetKeepsSameGuidButNeverIndexMigratesNewGuid()
		{
			var coordinator = Initialized(A, B);
			var oldA = coordinator.State.Records[A];

			var same = coordinator.ApplyStableBatch(Batch(2, 1, A, Failed(A, 0), Failed(C, 1)));

			Assert.Equal(DesktopStateChangeKind.Reset, same.StateChanged.Kind);
			Assert.Equal(oldA.Name.Value, same.NewState.Records[A].Name.Value);
			Assert.False(same.NewState.Records[C].Name.HasValue);
			Assert.Equal(WallpaperPosition.Fill, same.NewState.Records[C].WallpaperPosition);
		}

		[Fact]
		public void ProviderResetClearsSeedEmptyCandidate()
		{
			var coordinator = Coordinator(new[] { "seed" }, null, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			var firstEmptyAfterReset = coordinator.ApplyStableBatch(Batch(2, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported)));

			Assert.Equal("seed", firstEmptyAfterReset.NewState.Records[A].Name.Value);
			Assert.True(firstEmptyAfterReset.RequiresReconciliation);
		}

		[Fact]
		public void ValidCurrentTransitionOnlyChangesCurrentAndStateRevision()
		{
			var coordinator = Initialized(A, B);
			var before = coordinator.State;

			var result = coordinator.ApplyCurrentTransition(new VirtualDesktopCurrentTransition(1, 10, 1, B));

			Assert.True(result.Accepted);
			Assert.Equal(B, result.NewState.CurrentDesktopId);
			Assert.Equal(before.StateRevision + 1, result.NewState.StateRevision);
			Assert.Equal(before.ProviderSnapshotRevision, result.NewState.ProviderSnapshotRevision);
			Assert.Equal(DesktopStateChangeKind.CurrentChanged, result.StateChanged.Kind);
			Assert.Null(result.Projection);
			Assert.False(result.RequiresSave);
		}

		[Theory]
		[InlineData(0, 1, 2)]
		[InlineData(1, 0, 2)]
		[InlineData(1, 1, 99)]
		public void InvalidCurrentTransitionDoesNotMutateAndRequestsReconciliation(long epoch, long revision, int idSelector)
		{
			var coordinator = Initialized(A, B);
			var before = coordinator.State;
			var id = idSelector == 2 ? B : C;

			var result = coordinator.ApplyCurrentTransition(new VirtualDesktopCurrentTransition(epoch, 10, revision, id));

			Assert.False(result.Accepted);
			Assert.Same(before, coordinator.State);
			Assert.True(result.RequiresReconciliation);
		}

		[Fact]
		public void DuplicateCurrentTransitionIsOrderedNoOpWithoutReconciliation()
		{
			var coordinator = Initialized(A, B);
			var before = coordinator.State;

			var result = coordinator.ApplyCurrentTransition(new VirtualDesktopCurrentTransition(1, 10, 1, A));

			Assert.True(result.Accepted);
			Assert.Same(before, coordinator.State);
			Assert.Same(before, result.NewState);
			Assert.Null(result.Projection);
			Assert.Null(result.StateChanged);
			Assert.False(result.RequiresSave);
			Assert.False(result.RequiresReconciliation);

			var older = coordinator.ApplyCurrentTransition(new VirtualDesktopCurrentTransition(1, 9, 1, B));
			Assert.False(older.Accepted);
			Assert.True(older.RequiresReconciliation);
			Assert.Same(before, coordinator.State);
		}

		[Fact]
		public void DuplicateCurrentStormConsumesSequenceWithoutReconciliation()
		{
			var coordinator = Initialized(A, B);
			var before = coordinator.State;

			for (var sequence = 1L; sequence <= 20; sequence++)
			{
				var result = coordinator.ApplyCurrentTransition(new VirtualDesktopCurrentTransition(1, sequence, 1, A));
				Assert.True(result.Accepted);
				Assert.False(result.RequiresReconciliation);
				Assert.Null(result.StateChanged);
			}

			Assert.Same(before, coordinator.State);
		}

		[Fact]
		public void CurrentReadFailureKeepsCompleteTopologyAndRequestsReconciliation()
		{
			var coordinator = Initialized(A, B);

			var result = coordinator.ApplyStableBatch(BatchWithCurrentStatus(1, 2, null, VirtualDesktopReadStatus.Failed, Success(A, 0), Success(B, 1)));

			Assert.Equal(new[] { A, B }, result.NewState.Order);
			Assert.Equal(A, result.NewState.CurrentDesktopId);
			Assert.True(result.RequiresReconciliation);
			Assert.Equal(DesktopReconciliationReason.CurrentReadIncomplete, result.ReconciliationReason);
		}

		[Fact]
		public void ProjectionPreservesOrderNullEmptyAndFill()
		{
			var coordinator = Coordinator();

			var result = coordinator.ApplyStableBatch(Batch(1, 1, B,
				Entry(B, 0, null, VirtualDesktopReadStatus.Failed, string.Empty, VirtualDesktopReadStatus.Success),
				Entry(A, 1, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			Assert.Equal(2, result.Projection.Names.Count);
			Assert.Equal(new string[] { null, string.Empty }, result.Projection.Names);
			Assert.Equal(new string[] { string.Empty, null }, result.Projection.WallpaperPaths);
			Assert.All(result.Projection.Positions, value => Assert.Equal(WallpaperPosition.Fill, value));
		}

		[Fact]
		public void UnchangedStableBatchAdvancesProviderRevisionWithoutStateChangedOrSave()
		{
			var coordinator = Initialized(A, B);
			var stateRevision = coordinator.State.StateRevision;

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 0), Success(B, 1)));

			Assert.True(result.Accepted);
			Assert.Equal(2, result.NewState.ProviderSnapshotRevision);
			Assert.Equal(stateRevision, result.NewState.StateRevision);
			Assert.Null(result.StateChanged);
			Assert.Null(result.Projection);
			Assert.False(result.RequiresSave);
		}

		[Fact]
		public void NameWallpaperAndPositionChangesAreReportedSeparately()
		{
			var coordinator = Initialized(A);
			var name = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, "name-2", coordinator.State));
			coordinator.CommitLocalEdit(name);
			var wallpaper = coordinator.PrepareLocalEdit(DesktopLocalEdit.WallpaperPath(A, "wall-2", coordinator.State));
			coordinator.CommitLocalEdit(wallpaper);
			var position = coordinator.PrepareLocalEdit(DesktopLocalEdit.WallpaperPosition(A, WallpaperPosition.Span, coordinator.State));

			Assert.Single(name.Transition.StateChanged.NameChanges);
			Assert.Single(wallpaper.Transition.StateChanged.WallpaperChanges);
			Assert.Single(position.Transition.StateChanged.PositionChanges);
		}

		[Fact]
		public void PreparedLocalEditDoesNotMutateUntilExplicitCommit()
		{
			var coordinator = Initialized(A);
			var before = coordinator.State;

			var prepared = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, "new", before));

			Assert.True(prepared.Transition.Accepted);
			Assert.Same(before, coordinator.State);
			Assert.Equal("new", prepared.Transition.NewState.Records[A].Name.Value);
			var committed = coordinator.CommitLocalEdit(prepared);
			Assert.Same(committed.NewState, coordinator.State);
		}

		[Fact]
		public void DiscardingPreparedEditModelsComFailureWithoutRollback()
		{
			var coordinator = Initialized(A);
			var before = coordinator.State;

			coordinator.PrepareLocalEdit(DesktopLocalEdit.WallpaperPath(A, "not-committed", before));

			Assert.Same(before, coordinator.State);
			Assert.NotEqual("not-committed", coordinator.State.Records[A].WallpaperPath.Value);
		}

		[Fact]
		public void LocalEditDistinguishesNullAndEmpty()
		{
			var nullCoordinator = Initialized(A);
			var emptyCoordinator = Initialized(A);

			var nullEdit = nullCoordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, null, nullCoordinator.State));
			var emptyEdit = emptyCoordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, string.Empty, emptyCoordinator.State));

			Assert.False(nullEdit.Transition.NewState.Records[A].Name.HasValue);
			Assert.True(emptyEdit.Transition.NewState.Records[A].Name.HasValue);
			Assert.Equal(string.Empty, emptyEdit.Transition.NewState.Records[A].Name.Value);
		}

		[Fact]
		public void LocalEditThatOnlyChangesUnknownMetadataDoesNotRequestSave()
		{
			var coordinator = Coordinator();
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, null, VirtualDesktopReadStatus.Failed, "wall", VirtualDesktopReadStatus.Success)));

			var prepared = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, null, coordinator.State));

			Assert.True(prepared.Transition.Accepted);
			Assert.NotNull(prepared.Transition.StateChanged);
			Assert.Null(prepared.Transition.Projection);
			Assert.False(prepared.Transition.RequiresSave);
		}

		[Fact]
		public void UnknownAndStaleLocalEditsAreRejected()
		{
			var coordinator = Initialized(A);
			var state = coordinator.State;
			var unknown = DesktopLocalEdit.Name(C, "x", state);
			coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 0)));

			Assert.False(coordinator.PrepareLocalEdit(unknown).Transition.Accepted);
			Assert.False(coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(C, "x", coordinator.State)).Transition.Accepted);
		}

		[Fact]
		public void UnsupportedNameEditIsRejectedButWallpaperUsesApplicationAuthority()
		{
			var coordinator = Coordinator(new[] { "name" }, new[] { "wall" }, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Unsupported(A, 0)));

			var name = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, "new-name", coordinator.State));
			var wallpaper = coordinator.PrepareLocalEdit(DesktopLocalEdit.WallpaperPath(A, "new-wall", coordinator.State));

			Assert.False(name.Transition.Accepted);
			Assert.True(wallpaper.Transition.Accepted);
			Assert.Equal(DesktopPropertyAuthority.ApplicationAuthoritative, wallpaper.Transition.NewState.Records[A].WallpaperPath.Authority);
			Assert.Equal(VirtualDesktopReadStatus.Unsupported, wallpaper.Transition.NewState.Records[A].WallpaperPath.ReadStatus);
		}

		[Fact]
		public void CommittedLocalEditClearsSeedCandidate()
		{
			var coordinator = Coordinator(new[] { "seed" }, null, null);
			coordinator.ApplyStableBatch(Batch(1, 1, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.NotAttempted)));

			var prepared = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, "local", coordinator.State));
			coordinator.CommitLocalEdit(prepared);

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Entry(A, 0, string.Empty, VirtualDesktopReadStatus.Success, null, VirtualDesktopReadStatus.Unsupported)));
			Assert.Equal(string.Empty, result.NewState.Records[A].Name.Value);
			Assert.False(result.RequiresReconciliation);
		}

		[Fact]
		public void InvalidStableInputsAreRejectedWithoutStateProjectionOrSave()
		{
			var coordinator = Initialized(A);
			var before = coordinator.State;
			var duplicate = Batch(1, 2, A, Success(A, 0), Success(A, 1));

			var result = coordinator.ApplyStableBatch(duplicate);

			Assert.False(result.Accepted);
			Assert.Same(before, coordinator.State);
			Assert.Null(result.Projection);
			Assert.False(result.RequiresSave);
			Assert.True(result.RequiresReconciliation);
		}

		[Fact]
		public void EmptyIdAndNonContiguousOrderAreRejected()
		{
			var coordinator = Initialized(A);

			Assert.False(coordinator.ApplyStableBatch(Batch(1, 2, A, Success(Guid.Empty, 0))).Accepted);
			Assert.False(coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 1))).Accepted);
			Assert.False(coordinator.ApplyStableBatch(BatchWithCurrentStatus(1, 2, A, (VirtualDesktopReadStatus)999, Success(A, 0))).Accepted);
		}

		[Fact]
		public void CurrentOutsideOrderIsRejected()
		{
			var coordinator = Initialized(A);

			Assert.False(coordinator.ApplyStableBatch(Batch(1, 2, B, Success(A, 0))).Accepted);
		}

		[Fact]
		public void OldBatchCannotOverwriteNewerState()
		{
			var coordinator = Initialized(A);
			coordinator.ApplyStableBatch(Batch(1, 3, A, Success(A, 0, "new", "wall")));
			var before = coordinator.State;

			var result = coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 0, "old", "wall")));

			Assert.False(result.Accepted);
			Assert.Same(before, coordinator.State);
			Assert.Equal("new", coordinator.State.Records[A].Name.Value);
		}

		[Fact]
		public void ForeignPreparedEditWithMatchingRevisionsIsRejected()
		{
			var coordinatorA = Initialized(A);
			var coordinatorB = Initialized(B);
			var prepared = coordinatorA.PrepareLocalEdit(DesktopLocalEdit.Name(A, "from-a", coordinatorA.State));
			var beforeB = coordinatorB.State;

			var result = coordinatorB.CommitLocalEdit(prepared);

			Assert.False(result.Accepted);
			Assert.Same(beforeB, coordinatorB.State);
			Assert.Equal(new[] { B }, coordinatorB.State.Order);
		}

		[Fact]
		public void ForgedPreparedEditWithDifferentCommandAndTransitionIsRejected()
		{
			var coordinator = Initialized(A);
			var name = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, "name", coordinator.State));
			var position = coordinator.PrepareLocalEdit(DesktopLocalEdit.WallpaperPosition(A, WallpaperPosition.Span, coordinator.State));
			var forged = DesktopTransitionCoordinator.DesktopPreparedLocalEdit.Create(new object(), coordinator.State, name.Command, position.Transition);
			var before = coordinator.State;

			var result = coordinator.CommitLocalEdit(forged);

			Assert.False(result.Accepted);
			Assert.Same(before, coordinator.State);
		}

		[Fact]
		public void PreparedEditCannotBeCommittedTwice()
		{
			var coordinator = Initialized(A);
			var prepared = coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, "once", coordinator.State));

			var first = coordinator.CommitLocalEdit(prepared);
			var afterFirst = coordinator.State;
			var second = coordinator.CommitLocalEdit(prepared);

			Assert.True(first.Accepted);
			Assert.False(second.Accepted);
			Assert.Same(afterFirst, coordinator.State);
			Assert.Equal("once", coordinator.State.Records[A].Name.Value);
		}

		[Fact]
		public void PreparedAndTransitionConstructorsAreNotAssemblyCallable()
		{
			var transitionConstructors = typeof(DesktopCoordinatorTransition).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			var preparedConstructors = typeof(DesktopTransitionCoordinator.DesktopPreparedLocalEdit).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

			Assert.NotEmpty(transitionConstructors);
			Assert.All(transitionConstructors, constructor => Assert.True(constructor.IsPrivate));
			Assert.NotEmpty(preparedConstructors);
			Assert.All(preparedConstructors, constructor => Assert.True(constructor.IsPrivate));
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void SupportedLocalWriteRemainsUnconfirmedAcrossFailedSnapshot(bool wallpaper)
		{
			var coordinator = Initialized(A);
			var prepared = PreparePropertyEdit(coordinator, wallpaper, "target");
			coordinator.CommitLocalEdit(prepared);

			var committed = GetProperty(coordinator.State, wallpaper);
			Assert.Equal("target", committed.Value);
			Assert.Equal(DesktopPropertyAuthority.LocalWrite, committed.Authority);
			Assert.Equal(VirtualDesktopReadStatus.NotAttempted, committed.ReadStatus);
			Assert.False(committed.IsConfirmed);

			coordinator.ApplyStableBatch(Batch(1, 2, A, PropertyEntry(A, 0, wallpaper, null, VirtualDesktopReadStatus.Failed)));
			var failed = GetProperty(coordinator.State, wallpaper);
			Assert.Equal("target", failed.Value);
			Assert.Equal(DesktopPropertyAuthority.LocalWrite, failed.Authority);
			Assert.Equal(VirtualDesktopReadStatus.Failed, failed.ReadStatus);
			Assert.False(failed.IsConfirmed);
		}

		[Theory]
		[InlineData(false, true)]
		[InlineData(false, false)]
		[InlineData(true, true)]
		[InlineData(true, false)]
		public void StableSuccessConfirmsOrSupersedesSupportedLocalWrite(bool wallpaper, bool targetMatches)
		{
			var coordinator = Initialized(A);
			var prepared = PreparePropertyEdit(coordinator, wallpaper, "target");
			coordinator.CommitLocalEdit(prepared);
			var providerValue = targetMatches ? "target" : "external";

			coordinator.ApplyStableBatch(Batch(1, 2, A, PropertyEntry(A, 0, wallpaper, providerValue, VirtualDesktopReadStatus.Success)));

			var property = GetProperty(coordinator.State, wallpaper);
			Assert.Equal(providerValue, property.Value);
			Assert.Equal(DesktopPropertyAuthority.ProviderStableValue, property.Authority);
			Assert.Equal(VirtualDesktopReadStatus.Success, property.ReadStatus);
			Assert.True(property.IsConfirmed);
		}

		[Theory]
		[InlineData(false, (int)DesktopPropertyAuthority.LocalWrite)]
		[InlineData(true, (int)DesktopPropertyAuthority.ApplicationAuthoritative)]
		public void UnsupportedCapabilityAppliesPropertyAuthorityAfterLocalWrite(bool wallpaper, int expectedAuthorityValue)
		{
			var coordinator = Initialized(A);
			var prepared = PreparePropertyEdit(coordinator, wallpaper, "target");
			coordinator.CommitLocalEdit(prepared);

			coordinator.ApplyStableBatch(Batch(1, 2, A, PropertyEntry(A, 0, wallpaper, null, VirtualDesktopReadStatus.Unsupported)));

			var property = GetProperty(coordinator.State, wallpaper);
			Assert.Equal("target", property.Value);
			Assert.Equal((DesktopPropertyAuthority)expectedAuthorityValue, property.Authority);
			Assert.Equal(VirtualDesktopReadStatus.Unsupported, property.ReadStatus);
			Assert.False(property.IsConfirmed);
		}

		[Fact]
		public void PropertyStateRejectsInvalidStatusAuthorityAndConfirmationCombinations()
		{
			Assert.Throws<ArgumentException>(() => new DesktopPropertyState(false, null, VirtualDesktopReadStatus.Success, DesktopPropertyAuthority.ProviderStableValue, true));
			Assert.Throws<ArgumentOutOfRangeException>(() => new DesktopPropertyState(false, null, (VirtualDesktopReadStatus)999, DesktopPropertyAuthority.Unknown, false));
			Assert.Throws<ArgumentOutOfRangeException>(() => new DesktopPropertyState(false, null, VirtualDesktopReadStatus.Failed, (DesktopPropertyAuthority)999, false));
			Assert.Throws<ArgumentException>(() => new DesktopPropertyState(true, "value", VirtualDesktopReadStatus.Success, DesktopPropertyAuthority.LocalWrite, true));
			Assert.Throws<ArgumentException>(() => new DesktopPropertyState(true, "value", VirtualDesktopReadStatus.Success, DesktopPropertyAuthority.ApplicationAuthoritative, true));
		}

		[Fact]
		public void TransitionFactoriesRejectContradictoryEffects()
		{
			var coordinator = Initialized(A);
			var state = coordinator.State;
			var otherState = new DesktopRuntimeState(state.StateRevision + 1, state.ProviderEpoch, state.ProviderSnapshotRevision, state.CurrentDesktopId, state.Order, state.Records.ToDictionary(pair => pair.Key, pair => pair.Value));
			var changed = new DesktopStateChanged(DesktopStateChangeKind.LocalEdit, state, null, null, null, null, null, null, state.CurrentDesktopId);

			Assert.Throws<ArgumentException>(() => DesktopCoordinatorTransition.AcceptedChange(state, null, null, true, false, DesktopReconciliationReason.None));
			Assert.Throws<ArgumentException>(() => DesktopCoordinatorTransition.AcceptedChange(otherState, null, changed, false, false, DesktopReconciliationReason.None));
			Assert.Throws<ArgumentException>(() => DesktopCoordinatorTransition.AcceptedChange(state, null, null, false, false, DesktopReconciliationReason.InvalidStableBatch));
			Assert.Throws<ArgumentException>(() => DesktopCoordinatorTransition.Rejected(state, true, DesktopReconciliationReason.None));

			var rejected = DesktopCoordinatorTransition.Rejected(state, false, DesktopReconciliationReason.None);
			Assert.False(rejected.Accepted);
			Assert.Same(state, rejected.NewState);
			Assert.Null(rejected.Projection);
			Assert.Null(rejected.StateChanged);
			Assert.False(rejected.RequiresSave);
			Assert.False(rejected.RequiresReconciliation);
		}

		[Fact]
		public void StateRevisionZeroIsRejected()
		{
			var record = new DesktopRecord(A, DesktopPropertyState.Provider("a"), DesktopPropertyState.Provider("w"), WallpaperPosition.Fill, DesktopRecordOrigin.TrulyNewRecord);
			Assert.Throws<ArgumentOutOfRangeException>(() => new DesktopRuntimeState(0, 1, 1, A, new[] { A }, new Dictionary<Guid, DesktopRecord> { [A] = record }));
		}

		[Fact]
		public void StateAndSeedCollectionsAreDefensiveAndReadOnly()
		{
			var names = new[] { "seed" };
			var seed = new DesktopStartupSeed(names, null, null);
			names[0] = "mutated";
			var coordinator = new DesktopTransitionCoordinator(seed);
			var result = coordinator.ApplyStableBatch(Batch(1, 1, A, Failed(A, 0)));

			Assert.Equal("seed", result.NewState.Records[A].Name.Value);
			Assert.Throws<NotSupportedException>(() => ((IList<Guid>)result.NewState.Order).Add(B));
			Assert.Throws<NotSupportedException>(() => ((IDictionary<Guid, DesktopRecord>)result.NewState.Records).Add(B, result.NewState.Records[A]));
		}

		[Fact]
		public void RuntimeStateConstructorEnforcesIdentityInvariants()
		{
			var record = new DesktopRecord(A, DesktopPropertyState.Provider("a"), DesktopPropertyState.Provider("w"), WallpaperPosition.Fill, DesktopRecordOrigin.TrulyNewRecord);
			var records = new Dictionary<Guid, DesktopRecord> { [A] = record };

			Assert.Throws<ArgumentException>(() => new DesktopRuntimeState(1, 1, 1, null, new[] { A, A }, records));
			Assert.Throws<ArgumentException>(() => new DesktopRuntimeState(1, 1, 1, null, new[] { Guid.Empty }, records));
			Assert.Throws<ArgumentException>(() => new DesktopRuntimeState(1, 1, 1, null, new[] { B }, records));
			Assert.Throws<ArgumentException>(() => new DesktopRuntimeState(1, 1, 1, B, new[] { A }, records));
		}

		[Fact]
		public void StateRevisionIsMonotonicOnlyForDomainChanges()
		{
			var coordinator = Initialized(A);
			var initialized = coordinator.State.StateRevision;
			coordinator.ApplyStableBatch(Batch(1, 2, A, Success(A, 0)));
			var unchanged = coordinator.State.StateRevision;
			coordinator.ApplyStableBatch(Batch(1, 3, A, Success(A, 0, "changed", "wall")));

			Assert.Equal(initialized, unchanged);
			Assert.Equal(unchanged + 1, coordinator.State.StateRevision);
		}

		private static DesktopTransitionCoordinator Initialized(params Guid[] ids)
		{
			var coordinator = Coordinator();
			coordinator.ApplyStableBatch(Batch(1, 1, ids.Length == 0 ? (Guid?)null : ids[0], ids.Select((id, index) => Success(id, index)).ToArray()));
			return coordinator;
		}

		private static DesktopTransitionCoordinator Coordinator(IEnumerable<string> names = null, IEnumerable<string> wallpapers = null, IEnumerable<WallpaperPosition> positions = null)
			=> new DesktopTransitionCoordinator(new DesktopStartupSeed(names, wallpapers, positions));

		private static VirtualDesktopStableBatch Batch(long epoch, long revision, Guid? current, params VirtualDesktopStableEntry[] entries)
			=> new VirtualDesktopStableBatch(epoch, revision, current, current.HasValue ? VirtualDesktopReadStatus.Success : VirtualDesktopReadStatus.NotAttempted, entries, VirtualDesktopStableReason.ExplicitReconciliation);

		private static VirtualDesktopStableBatch BatchWithCurrentStatus(long epoch, long revision, Guid? current, VirtualDesktopReadStatus status, params VirtualDesktopStableEntry[] entries)
			=> new VirtualDesktopStableBatch(epoch, revision, current, status, entries, VirtualDesktopStableReason.ExplicitReconciliation);

		private static DesktopTransitionCoordinator.DesktopPreparedLocalEdit PreparePropertyEdit(DesktopTransitionCoordinator coordinator, bool wallpaper, string value)
			=> wallpaper
				? coordinator.PrepareLocalEdit(DesktopLocalEdit.WallpaperPath(A, value, coordinator.State))
				: coordinator.PrepareLocalEdit(DesktopLocalEdit.Name(A, value, coordinator.State));

		private static DesktopPropertyState GetProperty(DesktopRuntimeState state, bool wallpaper)
			=> wallpaper ? state.Records[A].WallpaperPath : state.Records[A].Name;

		private static VirtualDesktopStableEntry PropertyEntry(Guid id, int index, bool wallpaper, string value, VirtualDesktopReadStatus status)
			=> wallpaper
				? Entry(id, index, id.ToString(), VirtualDesktopReadStatus.Success, value, status)
				: Entry(id, index, value, status, "wall", VirtualDesktopReadStatus.Success);

		private static VirtualDesktopStableEntry Success(Guid id, int index, string name = null, string wallpaper = null)
			=> Entry(id, index, name ?? id.ToString(), VirtualDesktopReadStatus.Success, wallpaper ?? "wall", VirtualDesktopReadStatus.Success);

		private static VirtualDesktopStableEntry Failed(Guid id, int index)
			=> Entry(id, index, null, VirtualDesktopReadStatus.Failed, null, VirtualDesktopReadStatus.Failed);

		private static VirtualDesktopStableEntry Unsupported(Guid id, int index)
			=> Entry(id, index, null, VirtualDesktopReadStatus.Unsupported, null, VirtualDesktopReadStatus.Unsupported);

		private static VirtualDesktopStableEntry Entry(Guid id, int index, string name, VirtualDesktopReadStatus nameStatus, string wallpaper, VirtualDesktopReadStatus wallpaperStatus)
			=> new VirtualDesktopStableEntry(id, index, name, nameStatus, wallpaper, wallpaperStatus);
	}
}
