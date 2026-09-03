using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using SylphyHorn.Interop;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.Services.DesktopTransitions;

namespace SylphyHorn.Services
{
	public class WallpaperService : IDisposable
	{
		private static readonly ImageFormatSupportDetector[] _detectors =
		{
			new JpegXrSupportDetector(), new WebPSupportDetector(), new HeifSupportDetector(),
		};
		private static readonly Tuple<string, string, string>[] _defaultSupportedFormats =
		{
			Tuple.Create("JPEG", "JPEG", "*.jpg;*.jpeg;*.jpe;*.jfif"), Tuple.Create("PNG", "PNG", "*.png"),
			Tuple.Create("BMP", "Bitmap", "*.bmp;*.dib"), Tuple.Create("GIF", "GIF", "*.gif"), Tuple.Create("TIFF", "TIFF", "*.tif;*.tiff"),
		};

		public static readonly string SupportedFormats = CreateSupportFormatText();
		public static string[] SupportedFileTypes { get; } = _defaultSupportedFormats.Select(f => f.Item1).Concat(_detectors.Where(d => d.IsSupported).Select(d => d.FileType)).ToArray();
		public static WallpaperService Instance { get; } = new WallpaperService();

		private readonly WallpaperApplyQueue _applyQueue;
		private DesktopTransitionRuntime _runtime;
		private bool _sessionCaptured;

		private WallpaperService()
		{
			this._applyQueue = new WallpaperApplyQueue(ApplyDesktopWallpaper, exception => LoggingService.Instance.Register(exception), action => Task.Run(action));
		}

		internal void BindDesktopRuntime(DesktopTransitionRuntime runtime)
		{
			if (runtime == null) throw new ArgumentNullException(nameof(runtime));
			if (this._runtime != null)
			{
				if (ReferenceEquals(this._runtime, runtime)) return;
				throw new InvalidOperationException("WallpaperService cannot be rebound to another desktop runtime.");
			}
			this._runtime = runtime;
			runtime.StateChanged += this.OnDesktopStateChanged;
			if (Settings.General.ChangeBackgroundEachDesktop) this.CaptureOriginalWallpaper(true);
			this.ApplyCurrent(runtime.State);
		}

		public void SetManagementEnabled(bool enabled)
		{
			if (enabled == Settings.General.ChangeBackgroundEachDesktop.Value) return;
			if (enabled)
			{
				this.CaptureOriginalWallpaper(true);
				Settings.General.ChangeBackgroundEachDesktop.Value = true;
				if (this._runtime != null) this.ApplyCurrent(this._runtime.State);
				LoggingService.Instance.Write(LogLevel.Info, "WALLPAPER", "ManagementEnabled", "Individual desktop wallpaper management enabled.");
			}
			else
			{
				this.RestoreOriginalWallpaper();
				Settings.General.ChangeBackgroundEachDesktop.Value = false;
				LoggingService.Instance.Write(LogLevel.Info, "WALLPAPER", "ManagementDisabled", "Individual desktop wallpaper management disabled and original wallpaper restored when available.");
			}
		}

		public string OriginalWallpaperPath => Settings.General.OriginalWallpaperCaptured.Value ? Settings.General.OriginalWallpaperPath.Value : null;
		public WallpaperPosition OriginalWallpaperPosition => (WallpaperPosition)Settings.General.OriginalWallpaperPosition.Value;

		private void CaptureOriginalWallpaper(bool replace)
		{
			if (this._sessionCaptured && !replace) return;
			var wallpaper = DesktopWallpaperFactory.Create();
			var current = GetCurrentColorAndWallpaper();
			Settings.General.OriginalWallpaperPath.Value = current.Item2;
			Settings.General.OriginalWallpaperPosition.Value = (byte)wallpaper.GetPosition();
			Settings.General.OriginalWallpaperCaptured.Value = true;
			this._sessionCaptured = true;
			LoggingService.Instance.Write(LogLevel.Info, "WALLPAPER", "OriginalCaptured", "Original Windows wallpaper state captured before SHPC management.", details: current.Item2);
		}

		public void RestoreOriginalWallpaper()
		{
			if (!Settings.General.OriginalWallpaperCaptured.Value) return;
			this._applyQueue.ApplyNow(Settings.General.OriginalWallpaperPath.Value, (WallpaperPosition)Settings.General.OriginalWallpaperPosition.Value);
			LoggingService.Instance.Write(LogLevel.Info, "WALLPAPER", "OriginalRestored", "Original Windows wallpaper state restored.", details: Settings.General.OriginalWallpaperPath.Value);
		}

		private void OnDesktopStateChanged(object sender, DesktopRuntimeStateChanged e)
		{
			var state = e.Change.Snapshot;
			var current = state.CurrentDesktopId;
			if (!current.HasValue) return;
			if (e.Change.Kind == DesktopStateChangeKind.CurrentChanged || e.Change.Kind == DesktopStateChangeKind.Initialized || e.Change.Kind == DesktopStateChangeKind.Reset ||
				e.Change.WallpaperChanges.Any(change => change.Id == current.Value) || e.Change.PositionChanges.Any(change => change.Id == current.Value)) this.ApplyCurrent(state);
		}

		private void ApplyCurrent(DesktopRuntimeState state)
		{
			if (state?.CurrentDesktopId == null || !state.Records.TryGetValue(state.CurrentDesktopId.Value, out var record)) return;
			var path = record.WallpaperPath.HasValue ? record.WallpaperPath.Value : null;
			if (!ProductInfo.IsWallpaperSupportBuild && !Settings.General.ChangeBackgroundEachDesktop) path = null;
			this._applyQueue.Enqueue(path, record.WallpaperPosition);
		}

		internal static void ApplyDesktopWallpaper(string path, WallpaperPosition position)
		{
			var wallpaper = DesktopWallpaperFactory.Create();
			if (!ProductInfo.IsWallpaperSupportBuild && !string.IsNullOrEmpty(path)) wallpaper.SetWallpaper(null, path);
			var target = (DesktopWallpaperPosition)position;
			if (wallpaper.GetPosition() != target) wallpaper.SetPosition(target);
		}

		internal void ApplyDesktopWallpaperNow(string path, WallpaperPosition position) => this._applyQueue.ApplyNow(path, position);

		public void Dispose()
		{
			if (Settings.General.ChangeBackgroundEachDesktop) this.RestoreOriginalWallpaper();
			if (this._runtime != null) this._runtime.StateChanged -= this.OnDesktopStateChanged;
			this._runtime = null;
			this._applyQueue.Dispose();
		}

		public static void SetWallpaperEnabled(bool enabled) => DesktopWallpaperFactory.Create().Enable(enabled);
		public static void SetBackgroundColor(Color color) => DesktopWallpaperFactory.Create().SetBackgroundColor(new COLORREF { R = color.R, G = color.G, B = color.B });

		public static Tuple<Color, string> GetCurrentColorAndWallpaper()
		{
			var wallpaper = DesktopWallpaperFactory.Create();
			var colorref = wallpaper.GetBackgroundColor();
			string path = null;
			if (wallpaper.GetMonitorDevicePathCount() >= 1)
			{
				var monitorId = wallpaper.GetMonitorDevicePathAt(0);
				path = wallpaper.GetWallpaper(monitorId);
			}
			return Tuple.Create(Color.FromRgb(colorref.R, colorref.G, colorref.B), path);
		}

		private static string CreateSupportFormatText()
		{
			var defaultExtensions = string.Join(";", _defaultSupportedFormats.Select(f => f.Item3).Concat(_detectors.Where(d => d.IsSupported).SelectMany(d => d.Extensions.Select(e => $"*{e}"))));
			return $"Image File ({defaultExtensions})|{defaultExtensions}|" + string.Join("|", _defaultSupportedFormats.Select(f => $"{f.Item2} ({f.Item3})|{f.Item3}").Concat(_detectors.Where(d => d.IsSupported).Select(d => d.FormatInfo)));
		}
	}

	public enum WallpaperPosition : byte { Center = 0, Tile, Stretch, Fit, Fill, Span }
}
