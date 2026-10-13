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
    internal const int WS_DISABLED = 0x08000000;

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

    // ---- 显示器枚举 -----------------------------------------------------
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    // ---- 窗口事件钩子 ---------------------------------------------------
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_REORDER = 0x8004;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    internal const int WH_MOUSE_LL = 14;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_MBUTTONDOWN = 0x0207;
    internal const uint WM_MBUTTONUP = 0x0208;
    internal const uint WM_XBUTTONUP = 0x020C;
    private const uint TouchInputSignature = 0xFF515700;
    private const uint TouchInputSignatureMask = 0xFFFFFF00;
    private const uint TouchInputFlag = 0x80;

    // ---- 窗口查询 -------------------------------------------------------
    internal const uint GA_ROOT = 2;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const int CURSOR_SHOWING = 0x0001;
    internal const int CURSOR_SUPPRESSED = 0x0002;
    private static readonly object CursorShapeGate = new();
    private static IntPtr _cachedCursorShape;
    private static bool _cachedCursorShapeVisible;
    private static long _cursorShapeCacheUntil;
    private static IntPtr _cachedTouchGestureCursor;
    private static bool _cachedTouchGestureCursorRecognized;
    private static long _touchGestureCursorCacheUntil;

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
    private struct ICONINFOEX
    {
        public int cbSize;
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
        public ushort wResID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? szModName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? szResName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr Parameter;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct HIDP_CAPS
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportByteLength;
        [FieldOffset(46)] public ushort NumberInputButtonCaps;
        [FieldOffset(48)] public ushort NumberInputValueCaps;
        [FieldOffset(50)] public ushort NumberInputDataIndices;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
        [FieldOffset(68)] public ushort DataIndexMin;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct HIDP_BUTTON_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
        [FieldOffset(68)] public ushort DataIndexMin;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_DATA
    {
        public ushort DataIndex;
        public ushort Reserved;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct POINTER_DEVICE_INFO
    {
        public uint DisplayOrientation;
        public IntPtr Device;
        public uint Type;
        public IntPtr Monitor;
        public uint StartingCursorId;
        public ushort MaxActiveContacts;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 520)] public string ProductString;
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

    internal delegate IntPtr MouseHookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hookId, MouseHookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(IntPtr input, uint command, [Out] byte[]? data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, [Out] byte[]? data, ref uint size);

    [DllImport("user32.dll")]
    internal static extern bool GetPointerDeviceRects(IntPtr device, out RECT deviceRect, out RECT displayRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetPointerDevice(IntPtr device, out POINTER_DEVICE_INFO info);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(byte[] preparsed, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort count, byte[] preparsed);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetButtonCaps(int reportType, [Out] HIDP_BUTTON_CAPS[] caps, ref ushort count, byte[] preparsed);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetData(int reportType, [Out] HIDP_DATA[] data, ref uint count, byte[] preparsed, byte[] report, uint length);

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
    internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

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

    [DllImport("user32.dll", EntryPoint = "GetIconInfoExW", CharSet = CharSet.Unicode)]
    private static extern bool GetIconInfoEx(IntPtr cursor, ref ICONINFOEX info);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr bitmap);

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

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr process);

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

    internal static bool IsTouchInput(UIntPtr extraInfo)
    {
        uint signature = unchecked((uint)extraInfo.ToUInt64());
        return (signature & TouchInputSignatureMask) == TouchInputSignature
            && (signature & TouchInputFlag) != 0;
    }

    /// <summary>光标当前是否可见（触摸输入会隐藏光标）。</summary>
    internal static bool IsCursorVisible(bool allowTouchSuppression = false)
    {
        var pci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(out pci))
        {
            lock (CursorShapeGate) _cachedCursorShape = IntPtr.Zero;
            return false;
        }
        return IsCursorVisible(pci, allowTouchSuppression);
    }

    internal static bool IsCursorVisible(CURSORINFO pci, bool allowTouchSuppression)
    {
        bool suppressed = (pci.flags & CURSOR_SUPPRESSED) != 0;
        if (pci.hCursor == IntPtr.Zero || (suppressed ? !allowTouchSuppression : (pci.flags & CURSOR_SHOWING) == 0))
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

    internal static bool IsSystemTouchGestureCursor(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        lock (CursorShapeGate)
        {
            long now = Environment.TickCount64;
            if (_cachedTouchGestureCursor == handle && now < _touchGestureCursorCacheUntil)
                return _cachedTouchGestureCursorRecognized;
            var info = new ICONINFOEX { cbSize = Marshal.SizeOf<ICONINFOEX>() };
            try
            {
                _cachedTouchGestureCursorRecognized = GetIconInfoEx(handle, ref info) && info.wResID == 1
                    && string.Equals(info.szModName, Path.Combine(Environment.SystemDirectory, "NInput.dll"), StringComparison.OrdinalIgnoreCase);
                _cachedTouchGestureCursor = handle;
                _touchGestureCursorCacheUntil = now + 100;
                return _cachedTouchGestureCursorRecognized;
            }
            finally
            {
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            }
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

internal sealed class TouchCursorState
{
    private readonly Dictionary<IntPtr, bool> _windows = new();
    private readonly Dictionary<long, (IntPtr Window, bool Chromium)> _contacts = new();

    internal void Observe(NativeMethods.CURSORINFO cursor, IntPtr window, bool touchInput = false)
    {
        if ((cursor.flags & NativeMethods.CURSOR_SUPPRESSED) != 0
            || (touchInput && (cursor.flags & NativeMethods.CURSOR_SHOWING) != 0
                && NativeMethods.IsSystemTouchGestureCursor(cursor.hCursor))) return;
        Remember(window, NativeMethods.IsCursorVisible(cursor, false));
    }

    internal void BeginContact(IntPtr window, bool chromium, long pointerId = 1)
    {
        _contacts[pointerId] = (window, chromium);
    }

    internal void EndContact(long pointerId = 1)
    {
        _contacts.Remove(pointerId);
    }

    internal bool IsVisible(NativeMethods.CURSORINFO cursor, IntPtr window, bool chromium, long pointerId = 1)
    {
        var contact = _contacts.GetValueOrDefault(pointerId);
        if (contact.Window != IntPtr.Zero && contact.Chromium)
        {
            window = contact.Window;
            chromium = true;
        }
        if (contact.Window != IntPtr.Zero && (cursor.flags & (NativeMethods.CURSOR_SHOWING | NativeMethods.CURSOR_SUPPRESSED)) != 0
            && (!_windows.TryGetValue(contact.Window, out bool originVisible) || originVisible)
            && (!_windows.TryGetValue(window, out bool targetVisible) || targetVisible)
            && (NativeMethods.IsSystemTouchGestureCursor(cursor.hCursor)
                || (originVisible && (cursor.flags & NativeMethods.CURSOR_SUPPRESSED) != 0 && cursor.hCursor == IntPtr.Zero)))
        {
            Remember(window, true);
            return true;
        }
        if (chromium && window != IntPtr.Zero
            && ((cursor.flags & NativeMethods.CURSOR_SUPPRESSED) != 0
                || (window == contact.Window && contact.Chromium && cursor.hCursor == IntPtr.Zero)))
        {
            bool allowed = !_windows.TryGetValue(window, out bool previous) || previous;
            if (cursor.hCursor != IntPtr.Zero) allowed &= NativeMethods.IsCursorVisible(cursor, true);
            Remember(window, allowed);
            return allowed;
        }
        bool visible = NativeMethods.IsCursorVisible(cursor, true);
        if (chromium || contact.Window != IntPtr.Zero) Remember(window, visible);
        return visible;
    }

    private void Remember(IntPtr window, bool visible)
    {
        if (window == IntPtr.Zero) return;
        if (_windows.Count >= 128 && !_windows.ContainsKey(window)) _windows.Remove(_windows.Keys.First());
        _windows[window] = visible;
    }

    internal void Reset()
    {
        _windows.Clear();
        _contacts.Clear();
    }
}
