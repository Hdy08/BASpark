using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Text;
using Microsoft.Win32;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace BASpark;

/// <summary>运行中进程选择列表的一行。</summary>
public class ProcessItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

/// <summary>视觉表现「恢复默认」列表的一项。</summary>
public class VisualResetItem
{
    public VisualAppearanceResetFlags Flags { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsSelected { get; set; }

    public VisualResetItem(VisualAppearanceResetFlags flags, string title, string subtitle)
    {
        Flags = flags;
        Title = title;
        Subtitle = subtitle;
    }
}

/// <summary>多屏管理列表的一项；行本身在 <c>PanelScreenOptions</c> 中动态构建。</summary>
public class ScreenOptionItem
{
    public int DisplayIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ResolutionText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string EnableLabel { get; set; } = string.Empty;
}

/// <summary>
/// 控制面板（WinUI 3 原生实现，替代原 WPF 代码后置）。
///
/// 与迁移前的刻意差异：
///   * 主题交给 WinUI：<see cref="App.ResolveElementTheme"/> 应用到 RootGrid，并启用 Mica 背景；
///     不再有 ThemeManager（标题栏按钮颜色自行跟随，见 <see cref="ApplyTitleBarTheme"/>）。
///   * 静态文案不能写在 XAML 里：WinUI 3 的 XAML 编译器不支持自定义 MarkupExtension
///     （<c>{loc:Localization KEY}</c> 报 WMC0615），因此标记中的文案属性留空，
///     统一由 <see cref="ApplyLocalizedText"/> 在加载时填充——语义等同于旧版
///     的 UiLocalizer，切换语言后重新调用即可整体刷新。
///   * RadioButtons 容器统一读写 SelectedIndex（旧版是各个 RadioButton.IsChecked）。
///   * Yes/No 确认改用 ContentDialog（需要 XamlRoot）；纯提示仍走原生 NativeMessageBox。
///   * 旧版「滚动时临时显示滚动条」的 hack 去掉：OnScroll 直接映射为 Auto。
/// </summary>
public sealed partial class ControlPanelWindow : Window
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BASparkClient/1.0";
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    // 初始尺寸。
    private const int DesignWidth = 680;
    private const int DesignHeight = 710;

    // 最小尺寸下限：侧边栏固定 212px，设置页的滑块 + 数字框并排布局在此宽度下
    // 开始被压扁，再窄会出现控件显示不全。
    private const int MinDesignWidth = 560;
    private const int MinDesignHeight = 560;

    /// <summary>
    /// 侧栏设置子导航的展开/收起。
    ///
    /// 两个要求：
    ///   1. 下方的「日志 / 关于」要贴着展开边缘**连续**移动；
    ///   2. 动画要按屏幕刷新率更新（本机屏幕 180Hz），不能一卡一卡。
    ///
    /// 因此这里**不用布局属性做动画**。实测：对 <c>Height</c> 做依赖动画时每帧都要在
    /// UI 线程跑一遍 measure/arrange，实际只有约 30Hz，在 180Hz 屏上就是掉帧。改为：
    ///   * 布局只在切换的那一帧改一次：展开时容器直接拿到内容自然高度、收起时归 0；
    ///   * 「日志 / 关于」的位移用独立的 <see cref="TranslateTransform"/> 动画
    ///     （独立动画由合成器线程插值，按屏幕刷新率更新）；
    ///   * 内容区的「露出」用合成器 <see cref="InsetClip"/> 的 BottomInset 动画，同样
    ///     跑在合成器线程；
    ///   * 收起动画结束后再提交布局（容器高度归 0、平移归 0，视觉净位置不变）。
    ///
    /// 实测踩过的坑：
    ///   1. **不要给内容本身加平移动画**：展开时它会让 4 个子项先下沉几像素再回位。
    ///   2. 独立动画（变换 / 不透明度）与合成器动画不受 <c>EnableDependentAnimation</c>
    ///      限制；布局属性动画不显式开启该标志会被 WinUI 静默丢弃，直到 Completed
    ///      回调才瞬间设到终值。
    ///   3. <c>Storyboard.Completed</c> 是异步回调，**不在**调用方 try/catch 的栈上，
    ///      其中抛出的异常会直接终结进程；必须用代次号忽略过期回调，否则快速点击时
    ///      旧回调会把状态改回去。
    ///   4. 平移与裁剪必须用「当前值 → 目标值」的单关键帧动画（不写 From），这样动画
    ///      被打断时不会从 0 重新开始跳一下。
    /// </summary>
    private sealed class SubNavAnimator
    {
        // 展开略慢于收起：展开需要被看清，收起只需干净利落。
        private static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(220);

        private readonly Border _host;
        private readonly FrameworkElement _content;
        private readonly FrameworkElement _below;
        private readonly TranslateTransform _belowShift = new();

        private InsetClip? _revealClip;
        private bool _expanded;
        private int _generation;

        public SubNavAnimator(Border host, FrameworkElement content, FrameworkElement below)
        {
            _host = host;
            _content = content;
            _below = below;

            // 「日志 / 关于」整组靠平移让位，布局本身不参与动画。
            _below.RenderTransform = _belowShift;

            // 初始为收起状态：容器高度 0，内容不参与命中测试。
            _host.Height = 0;
            _host.IsHitTestVisible = false;
        }

        public void SetExpanded(bool expanded)
        {
            if (_expanded == expanded)
            {
                return;
            }

            _expanded = expanded;
            _generation++;

            try
            {
                Animate(expanded, _generation);
            }
            catch (Exception ex)
            {
                // 动画失败不能拖垮界面：直接落到目标状态。
                AppLogger.Warn($"Sub-nav animation failed: {ex.Message}");
                ApplyFinalState(expanded);
            }
        }

        private void Animate(bool expanded, int generation)
        {
            double height = MeasureContentHeight();
            if (height <= 0)
            {
                // 尚未布局出可用尺寸：直接到位，下次交互再动画。
                ApplyFinalState(expanded);
                return;
            }

            EnsureRevealClip(height);

            if (expanded)
            {
                // 布局一次性到位：容器直接拿到自然高度，「日志 / 关于」在布局里立刻
                // 下移 height；紧接着用平移把它们按回原位 —— 两者发生在同一帧，
                // 视觉上没有跳变，之后由合成器把它们连续推到新位置。
                _host.Height = height;
                _belowShift.Y = -height;
            }

            _host.IsHitTestVisible = expanded;
            StartAnimations(
                expanded ? ExpandDuration : CollapseDuration,
                expanded ? EasingMode.EaseOut : EasingMode.EaseIn,
                shiftTo: expanded ? 0 : -height,
                revealTo: expanded ? 0f : (float)height,
                onCompleted: () => ApplyFinalState(expanded),
                generation: generation);
        }

        /// <summary>
        /// 播放两条动画：
        ///   * 内容露出——合成器 <see cref="InsetClip"/> 的 BottomInset；
        ///   * 「日志 / 关于」位移——独立的 <see cref="TranslateTransform"/> 动画。
        /// 两者都由合成器线程插值，按屏幕刷新率更新，不经过 UI 线程布局。
        /// 都不写 From（单关键帧 / 只写 To）→ 从当前值开始，被打断时不会跳。
        /// </summary>
        private void StartAnimations(
            TimeSpan duration,
            EasingMode easingMode,
            double shiftTo,
            float revealTo,
            Action onCompleted,
            int generation)
        {
            var compositor = _revealClip!.Compositor;
            var reveal = compositor.CreateScalarKeyFrameAnimation();
            reveal.InsertKeyFrame(1f, revealTo, CreateEasing(compositor, easingMode));
            reveal.Duration = duration;
            _revealClip.StartAnimation("BottomInset", reveal);

            var shift = new DoubleAnimation
            {
                To = shiftTo,
                Duration = new Duration(duration),
                EasingFunction = new CubicEase { EasingMode = easingMode }
            };
            Storyboard.SetTarget(shift, _belowShift);
            Storyboard.SetTargetProperty(shift, "Y");

            var storyboard = new Storyboard();
            storyboard.Children.Add(shift);
            storyboard.Completed += (_, _) =>
            {
                // 过期回调直接忽略，否则快速点击时旧状态会覆盖新状态。
                if (generation != _generation)
                {
                    return;
                }

                onCompleted();
            };
            storyboard.Begin();
        }

        private static CompositionEasingFunction CreateEasing(Compositor compositor, EasingMode mode) =>
            mode == EasingMode.EaseIn
                ? compositor.CreateCubicBezierEasingFunction(new Vector2(0.55f, 0.055f), new Vector2(0.675f, 0.19f))
                : compositor.CreateCubicBezierEasingFunction(new Vector2(0.215f, 0.61f), new Vector2(0.355f, 1f));

        /// <summary>
        /// 懒创建内容区的合成器裁剪。收起态容器高度为 0，内容本来就不显示，
        /// 因此这里把下沿裁到内容高度即可（展开动画从当前值插值，不会跳）。
        /// </summary>
        private void EnsureRevealClip(double height)
        {
            if (_revealClip != null)
            {
                return;
            }

            Visual visual = ElementCompositionPreview.GetElementVisual(_host);
            InsetClip clip = visual.Compositor.CreateInsetClip();
            clip.BottomInset = (float)height;
            visual.Clip = clip;
            _revealClip = clip;
        }

        /// <summary>
        /// 量出内容自然高度。用 <see cref="UIElement.Measure"/> 而非常量：语言切换或
        /// 字号变化都会改变高度。首次布局前宽度不可用（此时测量会让 XAML 递归），
        /// 返回 0 由调用方走「不做动画、直接到位」的兜底。
        /// </summary>
        private double MeasureContentHeight()
        {
            double width = _host.ActualWidth > 0 ? _host.ActualWidth : _content.ActualWidth;
            if (width <= 0)
            {
                return 0;
            }

            _content.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
            return _content.DesiredSize.Height;
        }

        /// <summary>直接落到目标状态。只做无异常风险的赋值。</summary>
        private void ApplyFinalState(bool expanded)
        {
            if (expanded)
            {
                _host.IsHitTestVisible = true;
                _belowShift.Y = 0;
                if (_revealClip != null)
                {
                    _revealClip.BottomInset = 0;
                }

                // 交还高度约束，让布局接管（语言切换等改变内容高度时自动跟随）。
                _host.Height = double.NaN;
            }
            else
            {
                _host.IsHitTestVisible = false;
                // 布局收回 0 与平移归零必须在同一帧发生，视觉净位置才不变。
                _host.Height = 0;
                _belowShift.Y = 0;
                if (_revealClip != null)
                {
                    _revealClip.BottomInset = (float)Math.Max(1, MeasureContentHeight());
                }
            }
        }
    }

    private SubNavAnimator? _subNav;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly DispatcherQueueTimer? _refreshTimer;

    private readonly object _networkPromptLock = new();
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly List<ProcessItem> _allRunningProcesses = new();
    private readonly Dictionary<ScreenOptionItem, ToggleSwitch> _screenToggles = new();
    private readonly Dictionary<Slider, NumberBox> _sliderToBox = new();
    private readonly Dictionary<NumberBox, Slider> _boxToSlider = new();

    private bool _isCheckingUpdate;
    private bool _isLoading;
    private bool _isClosed;
    private bool _skipSaveOnClosing;
    private bool _suppressValueSync;
    private bool _autoNetworkFailurePromptShown;
    private bool _logViewInitialized;
    private int _themeRefreshPending;
    private string _languageAtLoad = Localization.CultureZhCn;
    private string? _pendingLanguage;
    private NetworkRegionOption _networkRegionAtLoad = NetworkRegionOption.Auto;

    // 首页「当前状态」的三种配色，复用实例（见 RefreshTimer_Tick 的说明）。
    private readonly SolidColorBrush _statusPausedBrush = new(Colors.Gray);
    private readonly SolidColorBrush _statusFilteredBrush = new(Color.FromArgb(255, 0xD9, 0x77, 0x06));
    private readonly SolidColorBrush _statusActiveBrush = new(Colors.Green);

    public ObservableCollection<FilterProfile> Profiles { get; set; } = new();
    public ObservableCollection<string> CurrentProfileProcesses { get; set; } = new();
    public ObservableCollection<ProcessItem> RunningProcessList { get; set; } = new();
    public ObservableCollection<VisualResetItem> VisualResetItems { get; set; } = new();
    public ObservableCollection<ScreenOptionItem> ScreenOptions { get; set; } = new();
    public ControlPanelWindow()
    {
        InitializeComponent();

        _languageAtLoad = string.IsNullOrWhiteSpace(ConfigManager.UiLanguage)
            ? Localization.CurrentCultureName
            : ConfigManager.UiLanguage;
        _networkRegionAtLoad = ConfigManager.NetworkRegion;

        ApplyWindowChrome();
        BindCollections();
        SetupSliderPairs();

        ComboProfiles.ItemsSource = Profiles;
        ListConfiguredProcesses.ItemsSource = CurrentProfileProcesses;
        ListRunningProcesses.ItemsSource = RunningProcessList;

        // 版本号 / 外链 / 公告 / 状态等动态文案在代码里补上；静态文案见 ApplyLocalizedText。
        ApplyLocalizedText();
        ApplyAboutLinkVisibility();
        PopulateLanguageCombo();
        LoadVersion();
        LoadSettings();
        ApplyScrollbarSettings();
        LoadScreenOptions();
        ApplyDarkMode();
        CheckAdminStatus();
        UpdatePageVisibility();
        InitLogView();

        AppLogger.EntryAdded += OnAppLogEntryAdded;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Closed += ControlPanelWindow_Closed;
        RootGrid.Loaded += RootGrid_Loaded;

        _ = CheckForUpdates(isManual: false);

        _refreshTimer = App.DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(500);
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += (_, _) => RefreshTimer_Tick();
        _refreshTimer.Start();
    }

    /// <summary>UI 已加载且窗口未关闭时，控件事件才会产生副作用（等价于旧版的 IsLoaded 判断）。</summary>
    private bool IsUiReady => !_isClosed && RootGrid is { IsLoaded: true };

    // ==================================================================
    // 窗口外观
    // ==================================================================

    private void ApplyWindowChrome()
    {
        Title = Localization.Get("App_Title_ControlPanel");

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Mica backdrop unavailable: {ex.Message}");
        }
        RootGrid.RequestedTheme = App.ResolveElementTheme();

        // 侧栏子导航的展开/收起：布局只改一次，「日志 / 关于」由独立平移动画让位，
        // 内容露出用合成器裁剪，整体按屏幕刷新率更新（不逐帧跑布局）。
        _subNav = new SubNavAnimator(SettingsSubNavHost, SettingsSubNav, NavAfterSettings);

        // 页面切换动画需要在不透明变换上做位移。
        foreach (FrameworkElement page in new FrameworkElement[]
                 {
                     PageWelcome, PageSettings, PageLog, PageAbout,
                     SectionBasic, SectionVisual, SectionFilter, SectionMultiScreen
                 })
        {
            page.RenderTransform = new TranslateTransform();
        }

        // 去掉系统标题栏、改用原生 TitleBar 控件。
        // 注意：SetTitleBar 必须在视觉树加载完成后调用，否则会抛 E_INVALIDARG
        // （Value does not fall within the expected range），导致整个窗口构造失败。
        RootGrid.Loaded += (_, _) => ApplyCustomTitleBar();

        AppWindow? appWindow = AppWindow;
        if (appWindow == null)
        {
            return;
        }

        try
        {
            // 按显示器 DPI 换算后居中。
            double scale = GetWindowDpiScale();
            int width = (int)Math.Round(DesignWidth * scale);
            int height = (int)Math.Round(DesignHeight * scale);

            DisplayArea? area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
            area ??= DisplayArea.Primary;

            if (area != null)
            {
                Windows.Graphics.RectInt32 work = area.WorkArea;
                int x = work.X + Math.Max(0, (work.Width - width) / 2);
                int y = work.Y + Math.Max(0, (work.Height - height) / 2);
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            }

            // 限制最小尺寸：侧边栏固定 212px，设置页的滑块/数字框并排布局在更窄的
            // 宽度下会被压到无法使用。这里按同样的 DPI 比例换算，保证逻辑尺寸下限。
            //
            // 只用 OverlappedPresenter 的原生属性，不做窗口子类化：comctl32 的
            // SetWindowSubclass 会接管窗口过程链，与 XAML 框架自身的消息处理叠加后
            // 极易造成 UI 线程死锁（实测表现为打开控制面板立即卡死、CPU 归零）。
            int minWidth = (int)Math.Round(MinDesignWidth * scale);
            int minHeight = (int)Math.Round(MinDesignHeight * scale);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = minWidth;
                presenter.PreferredMinimumHeight = minHeight;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to size/center control panel: {ex.Message}");
        }
    }

    /// <summary>
    /// 去掉系统标题栏、改用原生 <c>TitleBar</c> 控件。
    ///
    /// 两个必须遵守的约束：
    ///   1. 只设置 <c>ExtendsContentIntoTitleBar</c>，**不要**再调用
    ///      <c>SetTitleBar(AppTitleBar)</c>。SetTitleBar 只适用于普通 UIElement
    ///      拖拽区域；对 TitleBar 控件调用会抛 E_BOUNDS（0x800f1000），异常在
    ///      Microsoft.UI.Xaml.dll 内未被捕获，进程直接崩溃退出。
    ///   2. 必须在视觉树加载后调用；构造函数里执行会抛 E_INVALIDARG 并导致
    ///      窗口构造失败、界面完全不出现。
    /// </summary>
    private void ApplyCustomTitleBar()
    {
        if (_titleBarApplied)
        {
            return;
        }

        _titleBarApplied = true;

        try
        {
            ExtendsContentIntoTitleBar = true;
        }
        catch (Exception ex)
        {
            // 失败时回退到系统标题栏，功能不受影响。
            AppLogger.Warn($"Failed to extend content into the title bar: {ex.Message}");
        }
    }

    private bool _titleBarApplied;

    private double GetWindowDpiScale()
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"GetDpiForWindow failed: {ex.Message}");
        }

        return 1.0;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyTitleBarTheme();
    }

    /// <summary>WinUI 只负责内容区主题，标题栏按钮需要自己跟随（等价于旧的 ThemeManager.ApplyTitleBar）。</summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            AppWindowTitleBar? titleBar = AppWindow?.TitleBar;
            if (titleBar == null)
            {
                return;
            }

            bool dark = IsDarkThemeEffective();
            Color foreground = dark ? Colors.White : Colors.Black;
            Color hover = dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
            Color pressed = dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x33, 0x00, 0x00, 0x00);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonInactiveForegroundColor = dark
                ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x80, 0x00, 0x00, 0x00);
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Title bar theming unavailable: {ex.Message}");
        }
    }

    private bool IsDarkThemeEffective()
    {
        ElementTheme requested = RootGrid.RequestedTheme;
        if (requested == ElementTheme.Dark)
        {
            return true;
        }

        if (requested == ElementTheme.Light)
        {
            return false;
        }

        ElementTheme actual = RootGrid.ActualTheme;
        if (actual == ElementTheme.Dark)
        {
            return true;
        }

        if (actual == ElementTheme.Light)
        {
            return false;
        }

        return Application.Current?.RequestedTheme == ApplicationTheme.Dark;
    }

    // ==================================================================
    // 数据绑定与滑块 / 数字框联动
    // ==================================================================

    private void BindCollections()
    {
        // 新标记没有声明 ItemTemplate / DisplayMemberPath，这里在代码里补上显示字段。
        ComboProfiles.DisplayMemberPath = nameof(FilterProfile.Name);
        ListRunningProcesses.DisplayMemberPath = nameof(ProcessItem.DisplayName);
    }

    private void SetupSliderPairs()
    {
        // SliderX + TxtXValue 一一对应；ValueChanged 大多已在 XAML 接线，TrailRefresh 由这里接线。
        RegisterSliderPair(SliderScale, TxtScaleValue);
        RegisterSliderPair(SliderTrailScale, TxtTrailScaleValue);
        RegisterSliderPair(SliderClickScale, TxtClickScaleValue);
        RegisterSliderPair(SliderOpacity, TxtOpacityValue);
        RegisterSliderPair(SliderGlow, TxtGlowValue);
        RegisterSliderPair(SliderSpeed, TxtSpeedValue);
        RegisterSliderPair(SliderTrailAnimSpeed, TxtTrailSpeedValue);
        RegisterSliderPair(SliderClickAnimSpeed, TxtClickSpeedValue);
        RegisterSliderPair(SliderTrailRefresh, TxtTrailRefreshValue);

        CheckFollowDisplayRefreshRate.Toggled += FollowDisplayRefreshRate_Toggled;
        SyncSliderAndBoxValues();
    }

    private void RegisterSliderPair(Slider slider, NumberBox box)
    {
        _sliderToBox[slider] = box;
        _boxToSlider[box] = slider;
        slider.ValueChanged += EffectSlider_ValueChanged;
        box.ValueChanged += EffectNumberBox_ValueChanged;
    }

    private void SyncSliderAndBoxValues()
    {
        bool previous = _suppressValueSync;
        _suppressValueSync = true;
        try
        {
            foreach ((Slider slider, NumberBox box) in _sliderToBox)
            {
                if (double.IsNaN(box.Value) || Math.Abs(box.Value - slider.Value) > 0.000001)
                {
                    box.Value = slider.Value;
                }
            }
        }
        finally
        {
            _suppressValueSync = previous;
        }
    }

    private void EffectSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _ = e;
        if (_suppressValueSync || !IsUiReady)
        {
            return;
        }

        if (sender is not Slider slider || !_sliderToBox.TryGetValue(slider, out NumberBox? box))
        {
            return;
        }

        _suppressValueSync = true;
        try
        {
            box.Value = Math.Round(slider.Value, 4);
        }
        finally
        {
            _suppressValueSync = false;
        }
    }

    private void EffectNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        _ = args;
        if (_suppressValueSync || !IsUiReady)
        {
            return;
        }

        if (!_boxToSlider.TryGetValue(sender, out Slider? slider) || double.IsNaN(sender.Value))
        {
            return;
        }

        _suppressValueSync = true;
        try
        {
            slider.Value = sender.Value;
        }
        finally
        {
            _suppressValueSync = false;
        }
    }

    private void FollowDisplayRefreshRate_Toggled(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateTrailRefreshInterlock();
    }

    /// <summary>跟随显示器刷新率时，手动刷新率控件不可编辑。</summary>
    private void UpdateTrailRefreshInterlock()
    {
        bool follow = CheckFollowDisplayRefreshRate.IsOn;
        SliderTrailRefresh.IsEnabled = !follow;
        TxtTrailRefreshValue.IsEnabled = !follow;
    }

    // ==================================================================
    // 日志
    // ==================================================================

    /// <summary>
    /// 应用静态文案（等价于迁移前的 <c>UiLocalizer.ApplyControlPanel(this)</c>）。
    ///
    /// WinUI 3 的 XAML 编译器不支持自定义 MarkupExtension：标记里的
    /// <c>{loc:Localization KEY}</c> 会直接报 WMC0615（"Type 'loc' used after '{' must be a Markup Extension"，
    /// 用一个最小自定义扩展同样复现），因此静态文案只能像本仓库其它窗口
    /// （ColorPickerWindow / LanguageSelectWindow）那样在代码里填充。
    /// 好处是切换语言可以即时生效，与迁移前一致。
    /// </summary>
    private void ApplyLocalizedText()
    {
        // 导航项带图标，因此文案写在内部 TextBlock 上（不再是 RadioButton.Content）。
        TabWelcomeLabel.Text = Localization.Get("Nav_Home");
        TabSettingsLabel.Text = Localization.Get("Nav_Settings");
        SubTabBasicLabel.Text = Localization.Get("Nav_Basic");
        SubTabVisualLabel.Text = Localization.Get("Nav_Visual");
        SubTabFilterLabel.Text = Localization.Get("Nav_Filter");
        SubTabMultiScreenLabel.Text = Localization.Get("Nav_MultiScreen");
        TabLogLabel.Text = Localization.Get("Nav_Log");
        TabAboutLabel.Text = Localization.Get("Nav_About");
        AppTitleBar.Title = Localization.Get("App_Title_ControlPanel");
        TxtSidebarCopyright.Text = Localization.Get("Sidebar_Copyright");
        TxtWelcomeTitle.Text = Localization.Get("Welcome_Title");
        TxtWelcomeSubtitle.Text = Localization.Get("Welcome_Subtitle");
        TxtStatsTitle.Text = Localization.Get("Welcome_StatsTitle");
        TxtStatusLabel.Text = Localization.Get("Welcome_StatusLabel");
        TxtClicksLabel.Text = Localization.Get("Welcome_ClicksLabel");
        TxtSettingsTitle.Text = Localization.Get("Settings_Title");
        TxtSettingsHint.Text = Localization.Get("Settings_ApplyHint");
        BtnApplySettings.Content = Localization.Get("Settings_Apply");
        TxtBasicTitle.Text = Localization.Get("Basic_Title");
        TxtBasicLanguage.Text = Localization.Get("Basic_Language");
        TxtDarkMode.Text = Localization.Get("Basic_DarkMode");
        RadioDarkModeOff.Content = Localization.Get("Basic_DarkModeOff");
        RadioDarkModeOn.Content = Localization.Get("Basic_DarkModeOn");
        RadioDarkModeSystem.Content = Localization.Get("Basic_DarkModeSystem");
        TxtBasicNetworkRegion.Text = Localization.Get("Basic_NetworkRegion");
        RadioNetworkRegionAuto.Content = Localization.Get("Basic_NetworkRegionAuto");
        RadioNetworkRegionChina.Content = Localization.Get("Basic_NetworkRegionChina");
        RadioNetworkRegionGlobal.Content = Localization.Get("Basic_NetworkRegionGlobal");
        TxtNetworkRegionHint.Text = Localization.Get("Basic_NetworkRegionHint");
        TxtScrollbarVisibility.Text = Localization.Get("Basic_ScrollbarVisibility");
        RadioScrollbarAlways.Content = Localization.Get("Basic_ScrollbarAlways");
        RadioScrollbarOnScroll.Content = Localization.Get("Basic_ScrollbarOnScroll");
        TxtScrollbarHint.Text = Localization.Get("Basic_ScrollbarHint");
        CheckAlwaysTrailEffectSwitch.Header = Localization.Get("Basic_TrailSwitch");
        CheckMasterSwitch.Header = Localization.Get("Basic_MasterSwitch");
        TxtClickType.Text = Localization.Get("Basic_ClickType");
        CheckMiddleClickTrigger.Header = Localization.Get("Basic_MiddleClick");
        CheckScreenshotCompatibilityMode.Header = Localization.Get("Basic_ScreenshotMode");
        TxtScreenshotHint.Text = Localization.Get("Basic_ScreenshotHint");
        CheckAutoStart.Header = Localization.Get("Basic_AutoStart");
        CheckStartSilent.Header = Localization.Get("Basic_StartSilent");
        CheckRunAsAdmin.Header = Localization.Get("Basic_RunAsAdmin");
        TxtRunAsAdminHint.Text = Localization.Get("Basic_RunAsAdminHint");
        CheckTouchscreenMode.Header = Localization.Get("Basic_Touchscreen");
        TxtTouchscreenHint.Text = Localization.Get("Basic_TouchscreenHint");
        CheckTelemetry.Header = Localization.Get("Basic_Telemetry");
        TxtVisualTitle.Text = Localization.Get("Visual_Title");
        BtnVisualReset.Content = Localization.Get("Visual_ResetDefaults");
        TxtVisualInputHint.Text = Localization.Get("Visual_InputHint");
        CheckLinkedEffectScale.Header = Localization.Get("Visual_LinkedScale");
        TxtLinkedScaleHint.Text = Localization.Get("Visual_LinkedScaleHint");
        TxtVisualScale.Text = Localization.Get("Visual_Scale");
        TxtTrailScale.Text = Localization.Get("Visual_TrailScale");
        TxtClickScale.Text = Localization.Get("Visual_ClickScale");
        TxtVisualOpacity.Text = Localization.Get("Visual_Opacity");
        TxtGlowIntensity.Text = Localization.Get("Visual_GlowIntensity");
        CheckLinkedAnimationSpeed.Header = Localization.Get("Visual_LinkedSpeed");
        TxtLinkedSpeedHint.Text = Localization.Get("Visual_LinkedSpeedHint");
        TxtAnimSpeed.Text = Localization.Get("Visual_AnimSpeed");
        TxtTrailAnimSpeed.Text = Localization.Get("Visual_TrailAnimSpeed");
        TxtClickAnimSpeed.Text = Localization.Get("Visual_ClickAnimSpeed");
        CheckApplyCurveDraw.Header = Localization.Get("Visual_CurveDraw");
        TxtCurveDrawHint.Text = Localization.Get("Visual_CurveDrawHint");
        CheckFollowDisplayRefreshRate.Header = Localization.Get("Visual_FollowDisplayRefreshRate");
        TxtFollowDisplayRefreshRateHint.Text = Localization.Get("Visual_FollowDisplayRefreshRateHint");
        TxtTrailRefresh.Text = Localization.Get("Visual_TrailRefresh");
        TxtEffectColor.Text = Localization.Get("Visual_Color");
        BtnPickColor.Content = Localization.Get("Visual_ChangeColor");
        TxtFilterTitle.Text = Localization.Get("Filter_Title");
        CheckEnvironmentFilter.Header = Localization.Get("Filter_Enable");
        CheckHideInFullscreen.Header = Localization.Get("Filter_Fullscreen");
        CheckShowEffectOnDesktop.Header = Localization.Get("Filter_Desktop");
        TxtFilterProfiles.Text = Localization.Get("Filter_Profiles");
        BtnAddProfile.Content = Localization.Get("Filter_New");
        BtnRenameProfile.Content = Localization.Get("Filter_Rename");
        BtnDeleteProfile.Content = Localization.Get("Filter_Delete");
        TxtFilterMode.Text = Localization.Get("Filter_Mode");
        TxtProcessList.Text = Localization.Get("Filter_ProcessList");
        TxtAddProcess.Text = Localization.Get("Filter_AddProcess");
        BtnAddProcess.Content = Localization.Get("Filter_Add");
        BtnBrowseProcess.Content = Localization.Get("Filter_Browse");
        BtnSelectRunningProcess.Content = Localization.Get("Filter_SelectRunning");
        TxtMultiScreenTitle.Text = Localization.Get("MultiScreen_Title");
        BtnRefreshScreens.Content = Localization.Get("MultiScreen_Refresh");
        TxtMultiScreenHint.Text = Localization.Get("MultiScreen_Hint");
        TxtLogTitle.Text = Localization.Get("Log_Title");
        BtnClearLog.Content = Localization.Get("Log_Clear");
        TxtLogHint.Text = Localization.Get("Log_Hint");
        TxtAboutTitle.Text = Localization.Get("About_Title");
        BtnCheckUpdate.Content = Localization.Get("About_CheckUpdate");
        TxtAboutDescription.Text = Localization.Get("About_Description");
        TxtSecurityWarning.Text = Localization.Get("About_SecurityWarning");
        TxtAboutSupport.Text = Localization.Get("About_Support");
        BtnOfficialSite.Content = Localization.Get("About_OfficialSite");
        BtnGithub.Content = Localization.Get("About_Github");
        BtnBilibili.Content = Localization.Get("About_Bilibili");
        BtnQQ.Content = Localization.Get("About_QQ");
        BtnDiscord.Content = Localization.Get("About_Discord");
        BtnSponsor.Content = Localization.Get("About_Sponsor");
        TxtDevOptions.Text = Localization.Get("About_DevOptions");
        BtnResetAll.Content = Localization.Get("About_ResetAll");
        TxtOverlayRunning.Text = Localization.Get("Overlay_RunningProcess");
        SearchRunningProcess.PlaceholderText = Localization.Get("Filter_Browse");
        BtnOverlayCancel.Content = Localization.Get("Overlay_Cancel");
        BtnOverlayConfirmAdd.Content = Localization.Get("Overlay_ConfirmAdd");
        TxtOverlayVisualReset.Text = Localization.Get("Overlay_VisualReset");
        BtnOverlayVisualCancel.Content = Localization.Get("Overlay_Cancel");
        BtnOverlayVisualConfirm.Content = Localization.Get("Overlay_ConfirmReset");
        TxtOverlayRename.Text = Localization.Get("Overlay_RenameProfile");
        TxtOverlayRenamePrompt.Text = Localization.Get("Overlay_RenamePrompt");
        BtnOverlayRenameCancel.Content = Localization.Get("Overlay_Cancel");
        BtnOverlayRenameConfirm.Content = Localization.Get("Overlay_ConfirmRename");

        // 点击方式三个单选项在标记里用的是 Name=（不是 x:Name=），没有生成字段，只能从容器里取。
        if (RadioClickType.Items.Count >= 3)
        {
            SetRadioContent(RadioClickType, 0, "Basic_LeftClick");
            SetRadioContent(RadioClickType, 1, "Basic_RightClick");
            SetRadioContent(RadioClickType, 2, "Basic_BothClick");
        }

        // 进程过滤模式的三个条目同理。
        if (ComboProcessFilterMode.Items.Count >= 3)
        {
            SetComboItemContent(ComboProcessFilterMode, 0, "Filter_Mode_Disabled");
            SetComboItemContent(ComboProcessFilterMode, 1, "Filter_Mode_Blacklist");
            SetComboItemContent(ComboProcessFilterMode, 2, "Filter_Mode_Whitelist");
        }
    }

    private static void SetRadioContent(RadioButtons container, int index, string key)
    {
        if (container.Items[index] is RadioButton button)
        {
            button.Content = Localization.Get(key);
        }
    }

    private static void SetComboItemContent(ComboBox combo, int index, string key)
    {
        if (combo.Items[index] is ComboBoxItem item)
        {
            item.Content = Localization.Get(key);
        }
    }

    private void InitLogView()
    {
        if (_logViewInitialized)
        {
            return;
        }

        _logViewInitialized = true;
        TxtAppLog.Text = string.Join(Environment.NewLine, AppLogger.GetEntries());
        ScrollLogToEnd();
    }

    private void OnAppLogEntryAdded(string line)
    {
        if (_isClosed)
        {
            return;
        }

        App.DispatcherQueue.TryEnqueue(() => AppendLogLine(line));
    }

    private void AppendLogLine(string line)
    {
        if (string.IsNullOrEmpty(TxtAppLog.Text))
        {
            TxtAppLog.Text = line;
        }
        else
        {
            TxtAppLog.Text += Environment.NewLine + line;
        }

        ScrollLogToEnd();
    }

    private void ScrollLogToEnd()
    {
        // 日志 TextBox 自身不滚动，靠外层 ScrollViewer。
        LogScrollViewer?.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        TxtAppLog.Text = string.Empty;
        AppLogger.Clear();
        AppLogger.Info("Log view cleared by user.");
    }

    // ==================================================================
    // 特效颜色
    // ==================================================================

    private async void PickColor_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (!TryParseRgbString(ConfigManager.ParticleColor, out Color initialColor))
        {
            initialColor = Color.FromArgb(255, 76, 167, 255);
        }

        Color? picked = await ShowColorPickerAsync(initialColor);
        if (picked == null)
        {
            return;
        }

        string newColor = ToRgbString(picked.Value);
        ConfigManager.ParticleColor = newColor;
        UpdateColorPreview(newColor);
    }

    /// <summary>取色器宿主适配点：ColorPickerWindow 是独立窗口，返回用户确认的颜色（取消为 null）。</summary>
    private async Task<Color?> ShowColorPickerAsync(Color initialColor)
    {
        XamlRoot? root = await EnsureXamlRootAsync();
        if (root == null || _isClosed)
        {
            AppLogger.Warn("Cannot show color picker: XamlRoot unavailable.");
            return null;
        }

        var dialog = new ColorPickerWindow(initialColor);
        return await dialog.ShowDialogAsync(root);
    }

    private void UpdateColorPreview(string rgbString)
    {
        ColorPreview.Background = TryParseRgbString(rgbString, out Color color)
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Colors.Gray);
    }

    private static bool TryParseRgbString(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(',');
        if (parts.Length != 3 ||
            !byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue))
        {
            return false;
        }

        color = Color.FromArgb(255, red, green, blue);
        return true;
    }

    private static string ToRgbString(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"{color.R},{color.G},{color.B}");

    // ==================================================================
    // 外链 / 版本 / 语言
    // ==================================================================

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        string? url = button.Tag as string;
        if (button == BtnOfficialSite)
        {
            url = Localization.GetOfficialWebsiteUrl();
        }
        else if (button == BtnDiscord)
        {
            url = Localization.GetDiscordUrl();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NativeMessageBox.Show(Localization.Format("Msg_OpenLinkFailed", ex.Message));
        }
    }

    public void ApplyAboutLinkVisibility()
    {
        BtnOfficialSite.Tag = Localization.GetOfficialWebsiteUrl();

        bool isChinese = Localization.IsChineseLocale;
        BtnBilibili.Visibility = isChinese ? Visibility.Visible : Visibility.Collapsed;
        BtnQQ.Visibility = isChinese ? Visibility.Visible : Visibility.Collapsed;
        BtnSponsor.Visibility = isChinese ? Visibility.Visible : Visibility.Collapsed;
        BtnDiscord.Visibility = isChinese ? Visibility.Collapsed : Visibility.Visible;

        string? discordUrl = Localization.GetDiscordUrl();
        BtnDiscord.IsEnabled = !string.IsNullOrWhiteSpace(discordUrl);
        BtnDiscord.Tag = discordUrl ?? string.Empty;
    }

    private void LoadVersion()
    {
        try
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                string versionNum = $"V{version.Major}.{version.Minor}.{version.Build}";
                string versionText = $"BASpark V{version.Major}.{version.Minor}.{version.Build}";
                AboutVersionText.Text = versionNum;
                TxtSidebarVersion.Text = versionText;
            }
        }
        catch
        {
            string failed = Localization.Get("Version_ReadFailed");
            AboutVersionText.Text = failed;
            TxtSidebarVersion.Text = failed;
        }
    }

    public void PopulateLanguageCombo()
    {
        ComboLanguage.Items.Clear();
        ComboLanguage.Items.Add(new ComboBoxItem { Content = Localization.Get("LangSelect_Chinese"), Tag = Localization.CultureZhCn });
        ComboLanguage.Items.Add(new ComboBoxItem { Content = Localization.Get("LangSelect_English"), Tag = Localization.CultureEn });
        ComboLanguage.Items.Add(new ComboBoxItem { Content = Localization.Get("LangSelect_Japanese"), Tag = Localization.CultureJa });

        string current = string.IsNullOrWhiteSpace(ConfigManager.UiLanguage)
            ? Localization.CurrentCultureName
            : ConfigManager.UiLanguage;

        ComboBoxItem? selected = ComboLanguage.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase));

        ComboLanguage.SelectedItem = selected ?? ComboLanguage.Items[0];
    }

    private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;

        // 语言在「应用」时落盘（与迁移前一致）；这里记住待切换项，避免下拉框被重建时丢信息。
        _pendingLanguage = GetSelectedLanguage();
    }

    private string? GetSelectedLanguage()
    {
        if (ComboLanguage.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString();
        }

        return null;
    }

    // ==================================================================
    // 导航
    // ==================================================================

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (PageWelcome == null)
        {
            return;
        }

        UpdatePageVisibility();
    }

    private void SubTab_Click(object sender, RoutedEventArgs e)
    {
        if (SectionBasic == null)
        {
            return;
        }

        // 子标签本身同组互斥；这里只需保证「设置」处于选中态。
        TabSettings.IsChecked = true;
        UpdatePageVisibility();
    }

    /// <summary>页面切换动画：进入的页面淡入 + 轻微上移。</summary>
    private static readonly TimeSpan PageTransitionDuration = TimeSpan.FromMilliseconds(180);

    private void UpdatePageVisibility()
    {
        bool welcome = TabWelcome.IsChecked == true;
        bool settings = TabSettings.IsChecked == true;
        bool log = TabLog.IsChecked == true;
        bool about = TabAbout.IsChecked == true;

        FrameworkElement? incoming = settings
            ? PageSettings
            : log
                ? PageLog
                : about
                    ? PageAbout
                    : welcome
                        ? (FrameworkElement)PageWelcome
                        : null;

        SetPageVisible(PageWelcome, welcome, incoming);
        SetPageVisible(PageSettings, settings, incoming);
        SetPageVisible(PageLog, log, incoming);
        SetPageVisible(PageAbout, about, incoming);

        // 子导航展开/收起：原生 Expander 的内容动画 + 容器高度依赖动画，
        // 下面的「日志 / 关于」因此被连续推开，而不是等动画结束才瞬移。
        _subNav?.SetExpanded(settings);

        if (settings)
        {
            UpdateSettingsSectionVisibility();
        }

        if (log)
        {
            ScrollLogToEnd();
        }
    }

    /// <summary>显示/隐藏页面，并对「新进入」的页面播放过渡动画。</summary>
    private void SetPageVisible(FrameworkElement page, bool visible, FrameworkElement? incoming)
    {
        if (visible)
        {
            bool wasCollapsed = page.Visibility != Visibility.Visible;
            page.Visibility = Visibility.Visible;
            if (wasCollapsed && ReferenceEquals(page, incoming))
            {
                PlayEntranceTransition(page);
            }
        }
        else
        {
            page.Visibility = Visibility.Collapsed;
        }
    }

    private void PlayEntranceTransition(FrameworkElement page)
    {
        var storyboard = new Storyboard();
        var duration = new Duration(PageTransitionDuration);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, page);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);

        var slide = new DoubleAnimation
        {
            From = 12,
            To = 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, page);
        Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.Y)");
        storyboard.Children.Add(slide);

        storyboard.Begin();
    }

    private void UpdateSettingsSectionVisibility()
    {
        // 四个子项之间切换同样需要过渡动画，因此逐个走 SetPageVisible（它会对
        // 「本次新进入」的那一项播放动画），而不是直接赋 Visibility。
        FrameworkElement? incoming = SubTabBasic.IsChecked == true
            ? SectionBasic
            : SubTabVisual.IsChecked == true
                ? SectionVisual
                : SubTabFilter.IsChecked == true
                    ? SectionFilter
                    : SubTabMultiScreen.IsChecked == true
                        ? SectionMultiScreen
                        : null;

        SetPageVisible(SectionBasic, ReferenceEquals(incoming, SectionBasic), incoming);
        SetPageVisible(SectionVisual, ReferenceEquals(incoming, SectionVisual), incoming);
        SetPageVisible(SectionFilter, ReferenceEquals(incoming, SectionFilter), incoming);
        SetPageVisible(SectionMultiScreen, ReferenceEquals(incoming, SectionMultiScreen), incoming);
    }

    // ==================================================================
    // 首页统计
    // ==================================================================

    private void RefreshTimer_Tick()
    {
        if (_isClosed)
        {
            return;
        }

        ClickCountText.Text = Localization.Format("Welcome_ClicksUnit", ConfigManager.TotalClicks);

        bool suppressedByEnvironment = ConfigManager.IsEffectEnabled &&
            App.Overlay?.IsEffectSuppressedByEnvironment() == true;

        // 状态画刷复用实例：每 500ms 新建一个 SolidColorBrush 并重新赋给
        // Foreground，会让该文本块每次都被判定为「变了」而重绘，长期挂机时白白
        // 制造 GC 压力与无谓的重绘（拖动窗口时正好撞上就是一次卡顿）。
        if (!ConfigManager.IsEffectEnabled)
        {
            StatusText.Text = Localization.Get("Status_Paused");
            StatusText.Foreground = _statusPausedBrush;
        }
        else if (suppressedByEnvironment)
        {
            StatusText.Text = Localization.Get("Status_Filtered");
            StatusText.Foreground = _statusFilteredBrush;
        }
        else
        {
            StatusText.Text = Localization.Get("Status_Active");
            StatusText.Foreground = _statusActiveBrush;
        }
    }

    // ==================================================================
    // 设置载入
    // ==================================================================

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            LoadSettingsCore();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadSettingsCore()
    {
        CheckMasterSwitch.IsOn = ConfigManager.IsEffectEnabled;
        CheckAutoStart.IsOn = ConfigManager.AutoStart;
        CheckStartSilent.IsOn = ConfigManager.StartSilent;
        CheckTelemetry.IsOn = ConfigManager.EnableTelemetry;
        CheckAlwaysTrailEffectSwitch.IsOn = ConfigManager.EnableAlwaysTrailEffect;
        CheckEnvironmentFilter.IsOn = ConfigManager.EnableEnvironmentFilter;
        CheckHideInFullscreen.IsOn = ConfigManager.HideInFullscreen;
        CheckShowEffectOnDesktop.IsOn = ConfigManager.ShowEffectOnDesktop;
        CheckRunAsAdmin.IsOn = ConfigManager.RunAsAdmin;
        CheckTouchscreenMode.IsOn = ConfigManager.IsTouchscreenMode;
        CheckMiddleClickTrigger.IsOn = ConfigManager.EnableMiddleClickTrigger;
        CheckScreenshotCompatibilityMode.IsOn = ConfigManager.ScreenshotCompatibilityMode;

        // 0: 左键, 1: 右键, 2: 左右键
        RadioClickType.SelectedIndex = Math.Clamp(ConfigManager.ClickTriggerType, 0, 2);

        Profiles.Clear();
        foreach (FilterProfile profile in ConfigManager.GetProfiles())
        {
            Profiles.Add(profile);
        }

        ComboProfiles.SelectedItem = ConfigManager.GetActiveProfile();

        UpdateColorPreview(ConfigManager.ParticleColor);
        UpdateClickEffectPanelVisibility();
        UpdateEnvironmentFilterInterlock();

        CheckLinkedEffectScale.IsOn = ConfigManager.UseLinkedEffectScale;
        SliderScale.Value = ConfigManager.EffectScale;
        SliderTrailScale.Value = ConfigManager.TrailEffectScale;
        SliderClickScale.Value = ConfigManager.ClickEffectScale;
        SliderOpacity.Value = ConfigManager.EffectOpacity * 100;
        SliderGlow.Value = ConfigManager.GlowIntensity;
        CheckLinkedAnimationSpeed.IsOn = ConfigManager.UseLinkedAnimationSpeed;
        CheckApplyCurveDraw.IsOn = ConfigManager.ApplyCurveDraw;
        SliderSpeed.Value = ConfigManager.EffectSpeed;
        SliderTrailAnimSpeed.Value = ConfigManager.TrailAnimationSpeed;
        SliderClickAnimSpeed.Value = ConfigManager.ClickAnimationSpeed;
        CheckFollowDisplayRefreshRate.IsOn = ConfigManager.FollowDisplayRefreshRate;
        SliderTrailRefresh.Value = ConfigManager.TrailRefreshRate;
        SyncSliderAndBoxValues();

        UpdateEffectScalePanelVisibility();
        UpdateAnimationSpeedPanelVisibility();
        UpdateTrailRefreshInterlock();

        RadioScrollbarVisibility.SelectedIndex = ConfigManager.ScrollbarVisibility == PanelScrollbarVisibility.Always ? 0 : 1;

        SelectDarkMode(ConfigManager.DarkMode);
        SelectNetworkRegion(ConfigManager.NetworkRegion);
    }

    private void CheckAdminStatus()
    {
        CheckRunAsAdmin.IsOn = ConfigManager.RunAsAdmin;
    }

    private void SelectDarkMode(DarkModeOption mode)
    {
        // RadioDarkMode 顺序：Off / On / System
        RadioDarkMode.SelectedIndex = mode switch
        {
            DarkModeOption.Off => 0,
            DarkModeOption.On => 1,
            _ => 2
        };
    }

    private DarkModeOption GetSelectedDarkMode()
    {
        int index = RadioDarkMode.SelectedIndex;
        if (index < 0)
        {
            if (RadioDarkModeOff.IsChecked == true)
            {
                return DarkModeOption.Off;
            }

            if (RadioDarkModeOn.IsChecked == true)
            {
                return DarkModeOption.On;
            }

            return DarkModeOption.System;
        }

        return index switch
        {
            0 => DarkModeOption.Off,
            1 => DarkModeOption.On,
            _ => DarkModeOption.System
        };
    }

    private void SelectNetworkRegion(NetworkRegionOption region)
    {
        // RadioNetworkRegion 顺序：Auto / China / Global
        RadioNetworkRegion.SelectedIndex = region switch
        {
            NetworkRegionOption.China => 1,
            NetworkRegionOption.Global => 2,
            _ => 0
        };
    }

    private NetworkRegionOption GetSelectedNetworkRegion()
    {
        int index = RadioNetworkRegion.SelectedIndex;
        if (index < 0)
        {
            if (RadioNetworkRegionChina.IsChecked == true)
            {
                return NetworkRegionOption.China;
            }

            if (RadioNetworkRegionGlobal.IsChecked == true)
            {
                return NetworkRegionOption.Global;
            }

            return NetworkRegionOption.Auto;
        }

        return index switch
        {
            1 => NetworkRegionOption.China,
            2 => NetworkRegionOption.Global,
            _ => NetworkRegionOption.Auto
        };
    }

    private PanelScrollbarVisibility GetSelectedScrollbarVisibility() =>
        // RadioScrollbarVisibility 顺序：Always / OnScroll
        RadioScrollbarVisibility.SelectedIndex == 0 ? PanelScrollbarVisibility.Always : PanelScrollbarVisibility.OnScroll;

    private int GetSelectedClickTrigger()
    {
        // RadioClickType 顺序：左键 / 右键 / 左右键，正好对应 ClickTriggerType 取值。
        int index = RadioClickType.SelectedIndex;
        return index is 1 or 2 ? index : 0;
    }

    private void ApplyScrollbarSettings()
    {
        // 旧版“滚动时临时显示”的 hack 在 WinUI 不需要：OnScroll 直接映射为 Auto。
        ScrollBarVisibility visibility = ConfigManager.ScrollbarVisibility == PanelScrollbarVisibility.Always
            ? ScrollBarVisibility.Visible
            : ScrollBarVisibility.Auto;

        SettingsContentScrollViewer.VerticalScrollBarVisibility = visibility;
        PageWelcome.VerticalScrollBarVisibility = visibility;
        PageAbout.VerticalScrollBarVisibility = visibility;
        LogScrollViewer.VerticalScrollBarVisibility = visibility;
    }

    private void ApplyDarkMode()
    {
        RootGrid.RequestedTheme = App.ResolveElementTheme();
        ApplyTitleBarTheme();
        UpdateEnvironmentFilterInterlock();
    }

    // ==================================================================
    // 基础设置联动
    // ==================================================================

    private void CheckMasterSwitch_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady)
        {
            return;
        }

        UpdateClickEffectPanelVisibility();
    }

    private void UpdateClickEffectPanelVisibility()
    {
        PanelClickEffectOptions.Visibility = CheckMasterSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CheckRunAsAdmin_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady)
        {
            return;
        }

        if (CheckRunAsAdmin.IsOn)
        {
            StatusText.Text = Localization.Get("Status_AdminPending");
            StatusText.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }

    private void CurveDraw_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        // 曲线绘制在点「应用」时统一下发到叠加层（与迁移前一致）。
    }

    // ==================================================================
    // 环境过滤
    // ==================================================================

    private void EnvironmentFilterSetting_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady)
        {
            return;
        }

        UpdateEnvironmentFilterInterlock();
    }

    private void ProcessFilterMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady)
        {
            return;
        }

        if (ComboProfiles.SelectedItem is FilterProfile active)
        {
            active.Mode = GetSelectedProcessFilterMode();
        }

        UpdateEnvironmentFilterInterlock();
    }

    private void UpdateEnvironmentFilterInterlock()
    {
        bool environmentFilterEnabled = CheckEnvironmentFilter.IsOn;
        ProcessFilterModeOption selectedMode = GetSelectedProcessFilterMode();
        bool processFilterEnabled = environmentFilterEnabled && selectedMode != ProcessFilterModeOption.Disabled;

        CheckHideInFullscreen.IsEnabled = environmentFilterEnabled;
        CheckShowEffectOnDesktop.IsEnabled = environmentFilterEnabled;
        ComboProfiles.IsEnabled = environmentFilterEnabled;
        ComboProcessFilterMode.IsEnabled = environmentFilterEnabled;

        // 旧版在深色下换用暗色禁用模板；WinUI 的禁用态由系统负责，这里只保留透明度的细微差别。
        ListConfiguredProcesses.IsEnabled = processFilterEnabled;
        ListConfiguredProcesses.Opacity = processFilterEnabled ? 1.0 : 0.65;
        ManualProcessInput.IsEnabled = processFilterEnabled;
    }

    private void SelectProcessFilterMode(ProcessFilterModeOption mode)
    {
        // ComboProcessFilterMode 顺序：Disabled / Blacklist / Whitelist（与枚举一致）
        int index = (int)mode;
        if (index < 0 || index >= ComboProcessFilterMode.Items.Count)
        {
            index = 0;
        }

        ComboProcessFilterMode.SelectedIndex = index;
    }

    private ProcessFilterModeOption GetSelectedProcessFilterMode()
    {
        int index = ComboProcessFilterMode.SelectedIndex;
        return index switch
        {
            1 => ProcessFilterModeOption.Blacklist,
            2 => ProcessFilterModeOption.Whitelist,
            _ => ProcessFilterModeOption.Disabled
        };
    }

    // ==================================================================
    // 配置组
    // ==================================================================

    private void ComboProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;

        // 这里没有 IsLoaded 守卫：加载阶段也要把当前配置组的进程列表铺上去。
        if (ComboProfiles.SelectedItem is FilterProfile selected)
        {
            RefreshCurrentProfileProcesses(selected);
            SelectProcessFilterMode(selected.Mode);
            UpdateEnvironmentFilterInterlock();
        }
    }

    private void RefreshCurrentProfileProcesses(FilterProfile? profile)
    {
        CurrentProfileProcesses.Clear();
        if (profile == null)
        {
            return;
        }

        foreach (string name in NormalizeProcessNames(profile.Processes))
        {
            CurrentProfileProcesses.Add(name);
        }
    }

    private static IEnumerable<string> NormalizeProcessNames(IEnumerable<string> processes) =>
        processes
            .Select(NormalizeProcessName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeProcessName(string? name)
    {
        string normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (!normalized.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalized += ".exe";
        }

        return normalized;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var newProfile = new FilterProfile { Name = Localization.Format("Profile_NewNumbered", Profiles.Count + 1) };
        Profiles.Add(newProfile);
        ComboProfiles.SelectedItem = newProfile;
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (ComboProfiles.SelectedItem is FilterProfile active)
        {
            NewProfileNameInput.Text = active.Name;
            RenameProfileOverlay.Visibility = Visibility.Visible;
            NewProfileNameInput.Focus(FocusState.Programmatic);
            NewProfileNameInput.SelectAll();
        }
    }

    private void ConfirmRenameProfile_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        string newName = NewProfileNameInput.Text.Trim();
        if (!string.IsNullOrWhiteSpace(newName) && ComboProfiles.SelectedItem is FilterProfile active)
        {
            active.Name = newName;

            // 刷新 ComboBox 显示
            int index = Profiles.IndexOf(active);
            if (index != -1)
            {
                Profiles.RemoveAt(index);
                Profiles.Insert(index, active);
                ComboProfiles.SelectedItem = active;
            }
        }

        RenameProfileOverlay.Visibility = Visibility.Collapsed;
    }

    private void CloseRenameOverlay_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RenameProfileOverlay.Visibility = Visibility.Collapsed;
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (Profiles.Count <= 1)
        {
            NativeMessageBox.Show(Localization.Get("Msg_KeepOneProfile"), Localization.Get("Msg_Info"));
            return;
        }

        if (ComboProfiles.SelectedItem is not FilterProfile active)
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            Localization.Format("Msg_ConfirmDeleteProfile", active.Name),
            Localization.Get("Msg_ConfirmDelete_Title"));

        if (!confirmed)
        {
            return;
        }

        Profiles.Remove(active);
        ComboProfiles.SelectedIndex = 0;
    }

    // ==================================================================
    // 进程列表
    // ==================================================================

    private void RemoveProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        // 行模板把进程名放在 Tag 上（旧版用的是 DataContext）。
        string? processName = button.Tag as string ?? button.DataContext as string;
        if (string.IsNullOrEmpty(processName))
        {
            return;
        }

        if (ComboProfiles.SelectedItem is FilterProfile active)
        {
            active.Processes.RemoveAll(existing =>
                string.Equals(existing, processName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeProcessName(existing), processName, StringComparison.OrdinalIgnoreCase));
            RefreshCurrentProfileProcesses(active);
        }
        else
        {
            CurrentProfileProcesses.Remove(processName);
        }
    }

    private void AddManualProcess_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        string input = NormalizeProcessName(ManualProcessInput.Text);
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        AddProcessToActiveProfile(input);
        ManualProcessInput.Text = string.Empty;
    }

    private async void BrowseProcess_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                AddProcessToActiveProfile(Path.GetFileName(file.Path));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to pick process executable: {ex.Message}");
        }
    }

    private void AddProcessToActiveProfile(string processName)
    {
        if (ComboProfiles.SelectedItem is not FilterProfile active)
        {
            return;
        }

        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        if (!active.Processes.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            active.Processes.Add(normalized);
        }

        RefreshCurrentProfileProcesses(active);
    }

    // ==================================================================
    // 运行中进程
    // ==================================================================

    private void SelectRunningProcess_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        RefreshRunningProcessList();
        SearchRunningProcess.Text = string.Empty;
        ApplyRunningProcessFilter(null);
        RunningProcessOverlay.Visibility = Visibility.Visible;
    }

    private void CloseRunningProcessOverlay_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RunningProcessOverlay.Visibility = Visibility.Collapsed;
    }

    private void ConfirmAddRunningProcesses_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        SyncRunningProcessSelection();
        List<string> selected = _allRunningProcesses
            .Where(item => item.IsSelected)
            .Select(item => item.ProcessName)
            .ToList();

        foreach (string processName in selected)
        {
            AddProcessToActiveProfile(processName);
        }

        RunningProcessOverlay.Visibility = Visibility.Collapsed;
    }

    private void SearchRunningProcess_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = e;
        ApplyRunningProcessFilter(SearchRunningProcess.Text);
    }

    private void RefreshRunningProcessList()
    {
        SyncRunningProcessSelection();
        _allRunningProcesses.Clear();
        RunningProcessList.Clear();

        try
        {
            int selfId = Environment.ProcessId;
            foreach (Process process in Process.GetProcesses().OrderBy(item => item.ProcessName))
            {
                try
                {
                    if (process.Id == selfId)
                    {
                        continue;
                    }

                    IntPtr hwnd;
                    try
                    {
                        hwnd = process.MainWindowHandle;
                    }
                    catch
                    {
                        continue;
                    }

                    if (hwnd == IntPtr.Zero)
                    {
                        continue;
                    }

                    string baseName = process.ProcessName;
                    if (string.IsNullOrEmpty(baseName))
                    {
                        continue;
                    }

                    string processName = NormalizeProcessName(baseName);
                    if (_allRunningProcesses.Any(item => item.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string displayName = processName;
                    try
                    {
                        string? description = process.MainModule?.FileVersionInfo.FileDescription;
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            displayName = description;
                        }
                        else if (!string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            displayName = process.MainWindowTitle;
                        }
                    }
                    catch
                    {
                        if (!string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            displayName = process.MainWindowTitle;
                        }
                    }

                    _allRunningProcesses.Add(new ProcessItem
                    {
                        DisplayName = displayName,
                        ProcessName = processName,
                        IsSelected = false
                    });
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to enumerate running processes: {ex.Message}");
        }
    }

    private void ApplyRunningProcessFilter(string? filter)
    {
        SyncRunningProcessSelection();
        RunningProcessList.Clear();

        foreach (ProcessItem item in _allRunningProcesses)
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                item.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                item.ProcessName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            RunningProcessList.Add(item);
        }

        RestoreRunningProcessSelection();
    }

    /// <summary>把列表控件上的勾选写回数据项（筛选会重建集合，需要记住跨筛选的选择）。</summary>
    private void SyncRunningProcessSelection()
    {
        foreach (ProcessItem item in RunningProcessList)
        {
            item.IsSelected = ListRunningProcesses.SelectedItems.Contains(item);
        }
    }

    private void RestoreRunningProcessSelection()
    {
        foreach (ProcessItem item in RunningProcessList)
        {
            if (item.IsSelected && !ListRunningProcesses.SelectedItems.Contains(item))
            {
                ListRunningProcesses.SelectedItems.Add(item);
            }
        }
    }

    // ==================================================================
    // 视觉表现恢复默认
    // ==================================================================

    private void OpenVisualResetOverlay_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        RebuildVisualResetItems();
        foreach (VisualResetItem item in VisualResetItems)
        {
            item.IsSelected = true;
        }

        SearchVisualReset.Text = string.Empty;
        RefreshVisualResetRows(null);
        VisualResetOverlay.Visibility = Visibility.Visible;
    }

    private void CloseVisualResetOverlay_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        VisualResetOverlay.Visibility = Visibility.Collapsed;
    }

    private void SearchVisualReset_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = e;
        RefreshVisualResetRows(SearchVisualReset.Text);
    }

    private void RebuildVisualResetItems()
    {
        VisualResetItems.Clear();
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.UnifiedEffectScale, Localization.Get("VisualReset_UnifiedScale"), Localization.Get("VisualReset_UnifiedScale_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailEffectScale, Localization.Get("VisualReset_TrailScale"), Localization.Get("VisualReset_TrailScale_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.ClickEffectScale, Localization.Get("VisualReset_ClickScale"), Localization.Get("VisualReset_ClickScale_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.EffectOpacity, Localization.Get("VisualReset_Opacity"), Localization.Get("VisualReset_Opacity_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.UnifiedAnimationSpeed, Localization.Get("VisualReset_UnifiedSpeed"), Localization.Get("VisualReset_UnifiedSpeed_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailAnimationSpeed, Localization.Get("VisualReset_TrailSpeed"), Localization.Get("VisualReset_TrailSpeed_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.ClickAnimationSpeed, Localization.Get("VisualReset_ClickSpeed"), Localization.Get("VisualReset_ClickSpeed_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailRefreshRate, Localization.Get("VisualReset_TrailRefresh"), Localization.Get("VisualReset_TrailRefresh_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.GlowIntensity, Localization.Get("VisualReset_GlowIntensity"), Localization.Get("VisualReset_GlowIntensity_Sub")));
        VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.ParticleColor, Localization.Get("VisualReset_Color"), Localization.Get("VisualReset_Color_Sub")));
    }

    /// <summary>
    /// 标记里这个 ListView 没有 ItemTemplate，因此行（标题 + 副标题 + 复选框）在代码里构建，
    /// 语义与旧版 DataTemplate + IsSelected 一致。
    /// </summary>
    private void RefreshVisualResetRows(string? filter)
    {
        ListVisualResetItems.Items.Clear();

        foreach (VisualResetItem item in VisualResetItems)
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                item.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                item.Subtitle.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            ListVisualResetItems.Items.Add(CreateVisualResetRow(item));
        }
    }

    private static CheckBox CreateVisualResetRow(VisualResetItem item)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = item.Subtitle,
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        var check = new CheckBox
        {
            IsChecked = item.IsSelected,
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = item
        };

        check.Checked += (_, _) => item.IsSelected = true;
        check.Unchecked += (_, _) => item.IsSelected = false;
        return check;
    }

    private void ConfirmVisualReset_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        VisualAppearanceResetFlags flags = VisualAppearanceResetFlags.None;
        foreach (VisualResetItem item in VisualResetItems)
        {
            if (item.IsSelected)
            {
                flags |= item.Flags;
            }
        }

        if (flags == VisualAppearanceResetFlags.None)
        {
            NativeMessageBox.Show(Localization.Get("Msg_SelectVisualReset"), Localization.Get("Msg_VisualReset_Title"));
            return;
        }

        ConfigManager.ApplyVisualAppearanceDefaults(flags);
        LoadSettings();

        int trailRefreshRate = (int)Math.Round(SliderTrailRefresh.Value);
        bool followDisplayRefreshRate = CheckFollowDisplayRefreshRate.IsOn;
        ConfigManager.GetEffectScalesForOverlay(out double trailScale, out double clickScale);
        ConfigManager.GetAnimationSpeedsForOverlay(out double trailSpeed, out double clickSpeed);
        double effectOpacity = Math.Round(SliderOpacity.Value / 100.0, 2);

        App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
        App.Overlay?.UpdateEffectSettings(trailScale, clickScale, effectOpacity, trailSpeed, clickSpeed, ConfigManager.GlowIntensity);
        App.Overlay?.UpdateTrailRefreshRate(trailRefreshRate, followDisplayRefreshRate);
        App.Overlay?.SetCurveDraw(CheckApplyCurveDraw.IsOn);

        VisualResetOverlay.Visibility = Visibility.Collapsed;
        NativeMessageBox.Show(Localization.Get("Msg_VisualResetDone"), Localization.Get("Msg_VisualReset_Title"));
    }

    // ==================================================================
    // 缩放 / 速度联动
    // ==================================================================

    private void LinkedEffectScale_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady || _isLoading)
        {
            return;
        }

        _suppressValueSync = true;
        try
        {
            if (CheckLinkedEffectScale.IsOn)
            {
                double average = Math.Round((SliderTrailScale.Value + SliderClickScale.Value) / 2.0, 2);
                SliderScale.Value = Math.Clamp(average, 0.5, 3.0);
            }
            else
            {
                double value = Math.Clamp(Math.Round(SliderScale.Value, 2), 0.5, 3.0);
                SliderTrailScale.Value = value;
                SliderClickScale.Value = value;
            }

            SyncSliderAndBoxValues();
        }
        finally
        {
            _suppressValueSync = false;
        }

        UpdateEffectScalePanelVisibility();
    }

    private void UpdateEffectScalePanelVisibility()
    {
        bool linked = CheckLinkedEffectScale.IsOn;
        PanelUnifiedEffectScale.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitEffectScale.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LinkedAnimationSpeed_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady || _isLoading)
        {
            return;
        }

        _suppressValueSync = true;
        try
        {
            if (CheckLinkedAnimationSpeed.IsOn)
            {
                double average = Math.Clamp(Math.Round((SliderTrailAnimSpeed.Value + SliderClickAnimSpeed.Value) / 2.0, 2), 0.2, 3.0);
                SliderSpeed.Value = average;
            }
            else
            {
                double value = Math.Clamp(Math.Round(SliderSpeed.Value, 2), 0.2, 3.0);
                SliderTrailAnimSpeed.Value = value;
                SliderClickAnimSpeed.Value = value;
            }

            SyncSliderAndBoxValues();
        }
        finally
        {
            _suppressValueSync = false;
        }

        UpdateAnimationSpeedPanelVisibility();
    }

    private void UpdateAnimationSpeedPanelVisibility()
    {
        bool linked = CheckLinkedAnimationSpeed.IsOn;
        PanelUnifiedAnimationSpeed.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitAnimationSpeed.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GetUiEffectScales(out double trailScale, out double clickScale)
    {
        if (CheckLinkedEffectScale.IsOn)
        {
            double value = Math.Round(SliderScale.Value, 2);
            trailScale = value;
            clickScale = value;
        }
        else
        {
            trailScale = Math.Round(SliderTrailScale.Value, 2);
            clickScale = Math.Round(SliderClickScale.Value, 2);
        }
    }

    private void GetUiAnimationSpeeds(out double trailSpeed, out double clickSpeed)
    {
        if (CheckLinkedAnimationSpeed.IsOn)
        {
            double value = Math.Round(SliderSpeed.Value, 2);
            trailSpeed = value;
            clickSpeed = value;
        }
        else
        {
            trailSpeed = Math.Round(SliderTrailAnimSpeed.Value, 2);
            clickSpeed = Math.Round(SliderClickAnimSpeed.Value, 2);
        }
    }

    // ==================================================================
    // 多屏管理
    // ==================================================================

    private void LoadScreenOptions()
    {
        ScreenOptions.Clear();
        PanelScreenOptions.Children.Clear();
        _screenToggles.Clear();

        List<ScreenInfo> screens = ScreenInfo.AllScreens
            .OrderBy(screen => screen.BoundsLeft)
            .ThenBy(screen => screen.BoundsTop)
            .ToList();

        List<ScreenIdentityInfo> identities = screens.Select(ScreenIdentity.FromScreen).ToList();
        HashSet<string> enabledDeviceNames = ConfigManager.ResolveEnabledScreenDeviceNames(identities);

        for (int index = 0; index < screens.Count; index++)
        {
            ScreenInfo screen = screens[index];
            ScreenIdentityInfo identity = identities[index];
            bool enabled = enabledDeviceNames.Contains(screen.DeviceName);
            // 显示真实显示器名称，减少 DISPLAY1/2 变化误判
            string title = identity.DisplayName + (IsPrimaryScreen(screen) ? Localization.Get("MultiScreen_Primary") : string.Empty);
            string resolution = $"{screen.BoundsWidth} x {screen.BoundsHeight}";
            string detail = Localization.Format(
                "MultiScreen_Detail",
                GetScaleText(screen),
                screen.BoundsLeft,
                screen.BoundsTop,
                screen.DeviceName);

            var item = new ScreenOptionItem
            {
                DisplayIndex = index + 1,
                Title = title,
                ResolutionText = resolution,
                DetailText = detail,
                DeviceName = screen.DeviceName,
                IdentityKey = identity.IdentityKey,
                DisplayName = identity.DisplayName,
                IsEnabled = enabled,
                EnableLabel = Localization.Get("MultiScreen_Enable")
            };

            ScreenOptions.Add(item);
            PanelScreenOptions.Children.Add(CreateScreenRow(item));
        }
    }

    private static bool IsPrimaryScreen(ScreenInfo screen)
    {
        try
        {
            DisplayArea? primary = DisplayArea.Primary;
            if (primary == null)
            {
                return false;
            }

            Windows.Graphics.RectInt32 bounds = primary.OuterBounds;
            return bounds.X == screen.BoundsLeft &&
                   bounds.Y == screen.BoundsTop &&
                   bounds.Width == screen.BoundsWidth &&
                   bounds.Height == screen.BoundsHeight;
        }
        catch
        {
            return false;
        }
    }

    private static string GetScaleText(ScreenInfo screen)
    {
        try
        {
            var center = new NativePoint
            {
                X = screen.BoundsLeft + (screen.BoundsWidth / 2),
                Y = screen.BoundsTop + (screen.BoundsHeight / 2)
            };

            IntPtr monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return Localization.Format("MultiScreen_Scale", 100);
            }

            int hr = GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _);
            if (hr != 0 || dpiX == 0)
            {
                return Localization.Format("MultiScreen_Scale", 100);
            }

            int scale = (int)Math.Round(dpiX / 96.0 * 100);
            return Localization.Format("MultiScreen_Scale", scale);
        }
        catch
        {
            return Localization.Format("MultiScreen_Scale", 100);
        }
    }

    /// <summary>每个显示器一行：名称 / 分辨率 / 位置与缩放 + 启用开关。</summary>
    private FrameworkElement CreateScreenRow(ScreenOptionItem item)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = item.ResolutionText,
            Opacity = 0.8,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0)
        });
        text.Children.Add(new TextBlock
        {
            Text = item.DetailText,
            Opacity = 0.6,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var toggle = new ToggleSwitch
        {
            IsOn = item.IsEnabled,
            OnContent = item.EnableLabel,
            OffContent = string.Empty,
            MinWidth = 0,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (_, _) => item.IsEnabled = toggle.IsOn;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(text);
        row.Children.Add(toggle);

        var border = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row
        };

        Style? cardStyle = TryGetAppResource<Style>("BasCardStyle");
        if (cardStyle != null)
        {
            border.Style = cardStyle;
        }
        else
        {
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(8);
        }

        _screenToggles[item] = toggle;
        return border;
    }

    private static T? TryGetAppResource<T>(string key)
        where T : class
    {
        try
        {
            object? value = Application.Current?.Resources?[key];
            return value as T;
        }
        catch
        {
            return null;
        }
    }

    private void SyncScreenToggles()
    {
        foreach ((ScreenOptionItem item, ToggleSwitch toggle) in _screenToggles)
        {
            if (toggle.IsOn != item.IsEnabled)
            {
                toggle.IsOn = item.IsEnabled;
            }
        }
    }

    public void RefreshScreenEnableLabels()
    {
        string label = Localization.Get("MultiScreen_Enable");
        foreach (ScreenOptionItem item in ScreenOptions)
        {
            item.EnableLabel = label;
            if (_screenToggles.TryGetValue(item, out ToggleSwitch? toggle))
            {
                toggle.OnContent = label;
            }
        }
    }

    private void RefreshScreenOptions_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        HashSet<string> selectedKeys = ScreenOptions
            .Where(item => item.IsEnabled)
            .Select(item => item.IdentityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedIds = ScreenOptions
            .Where(item => item.IsEnabled)
            .Select(item => item.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        LoadScreenOptions();

        foreach (ScreenOptionItem item in ScreenOptions)
        {
            // 手动刷新时保留当前勾选，避免刷新列表本身改变尚未保存的选择
            item.IsEnabled = (selectedKeys.Count == 0 && selectedIds.Count == 0) ||
                             selectedKeys.Contains(item.IdentityKey) ||
                             selectedIds.Contains(item.DeviceName);
        }

        SyncScreenToggles();
    }

    private static ScreenIdentityInfo CreateScreenIdentityInfo(ScreenOptionItem item) =>
        new()
        {
            DeviceName = item.DeviceName,
            IdentityKey = item.IdentityKey,
            DisplayName = item.DisplayName
        };

    // ==================================================================
    // 保存设置
    // ==================================================================

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        DarkModeOption selectedDarkMode = GetSelectedDarkMode();
        string? selectedLanguage = GetSelectedLanguage() ?? _pendingLanguage;
        bool languageChanged = !string.IsNullOrWhiteSpace(selectedLanguage) &&
            !string.Equals(selectedLanguage, _languageAtLoad, StringComparison.OrdinalIgnoreCase);
        NetworkRegionOption selectedNetworkRegion = GetSelectedNetworkRegion();
        bool networkRegionChanged = selectedNetworkRegion != _networkRegionAtLoad;

        if (!string.IsNullOrWhiteSpace(selectedLanguage))
        {
            ConfigManager.Save("UiLanguage", selectedLanguage);
            Localization.ApplyCulture(selectedLanguage);
        }

        bool useLinkedEffectScale = CheckLinkedEffectScale.IsOn;
        double trailEffectScale;
        double clickEffectScale;
        double effectScaleForRegistry;
        if (useLinkedEffectScale)
        {
            effectScaleForRegistry = Math.Round(SliderScale.Value, 2);
            trailEffectScale = effectScaleForRegistry;
            clickEffectScale = effectScaleForRegistry;
        }
        else
        {
            trailEffectScale = Math.Round(SliderTrailScale.Value, 2);
            clickEffectScale = Math.Round(SliderClickScale.Value, 2);
            effectScaleForRegistry = clickEffectScale;
        }

        double effectOpacity = Math.Round(SliderOpacity.Value / 100.0, 2);
        double glowIntensity = Math.Round(SliderGlow.Value, 2);

        bool useLinkedAnimationSpeed = CheckLinkedAnimationSpeed.IsOn;
        double trailAnimSpeed;
        double clickAnimSpeed;
        double effectSpeedForRegistry;
        if (useLinkedAnimationSpeed)
        {
            effectSpeedForRegistry = Math.Round(SliderSpeed.Value, 2);
            trailAnimSpeed = effectSpeedForRegistry;
            clickAnimSpeed = effectSpeedForRegistry;
        }
        else
        {
            trailAnimSpeed = Math.Round(SliderTrailAnimSpeed.Value, 2);
            clickAnimSpeed = Math.Round(SliderClickAnimSpeed.Value, 2);
            effectSpeedForRegistry = clickAnimSpeed;
        }

        int trailRefreshRate = (int)Math.Round(SliderTrailRefresh.Value);
        bool followDisplayRefreshRate = CheckFollowDisplayRefreshRate.IsOn;
        bool autoStartEnabled = CheckAutoStart.IsOn;
        bool startSilentEnabled = CheckStartSilent.IsOn;
        bool runAsAdminEnabled = CheckRunAsAdmin.IsOn;
        bool isTouchscreenEnabled = CheckTouchscreenMode.IsOn;
        bool middleClickEnabled = CheckMiddleClickTrigger.IsOn;
        bool screenshotCompatibilityEnabled = CheckScreenshotCompatibilityMode.IsOn;
        int clickType = GetSelectedClickTrigger();

        bool telemetryEnabled = CheckTelemetry.IsOn;
        bool telemetryWasEnabled = ConfigManager.EnableTelemetry;

        // 保存配置组
        string activeId = (ComboProfiles.SelectedItem as FilterProfile)?.Id ?? string.Empty;
        ConfigManager.SaveProfiles(Profiles.ToList(), activeId);

        ConfigManager.Save("RunAsAdmin", runAsAdminEnabled);
        ConfigManager.Save("IsTouchscreenMode", isTouchscreenEnabled);
        ConfigManager.Save("IsEffectEnabled", CheckMasterSwitch.IsOn);
        ConfigManager.Save("AutoStart", autoStartEnabled);
        ConfigManager.Save("EnableTelemetry", telemetryEnabled);
        ConfigManager.Save("ParticleColor", ConfigManager.ParticleColor);
        ConfigManager.Save("EffectScale", effectScaleForRegistry);
        ConfigManager.Save("UseLinkedEffectScale", useLinkedEffectScale);
        ConfigManager.Save("TrailEffectScale", trailEffectScale);
        ConfigManager.Save("ClickEffectScale", clickEffectScale);
        ConfigManager.Save("EffectOpacity", effectOpacity);
        ConfigManager.Save("GlowIntensity", glowIntensity);
        ConfigManager.Save("UseLinkedAnimationSpeed", useLinkedAnimationSpeed);
        ConfigManager.Save("EffectSpeed", effectSpeedForRegistry);
        ConfigManager.Save("TrailAnimationSpeed", trailAnimSpeed);
        ConfigManager.Save("ClickAnimationSpeed", clickAnimSpeed);
        ConfigManager.Save("TrailRefreshRate", trailRefreshRate);
        ConfigManager.Save("FollowDisplayRefreshRate", followDisplayRefreshRate);
        ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);
        ConfigManager.Save("EnableAlwaysTrailEffect", CheckAlwaysTrailEffectSwitch.IsOn);
        ConfigManager.Save("ScrollbarVisibility", GetSelectedScrollbarVisibility());
        ApplyScrollbarSettings();
        ConfigManager.Save("NetworkRegion", selectedNetworkRegion);
        ConfigManager.Save("DarkMode", selectedDarkMode);
        ConfigManager.Save("StartSilent", startSilentEnabled);
        ConfigManager.Save("EnableEnvironmentFilter", CheckEnvironmentFilter.IsOn);
        ConfigManager.Save("HideInFullscreen", CheckHideInFullscreen.IsOn);
        ConfigManager.Save("ShowEffectOnDesktop", CheckShowEffectOnDesktop.IsOn);
        ConfigManager.Save("ClickTriggerType", clickType);
        ConfigManager.Save("EnableMiddleClickTrigger", middleClickEnabled);
        ConfigManager.Save("ScreenshotCompatibilityMode", screenshotCompatibilityEnabled);
        ConfigManager.Save("ApplyCurveDraw", CheckApplyCurveDraw.IsOn);

        HashSet<string> previousEnabledScreenIds = ConfigManager.ResolveEnabledScreenDeviceNames(
            ScreenOptions.Select(CreateScreenIdentityInfo));
        HashSet<string> selectedIds = ScreenOptions
            .Where(item => item.IsEnabled)
            .Select(item => item.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedIds.Count == 0)
        {
            NativeMessageBox.ShowWarning(Localization.Get("Msg_MinOneScreen"), Localization.Get("Msg_MultiScreen_Title"));
            return;
        }

        // 保存当前可见屏幕的启用状态，离线屏幕的旧设置会在 ConfigManager 中保留
        ConfigManager.SaveScreenSelections(ScreenOptions.Select(item => new ScreenSelectionState
        {
            IdentityKey = item.IdentityKey,
            DeviceName = item.DeviceName,
            DisplayName = item.DisplayName,
            IsEnabled = item.IsEnabled
        }));

        ApplyAutoStartSettings();

        App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
        GetUiEffectScales(out double overlayTrailScale, out double overlayClickScale);
        GetUiAnimationSpeeds(out double overlayTrailSpeed, out double overlayClickSpeed);
        App.Overlay?.UpdateEffectSettings(overlayTrailScale, overlayClickScale, effectOpacity, overlayTrailSpeed, overlayClickSpeed, glowIntensity);
        App.Overlay?.UpdateTrailRefreshRate(trailRefreshRate, followDisplayRefreshRate);
        App.Overlay?.RefreshEnvironmentFilterState();
        App.Overlay?.UpdateTouchMode(isTouchscreenEnabled);
        App.Overlay?.UpdateScreenshotCompatibilityMode(screenshotCompatibilityEnabled);
        App.Overlay?.SetCurveDraw(CheckApplyCurveDraw.IsOn);
        if (!previousEnabledScreenIds.SetEquals(selectedIds))
        {
            App.Overlay?.RefreshScreenSelection();
        }

        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            bool isCurrentAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            if (runAsAdminEnabled && !isCurrentAdmin)
            {
                bool restartAsAdmin = await ConfirmAsync(
                    Localization.Get("Msg_AdminRestart"),
                    Localization.Get("Msg_AdminRestart_Title"));

                if (restartAsAdmin)
                {
                    _skipSaveOnClosing = true;
                    (Application.Current as App)?.RestartApplicationFromPanel();
                    return;
                }
            }
        }

        if (languageChanged)
        {
            // 与迁移前一致：先整体重刷文案（旧版是 UiLocalizer.ApplyControlPanel），再询问是否重启。
            ApplyLocalizedText();
            ApplyAboutLinkVisibility();
            LoadScreenOptions();
            _languageAtLoad = selectedLanguage!;

            bool restart = await ConfirmAsync(
                Localization.Get("Msg_LanguageRestart"),
                Localization.Get("Msg_LanguageRestart_Title"));

            if (restart)
            {
                _skipSaveOnClosing = true;
                (Application.Current as App)?.RestartApplicationFromPanel();
                return;
            }
        }
        else if (networkRegionChanged)
        {
            ApplyLocalizedText();
            ApplyAboutLinkVisibility();
            _networkRegionAtLoad = selectedNetworkRegion;
        }

        if (telemetryEnabled && (!telemetryWasEnabled || networkRegionChanged))
        {
            TelemetryHelper.SendStartupData();
        }

        ApplyDarkMode();
    }

    // ==================================================================
    // 自启动
    // ==================================================================

    private void ApplyAutoStartSettings()
    {
        bool autoStart = CheckAutoStart.IsOn;
        bool runAsAdmin = CheckRunAsAdmin.IsOn;

        string? exePath = AutoStartManager.ResolveExecutablePath(
            Environment.ProcessPath,
            Process.GetCurrentProcess().MainModule?.FileName,
            Assembly.GetExecutingAssembly().Location,
            AppDomain.CurrentDomain.BaseDirectory);

        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        const string regKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        AutoStartPlan plan = AutoStartManager.CreatePlan(autoStart, runAsAdmin);

        try
        {
            // 注册表 Run 条目（普通自启）与计划任务（管理员自启）是两条独立路径，
            // 注册表打开失败不应阻止计划任务的创建/清理
            using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(regKeyPath, true))
            {
                if (key == null)
                {
                    AppLogger.Warn($"Failed to open registry key for auto-start: {regKeyPath}");
                }
                else if (plan.RegistryRunEnabled)
                {
                    key.SetValue(AutoStartManager.RunValueName, AutoStartManager.BuildRunCommand(exePath));
                }
                else
                {
                    key.DeleteValue(AutoStartManager.RunValueName, false);
                }
            }

            ManageTaskScheduler(AutoStartManager.TaskName, exePath, plan.ScheduledTaskEnabled);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to apply auto-start settings: {ex.Message}");
        }
    }

    private static void ManageTaskScheduler(string taskName, string exePath, bool create)
    {
        try
        {
            string arguments = create
                ? $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" --autostart\" /sc onlogon /rl highest /f"
                : $"/delete /tn \"{taskName}\" /f";

            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? string.Empty : "runas"
            };

            using Process? process = Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("任务计划程序配置失败: " + ex.Message);
        }
    }

    private async void ResetConfig_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        bool confirmed = await ConfirmAsync(
            Localization.Get("Msg_ConfirmReset"),
            Localization.Get("Msg_ConfirmReset_Title"));

        if (!confirmed)
        {
            return;
        }

        try
        {
            _skipSaveOnClosing = true;
            ConfigManager.ResetAndClear();
            (Application.Current as App)?.ExitApplication();
        }
        catch (Exception ex)
        {
            _skipSaveOnClosing = false;
            NativeMessageBox.Show(Localization.Format("Msg_DeleteFailed", ex.Message));
        }
    }

    // ==================================================================
    // 更新检查 / 公告
    // ==================================================================

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (_isCheckingUpdate)
        {
            return;
        }

        Button? button = BtnCheckUpdate ?? sender as Button;
        try
        {
            _isCheckingUpdate = true;
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = Localization.Get("About_CheckingUpdate");
            }

            await CheckForUpdates(isManual: true);
        }
        finally
        {
            _isCheckingUpdate = false;
            if (button != null)
            {
                button.IsEnabled = true;
                button.Content = Localization.Get("About_CheckUpdate");
            }
        }
    }

    private async Task CheckForUpdates(bool isManual)
    {
        string updateUrl = Localization.GetRemoteUpdateUrl();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

            string json = await client.GetStringAsync(updateUrl);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string latestVersionStr = root.GetProperty("version").GetString() ?? "0.0.0.0";
            string downloadUrl = root.GetProperty("url").GetString() ?? string.Empty;
            string updateNotes = root.GetProperty("notes").GetString() ?? Localization.Get("Msg_NoUpdateNotes");

            Version latestVersion = new(latestVersionStr);
            Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (currentVersion != null && latestVersion > currentVersion)
            {
                await RunOnUiThreadAsync(async () =>
                {
                    bool download = await ConfirmAsync(
                        Localization.Format("Msg_UpdateAvailable", latestVersionStr, updateNotes),
                        Localization.Get("Msg_UpdateAvailable_Title"));

                    if (download && !string.IsNullOrEmpty(downloadUrl))
                    {
                        Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
                    }
                });
            }
            else if (isManual)
            {
                await RunOnUiThreadAsync(() =>
                {
                    NativeMessageBox.Show(Localization.Get("Msg_UpToDate"), Localization.Get("Msg_CheckUpdate_Title"));
                    return Task.CompletedTask;
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Update check failed: {ex.Message}");
            HandleNetworkFetchFailure(isManual, ex.Message);
        }
    }


    private void HandleNetworkFetchFailure(bool isManual, string errorMessage)
    {
        lock (_networkPromptLock)
        {
            if (!isManual && _autoNetworkFailurePromptShown)
            {
                return;
            }

            if (!isManual)
            {
                _autoNetworkFailurePromptShown = true;
            }
        }

        _ = RunOnUiThreadAsync(() => PromptSwitchNetworkSourceAsync(isManual, errorMessage));
    }

    private async Task PromptSwitchNetworkSourceAsync(bool isManual, string errorMessage)
    {
        NetworkRegionOption alternateRegion = GetAlternateNetworkRegion();
        string currentLabel = GetNetworkRegionLabel(ConfigManager.NetworkRegion);
        string alternateLabel = GetNetworkRegionLabel(alternateRegion);
        string message = isManual
            ? Localization.Format("Msg_SwitchNetworkSourcePromptManual", errorMessage, currentLabel, alternateLabel)
            : Localization.Format("Msg_SwitchNetworkSourcePromptAuto", alternateLabel);

        bool switchSource = await ConfirmAsync(message, Localization.Get("Msg_SwitchNetworkSource_Title"));
        if (!switchSource)
        {
            return;
        }

        ConfigManager.Save("NetworkRegion", alternateRegion);
        _networkRegionAtLoad = alternateRegion;
        SelectNetworkRegion(alternateRegion);
        ApplyLocalizedText();
        ApplyAboutLinkVisibility();
        AppLogger.Info($"Network source switched to {alternateRegion}.");

        if (isManual)
        {
            _ = CheckForUpdates(isManual: true);
        }
    }

    private static NetworkRegionOption GetAlternateNetworkRegion() =>
        Localization.UseChinaNetworkEndpoint()
            ? NetworkRegionOption.Global
            : NetworkRegionOption.China;

    private static string GetNetworkRegionLabel(NetworkRegionOption region) =>
        region switch
        {
            NetworkRegionOption.China => Localization.Get("Basic_NetworkRegionChina"),
            NetworkRegionOption.Global => Localization.Get("Basic_NetworkRegionGlobal"),
            _ => Localization.Get("Basic_NetworkRegionAuto")
        };

    private static void ShowWindowsNotification(string title, string content)
    {
        try
        {
            new ToastContentBuilder().AddText(title).AddText(content).Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("通知推送失败: " + ex.Message);
        }
    }

    // ==================================================================
    // 对话框 / UI 线程
    // ==================================================================

    /// <summary>Yes/No 确认框。ContentDialog 需要 XamlRoot，早于窗口加载的场景会先等 Loaded。</summary>
    private async Task<bool> ConfirmAsync(string message, string title)
    {
        bool gateHeld = false;
        try
        {
            await _dialogGate.WaitAsync();
            gateHeld = true;

            XamlRoot? root = await EnsureXamlRootAsync();
            if (root == null || _isClosed)
            {
                AppLogger.Warn("Cannot show confirmation dialog: XamlRoot unavailable.");
                return false;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                // 通用文案里没有 Yes/No，沿用已有的本地化「确定 / 取消」。
                PrimaryButtonText = Localization.Get("ColorPicker_Confirm"),
                CloseButtonText = Localization.Get("Overlay_Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to show confirmation dialog: {ex.Message}");
            return false;
        }
        finally
        {
            if (gateHeld)
            {
                _dialogGate.Release();
            }
        }
    }

    private async Task<XamlRoot?> EnsureXamlRootAsync()
    {
        if (RootGrid.XamlRoot != null)
        {
            return RootGrid.XamlRoot;
        }

        var completion = new TaskCompletionSource();
        void OnLoaded(object sender, RoutedEventArgs e) => completion.TrySetResult();

        RootGrid.Loaded += OnLoaded;
        try
        {
            await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            RootGrid.Loaded -= OnLoaded;
        }

        return RootGrid.XamlRoot;
    }

    /// <summary>网络回调可能落在任意线程，统一回到 UI 线程再碰控件。</summary>
    private static Task RunOnUiThreadAsync(Func<Task> work)
    {
        DispatcherQueue queue = App.DispatcherQueue;
        if (queue.HasThreadAccess)
        {
            return work();
        }

        var completion = new TaskCompletionSource();
        bool enqueued = queue.TryEnqueue(async () =>
        {
            try
            {
                await work();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }

    // ==================================================================
    // 系统事件与关闭
    // ==================================================================

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _ = sender;

        if (ConfigManager.DarkMode != DarkModeOption.System ||
            (e.Category != UserPreferenceCategory.General &&
             e.Category != UserPreferenceCategory.VisualStyle &&
             e.Category != UserPreferenceCategory.Color) ||
            _isClosed ||
            Interlocked.Exchange(ref _themeRefreshPending, 1) != 0)
        {
            return;
        }

        App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            Interlocked.Exchange(ref _themeRefreshPending, 0);
            if (!_isClosed && ConfigManager.DarkMode == DarkModeOption.System)
            {
                ApplyDarkMode();
            }
        });
    }

    private void ControlPanelWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;

        _isClosed = true;

        if (!_skipSaveOnClosing)
        {
            ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);
        }

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        AppLogger.EntryAdded -= OnAppLogEntryAdded;

        try
        {
            _refreshTimer?.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to stop panel timers during close: {ex.Message}");
        }
    }
}
