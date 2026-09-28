using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.UI.Bindings;

namespace SylphyHorn.Services
{
	internal sealed class PortableDesktopRecord
	{
		public string CName { get; set; }
		public string Title { get; set; }
		public int Position { get; set; }
		public string WallpaperEntry { get; set; }
		public string WallpaperOriginalPath { get; set; }
		public byte WallpaperPosition { get; set; }
	}
	internal sealed class PortableSettingRecord
	{
		public string Key { get; set; }
		public string Type { get; set; }
		public JsonElement Value { get; set; }
	}
	internal sealed class PortablePackageMetadata
	{
		public int FormatVersion { get; set; } = 1;
		public string Application { get; set; } = "SylphyHornPlusCon";
		public string ApplicationVersion { get; set; }
		public string CreatedUtc { get; set; }
		public string SourceMachine { get; set; }
		public string SourceWindows { get; set; }
		public bool IncludesDesktops { get; set; }
		public bool IncludesWallpapers { get; set; }
		public bool IncludesWebSocket { get; set; }
		public bool IncludesGeneral { get; set; }
		public string SettingsFile { get; set; } = "settings.json";
	}
	internal sealed class PortableExportOptions
	{
		public bool Desktops { get; set; }
		public bool Wallpapers { get; set; }
		public bool WebSocket { get; set; }
		public bool General { get; set; }
		public HashSet<string> DesktopCNames { get; } = new(StringComparer.OrdinalIgnoreCase);
	}
	internal sealed class PortableImportOptions
	{
		public bool Desktops { get; set; }
		public bool Wallpapers { get; set; }
		public bool WebSocket { get; set; }
		public bool General { get; set; }
		public HashSet<string> DesktopCNames { get; } = new(StringComparer.OrdinalIgnoreCase);
	}
	internal sealed class PortablePackageContent : IDisposable
	{
		public string TemporaryDirectory { get; init; }
		public PortablePackageMetadata Manifest { get; init; }
		public List<PortableDesktopRecord> Desktops { get; init; } = new();
		public List<PortableSettingRecord> Settings { get; init; } = new();
		public void Dispose() { try { if (!string.IsNullOrWhiteSpace(this.TemporaryDirectory) && Directory.Exists(this.TemporaryDirectory)) Directory.Delete(this.TemporaryDirectory, true); } catch { } }
	}

	internal static class PortableSettingsPackageService
	{
		internal const string Filter = "SylphyHornPlusCon package (*.shpc)|*.shpc";
		private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

		internal static async Task ExportAsync(string targetPath, PortableExportOptions options, IReadOnlyDictionary<string, object> settings, IReadOnlyList<VirtualDesktopViewModel> desktops)
		{
			if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("A target path is required.", nameof(targetPath));
			if (options == null) throw new ArgumentNullException(nameof(options));
			if (settings == null) throw new ArgumentNullException(nameof(settings));
			if (!options.Desktops && !options.WebSocket && !options.General) throw new InvalidDataException("Select at least one section to export.");
			var directory = Path.GetDirectoryName(targetPath);
			if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("The export directory is unavailable.");
			Directory.CreateDirectory(directory);
			var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				var package = BuildPackage(options, settings, desktops);
				using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
				using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
				{
					if (options.Desktops && options.Wallpapers)
					{
						foreach (var desktop in package.Desktops)
						{
							var source = desktops.First(item => string.Equals(item.CanonicalName, desktop.CName, StringComparison.OrdinalIgnoreCase)).WallpaperPath;
							if (string.IsNullOrWhiteSpace(source)) continue;
							if (!File.Exists(source)) throw new FileNotFoundException($"Wallpaper for desktop '{desktop.CName}' does not exist.", source);
							var extension = Path.GetExtension(source);
							var entryName = $"wallpapers/{SanitizeFileName(desktop.CName)}{extension}";
							var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
							using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
							using (var output = entry.Open()) await input.CopyToAsync(output).ConfigureAwait(false);
							desktop.WallpaperEntry = entryName;
						}
					}
					var manifestEntry = archive.CreateEntry("package.json", CompressionLevel.Optimal);
					using (var writer = new StreamWriter(manifestEntry.Open()))
						await writer.WriteAsync(JsonSerializer.Serialize(package.Metadata, JsonOptions)).ConfigureAwait(false);
					var settingsEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
					using (var writer = new StreamWriter(settingsEntry.Open()))
						await writer.WriteAsync(JsonSerializer.Serialize(new PortablePackageSettings { Desktops = package.Desktops, Settings = package.Settings }, JsonOptions)).ConfigureAwait(false);
				}
				// Re-open with the same validator used by import. Export is successful only when its result is importable.
				using (var verified = await OpenAndValidateAsync(tempPath).ConfigureAwait(false)) { }
				if (File.Exists(targetPath)) File.Replace(tempPath, targetPath, null, true); else File.Move(tempPath, targetPath);
			}
			finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
		}

		internal static async Task<PortablePackageContent> OpenAndValidateAsync(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("The SHPC package was not found.", path);
			var temp = Path.Combine(Path.GetTempPath(), "SHPC-import-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temp);
			try
			{
				PortablePackageMetadata metadata;
				List<PortableDesktopRecord> desktops;
				List<PortableSettingRecord> settings;
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
				{
					var entry = archive.GetEntry("package.json") ?? throw new InvalidDataException("package.json is missing.");
					using (var reader = new StreamReader(entry.Open()))
						metadata = JsonSerializer.Deserialize<PortablePackageMetadata>(await reader.ReadToEndAsync().ConfigureAwait(false), JsonOptions)
							?? throw new InvalidDataException("package.json is invalid.");
					var settingsEntry = archive.GetEntry(metadata.SettingsFile ?? "settings.json") ?? throw new InvalidDataException("settings.json is missing.");
					using (var reader = new StreamReader(settingsEntry.Open()))
					{
						var payload = JsonSerializer.Deserialize<PortablePackageSettings>(await reader.ReadToEndAsync().ConfigureAwait(false), JsonOptions)
							?? throw new InvalidDataException("settings.json is invalid.");
						desktops = payload.Desktops ?? new();
						settings = payload.Settings ?? new();
					}
					ValidatePackage(metadata, desktops, settings, archive);
					foreach (var wallpaper in desktops.Where(item => !string.IsNullOrWhiteSpace(item.WallpaperEntry)))
					{
						var source = archive.GetEntry(wallpaper.WallpaperEntry) ?? throw new InvalidDataException($"Wallpaper '{wallpaper.WallpaperEntry}' is missing.");
						var target = Path.Combine(temp, Path.GetFileName(wallpaper.WallpaperEntry));
						using var input = source.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
						await input.CopyToAsync(output).ConfigureAwait(false);
						if (new FileInfo(target).Length == 0) throw new InvalidDataException($"Wallpaper '{wallpaper.WallpaperEntry}' is empty.");
						wallpaper.WallpaperEntry = target;
					}
				}
				return new PortablePackageContent { TemporaryDirectory = temp, Manifest = metadata, Desktops = desktops, Settings = settings };
			}
			catch { try { Directory.Delete(temp, true); } catch { } throw; }
		}

		internal static void ApplySelectedSettings(IDictionary<string, object> target, IReadOnlyList<PortableSettingRecord> settings, PortableImportOptions options)
		{
			foreach (var record in settings)
			{
				var webSocket = IsWebSocketKey(record.Key);
				var general = IsGeneralKey(record.Key);
				if ((webSocket && !options.WebSocket) || (general && !options.General) || (!webSocket && !general)) continue;
				if (!target.TryGetValue(record.Key, out var current) || current == null) throw new InvalidDataException($"Setting '{record.Key}' is not supported by this SHPC version.");
				if (record.Key.EndsWith(".WebSocketApiKeyProtected", StringComparison.Ordinal))
				{
					var plain = record.Value.ValueKind == JsonValueKind.String ? record.Value.GetString() : string.Empty;
					target[record.Key] = WebSocketConnectionService.ProtectApiKey(plain);
					continue;
				}
				var type = current.GetType();
				var value = JsonSerializer.Deserialize(record.Value.GetRawText(), type, JsonOptions);
				if (value == null && type.IsValueType) throw new InvalidDataException($"Setting '{record.Key}' has an invalid value.");
				target[record.Key] = value;
			}
		}

		internal static string CreateAutomaticBackupPath()
		{
			var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Company, ProductInfo.Product, "Backups");
			Directory.CreateDirectory(root);
			return Path.Combine(root, $"SHPC-backup-{DateTime.Now:yyyyMMdd-HHmmss}.shpc");
		}

		private sealed class PortablePackageSettings
		{
			public List<PortableDesktopRecord> Desktops { get; set; } = new();
			public List<PortableSettingRecord> Settings { get; set; } = new();
		}
		private sealed class PortablePackageBuild
		{
			public PortablePackageMetadata Metadata { get; init; }
			public List<PortableDesktopRecord> Desktops { get; init; }
			public List<PortableSettingRecord> Settings { get; init; }
		}

		private static PortablePackageBuild BuildPackage(PortableExportOptions options, IReadOnlyDictionary<string, object> settings, IReadOnlyList<VirtualDesktopViewModel> desktops)
		{
			var metadata = new PortablePackageMetadata
			{
				ApplicationVersion = ProductInfo.VersionString, CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
				SourceMachine = Environment.MachineName, SourceWindows = Environment.OSVersion.VersionString,
				IncludesDesktops = options.Desktops, IncludesWallpapers = options.Desktops && options.Wallpapers,
				IncludesWebSocket = options.WebSocket, IncludesGeneral = options.General,
			};
			var desktopRecords = new List<PortableDesktopRecord>();
			if (options.Desktops)
			{
				foreach (var desktop in desktops.Where(item => options.DesktopCNames.Contains(item.CanonicalName)).OrderBy(item => item.Index))
					desktopRecords.Add(new PortableDesktopRecord { CName = desktop.CanonicalName, Title = desktop.StoredTitle, Position = desktop.Index + 1, WallpaperOriginalPath = desktop.WallpaperPath, WallpaperPosition = (byte)desktop.WallpaperPosition });
				if (desktopRecords.Count == 0) throw new InvalidDataException("Select at least one desktop to export.");
			}
			var settingRecords = new List<PortableSettingRecord>();
			foreach (var pair in settings.OrderBy(item => item.Key, StringComparer.Ordinal))
			{
				var webSocket = options.WebSocket && IsWebSocketKey(pair.Key);
				var general = options.General && IsGeneralKey(pair.Key);
				if (!webSocket && !general) continue;
				object value = pair.Value;
				if (pair.Key.EndsWith(".WebSocketApiKeyProtected", StringComparison.Ordinal)) value = WebSocketConnectionService.UnprotectApiKey(value as string);
				if (value == null) continue;
				settingRecords.Add(new PortableSettingRecord { Key = pair.Key, Type = value.GetType().FullName, Value = JsonSerializer.SerializeToElement(value, value.GetType(), JsonOptions) });
			}
			return new PortablePackageBuild { Metadata = metadata, Desktops = desktopRecords, Settings = settingRecords };
		}

		private static bool IsWebSocketKey(string key) => key.StartsWith("GeneralSettings.WebSocket", StringComparison.Ordinal);
		private static bool IsGeneralKey(string key) => key.StartsWith("GeneralSettings.", StringComparison.Ordinal) && !IsWebSocketKey(key) && !IsDesktopKey(key);
		private static bool IsDesktopKey(string key) => key.StartsWith(SettingsService.DesktopNamesKey, StringComparison.Ordinal) || key.StartsWith(SettingsService.DesktopWallpaperPathsKey, StringComparison.Ordinal) || key.StartsWith(SettingsService.DesktopPositionsKey, StringComparison.Ordinal) || key.StartsWith("GeneralSettings.DesktopCanonicalNames", StringComparison.Ordinal);
		private static string SanitizeFileName(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return string.IsNullOrWhiteSpace(value) ? "desktop" : value; }
		private static void ValidatePackage(PortablePackageMetadata manifest, IReadOnlyList<PortableDesktopRecord> desktops, IReadOnlyList<PortableSettingRecord> settings, ZipArchive archive)
		{
			if (manifest.FormatVersion != 1) throw new InvalidDataException($"Unsupported SHPC package format {manifest.FormatVersion}.");
			if (!string.Equals(manifest.Application, "SylphyHornPlusCon", StringComparison.Ordinal)) throw new InvalidDataException("This package was not created by SylphyHornPlusCon.");
			if (!manifest.IncludesDesktops && !manifest.IncludesWebSocket && !manifest.IncludesGeneral) throw new InvalidDataException("The package contains no importable sections.");
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var desktop in package.Desktops)
			{
				if (string.IsNullOrWhiteSpace(desktop.CName) || !names.Add(desktop.CName)) throw new InvalidDataException("Desktop CNames must be present and unique.");
				if (desktop.Position < 1) throw new InvalidDataException($"Desktop '{desktop.CName}' has an invalid position.");
				if (!string.IsNullOrWhiteSpace(desktop.WallpaperEntry) && archive.GetEntry(desktop.WallpaperEntry) == null) throw new InvalidDataException($"Wallpaper '{desktop.WallpaperEntry}' is missing.");
			}
			if (manifest.IncludesDesktops && package.Desktops.Count == 0) throw new InvalidDataException("The package declares desktops but contains none.");
			if (settings.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new InvalidDataException("The package contains duplicate settings.");
		}
	}
}