using Microsoft.UI.Windowing;

namespace BASpark;

/// <summary>
/// 启动阶段（控制面板尚未创建）显示语言选择与隐私政策窗口。
///
/// 这两个窗口以普通 WinUI 3 <see cref="Microsoft.UI.Xaml.Window"/> 实现而非 ContentDialog，
/// 因为 ContentDialog 需要一个已加载的 <c>XamlRoot</c>，而启动阶段还没有任何窗口。
/// 两者都返回可等待的对话框契约；<c>XamlRoot</c> 传 <c>null</c> 也能正常显示与定尺寸。
/// </summary>
internal static class StartupDialogHost
{
    public static async Task<string?> AskLanguageAsync()
    {
        var window = CreateDialog(() => new LanguageSelectWindow());
        if (window == null)
        {
            return null;
        }

        return await window.ShowDialogAsync(null!);
    }

    public static async Task<bool> AskPrivacyAsync()
    {
        var window = CreateDialog(() => new PrivacyWindow());
        if (window == null)
        {
            return false;
        }

        return await window.ShowDialogAsync(null!);
    }

    private static T? CreateDialog<T>(Func<T> factory)
        where T : Microsoft.UI.Xaml.Window
    {
        try
        {
            T window = factory();

            // 启动对话框不应出现在任务栏，也不应可调整大小。
            if (window.AppWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            if (window.AppWindow != null)
            {
                window.AppWindow.IsShownInSwitchers = false;
            }

            WindowChrome.ApplyAppIcon(window);
            return window;
        }
        catch (Exception ex)
        {
            AppLogger.Error("启动对话框初始化失败。", ex);
            return null;
        }
    }
}
