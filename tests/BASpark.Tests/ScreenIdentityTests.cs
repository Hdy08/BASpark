using System.Reflection;
using System.Runtime.InteropServices;

namespace BASpark.Tests;

public class ScreenIdentityTests
{
    [Fact]
    public void ScreenPreferences_KeepStableIdentitiesWhenDisplayNamesAreReassigned()
    {
        string originalSelections = ConfigManager.ScreenSelections;
        string originalEnabledIds = ConfigManager.EnabledScreenIds;
        try
        {
            ConfigManager.ScreenSelections = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new ScreenSelectionState { IdentityKey = "monitor-a", DeviceName = "DISPLAY1", IsEnabled = true },
                new ScreenSelectionState { IdentityKey = "monitor-b", DeviceName = "DISPLAY2", IsEnabled = false }
            });
            ConfigManager.EnabledScreenIds = "[\"DISPLAY1\"]";
            HashSet<string> enabled = ConfigManager.ResolveEnabledScreenDeviceNames(
            [
                new ScreenIdentityInfo { IdentityKey = "monitor-b", DeviceName = "DISPLAY1" },
                new ScreenIdentityInfo { IdentityKey = "monitor-a", DeviceName = "DISPLAY2" },
                new ScreenIdentityInfo { IdentityKey = "monitor-c", DeviceName = "DISPLAY3" }
            ]);
            Assert.Equal(new[] { "DISPLAY2", "DISPLAY3" }, enabled.Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            ConfigManager.ScreenSelections = originalSelections;
            ConfigManager.EnabledScreenIds = originalEnabledIds;
        }
    }

    [Theory]
    [InlineData("monitor-a", "DISPLAY1", "monitor-b", "DISPLAY1", false)]
    [InlineData("monitor-a", "DISPLAY1", "MONITOR-A", "DISPLAY2", true)]
    [InlineData("", "DISPLAY1", "monitor-a", "DISPLAY1", true)]
    [InlineData("DISPLAY1", "DISPLAY1", "monitor-a", "DISPLAY1", true)]
    [InlineData("monitor-a", "DISPLAY1", "monitor-b", "DISPLAY2", false)]
    public void ScreenPreferences_MergeByHardwareIdentityWithLegacyDeviceFallback(
        string leftIdentity, string leftDevice, string rightIdentity, string rightDevice, bool expected)
    {
        var left = new ScreenSelectionState { IdentityKey = leftIdentity, DeviceName = leftDevice };
        var right = new ScreenSelectionState { IdentityKey = rightIdentity, DeviceName = rightDevice };
        MethodInfo sameScreen = typeof(ConfigManager).GetMethod("IsSameSavedScreen", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, sameScreen.Invoke(null, [left, right]));
    }

    [Fact]
    public void DevMode_UsesExpectedUnicodeDisplayLayout()
    {
        Type devMode = typeof(ScreenIdentity).GetNestedType(
            "DEVMODE",
            BindingFlags.NonPublic)!;

        Assert.Equal(220, Marshal.SizeOf(devMode));
        Assert.Equal(68, Marshal.OffsetOf(devMode, "dmSize").ToInt32());
        Assert.Equal(184, Marshal.OffsetOf(devMode, "dmDisplayFrequency").ToInt32());
    }

    [Theory]
    [InlineData("", 60, 60)]
    [InlineData(" ", 1, 30)]
    [InlineData("BASpark.Invalid.Display", 999, 360)]
    public void GetRefreshRate_InvalidDeviceUsesClampedFallback(
        string deviceName,
        int fallback,
        int expected)
    {
        Assert.Equal(expected, ScreenIdentity.GetRefreshRate(deviceName, fallback));
    }
}
