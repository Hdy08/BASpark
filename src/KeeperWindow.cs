using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace BASpark;

/// <summary>
/// 一个 1×1、不可见、不在任务栏与 Alt+Tab 中出现的常驻窗口。
///
/// WinUI 3 没有 WPF 的 <c>ShutdownMode</c>：最后一个窗口关闭时应用会自动退出。
/// 但 BASpark 需要常驻托盘，且启动阶段（语言/隐私对话框）与叠加层运行期间
/// 都可能没有任何可见窗口，因此必须始终保留一个窗口让消息循环存活，
/// 真正的退出只走 <see cref="App.ExitApplication"/>。
/// </summary>
internal sealed class KeeperWindow
{
    private Window? _window;

    public void EnsureCreated()
    {
        if (_window != null)
        {
            return;
        }

        var content = new Grid { Width = 1, Height = 1, Opacity = 0 };
        _window = new Window { Content = content };

        if (_window.AppWindow != null)
        {
            _window.AppWindow.IsShownInSwitchers = false;
            _window.AppWindow.Resize(new SizeInt32 { Width = 1, Height = 1 });

            if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }

        _window.Activate();

        // 移出屏幕，避免 1×1 窗口在桌面角落闪现。
        if (_window.AppWindow != null)
        {
            _window.AppWindow.Move(new PointInt32 { X = -32000, Y = -32000 });
        }
    }

    public void Destroy()
    {
        try
        {
            _window?.Close();
        }
        catch
        {
            // 退出阶段尽力清理即可。
        }

        _window = null;
    }
}
