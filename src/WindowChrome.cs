using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

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
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

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
