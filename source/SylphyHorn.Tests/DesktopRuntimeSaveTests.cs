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

using static SylphyHorn.Tests.DesktopRuntimeTestData;

namespace SylphyHorn.Tests
{
	public sealed class DesktopRuntimeSaveTests
	{
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

	}
}
