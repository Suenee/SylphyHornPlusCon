using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WindowsDesktop;

namespace SylphyHorn.Services.DesktopTransitions
{
	internal enum DesktopRecordOrigin
	{
		SeededExistingRecord,
		TrulyNewRecord,
	}

	internal enum DesktopPropertyAuthority
	{
		Unknown,
		PersistedLastKnownGood,
		ProviderStableValue,
		LocalWrite,
		ApplicationAuthoritative,
	}

	internal enum DesktopPropertyKind
	{
		Name,
		WallpaperPath,
	}

	internal enum DesktopStateChangeKind
	{
		Initialized,
		Reconciled,
		LocalEdit,
		CurrentChanged,
		Reset,
	}

	internal enum DesktopReconciliationReason
	{
		None,
		InvalidStableBatch,
		StaleStableBatch,
		SeedEmptyConfirmation,
		PropertyReadIncomplete,
		CurrentReadIncomplete,
		InvalidCurrentTransition,
		InvalidLocalEdit,
	}

	internal sealed class DesktopPropertyState
	{
		internal DesktopPropertyState(bool hasValue, string value, VirtualDesktopReadStatus readStatus, DesktopPropertyAuthority authority, bool isConfirmed)
		{
			if (hasValue && value == null) throw new ArgumentNullException(nameof(value));
			if (!hasValue && value != null) throw new ArgumentException("An unknown property cannot carry a value.", nameof(value));
			if (!IsReadStatus(readStatus)) throw new ArgumentOutOfRangeException(nameof(readStatus));
			if (!IsAuthority(authority)) throw new ArgumentOutOfRangeException(nameof(authority));
			if (!hasValue && readStatus == VirtualDesktopReadStatus.Success) throw new ArgumentException("A successful property must carry a value.", nameof(readStatus));
			if (!hasValue && isConfirmed) throw new ArgumentException("An unknown property cannot be confirmed.", nameof(isConfirmed));
			if (authority == DesktopPropertyAuthority.ProviderStableValue && (!hasValue || !isConfirmed))
				throw new ArgumentException("Provider stable values must be successful, present, and confirmed.", nameof(authority));
			if (authority == DesktopPropertyAuthority.LocalWrite && (readStatus == VirtualDesktopReadStatus.Success || isConfirmed))
				throw new ArgumentException("Local writes remain unconfirmed until a provider stable value is accepted.", nameof(authority));
			if (authority == DesktopPropertyAuthority.PersistedLastKnownGood && (!hasValue || isConfirmed))
				throw new ArgumentException("Persisted last-known-good values must be present and unconfirmed.", nameof(authority));
			if (authority == DesktopPropertyAuthority.Unknown && (hasValue || isConfirmed))
				throw new ArgumentException("Unknown authority cannot carry a value or confirmation.", nameof(authority));
			if (authority == DesktopPropertyAuthority.ApplicationAuthoritative && readStatus == VirtualDesktopReadStatus.Success)
				throw new ArgumentException("Supported provider values cannot be application-authoritative.", nameof(authority));

			this.HasValue = hasValue;
			this.Value = value;
			this.ReadStatus = readStatus;
			this.Authority = authority;
			this.IsConfirmed = isConfirmed;
		}

		internal bool HasValue { get; }
		internal string Value { get; }
		internal VirtualDesktopReadStatus ReadStatus { get; }
		internal DesktopPropertyAuthority Authority { get; }
		internal bool IsConfirmed { get; }

		internal static DesktopPropertyState Unknown(VirtualDesktopReadStatus status, DesktopPropertyAuthority authority = DesktopPropertyAuthority.Unknown)
			=> new DesktopPropertyState(false, null, status, authority, false);

		internal static DesktopPropertyState Persisted(string value)
			=> value == null
				? Unknown(VirtualDesktopReadStatus.NotAttempted)
				: new DesktopPropertyState(true, value, VirtualDesktopReadStatus.NotAttempted, DesktopPropertyAuthority.PersistedLastKnownGood, false);

		internal static DesktopPropertyState Provider(string value)
			=> new DesktopPropertyState(true, value ?? throw new ArgumentNullException(nameof(value)), VirtualDesktopReadStatus.Success, DesktopPropertyAuthority.ProviderStableValue, true);

		internal DesktopPropertyState Preserve(VirtualDesktopReadStatus status)
			=> new DesktopPropertyState(this.HasValue, this.Value, status, this.Authority, this.IsConfirmed);

		private static bool IsReadStatus(VirtualDesktopReadStatus status)
			=> status == VirtualDesktopReadStatus.Success || status == VirtualDesktopReadStatus.Unsupported || status == VirtualDesktopReadStatus.Failed || status == VirtualDesktopReadStatus.NotAttempted;

		private static bool IsAuthority(DesktopPropertyAuthority authority)
			=> authority == DesktopPropertyAuthority.Unknown || authority == DesktopPropertyAuthority.PersistedLastKnownGood || authority == DesktopPropertyAuthority.ProviderStableValue || authority == DesktopPropertyAuthority.LocalWrite || authority == DesktopPropertyAuthority.ApplicationAuthoritative;
	}

	internal sealed class DesktopRecord
	{
		internal DesktopRecord(Guid id, DesktopPropertyState name, DesktopPropertyState wallpaperPath, WallpaperPosition wallpaperPosition, DesktopRecordOrigin origin)
		{
			if (id == Guid.Empty) throw new ArgumentException("A desktop ID cannot be empty.", nameof(id));
			if (origin != DesktopRecordOrigin.SeededExistingRecord && origin != DesktopRecordOrigin.TrulyNewRecord) throw new ArgumentOutOfRangeException(nameof(origin));
			if (!Enum.IsDefined(typeof(WallpaperPosition), wallpaperPosition)) throw new ArgumentOutOfRangeException(nameof(wallpaperPosition));
			this.Id = id;
			this.Name = name ?? throw new ArgumentNullException(nameof(name));
			this.WallpaperPath = wallpaperPath ?? throw new ArgumentNullException(nameof(wallpaperPath));
			this.WallpaperPosition = wallpaperPosition;
			this.Origin = origin;
		}

		internal Guid Id { get; }
		internal DesktopPropertyState Name { get; }
		internal DesktopPropertyState WallpaperPath { get; }
		internal WallpaperPosition WallpaperPosition { get; }
		internal DesktopRecordOrigin Origin { get; }

		internal DesktopRecord With(DesktopPropertyState name = null, DesktopPropertyState wallpaperPath = null, WallpaperPosition? wallpaperPosition = null)
			=> new DesktopRecord(this.Id, name ?? this.Name, wallpaperPath ?? this.WallpaperPath, wallpaperPosition ?? this.WallpaperPosition, this.Origin);
	}

	internal sealed class DesktopRuntimeState
	{
		internal DesktopRuntimeState(long stateRevision, long providerEpoch, long providerSnapshotRevision, Guid? currentDesktopId, IEnumerable<Guid> order, IDictionary<Guid, DesktopRecord> records)
		{
			if (stateRevision <= 0) throw new ArgumentOutOfRangeException(nameof(stateRevision));
			if (providerEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(providerEpoch));
			if (providerSnapshotRevision <= 0) throw new ArgumentOutOfRangeException(nameof(providerSnapshotRevision));

			var orderCopy = new List<Guid>(order ?? throw new ArgumentNullException(nameof(order)));
			var recordCopy = new Dictionary<Guid, DesktopRecord>(records ?? throw new ArgumentNullException(nameof(records)));
			if (orderCopy.Any(id => id == Guid.Empty)) throw new ArgumentException("Order cannot contain an empty ID.", nameof(order));
			if (orderCopy.Distinct().Count() != orderCopy.Count) throw new ArgumentException("Order cannot contain duplicate IDs.", nameof(order));
			if (recordCopy.Keys.Any(id => id == Guid.Empty)) throw new ArgumentException("Records cannot contain an empty ID.", nameof(records));
			if (recordCopy.Count != orderCopy.Count || orderCopy.Any(id => !recordCopy.TryGetValue(id, out var record) || record == null || record.Id != id))
				throw new ArgumentException("Order and records must describe the same active desktops.", nameof(records));
			if (currentDesktopId.HasValue && !recordCopy.ContainsKey(currentDesktopId.Value)) throw new ArgumentException("CurrentDesktopId must be present in Order.", nameof(currentDesktopId));

			this.StateRevision = stateRevision;
			this.ProviderEpoch = providerEpoch;
			this.ProviderSnapshotRevision = providerSnapshotRevision;
			this.CurrentDesktopId = currentDesktopId;
			this.Order = new ReadOnlyCollection<Guid>(orderCopy);
			this.Records = new ReadOnlyDictionary<Guid, DesktopRecord>(recordCopy);
		}

		internal long StateRevision { get; }
		internal long ProviderEpoch { get; }
		internal long ProviderSnapshotRevision { get; }
		internal Guid? CurrentDesktopId { get; }
		internal IReadOnlyList<Guid> Order { get; }
		internal IReadOnlyDictionary<Guid, DesktopRecord> Records { get; }
	}

	internal sealed class DesktopStartupSeed
	{
		internal DesktopStartupSeed(IEnumerable<string> names, IEnumerable<string> wallpaperPaths, IEnumerable<WallpaperPosition> positions)
		{
			this.Names = new ReadOnlyCollection<string>(new List<string>(names ?? Array.Empty<string>()));
			this.WallpaperPaths = new ReadOnlyCollection<string>(new List<string>(wallpaperPaths ?? Array.Empty<string>()));
			this.Positions = new ReadOnlyCollection<WallpaperPosition>(new List<WallpaperPosition>(positions ?? Array.Empty<WallpaperPosition>()));
		}

		internal IReadOnlyList<string> Names { get; }
		internal IReadOnlyList<string> WallpaperPaths { get; }
		internal IReadOnlyList<WallpaperPosition> Positions { get; }
		internal static DesktopStartupSeed Empty { get; } = new DesktopStartupSeed(null, null, null);
	}

	internal sealed class DesktopSettingsProjection
	{
		internal DesktopSettingsProjection(IEnumerable<string> names, IEnumerable<string> wallpaperPaths, IEnumerable<WallpaperPosition> positions)
		{
			this.Names = new ReadOnlyCollection<string>(new List<string>(names));
			this.WallpaperPaths = new ReadOnlyCollection<string>(new List<string>(wallpaperPaths));
			this.Positions = new ReadOnlyCollection<WallpaperPosition>(new List<WallpaperPosition>(positions));
			if (this.Names.Count != this.WallpaperPaths.Count || this.Names.Count != this.Positions.Count)
				throw new ArgumentException("All projection lists must have the same count.");
		}

		internal IReadOnlyList<string> Names { get; }
		internal IReadOnlyList<string> WallpaperPaths { get; }
		internal IReadOnlyList<WallpaperPosition> Positions { get; }
	}

	internal sealed class DesktopMove
	{
		internal DesktopMove(Guid id, int oldIndex, int newIndex) { this.Id = id; this.OldIndex = oldIndex; this.NewIndex = newIndex; }
		internal Guid Id { get; }
		internal int OldIndex { get; }
		internal int NewIndex { get; }
	}

	internal sealed class DesktopPropertyChange
	{
		internal DesktopPropertyChange(Guid id, DesktopPropertyState oldValue, DesktopPropertyState newValue) { this.Id = id; this.OldValue = oldValue; this.NewValue = newValue; }
		internal Guid Id { get; }
		internal DesktopPropertyState OldValue { get; }
		internal DesktopPropertyState NewValue { get; }
	}

	internal sealed class DesktopPositionChange
	{
		internal DesktopPositionChange(Guid id, WallpaperPosition oldValue, WallpaperPosition newValue) { this.Id = id; this.OldValue = oldValue; this.NewValue = newValue; }
		internal Guid Id { get; }
		internal WallpaperPosition OldValue { get; }
		internal WallpaperPosition NewValue { get; }
	}

	internal sealed class DesktopStateChanged
	{
		internal DesktopStateChanged(DesktopStateChangeKind kind, DesktopRuntimeState snapshot, IEnumerable<Guid> addedIds, IEnumerable<Guid> removedIds, IEnumerable<DesktopMove> moves, IEnumerable<DesktopPropertyChange> nameChanges, IEnumerable<DesktopPropertyChange> wallpaperChanges, IEnumerable<DesktopPositionChange> positionChanges, Guid? oldCurrentDesktopId)
		{
			if (!Enum.IsDefined(typeof(DesktopStateChangeKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
			this.Kind = kind;
			this.Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
			this.StateRevision = snapshot.StateRevision;
			this.ProviderEpoch = snapshot.ProviderEpoch;
			this.ProviderSnapshotRevision = snapshot.ProviderSnapshotRevision;
			this.CurrentDesktopId = snapshot.CurrentDesktopId;
			this.OldCurrentDesktopId = oldCurrentDesktopId;
			this.AddedIds = Copy(addedIds);
			this.RemovedIds = Copy(removedIds);
			this.Moves = Copy(moves);
			this.NameChanges = Copy(nameChanges);
			this.WallpaperChanges = Copy(wallpaperChanges);
			this.PositionChanges = Copy(positionChanges);
		}

		internal long StateRevision { get; }
		internal long ProviderEpoch { get; }
		internal long ProviderSnapshotRevision { get; }
		internal Guid? CurrentDesktopId { get; }
		internal Guid? OldCurrentDesktopId { get; }
		internal DesktopStateChangeKind Kind { get; }
		internal DesktopRuntimeState Snapshot { get; }
		internal IReadOnlyList<Guid> AddedIds { get; }
		internal IReadOnlyList<Guid> RemovedIds { get; }
		internal IReadOnlyList<DesktopMove> Moves { get; }
		internal IReadOnlyList<DesktopPropertyChange> NameChanges { get; }
		internal IReadOnlyList<DesktopPropertyChange> WallpaperChanges { get; }
		internal IReadOnlyList<DesktopPositionChange> PositionChanges { get; }
		internal bool CurrentChanged => this.OldCurrentDesktopId != this.CurrentDesktopId;

		private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) => new ReadOnlyCollection<T>(new List<T>(values ?? Array.Empty<T>()));
	}

	internal sealed class DesktopCoordinatorTransition
	{
		private DesktopCoordinatorTransition(bool accepted, DesktopRuntimeState newState, DesktopSettingsProjection projection, DesktopStateChanged stateChanged, bool requiresSave, bool requiresReconciliation, DesktopReconciliationReason reconciliationReason)
		{
			this.Accepted = accepted;
			this.NewState = newState;
			this.Projection = projection;
			this.StateChanged = stateChanged;
			this.RequiresSave = requiresSave;
			this.RequiresReconciliation = requiresReconciliation;
			this.ReconciliationReason = reconciliationReason;
		}

		internal bool Accepted { get; }
		internal DesktopRuntimeState NewState { get; }
		internal DesktopSettingsProjection Projection { get; }
		internal DesktopStateChanged StateChanged { get; }
		internal bool RequiresSave { get; }
		internal bool RequiresReconciliation { get; }
		internal DesktopReconciliationReason ReconciliationReason { get; }

		internal static DesktopCoordinatorTransition AcceptedChange(DesktopRuntimeState newState, DesktopSettingsProjection projection, DesktopStateChanged stateChanged, bool requiresSave, bool requiresReconciliation, DesktopReconciliationReason reconciliationReason)
		{
			if (newState == null) throw new ArgumentNullException(nameof(newState));
			if (requiresSave && projection == null) throw new ArgumentException("A save request requires a complete projection.", nameof(projection));
			if (stateChanged != null && !ReferenceEquals(stateChanged.Snapshot, newState)) throw new ArgumentException("StateChanged must describe NewState.", nameof(stateChanged));
			ValidateReconciliation(requiresReconciliation, reconciliationReason);
			return new DesktopCoordinatorTransition(true, newState, projection, stateChanged, requiresSave, requiresReconciliation, reconciliationReason);
		}

		internal static DesktopCoordinatorTransition AcceptedNoOp(DesktopRuntimeState currentState)
			=> AcceptedChange(currentState ?? throw new ArgumentNullException(nameof(currentState)), null, null, false, false, DesktopReconciliationReason.None);

		internal static DesktopCoordinatorTransition Rejected(DesktopRuntimeState currentState, bool requiresReconciliation, DesktopReconciliationReason reconciliationReason)
		{
			ValidateReconciliation(requiresReconciliation, reconciliationReason);
			return new DesktopCoordinatorTransition(false, currentState, null, null, false, requiresReconciliation, reconciliationReason);
		}

		private static void ValidateReconciliation(bool requiresReconciliation, DesktopReconciliationReason reconciliationReason)
		{
			if (!Enum.IsDefined(typeof(DesktopReconciliationReason), reconciliationReason)) throw new ArgumentOutOfRangeException(nameof(reconciliationReason));
			if (requiresReconciliation != (reconciliationReason != DesktopReconciliationReason.None))
				throw new ArgumentException("Reconciliation intent and reason must be specified together.", nameof(reconciliationReason));
		}
	}

	internal sealed class DesktopLocalEdit
	{
		private DesktopLocalEdit(Guid desktopId, DesktopPropertyKind? property, string value, WallpaperPosition? position, long providerEpoch, long providerSnapshotRevision, long stateRevision)
		{
			this.DesktopId = desktopId;
			this.Property = property;
			this.Value = value;
			this.Position = position;
			this.ProviderEpoch = providerEpoch;
			this.ProviderSnapshotRevision = providerSnapshotRevision;
			this.StateRevision = stateRevision;
		}

		internal Guid DesktopId { get; }
		internal DesktopPropertyKind? Property { get; }
		internal string Value { get; }
		internal WallpaperPosition? Position { get; }
		internal long ProviderEpoch { get; }
		internal long ProviderSnapshotRevision { get; }
		internal long StateRevision { get; }

		internal static DesktopLocalEdit Name(Guid id, string value, DesktopRuntimeState state) => new DesktopLocalEdit(id, DesktopPropertyKind.Name, value, null, state.ProviderEpoch, state.ProviderSnapshotRevision, state.StateRevision);
		internal static DesktopLocalEdit WallpaperPath(Guid id, string value, DesktopRuntimeState state) => new DesktopLocalEdit(id, DesktopPropertyKind.WallpaperPath, value, null, state.ProviderEpoch, state.ProviderSnapshotRevision, state.StateRevision);
		internal static DesktopLocalEdit WallpaperPosition(Guid id, WallpaperPosition value, DesktopRuntimeState state) => new DesktopLocalEdit(id, null, null, value, state.ProviderEpoch, state.ProviderSnapshotRevision, state.StateRevision);
	}


	internal sealed class SeedEmptyCandidate
	{
		internal SeedEmptyCandidate(long providerEpoch, long firstStableSnapshotRevision, Guid desktopId, DesktopPropertyKind property)
		{
			this.ProviderEpoch = providerEpoch;
			this.FirstStableSnapshotRevision = firstStableSnapshotRevision;
			this.DesktopId = desktopId;
			this.Property = property;
		}

		internal long ProviderEpoch { get; }
		internal long FirstStableSnapshotRevision { get; }
		internal Guid DesktopId { get; }
		internal DesktopPropertyKind Property { get; }
	}
}
