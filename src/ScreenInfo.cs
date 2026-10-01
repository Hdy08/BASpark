using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BASpark;

/// <summary>框架无关的显示器描述（替代 System.Windows.Forms.Screen）。</summary>
public sealed record ScreenInfo(string DeviceName, int BoundsLeft, int BoundsTop, int BoundsWidth, int BoundsHeight)
{
    // MONITOR_DEFAULTTONEAREST
    private const uint MonitorDefaultToNearest = 2;

    /// <summary>
    /// 判断屏幕坐标是否落在该显示器矩形内。
    /// 与 System.Drawing.Rectangle.Contains 一致：左/上边界包含，右/下边界不包含。
    /// </summary>
    public bool ContainsPoint(int x, int y)
    {
        return x >= BoundsLeft &&
               x < BoundsLeft + BoundsWidth &&
               y >= BoundsTop &&
               y < BoundsTop + BoundsHeight;
    }

    /// <summary>
    /// 返回包含指定点的显示器；与 Windows 自身行为一致，无显示器包含该点时返回最近的显示器。
    /// 仅当系统确实没有显示器时返回 null。
    /// </summary>
    public static ScreenInfo? FromPoint(int x, int y)
    {
        try
        {
            // 使用 MONITOR_DEFAULTTONEAREST，与 Screen.FromPoint 的最近显示器语义一致
            IntPtr monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MonitorDefaultToNearest);
            ScreenInfo? nearest = TryDescribeMonitor(monitor);
            if (nearest != null)
            {
                return nearest;
            }
        }
        catch
        {
            // 忽略 Win32 失败，回退到下面的线性扫描
        }

        IReadOnlyList<ScreenInfo> screens = AllScreens;
        if (screens.Count == 0)
        {
            return null;
        }

        foreach (ScreenInfo screen in screens)
        {
            if (screen.ContainsPoint(x, y))
            {
                return screen;
            }
        }

        // 线性扫描也没有命中时，按矩形距离取最近的一个，保证有显示器就不会返回 null
        ScreenInfo closest = screens[0];
        long closestDistance = DistanceSquared(closest, x, y);
        for (int index = 1; index < screens.Count; index++)
        {
            ScreenInfo candidate = screens[index];
            long distance = DistanceSquared(candidate, x, y);
            if (distance < closestDistance)
            {
                closest = candidate;
                closestDistance = distance;
            }
        }

        return closest;
    }

    /// <summary>当前所有显示器，每次访问实时枚举（与 Screen.AllScreens 一致），失败时返回空列表而不抛出。</summary>
    public static IReadOnlyList<ScreenInfo> AllScreens
    {
        get
        {
            var screens = new List<ScreenInfo>();
            try
            {
                // 委托先保存在局部变量里，避免枚举过程中被回收
                MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                {
                    ScreenInfo? screen = TryDescribeMonitor(hMonitor);
                    if (screen != null)
                    {
                        screens.Add(screen);
                    }

                    return true;
                };

                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            }
            catch
            {
                // 枚举失败时返回空列表，避免影响调用方
            }

            return screens;
        }
    }

    private static long DistanceSquared(ScreenInfo screen, int x, int y)
    {
        long dx = 0;
        if (x < screen.BoundsLeft)
        {
            dx = screen.BoundsLeft - x;
        }
        else if (x >= screen.BoundsLeft + screen.BoundsWidth)
        {
            dx = x - (screen.BoundsLeft + screen.BoundsWidth - 1);
        }

        long dy = 0;
        if (y < screen.BoundsTop)
        {
            dy = screen.BoundsTop - y;
        }
        else if (y >= screen.BoundsTop + screen.BoundsHeight)
        {
            dy = y - (screen.BoundsTop + screen.BoundsHeight - 1);
        }

        return (dx * dx) + (dy * dy);
    }

    private static ScreenInfo? TryDescribeMonitor(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            MONITORINFOEX info = CreateMonitorInfo();
            if (!GetMonitorInfo(monitor, ref info))
            {
                return null;
            }

            return ToScreenInfo(info);
        }
        catch
        {
            return null;
        }
    }

    private static MONITORINFOEX CreateMonitorInfo()
    {
        return new MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<MONITORINFOEX>(),
            szDevice = string.Empty
        };
    }

    private static ScreenInfo ToScreenInfo(MONITORINFOEX info)
    {
        // rcMonitor 是显示器完整矩形，与 Screen.Bounds 语义保持一致
        RECT bounds = info.rcMonitor;
        return new ScreenInfo(
            string.IsNullOrWhiteSpace(info.szDevice) ? string.Empty : info.szDevice,
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top);
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        // CCHDEVICENAME = 32，形如 \\.\DISPLAY1
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
