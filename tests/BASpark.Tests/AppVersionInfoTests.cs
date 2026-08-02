namespace BASpark.Tests;

public class AppVersionInfoTests
{
    [Fact]
    public void DisplayVersion_MatchesStableRelease()
    {
        Assert.Equal("1.6.2", AppVersionInfo.DisplayVersion);
    }

    [Fact]
    public void StableReleaseMetadata_DoesNotRetainPrereleasePlaceholders()
    {
        string root = FindWorkspaceRoot();
        string project = File.ReadAllText(Path.Combine(root, "src", "BASpark.csproj"));
        string installer = File.ReadAllText(Path.Combine(root, "setup.iss"));
        string controlPanel = File.ReadAllText(Path.Combine(root, "src", "ControlPanelWindow.xaml"));
        string privacyWindow = File.ReadAllText(Path.Combine(root, "src", "PrivacyWindow.xaml"));

        Assert.Contains("<Version>1.6.2</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>1.6.2</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.Contains("#define AppVersion \"1.6.2\"", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("1.6.2-beta", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1.6.2-beta", controlPanel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1.6.2-beta", privacyWindow, StringComparison.OrdinalIgnoreCase);
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

    private static string FindWorkspaceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BASpark.sln")))
            {
                return directory.FullName;
            }
        }

        throw new Xunit.Sdk.XunitException("Could not locate the BASpark workspace root.");
    }
}
