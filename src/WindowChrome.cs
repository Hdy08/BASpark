using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BASpark;

/// <summary>
/// WinUI 3 不读取 csproj 的 <c>ApplicationIcon</c> 作为窗口图标，
/// 因此这里复用嵌入的应用图标，显式设置窗口句柄和原生标题栏。
/// </summary>
internal static class WindowChrome
{
    internal sealed class ComboBoxWidthTracker : IDisposable
    {
        private readonly ComboBox _combo;
        private readonly long _placeholderToken;

        public ComboBoxWidthTracker(ComboBox combo)
        {
            _combo = combo;
            combo.Loaded += Combo_Loaded;
            combo.SelectionChanged += Combo_SelectionChanged;
            _placeholderToken = combo.RegisterPropertyChangedCallback(ComboBox.PlaceholderTextProperty, (_, _) => Refresh());
        }

        private void Combo_Loaded(object sender, RoutedEventArgs args) => Refresh();
        private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs args) => Refresh();

        public void Refresh()
        {
            if (!_combo.IsLoaded) return;
            _combo.ApplyTemplate();
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(_combo) == 0 ||
                Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(_combo, 0) is not Grid layout) return;
            string text = _combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? string.Empty
                : _combo.SelectedItem?.ToString() ?? _combo.PlaceholderText ?? string.Empty;
            var label = new TextBlock
            {
                Text = text, FontFamily = _combo.FontFamily, FontSize = _combo.FontSize,
                FontWeight = _combo.FontWeight, FontStyle = _combo.FontStyle,
                CharacterSpacing = _combo.CharacterSpacing, Language = _combo.Language, TextWrapping = TextWrapping.NoWrap
            };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double chrome = layout.ColumnDefinitions.Where(column => column.Width.IsAbsolute).Sum(column => column.Width.Value);
            double minimum = layout.Children.OfType<Border>().FirstOrDefault(border => border.Name == "Background")?.MinWidth ?? 0;
            double scale = _combo.XamlRoot.RasterizationScale;
            double width = Math.Ceiling(Math.Max(minimum, label.DesiredSize.Width + chrome + _combo.Padding.Left + _combo.Padding.Right) * scale) / scale;
            if (_combo.Width != width) _combo.Width = width;
        }

        public void Dispose()
        {
            _combo.Loaded -= Combo_Loaded;
            _combo.SelectionChanged -= Combo_SelectionChanged;
            _combo.UnregisterPropertyChangedCallback(ComboBox.PlaceholderTextProperty, _placeholderToken);
        }
    }

    internal sealed class RenderClock : IDisposable
    {
        private static readonly object ClockLock = new();
        private static int _activeWindows;
        private static bool _precisionRequested;
        private static bool _boostRequested;
        private readonly Window? _window;
        private bool _disposed;

        public bool IsActive { get; private set; }

        public RenderClock(Window? window = null)
        {
            _window = window;
            if (window == null) return;
            window.Activated += Window_Activated;
            window.AppWindow.Changed += Window_Changed;
            window.Closed += Window_Closed;
            RefreshWindowState();
        }

        public void SetActive(bool active)
        {
            lock (ClockLock)
            {
                if (_disposed || IsActive == active) return;
                IsActive = active;
                if (active)
                {
                    if (_activeWindows++ != 0) return;
                    _precisionRequested = TimeBeginPeriod(1) == 0;
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                    {
                        int result = DCompositionBoostCompositorClock(true);
                        _boostRequested = result >= 0;
                    }
                }
                else if (--_activeWindows == 0)
                {
                    if (_boostRequested)
                    {
                        _ = DCompositionBoostCompositorClock(false);
                        _boostRequested = false;
                    }
                    if (_precisionRequested)
                    {
                        _ = TimeEndPeriod(1);
                        _precisionRequested = false;
                    }
                }
            }
        }

        private void RefreshWindowState()
        {
            if (_window != null && !_disposed)
                SetActive(_window.AppWindow.IsVisible && !IsIconic(WinRT.Interop.WindowNative.GetWindowHandle(_window)));
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args) => RefreshWindowState();

        private void Window_Changed(AppWindow sender, AppWindowChangedEventArgs args) => RefreshWindowState();

        private void Window_Closed(object sender, WindowEventArgs args) => Dispose();

        public void Dispose()
        {
            lock (ClockLock)
            {
                if (_disposed) return;
                if (_window != null)
                {
                    _window.Activated -= Window_Activated;
                    _window.AppWindow.Changed -= Window_Changed;
                    _window.Closed -= Window_Closed;
                }
                SetActive(false);
                _disposed = true;
            }
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint period);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint period);

        [DllImport("dcomp.dll")]
        private static extern int DCompositionBoostCompositorClock([MarshalAs(UnmanagedType.Bool)] bool enable);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
    }

    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private static System.Drawing.Icon? _appIcon;
    private static BitmapImage? _appIconImage;

    public static void ApplyNativeShadow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        int policy = DwmNcrpEnabled;
        _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
        var margins = new FrameMargins { LeftWidth = 1, RightWidth = 1, TopHeight = 1, BottomHeight = 1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private static System.Drawing.Icon GetAppIcon()
    {
        if (_appIcon != null) return _appIcon;
        using Stream stream = typeof(WindowChrome).Assembly.GetManifestResourceStream("BASpark.app.ico")
            ?? throw new InvalidOperationException("Application icon resource is unavailable.");
        return _appIcon = new System.Drawing.Icon(stream, 32, 32);
    }

    public static void ApplyTitleBarIcon(TitleBar titleBar)
    {
        titleBar.Resources["TitleBarLeftHeaderPaddingWidth"] = 8d;
        titleBar.Resources["TitleBarIconMargin"] = new Thickness(0, 0, 4, 0);
        titleBar.IconSource = CreateAppIconSource();
    }

    private static ImageIconSource? CreateAppIconSource()
    {
        try
        {
            if (_appIconImage == null)
            {
                using System.Drawing.Bitmap bitmap = GetAppIcon().ToBitmap();
                using var stream = new MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                using var image = stream.AsRandomAccessStream();
                var source = new BitmapImage();
                source.SetSource(image);
                _appIconImage = source;
            }
            return new ImageIconSource { ImageSource = _appIconImage };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void ApplyAppIcon(Window window)
    {
        ApplyAppIcon(WinRT.Interop.WindowNative.GetWindowHandle(window));
        _ = new RenderClock(window);
    }

    public static void ApplyAppIcon(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr icon = GetAppIcon().Handle;
            SendMessage(hwnd, WM_SETICON, ICON_SMALL, icon);
            SendMessage(hwnd, WM_SETICON, ICON_BIG, icon);
        }
        catch (Exception)
        {
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
        catch (Exception)
        {
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
        ApplyTitleBarTheme(WinRT.Interop.WindowNative.GetWindowHandle(window), isDark);
    }

    public static void ApplyTitleBarTheme(IntPtr hwnd, bool isDark)
    {
        try
        {
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
        catch (Exception)
        {
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmNcrpEnabled = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameMargins
    {
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref FrameMargins margins);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
