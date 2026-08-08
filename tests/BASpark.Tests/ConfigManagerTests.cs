using System.Reflection;

namespace BASpark.Tests;

public class ConfigManagerTests
{
    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(3.0, 2.0)]
    [InlineData(0.5, 0.3333333333333333)]
    public void LegacyEffectScale_NormalizesToTheNewOnePointZeroBaseline(
        double legacyScale,
        double expectedScale)
    {
        MethodInfo normalizer = typeof(ConfigManager).GetMethod(
            "NormalizeLegacyEffectScale",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        double actualScale = (double)normalizer.Invoke(null, new object[] { legacyScale })!;

        Assert.Equal(expectedScale, actualScale, 3);
    }
}
