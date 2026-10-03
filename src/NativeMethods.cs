using System.Runtime.InteropServices;
using System.Text;

namespace BASpark;

/// <summary>
/// 覆盖层与多屏路由所需的全部 Win32 互操作声明。
/// 集中在此处便于审计，避免各文件重复声明同一 API。
/// </summary>
internal static class NativeMethods
{
    // ---- 窗口样式 -------------------------------------------------------
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const int WS_POPUP = unchecked((int)0x80000000);

    internal const uint LWA_ALPHA = 0x00000002;
    internal const uint LWA_COLORKEY = 0x00000001;

    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_HIDE = 0;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOSENDCHANGING = 0x0400;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;

    // ---- 屏幕捕获排除 ---------------------------------------------------
    internal const uint WDA_NONE = 0x00000000;
    internal const uint WDA_MONITOR = 0x00000001;
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // ---- 显示器枚举 -----------------------------------------------------
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    // ---- 窗口事件钩子 ---------------------------------------------------
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_REORDER = 0x8004;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    // ---- 窗口查询 -------------------------------------------------------
    internal const uint GA_ROOT = 2;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const int CURSOR_SHOWING = 0x0001;
    private static readonly object CursorShapeGate = new();
    private static IntPtr _cachedCursorShape;
    private static bool _cachedCursorShapeVisible;
    private static long _cursorShapeCacheUntil;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        internal readonly int Width => Right - Left;
        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    internal delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    private static readonly IntPtr DefWindowProcPointer =
        GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW");

    internal static IntPtr DefWindowProcPointerValue => DefWindowProcPointer;

    /// <summary>返回窗口类名；失败时返回空字符串。</summary>
    internal static string GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>返回进程可执行文件名（小写，含 .exe）；失败时返回空字符串。</summary>
    internal static string GetProcessExecutableName(uint processId)
    {
        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProc == IntPtr.Zero)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(1024);
        int size = sb.Capacity;
        if (!QueryFullProcessImageName(hProc, 0, sb, ref size))
        {
            CloseHandle(hProc);
            return string.Empty;
        }

        CloseHandle(hProc);
        string fileName = Path.GetFileName(sb.ToString());
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".exe";
        }

        return fileName.ToLowerInvariant();
    }

    /// <summary>光标当前是否可见（触摸输入会隐藏光标）。</summary>
    internal static bool IsCursorVisible()
    {
        var pci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(out pci) || (pci.flags & CURSOR_SHOWING) == 0 || pci.hCursor == IntPtr.Zero)
        {
            lock (CursorShapeGate) _cachedCursorShape = IntPtr.Zero;
            return false;
        }
        lock (CursorShapeGate)
        {
            long now = Environment.TickCount64;
            if (_cachedCursorShape == pci.hCursor && now < _cursorShapeCacheUntil) return _cachedCursorShapeVisible;
            _cachedCursorShapeVisible = HasVisibleCursorShape(pci.hCursor);
            _cachedCursorShape = pci.hCursor;
            _cursorShapeCacheUntil = now + 100;
            return _cachedCursorShapeVisible;
        }
    }

    private static bool HasVisibleCursorShape(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        try
        {
            using var cursor = new System.Windows.Forms.Cursor(handle);
            using var bitmap = new System.Drawing.Bitmap(cursor.Size.Width, cursor.Size.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            var bounds = new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size);
            foreach (System.Drawing.Color background in new[] { System.Drawing.Color.Black, System.Drawing.Color.White })
            {
                graphics.Clear(background);
                cursor.Draw(graphics, bounds);
                var data = bitmap.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var pixels = new int[bitmap.Width * bitmap.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    if (pixels.Any(pixel => (pixel & 0x00FFFFFF) != (background.ToArgb() & 0x00FFFFFF))) return true;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or System.ComponentModel.Win32Exception or ExternalException)
        {
            return true;
        }
    }

    internal static bool TryGetCursorPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!GetCursorPos(out POINT pt))
        {
            return false;
        }

        x = pt.x;
        y = pt.y;
        return true;
    }
}
