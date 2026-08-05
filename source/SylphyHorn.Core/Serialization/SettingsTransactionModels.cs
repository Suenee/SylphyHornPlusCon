using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MetroTrilithon.Serialization;

namespace SylphyHorn.Serialization
{
	public enum SettingsSaveErrorCategory
	{
		None,
		Serialization,
		Io,
		Unknown,
	}

	public sealed class SettingsSaveResult
	{
		private SettingsSaveResult(bool succeeded, long saveRevision, long settingsRevision, SettingsSaveErrorCategory errorCategory, string exceptionType)
		{
			this.Succeeded = succeeded;
			this.SaveRevision = saveRevision;
			this.SettingsRevision = settingsRevision;
			this.ErrorCategory = errorCategory;
			this.ExceptionType = exceptionType;
		}

		public bool Succeeded { get; }
		public long SaveRevision { get; }
		public long SettingsRevision { get; }
		public SettingsSaveErrorCategory ErrorCategory { get; }
		public string ExceptionType { get; }

		internal static SettingsSaveResult Success(long saveRevision, long settingsRevision)
			=> new SettingsSaveResult(true, saveRevision, settingsRevision, SettingsSaveErrorCategory.None, null);

		internal static SettingsSaveResult Failure(long saveRevision, long settingsRevision, SettingsSaveErrorCategory category, Type exceptionType)
			=> new SettingsSaveResult(false, saveRevision, settingsRevision, category, exceptionType?.FullName);
	}

	public sealed class StagedSettingsImport
	{
		private readonly object _ownerToken;
		private readonly ReadOnlyDictionary<string, object> _settings;

		internal StagedSettingsImport(object ownerToken, IDictionary<string, object> settings, long settingsRevision, string activeFingerprint, string diskHash)
		{
			this._ownerToken = ownerToken ?? throw new ArgumentNullException(nameof(ownerToken));
			this.StageId = Guid.NewGuid();
			this._settings = new ReadOnlyDictionary<string, object>(SettingsDictionarySnapshot.CloneDictionary(settings));
			this.SettingsRevision = settingsRevision;
			this.ActiveFingerprint = activeFingerprint ?? throw new ArgumentNullException(nameof(activeFingerprint));
			this.DiskHash = diskHash;
		}

		public Guid StageId { get; }
		public long SettingsRevision { get; }
		public string ActiveFingerprint { get; }
		public string DiskHash { get; }
		public IReadOnlyDictionary<string, object> Settings => this._settings;
		public IDictionary<string, object> CreateCommitDictionary() => SettingsDictionarySnapshot.CloneDictionary(this._settings);
		internal bool IsOwnedBy(object ownerToken) => ReferenceEquals(this._ownerToken, ownerToken);
	}

	public enum SettingsImportCommitStatus
	{
		Publishing,
		Completed,
		CompletedWithFailures,
		FailedWithoutStableState,
		SupersededByReset,
		Conflict,
		InvalidStage,
		PublishFailed,
		Discarded,
		Cancelled,
		ShuttingDown,
	}

	public sealed class SettingsImportCommitResult
	{
		internal SettingsImportCommitResult(SettingsImportCommitStatus status, SettingsSaveResult saveResult)
		{
			this.Status = status;
			this.SaveResult = saveResult;
		}

		public SettingsImportCommitStatus Status { get; }
		public SettingsSaveResult SaveResult { get; }
		public bool Succeeded => this.Status == SettingsImportCommitStatus.Completed;
		public static SettingsImportCommitResult Failed() => new SettingsImportCommitResult(SettingsImportCommitStatus.PublishFailed, null);
		public static SettingsImportCommitResult Cancelled() => new SettingsImportCommitResult(SettingsImportCommitStatus.Cancelled, null);
		public static SettingsImportCommitResult ShuttingDown() => new SettingsImportCommitResult(SettingsImportCommitStatus.ShuttingDown, null);
		public static SettingsImportCommitResult CompletedWithFailures() => new SettingsImportCommitResult(SettingsImportCommitStatus.CompletedWithFailures, null);
		public static SettingsImportCommitResult FailedWithoutStableState() => new SettingsImportCommitResult(SettingsImportCommitStatus.FailedWithoutStableState, null);
		public static SettingsImportCommitResult SupersededByReset() => new SettingsImportCommitResult(SettingsImportCommitStatus.SupersededByReset, null);
	}

	internal enum SettingsImportTransactionPhase
	{
		None,
		Preparing,
		Prepared,
		Publishing,
	}
}
