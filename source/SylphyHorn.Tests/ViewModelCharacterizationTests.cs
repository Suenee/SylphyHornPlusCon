using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using SylphyHorn.Properties;
using SylphyHorn.Services;
using SylphyHorn.Services.DesktopTransitions;
using SylphyHorn.UI.Bindings;
using Xunit;

namespace SylphyHorn.Tests
{
	public class ViewModelCharacterizationTests
	{
		[Fact]
		public void NotificationWindowTitleRaisesOnlyWhenItsValueChanges()
		{
			var viewModel = new NotificationWindowViewModel();
			var names = new List<string>();
			viewModel.PropertyChanged += (_, args) => names.Add(args.PropertyName);

			Assert.Null(viewModel.Title);

			viewModel.Title = "title";
			viewModel.Title = "title";

			Assert.Equal("title", viewModel.Title);
			Assert.Equal(new[] { nameof(viewModel.Title) }, names);
		}

		[Fact]
		public void NotificationWindowTextPropertiesRaiseOnlyTheirOwnNames()
		{
			var type = typeof(NotificationWindowViewModel);
			var viewModel = (INotifyPropertyChanged)Activator.CreateInstance(type);
			var header = type.GetProperty(nameof(NotificationWindowViewModel.Header));
			var body = type.GetProperty(nameof(NotificationWindowViewModel.Body));
			var names = new List<string>();
			viewModel.PropertyChanged += (_, args) => names.Add(args.PropertyName);

			header.SetValue(viewModel, "header");
			header.SetValue(viewModel, "header");
			body.SetValue(viewModel, "body");
			body.SetValue(viewModel, "body");

			Assert.Equal(new[] { nameof(NotificationWindowViewModel.Header), nameof(NotificationWindowViewModel.Body) }, names);
			Assert.Equal("header", header.GetValue(viewModel));
			Assert.Equal("body", body.GetValue(viewModel));
			Assert.Equal(Visibility.Visible, type.GetProperty(nameof(NotificationWindowViewModel.HeaderVisibility)).GetValue(viewModel));
			Assert.Equal(Visibility.Visible, type.GetProperty(nameof(NotificationWindowViewModel.BodyVisibility)).GetValue(viewModel));
		}

		[Fact]
		public void NotificationWindowHeaderVisibilityIsCollapsedForNullAndEmptyHeaders()
		{
			var type = typeof(NotificationWindowViewModel);
			var viewModel = Activator.CreateInstance(type);
			var header = type.GetProperty(nameof(NotificationWindowViewModel.Header));
			var headerVisibility = type.GetProperty(nameof(NotificationWindowViewModel.HeaderVisibility));

			Assert.Null(header.GetValue(viewModel));
			Assert.Equal(Visibility.Collapsed, headerVisibility.GetValue(viewModel));

			header.SetValue(viewModel, string.Empty);

			Assert.Equal(string.Empty, header.GetValue(viewModel));
			Assert.Equal(Visibility.Collapsed, headerVisibility.GetValue(viewModel));
		}

		[Fact]
		public void HeaderContentPropertiesRaiseOnlyWhenValuesChange()
		{
			var viewModel = new HeaderContentViewModel();
			var names = new List<string>();
			viewModel.PropertyChanged += (_, args) => names.Add(args.PropertyName);

			viewModel.Header = "header";
			viewModel.Header = "header";
			viewModel.Content = "content";
			viewModel.Content = "content";

			Assert.Equal(new[] { nameof(viewModel.Header), nameof(viewModel.Content) }, names);
			Assert.Equal("header", viewModel.Header);
			Assert.Equal("content", viewModel.Content);
		}

		[Fact]
		public void LogViewModelFormatsTheHeaderAndCopiesContent()
		{
			var dateTime = new DateTimeOffset(2026, 8, 17, 12, 34, 56, TimeSpan.FromHours(9));
			var log = new TestLog(dateTime, "Synthetic", "details");

			var viewModel = new LogViewModel(log);

			Assert.Equal($"{dateTime:G} Synthetic", viewModel.Header);
			Assert.Equal("details", viewModel.Content);
		}

		[Fact]
		public void LicenseViewModelCopiesEmbeddedLicenseValues()
		{
			var license = Assert.Single(LicenseInfo.All, item => item.ProductName == "VirtualDesktop");

			var viewModel = new LicenseViewModel(license);

			Assert.Equal(license.ProductName, viewModel.Header);
			Assert.Equal(license.LicenseBody, viewModel.Content);
			Assert.False(string.IsNullOrWhiteSpace(viewModel.Content));
		}

		[Fact]
		public void ResourceServiceExposesStableResourcesAndSupportedCultures()
		{
			var service = ResourceService.Current;

			Assert.NotNull(service.Resources);
			Assert.Same(service.Resources, service.Resources);
			Assert.Equal(new[] { "en", "ja" }, service.SupportedCultures.Select(culture => culture.Name));
		}

		[Fact]
		public void VirtualDesktopUpdateRaisesTheCurrentPropertyNameSequence()
		{
			var initial = new DesktopRecord(
				DesktopRuntimeTestData.A,
				DesktopPropertyState.Provider("before"),
				DesktopPropertyState.Provider("before.jpg"),
				WallpaperPosition.Fill,
				DesktopRecordOrigin.TrulyNewRecord);
			var updated = new DesktopRecord(
				DesktopRuntimeTestData.A,
				DesktopPropertyState.Provider("after"),
				DesktopPropertyState.Provider("after.jpg"),
				WallpaperPosition.Fit,
				DesktopRecordOrigin.TrulyNewRecord);
			var harness = Harness.Create(DesktopRuntimeTestData.Batch(
				1,
				1,
				DesktopRuntimeTestData.A,
				DesktopRuntimeTestData.Entry(DesktopRuntimeTestData.A, 0, "before", "before.jpg")));
			var viewModel = new VirtualDesktopViewModel(harness.Runtime, 0, initial);
			var names = new List<string>();
			viewModel.PropertyChanged += (_, args) => names.Add(args.PropertyName);

			viewModel.Update(1, updated);

			Assert.Equal(
				new[]
				{
					nameof(viewModel.Index),
					nameof(viewModel.NumberText),
					nameof(viewModel.Name),
					nameof(viewModel.WallpaperPath),
					nameof(viewModel.WallpaperPathOrDefault),
					nameof(viewModel.WallpaperPosition),
					nameof(viewModel.HasWallpaper),
					nameof(viewModel.HasNoWallpaper),
				},
				names);
		}

		[Fact]
		public void VirtualDesktopUpdateDoesNotNotifyWhenTheRecordIsUnchanged()
		{
			var record = new DesktopRecord(
				DesktopRuntimeTestData.A,
				DesktopPropertyState.Provider("name"),
				DesktopPropertyState.Provider("wallpaper.jpg"),
				WallpaperPosition.Fill,
				DesktopRecordOrigin.TrulyNewRecord);
			var harness = Harness.Create(DesktopRuntimeTestData.Batch(
				1,
				1,
				DesktopRuntimeTestData.A,
				DesktopRuntimeTestData.Entry(DesktopRuntimeTestData.A, 0, "name", "wallpaper.jpg")));
			var viewModel = new VirtualDesktopViewModel(harness.Runtime, 0, record);
			var names = new List<string>();
			viewModel.PropertyChanged += (_, args) => names.Add(args.PropertyName);

			viewModel.Update(0, record);

			Assert.Empty(names);
		}

		[Fact]
		public void SupportedWallpaperRejectsEmptyPathAndRestoresTheDisplayedValue()
		{
			var record = new DesktopRecord(
				DesktopRuntimeTestData.A,
				DesktopPropertyState.Provider("name"),
				DesktopPropertyState.Provider("wallpaper.jpg"),
				WallpaperPosition.Fill,
				DesktopRecordOrigin.TrulyNewRecord);
			var harness = Harness.Create(DesktopRuntimeTestData.Batch(
				1,
				1,
				DesktopRuntimeTestData.A,
				DesktopRuntimeTestData.Entry(DesktopRuntimeTestData.A, 0, "name", "wallpaper.jpg")));
			var viewModel = new VirtualDesktopViewModel(harness.Runtime, 0, record);
			var names = new List<string>();
			viewModel.PropertyChanged += (_, args) => names.Add(args.PropertyName);

			viewModel.WallpaperPath = string.Empty;

			Assert.Equal("wallpaper.jpg", viewModel.WallpaperPath);
			Assert.Equal(new[] { nameof(viewModel.WallpaperPath) }, names);
			Assert.Equal(0, harness.Operations.WallpaperCalls);
		}

		[Fact]
		public void VirtualDesktopCommandsAreStableAlwaysExecutableAndDelegateToTheirMethods()
		{
			var initial = new DesktopRecord(
				DesktopRuntimeTestData.A,
				DesktopPropertyState.Provider("name"),
				DesktopPropertyState.Provider("wallpaper.jpg"),
				WallpaperPosition.Fill,
				DesktopRecordOrigin.TrulyNewRecord);
			var harness = Harness.Create(DesktopRuntimeTestData.Batch(
				1,
				1,
				DesktopRuntimeTestData.A,
				DesktopRuntimeTestData.Entry(DesktopRuntimeTestData.A, 0, "name", "wallpaper.jpg")));
			var viewModel = new VirtualDesktopViewModel(harness.Runtime, 0, initial);

			Assert.Same(viewModel.CloseCommand, viewModel.CloseCommand);
			Assert.Same(viewModel.MoveToPreviousCommand, viewModel.MoveToPreviousCommand);
			Assert.Same(viewModel.MoveToNextCommand, viewModel.MoveToNextCommand);
			Assert.Same(viewModel.MoveToFirstCommand, viewModel.MoveToFirstCommand);
			Assert.Same(viewModel.MoveToLastCommand, viewModel.MoveToLastCommand);
			Assert.Same(viewModel.SwitchCommand, viewModel.SwitchCommand);
			Assert.True(viewModel.CloseCommand.CanExecute(null));
			Assert.True(viewModel.MoveToPreviousCommand.CanExecute(null));
			Assert.True(viewModel.MoveToNextCommand.CanExecute(null));
			Assert.True(viewModel.MoveToFirstCommand.CanExecute(null));
			Assert.True(viewModel.MoveToLastCommand.CanExecute(null));
			Assert.True(viewModel.SwitchCommand.CanExecute(null));

			viewModel.CloseCommand.Execute(null);
			viewModel.MoveToPreviousCommand.Execute(null);
			viewModel.MoveToNextCommand.Execute(null);
			viewModel.MoveToFirstCommand.Execute(null);
			viewModel.MoveToLastCommand.Execute(null);
			viewModel.SwitchCommand.Execute(null);

			Assert.Equal(
				new[] { "Remove", "MoveLeft", "MoveRight", "MoveFirst", "MoveLast", "Switch" },
				harness.Operations.DesktopOperationNames);
			Assert.Equal(
				new[]
				{
					DesktopRuntimeTestData.A,
					DesktopRuntimeTestData.A,
					DesktopRuntimeTestData.A,
					DesktopRuntimeTestData.A,
					DesktopRuntimeTestData.A,
					DesktopRuntimeTestData.A,
				},
				harness.Operations.DesktopOperationIds);
		}

		private sealed class TestLog : ILog
		{
			internal TestLog(DateTimeOffset dateTime, string header, string content)
			{
				this.DateTime = dateTime;
				this.Header = header;
				this.Content = content;
			}

			public DateTimeOffset DateTime { get; }
			public string Header { get; }
			public string Content { get; }
		}
	}
}
