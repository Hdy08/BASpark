using System.Text;

namespace BASpark.Tests;

public sealed class EnvironmentFilterBehaviorTests
{
    [Fact]
    public void EnvironmentSuppression_LeavesExistingEffectsRunning()
    {
        string mainWindowSource = ReadSource("src", "MainWindow.xaml.cs");
        int methodStart = mainWindowSource.IndexOf(
            "public void SetEnvironmentSuppressed(bool suppressed)",
            StringComparison.Ordinal);
        int methodEnd = mainWindowSource.IndexOf(
            "private bool ShouldOverlayBeVisible",
            methodStart,
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0, "Environment suppression handler is missing.");
        Assert.True(methodEnd > methodStart, "Environment suppression handler is incomplete.");

        string method = mainWindowSource[methodStart..methodEnd];
        Assert.Contains("_environmentInputSuppressed = suppressed", method, StringComparison.Ordinal);
        Assert.Contains("ApplyEnvironmentInputSuppression()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncOverlayPresentationState", method, StringComparison.Ordinal);
        Assert.DoesNotContain("PauseOverlayRuntime", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_hiddenByEnvironmentSuppression", mainWindowSource, StringComparison.Ordinal);

        string overlayVisibility = mainWindowSource[methodEnd..];
        Assert.Contains("private bool ShouldOverlayBeVisible =>", overlayVisibility, StringComparison.Ordinal);
        Assert.Contains("!_hiddenForExternalScreenshotCapture", overlayVisibility, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentSuppression_ReleasesInputWithoutUsingTheCancelPath()
    {
        string overlaySource = ReadSource("src", "OverlayManager.cs");
        int applyStart = overlaySource.IndexOf(
            "private void ApplySuppressionSideEffects(bool isSuppressed)",
            StringComparison.Ordinal);
        int applyEnd = overlaySource.IndexOf(
            "private bool TryGetForegroundProcessName",
            applyStart,
            StringComparison.Ordinal);
        int releaseStart = overlaySource.IndexOf(
            "private void ReleasePointerStateForEnvironmentSuppression()",
            StringComparison.Ordinal);
        int releaseEnd = overlaySource.IndexOf(
            "private void ReleasePointerState()",
            releaseStart,
            StringComparison.Ordinal);

        Assert.True(applyStart >= 0 && applyEnd > applyStart);
        Assert.True(releaseStart >= 0 && releaseEnd > releaseStart);

        string applyMethod = overlaySource[applyStart..applyEnd];
        string releaseMethod = overlaySource[releaseStart..releaseEnd];
        Assert.Contains("ReleasePointerStateForEnvironmentSuppression()", applyMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleasePointerStateSilent()", applyMethod, StringComparison.Ordinal);
        Assert.Contains("ResetPrimaryPointerState()", releaseMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("EmitCancel", releaseMethod, StringComparison.Ordinal);

        string adapterSource = ReadSource("src", "Web", "fx-adapter.js");
        int adapterStart = adapterSource.IndexOf(
            "window.setEnvironmentInputSuppressed = function (suppressed)",
            StringComparison.Ordinal);
        int adapterEnd = adapterSource.IndexOf("window.externalCancel", adapterStart, StringComparison.Ordinal);

        Assert.True(adapterStart >= 0 && adapterEnd > adapterStart);

        string adapterMethod = adapterSource[adapterStart..adapterEnd];
        Assert.Contains("window.externalUp()", adapterMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("clearTrail", adapterMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void PointerOverBASparkWindow_DoesNotFallBackToTheFilteredForegroundWindow()
    {
        string overlaySource = ReadSource("src", "OverlayManager.cs");
        int ownWindowCheck = overlaySource.IndexOf(
            "if (IsCurrentProcessWindow(targetWindow))",
            StringComparison.Ordinal);
        int foregroundFallback = overlaySource.IndexOf(
            "if (!TryGetForegroundProcessName(targetWindow, out string processName))",
            StringComparison.Ordinal);
        int helperStart = overlaySource.IndexOf(
            "private static bool IsCurrentProcessWindow(IntPtr hwnd)",
            StringComparison.Ordinal);
        int helperEnd = overlaySource.IndexOf(
            "private static bool IsSuppressedByProcessFilter",
            helperStart,
            StringComparison.Ordinal);

        Assert.True(ownWindowCheck >= 0 && foregroundFallback > ownWindowCheck);
        Assert.True(helperStart >= 0 && helperEnd > helperStart);

        string helper = overlaySource[helperStart..helperEnd];
        Assert.Contains("GetWindowThreadProcessId(hwnd, out uint processId)", helper, StringComparison.Ordinal);
        Assert.Contains("processId == (uint)Environment.ProcessId", helper, StringComparison.Ordinal);

        int mouseMoveStart = overlaySource.IndexOf(
            "private void OnMouseMoveExt(object? sender, MouseEventExtArgs e)",
            StringComparison.Ordinal);
        int mouseMoveEnd = overlaySource.IndexOf(
            "private void OnMouseUpExt",
            mouseMoveStart,
            StringComparison.Ordinal);

        Assert.True(mouseMoveStart >= 0 && mouseMoveEnd > mouseMoveStart);
        string mouseMoveMethod = overlaySource[mouseMoveStart..mouseMoveEnd];
        Assert.Contains("if (!ConfigManager.IsTrailEffectActive)", mouseMoveMethod, StringComparison.Ordinal);
        Assert.Contains("ShouldSuppressEffects();", mouseMoveMethod, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] pathParts) =>
        File.ReadAllText(Path.Combine([FindWorkspaceRoot(), .. pathParts]), Encoding.UTF8);

    private static string FindWorkspaceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BASpark.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the BASpark workspace root.");
    }
}
