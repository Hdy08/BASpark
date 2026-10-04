using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace BASpark;

/// <summary>
/// 控制面板的窗口宿主：自己创建带 <c>WS_EX_NOREDIRECTIONBITMAP</c> 的 Win32 窗口，
/// 再把 XAML 内容用 <see cref="DesktopWindowXamlSource"/> 放进去。
///
/// 为什么不直接用 WinUI 的 <see cref="Window"/>（实测对比参照程序 config.exe）：
///   * WinUI 窗口走**重定向表面**：合成输出落后窗口边框约 130ms（注入式圆形高速拖动
///     实测漂移约 228px），快速调整宽度时标题栏按钮会跑出窗口；
///   * 最小化/显示动画期间 DWM 缩放的是那张空的重定向表面 → 整块黑；
///   * 无重定向表面的窗口（config.exe 即如此，EXSTYLE=0x00200100）实测漂移仅 8px、
///     内容滞后约 1 帧，动画期间 DWM 直接缩放**实时内容**、不再有黑块。
///
/// 窗口样式保留 <c>WS_CAPTION</c>：DWM 的最小化/还原动画依赖它（清掉动画就没了），
/// 非客户区则由 <see cref="WmNcCalcSize"/> 抹平，客户区铺满整窗；拖拽移动与四边缩放
/// 改用 <see cref="InputNonClientPointerSource"/> 的命中区域交还给系统模态循环处理。
/// </summary>
internal sealed class DcompPanelHost : IDisposable
{
    private const string WindowClassName = "BASparkPanelHost";

    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsSysMenu = 0x00080000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsExNoRedirectionBitmap = 0x00200000;

    private const int WmSize = 0x0005;
    private const int WmShowWindow = 0x0018;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmSetFocus = 0x0007;
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;
    private const int WmNcDestroy = 0x0082;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmNcCalcSize = 0x0083;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmDpiChanged = 0x02E0;
    private const int DwmwaCloaked = 14;

    /// <summary>标题栏拖拽带高度（逻辑像素）与四边缩放抓取宽度。</summary>
    private const int CaptionHeight = 32;
    private const int ResizeGrip = 6;

    /// <summary>窗口最小尺寸（逻辑像素），与设计下限一致。</summary>
    private const int MinWidthDesign = 800;
    private const int MinHeightDesign = 560;

    private static bool _classRegistered;
    private static WndProcDelegate? _wndProc;

    private readonly DesktopWindowXamlSource _xamlSource;
    private readonly AppWindow _appWindow;
    private IntPtr _hwnd;
    private RectInt32 _captionButtonsBounds;
    private bool _renderClockActive;
    private readonly WindowChrome.RenderClock _renderClock = new();
    private IntPtr _previousForegroundWindow;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    public DcompPanelHost()
    {
        EnsureClassRegistered();

        int style = WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox
                    | WsClipSiblings;

        _hwnd = CreateWindowEx(
            WsExNoRedirectionBitmap,
            WindowClassName,
            string.Empty,
            style,
            0, 0, MinWidthDesign, MinHeightDesign,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"控制面板宿主窗口创建失败，Win32 错误码 {Marshal.GetLastWin32Error()}。");
        }

        DesktopWindowXamlSource? xamlSource = null;
        try
        {
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            xamlSource = new DesktopWindowXamlSource();
            xamlSource.Initialize(windowId);
            _xamlSource = xamlSource;
            _xamlSource.TakeFocusRequested += static (sender, args) =>
            {
                XamlSourceFocusNavigationReason reason = args.Request.Reason;
                if (reason is XamlSourceFocusNavigationReason.First or XamlSourceFocusNavigationReason.Last)
                {
                    sender.NavigateFocus(new XamlSourceFocusNavigationRequest(reason));
                }
            };
        }
        catch
        {
            xamlSource?.Dispose();
            _ = DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            throw;
        }

        Instances[_hwnd] = this;
        UpdateXamlIslandBounds();
    }

    /// <summary>宿主窗口句柄。</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>宿主窗口对应的 AppWindow（尺寸、最小尺寸、最小化/最大化都走它）。</summary>
    public AppWindow? AppWindow =>
        _hwnd != IntPtr.Zero ? _appWindow : null;

    public bool IsMaximized => _hwnd != IntPtr.Zero && IsZoomed(_hwnd);

    /// <summary>用户关窗（标题栏关闭按钮）时触发；宿主只隐藏窗口，不销毁。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>窗口尺寸变化后触发（用于重算命中区域）。</summary>
    public event EventHandler? SizeChanged;

    /// <summary>设置 XAML 内容。</summary>
    public void SetContent(UIElement content)
    {
        _xamlSource.Content = content;
        UpdateXamlIslandBounds();
    }

    /// <summary>设置窗口背景（系统背景，用于最小化/还原动画期间不出现黑块）。</summary>
    public void SetBackdrop(SystemBackdrop? backdrop)
    {
        _xamlSource.SystemBackdrop = backdrop;
    }

    public void SetTitle(string title)
    {
        if (_hwnd != IntPtr.Zero)
        {
            _ = SetWindowText(_hwnd, title);
        }
    }

    public void Show()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (CanActivateExternalWindow(foreground)) _previousForegroundWindow = foreground;
        else if (!CanActivateExternalWindow(_previousForegroundWindow)) _previousForegroundWindow = FindExternalWindow();
        SetRenderClockActive(true);
        _ = ShowWindow(_hwnd, IsIconic(_hwnd) ? SW_RESTORE : SW_SHOW);
        _ = SetForegroundWindow(_hwnd);
    }

    public void Minimize()
    {
        if (AppWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
            SetRenderClockActive(false);
        }
    }

    public void ToggleMaximize()
    {
        if (AppWindow?.Presenter is OverlappedPresenter presenter)
        {
            if (IsMaximized)
            {
                presenter.Restore();
            }
            else
            {
                presenter.Maximize();
            }
        }
    }

    public void SetCaptionButtonsBounds(RectInt32 bounds)
    {
        _captionButtonsBounds = bounds;
        UpdateNonClientRegions();
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero)
        {
            RestoreExternalForeground();
            _ = ShowWindow(_hwnd, SW_HIDE);
            SetRenderClockActive(false);
            RestoreExternalForeground();
        }
    }

    private static bool CanActivateExternalWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window) || !IsWindowVisible(window) ||
            IsIconic(window) || !IsWindowEnabled(window) ||
            (NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_NOACTIVATE) != 0)
            return false;
        _ = NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || processId == Environment.ProcessId) return false;
        return DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static IntPtr FindExternalWindow()
    {
        IntPtr target = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!CanActivateExternalWindow(window) ||
                (NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;
            target = window;
            return false;
        }, IntPtr.Zero);
        return target;
    }

    private void RestoreExternalForeground()
    {
        _ = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint processId);
        if (processId != Environment.ProcessId) return;
        IntPtr target = CanActivateExternalWindow(_previousForegroundWindow) ? _previousForegroundWindow : FindExternalWindow();
        if (target != IntPtr.Zero) _ = SetForegroundWindow(target);
    }

    /// <summary>按 DPI 把窗口居中到当前显示器。</summary>
    public void CenterOnCurrentDisplay(int designWidth, int designHeight)
    {
        AppWindow? appWindow = AppWindow;
        if (appWindow == null)
        {
            return;
        }

        double scale = GetDpiScale();
        int width = (int)Math.Round(designWidth * scale);
        int height = (int)Math.Round(designHeight * scale);

        DisplayArea? area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
        if (area != null)
        {
            RectInt32 work = area.WorkArea;
            int x = work.X + Math.Max(0, (work.Width - width) / 2);
            int y = work.Y + Math.Max(0, (work.Height - height) / 2);
            appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }

        UpdateNonClientRegions();
    }

    /// <summary>
    /// 重算拖拽/缩放命中区域。窗口尺寸或 DPI 变化后必须调用，
    /// 否则标题栏拖拽带与右/下缩放边会停在旧位置。
    /// </summary>
    public void UpdateNonClientRegions()
    {
        if (_hwnd == IntPtr.Zero || IsIconic(_hwnd) || !GetClientRect(_hwnd, out RECT rect))
        {
            return;
        }

        double scale = GetDpiScale();
        int caption = Math.Max(1, (int)Math.Round(CaptionHeight * scale));
        int grip = Math.Max(2, (int)Math.Round(ResizeGrip * scale));

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int captionWidth = _captionButtonsBounds.Width > 0
            ? Math.Clamp(_captionButtonsBounds.X, 0, width)
            : width;

        try
        {
            InputNonClientPointerSource source =
                InputNonClientPointerSource.GetForWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

            // 标题栏拖拽带交给系统（拖动走系统模态循环，与普通窗口一致）。
            source.SetRegionRects(
                NonClientRegionKind.Caption,
                [new RectInt32(0, 0, captionWidth, Math.Min(caption, height))]);
            source.SetRegionRects(
                NonClientRegionKind.Passthrough,
                _captionButtonsBounds.Width > 0 && _captionButtonsBounds.Height > 0
                    ? [_captionButtonsBounds]
                    : []);
            source.SetRegionRects(NonClientRegionKind.TopBorder, IsMaximized ? [] : [new RectInt32(0, 0, width, grip)]);
            source.SetRegionRects(NonClientRegionKind.BottomBorder, IsMaximized ? [] : [new RectInt32(0, height - grip, width, grip)]);
            source.SetRegionRects(NonClientRegionKind.LeftBorder, IsMaximized ? [] : [new RectInt32(0, 0, grip, height)]);
            source.SetRegionRects(NonClientRegionKind.RightBorder, IsMaximized ? [] : [new RectInt32(width - grip, 0, grip, height)]);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to update panel host regions: {ex.Message}");
        }
    }

    private double GetDpiScale()
    {
        uint dpi = _hwnd != IntPtr.Zero ? GetDpiForWindow(_hwnd) : 96;
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
        {
            return;
        }

        _wndProc = WindowProc;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0003,   // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, 32512),   // IDC_ARROW
            lpszClassName = WindowClassName
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            AppLogger.Warn($"RegisterClassEx failed for the panel host: {Marshal.GetLastWin32Error()}");
        }

        _classRegistered = true;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmNcCalcSize:
                // 客户区铺满整窗：非客户区（Windows 10 上是 9px 玻璃边框 + 顶部 1px）
                // 因此完全消失。保持建议的窗口矩形不变并返回 0 即表示整窗为客户区。
                if (IsZoomed(hwnd))
                {
                    // 最大化时窗口矩形比工作区各方向大一圈（约 9px 的不可见边框），
                    // 客户区若照搬窗口矩形，内容会顶出工作区被裁掉。这里改用监视器工作区。
                    RECT proposed = Marshal.PtrToStructure<RECT>(lParam);
                    IntPtr monitor = MonitorFromRect(ref proposed, MonitorDefaultToNearest);
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                    {
                        Marshal.StructureToPtr(info.rcWork, lParam, fDeleteOld: false);
                    }
                }

                return IntPtr.Zero;

            case WmGetMinMaxInfo:
                ApplyMinMaxInfo(hwnd, lParam);
                return IntPtr.Zero;

            case WmSize:
                InstanceOf(hwnd)?.OnWindowSizeChanged();
                return IntPtr.Zero;

            case WmShowWindow:
                InstanceOf(hwnd)?.SetRenderClockActive(wParam != IntPtr.Zero && !IsIconic(hwnd));
                break;

            case WmWindowPosChanged:
                InstanceOf(hwnd)?.SetRenderClockActive(IsWindowVisible(hwnd) && !IsIconic(hwnd));
                break;

            case WmSetFocus:
                InstanceOf(hwnd)?._xamlSource.NavigateFocus(
                    new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.Restore));
                return IntPtr.Zero;

            case WmDpiChanged:
                // 按系统建议的新矩形迁移，并重算命中区域。
                if (Marshal.PtrToStructure<RECT>(lParam) is RECT suggested)
                {
                    _ = SetWindowPos(
                        hwnd, IntPtr.Zero,
                        suggested.Left, suggested.Top,
                        suggested.Right - suggested.Left, suggested.Bottom - suggested.Top,
                        SwpNoZOrder | SwpNoActivate);
                }

                InstanceOf(hwnd)?.UpdateNonClientRegions();
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return new IntPtr(1);

            case WmClose:
                // 关闭按钮 = 隐藏面板（应用继续驻留托盘）。
                InstanceOf(hwnd)?.RequestClose();
                return IntPtr.Zero;

            case WmDestroy:
                return IntPtr.Zero;

            case WmNcDestroy:
                Instances.Remove(hwnd);
                break;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static void ApplyMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            info.ptMinTrackSize.X = (int)Math.Round(MinWidthDesign * scale);
            info.ptMinTrackSize.Y = (int)Math.Round(MinHeightDesign * scale);
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Failed to apply min track size: {ex.Message}");
        }
    }

    // 窗口句柄到宿主的映射：窗口过程是静态的，用属性表找回实例。
    private static readonly Dictionary<IntPtr, DcompPanelHost> Instances = [];

    private static DcompPanelHost? InstanceOf(IntPtr hwnd) =>
        Instances.TryGetValue(hwnd, out DcompPanelHost? host) ? host : null;

    private void SetRenderClockActive(bool active)
    {
        if (_renderClockActive == active) return;
        _renderClock.SetActive(active);
        _renderClockActive = _renderClock.IsActive;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    private void OnWindowSizeChanged()
    {
        SetRenderClockActive(IsWindowVisible(_hwnd) && !IsIconic(_hwnd));
        if (IsIconic(_hwnd))
        {
            return;
        }

        UpdateXamlIslandBounds();
        UpdateNonClientRegions();
        SizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 让 XAML 岛跟随窗口客户区。
    ///
    /// <see cref="DesktopWindowXamlSource"/> 只在初始化时量一次尺寸，窗口后续的缩放
    /// （居中、拖边、最大化）不会自动同步；不补这一步，界面会停在创建时的大小，
    /// 右/下多出来的部分因为没有重定向表面而变成透明区（透出后面的窗口）。
    /// </summary>
    private void UpdateXamlIslandBounds()
    {
        if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out RECT client))
        {
            return;
        }

        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        try
        {
            _xamlSource.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to resize the XAML island: {ex.Message}");
        }
    }

    /// <summary>关闭面板：隐藏窗口并通知调用方（应用继续驻留托盘，不销毁窗口）。</summary>
    public void RequestClose()
    {
        Hide();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        IntPtr hwnd = _hwnd;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        RestoreExternalForeground();
        SetRenderClockActive(false);
        _renderClock.Dispose();
        CloseRequested = null;
        SizeChanged = null;
        Instances.Remove(hwnd);
        _hwnd = IntPtr.Zero;
        try
        {
            _xamlSource.Content = null;
            _xamlSource.Dispose();
        }
        finally
        {
            _ = DestroyWindow(hwnd);
            RestoreExternalForeground();
        }
    }

    // ------------------------------------------------------------------
    // Win32 互操作
    // ------------------------------------------------------------------

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>MONITOR_DEFAULTTONEAREST。</summary>
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    // 显式指定 W 版本入口点：中文标题经默认封送会被截成单个字符。
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private delegate bool EnumWindowsDelegate(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hwnd, string text);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
