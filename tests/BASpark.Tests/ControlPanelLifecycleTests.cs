namespace BASpark.Tests;

public class ControlPanelLifecycleTests
{
    [Fact]
    public void ResetConfiguration_DoesNotRecreateDeletedSettingsDuringWindowClose()
    {
        string root = FindWorkspaceRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "ControlPanelWindow.xaml.cs"));

        int closingStart = source.IndexOf("protected override void OnClosing", StringComparison.Ordinal);
        int closingEnd = source.IndexOf("private void EffectSlider_ValueChanged", closingStart, StringComparison.Ordinal);
        Assert.True(closingStart >= 0 && closingEnd > closingStart, "OnClosing implementation is missing.");
        string closing = source[closingStart..closingEnd];
        Assert.Contains("if (!_skipSaveOnClosing)", closing, StringComparison.Ordinal);
        Assert.Contains("ConfigManager.Save(\"TotalClicks\", ConfigManager.TotalClicks);", closing, StringComparison.Ordinal);

        int resetStart = source.IndexOf("private void ResetConfig_Click", StringComparison.Ordinal);
        Assert.True(resetStart >= 0, "ResetConfig_Click implementation is missing.");
        string reset = source[resetStart..];
        int suppressSave = reset.IndexOf("_skipSaveOnClosing = true;", StringComparison.Ordinal);
        int clearSettings = reset.IndexOf("ConfigManager.ResetAndClear();", StringComparison.Ordinal);
        int shutdown = reset.IndexOf("System.Windows.Application.Current.Shutdown();", StringComparison.Ordinal);
        int restoreSave = reset.IndexOf("_skipSaveOnClosing = false;", StringComparison.Ordinal);

        Assert.True(suppressSave >= 0 && suppressSave < clearSettings);
        Assert.True(clearSettings < shutdown);
        Assert.True(shutdown < restoreSave);
    }

    private static string FindWorkspaceRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BASpark.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the BASpark workspace root.");
    }
}
