using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr window, StartupWindowProcedure procedure, UIntPtr identity, UIntPtr reference);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr window, StartupWindowProcedure procedure, UIntPtr identity);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr parameter, IntPtr data);

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
        if (WindowProcedures.ContainsKey(handle)) return;
        StartupWindowProcedure procedure = (target, message, parameter, data, identity, reference) =>
        {
            if (message == 0x00A3 || (message == 0x0112 && ((long)parameter & 0xFFF0) is 0xF000 or 0xF030))
                return IntPtr.Zero;
            return DefSubclassProc(target, message, parameter, data);
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
                naturalWidth = Math.Max(naturalWidth, measurement.DesiredSize.Width + 84);
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
