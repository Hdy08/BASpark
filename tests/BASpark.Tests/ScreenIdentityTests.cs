using System.Reflection;
using System.Runtime.InteropServices;

namespace BASpark.Tests;

public class ScreenIdentityTests
{
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
