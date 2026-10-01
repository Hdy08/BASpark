using System.Runtime.InteropServices;

namespace BASpark;

/// <summary>
/// WinUI 3 的合成器窗口无法启用 <c>WS_EX_LAYERED</c>（顶层窗口带
/// <c>WS_EX_NOREDIRECTIONBITMAP</c>，<c>SetLayeredWindowAttributes</c> 无效），
/// 因此特效叠加层不使用 XAML <c>Window</c>，而是直接创建一个原生 Win32
/// 分层窗口来承载 WebView2。这样可以在单进程内同时得到：
///   * 每像素 alpha 透明（WebView2 默认背景透明 + 分层窗口合成）
///   * 鼠标穿透（<c>WS_EX_TRANSPARENT</c>）
///   * 不抢焦点、不出现在 Alt+Tab（<c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c>）
/// </summary>
internal sealed class LayeredWindowHost : IDisposable
{
    private const string WindowClassName = "BASparkOverlayHost";

    private static readonly object ClassGate = new();
    private static bool _classRegistered;

    // 必须持有委托实例，否则 GC 会回收 WndProc 导致原生侧悬挂调用。
    private static readonly IntPtr DefWindowProc = NativeMethods.DefWindowProcPointerValue;

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _layeredStyleApplied;
    private bool _disposed;

    // 上一次实际下发的窗口矩形。叠加层每 5 秒会重新断言一次置顶，若每次都对全屏
    // 分层窗口做一次 SetWindowPos，就会顺带触发整屏重新合成 —— 拖动别的窗口时正好
    // 撞上就会卡一下。位置尺寸没变时只刷新 DPI，不再重复下发。
    private bool _boundsApplied;
    private int _boundsLeft;
    private int _boundsTop;
    private int _boundsWidth;
    private int _boundsHeight;

    public IntPtr Handle => _hwnd;

    public double DpiScale { get; private set; } = 1.0;

    public LayeredWindowHost()
    {
        EnsureWindowClassRegistered();

        // 创建时不带 WS_EX_LAYERED：窗口以隐藏状态建立，Show() 时再补上样式。
        int exStyle = NativeMethods.WS_EX_TRANSPARENT
                    | NativeMethods.WS_EX_TOOLWINDOW
                    | NativeMethods.WS_EX_NOACTIVATE;

        IntPtr hInstance = NativeMethods.GetModuleHandle(null);
        _hwnd = NativeMethods.CreateWindowEx(
            exStyle,
            WindowClassName,
            "BASparkOverlay",
            NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"叠加层宿主窗口创建失败，Win32 错误码 {Marshal.GetLastWin32Error()}。");
        }

        DpiScale = ReadDpiScale();
    }

    private static void EnsureWindowClassRegistered()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = DefWindowProc,
                hInstance = NativeMethods.GetModuleHandle(null),
                lpszClassName = WindowClassName
            };

            if (NativeMethods.RegisterClassEx(ref wc) == 0)
            {
                // ERROR_CLASS_ALREADY_EXISTS (1410) 表示另一个实例已注册，可安全复用。
                int error = Marshal.GetLastWin32Error();
                const int ErrorClassAlreadyExists = 1410;
                if (error != ErrorClassAlreadyExists)
                {
                    throw new InvalidOperationException(
                        $"叠加层窗口类注册失败，Win32 错误码 {error}。");
                }
            }

            _classRegistered = true;
        }
    }

    /// <summary>
    /// 按物理像素定位到指定显示器。坐标来自 <c>rcMonitor</c>，与 DPI 无关，
    /// 所以必须走 SetWindowPos 而不是 XAML 布局。
    /// </summary>
    public void SetBounds(int left, int top, int width, int height)
    {
        if (_hwnd == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        // 尺寸位置没变就不做 SetWindowPos：全屏分层窗口的重复定位会连带整屏重新合成。
        if (_boundsApplied
            && _boundsLeft == left && _boundsTop == top
            && _boundsWidth == width && _boundsHeight == height)
        {
            DpiScale = ReadDpiScale();
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, left, top, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);

        _boundsApplied = true;
        _boundsLeft = left;
        _boundsTop = top;
        _boundsWidth = width;
        _boundsHeight = height;

        DpiScale = ReadDpiScale();
    }

    /// <summary>把窗口抬到 Z 序顶端。启动阶段与恢复阶段都会被调用。</summary>
    public void SetTopmost()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING);
    }

    public void Show()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        ApplyLayeredStyle();
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        SetTopmost();
    }

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    public bool IsVisible => _hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(_hwnd);

    /// <summary>
    /// 分层样式必须在窗口可见期间保持。bAlpha=255 表示不做整体淡化，
    /// 因此每个像素的 alpha 完全由 WebView2 的合成结果决定。
    /// </summary>
    private void ApplyLayeredStyle()
    {
        if (_hwnd == IntPtr.Zero || _layeredStyleApplied)
        {
            return;
        }

        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_LAYERED);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);
        _layeredStyleApplied = true;
    }

    /// <summary>把叠加层排除在系统截图之外（截图兼容模式）。</summary>
    public void ApplyCaptureExclusion(bool excludeFromCapture)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        uint affinity = excludeFromCapture
            ? NativeMethods.WDA_EXCLUDEFROMCAPTURE
            : NativeMethods.WDA_NONE;

        if (!NativeMethods.SetWindowDisplayAffinity(_hwnd, affinity) && excludeFromCapture)
        {
            // 旧版系统不支持 EXCLUDEFROMCAPTURE 时退回整窗涂黑。
            NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_MONITOR);
        }
    }

    public bool TryGetWindowRect(out NativeMethods.RECT rect)
    {
        rect = default;
        return _hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out rect);
    }

    public bool TryGetOwningMonitorBounds(out NativeMethods.RECT monitorBounds, out string deviceName)
    {
        monitorBounds = default;
        deviceName = string.Empty;

        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        monitorBounds = info.rcMonitor;
        deviceName = info.szDevice;
        return true;
    }

    private double ReadDpiScale()
    {
        if (!TryGetOwningMonitorBounds(out _, out _))
        {
            return DpiScale;
        }

        try
        {
            uint dpi = GetDpiForWindow(_hwnd);
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Windows 10 1607 之前没有 GetDpiForWindow，保持上一次的比例。
        }

        return DpiScale;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            // 先解除置顶与捕获排除，避免销毁阶段残留状态。
            NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_NONE);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
