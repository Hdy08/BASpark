using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;

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
    private delegate IntPtr StartupWindowProcedure(IntPtr window, uint message, IntPtr parameter, IntPtr data, UIntPtr identity, UIntPtr reference);
    private static readonly Dictionary<IntPtr, StartupWindowProcedure> WindowProcedures = [];
    private static readonly Dictionary<IntPtr, Task> ClosingWindows = [];
    private static readonly TimeSpan CloseAnimationRetention = TimeSpan.FromMilliseconds(300);
    private const int DwmwaTransitionsForcedDisabled = 3;
    private const int DwmwaCloak = 13;
    private const int DwmwaCloaked = 14;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const uint AwActivate = 0x00020000;
    private const uint AwBlend = 0x00080000;
    private const uint ShowAnimationMilliseconds = 200;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AnimateWindow(IntPtr window, uint duration, uint flags);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr window, StartupWindowProcedure procedure, UIntPtr identity, UIntPtr reference);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr window, StartupWindowProcedure procedure, UIntPtr identity);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr parameter, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out NativeMethods.RECT rectangle);
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref NativeMethods.POINT point);

    private static void SetDwmFlag(IntPtr handle, int attribute, bool enabled)
    {
        int value = enabled ? 1 : 0;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)));
    }

    public static void ShowWhenReady(Window window, FrameworkElement root)
    {
        IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        bool closed = false;
        bool rendering = false;
        bool cleaned = false;

        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            root.Loaded -= Loaded;
            CompositionTarget.Rendering -= Rendered;
            window.Closed -= Closed;
        }

        void Closed(object sender, WindowEventArgs args)
        {
            closed = true;
            Cleanup();
        }

        void Loaded(object sender, RoutedEventArgs args)
        {
            root.UpdateLayout();
            FitToContent(window, root);
            root.UpdateLayout();
            if (!rendering)
            {
                rendering = true;
                CompositionTarget.Rendering += Rendered;
            }
        }

        void Rendered(object? sender, object args)
        {
            CompositionTarget.Rendering -= Rendered;
            window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    if (closed || ClosingWindows.ContainsKey(handle)) return;
                    await ElementCompositionPreview.GetElementVisual(root).Compositor.RequestCommitAsync();
                    if (closed || ClosingWindows.ContainsKey(handle) || !NativeMethods.IsWindow(handle)) return;
                    _ = DwmFlush();
                    window.AppWindow.Hide();
                    _ = DwmFlush();
                    SetDwmFlag(handle, DwmwaCloak, false);
                    SetDwmFlag(handle, DwmwaTransitionsForcedDisabled, false);
                    _ = DwmFlush();
                    if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled &&
                        !AnimateWindow(handle, ShowAnimationMilliseconds, AwActivate | AwBlend))
                        AppLogger.Debug($"Failed to animate the startup window: {Marshal.GetLastWin32Error()}.");
                    if (!closed && !ClosingWindows.ContainsKey(handle) && NativeMethods.IsWindow(handle)) window.Activate();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to present the startup window: {ex.Message}");
                    if (!closed && !ClosingWindows.ContainsKey(handle) && NativeMethods.IsWindow(handle))
                    {
                        int disabled = 0;
                        _ = DwmSetWindowAttribute(handle, DwmwaCloak, ref disabled, sizeof(int));
                        _ = DwmSetWindowAttribute(handle, DwmwaTransitionsForcedDisabled, ref disabled, sizeof(int));
                        window.Activate();
                    }
                }
                finally
                {
                    Cleanup();
                }
            });
        }

        int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style | WsExNoRedirectionBitmap);
        SetDwmFlag(handle, DwmwaTransitionsForcedDisabled, true);
        SetDwmFlag(handle, DwmwaCloak, true);
        window.Closed += Closed;
        root.Loaded += Loaded;
        try
        {
            window.Activate();
            if (root.IsLoaded) Loaded(root, new RoutedEventArgs());
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public static Task CloseAsync(Window window)
    {
        IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (ClosingWindows.TryGetValue(handle, out Task? closing)) return closing;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ClosingWindows.Add(handle, completion.Task);

        async Task FinishCloseAsync()
        {
            try
            {
                bool animate = NativeMethods.IsWindowVisible(handle) &&
                    DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked == 0 &&
                    new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
                window.AppWindow.Hide();
                if (animate) await Task.Delay(CloseAnimationRetention);
                if (NativeMethods.IsWindow(handle)) window.Close();
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to close the startup window.", ex);
                completion.TrySetException(ex);
            }
            finally
            {
                ClosingWindows.Remove(handle);
            }
        }

        _ = FinishCloseAsync();
        return completion.Task;
    }

    private static void AlignContent(Window window, IntPtr handle)
    {
        if (!window.ExtendsContentIntoTitleBar || !GetClientRect(handle, out NativeMethods.RECT client) ||
            client.Width <= 0 || client.Height <= 0) return;
        IntPtr content = FindWindowEx(handle, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (content == IntPtr.Zero) return;
        var origin = new NativeMethods.POINT();
        if (ClientToScreen(handle, ref origin) && NativeMethods.GetWindowRect(content, out NativeMethods.RECT bounds) &&
            bounds.Left == origin.x && bounds.Top == origin.y && bounds.Width == client.Width && bounds.Height == client.Height) return;
        _ = NativeMethods.SetWindowPos(content, IntPtr.Zero, 0, 0, client.Width, client.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public static void LockWindow(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int style = NativeMethods.GetWindowLong(handle, -16);
        NativeMethods.SetWindowLong(handle, -16, style & ~(0x00040000 | 0x00010000 | 0x00020000));
        if (!WindowProcedures.ContainsKey(handle))
        {
            StartupWindowProcedure procedure = (target, message, parameter, data, identity, reference) =>
            {
                if (message == 0x0010)
                {
                    _ = CloseAsync(window);
                    return IntPtr.Zero;
                }
                if (message == 0x0083 && window.ExtendsContentIntoTitleBar)
                {
                    NativeMethods.RECT bounds = Marshal.PtrToStructure<NativeMethods.RECT>(data);
                    _ = DefSubclassProc(target, message, parameter, data);
                    Marshal.StructureToPtr(bounds, data, false);
                    return IntPtr.Zero;
                }
                if (message == 0x00A3 || (message == 0x0112 && ((long)parameter & 0xFFF0) is 0xF000 or 0xF030))
                    return IntPtr.Zero;
                IntPtr result = DefSubclassProc(target, message, parameter, data);
                if (message is 0x0005 or 0x0047) AlignContent(window, target);
                return result;
            };
            if (!SetWindowSubclass(handle, procedure, new UIntPtr(1), UIntPtr.Zero))
                throw new InvalidOperationException("Cannot lock startup window commands.");
            WindowProcedures.Add(handle, procedure);
            window.Closed += (_, _) =>
            {
                _ = RemoveWindowSubclass(handle, procedure, new UIntPtr(1));
                WindowProcedures.Remove(handle);
            };
        }
        _ = NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | 0x0020);
        AlignContent(window, handle);
    }

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text) yield return text;
            foreach (TextBlock descendant in TextBlocks(child)) yield return descendant;
        }
    }

    public static void FitToContent(Window window, FrameworkElement root)
    {
        if (!root.IsLoaded || root.ActualWidth <= 0 || window.AppWindow is not { } appWindow) return;
        double scale = root.XamlRoot.RasterizationScale;
        SizeInt32 clientSize = appWindow.ClientSize;
        int frameHeight = appWindow.Size.Height - clientSize.Height;
        int frameWidth = appWindow.Size.Width - clientSize.Width;
        DisplayArea area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        var scroller = root is Grid grid ? grid.Children.OfType<ScrollViewer>().FirstOrDefault() : null;
        double naturalWidth = 560;
        if (scroller?.Content is FrameworkElement content)
        {
            foreach (TextBlock text in TextBlocks(content))
            {
                var measurement = new TextBlock
                {
                    Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
                    FontWeight = text.FontWeight, CharacterSpacing = text.CharacterSpacing, TextWrapping = TextWrapping.NoWrap
                };
                measurement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double iconWidth = 0;
                for (DependencyObject? parent = VisualTreeHelper.GetParent(text); parent != null; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is InfoBar { IsIconVisible: true }) { iconWidth = 30; break; }
                naturalWidth = Math.Max(naturalWidth, measurement.DesiredSize.Width + 84 + iconWidth);
            }
        }
        int width = Math.Min((int)Math.Ceiling(naturalWidth * scale), area.WorkArea.Width - frameWidth - (int)Math.Round(48 * scale));
        root.Measure(new Size(width / scale, double.PositiveInfinity));
        int height = (int)Math.Ceiling(root.DesiredSize.Height * scale);
        height = Math.Min(height, area.WorkArea.Height - frameHeight - (int)Math.Round(32 * scale));
        if (height <= 0 || (height == clientSize.Height && width == clientSize.Width)) return;
        appWindow.Resize(new SizeInt32(width + frameWidth, height + frameHeight));
        appWindow.Move(new PointInt32(
            area.WorkArea.X + Math.Max(0, (area.WorkArea.Width - appWindow.Size.Width) / 2),
            area.WorkArea.Y + Math.Max(0, (area.WorkArea.Height - appWindow.Size.Height) / 2)));
    }

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
