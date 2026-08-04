using System;
using System.Collections.Generic;
using System.Linq;
using WindowsDesktop;

namespace SylphyHorn.Services.DesktopTransitions
{
	internal sealed class DesktopTransitionCoordinator
	{
		private readonly DesktopStartupSeed _startupSeed;
		private readonly Dictionary<SeedCandidateKey, SeedEmptyCandidate> _seedEmptyCandidates = new Dictionary<SeedCandidateKey, SeedEmptyCandidate>();
		private readonly object _ownerToken = new object();
		private DesktopRuntimeState _state;
		private bool _startupSeedAssigned;
		private long _lastCurrentIngressSequence;

		internal DesktopTransitionCoordinator(DesktopStartupSeed startupSeed)
		{
			this._startupSeed = startupSeed ?? throw new ArgumentNullException(nameof(startupSeed));
		}

		private DesktopTransitionCoordinator(DesktopTransitionCoordinator source)
		{
			this._startupSeed = source._startupSeed;
			this._state = source._state;
			this._startupSeedAssigned = source._startupSeedAssigned;
			this._lastCurrentIngressSequence = source._lastCurrentIngressSequence;
			foreach (var candidate in source._seedEmptyCandidates) this._seedEmptyCandidates.Add(candidate.Key, candidate.Value);
		}

		internal DesktopRuntimeState State => this._state;

		internal DesktopCoordinatorTransition ApplyStableBatch(VirtualDesktopStableBatch batch)
		{
			if (!TryValidateBatch(batch, out var orderedEntries))
				return this.Rejected(DesktopReconciliationReason.InvalidStableBatch);

			var previous = this._state;
			if (previous != null)
			{
				if (batch.ProviderEpoch < previous.ProviderEpoch ||
					(batch.ProviderEpoch == previous.ProviderEpoch && batch.SnapshotRevision <= previous.ProviderSnapshotRevision))
					return this.Rejected(DesktopReconciliationReason.StaleStableBatch);
			}

			var reset = previous != null && batch.ProviderEpoch != previous.ProviderEpoch;
			if (reset)
			{
				this._seedEmptyCandidates.Clear();
				this._lastCurrentIngressSequence = 0;
			}

			var order = orderedEntries.Select(entry => entry.Id).ToList();
			var records = new Dictionary<Guid, DesktopRecord>();
			var requiresReconciliation = false;
			var reconciliationReason = DesktopReconciliationReason.None;

			for (var index = 0; index < orderedEntries.Count; index++)
			{
				var entry = orderedEntries[index];
				DesktopRecord oldRecord = null;
				previous?.Records.TryGetValue(entry.Id, out oldRecord);
				var baseRecord = oldRecord ?? this.CreateInitialRecord(entry.Id, index, !this._startupSeedAssigned);
				var name = this.ApplyProperty(baseRecord, baseRecord.Name, entry.Name, entry.NameReadStatus, DesktopPropertyKind.Name, batch.ProviderEpoch, batch.SnapshotRevision, ref requiresReconciliation, ref reconciliationReason);
				var wallpaper = this.ApplyProperty(baseRecord, baseRecord.WallpaperPath, entry.WallpaperPath, entry.WallpaperPathReadStatus, DesktopPropertyKind.WallpaperPath, batch.ProviderEpoch, batch.SnapshotRevision, ref requiresReconciliation, ref reconciliationReason);
				records.Add(entry.Id, baseRecord.With(name, wallpaper));
			}

			var activeIds = new HashSet<Guid>(order);
			foreach (var key in this._seedEmptyCandidates.Keys.Where(key => !activeIds.Contains(key.DesktopId)).ToList()) this._seedEmptyCandidates.Remove(key);

			Guid? currentDesktopId;
			if (batch.CurrentDesktopReadStatus == VirtualDesktopReadStatus.Success)
			{
				currentDesktopId = batch.CurrentDesktopId;
			}
			else
			{
				currentDesktopId = previous?.CurrentDesktopId;
				if (currentDesktopId.HasValue && !activeIds.Contains(currentDesktopId.Value)) currentDesktopId = null;
				if (batch.CurrentDesktopReadStatus == VirtualDesktopReadStatus.Failed || batch.CurrentDesktopReadStatus == VirtualDesktopReadStatus.NotAttempted)
				{
					requiresReconciliation = true;
					if (reconciliationReason == DesktopReconciliationReason.None) reconciliationReason = DesktopReconciliationReason.CurrentReadIncomplete;
				}
			}

			var provisional = new DesktopRuntimeState(previous == null ? 1 : previous.StateRevision, batch.ProviderEpoch, batch.SnapshotRevision, currentDesktopId, order, records);
			var diff = CreateDiff(previous, provisional, reset);
			var domainChanged = previous == null || diff.HasChanges || reset;
			var stateRevision = previous == null ? 1 : previous.StateRevision + (domainChanged ? 1 : 0);
			var next = new DesktopRuntimeState(stateRevision, batch.ProviderEpoch, batch.SnapshotRevision, currentDesktopId, order, records);

			DesktopSettingsProjection projection = null;
			var requiresSave = false;
			DesktopStateChanged stateChanged = null;
			if (previous == null)
			{
				projection = CreateProjection(next);
				requiresSave = !ProjectionEqualsSeed(projection, this._startupSeed);
				stateChanged = diff.ToStateChanged(DesktopStateChangeKind.Initialized, next);
			}
			else if (domainChanged)
			{
				var previousProjection = CreateProjection(previous);
				var nextProjection = CreateProjection(next);
				requiresSave = !ProjectionEquals(previousProjection, nextProjection);
				projection = requiresSave ? nextProjection : null;
				stateChanged = diff.ToStateChanged(diff.Kind, next);
			}

			this._state = next;
			this._startupSeedAssigned = true;
			return DesktopCoordinatorTransition.AcceptedChange(next, projection, stateChanged, requiresSave, requiresReconciliation, reconciliationReason);
		}

		internal DesktopCoordinatorTransition ApplyCurrentTransition(VirtualDesktopCurrentTransition transition)
		{
			if (transition == null || this._state == null ||
				transition.ProviderEpoch != this._state.ProviderEpoch ||
				transition.BaseSnapshotRevision != this._state.ProviderSnapshotRevision ||
				transition.IngressSequence <= this._lastCurrentIngressSequence ||
				transition.CurrentDesktopId == Guid.Empty ||
				!this._state.Records.ContainsKey(transition.CurrentDesktopId))
				return this.Rejected(DesktopReconciliationReason.InvalidCurrentTransition);

			this._lastCurrentIngressSequence = transition.IngressSequence;
			if (transition.CurrentDesktopId == this._state.CurrentDesktopId)
				return DesktopCoordinatorTransition.AcceptedNoOp(this._state);

			var previous = this._state;
			var next = new DesktopRuntimeState(previous.StateRevision + 1, previous.ProviderEpoch, previous.ProviderSnapshotRevision, transition.CurrentDesktopId, previous.Order, previous.Records.ToDictionary(pair => pair.Key, pair => pair.Value));
			var changed = new DesktopStateChanged(DesktopStateChangeKind.CurrentChanged, next, null, null, null, null, null, null, previous.CurrentDesktopId);
			this._state = next;
			return DesktopCoordinatorTransition.AcceptedChange(next, null, changed, false, false, DesktopReconciliationReason.None);
		}

		internal DesktopPreparedLocalEdit PrepareLocalEdit(DesktopLocalEdit command)
		{
			var sourceState = this._state;
			if (command == null || sourceState == null || command.DesktopId == Guid.Empty ||
				command.ProviderEpoch != sourceState.ProviderEpoch ||
				command.ProviderSnapshotRevision != sourceState.ProviderSnapshotRevision ||
				command.StateRevision != sourceState.StateRevision ||
				!sourceState.Records.TryGetValue(command.DesktopId, out var oldRecord))
				return DesktopPreparedLocalEdit.Create(this._ownerToken, sourceState, command, this.Rejected(DesktopReconciliationReason.InvalidLocalEdit));

			DesktopRecord newRecord;
			if (command.Position.HasValue)
			{
				newRecord = oldRecord.With(wallpaperPosition: command.Position.Value);
			}
			else if (command.Property == DesktopPropertyKind.Name)
			{
				if (oldRecord.Name.ReadStatus == VirtualDesktopReadStatus.Unsupported)
					return DesktopPreparedLocalEdit.Create(this._ownerToken, sourceState, command, DesktopCoordinatorTransition.Rejected(sourceState, false, DesktopReconciliationReason.None));
				newRecord = oldRecord.With(name: CreateLocalProperty(command.Value, false));
			}
			else if (command.Property == DesktopPropertyKind.WallpaperPath)
			{
				newRecord = oldRecord.With(wallpaperPath: CreateLocalProperty(command.Value, oldRecord.WallpaperPath.ReadStatus == VirtualDesktopReadStatus.Unsupported));
			}
			else
			{
				return DesktopPreparedLocalEdit.Create(this._ownerToken, sourceState, command, DesktopCoordinatorTransition.Rejected(sourceState, false, DesktopReconciliationReason.None));
			}

			var records = sourceState.Records.ToDictionary(pair => pair.Key, pair => pair.Value);
			records[command.DesktopId] = newRecord;
			var changedValue = !RecordEquals(oldRecord, newRecord);
			var next = changedValue
				? new DesktopRuntimeState(sourceState.StateRevision + 1, sourceState.ProviderEpoch, sourceState.ProviderSnapshotRevision, sourceState.CurrentDesktopId, sourceState.Order, records)
				: sourceState;
			DesktopSettingsProjection projection = null;
			DesktopStateChanged stateChanged = null;
			var requiresSave = false;
			if (changedValue)
			{
				var previousProjection = CreateProjection(sourceState);
				var nextProjection = CreateProjection(next);
				requiresSave = !ProjectionEquals(previousProjection, nextProjection);
				projection = requiresSave ? nextProjection : null;
				stateChanged = CreateDiff(sourceState, next, false).ToStateChanged(DesktopStateChangeKind.LocalEdit, next);
			}

			var transition = DesktopCoordinatorTransition.AcceptedChange(next, projection, stateChanged, requiresSave, false, DesktopReconciliationReason.None);
			return DesktopPreparedLocalEdit.Create(this._ownerToken, sourceState, command, transition);
		}

		internal DesktopCoordinatorTransition CommitLocalEdit(DesktopPreparedLocalEdit prepared)
		{
			if (prepared == null || this._state == null || !prepared.TryConsume(this._ownerToken, this._state))
				return this.Rejected(DesktopReconciliationReason.InvalidLocalEdit);

			this._state = prepared.Transition.NewState;
			if (prepared.Command.Property.HasValue) this._seedEmptyCandidates.Remove(new SeedCandidateKey(prepared.Command.DesktopId, prepared.Command.Property.Value));
			return prepared.Transition;
		}
		internal DesktopPreparedRuntime BeginStagedRuntime()
		{
			if (this._state == null) throw new InvalidOperationException("An initial stable state is required before staging runtime changes.");
			return DesktopPreparedRuntime.Create(this._ownerToken, this._state, new DesktopTransitionCoordinator(this));
		}

		internal DesktopCoordinatorTransition CommitStagedRuntime(DesktopPreparedRuntime prepared, bool requiresSave)
		{
			if (prepared == null || this._state == null || !prepared.TryConsume(this._ownerToken, this._state))
				return this.Rejected(DesktopReconciliationReason.InvalidLocalEdit);
			var before = this._state;
			var staged = prepared.Coordinator;
			var next = staged.State;
			if (ReferenceEquals(before, next)) return DesktopCoordinatorTransition.AcceptedNoOp(before);
			this._state = next;
			this._startupSeedAssigned = staged._startupSeedAssigned;
			this._lastCurrentIngressSequence = staged._lastCurrentIngressSequence;
			this._seedEmptyCandidates.Clear();
			foreach (var candidate in staged._seedEmptyCandidates) this._seedEmptyCandidates.Add(candidate.Key, candidate.Value);
			var projection = CreateProjection(next);
			var changed = CreateDiff(before, next, true).ToStateChanged(DesktopStateChangeKind.Reset, next);
			return DesktopCoordinatorTransition.AcceptedChange(next, projection, changed, requiresSave, false, DesktopReconciliationReason.None);
		}

		private DesktopRecord CreateInitialRecord(Guid id, int index, bool allowSeed)
		{
			var hasSeed = allowSeed && (index < this._startupSeed.Names.Count || index < this._startupSeed.WallpaperPaths.Count || index < this._startupSeed.Positions.Count);
			if (!hasSeed) return new DesktopRecord(id, DesktopPropertyState.Unknown(VirtualDesktopReadStatus.NotAttempted), DesktopPropertyState.Unknown(VirtualDesktopReadStatus.NotAttempted), WallpaperPosition.Fill, DesktopRecordOrigin.TrulyNewRecord);

			var name = index < this._startupSeed.Names.Count ? DesktopPropertyState.Persisted(this._startupSeed.Names[index]) : DesktopPropertyState.Unknown(VirtualDesktopReadStatus.NotAttempted);
			var wallpaper = index < this._startupSeed.WallpaperPaths.Count ? DesktopPropertyState.Persisted(this._startupSeed.WallpaperPaths[index]) : DesktopPropertyState.Unknown(VirtualDesktopReadStatus.NotAttempted);
			var position = index < this._startupSeed.Positions.Count ? this._startupSeed.Positions[index] : WallpaperPosition.Fill;
			return new DesktopRecord(id, name, wallpaper, position, DesktopRecordOrigin.SeededExistingRecord);
		}

		private DesktopPropertyState ApplyProperty(DesktopRecord record, DesktopPropertyState current, string value, VirtualDesktopReadStatus status, DesktopPropertyKind property, long epoch, long snapshotRevision, ref bool requiresReconciliation, ref DesktopReconciliationReason reason)
		{
			var key = new SeedCandidateKey(record.Id, property);
			switch (status)
			{
				case VirtualDesktopReadStatus.Success:
					if (value == null) throw new ArgumentException("A successful stable property cannot be null.");
					if (value.Length != 0)
					{
						this._seedEmptyCandidates.Remove(key);
						return DesktopPropertyState.Provider(value);
					}

					if (record.Origin == DesktopRecordOrigin.SeededExistingRecord && current.HasValue && current.Value.Length != 0 && current.Authority == DesktopPropertyAuthority.PersistedLastKnownGood)
					{
						if (this._seedEmptyCandidates.TryGetValue(key, out var candidate) && candidate.ProviderEpoch == epoch)
						{
							this._seedEmptyCandidates.Remove(key);
							return DesktopPropertyState.Provider(string.Empty);
						}

						this._seedEmptyCandidates[key] = new SeedEmptyCandidate(epoch, snapshotRevision, record.Id, property);
						requiresReconciliation = true;
						reason = DesktopReconciliationReason.SeedEmptyConfirmation;
						return current;
					}

					this._seedEmptyCandidates.Remove(key);
					return DesktopPropertyState.Provider(string.Empty);

				case VirtualDesktopReadStatus.Failed:
				case VirtualDesktopReadStatus.NotAttempted:
					requiresReconciliation = true;
					if (reason == DesktopReconciliationReason.None) reason = DesktopReconciliationReason.PropertyReadIncomplete;
					return current.HasValue ? current.Preserve(status) : DesktopPropertyState.Unknown(status, current.Authority);

				case VirtualDesktopReadStatus.Unsupported:
					this._seedEmptyCandidates.Remove(key);
					if (current.HasValue)
					{
						var authority = property == DesktopPropertyKind.WallpaperPath ? DesktopPropertyAuthority.ApplicationAuthoritative : current.Authority;
						return new DesktopPropertyState(true, current.Value, status, authority, current.IsConfirmed);
					}
					return DesktopPropertyState.Unknown(status, property == DesktopPropertyKind.WallpaperPath ? DesktopPropertyAuthority.ApplicationAuthoritative : DesktopPropertyAuthority.Unknown);

				default:
					throw new ArgumentOutOfRangeException(nameof(status));
			}
		}

		private static DesktopPropertyState CreateLocalProperty(string value, bool unsupported)
		{
			var status = unsupported ? VirtualDesktopReadStatus.Unsupported : VirtualDesktopReadStatus.NotAttempted;
			var authority = unsupported ? DesktopPropertyAuthority.ApplicationAuthoritative : DesktopPropertyAuthority.LocalWrite;
			return new DesktopPropertyState(value != null, value, status, authority, false);
		}

		private DesktopCoordinatorTransition Rejected(DesktopReconciliationReason reason)
			=> DesktopCoordinatorTransition.Rejected(this._state, true, reason);

		private static bool TryValidateBatch(VirtualDesktopStableBatch batch, out List<VirtualDesktopStableEntry> entries)
		{
			entries = null;
			if (batch == null || batch.ProviderEpoch <= 0 || batch.SnapshotRevision <= 0 || batch.Desktops == null) return false;
			var materialized = batch.Desktops.ToList();
			if (materialized.Any(entry => entry == null || entry.Id == Guid.Empty || entry.OrderIndex < 0)) return false;
			if (!IsReadStatus(batch.CurrentDesktopReadStatus) || materialized.Any(entry => !IsReadStatus(entry.NameReadStatus) || !IsReadStatus(entry.WallpaperPathReadStatus))) return false;
			if (materialized.Select(entry => entry.Id).Distinct().Count() != materialized.Count) return false;
			if (materialized.Select(entry => entry.OrderIndex).Distinct().Count() != materialized.Count) return false;
			entries = materialized.OrderBy(entry => entry.OrderIndex).ToList();
			if (entries.Where((entry, index) => entry.OrderIndex != index).Any()) return false;
			if (batch.CurrentDesktopReadStatus == VirtualDesktopReadStatus.Success && (!batch.CurrentDesktopId.HasValue || !entries.Any(entry => entry.Id == batch.CurrentDesktopId.Value))) return false;
			return true;
		}

		private static bool IsReadStatus(VirtualDesktopReadStatus status)
			=> status == VirtualDesktopReadStatus.Success || status == VirtualDesktopReadStatus.Unsupported || status == VirtualDesktopReadStatus.Failed || status == VirtualDesktopReadStatus.NotAttempted;

		private static DesktopSettingsProjection CreateProjection(DesktopRuntimeState state)
			=> new DesktopSettingsProjection(
				state.Order.Select(id => state.Records[id].Name.HasValue ? state.Records[id].Name.Value : null),
				state.Order.Select(id => state.Records[id].WallpaperPath.HasValue ? state.Records[id].WallpaperPath.Value : null),
				state.Order.Select(id => state.Records[id].WallpaperPosition));

		private static bool ProjectionEqualsSeed(DesktopSettingsProjection projection, DesktopStartupSeed seed)
			=> projection.Names.SequenceEqual(seed.Names) && projection.WallpaperPaths.SequenceEqual(seed.WallpaperPaths) && projection.Positions.SequenceEqual(seed.Positions);

		private static bool ProjectionEquals(DesktopSettingsProjection left, DesktopSettingsProjection right)
			=> left.Names.SequenceEqual(right.Names) && left.WallpaperPaths.SequenceEqual(right.WallpaperPaths) && left.Positions.SequenceEqual(right.Positions);

		private static Diff CreateDiff(DesktopRuntimeState before, DesktopRuntimeState after, bool providerReset)
		{
			if (before == null) return Diff.Initial(after.Order);

			var beforeIds = new HashSet<Guid>(before.Order);
			var afterIds = new HashSet<Guid>(after.Order);
			var added = after.Order.Where(id => !beforeIds.Contains(id)).ToList();
			var removed = before.Order.Where(id => !afterIds.Contains(id)).ToList();
			var common = after.Order.Where(beforeIds.Contains).ToList();
			var names = new List<DesktopPropertyChange>();
			var wallpapers = new List<DesktopPropertyChange>();
			var positions = new List<DesktopPositionChange>();
			foreach (var id in common)
			{
				var oldRecord = before.Records[id];
				var newRecord = after.Records[id];
				if (!PropertyEquals(oldRecord.Name, newRecord.Name)) names.Add(new DesktopPropertyChange(id, oldRecord.Name, newRecord.Name));
				if (!PropertyEquals(oldRecord.WallpaperPath, newRecord.WallpaperPath)) wallpapers.Add(new DesktopPropertyChange(id, oldRecord.WallpaperPath, newRecord.WallpaperPath));
				if (oldRecord.WallpaperPosition != newRecord.WallpaperPosition) positions.Add(new DesktopPositionChange(id, oldRecord.WallpaperPosition, newRecord.WallpaperPosition));
			}

			var moves = new List<DesktopMove>();
			var ambiguousOrder = false;
			if (added.Count == 0 && removed.Count == 0 && !before.Order.SequenceEqual(after.Order))
			{
				if (TryFindSingleMove(before.Order, after.Order, out var move)) moves.Add(move);
				else ambiguousOrder = true;
			}
			else if ((added.Count != 0 && removed.Count != 0) || !before.Order.Where(afterIds.Contains).SequenceEqual(after.Order.Where(beforeIds.Contains)))
			{
				ambiguousOrder = true;
			}

			var allIdsReplaced = before.Order.Count != 0 && after.Order.Count != 0 && common.Count == 0;
			var kind = providerReset || allIdsReplaced || ambiguousOrder ? DesktopStateChangeKind.Reset : DesktopStateChangeKind.Reconciled;
			return new Diff(kind, added, removed, moves, names, wallpapers, positions, before.CurrentDesktopId, after.CurrentDesktopId);
		}

		private static bool TryFindSingleMove(IReadOnlyList<Guid> before, IReadOnlyList<Guid> after, out DesktopMove move)
		{
			move = null;
			DesktopMove candidate = null;
			var matches = 0;
			for (var oldIndex = 0; oldIndex < before.Count; oldIndex++)
			{
				for (var newIndex = 0; newIndex < before.Count; newIndex++)
				{
					if (oldIndex == newIndex) continue;
					var list = before.ToList();
					var id = list[oldIndex];
					list.RemoveAt(oldIndex);
					list.Insert(newIndex, id);
					if (!list.SequenceEqual(after)) continue;
					candidate = new DesktopMove(id, oldIndex, newIndex);
					matches++;
				}
			}
			if (matches != 1) return false;
			move = candidate;
			return true;
		}

		private static bool RecordEquals(DesktopRecord left, DesktopRecord right)
			=> left.Id == right.Id && left.Origin == right.Origin && left.WallpaperPosition == right.WallpaperPosition && PropertyEquals(left.Name, right.Name) && PropertyEquals(left.WallpaperPath, right.WallpaperPath);

		private static bool PropertyEquals(DesktopPropertyState left, DesktopPropertyState right)
			=> left.HasValue == right.HasValue && left.Value == right.Value && left.ReadStatus == right.ReadStatus && left.Authority == right.Authority && left.IsConfirmed == right.IsConfirmed;

		internal sealed class DesktopPreparedRuntime
		{
			private readonly object _ownerToken;
			private readonly DesktopRuntimeState _sourceState;
			private bool _consumed;

			private DesktopPreparedRuntime(object ownerToken, DesktopRuntimeState sourceState, DesktopTransitionCoordinator coordinator)
			{
				this._ownerToken = ownerToken;
				this._sourceState = sourceState;
				this.Coordinator = coordinator;
			}

			internal DesktopTransitionCoordinator Coordinator { get; }
			internal static DesktopPreparedRuntime Create(object ownerToken, DesktopRuntimeState sourceState, DesktopTransitionCoordinator coordinator)
				=> new DesktopPreparedRuntime(ownerToken, sourceState, coordinator);
			internal bool TryConsume(object ownerToken, DesktopRuntimeState state)
			{
				if (this._consumed || !ReferenceEquals(this._ownerToken, ownerToken) || !ReferenceEquals(this._sourceState, state) || this.Coordinator?.State == null) return false;
				this._consumed = true;
				return true;
			}
		}
		internal sealed class DesktopPreparedLocalEdit
		{
			private readonly object _ownerToken;
			private readonly DesktopRuntimeState _sourceState;
			private bool _consumed;

			private DesktopPreparedLocalEdit(object ownerToken, DesktopRuntimeState sourceState, DesktopLocalEdit command, DesktopCoordinatorTransition transition)
			{
				this._ownerToken = ownerToken ?? throw new ArgumentNullException(nameof(ownerToken));
				this._sourceState = sourceState;
				this.Command = command;
				this.Transition = transition ?? throw new ArgumentNullException(nameof(transition));
			}

			internal DesktopLocalEdit Command { get; }
			internal DesktopCoordinatorTransition Transition { get; }

			internal static DesktopPreparedLocalEdit Create(object ownerToken, DesktopRuntimeState sourceState, DesktopLocalEdit command, DesktopCoordinatorTransition transition)
				=> new DesktopPreparedLocalEdit(ownerToken, sourceState, command, transition);

			internal bool TryConsume(object ownerToken, DesktopRuntimeState currentState)
			{
				if (this._consumed || !this.Transition.Accepted || !ReferenceEquals(this._ownerToken, ownerToken) || !ReferenceEquals(this._sourceState, currentState))
					return false;
				if (this.Command == null || this.Transition.NewState == null || (this.Transition.StateChanged != null && !ReferenceEquals(this.Transition.StateChanged.Snapshot, this.Transition.NewState)))
					return false;

				this._consumed = true;
				return true;
			}
		}

		private readonly struct SeedCandidateKey : IEquatable<SeedCandidateKey>
		{
			internal SeedCandidateKey(Guid desktopId, DesktopPropertyKind property) { this.DesktopId = desktopId; this.Property = property; }
			internal Guid DesktopId { get; }
			internal DesktopPropertyKind Property { get; }
			public bool Equals(SeedCandidateKey other) => this.DesktopId == other.DesktopId && this.Property == other.Property;
			public override bool Equals(object obj) => obj is SeedCandidateKey other && this.Equals(other);
			public override int GetHashCode() => (this.DesktopId.GetHashCode() * 397) ^ (int)this.Property;
		}

		private sealed class Diff
		{
			internal Diff(DesktopStateChangeKind kind, IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed, IReadOnlyList<DesktopMove> moves, IReadOnlyList<DesktopPropertyChange> names, IReadOnlyList<DesktopPropertyChange> wallpapers, IReadOnlyList<DesktopPositionChange> positions, Guid? oldCurrent, Guid? newCurrent)
			{
				this.Kind = kind; this.Added = added; this.Removed = removed; this.Moves = moves; this.Names = names; this.Wallpapers = wallpapers; this.Positions = positions; this.OldCurrent = oldCurrent; this.NewCurrent = newCurrent;
			}
			internal DesktopStateChangeKind Kind { get; }
			internal IReadOnlyList<Guid> Added { get; }
			internal IReadOnlyList<Guid> Removed { get; }
			internal IReadOnlyList<DesktopMove> Moves { get; }
			internal IReadOnlyList<DesktopPropertyChange> Names { get; }
			internal IReadOnlyList<DesktopPropertyChange> Wallpapers { get; }
			internal IReadOnlyList<DesktopPositionChange> Positions { get; }
			internal Guid? OldCurrent { get; }
			internal Guid? NewCurrent { get; }
			internal bool HasChanges => this.Added.Count != 0 || this.Removed.Count != 0 || this.Moves.Count != 0 || this.Names.Count != 0 || this.Wallpapers.Count != 0 || this.Positions.Count != 0 || this.OldCurrent != this.NewCurrent || this.Kind == DesktopStateChangeKind.Reset;
			internal DesktopStateChanged ToStateChanged(DesktopStateChangeKind kind, DesktopRuntimeState state) => new DesktopStateChanged(kind, state, this.Added, this.Removed, this.Moves, this.Names, this.Wallpapers, this.Positions, this.OldCurrent);
			internal static Diff Initial(IReadOnlyList<Guid> order) => new Diff(DesktopStateChangeKind.Initialized, order.ToList(), Array.Empty<Guid>(), Array.Empty<DesktopMove>(), Array.Empty<DesktopPropertyChange>(), Array.Empty<DesktopPropertyChange>(), Array.Empty<DesktopPositionChange>(), null, null);
		}
	}
}
