using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SylphyHorn.Serialization;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class SettingsContractTests
	{
		[Fact]
		public async Task GeneralSettingsSchemaKeysTypesAndDefaultsRemainStable()
		{
			var provider = await CreateProviderAsync();
			var settings = new GeneralSettings(provider);
			var expected = new[]
			{
				Scalar("LoopDesktop", false), Scalar("NotificationWhenSwitchedDesktop", true), Scalar("AlwaysShowDesktopNotification", false),
				Scalar("SimpleNotification", false), Scalar("NotificationDuration", 2500), Scalar("ChangeBackgroundEachDesktop", false),
				Scalar<string>("DesktopBackgroundFolderPath", null), Scalar("DesktopCanonicalNames", "{}"),
				Scalar<string>("OriginalWallpaperPath", null), Scalar("OriginalWallpaperPosition", (byte)4), Scalar("OriginalWallpaperCaptured", false),
				Scalar("OverrideWindowsDefaultKeyCombination", false), Scalar("SuspendKeyDetection", false), Scalar("FirstTime", true),
				Scalar<string>("Culture", null), Scalar("LoggingMode", "single"), Scalar("Placement", 5U), Scalar("Display", 0U),
				Scalar("NotificationWindowStyle", 4U), Scalar("NotificationCornerStyle", 1U), Scalar("NotificationHeaderAlignment", 0U), Scalar("NotificationBodyAlignment", 0U),
				Scalar<string>("NotificationFontFamily", null), Scalar("NotificationHeaderFontSize", 18), Scalar("NotificationBodyFontSize", 32), Scalar("NotificationLineSpacing", -4),
				Scalar("NotificationMinWidth", 500), Scalar("SimpleNotificationMinWidth", 210), Scalar("PinWindowMinWidth", 400), Scalar("NotificationMinHeight", 100),
				Scalar("NotificationOffsetX", 0), Scalar("NotificationOffsetY", 0), Scalar("PinWindowOffsetX", 0), Scalar("PinWindowOffsetY", 0),
				Scalar("TrayShowDesktop", false), Scalar("TrayShowOnlyCurrentNumber", false), Scalar("UseDesktopName", false), Scalar("OverrideDesktopsOnStartup", false),
				List("DesktopNames", typeof(DesktopNamePropertyList)), List("DesktopBackgroundImagePaths", typeof(WallpaperPathPropertyList)), List("DesktopBackgroundPositions", typeof(WallpaperPositionsPropertyList)),
			};
			AssertSettingsContract(settings, expected);
		}

		[Fact]
		public async Task ShortcutSettingsSchemaKeysAndDefaultsRemainStable()
		{
			var provider = await CreateProviderAsync(); var settings = new ShortcutKeySettings(provider);
			var defaults = new Dictionary<string, int[]>(StringComparer.Ordinal)
			{
				["MoveLeftAndSwitch"] = new[] { 37, 162, 164, 91 }, ["MoveRightAndSwitch"] = new[] { 39, 162, 164, 91 },
				["MoveNewAndSwitch"] = new[] { 68, 162, 164, 91 }, ["SwitchToLeftWithDefault"] = new[] { 37, 162, 91 },
				["SwitchToRightWithDefault"] = new[] { 39, 162, 91 }, ["TogglePin"] = new[] { 80, 162, 164, 91 },
			};
			AssertShortcutContract(settings, "ShortcutKeySettings", defaults);
		}

		[Fact]
		public async Task MouseShortcutSettingsUseIndependentKeysAndRejectUnsupportedDefaults()
		{
			var provider = await CreateProviderAsync(); var settings = new MouseShortcutSettings(provider);
			AssertShortcutContract(settings, "MouseShortcutSettings", new Dictionary<string, int[]>(StringComparer.Ordinal)
			{
				["SwitchToLeftWithDefault"] = new[] { 37, 162, 91 }, ["SwitchToRightWithDefault"] = new[] { 39, 162, 91 },
			});
		}

		private static void AssertSettingsContract(object settings, IReadOnlyCollection<SettingContract> expected)
		{
			var actualProperties = GetSettingProperties(settings.GetType());
			Assert.Equal(expected.Select(item => item.Name).OrderBy(name => name), actualProperties.Keys.OrderBy(name => name));
			foreach (var contract in expected)
			{
				var propertyInfo = actualProperties[contract.Name]; Assert.Equal(contract.PropertyType, propertyInfo.PropertyType);
				var property = propertyInfo.GetValue(settings); Assert.Equal(settings.GetType().Name + "." + contract.Name, GetPropertyValue<string>(property, "Key"));
				if (contract.IsList) { Assert.Equal(0, GetPropertyValue<int>(property, "Count")); continue; }
				AssertValue(contract.DefaultValue, GetPropertyValue<object>(property, "Default")); AssertValue(contract.DefaultValue, GetPropertyValue<object>(property, "Value"));
			}
		}

		private static void AssertShortcutContract(object settings, string prefix, IReadOnlyDictionary<string, int[]> defaults)
		{
			var properties = GetSettingProperties(settings.GetType());
			foreach (var pair in properties)
			{
				var property = pair.Value.GetValue(settings); Assert.Equal(prefix + "." + pair.Key, GetPropertyValue<string>(property, "Key"));
				var expected = defaults.TryGetValue(pair.Key, out var value) ? value : Array.Empty<int>();
				Assert.Equal(expected, GetPropertyValue<int[]>(property, "Value"));
			}
		}

		private static Dictionary<string, PropertyInfo> GetSettingProperties(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.PropertyType.IsGenericType || property.PropertyType.Name.EndsWith("PropertyList", StringComparison.Ordinal)).ToDictionary(property => property.Name);
		private static T GetPropertyValue<T>(object target, string propertyName) => (T)target.GetType().GetProperty(propertyName).GetValue(target);
		private static void AssertValue(object expected, object actual) { if (expected is Array expectedArray && actual is Array actualArray) Assert.Equal(expectedArray.Cast<object>(), actualArray.Cast<object>()); else Assert.Equal(expected, actual); }
		private static SettingContract Scalar<T>(string name, T defaultValue) => new SettingContract(name, typeof(MetroTrilithon.Serialization.SerializableProperty<T>), defaultValue, false);
		private static SettingContract List(string name, Type type) => new SettingContract(name, type, null, true);
		private sealed class SettingContract
		{
			public SettingContract(string name, Type propertyType, object defaultValue, bool isList) { this.Name = name; this.PropertyType = propertyType; this.DefaultValue = defaultValue; this.IsList = isList; }
			public string Name { get; } public Type PropertyType { get; } public object DefaultValue { get; } public bool IsList { get; }
		}
		private static async Task<MetroTrilithon.Serialization.ISerializationProvider> CreateProviderAsync()
		{
			var type = typeof(GeneralSettings).Assembly.GetType("SylphyHorn.Serialization.InMemorySerializationProvider", throwOnError: true);
			var provider = (MetroTrilithon.Serialization.ISerializationProvider)Activator.CreateInstance(type, nonPublic: true);
			var initialize = type.GetMethod("InitializeAsync", BindingFlags.Public | BindingFlags.Instance); if (initialize != null) await (Task)initialize.Invoke(provider, null); return provider;
		}
	}
}
