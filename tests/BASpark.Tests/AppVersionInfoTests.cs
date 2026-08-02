namespace BASpark.Tests;

public class AppVersionInfoTests
{
    [Fact]
    public void DisplayVersion_MatchesStableRelease()
    {
        Assert.Equal("1.6.2", AppVersionInfo.DisplayVersion);
    }

    [Fact]
    public void TryCompare_TreatsReleaseAsNewerThanSamePrerelease()
    {
        Assert.True(AppVersionInfo.TryCompare("1.6.2", "1.6.2-beta", out int result));
        Assert.True(result > 0);
    }

    [Fact]
    public void TryCompare_HandlesBetaVersionStrings()
    {
        Assert.True(AppVersionInfo.TryCompare("v1.6.3-beta", "1.6.2-beta", out int result));
        Assert.True(result > 0);
    }

    [Fact]
    public void TryCompare_TreatsBeta2AsNewerThanBeta()
    {
        Assert.True(AppVersionInfo.TryCompare("1.6.2-beta2", "1.6.2-beta", out int result));
        Assert.True(result > 0);
    }

    [Fact]
    public void TryCompare_RejectsMalformedVersionStrings()
    {
        Assert.False(AppVersionInfo.TryCompare("not-a-version", "1.6.2-beta", out _));
    }
}
