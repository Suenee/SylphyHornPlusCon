using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using WindowsDesktop;

namespace SylphyHorn.UI.Bindings
{
	public class VirtualDesktopViewModel : ObservableObject
	{
		private static readonly object CanonicalNamesGate = new object();
		private readonly DesktopTransitionRuntime _runtime;
		private string _name;
		private string _canonicalName;
		private WallpaperViewModel _wallpaper;
		private bool _supportsWallpaperPath;

		internal VirtualDesktopViewModel(DesktopTransitionRuntime runtime, int index, DesktopRecord record)
		{
			this._runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			this.Id = record?.Id ?? throw new ArgumentNullException(nameof(record));
			this.Index = index;
			this._name = record.Name.HasValue ? record.Name.Value : null;
			this._canonicalName = ReadCanonicalName(this.Id);
			if (string.IsNullOrWhiteSpace(this._canonicalName))
			{
				this._canonicalName = CreateUniqueCanonicalName(this.Id, this._name, index);
				WriteCanonicalName(this.Id, this._canonicalName);
			}
			else
			{
				var normalized = CreateUniqueCanonicalName(this.Id, this._canonicalName, index);
				if (!string.Equals(normalized, this._canonicalName, StringComparison.Ordinal))
				{
					this._canonicalName = normalized;
					WriteCanonicalName(this.Id, normalized);
				}
			}
			this._supportsWallpaperPath = record.WallpaperPath.ReadStatus != VirtualDesktopReadStatus.Unsupported;
			this._wallpaper = new WallpaperViewModel(record.WallpaperPath.HasValue ? record.WallpaperPath.Value : null, record.WallpaperPosition,
				path => this._runtime.EditWallpaperPath(this.Id, path), position => this._runtime.EditWallpaperPosition(this.Id, position));
			this.CloseCommand = new RelayCommand(this.Close);
			this.MoveToPreviousCommand = new RelayCommand(this.MoveToPrevious);
			this.MoveToNextCommand = new RelayCommand(this.MoveToNext);
			this.MoveToFirstCommand = new RelayCommand(this.MoveToFirst);
			this.MoveToLastCommand = new RelayCommand(this.MoveToLast);
			this.SwitchCommand = new RelayCommand(this.Switch);
		}

		public RelayCommand CloseCommand { get; }
		public RelayCommand MoveToPreviousCommand { get; }
		public RelayCommand MoveToNextCommand { get; }
		public RelayCommand MoveToFirstCommand { get; }
		public RelayCommand MoveToLastCommand { get; }
		public RelayCommand SwitchCommand { get; }
		public Guid Id { get; }
		public int Index { get; private set; }
		public string NumberText => (this.Index + 1).ToString();
		public string Name { get => this._name; set { if (this._name != value) this._runtime.EditName(this.Id, value); } }
		public string Title { get => this.Name; set => this.Name = value; }
		public string CanonicalName
		{
			get => this._canonicalName;
			set
			{
				var canonical = CreateUniqueCanonicalName(this.Id, value, this.Index);
				if (string.Equals(this._canonicalName, canonical, StringComparison.Ordinal)) return;
				this._canonicalName = canonical;
				WriteCanonicalName(this.Id, canonical);
				this.OnPropertyChanged();
			}
		}
		public bool IsWallpaperEnabled => ProductInfo.IsWallpaperSupportBuild || Settings.General.ChangeBackgroundEachDesktop;
		public string WallpaperPath
		{
			get => this._wallpaper.FilePath;
			set
			{
				if (this._wallpaper.FilePath == value) return;
				if (this._supportsWallpaperPath && string.IsNullOrEmpty(value))
				{
					this.OnPropertyChanged(nameof(this.WallpaperPath));
					return;
				}
				this._wallpaper.FilePath = value;
			}
		}
		public string WallpaperPathOrDefault => this._wallpaper.FilePathOrDefault;
		public WallpaperPosition WallpaperPosition { get => this._wallpaper.Position; set => this._wallpaper.Position = value; }
		public WallpaperViewModel Wallpaper => this._wallpaper;
		public bool HasWallpaper => !string.IsNullOrEmpty(this.WallpaperPath);
		public bool HasNoWallpaper => string.IsNullOrEmpty(this.WallpaperPath);

		internal void Update(int index, DesktopRecord record)
		{
			if (record == null || record.Id != this.Id) throw new ArgumentException("A desktop view model can only be updated by its matching ID.", nameof(record));
			if (this.Index != index) { this.Index = index; this.OnPropertyChanged(nameof(this.Index)); this.OnPropertyChanged(nameof(this.NumberText)); }
			var name = record.Name.HasValue ? record.Name.Value : null;
			if (this._name != name) { this._name = name; this.OnPropertyChanged(nameof(this.Name)); }
			var wallpaperPath = record.WallpaperPath.HasValue ? record.WallpaperPath.Value : null;
			this._supportsWallpaperPath = record.WallpaperPath.ReadStatus != VirtualDesktopReadStatus.Unsupported;
			var wallpaperPathChanged = this._wallpaper.FilePath != wallpaperPath;
			var wallpaperPositionChanged = this._wallpaper.Position != record.WallpaperPosition;
			this._wallpaper.Update(wallpaperPath, record.WallpaperPosition);
			if (wallpaperPathChanged)
			{
				this.OnPropertyChanged(nameof(this.WallpaperPath));
				this.OnPropertyChanged(nameof(this.WallpaperPathOrDefault));
			}
			if (wallpaperPositionChanged) this.OnPropertyChanged(nameof(this.WallpaperPosition));
			if (wallpaperPathChanged)
			{
				this.OnPropertyChanged(nameof(this.HasWallpaper));
				this.OnPropertyChanged(nameof(this.HasNoWallpaper));
			}
		}

		public void ResetWallpaperPath(string path) => this._runtime.EditWallpaperPath(this.Id, path);
		public void MoveToPrevious() => this._runtime.MoveLeft(this.Id);
		public void MoveToNext() => this._runtime.MoveRight(this.Id);
		public void MoveToFirst() => this._runtime.MoveFirst(this.Id);
		public void MoveToLast() => this._runtime.MoveLast(this.Id);
		public void Switch() => this._runtime.Switch(this.Id);
		public void Close() => this._runtime.Remove(this.Id);
		public void ForgetCanonicalName()
		{
			lock (CanonicalNamesGate)
			{
				var names = ReadCanonicalNamesUnsafe();
				if (!names.Remove(this.Id.ToString("D"))) return;
				Settings.General.DesktopCanonicalNames.Value = JsonSerializer.Serialize(names);
			}
		}

		private static string CreateUniqueCanonicalName(Guid id, string value, int index)
		{
			var normalized = NormalizeCanonicalName(value);
			if (string.IsNullOrEmpty(normalized)) normalized = $"desktop-{index + 1}";
			lock (CanonicalNamesGate)
			{
				var names = ReadCanonicalNamesUnsafe();
				var candidate = normalized;
				var suffix = 2;
				while (ContainsCanonicalName(names, id, candidate)) candidate = $"{normalized}-{suffix++}";
				return candidate;
			}
		}

		private static string NormalizeCanonicalName(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return string.Empty;
			var source = value.Trim().ToLowerInvariant();
			var result = new StringBuilder(source.Length);
			var lastSeparator = false;
			foreach (var c in source)
			{
				if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') { result.Append(c); lastSeparator = false; }
				else if (c == '-' || char.IsWhiteSpace(c)) { if (result.Length > 0 && !lastSeparator) { result.Append('-'); lastSeparator = true; } }
				else if (result.Length > 0 && !lastSeparator) { result.Append('-'); lastSeparator = true; }
			}
			return result.ToString().Trim('-');
		}

		private static bool ContainsCanonicalName(Dictionary<string, string> names, Guid id, string candidate)
		{
			var ownKey = id.ToString("D");
			foreach (var pair in names)
				if (!string.Equals(pair.Key, ownKey, StringComparison.OrdinalIgnoreCase) && string.Equals(pair.Value, candidate, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		private static string ReadCanonicalName(Guid id)
		{
			lock (CanonicalNamesGate) { var names = ReadCanonicalNamesUnsafe(); return names.TryGetValue(id.ToString("D"), out var value) ? value : null; }
		}
		private static void WriteCanonicalName(Guid id, string value)
		{
			lock (CanonicalNamesGate) { var names = ReadCanonicalNamesUnsafe(); names[id.ToString("D")] = value; Settings.General.DesktopCanonicalNames.Value = JsonSerializer.Serialize(names); }
		}
		private static Dictionary<string, string> ReadCanonicalNamesUnsafe()
		{
			try
			{
				var json = Settings.General.DesktopCanonicalNames.Value;
				if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			}
			catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
		}
	}

	public class WallpaperViewModel : ObservableObject
	{
		private string _path;
		private WallpaperPosition _position;
		private readonly Action<string> _setPath;
		private readonly Action<WallpaperPosition> _setPosition;
		internal WallpaperViewModel(string path, WallpaperPosition position, Action<string> setPath, Action<WallpaperPosition> setPosition)
		{ this._path = path; this._position = position; this._setPath = setPath ?? throw new ArgumentNullException(nameof(setPath)); this._setPosition = setPosition ?? throw new ArgumentNullException(nameof(setPosition)); }
		public string FilePath { get => this._path; set { if (this._path != value) this._setPath(value); } }
		public string FilePathOrDefault => !string.IsNullOrEmpty(this._path) ? this._path : WallpaperService.GetCurrentColorAndWallpaper().Item2 ?? string.Empty;
		public WallpaperPosition Position { get => this._position; set { if (this._position != value) this._setPosition(value); } }
		public Color Color { get => WallpaperService.GetCurrentColorAndWallpaper().Item1; set => WallpaperService.SetBackgroundColor(value); }
		internal void Update(string path, WallpaperPosition position)
		{
			if (this._path != path) { this._path = path; this.OnPropertyChanged(nameof(this.FilePath)); this.OnPropertyChanged(nameof(this.FilePathOrDefault)); }
			if (this._position != position) { this._position = position; this.OnPropertyChanged(nameof(this.Position)); }
		}
	}
}
