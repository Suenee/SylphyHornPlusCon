using Livet;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using System;
using System.Linq;
using System.Windows.Media;

namespace SylphyHorn.UI.Bindings
{
	public class VirtualDesktopViewModel : ViewModel
	{
		private readonly DesktopTransitionRuntime _runtime;
		private string _name;
		private WallpaperViewModel _wallpaper;

		internal VirtualDesktopViewModel(DesktopTransitionRuntime runtime, int index, DesktopRecord record)
		{
			this._runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			this.Id = record?.Id ?? throw new ArgumentNullException(nameof(record));
			this.Index = index;
			this._name = record.Name.HasValue ? record.Name.Value : null;
			this._wallpaper = new WallpaperViewModel(
				record.WallpaperPath.HasValue ? record.WallpaperPath.Value : null,
				record.WallpaperPosition,
				path => this._runtime.EditWallpaperPath(this.Id, path),
				position => this._runtime.EditWallpaperPosition(this.Id, position));
		}

		public Guid Id { get; }
		public int Index { get; private set; }
		public string NumberText => (this.Index + 1).ToString();
		public string Name
		{
			get => this._name;
			set
			{
				if (this._name == value) return;
				this._runtime.EditName(this.Id, value);
			}
		}
		public bool IsWallpaperEnabled => ProductInfo.IsWallpaperSupportBuild || Settings.General.ChangeBackgroundEachDesktop;
		public string WallpaperPath { get => this._wallpaper.FilePath; set => this._wallpaper.FilePath = value; }
		public string WallpaperPathOrDefault => this._wallpaper.FilePathOrDefault;
		public WallpaperPosition WallpaperPosition { get => this._wallpaper.Position; set => this._wallpaper.Position = value; }
		public WallpaperViewModel Wallpaper => this._wallpaper;
		public bool HasWallpaper => !string.IsNullOrEmpty(this.WallpaperPath);
		public bool HasNoWallpaper => string.IsNullOrEmpty(this.WallpaperPath);

		internal void Update(int index, DesktopRecord record)
		{
			if (record == null || record.Id != this.Id) throw new ArgumentException("A desktop view model can only be updated by its matching ID.", nameof(record));
			if (this.Index != index)
			{
				this.Index = index;
				this.RaisePropertyChanged(nameof(this.Index));
				this.RaisePropertyChanged(nameof(this.NumberText));
			}
			var name = record.Name.HasValue ? record.Name.Value : null;
			if (this._name != name)
			{
				this._name = name;
				this.RaisePropertyChanged(nameof(this.Name));
			}
			this._wallpaper.Update(record.WallpaperPath.HasValue ? record.WallpaperPath.Value : null, record.WallpaperPosition);
			this.RaisePropertyChanged(nameof(this.WallpaperPath));
			this.RaisePropertyChanged(nameof(this.WallpaperPathOrDefault));
			this.RaisePropertyChanged(nameof(this.WallpaperPosition));
			this.RaisePropertyChanged(nameof(this.HasWallpaper));
			this.RaisePropertyChanged(nameof(this.HasNoWallpaper));
		}

		public void MoveToPrevious() => this._runtime.MoveLeft(this.Id);
		public void MoveToNext() => this._runtime.MoveRight(this.Id);
		public void MoveToFirst() => this._runtime.MoveFirst(this.Id);
		public void MoveToLast() => this._runtime.MoveLast(this.Id);
		public void Switch() => this._runtime.Switch(this.Id);
		public void Close() => this._runtime.Remove(this.Id);

		internal static VirtualDesktopViewModel[] CreateAll(DesktopTransitionRuntime runtime)
		{
			var state = runtime?.State ?? throw new InvalidOperationException("The desktop runtime is not initialized.");
			return state.Order.Select((id, index) => new VirtualDesktopViewModel(runtime, index, state.Records[id])).ToArray();
		}
	}

	public class WallpaperViewModel : ViewModel
	{
		private string _path;
		private WallpaperPosition _position;
		private readonly Action<string> _setPath;
		private readonly Action<WallpaperPosition> _setPosition;

		internal WallpaperViewModel(string path, WallpaperPosition position, Action<string> setPath, Action<WallpaperPosition> setPosition)
		{
			this._path = path;
			this._position = position;
			this._setPath = setPath ?? throw new ArgumentNullException(nameof(setPath));
			this._setPosition = setPosition ?? throw new ArgumentNullException(nameof(setPosition));
		}

		public string FilePath
		{
			get => this._path;
			set { if (this._path != value) this._setPath(value); }
		}
		public string FilePathOrDefault => !string.IsNullOrEmpty(this._path) ? this._path : WallpaperService.GetCurrentColorAndWallpaper().Item2 ?? string.Empty;
		public WallpaperPosition Position
		{
			get => this._position;
			set { if (this._position != value) this._setPosition(value); }
		}
		public Color Color { get => WallpaperService.GetCurrentColorAndWallpaper().Item1; set => WallpaperService.SetBackgroundColor(value); }

		internal void Update(string path, WallpaperPosition position)
		{
			if (this._path != path)
			{
				this._path = path;
				this.RaisePropertyChanged(nameof(this.FilePath));
				this.RaisePropertyChanged(nameof(this.FilePathOrDefault));
			}
			if (this._position != position)
			{
				this._position = position;
				this.RaisePropertyChanged(nameof(this.Position));
			}
		}
	}
}
