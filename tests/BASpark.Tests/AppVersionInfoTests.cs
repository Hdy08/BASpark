namespace BASpark.Tests;

public class AppVersionInfoTests
{
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
    public void TryCompare_RejectsMalformedVersionStrings()
    {
        Assert.False(AppVersionInfo.TryCompare("not-a-version", "1.6.2-beta", out _));
    }
}
