using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace BASpark;

/// <summary>
/// WinUI 3 不读取 csproj 的 <c>ApplicationIcon</c> 作为窗口图标，
/// 因此这里从自身可执行文件提取图标并显式设置到窗口句柄上，
/// 让标题栏与 Alt+Tab 使用与安装包一致的图标。
/// </summary>
internal static class WindowChrome
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const uint LR_SHARED = 0x00008000;

    public static void ApplyAppIcon(Window window)
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            string exePath = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            IntPtr smallIcon = LoadImage(
                IntPtr.Zero, exePath, IMAGE_ICON, 16, 16, LR_SHARED);
            if (smallIcon != IntPtr.Zero)
            {
                SendMessage(hwnd, WM_SETICON, ICON_SMALL, smallIcon);
            }

            IntPtr bigIcon = LoadImage(
                IntPtr.Zero, exePath, IMAGE_ICON, 32, 32, LR_SHARED);
            if (bigIcon != IntPtr.Zero)
            {
                SendMessage(hwnd, WM_SETICON, ICON_BIG, bigIcon);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Failed to apply the window icon: {ex.Message}");
        }
    }

    /// <summary>
    /// 去掉窗口边框，并把「拖拽移动 + 四条边缩放」用非客户区命中区域补回来。
    ///
    /// 背景：Windows 10 上 <c>WS_CAPTION</c>（含 <c>WS_DLGFRAME</c>）会给窗口留出
    /// 9px（125% 缩放）的非客户区边框，而 WinUI 3 启用 <c>ExtendsContentIntoTitleBar</c>
    /// 后这圈边框是**玻璃（透明）**的，XAML 内容又只铺满客户区，于是窗口四周会透出
    /// 桌面背景 —— 深色壁纸下就表现为窗口最底部一条黑边，显示/最小化动画里整个窗口
    /// 被缩放时最明显。实测：
    ///   * <c>DWMWA_BORDER_COLOR</c> 在 Windows 10 19045 上不被支持（E_INVALIDARG）；
    ///   * 只调 <c>SetBorderAndTitleBar(false, false)</c> 不改样式，边框依旧 9px；
    ///   * 只清 <c>WS_THICKFRAME</c> 边框仍是 9px（由 <c>WS_CAPTION</c> 决定）；
    ///   * 同时清掉 <c>WS_CAPTION</c> 后客户区与窗口矩形完全重合（边框 0px）—— 但
    ///     系统随之不再把标题栏当作标题栏，拖拽移动失效。
    /// 因此这里清掉这两个样式，再用 <see cref="InputNonClientPointerSource"/>：
    ///   * <c>Caption</c> 区域 = 标题栏控件所在的那一条（右侧给三个按钮留出宽度），
    ///     系统据此继续支持拖拽移动与拖到屏幕边缘贴靠；
    ///   * 四条 <c>*Border</c> 区域 = 四边 6 逻辑像素的缩放抓取带。
    /// <c>DwmExtendFrameIntoClientArea(1,1,1,1)</c> 用来保住无边框窗口的 DWM 阴影。
    /// </summary>
    public static void ApplyBorderlessChrome(
        Window window,
        FrameworkElement dragRegion,
        double gripDesignPixels,
        double captionButtonsDesignWidth)
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
            style &= ~(WsCaption | WsThickFrame);
            _ = SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));

            var margins = new Margins { Left = 1, Top = 1, Right = 1, Bottom = 1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

            _ = SetWindowPos(
                hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

            UpdateNonClientRegions(window, dragRegion, gripDesignPixels, captionButtonsDesignWidth);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to apply borderless chrome: {ex.Message}");
        }
    }

    /// <summary>
    /// 重算拖拽移动与四条缩放边的命中区域。标题栏宽度、窗口右/下边都会随尺寸变化，
    /// 所以每次尺寸变化都要重算。
    /// </summary>
    public static void UpdateNonClientRegions(
        Window window,
        FrameworkElement dragRegion,
        double gripDesignPixels,
        double captionButtonsDesignWidth)
    {
        try
        {
            if (window.AppWindow == null)
            {
                return;
            }

            SizeInt32 size = window.AppWindow.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            double scale = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) / 96.0 : 1.0;
            int grip = Math.Max(4, (int)Math.Round(gripDesignPixels * scale));
            grip = Math.Min(grip, Math.Min(size.Width, size.Height) / 3);

            InputNonClientPointerSource source =
                InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);

            // 标题栏拖拽带：宽度取标题栏控件自身宽度，右侧留出三个系统按钮的位置。
            double dragWidth = dragRegion.ActualWidth - captionButtonsDesignWidth;
            int captionWidth = (int)Math.Round(Math.Max(0, dragWidth) * scale);
            int captionHeight = (int)Math.Round(
                (dragRegion.ActualHeight > 0 ? dragRegion.ActualHeight : 48) * scale);
            source.SetRegionRects(
                NonClientRegionKind.Caption,
                new[] { new RectInt32(0, 0, Math.Min(captionWidth, size.Width), Math.Min(captionHeight, size.Height)) });

            source.SetRegionRects(
                NonClientRegionKind.TopBorder,
                new[] { new RectInt32(0, 0, size.Width, grip) });
            source.SetRegionRects(
                NonClientRegionKind.BottomBorder,
                new[] { new RectInt32(0, size.Height - grip, size.Width, grip) });
            source.SetRegionRects(
                NonClientRegionKind.LeftBorder,
                new[] { new RectInt32(0, 0, grip, size.Height) });
            source.SetRegionRects(
                NonClientRegionKind.RightBorder,
                new[] { new RectInt32(size.Width - grip, 0, grip, size.Height) });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to update non-client regions: {ex.Message}");
        }
    }

    /// <summary>按 DPI 把窗口缩放到指定逻辑尺寸并居中到当前显示器。</summary>
    public static void SetInitialSize(Window window, int logicalWidth, int logicalHeight)
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            uint dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 96;
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;

            if (window.AppWindow == null)
            {
                return;
            }

            int width = (int)Math.Round(logicalWidth * scale);
            int height = (int)Math.Round(logicalHeight * scale);

            Microsoft.UI.Windowing.DisplayArea area =
                Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    window.AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

            var size = new Windows.Graphics.SizeInt32 { Width = width, Height = height };
            window.AppWindow.Resize(size);

            var center = new Windows.Graphics.PointInt32
            {
                X = area.WorkArea.X + Math.Max(0, (area.WorkArea.Width - width) / 2),
                Y = area.WorkArea.Y + Math.Max(0, (area.WorkArea.Height - height) / 2)
            };
            window.AppWindow.Move(center);
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Failed to size the window: {ex.Message}");
        }
    }

    /// <summary>
    /// 让标题栏跟随深浅色主题。
    ///
    /// Windows 11 上 WinUI 的 <c>AppWindowTitleBar</c> 已能处理；但 Windows 10
    /// 的 <c>AppWindowTitleBar.IsCustomizationSupported()</c> 返回 false，
    /// 需要退回 DWM 的沉浸式深色模式（属性 20，旧版 19）。
    /// </summary>
    public static void ApplyTitleBarTheme(Window window, bool isDark)
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int value = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
            }

            // 让非客户区（标题栏按钮、边框）使用深色资源，Win10 需要显式指定。
            _ = SetWindowTheme(hwnd, isDark ? "DarkMode_Explorer" : "Explorer", null);

            // 立即刷新非客户区，避免主题切换后残留旧配色。
            _ = SetWindowPos(
                hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Failed to apply the title bar theme: {ex.Message}");
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
