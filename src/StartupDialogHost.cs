using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;

namespace BASpark;

/// <summary>
/// 启动阶段（控制面板尚未创建）显示语言选择与隐私政策窗口。
///
/// </summary>
internal static class StartupDialogHost
{
    private static readonly Dictionary<IntPtr, Task> ClosingWindows = [];
    private static readonly TimeSpan CloseAnimationRetention = TimeSpan.FromMilliseconds(300);
    private const int DwmwaTransitionsForcedDisabled = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private static void SetDwmFlag(IntPtr handle, int attribute, bool enabled)
    {
        int value = enabled ? 1 : 0;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)));
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
        where T : UserControl
    {
        try
        {
            return factory();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void ShowWhenReady(DcompPanelHost host, FrameworkElement root)
    {
        IntPtr handle = host.Handle;
        bool prepared = false;
        async void Ready(object sender, RoutedEventArgs args)
        {
            if (prepared) return;
            prepared = true;
            root.Loaded -= Ready;
            try
            {
                root.UpdateLayout();
                FitToContent(host, root);
                root.UpdateLayout();
                await ElementCompositionPreview.GetElementVisual(root).Compositor.RequestCommitAsync();
                if (host.Handle == IntPtr.Zero || ClosingWindows.ContainsKey(handle)) return;
                _ = DwmFlush();
                SetDwmFlag(handle, DwmwaTransitionsForcedDisabled, false);
                _ = DwmFlush();
                host.Show();
            }
            catch (Exception)
            {
                if (host.Handle != IntPtr.Zero && !ClosingWindows.ContainsKey(handle))
                {
                    int disabled = 0;
                    _ = DwmSetWindowAttribute(handle, DwmwaTransitionsForcedDisabled, ref disabled, sizeof(int));
                    host.Show();
                }
            }
        }
        root.Loaded += Ready;
        if (root.IsLoaded) Ready(root, new RoutedEventArgs());
    }

    public static Task CloseAsync(DcompPanelHost host)
    {
        IntPtr handle = host.Handle;
        if (ClosingWindows.TryGetValue(handle, out Task? closing)) return closing;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ClosingWindows.Add(handle, completion.Task);
        async Task FinishCloseAsync()
        {
            try
            {
                bool visible = host.AppWindow?.IsVisible == true;
                host.Hide();
                if (visible && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
                    await Task.Delay(CloseAnimationRetention);
                host.Dispose();
                completion.TrySetResult(true);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { ClosingWindows.Remove(handle); }
        }
        _ = FinishCloseAsync();
        return completion.Task;
    }

    public static void ConfigureCaptionButton(DcompPanelHost host, FrameworkElement root, Button close)
    {
        void UpdateBounds()
        {
            if (host.Handle == IntPtr.Zero || !root.IsLoaded || close.ActualWidth <= 0) return;
            Rect bounds = close.TransformToVisual(root).TransformBounds(new Rect(0, 0, close.ActualWidth, close.ActualHeight));
            double scale = root.XamlRoot.RasterizationScale;
            int left = (int)Math.Round(bounds.Left * scale);
            int top = (int)Math.Round(bounds.Top * scale);
            int right = (int)Math.Round(bounds.Right * scale);
            int bottom = (int)Math.Round(bounds.Bottom * scale);
            host.SetCaptionButtonsBounds(new RectInt32(left, top, right - left, bottom - top));
        }
        root.Loaded += (_, _) => root.DispatcherQueue.TryEnqueue(UpdateBounds);
        root.SizeChanged += (_, _) => root.DispatcherQueue.TryEnqueue(UpdateBounds);
        close.SizeChanged += (_, _) => root.DispatcherQueue.TryEnqueue(UpdateBounds);
    }

    public static void FitToContent(DcompPanelHost host, FrameworkElement root)
    {
        if (!root.IsLoaded || root.ActualWidth <= 0 || host.AppWindow is not { } appWindow) return;
        double scale = root.XamlRoot.RasterizationScale;
        DisplayArea area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double naturalWidth = root.DesiredSize.Width;
        int width = Math.Min((int)Math.Ceiling(naturalWidth * scale), area.WorkArea.Width - (int)Math.Round(48 * scale));
        root.Measure(new Size(width / scale, double.PositiveInfinity));
        int height = Math.Min((int)Math.Ceiling(root.DesiredSize.Height * scale), area.WorkArea.Height - (int)Math.Round(32 * scale));
        root.InvalidateMeasure();
        root.UpdateLayout();
        if (height <= 0 || (height == appWindow.Size.Height && width == appWindow.Size.Width)) return;
        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(
            area.WorkArea.X + Math.Max(0, (area.WorkArea.Width - width) / 2),
            area.WorkArea.Y + Math.Max(0, (area.WorkArea.Height - height) / 2)));
    }
}
