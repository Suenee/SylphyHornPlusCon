using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SylphyHorn.Serialization;
using SylphyHorn.UI.Bindings;
using WindowsDesktop;

namespace SylphyHorn.Services
{
	/// <summary>
	/// Provides one user-facing logical desktop move operation on every supported Windows build.
	/// Windows 11 uses the native desktop reorder API. Windows 10 keeps the physical desktop slots
	/// fixed and rotates their logical contents (windows and SHPC desktop metadata) instead.
	/// </summary>
	internal sealed class LogicalDesktopOrderService
	{
		private const uint GwOwner = 4;
		private static readonly HashSet<string> ShellWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
		};

		internal static LogicalDesktopOrderService Instance { get; } = new LogicalDesktopOrderService();

		private LogicalDesktopOrderService() { }

		internal void Move(VirtualDesktopViewModel[] desktops, int sourceIndex, int targetIndex, bool nativeReorderSupported)
		{
			if (desktops == null || desktops.Length < 2) return;
			if (sourceIndex < 0 || sourceIndex >= desktops.Length) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
			if (targetIndex < 0 || targetIndex >= desktops.Length) throw new ArgumentOutOfRangeException(nameof(targetIndex));
			if (sourceIndex == targetIndex) return;

			if (nativeReorderSupported)
			{
				var desktop = VirtualDesktop.FromId(desktops[sourceIndex].Id)
					?? throw new InvalidOperationException("The source virtual desktop no longer exists.");
				desktop.Move(targetIndex);
				LoggingService.Instance.Write(LogLevel.Info, "DESKTOP", "LogicalDesktopMove", $"Native desktop move completed: {sourceIndex + 1} -> {targetIndex + 1}.", desktop.Id.ToString("D"), "backend=native");
				return;
			}

			this.MoveEmulated(desktops, sourceIndex, targetIndex);
		}

		private void MoveEmulated(VirtualDesktopViewModel[] desktops, int sourceIndex, int targetIndex)
		{
			var physicalDesktops = VirtualDesktop.GetDesktops();
			if (physicalDesktops.Length != desktops.Length)
				throw new InvalidOperationException("Virtual desktop topology changed while the logical move was being prepared. Try the move again.");

			var physicalById = physicalDesktops.ToDictionary(x => x.Id);
			for (var i = 0; i < desktops.Length; i++)
				if (!physicalById.ContainsKey(desktops[i].Id)) throw new InvalidOperationException("Virtual desktop topology changed while the logical move was being prepared. Try the move again.");

			var snapshots = desktops.Select(x => new LogicalDesktopSnapshot(
				x.Id,
				x.StoredTitle,
				x.CanonicalName,
				x.CanonicalNameIsAutomatic,
				x.WallpaperPath,
				x.WallpaperPosition)).ToArray();

			this.CaptureWindows(snapshots);

			var logicalOrder = Enumerable.Range(0, desktops.Length).ToList();
			var movingLogicalIndex = logicalOrder[sourceIndex];
			logicalOrder.RemoveAt(sourceIndex);
			logicalOrder.Insert(targetIndex, movingLogicalIndex);

			var destinationBySource = new int[desktops.Length];
			for (var destination = 0; destination < logicalOrder.Count; destination++)
				destinationBySource[logicalOrder[destination]] = destination;

			var movedWindows = new List<MovedWindow>();
			try
			{
				for (var source = 0; source < snapshots.Length; source++)
				{
					var destination = destinationBySource[source];
					if (source == destination) continue;
					var destinationDesktop = physicalById[desktops[destination].Id];
					var originalDesktop = physicalById[desktops[source].Id];
					foreach (var hwnd in snapshots[source].Windows)
					{
						if (!IsWindow(hwnd)) continue;
						VirtualDesktopHelper.MoveToDesktop(hwnd, destinationDesktop);
						movedWindows.Add(new MovedWindow(hwnd, originalDesktop));
					}
				}
			}
			catch (Exception ex)
			{
				for (var i = movedWindows.Count - 1; i >= 0; i--)
				{
					try { if (IsWindow(movedWindows[i].Handle)) VirtualDesktopHelper.MoveToDesktop(movedWindows[i].Handle, movedWindows[i].OriginalDesktop); }
					catch { }
				}
				LoggingService.Instance.Write(LogLevel.Error, "DESKTOP", "LogicalDesktopMoveFailed", "Windows 10 logical desktop move failed and window rollback was attempted.", desktops[sourceIndex].Id.ToString("D"), ex.ToString());
				throw;
			}

			var targetModels = new VirtualDesktopViewModel[desktops.Length];
			var canonicalValues = new string[desktops.Length];
			var canonicalAutomatic = new bool[desktops.Length];
			for (var destination = 0; destination < desktops.Length; destination++)
			{
				var source = logicalOrder[destination];
				var snapshot = snapshots[source];
				var target = desktops[destination];
				targetModels[destination] = target;
				canonicalValues[destination] = snapshot.CanonicalName;
				canonicalAutomatic[destination] = snapshot.CanonicalNameIsAutomatic;
				target.ApplyLogicalTitle(snapshot.Title);
				target.ApplyLogicalWallpaper(snapshot.WallpaperPath, snapshot.WallpaperPosition);
			}
			VirtualDesktopViewModel.ApplyLogicalCanonicalNames(targetModels, canonicalValues, canonicalAutomatic);

			var currentPhysicalId = VirtualDesktop.Current.Id;
			var currentSourceIndex = Array.FindIndex(snapshots, x => x.PhysicalId == currentPhysicalId);
			if (currentSourceIndex >= 0)
			{
				var currentDestination = destinationBySource[currentSourceIndex];
				var destinationDesktop = physicalById[desktops[currentDestination].Id];
				if (destinationDesktop.Id != currentPhysicalId) destinationDesktop.Switch();
			}

			LoggingService.Instance.Write(LogLevel.Info, "DESKTOP", "LogicalDesktopMove", $"Emulated desktop move completed: {sourceIndex + 1} -> {targetIndex + 1}.", desktops[targetIndex].Id.ToString("D"), $"backend=windows10-content-rotation;windows={movedWindows.Count}");
		}

		private void CaptureWindows(LogicalDesktopSnapshot[] snapshots)
		{
			var indexById = snapshots.Select((x, index) => new { x.PhysicalId, Index = index }).ToDictionary(x => x.PhysicalId, x => x.Index);
			EnumWindows((hwnd, _) =>
			{
				try
				{
					if (!IsApplicationWindow(hwnd) || VirtualDesktop.IsPinnedWindowOrDefault(hwnd)) return true;
					var desktop = VirtualDesktop.FromHwnd(hwnd);
					if (desktop != null && indexById.TryGetValue(desktop.Id, out var index)) snapshots[index].Windows.Add(hwnd);
				}
				catch { }
				return true;
			}, IntPtr.Zero);
		}

		private static bool IsApplicationWindow(IntPtr hwnd)
		{
			if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero) return false;
			var builder = new StringBuilder(128);
			if (GetClassName(hwnd, builder, builder.Capacity) > 0 && ShellWindowClasses.Contains(builder.ToString())) return false;
			return true;
		}

		private sealed class LogicalDesktopSnapshot
		{
			internal LogicalDesktopSnapshot(Guid physicalId, string title, string canonicalName, bool canonicalNameIsAutomatic, string wallpaperPath, WallpaperPosition wallpaperPosition)
			{
				this.PhysicalId = physicalId;
				this.Title = title;
				this.CanonicalName = canonicalName;
				this.CanonicalNameIsAutomatic = canonicalNameIsAutomatic;
				this.WallpaperPath = wallpaperPath;
				this.WallpaperPosition = wallpaperPosition;
			}
			internal Guid PhysicalId { get; }
			internal string Title { get; }
			internal string CanonicalName { get; }
			internal bool CanonicalNameIsAutomatic { get; }
			internal string WallpaperPath { get; }
			internal WallpaperPosition WallpaperPosition { get; }
			internal List<IntPtr> Windows { get; } = new List<IntPtr>();
		}

		private readonly struct MovedWindow
		{
			internal MovedWindow(IntPtr handle, VirtualDesktop originalDesktop) { this.Handle = handle; this.OriginalDesktop = originalDesktop; }
			internal IntPtr Handle { get; }
			internal VirtualDesktop OriginalDesktop { get; }
		}

		private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindow(IntPtr hwnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindowVisible(IntPtr hwnd);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
	}
}
