using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
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
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace BASpark;

public sealed class ColorPresetLayout : NonVirtualizingLayout
{
    private const int Columns = 8;
    private const double Side = 28;
    private const double Spacing = 6;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        foreach (UIElement child in context.Children)
        {
            child.Measure(new Size(Side, Side));
        }

        int rows = (context.Children.Count + Columns - 1) / Columns;
        return new Size(Columns * Side + (Columns - 1) * Spacing, rows * (Side + Spacing) - (rows > 0 ? Spacing : 0));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        double scale = context.Children.Count > 0 ? context.Children[0].XamlRoot?.RasterizationScale ?? 1 : 1;
        for (int index = 0; index < context.Children.Count; index++)
        {
            double left = Math.Round(index % Columns * (Side + Spacing) * scale) / scale;
            double top = Math.Round(index / Columns * (Side + Spacing) * scale) / scale;
            context.Children[index].Arrange(new Rect(left, top, Side, Side));
        }

        return finalSize;
    }
}

public sealed class SegmentedRadioLayout : NonVirtualizingLayout
{
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        int count = context.Children.Count;
        if (count == 0)
        {
            return new Size();
        }

        double scale = context.Children[0].XamlRoot?.RasterizationScale ?? 1;
        var desiredWidths = new double[count];
        double height = 0;
        for (int index = 0; index < count; index++)
        {
            UIElement child = context.Children[index];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            desiredWidths[index] = Math.Ceiling(child.DesiredSize.Width * scale) / scale;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        context.LayoutState = desiredWidths;
        return new Size(desiredWidths.Sum(), height);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        int count = context.Children.Count;
        if (count == 0) return finalSize;
        double scale = count > 0 ? context.Children[0].XamlRoot?.RasterizationScale ?? 1 : 1;
        var desiredWidths = (double[])context.LayoutState;
        double left = 0;
        double position = 0;
        for (int index = 0; index < count; index++)
        {
            position += desiredWidths[index];
            double right = index == count - 1 ? finalSize.Width : Math.Round(position * scale) / scale;
            context.Children[index].Arrange(new Rect(left, 0, right - left, finalSize.Height));
            left = right;
        }

        return finalSize;
    }
}

public abstract class SelectionCardItem : INotifyPropertyChanged
{
    private bool _isSelected;
    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    public Visibility SubtitleVisibility => string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SelectionCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate CardTemplate { get; set; } = null!;
    public DataTemplate HeadingTemplate { get; set; } = null!;
    protected override DataTemplate SelectTemplateCore(object item) => item is string ? HeadingTemplate : CardTemplate;
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>运行中进程选择列表的一行。</summary>
public class ProcessItem : SelectionCardItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public override string Title => DisplayName;
    public override string Subtitle => DisplayName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase) ? string.Empty : ProcessName;
}

/// <summary>视觉表现「恢复默认」列表的一项。</summary>
public class VisualResetItem : SelectionCardItem
{
    public VisualAppearanceResetFlags Flags { get; }
    public override string Title { get; }
    public override string Subtitle { get; }
    public Action? Restore { get; set; }
    public Action? Save { get; set; }
    public string? SettingKey { get; set; }
    public string? SelectionId { get; set; }
    public string Group { get; set; } = string.Empty;

    public VisualResetItem(string title, string subtitle, Action restore)
        : this(VisualAppearanceResetFlags.None, title, subtitle)
    {
        Restore = restore;
    }

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
///   * 确认与纯提示均使用绑定当前 XamlRoot 的原生 ContentDialog。
///   * 旧版「滚动时临时显示滚动条」的 hack 去掉：OnScroll 直接映射为 Auto。
/// </summary>
public sealed partial class ControlPanelWindow : UserControl
{
    private sealed class SegmentedSelectorAnimator : IDisposable
    {
        private readonly RadioButtons _selector;
        private Border? _selection;
        private ItemsRepeater? _repeater;
        private CompositionPropertySet? _translation;
        private int _selectedIndex = -1;
        private double _targetX = double.NaN;
        private bool _queued;
        private bool _animatePending;
        private bool _disposed;

        public SegmentedSelectorAnimator(RadioButtons selector)
        {
            _selector = selector;
            selector.Loaded += Selector_Loaded;
            selector.SizeChanged += Selector_SizeChanged;
            selector.SelectionChanged += Selector_SelectionChanged;
        }

        private void Selector_Loaded(object sender, RoutedEventArgs args)
        {
            _selector.ApplyTemplate();
            _selection = FindVisualDescendant<Border>(_selector, "SegmentedSelection");
            _repeater = FindVisualDescendant<ItemsRepeater>(_selector, "InnerRepeater");
            if (_selection != null)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(_selection, true);
                _translation = ElementCompositionPreview.GetElementVisual(_selection).Properties;
            }
            QueueUpdate(false);
        }

        private void Selector_SizeChanged(object sender, SizeChangedEventArgs args) => QueueUpdate(false);
        private void Selector_SelectionChanged(object sender, SelectionChangedEventArgs args) => QueueUpdate(true);

        public void RefreshLayout()
        {
            _repeater?.InvalidateMeasure();
            _selector.InvalidateMeasure();
            QueueUpdate(false);
        }

        private void QueueUpdate(bool animate)
        {
            if (_disposed) return;
            _animatePending |= animate;
            if (_queued) return;
            _queued = true;
            App.DispatcherQueue.TryEnqueue(() =>
            {
                _queued = false;
                bool shouldAnimate = _animatePending;
                _animatePending = false;
                if (!_disposed) Update(shouldAnimate);
            });
        }

        private void Update(bool animate)
        {
            if (!_selector.IsLoaded || _selection == null || _repeater == null || _translation == null) return;
            _selector.UpdateLayout();
            double scale = _selector.XamlRoot.RasterizationScale;
            if (_selector.Parent is Border frame)
            {
                double stroke = Math.Max(1, Math.Round(scale, MidpointRounding.AwayFromZero)) / scale;
                double padding = Math.Round(2 * scale, MidpointRounding.AwayFromZero) / scale;
                var thickness = new Thickness(stroke);
                var inset = new Thickness(padding);
                if (frame.BorderThickness != thickness) frame.BorderThickness = thickness;
                if (frame.Padding != inset) frame.Padding = inset;
            }
            int index = _selector.SelectedIndex;
            if (index < 0 || _repeater.TryGetElement(index) is not FrameworkElement cell || cell.ActualWidth <= 0)
            {
                _selection.Opacity = 0;
                _selectedIndex = -1;
                return;
            }
            Point position = cell.TransformToVisual(_repeater).TransformPoint(new Point());
            if (_selection.Width != cell.ActualWidth) _selection.Width = cell.ActualWidth;
            if (_selection.Height != _repeater.ActualHeight) _selection.Height = _repeater.ActualHeight;
            _selection.Opacity = 1;
            if (_selectedIndex == index && Math.Abs(_targetX - position.X) < 0.01) return;
            if (animate && _selectedIndex >= 0)
            {
                var compositor = _translation.Compositor;
                var animation = compositor.CreateScalarKeyFrameAnimation();
                animation.InsertExpressionKeyFrame(0, "this.StartingValue");
                animation.InsertKeyFrame(1, (float)position.X,
                    compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1)));
                animation.Duration = TimeSpan.Parse((string)Application.Current.Resources["ControlFastAnimationDuration"], CultureInfo.InvariantCulture);
                _translation.StartAnimation("Translation.X", animation);
            }
            else
            {
                _translation.StopAnimation("Translation.X");
                _translation.InsertVector3("Translation", new Vector3((float)position.X, 0, 0));
            }
            _selectedIndex = index;
            _targetX = position.X;
        }

        public void Dispose()
        {
            _disposed = true;
            _translation?.StopAnimation("Translation.X");
            _selector.Loaded -= Selector_Loaded;
            _selector.SizeChanged -= Selector_SizeChanged;
            _selector.SelectionChanged -= Selector_SelectionChanged;
        }
    }
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BASparkClient/1.0";
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    // 初始尺寸。
    private const int DesignWidth = 800;
    private const int DesignHeight = 710;

    private NavigationViewItem? _selectedSettingsItem;
    private readonly Dictionary<NavigationViewItem, (FrameworkElement Indicator, bool Active)> _navigationIndicators = new();
    private readonly SolidColorBrush _navigationIndicatorPlaceholderBrush = new(Colors.Transparent);
    private readonly List<(NavigationViewItem Item, DependencyProperty Property, long Token)> _navigationIndicatorCallbacks = new();
    private bool _navigationIndicatorQueued;
    private bool _animateNavigationIndicator;
    private ToggleSwitch? _pressedToggleSwitch;
    private bool _toggleDoubleTapPending;
    private uint _togglePointerId;
    private Point _togglePressPosition;

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

    private readonly DcompPanelHost _host;
    private readonly DispatcherQueueTimer? _refreshTimer;

    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly Dictionary<Panel, Storyboard> _settingsAnimations = new();
    private sealed class ModalDialogState
    {
        public TaskCompletionSource<bool> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CloseRequested { get; set; }
    }
    private readonly Dictionary<ContentDialog, ModalDialogState> _modalDialogs = new();
    private readonly Dictionary<ContentDialog, Border> _dialogPresentationGuards = new();
    private readonly HashSet<ContentDialog> _dialogPresentationsPending = new();
    private readonly List<ProcessItem> _allRunningProcesses = new();
    private readonly Dictionary<ScreenOptionItem, ToggleSwitch> _screenToggles = new();
    private bool _syncingColorControls;
    private CompositionRoundedRectangleGeometry? _colorCardGeometry;
    private Border? _colorContentHost;
    private bool _colorCardAnimating;
    private int _colorCardAnimationGeneration;
    private ColorPickerSlider? _colorAlphaSlider;
    private double _effectOpacity = 1;
    private string _particleColor = ConfigManager.ParticleColor;
    private bool _colorPreviewPending;
    private Color _pendingPreviewColor;
    private bool _colorPreviewSubscribed;
    private bool _isColorDragging;
    private long _lastColorLabelUpdate;
    private readonly Dictionary<int, int> _presetColorIndices = new();
    private readonly SolidColorBrush _previewColorBrush = new();
    private Microsoft.UI.Xaml.Controls.Primitives.ColorSpectrum? _configuredColorSpectrum;
    private readonly List<SegmentedSelectorAnimator> _segmentedSelectors = new();
    private readonly Dictionary<Slider, NumberBox> _sliderToBox = new();
    private readonly Dictionary<NumberBox, Slider> _boxToSlider = new();

    private bool _isLoading;
    private bool _isClosed;
    private bool _skipSaveOnClosing;
    private bool _suppressValueSync;
    private bool _logViewInitialized;
    private int _themeRefreshPending;
    private string _languageAtLoad = Localization.CultureZhCn;
    private string? _pendingLanguage;
    private enum ResetScope { Visual, Basic, Filter, Screens, All }
    private ResetScope _resetScope;
    [Flags]
    private enum SettingsSections { General = 1, Visual = 2, Screens = 4, All = 7 }
    private sealed record SettingsState(string General, string Visual, string Screens);
    private SettingsState? _savedSettingsState;
    private SettingsState? _currentSettingsState;
    private SettingsSections _pendingSettingsSections;
    private bool _settingsCheckQueued;
    private bool _isApplyingSettings;
    private ContentDialog? _messageDialog;
    private readonly List<(DependencyObject Control, DependencyProperty Property, long Token)> _settingsCallbacks = new();
    private readonly Dictionary<NumberBox, TextBox> _numberInputs = new();


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
        WindowChrome.ApplyTitleBarIcon(AppTitleBar);
        _host = new DcompPanelHost();

        try
        {
            _languageAtLoad = string.IsNullOrWhiteSpace(ConfigManager.UiLanguage)
                ? Localization.CurrentCultureName
                : ConfigManager.UiLanguage;

            ApplyWindowChrome();
            foreach (NavigationViewItem item in new[] { TabWelcome, TabSettings, SubTabBasic, SubTabVisual, SubTabFilter, SubTabMultiScreen, SubTabBackup, TabLog, TabAbout })
                foreach (DependencyProperty property in new[] { NavigationViewItem.IsExpandedProperty, NavigationViewItem.IsChildSelectedProperty })
                    _navigationIndicatorCallbacks.Add((item, property, item.RegisterPropertyChangedCallback(property, (_, _) => QueueNavigationIndicatorUpdate(true))));
            var dangerResources = (ResourceDictionary)Resources["DangerButtonResources"];
            foreach (Button button in new[] { BtnResetSettings, BtnDeleteProfile, BtnExitApplication, BtnClearLog })
                foreach (var theme in dangerResources.ThemeDictionaries)
                {
                    var resources = new ResourceDictionary();
                    foreach (var resource in (ResourceDictionary)theme.Value) resources[resource.Key] = resource.Value;
                    button.Resources.ThemeDictionaries[theme.Key] = resources;
                }
            BindCollections();
            SetupSliderPairs();
            ConfigureInputControls();
            foreach (var selector in new[] { RadioDarkMode, RadioClickType })
            {
                _segmentedSelectors.Add(new SegmentedSelectorAnimator(selector));
            }
            var checkerBrush = CreateColorCheckerBrush();
            ColorPreviewCheckers.Fill = checkerBrush;
            ExpandedColorPreviewCheckers.Fill = checkerBrush;
            ColorPreviewFill.Background = _previewColorBrush;
            ExpandedColorPreviewFill.Background = _previewColorBrush;
            EffectColorPicker.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ColorPicker_PointerPressed), true);
            EffectColorPicker.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ColorPicker_PointerFinished), true);
            EffectColorPicker.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ColorPicker_PointerFinished), true);
            EffectColorPicker.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ColorPicker_PointerFinished), true);
            RootGrid.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(RootGrid_PointerPressed), true);
            RootGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ColorPicker_PointerFinished), true);
            RootGrid.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ColorPicker_PointerFinished), true);
            RootGrid.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(ToggleSwitch_DoubleTapped), true);
            RootGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ToggleSwitch_PointerReleased), true);
            RootGrid.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ToggleSwitch_PointerCanceled), true);

            ComboProfiles.ItemsSource = Profiles;
            ListConfiguredProcesses.ItemsSource = CurrentProfileProcesses;
            CurrentProfileProcesses.CollectionChanged += (_, _) => UpdateConfiguredProcessSummary();
            ListRunningProcesses.ItemsSource = RunningProcessList;

            ApplyLocalizedText();
            PopulateLanguageCombo();
            LoadVersion();
            LoadSettings();
            ApplyScrollbarSettings();
            LoadScreenOptions();
            ApplyDarkMode();
            CheckAdminStatus();
            UpdatePageVisibility();
            InitLogView();
            InitializeSettingsTracking();

            _host.CloseRequested += ControlPanelWindow_Closed;
            _host.SizeChanged += (_, _) => UpdateCaptionButtonState();
            RootGrid.Loaded += RootGrid_Loaded;
            RootGrid.SizeChanged += (_, _) =>
            {
                StopSettingsAnimations();
                UpdateCaptionButtonBounds();
                if (_messageDialog != null) UpdateDialogScrim(_messageDialog);
                foreach (ContentDialog dialog in _modalDialogs.Keys) UpdateDialogScrim(dialog);
            };
            CaptionButtons.SizeChanged += (_, _) => UpdateCaptionButtonBounds();

            _refreshTimer = App.DispatcherQueue.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(500);
            _refreshTimer.IsRepeating = true;
            _refreshTimer.Tick += RefreshTimer_OnTick;

            _host.SetContent(this);
            AppLogger.EntryAdded += OnAppLogEntryAdded;
            App.EffectsPauseChanged += EffectsPauseChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            _refreshTimer.Start();
        }
        catch
        {
            Close();
            throw;
        }
    }

    public IntPtr Handle => _host.Handle;

    public event EventHandler? Closed;

    public void Activate() => _host.Show();

    public void Close() => ControlPanelWindow_Closed(this, EventArgs.Empty);

    /// <summary>UI 已加载且窗口未关闭时，控件事件才会产生副作用（等价于旧版的 IsLoaded 判断）。</summary>
    private bool IsUiReady => !_isClosed && RootGrid is { IsLoaded: true };

    // ==================================================================
    // 窗口外观
    // ==================================================================

    private void ApplyWindowChrome()
    {
        _host.SetTitle(Localization.Get("App_Title_ControlPanel"));

        RootGrid.RequestedTheme = App.ResolveElementTheme();

        _selectedSettingsItem = SubTabBasic;

        // 页面切换动画需要在不透明变换上做位移。
        foreach (FrameworkElement page in new FrameworkElement[]
                 {
                     PageWelcome, PageSettings, PageLog, PageAbout,
                     SectionBasic, SectionVisual, SectionFilter, SectionMultiScreen, SectionBackup
                 })
        {
            page.RenderTransform = new TranslateTransform();
        }

        try
        {
            _host.CenterOnCurrentDisplay(DesignWidth, DesignHeight);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to size/center control panel: {ex.Message}");
        }
    }

    private void UpdateCaptionButtonBounds()
    {
        if (_isClosed || !RootGrid.IsLoaded || CaptionButtons.ActualWidth <= 0)
        {
            return;
        }

        Windows.Foundation.Rect bounds = CaptionButtons.TransformToVisual(RootGrid).TransformBounds(
            new Windows.Foundation.Rect(0, 0, CaptionButtons.ActualWidth, CaptionButtons.ActualHeight));
        double scale = XamlRoot.RasterizationScale;
        int left = (int)Math.Floor(bounds.X * scale);
        int top = (int)Math.Floor(bounds.Y * scale);
        int right = (int)Math.Ceiling((bounds.X + bounds.Width) * scale);
        int bottom = (int)Math.Ceiling((bounds.Y + bounds.Height) * scale);
        _host.SetCaptionButtonsBounds(new Windows.Graphics.RectInt32(left, top, right - left, bottom - top));
    }

    private void UpdateCaptionButtonState()
    {
        bool maximized = _host.IsMaximized;
        CaptionMaximizeIcon.Glyph = maximized ? "\uE923" : "\uE922";
        SetCaptionButtonLabel(BtnCaptionMaximize, maximized ? "Window_Restore" : "Window_Maximize");
    }

    private static void SetCaptionButtonLabel(Button button, string key)
    {
        string label = Localization.Get(key);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
    }

    private void CaptionMinimize_Click(object sender, RoutedEventArgs args) => _host.Minimize();

    private void CaptionMaximize_Click(object sender, RoutedEventArgs args) => _host.ToggleMaximize();

    private void CaptionClose_Click(object sender, RoutedEventArgs args) => _host.RequestClose();

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        XamlRoot.Changed += PanelXamlRoot_Changed;
        UpdateCaptionButtonBounds();
        UpdateCaptionButtonState();
        ApplyTitleBarTheme();
    }

    /// <summary>WinUI 只负责内容区主题，标题栏按钮需要自己跟随（等价于旧的 ThemeManager.ApplyTitleBar）。</summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            WindowChrome.ApplyTitleBarTheme(Handle, IsDarkThemeEffective());
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Title bar theming unavailable: {ex.Message}");
        }
    }

    private void PanelXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateCaptionButtonBounds();
    private void RefreshTimer_OnTick(DispatcherQueueTimer sender, object args) => RefreshTimer_Tick();

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

    private void InitializeSettingsTracking()
    {
        foreach (ToggleSwitch toggle in new[] { CheckMasterSwitch, CheckAutoStart, CheckStartSilent, CheckHideTrayIcon,
                     CheckAlwaysTrailEffectSwitch, CheckRunAsAdmin, CheckTouchscreenMode, CheckMiddleClickTrigger,
                     CheckScreenshotCompatibilityMode, CheckEnvironmentFilter, CheckHideInFullscreen, CheckShowEffectOnDesktop })
            TrackSettingsProperty(toggle, ToggleSwitch.IsOnProperty, SettingsSections.General);
        foreach (ToggleSwitch toggle in new[] { CheckLinkedEffectScale, CheckLinkedAnimationSpeed, CheckApplyCurveDraw, CheckFollowDisplayRefreshRate })
            TrackSettingsProperty(toggle, ToggleSwitch.IsOnProperty, SettingsSections.Visual);
        foreach (ComboBox combo in new[] { ComboLanguage, ComboProfiles, ComboProcessFilterMode })
            TrackSettingsProperty(combo, ComboBox.SelectedIndexProperty, SettingsSections.General);
        foreach (RadioButtons selector in new[] { RadioDarkMode, RadioClickType })
            TrackSettingsProperty(selector, RadioButtons.SelectedIndexProperty, SettingsSections.General);
        foreach (Slider slider in _sliderToBox.Keys)
            TrackSettingsProperty(slider, Slider.ValueProperty, SettingsSections.Visual);
        TrackSettingsProperty(EffectColorPicker, ColorPicker.ColorProperty, SettingsSections.Visual);
        TrackSettingsProperty(EffectColorHexInput, TextBox.TextProperty, SettingsSections.Visual);
        Profiles.CollectionChanged += GeneralSettingsCollectionChanged;
        CurrentProfileProcesses.CollectionChanged += GeneralSettingsCollectionChanged;
        ScreenOptions.CollectionChanged += ScreenSettingsCollectionChanged;
        CaptureSettingsBaseline();
    }

    private void TrackSettingsProperty(DependencyObject control, DependencyProperty property, SettingsSections sections)
    {
        long token = control.RegisterPropertyChangedCallback(property, (_, _) =>
        {
            if (!_syncingColorControls) QueueSettingsChangeCheck(sections);
        });
        _settingsCallbacks.Add((control, property, token));
    }

    private void GeneralSettingsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) =>
        QueueSettingsChangeCheck(SettingsSections.General);

    private void ScreenSettingsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) =>
        QueueSettingsChangeCheck(SettingsSections.Screens);

    private string CaptureGeneralSettings() => JsonSerializer.Serialize(new
    {
        UiLanguage = GetSelectedLanguage(), DarkMode = GetSelectedDarkMode(), ClickTriggerType = GetSelectedClickTrigger(),
        IsEffectEnabled = CheckMasterSwitch.IsOn, AutoStart = CheckAutoStart.IsOn, StartSilent = CheckStartSilent.IsOn,
        HideTrayIcon = CheckHideTrayIcon.IsOn, EnableAlwaysTrailEffect = CheckAlwaysTrailEffectSwitch.IsOn,
        RunAsAdmin = CheckRunAsAdmin.IsOn, IsTouchscreenMode = CheckTouchscreenMode.IsOn,
        EnableMiddleClickTrigger = CheckMiddleClickTrigger.IsOn, ScreenshotCompatibilityMode = CheckScreenshotCompatibilityMode.IsOn,
        EnableEnvironmentFilter = CheckEnvironmentFilter.IsOn, HideInFullscreen = CheckHideInFullscreen.IsOn,
        ShowEffectOnDesktop = CheckShowEffectOnDesktop.IsOn, ActiveProfileId = (ComboProfiles.SelectedItem as FilterProfile)?.Id,
        Profiles = Profiles.OrderBy(profile => profile.Id, StringComparer.Ordinal).Select(profile => new
        {
            profile.Id, profile.Name, profile.Mode,
            Processes = profile.Processes.Select(name => name.ToUpperInvariant()).OrderBy(name => name, StringComparer.Ordinal).ToArray()
        }).ToArray()
    });

    private double GetPendingSliderValue(Slider slider)
    {
        if (_numberInputs.TryGetValue(_sliderToBox[slider], out TextBox? input) && input.FocusState != FocusState.Unfocused &&
            double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) && double.IsFinite(value))
            return Math.Round(Math.Clamp(value, slider.Minimum, slider.Maximum), 2);
        return Math.Round(slider.Value, 2);
    }

    private string CaptureVisualSettings()
    {
        string color = _particleColor;
        double opacity = _effectOpacity;
        if (EffectColorHexInput.FocusState != FocusState.Unfocused && ColorPickerColorMath.TryParseHex(EffectColorHexInput.Text, out Color pending))
        {
            color = ToRgbString(pending);
            opacity = Math.Clamp(pending.A / 255.0, 0.1, 1);
        }
        return JsonSerializer.Serialize(new
        {
            UseLinkedEffectScale = CheckLinkedEffectScale.IsOn,
            EffectScale = GetPendingSliderValue(SliderScale), TrailEffectScale = GetPendingSliderValue(SliderTrailScale),
            ClickEffectScale = GetPendingSliderValue(SliderClickScale), GlowIntensity = GetPendingSliderValue(SliderGlow),
            UseLinkedAnimationSpeed = CheckLinkedAnimationSpeed.IsOn,
            EffectSpeed = GetPendingSliderValue(SliderSpeed), TrailAnimationSpeed = GetPendingSliderValue(SliderTrailAnimSpeed),
            ClickAnimationSpeed = GetPendingSliderValue(SliderClickAnimSpeed),
            ApplyCurveDraw = CheckApplyCurveDraw.IsOn, FollowDisplayRefreshRate = CheckFollowDisplayRefreshRate.IsOn,
            TrailRefreshRate = GetPendingSliderValue(SliderTrailRefresh), ParticleColor = color, EffectOpacity = Math.Round(opacity, 2)
        });
    }

    private string CaptureScreenSettings() => JsonSerializer.Serialize(ScreenOptions
        .OrderBy(item => item.IdentityKey, StringComparer.Ordinal).ThenBy(item => item.DeviceName, StringComparer.Ordinal)
        .Select(item => new { item.IdentityKey, item.DeviceName, item.IsEnabled }).ToArray());

    private SettingsState CaptureSettingsState() => new(CaptureGeneralSettings(), CaptureVisualSettings(), CaptureScreenSettings());

    private void CaptureSettingsBaseline()
    {
        _savedSettingsState = _currentSettingsState = CaptureSettingsState();
        BtnApplySettings.IsEnabled = false;
    }

    private void MarkResetSettingsSaved(IEnumerable<VisualResetItem> selected)
    {
        if (_savedSettingsState == null) return;
        var general = System.Text.Json.Nodes.JsonNode.Parse(_savedSettingsState.General)!.AsObject();
        var visual = System.Text.Json.Nodes.JsonNode.Parse(_savedSettingsState.Visual)!.AsObject();
        var screens = System.Text.Json.Nodes.JsonNode.Parse(_savedSettingsState.Screens)!.AsArray();
        var currentGeneral = System.Text.Json.Nodes.JsonNode.Parse(CaptureGeneralSettings())!.AsObject();
        var currentVisual = System.Text.Json.Nodes.JsonNode.Parse(CaptureVisualSettings())!.AsObject();
        var currentScreens = System.Text.Json.Nodes.JsonNode.Parse(CaptureScreenSettings())!.AsArray();
        foreach (VisualResetItem item in selected)
        {
            if (item.SettingKey is not string key) continue;
            if (key == "Screen")
            {
                var savedScreen = screens.FirstOrDefault(screen => GetScreenResetKey(screen!) == item.SelectionId);
                var currentScreen = currentScreens.FirstOrDefault(screen => GetScreenResetKey(screen!) == item.SelectionId);
                if (savedScreen != null && currentScreen != null) savedScreen["IsEnabled"] = currentScreen["IsEnabled"]!.DeepClone();
            }
            else if (key.StartsWith("Profile.", StringComparison.Ordinal))
            {
                string property = key["Profile.".Length..];
                var savedProfile = general["Profiles"]!.AsArray().FirstOrDefault(profile => profile!["Id"]!.GetValue<string>() == item.SelectionId);
                var currentProfile = currentGeneral["Profiles"]!.AsArray().FirstOrDefault(profile => profile!["Id"]!.GetValue<string>() == item.SelectionId);
                if (savedProfile != null && currentProfile != null) savedProfile[property] = currentProfile[property]!.DeepClone();
            }
            else if (currentVisual.ContainsKey(key))
            {
                visual[key] = currentVisual[key]?.DeepClone();
                if (key == "ParticleColor") visual["EffectOpacity"] = currentVisual["EffectOpacity"]?.DeepClone();
            }
            else if (currentGeneral.ContainsKey(key))
            {
                general[key] = currentGeneral[key]?.DeepClone();
                if (key == "Profiles") general["ActiveProfileId"] = currentGeneral["ActiveProfileId"]?.DeepClone();
            }
        }
        _savedSettingsState = new SettingsState(general.ToJsonString(), visual.ToJsonString(), screens.ToJsonString());
        UpdateApplySettingsState(SettingsSections.All);
    }

    private static string GetScreenResetKey(System.Text.Json.Nodes.JsonNode screen) =>
        screen["IdentityKey"]?.GetValue<string>() is { Length: > 0 } identity ? identity : screen["DeviceName"]!.GetValue<string>();

    private void QueueSettingsChangeCheck(SettingsSections sections)
    {
        if (_isLoading || !IsUiReady || _savedSettingsState == null) return;
        _pendingSettingsSections |= sections;
        if (_isColorDragging && sections == SettingsSections.Visual) return;
        if (_settingsCheckQueued) return;
        _settingsCheckQueued = true;
        App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _settingsCheckQueued = false;
            SettingsSections pending = _pendingSettingsSections;
            _pendingSettingsSections = 0;
            if (!_isClosed) UpdateApplySettingsState(pending);
        });
    }

    private void UpdateApplySettingsState(SettingsSections sections)
    {
        if (_savedSettingsState == null || _currentSettingsState == null) return;
        if (sections.HasFlag(SettingsSections.General)) _currentSettingsState = _currentSettingsState with { General = CaptureGeneralSettings() };
        if (sections.HasFlag(SettingsSections.Visual)) _currentSettingsState = _currentSettingsState with { Visual = CaptureVisualSettings() };
        if (sections.HasFlag(SettingsSections.Screens)) _currentSettingsState = _currentSettingsState with { Screens = CaptureScreenSettings() };
        BtnApplySettings.IsEnabled = !_isApplyingSettings && _currentSettingsState != _savedSettingsState;
    }

    private void BindCollections()
    {
        // 新标记没有声明 ItemTemplate / DisplayMemberPath，这里在代码里补上显示字段。
        ComboProfiles.DisplayMemberPath = nameof(FilterProfile.Name);
    }

    private void SetupSliderPairs()
    {
        // SliderX + TxtXValue 一一对应；ValueChanged 大多已在 XAML 接线，TrailRefresh 由这里接线。
        RegisterSliderPair(SliderScale, TxtScaleValue);
        RegisterSliderPair(SliderTrailScale, TxtTrailScaleValue);
        RegisterSliderPair(SliderClickScale, TxtClickScaleValue);
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
        int precision = slider.StepFrequency >= 1 ? 0 : 2;
        box.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = precision,
            NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder
            {
                Increment = Math.Pow(10, -precision),
                RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp
            }
        };
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
            box.Value = Math.Round(slider.Value, slider.StepFrequency >= 1 ? 0 : 2);
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
            double value = Math.Round(sender.Value, slider.StepFrequency >= 1 ? 0 : 2);
            if (sender.Value != value) sender.Value = value;
            slider.Value = value;
        }
        finally
        {
            _suppressValueSync = false;
        }

    }

    private void ConfigureInputControls()
    {
        foreach (NumberBox input in _boxToSlider.Keys)
        {
            input.Loaded += InputControl_Loaded;
        }

        foreach (TextBox input in new[] { EffectColorHexInput, SearchRunningProcess, SearchVisualReset, NewProfileNameInput })
        {
            input.Loaded += InputControl_Loaded;
        }
    }

    private void InputControl_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.ApplyTemplate();
        TextBox? input = control as TextBox ?? FindVisualDescendant<TextBox>(control);
        if (input == null)
        {
            return;
        }

        input.ApplyTemplate();
        if (control is NumberBox number && !_numberInputs.ContainsKey(number))
        {
            _numberInputs[number] = input;
            TrackSettingsProperty(input, TextBox.TextProperty, SettingsSections.Visual);
        }
        input.Padding = new Thickness(10, 0, 6, 0);
        input.VerticalContentAlignment = VerticalAlignment.Center;
        if (FindVisualDescendant<ScrollViewer>(input, "ContentElement") is { } content)
        {
            content.VerticalContentAlignment = VerticalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
        }

        if (FindVisualDescendant<TextBlock>(input, "PlaceholderTextContentPresenter") is { } placeholder)
        {
            placeholder.VerticalAlignment = VerticalAlignment.Center;
        }

        if (FindVisualDescendant<Button>(input, "DeleteButton") is { } clearButton)
        {
            clearButton.MinWidth = 0;
            clearButton.Width = 0;
            clearButton.MaxWidth = 0;
            clearButton.Margin = new Thickness(0);
            clearButton.IsHitTestVisible = false;
        }
    }

    private void RootGrid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source != null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is not ToggleSwitch toggle) continue;
            var point = args.GetCurrentPoint(toggle);
            if (args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse || point.Properties.IsLeftButtonPressed)
            {
                _pressedToggleSwitch = toggle;
                _togglePointerId = args.Pointer.PointerId;
                _togglePressPosition = point.Position;
                _toggleDoubleTapPending = false;
            }
            break;
        }
        if (FocusManager.GetFocusedElement(XamlRoot) is not TextBox focusedInput)
        {
            return;
        }

        for (DependencyObject? target = args.OriginalSource as DependencyObject; target != null; target = VisualTreeHelper.GetParent(target))
        {
            if (target == focusedInput || target is TextBox)
            {
                return;
            }
        }

        Focus(FocusState.Programmatic);
    }

    private void ToggleSwitch_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source != null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is not ToggleSwitch toggle) continue;
            if (toggle.IsEnabled)
            {
                if (_pressedToggleSwitch == toggle) _toggleDoubleTapPending = true;
                else toggle.IsOn = !toggle.IsOn;
            }
            args.Handled = true;
            return;
        }
    }

    private void ToggleSwitch_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pressedToggleSwitch is not { } toggle || args.Pointer.PointerId != _togglePointerId) return;
        bool pending = _toggleDoubleTapPending;
        _pressedToggleSwitch = null;
        _toggleDoubleTapPending = false;
        Point position = args.GetCurrentPoint(toggle).Position;
        if (pending && toggle.IsEnabled && position.X >= 0 && position.Y >= 0 && position.X <= toggle.ActualWidth && position.Y <= toggle.ActualHeight &&
            Math.Abs(position.X - _togglePressPosition.X) < 8 && Math.Abs(position.Y - _togglePressPosition.Y) < 8)
            toggle.IsOn = !toggle.IsOn;
    }

    private void ToggleSwitch_PointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerId != _togglePointerId) return;
        _pressedToggleSwitch = null;
        _toggleDoubleTapPending = false;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent, string? name = null) where T : DependencyObject
    {
        for (int childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(parent); childIndex++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is T match && (name == null || child is FrameworkElement element && element.Name == name))
            {
                return match;
            }

            if (FindVisualDescendant<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
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
        ApplyBackupLocalizedText();
        TabLogLabel.Text = Localization.Get("Nav_Log");
        TabAboutLabel.Text = Localization.Get("Nav_About");
        AppTitleBar.Title = Localization.Get("App_Title_ControlPanel");
        _host.SetTitle(AppTitleBar.Title);
        SetCaptionButtonLabel(BtnCaptionMinimize, "Window_Minimize");
        SetCaptionButtonLabel(BtnCaptionClose, "Window_Close");
        UpdateCaptionButtonState();
        TxtSidebarCopyright.Text = Localization.Get("Sidebar_Copyright");
        TxtWelcomeTitle.Text = Localization.Get("Welcome_Title");
        TxtWelcomeSubtitle.Text = Localization.Get("Welcome_Subtitle");
        TxtStatsTitle.Text = Localization.Get("Welcome_StatsTitle");
        UpdateEffectsPauseButton();
        TxtStatusLabel.Text = Localization.Get("Welcome_StatusLabel");
        TxtClicksLabel.Text = Localization.Get("Welcome_ClicksLabel");
        TxtSettingsTitle.Text = Localization.Get("Settings_Title");
        BtnApplySettings.Content = Localization.Get("Settings_Apply");
        TxtBasicTitle.Text = Localization.Get("Basic_Title");
        TxtBasicLanguage.Text = Localization.Get("Basic_Language");
        TxtDarkMode.Text = Localization.Get("Basic_DarkMode");
        RadioDarkModeOff.Content = Localization.Get("Basic_DarkModeOff");
        RadioDarkModeOn.Content = Localization.Get("Basic_DarkModeOn");
        RadioDarkModeSystem.Content = Localization.Get("Basic_DarkModeSystem");

        CheckAlwaysTrailEffectSwitch.Header = Localization.Get("Basic_TrailSwitch");
        CheckMasterSwitch.Header = Localization.Get("Basic_MasterSwitch");
        TxtClickType.Text = Localization.Get("Basic_ClickType");
        CheckMiddleClickTrigger.Header = Localization.Get("Basic_MiddleClick");
        CheckScreenshotCompatibilityMode.Header = Localization.Get("Basic_ScreenshotMode");
        TxtScreenshotHint.Text = Localization.Get("Basic_ScreenshotHint");
        CheckAutoStart.Header = Localization.Get("Basic_AutoStart");
        CheckStartSilent.Header = Localization.Get("Basic_StartSilent");
        CheckHideTrayIcon.Header = Localization.Get("Basic_HideTrayIcon");
        TxtHideTrayHint.Text = Localization.Get("Basic_HideTrayHint");
        BtnBasicReset.Content = Localization.Get("Settings_ResetPage");
        BtnFilterReset.Content = Localization.Get("Settings_ResetPage");
        CheckRunAsAdmin.Header = Localization.Get("Basic_RunAsAdmin");
        TxtRunAsAdminHint.Text = Localization.Get("Basic_RunAsAdminHint");
        CheckTouchscreenMode.Header = Localization.Get("Basic_Touchscreen");
        TxtTouchscreenHint.Text = Localization.Get("Basic_TouchscreenHint");
        TxtVisualTitle.Text = Localization.Get("Visual_Title");
        BtnVisualReset.Content = Localization.Get("Settings_ResetPage");
        CheckLinkedEffectScale.Header = Localization.Get("Visual_LinkedScale");
        TxtLinkedScaleHint.Text = Localization.Get("Visual_LinkedScaleHint");
        TxtVisualScale.Text = Localization.Get("Visual_Scale");
        TxtTrailScale.Text = Localization.Get("Visual_TrailScale");
        TxtClickScale.Text = Localization.Get("Visual_ClickScale");
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
        BtnBrowseProcess.Content = Localization.Get("Filter_Browse");
        BtnSelectRunningProcess.Content = Localization.Get("Filter_SelectRunning");
        TxtMultiScreenTitle.Text = Localization.Get("MultiScreen_Title");
        BtnRefreshScreens.Content = Localization.Get("MultiScreen_Refresh");
        TxtMultiScreenHint.Text = Localization.Get("MultiScreen_Hint");
        TxtLogTitle.Text = Localization.Get("Log_Title");
        BtnClearLog.Content = Localization.Get("Log_Clear");
        TxtLogHint.Text = Localization.Get("Log_Hint");
        TxtAboutTitle.Text = Localization.Get("About_Title");
        TxtAboutDescription.Text = Localization.Get("About_Description");
        TxtRepositoryLinks.Text = Localization.Get("About_Repositories");
        BtnRepoDoomVoss.Content = Localization.Get("About_OpenRepository");
        BtnRepoCialloKing.Content = Localization.Get("About_OpenRepository");
        BtnRepoWinUI.Content = Localization.Get("About_OpenRepository");
        BtnResetSettings.Content = Localization.Get("Settings_Reset");
        BtnScreensReset.Content = Localization.Get("Settings_ResetPage");
        TxtOverlayRunning.Text = Localization.Get("Overlay_RunningProcess");
        SearchRunningProcess.PlaceholderText = Localization.Get("Overlay_SearchProcesses");
        BtnOverlayCancel.Content = Localization.Get("Overlay_Cancel");
        BtnOverlayConfirmAdd.Content = Localization.Get("Overlay_ConfirmAdd");
        TxtOverlayVisualReset.Text = Localization.Get("Overlay_VisualReset");
        BtnOverlayVisualCancel.Content = Localization.Get("Overlay_Cancel");
        BtnOverlayVisualConfirm.Content = Localization.Get("Overlay_ConfirmReset");
        SearchVisualReset.PlaceholderText = Localization.Get("Overlay_SearchSettings");
        TxtOverlayRename.Text = Localization.Get("Overlay_RenameProfile");
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
        foreach (var selector in _segmentedSelectors) selector.RefreshLayout();
        UpdateConfiguredProcessSummary();
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

    private void EffectColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!IsUiReady || _syncingColorControls)
        {
            return;
        }

        _particleColor = ToRgbString(args.NewColor);
        _effectOpacity = Math.Clamp((_colorAlphaSlider?.Value ?? args.NewColor.A / 255.0 * 100) / 100, 0.1, 1);
        _pendingPreviewColor = args.NewColor;
        _colorPreviewPending = true;
        EnsureColorPreviewRendering();
    }

    private void EnsureColorPreviewRendering()
    {
        if (_colorPreviewSubscribed) return;
        _colorPreviewSubscribed = true;
        CompositionTarget.Rendering += ColorPreview_Rendering;
    }

    private void StopColorPreviewRendering()
    {
        if (!_colorPreviewSubscribed) return;
        _colorPreviewSubscribed = false;
        CompositionTarget.Rendering -= ColorPreview_Rendering;
    }

    private void ColorPicker_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(EffectColorPicker).IsInContact) return;
        DependencyObject? source = args.OriginalSource as DependencyObject;
        while (source != null && source != EffectColorPicker && source is not ColorSpectrum and not ColorPickerSlider)
            source = VisualTreeHelper.GetParent(source);
        if (source is not ColorSpectrum and not ColorPickerSlider) return;
        _isColorDragging = true;
        _lastColorLabelUpdate = 0;
        EnsureColorPreviewRendering();
    }

    private void ColorPicker_PointerFinished(object sender, PointerRoutedEventArgs args)
    {
        if (!_isColorDragging) return;
        _isColorDragging = false;
        _colorPreviewPending = true;
        EnsureColorPreviewRendering();
        QueueSettingsChangeCheck(SettingsSections.Visual);
    }

    private void ColorPreview_Rendering(object? sender, object args)
    {
        if (_isClosed)
        {
            StopColorPreviewRendering();
            return;
        }
        if (_colorPreviewPending)
        {
            _colorPreviewPending = false;
            ApplyColorPreview(_pendingPreviewColor, updatePicker: false);
        }
        if (_isColorDragging && _pendingSettingsSections != 0)
        {
            SettingsSections pending = _pendingSettingsSections;
            _pendingSettingsSections = 0;
            UpdateApplySettingsState(pending);
        }
        if (!_isColorDragging) StopColorPreviewRendering();
    }

    private void EffectColorPresets_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!IsUiReady || _syncingColorControls ||
            EffectColorPresets.SelectedItem is not RadioButton { Tag: string hex } ||
            !ColorPickerColorMath.TryParseHex(hex, out Color color))
        {
            return;
        }

        _particleColor = ToRgbString(color);
        UpdateColorPreview(_particleColor);
    }

    private void EffectColorHexInput_LostFocus(object sender, RoutedEventArgs args) => CommitEffectColorHex();

    private void EffectColorHexInput_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Enter)
        {
            CommitEffectColorHex();
            args.Handled = true;
        }
        else if (args.Key == Windows.System.VirtualKey.Escape)
        {
            UpdateColorPreview(_particleColor);
            args.Handled = true;
        }
    }

    private void CommitEffectColorHex()
    {
        if (!IsUiReady || _syncingColorControls)
        {
            return;
        }

        string hex = EffectColorHexInput.Text.Trim().TrimStart('#');
        bool hasAlpha = hex.Length == 8;
        byte alpha = 255;
        if (hasAlpha && byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha))
        {
            hex = hex[2..];
        }

        if (ColorPickerColorMath.TryParseHex(hex, out Color color))
        {
            _particleColor = ToRgbString(color);
            if (hasAlpha)
            {
                _effectOpacity = Math.Clamp(alpha / 255.0, 0.1, 1);
            }
        }

        UpdateColorPreview(_particleColor);
    }

    private void EffectColorExpander_Loaded(object sender, RoutedEventArgs args)
    {
        EffectColorExpander.ApplyTemplate();
        _colorContentHost = FindVisualDescendant<Border>(EffectColorExpander, "ExpanderContentHost");
        Visual visual = ElementCompositionPreview.GetElementVisual(EffectColorCard);
        _colorCardGeometry ??= visual.Compositor.CreateRoundedRectangleGeometry();
        _colorCardGeometry.CornerRadius = new Vector2(6);
        visual.Clip = visual.Compositor.CreateGeometricClip(_colorCardGeometry);
        if (_colorCardAnimating || _colorContentHost == null) return;
        _colorContentHost.Height = EffectColorExpander.IsExpanded ? double.NaN : 0;
        _colorContentHost.IsHitTestVisible = EffectColorExpander.IsExpanded;
        EffectColorCard.UpdateLayout();
        _colorCardGeometry.Size = new Vector2((float)EffectColorCard.ActualWidth, (float)EffectColorCard.ActualHeight);
    }

    private void EffectColorCard_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_colorCardGeometry != null && !_colorCardAnimating)
        {
            _colorCardGeometry.Size = new Vector2((float)args.NewSize.Width, (float)args.NewSize.Height);
        }
    }

    private void EffectColorExpander_Expanding(Expander sender, object args) => AnimateColorCard(expanding: true);

    private void EffectColorExpander_Collapsed(Expander sender, object args) => AnimateColorCard(expanding: false);

    private void AnimateColorCard(bool expanding)
    {
        if (_colorContentHost == null || _colorCardGeometry == null || !EffectColorCard.IsLoaded)
        {
            return;
        }

        int generation = ++_colorCardAnimationGeneration;
        ColorPickerBody.Measure(new Size(Math.Max(0, EffectColorCard.ActualWidth - 34), double.PositiveInfinity));
        double contentHeight = ColorPickerBody.DesiredSize.Height + _colorContentHost.Padding.Top + _colorContentHost.Padding.Bottom;
        if (!_colorCardAnimating)
            _colorCardGeometry.Size = new Vector2((float)EffectColorCard.ActualWidth, expanding ? 60 : (float)EffectColorCard.ActualHeight);
        _colorCardAnimating = true;
        _colorContentHost.Height = contentHeight;
        _colorContentHost.IsHitTestVisible = expanding;
        EffectColorCard.UpdateLayout();
        var compositor = _colorCardGeometry.Compositor;
        ElementCompositionPreview.GetElementVisual(EffectColorCard).Clip = compositor.CreateGeometricClip(_colorCardGeometry);
        float targetHeight = expanding ? (float)EffectColorCard.ActualHeight : 60;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, targetHeight,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(expanding ? 220 : 180);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _colorCardGeometry.StartAnimation("Size.Y", animation);
        batch.Completed += (_, _) =>
        {
            batch.Dispose();
            App.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed || generation != _colorCardAnimationGeneration)
                {
                    return;
                }

                _colorCardGeometry.StopAnimation("Size.Y");
                _colorContentHost.Height = expanding ? double.NaN : 0;
                EffectColorCard.UpdateLayout();
                _colorCardAnimating = false;
                _colorCardGeometry.Size = new Vector2((float)EffectColorCard.ActualWidth, (float)EffectColorCard.ActualHeight);
            });
        };
        batch.End();
    }

    private void EffectColorPicker_Loaded(object sender, RoutedEventArgs args)
    {
        EffectColorPicker.ApplyTemplate();
        var alphaSlider = FindVisualDescendant<ColorPickerSlider>(EffectColorPicker, "AlphaSlider");
        if (_colorAlphaSlider != alphaSlider)
        {
            if (_colorAlphaSlider != null)
            {
                _colorAlphaSlider.ValueChanged -= ColorAlphaSlider_ValueChanged;
            }

            _colorAlphaSlider = alphaSlider;
            if (_colorAlphaSlider != null)
            {
                _colorAlphaSlider.Minimum = 0;
                _colorAlphaSlider.ValueChanged += ColorAlphaSlider_ValueChanged;
                bool previousSync = _syncingColorControls;
                _syncingColorControls = true;
                try
                {
                    _colorAlphaSlider.Value = Math.Clamp(_effectOpacity * 100, 10, 100);
                }
                finally
                {
                    _syncingColorControls = previousSync;
                }
            }
        }

        if (FindVisualDescendant<ColorSpectrum>(EffectColorPicker, "ColorSpectrum") is { } spectrum && spectrum != _configuredColorSpectrum)
        {
            _configuredColorSpectrum = spectrum;
            spectrum.SizeChanged += (_, _) => UpdateSpectrumClip();
        }

        UpdateSpectrumClip();
    }

    private void ColorAlphaSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (args.NewValue < 10 && _colorAlphaSlider != null)
        {
            _colorAlphaSlider.Value = 10;
        }
        QueueSettingsChangeCheck(SettingsSections.Visual);
    }

    private void UpdateSpectrumClip()
    {
        if (_configuredColorSpectrum is { } spectrum && FindVisualDescendant<Grid>(spectrum, "SizingGrid") is { } sizing)
        {
            sizing.Clip = new RectangleGeometry { Rect = new Rect(-8, -8, spectrum.ActualWidth + 16, spectrum.ActualHeight + 16) };
        }
    }

    private static ImageBrush CreateColorCheckerBrush()
    {
        var bitmap = new WriteableBitmap(64, 64);
        var pixels = new byte[64 * 64 * 4];
        for (int row = 0; row < 64; row++)
        {
            for (int column = 0; column < 64; column++)
            {
                byte shade = (byte)((row / 8 + column / 8) % 2 == 0 ? 136 : 204);
                int offset = (row * 64 + column) * 4;
                pixels[offset] = shade;
                pixels[offset + 1] = shade;
                pixels[offset + 2] = shade;
                pixels[offset + 3] = 255;
            }
        }

        using (Stream stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels);
        }

        bitmap.Invalidate();
        return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
    }

    private void UpdateColorPreview(string rgbString)
    {
        Color rgb = TryParseRgbString(rgbString, out Color parsed) ? parsed : Colors.Gray;
        byte alpha = (byte)Math.Round(Math.Clamp(_effectOpacity, 0.1, 1) * 255);
        _colorPreviewPending = false;
        _pendingPreviewColor = Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
        if (!_isColorDragging) StopColorPreviewRendering();
        ApplyColorPreview(_pendingPreviewColor, updatePicker: true);
        QueueSettingsChangeCheck(SettingsSections.Visual);
    }

    private void ApplyColorPreview(Color color, bool updatePicker)
    {
        long now = Stopwatch.GetTimestamp();
        bool updateLabels = !_isColorDragging || now - _lastColorLabelUpdate >= Stopwatch.Frequency / 20;
        _syncingColorControls = true;
        try
        {
            if (_previewColorBrush.Color != color) _previewColorBrush.Color = color;
            if (updateLabels)
            {
                _lastColorLabelUpdate = now;
                string hex = $"#{color.A:X2}{ColorPickerColorMath.ToHex(color)[1..]}";
                if (EffectColorHexInput.Text != hex) EffectColorHexInput.Text = hex;
                if (ExpandedColorHex.Text != hex) ExpandedColorHex.Text = hex;
                string opacity = $"{Localization.Get("Visual_Opacity")} {_effectOpacity * 100:0.#}%";
                if (ExpandedColorOpacity.Text != opacity) ExpandedColorOpacity.Text = opacity;
            }
            if (updatePicker && EffectColorPicker.Color != color)
            {
                EffectColorPicker.Color = color;
            }

            if (_presetColorIndices.Count == 0)
                for (int index = 0; index < EffectColorPresets.Items.Count; index++)
                    if (EffectColorPresets.Items[index] is RadioButton { Tag: string preset } && ColorPickerColorMath.TryParseHex(preset, out Color presetColor))
                        _presetColorIndices[presetColor.R << 16 | presetColor.G << 8 | presetColor.B] = index;
            int selectedIndex = _presetColorIndices.TryGetValue(color.R << 16 | color.G << 8 | color.B, out int presetIndex) ? presetIndex : -1;

            if (EffectColorPresets.SelectedIndex != selectedIndex)
            {
                EffectColorPresets.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            _syncingColorControls = false;
        }
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

    private async void OpenRepository_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string url }) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to open repository link: {exception.Message}");
            await ShowMessageAsync(Localization.Format("Msg_OpenLinkFailed", exception.Message));
        }
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

    private void SidebarNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && TabSettings.MenuItems.Contains(item))
            _selectedSettingsItem = item;
        if (PageWelcome != null && !_isClosed) UpdatePageVisibility();
        QueueNavigationIndicatorUpdate(true);
    }

    private void NavigationItem_Loaded(object sender, RoutedEventArgs args)
    {
        _navigationIndicators.Remove((NavigationViewItem)sender);
        QueueNavigationIndicatorUpdate(false);
    }

    private void QueueNavigationIndicatorUpdate(bool animate)
    {
        if (_isClosed) return;
        _animateNavigationIndicator |= animate;
        if (_navigationIndicatorQueued) return;
        _navigationIndicatorQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _navigationIndicatorQueued = false;
            bool playAnimation = _animateNavigationIndicator && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            _animateNavigationIndicator = false;
            if (_isClosed || !SidebarNavigation.IsLoaded) return;
            try
            {
                if (TabSettings.IsExpanded && ReferenceEquals(SidebarNavigation.SelectedItem, TabSettings) && _selectedSettingsItem != null)
                    SidebarNavigation.SelectedItem = _selectedSettingsItem;
                foreach (NavigationViewItem item in new[] { TabWelcome, TabSettings, SubTabBasic, SubTabVisual, SubTabFilter, SubTabMultiScreen, SubTabBackup, TabLog, TabAbout })
                {
                    if (!item.IsLoaded || FindVisualDescendant<Microsoft.UI.Xaml.Shapes.Rectangle>(item, "SelectionIndicator") is not { Parent: Grid indicatorHost } nativeIndicator) continue;
                    nativeIndicator.Fill = _navigationIndicatorPlaceholderBrush;
                    FrameworkElement? indicator = indicatorHost.Children.OfType<FrameworkElement>().FirstOrDefault(child => child.Name == "LocalSelectionIndicator");
                    if (indicator == null)
                    {
                        indicator = new Microsoft.UI.Xaml.Shapes.Rectangle
                        {
                            Name = "LocalSelectionIndicator",
                            Style = (Style)Application.Current.Resources["BasNavigationIndicatorStyle"],
                            Visibility = Visibility.Collapsed
                        };
                        indicatorHost.Children.Add(indicator);
                    }
                    bool active = ReferenceEquals(SidebarNavigation.SelectedItem, item) || item == TabSettings && !item.IsExpanded &&
                        SidebarNavigation.SelectedItem is NavigationViewItem selected && TabSettings.MenuItems.Contains(selected);
                    indicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                    if (active && (indicator.ActualWidth == 0 || indicator.ActualHeight == 0)) indicatorHost.UpdateLayout();
                    bool tracked = _navigationIndicators.TryGetValue(item, out var previous) && previous.Indicator == indicator;
                    if (tracked && previous.Active == active) continue;
                    _navigationIndicators[item] = (indicator, active);
                    Visual visual = ElementCompositionPreview.GetElementVisual(indicator);
                    visual.StopAnimation("Offset");
                    visual.StopAnimation("Scale");
                    visual.StopAnimation("Opacity");
                    visual.CenterPoint = new Vector3((float)indicator.ActualWidth / 2, (float)indicator.ActualHeight / 2, 0);
                    if (!playAnimation || !tracked && !active)
                    {
                        visual.Scale = new Vector3(1, active ? 1 : 0.35f, 1);
                        visual.Opacity = active ? 1 : 0;
                        continue;
                    }
                    Compositor compositor = visual.Compositor;
                    var scale = compositor.CreateVector3KeyFrameAnimation();
                    if (active) scale.InsertKeyFrame(0, new Vector3(1, 0.35f, 1));
                    else scale.InsertExpressionKeyFrame(0, "this.StartingValue");
                    scale.InsertKeyFrame(1, new Vector3(1, active ? 1 : 0.35f, 1),
                        compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1)));
                    scale.Duration = TimeSpan.FromMilliseconds(active ? 220 : 120);
                    visual.StartAnimation("Scale", scale);
                    var opacity = compositor.CreateScalarKeyFrameAnimation();
                    if (active) opacity.InsertKeyFrame(0, 0);
                    else opacity.InsertExpressionKeyFrame(0, "this.StartingValue");
                    opacity.InsertKeyFrame(1, active ? 1 : 0);
                    opacity.Duration = TimeSpan.FromMilliseconds(active ? 150 : 120);
                    visual.StartAnimation("Opacity", opacity);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Error("Failed to animate the navigation indicator.", exception);
            }
        });
    }

    private bool IsSettingsNavigation(NavigationViewItem item) => item == TabSettings || TabSettings.MenuItems.Contains(item);

    /// <summary>页面切换动画：进入的页面淡入 + 轻微上移。</summary>
    private static readonly TimeSpan PageTransitionDuration = TimeSpan.FromMilliseconds(180);

    private void UpdatePageVisibility()
    {
        StopSettingsAnimations();
        var selected = SidebarNavigation.SelectedItem as NavigationViewItem ?? TabWelcome;
        bool welcome = selected == TabWelcome;
        bool settings = IsSettingsNavigation(selected);
        bool log = selected == TabLog;
        bool about = selected == TabAbout;

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
        StopSettingsAnimations();
        // 四个子项之间切换同样需要过渡动画，因此逐个走 SetPageVisible（它会对
        // 「本次新进入」的那一项播放动画），而不是直接赋 Visibility。
        FrameworkElement? incoming = _selectedSettingsItem == SubTabBasic
            ? SectionBasic
            : _selectedSettingsItem == SubTabVisual
                ? SectionVisual
                : _selectedSettingsItem == SubTabFilter
                    ? SectionFilter
                    : _selectedSettingsItem == SubTabMultiScreen
                        ? SectionMultiScreen
                        : _selectedSettingsItem == SubTabBackup
                            ? SectionBackup
                            : null;

        SetPageVisible(SectionBasic, ReferenceEquals(incoming, SectionBasic), incoming);
        SetPageVisible(SectionVisual, ReferenceEquals(incoming, SectionVisual), incoming);
        SetPageVisible(SectionFilter, ReferenceEquals(incoming, SectionFilter), incoming);
        SetPageVisible(SectionMultiScreen, ReferenceEquals(incoming, SectionMultiScreen), incoming);
        SetPageVisible(SectionBackup, ReferenceEquals(incoming, SectionBackup), incoming);
    }

    // ==================================================================
    // 首页统计
    // ==================================================================

    private void RefreshTimer_Tick()
    {
        if (_isClosed || PageWelcome.Visibility != Visibility.Visible)
        {
            return;
        }

        ClickCountText.Text = Localization.Format("Welcome_ClicksUnit", ConfigManager.TotalClicks);

        bool suppressedByEnvironment = (ConfigManager.IsEffectEnabled || ConfigManager.EnableAlwaysTrailEffect) &&
            App.Overlay?.IsEffectSuppressedByEnvironment() == true;

        // 状态画刷复用实例：每 500ms 新建一个 SolidColorBrush 并重新赋给
        // Foreground，会让该文本块每次都被判定为「变了」而重绘，长期挂机时白白
        // 制造 GC 压力与无谓的重绘（拖动窗口时正好撞上就是一次卡顿）。
        if (App.IsEffectsPaused || !ConfigManager.IsEffectEnabled && !ConfigManager.EnableAlwaysTrailEffect)
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

    private void ToggleEffectsPause_Click(object sender, RoutedEventArgs args) => App.ToggleEffectsPaused();

    private void EffectsPauseChanged(object? sender, EventArgs args)
    {
        UpdateEffectsPauseButton();
        RefreshTimer_Tick();
    }

    private void UpdateEffectsPauseButton()
    {
        BtnToggleEffectsPause.IsChecked = App.IsEffectsPaused;
        BtnToggleEffectsPause.Content = Localization.Get(App.IsEffectsPaused ? "Effects_Enable" : "Effects_Pause");
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
        _particleColor = ConfigManager.ParticleColor;
        CheckMasterSwitch.IsOn = ConfigManager.IsEffectEnabled;
        CheckAutoStart.IsOn = ConfigManager.AutoStart;
        CheckStartSilent.IsOn = ConfigManager.StartSilent;
        CheckHideTrayIcon.IsOn = ConfigManager.HideTrayIcon;
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

        ComboProfiles.SelectedItem = Profiles.FirstOrDefault(profile => profile.Id == ConfigManager.GetActiveProfile()?.Id);

        UpdateClickEffectPanelVisibility();
        UpdateEnvironmentFilterInterlock();

        CheckLinkedEffectScale.IsOn = ConfigManager.UseLinkedEffectScale;
        SliderScale.Value = ConfigManager.EffectScale;
        SliderTrailScale.Value = ConfigManager.TrailEffectScale;
        SliderClickScale.Value = ConfigManager.ClickEffectScale;
        _effectOpacity = Math.Clamp(ConfigManager.EffectOpacity, 0.1, 1);
        SliderGlow.Value = ConfigManager.GlowIntensity;
        CheckLinkedAnimationSpeed.IsOn = ConfigManager.UseLinkedAnimationSpeed;
        CheckApplyCurveDraw.IsOn = ConfigManager.ApplyCurveDraw;
        SliderSpeed.Value = ConfigManager.EffectSpeed;
        SliderTrailAnimSpeed.Value = ConfigManager.TrailAnimationSpeed;
        SliderClickAnimSpeed.Value = ConfigManager.ClickAnimationSpeed;
        CheckFollowDisplayRefreshRate.IsOn = ConfigManager.FollowDisplayRefreshRate;
        SliderTrailRefresh.Value = ConfigManager.TrailRefreshRate;
        SyncSliderAndBoxValues();
        UpdateColorPreview(_particleColor);

        UpdateEffectScalePanelVisibility();
        UpdateAnimationSpeedPanelVisibility();
        UpdateTrailRefreshInterlock();

        SelectDarkMode(ConfigManager.DarkMode);
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

    private int GetSelectedClickTrigger()
    {
        // RadioClickType 顺序：左键 / 右键 / 左右键，正好对应 ClickTriggerType 取值。
        int index = RadioClickType.SelectedIndex;
        return index is 1 or 2 ? index : 0;
    }

    private void ApplyScrollbarSettings()
    {
        ScrollBarVisibility visibility = ScrollBarVisibility.Auto;

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

    private void ResetBasicDefaults_Click(object sender, RoutedEventArgs args) => OpenResetOverlay(ResetScope.Basic);

    private void ResetFilterDefaults_Click(object sender, RoutedEventArgs args) => OpenResetOverlay(ResetScope.Filter);

    private void RestoreLanguageDefault()
    {
        string culture = Localization.NormalizeCulture(CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name);
        ComboLanguage.SelectedItem = ComboLanguage.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), culture, StringComparison.OrdinalIgnoreCase));
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private void CheckMasterSwitch_Changed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsUiReady || _isLoading)
        {
            return;
        }

        UpdateClickEffectPanelVisibility(animate: true);
    }

    private void UpdateClickEffectPanelVisibility(bool animate = false) =>
        SetSettingsVisibility(SectionBasic, animate, (PanelClickEffectOptions, CheckMasterSwitch.IsOn));

    private void SetSettingsVisibility(Panel section, bool animate, params (FrameworkElement Element, bool Visible)[] changes)
    {
        StopSettingsAnimation(section);
        if (changes.All(change => (change.Element.Visibility == Visibility.Visible) == change.Visible)) return;
        bool play = animate && !_isLoading && IsUiReady && PageSettings.Visibility == Visibility.Visible &&
            section.Visibility == Visibility.Visible && section.ActualHeight > 0;
        section.UpdateLayout();
        var positions = play
            ? section.Children.OfType<FrameworkElement>().Where(element => element.Visibility == Visibility.Visible)
                .ToDictionary(element => element, element => element.TransformToVisual(section).TransformPoint(new Point()).Y)
            : new Dictionary<FrameworkElement, double>();
        foreach (var change in changes) change.Element.Visibility = change.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (!play) return;
        section.UpdateLayout();
        var storyboard = new Storyboard();
        foreach (FrameworkElement element in section.Children.OfType<FrameworkElement>().Where(element => element.Visibility == Visibility.Visible))
        {
            bool existing = positions.TryGetValue(element, out double previousTop);
            double offset = existing ? previousTop - element.TransformToVisual(section).TransformPoint(new Point()).Y : 0;
            if (Math.Abs(offset) > 0.1)
            {
                var reposition = new RepositionThemeAnimation { FromHorizontalOffset = 0, FromVerticalOffset = offset };
                Storyboard.SetTarget(reposition, element);
                storyboard.Children.Add(reposition);
            }
            if (!existing)
            {
                var entrance = new FadeInThemeAnimation();
                Storyboard.SetTarget(entrance, element);
                storyboard.Children.Add(entrance);
            }
        }
        if (storyboard.Children.Count == 0) return;
        _settingsAnimations[section] = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (_settingsAnimations.TryGetValue(section, out Storyboard? active) && ReferenceEquals(active, storyboard))
                StopSettingsAnimation(section);
        };
        storyboard.Begin();
    }

    private void StopSettingsAnimation(Panel section)
    {
        if (_settingsAnimations.Remove(section, out Storyboard? storyboard)) storyboard.Stop();
    }

    private void StopSettingsAnimations()
    {
        foreach (Storyboard storyboard in _settingsAnimations.Values) storyboard.Stop();
        _settingsAnimations.Clear();
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
        BtnAddProfile.IsEnabled = environmentFilterEnabled;
        BtnRenameProfile.IsEnabled = environmentFilterEnabled;
        BtnDeleteProfile.IsEnabled = environmentFilterEnabled;

        ListConfiguredProcesses.IsEnabled = processFilterEnabled && CurrentProfileProcesses.Count > 0;
        BtnBrowseProcess.IsEnabled = processFilterEnabled;
        BtnSelectRunningProcess.IsEnabled = processFilterEnabled;
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

        UpdateConfiguredProcessSummary();
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
            _ = SetModalOverlayVisibleAsync(RenameProfileOverlay, visible: true);
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

        _ = SetModalOverlayVisibleAsync(RenameProfileOverlay, visible: false);
    }

    private void CloseRenameOverlay_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = SetModalOverlayVisibleAsync(RenameProfileOverlay, visible: false);
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (Profiles.Count <= 1)
        {
            await ShowMessageAsync(Localization.Get("Msg_KeepOneProfile"));
            return;
        }

        if (ComboProfiles.SelectedItem is not FilterProfile active)
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            Localization.Format("Msg_ConfirmDeleteProfile", active.Name),
            confirmText: Localization.Get("Msg_ConfirmDelete_Title"));

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

    private void UpdateConfiguredProcessSummary()
    {
        ListConfiguredProcesses.PlaceholderText = Localization.Format("Filter_SelectedProcesses", CurrentProfileProcesses.Count);
        UpdateEnvironmentFilterInterlock();
    }

    private void ConfiguredProcesses_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ListConfiguredProcesses.SelectedIndex >= 0) ListConfiguredProcesses.SelectedIndex = -1;
    }

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
        bool wasOpen = ListConfiguredProcesses.IsDropDownOpen;

        if (ComboProfiles.SelectedItem is FilterProfile active)
        {
            active.Processes.RemoveAll(existing =>
                string.Equals(existing, processName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeProcessName(existing), processName, StringComparison.OrdinalIgnoreCase));
            CurrentProfileProcesses.Remove(processName);
        }
        else
        {
            CurrentProfileProcesses.Remove(processName);
        }
        if (wasOpen && CurrentProfileProcesses.Count > 0)
        {
            ListConfiguredProcesses.IsDropDownOpen = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosed && CurrentProfileProcesses.Count > 0) ListConfiguredProcesses.IsDropDownOpen = true;
            });
        }
    }

    private async void BrowseProcess_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            InitializeWithWindow.Initialize(picker, Handle);

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
        _ = SetModalOverlayVisibleAsync(RunningProcessOverlay, visible: true);
    }

    private void CloseRunningProcessOverlay_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = SetModalOverlayVisibleAsync(RunningProcessOverlay, visible: false);
    }

    private void ConfirmAddRunningProcesses_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        List<string> selected = _allRunningProcesses
            .Where(item => item.IsSelected)
            .Select(item => item.ProcessName)
            .ToList();

        foreach (string processName in selected)
        {
            AddProcessToActiveProfile(processName);
        }

        _ = SetModalOverlayVisibleAsync(RunningProcessOverlay, visible: false);
    }

    private void SearchRunningProcess_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = e;
        ApplyRunningProcessFilter(SearchRunningProcess.Text);
    }

    private void RefreshRunningProcessList()
    {
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

    }

    // ==================================================================
    // 视觉表现恢复默认
    // ==================================================================

    private void OpenVisualResetOverlay_Click(object sender, RoutedEventArgs args) => OpenResetOverlay(ResetScope.Visual);
    private void OpenAllResetOverlay_Click(object sender, RoutedEventArgs args) => OpenResetOverlay(ResetScope.All);
    private void ResetScreenDefaults_Click(object sender, RoutedEventArgs args) => OpenResetOverlay(ResetScope.Screens);

    private void OpenResetOverlay(ResetScope scope)
    {
        _resetScope = scope;
        VisualResetItems.Clear();
        if (scope is ResetScope.Basic or ResetScope.All) RebuildBasicResetItems();
        if (scope is ResetScope.Visual or ResetScope.All) RebuildVisualResetItems();
        if (scope is ResetScope.Filter or ResetScope.All) RebuildFilterResetItems();
        if (scope is ResetScope.Screens or ResetScope.All) RebuildScreenResetItems();
        TxtOverlayVisualReset.Text = Localization.Get(scope switch
        {
            ResetScope.Basic => "Overlay_BasicReset",
            ResetScope.Filter => "Overlay_FilterReset",
            ResetScope.Screens => "Overlay_ScreenReset",
            ResetScope.All => "Overlay_AllReset",
            _ => "Overlay_VisualReset"
        });
        foreach (VisualResetItem item in VisualResetItems) item.IsSelected = true;
        SearchVisualReset.Text = string.Empty;
        RefreshVisualResetRows(null);
        _ = SetModalOverlayVisibleAsync(VisualResetOverlay, visible: true);
    }

    private void AddPageResetItem(string group, string title, string value, string key, Action restore, Action save, string? selectionId = null) =>
        VisualResetItems.Add(new VisualResetItem(title, Localization.Format("Reset_DefaultValue", value), restore)
        { Group = group, SettingKey = key, SelectionId = selectionId, Save = save });

    private void AddToggleResetItem(string group, ToggleSwitch toggle, bool value, string key) =>
        AddPageResetItem(group, toggle.Header?.ToString() ?? string.Empty,
            Localization.Get(value ? "Basic_DarkModeOn" : "Basic_DarkModeOff"), key, () => toggle.IsOn = value, () => SaveResetValue(key, value));

    private static void SaveResetValue(string key, object value)
    {
        if (!ConfigManager.Save(key, value)) throw new InvalidOperationException(Localization.Get("Msg_SettingsSaveFailed"));
    }

    private void RebuildBasicResetItems()
    {
        string group = TxtBasicTitle.Text;
        AddPageResetItem(group, TxtBasicLanguage.Text, Localization.Get("Basic_DarkModeSystem"), "UiLanguage", RestoreLanguageDefault,
            () => SaveResetValue("UiLanguage", GetSelectedLanguage() ?? Localization.CurrentCultureName));
        AddPageResetItem(group, TxtDarkMode.Text, Localization.Get("Basic_DarkModeSystem"), "DarkMode",
            () => SelectDarkMode(DarkModeOption.System), () => SaveResetValue("DarkMode", DarkModeOption.System));
        AddToggleResetItem(group, CheckAlwaysTrailEffectSwitch, false, "EnableAlwaysTrailEffect");
        AddToggleResetItem(group, CheckMasterSwitch, true, "IsEffectEnabled");
        AddPageResetItem(group, TxtClickType.Text, Localization.Get("Basic_LeftClick"), "ClickTriggerType",
            () => RadioClickType.SelectedIndex = 0, () => SaveResetValue("ClickTriggerType", 0));
        foreach (var setting in new[] { (CheckMiddleClickTrigger, "EnableMiddleClickTrigger"),
                     (CheckScreenshotCompatibilityMode, "ScreenshotCompatibilityMode"), (CheckAutoStart, "AutoStart"),
                     (CheckStartSilent, "StartSilent"), (CheckHideTrayIcon, "HideTrayIcon"), (CheckRunAsAdmin, "RunAsAdmin"),
                     (CheckTouchscreenMode, "IsTouchscreenMode") })
            AddToggleResetItem(group, setting.Item1, false, setting.Item2);
    }

    private void RebuildFilterResetItems()
    {
        string group = TxtFilterTitle.Text;
        AddToggleResetItem(group, CheckEnvironmentFilter, false, "EnableEnvironmentFilter");
        AddToggleResetItem(group, CheckHideInFullscreen, true, "HideInFullscreen");
        AddToggleResetItem(group, CheckShowEffectOnDesktop, true, "ShowEffectOnDesktop");
        AddPageResetItem(group, Localization.Get("Filter_ProfileGroup"), Localization.Get("Reset_DefaultProfile"), "Profiles", () =>
        {
            FilterProfile profile = Profiles.FirstOrDefault(item => item.Name == Localization.Get("Profile_Default"))
                ?? Profiles.FirstOrDefault() ?? new FilterProfile();
            profile.Name = Localization.Get("Profile_Default");
            profile.Mode = ProcessFilterModeOption.Blacklist;
            profile.Processes.Clear();
            Profiles.Clear();
            Profiles.Add(profile);
            ComboProfiles.SelectedIndex = 0;
        }, () =>
        {
            if (!ConfigManager.SaveProfiles(Profiles.ToList(), (ComboProfiles.SelectedItem as FilterProfile)?.Id ?? string.Empty))
                throw new InvalidOperationException(Localization.Get("Msg_SettingsSaveFailed"));
        });
    }

    private void RebuildScreenResetItems()
    {
        foreach (ScreenOptionItem screen in ScreenOptions)
            AddPageResetItem(TxtMultiScreenTitle.Text, screen.Title, Localization.Get("Basic_DarkModeOn"), "Screen", () =>
            {
                screen.IsEnabled = true;
                SyncScreenToggles();
            }, () => SaveResetScreen(screen), string.IsNullOrEmpty(screen.IdentityKey) ? screen.DeviceName : screen.IdentityKey);
    }

    private void SaveResetScreen(ScreenOptionItem screen)
    {
        var screens = JsonSerializer.Deserialize<List<ScreenSelectionState>>(_savedSettingsState!.Screens)!;
        ScreenSelectionState? saved = screens.FirstOrDefault(item => item.IdentityKey == screen.IdentityKey && item.DeviceName == screen.DeviceName);
        if (saved == null) throw new InvalidOperationException(Localization.Get("Msg_SettingsSaveFailed"));
        saved.IsEnabled = true;
        var persisted = ConfigManager.GetScreenSelections();
        foreach (ScreenSelectionState current in screens)
        {
            persisted.RemoveAll(item => !string.IsNullOrEmpty(current.IdentityKey) ? item.IdentityKey == current.IdentityKey : item.DeviceName == current.DeviceName);
            persisted.Add(current);
        }
        if (!ConfigManager.SaveScreenSelections(persisted)) throw new InvalidOperationException(Localization.Get("Msg_SettingsSaveFailed"));
        MarkResetSettingsSaved(VisualResetItems.Where(item => item.SettingKey == "Screen" && item.SelectionId ==
            (string.IsNullOrEmpty(screen.IdentityKey) ? screen.DeviceName : screen.IdentityKey)));
    }

    private async void CloseVisualResetOverlay_Click(object sender, RoutedEventArgs args)
    {
        if (!await SetModalOverlayVisibleAsync(VisualResetOverlay, visible: false) || _isClosed) return;
        ListVisualResetItems.ItemsSource = null;
        VisualResetItems.Clear();
    }

    private void SearchVisualReset_TextChanged(object sender, TextChangedEventArgs args) => RefreshVisualResetRows(SearchVisualReset.Text);

    private void AddVisualResetItem(VisualAppearanceResetFlags flags, string title, string key, object value, Action restore)
    {
        object label = value is bool enabled ? Localization.Get(enabled ? "Basic_DarkModeOn" : "Basic_DarkModeOff") : value;
        var item = new VisualResetItem(flags, title, Localization.Format("Reset_DefaultValue", label))
        {
            Group = TxtVisualTitle.Text,
            Restore = restore,
            Save = () => SaveResetValue(key, value),
            SettingKey = key
        };
        VisualResetItems.Add(item);
    }

    private void RebuildVisualResetItems()
    {
        AddVisualResetItem(VisualAppearanceResetFlags.UnifiedEffectScale, CheckLinkedEffectScale.Header.ToString()!, "UseLinkedEffectScale", true,
            () => CheckLinkedEffectScale.IsOn = true);
        AddVisualResetItem(VisualAppearanceResetFlags.UnifiedEffectScale, Localization.Get("VisualReset_UnifiedScale"), "EffectScale", 1.0, () => SliderScale.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.TrailEffectScale, Localization.Get("VisualReset_TrailScale"), "TrailEffectScale", 1.0, () => SliderTrailScale.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.ClickEffectScale, Localization.Get("VisualReset_ClickScale"), "ClickEffectScale", 1.0, () => SliderClickScale.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.GlowIntensity, Localization.Get("VisualReset_GlowIntensity"), "GlowIntensity", 1.0, () => SliderGlow.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.UnifiedAnimationSpeed, CheckLinkedAnimationSpeed.Header.ToString()!, "UseLinkedAnimationSpeed", true,
            () => CheckLinkedAnimationSpeed.IsOn = true);
        AddVisualResetItem(VisualAppearanceResetFlags.UnifiedAnimationSpeed, Localization.Get("VisualReset_UnifiedSpeed"), "EffectSpeed", 1.0, () => SliderSpeed.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.TrailAnimationSpeed, Localization.Get("VisualReset_TrailSpeed"), "TrailAnimationSpeed", 1.0, () => SliderTrailAnimSpeed.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.ClickAnimationSpeed, Localization.Get("VisualReset_ClickSpeed"), "ClickAnimationSpeed", 1.0, () => SliderClickAnimSpeed.Value = 1);
        AddVisualResetItem(VisualAppearanceResetFlags.None, CheckApplyCurveDraw.Header.ToString()!, "ApplyCurveDraw", false, () => CheckApplyCurveDraw.IsOn = false);
        AddVisualResetItem(VisualAppearanceResetFlags.TrailRefreshRate, CheckFollowDisplayRefreshRate.Header.ToString()!, "FollowDisplayRefreshRate", true,
            () => CheckFollowDisplayRefreshRate.IsOn = true);
        AddVisualResetItem(VisualAppearanceResetFlags.TrailRefreshRate, Localization.Get("VisualReset_TrailRefresh"), "TrailRefreshRate", 60, () => SliderTrailRefresh.Value = 60);
        AddVisualResetItem(VisualAppearanceResetFlags.ParticleColor | VisualAppearanceResetFlags.EffectOpacity,
            Localization.Get("VisualReset_Color"), "ParticleColor", ConfigManager.DefaultThemeColor,
            () =>
            {
                _particleColor = "76,167,255";
                _effectOpacity = 1;
                UpdateColorPreview(_particleColor);
            });
    }

    private void RefreshVisualResetRows(string? filter)
    {
        var rows = new List<object>();
        string? group = null;
        foreach (VisualResetItem item in VisualResetItems)
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                !item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !item.Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !item.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (_resetScope == ResetScope.All && group != item.Group)
            {
                group = item.Group;
                rows.Add(group);
            }
            rows.Add(item);
        }
        ListVisualResetItems.ItemsSource = rows;
    }

    private void SelectionCard_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not FrameworkElement card) return;
        var point = args.GetCurrentPoint(card);
        if (args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse || point.Properties.IsLeftButtonPressed)
            card.Tag = point.Position;
    }

    private void SelectionCard_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not FrameworkElement card || card.Tag is not Point start) return;
        card.Tag = null;
        var point = args.GetCurrentPoint(card);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse &&
            point.Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased) return;
        if (Math.Abs(point.Position.X - start.X) > 8 || Math.Abs(point.Position.Y - start.Y) > 8) return;
        if (TryToggleSelectionCard(card, args.OriginalSource as DependencyObject)) args.Handled = true;
    }

    private static bool TryToggleSelectionCard(FrameworkElement card, DependencyObject? source)
    {
        if (card.DataContext is not SelectionCardItem item) return false;
        for (; source != null && source != card; source = VisualTreeHelper.GetParent(source))
            if (source is CheckBox) return false;
        item.IsSelected = !item.IsSelected;
        return true;
    }

    private async void ConfirmVisualReset_Click(object sender, RoutedEventArgs args)
    {
        VisualResetItem[] selected = VisualResetItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            await ShowMessageAsync(Localization.Get("Msg_SelectVisualReset"));
            return;
        }
        Focus(FocusState.Programmatic);
        _isLoading = true;
        _suppressValueSync = true;
        try
        {
            foreach (VisualResetItem item in selected) item.Restore?.Invoke();
            SyncSliderAndBoxValues();
        }
        finally
        {
            _suppressValueSync = false;
            _isLoading = false;
        }
        UpdateClickEffectPanelVisibility();
        UpdateEnvironmentFilterInterlock();
        UpdateEffectScalePanelVisibility();
        UpdateAnimationSpeedPanelVisibility();
        UpdateTrailRefreshInterlock();
        if (!await SetModalOverlayVisibleAsync(VisualResetOverlay, visible: false) || _isClosed) return;
        try
        {
            foreach (VisualResetItem item in selected) item.Save?.Invoke();
            MarkResetSettingsSaved(selected);
            App.Tray?.SetHidden(ConfigManager.HideTrayIcon);
            ConfigManager.GetEffectScalesForOverlay(out double trailScale, out double clickScale);
            ConfigManager.GetAnimationSpeedsForOverlay(out double trailSpeed, out double clickSpeed);
            App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
            App.Overlay?.UpdateEffectSettings(trailScale, clickScale, ConfigManager.EffectOpacity, trailSpeed, clickSpeed, ConfigManager.GlowIntensity);
            App.Overlay?.UpdateTrailRefreshRate(ConfigManager.TrailRefreshRate, ConfigManager.FollowDisplayRefreshRate);
            App.Overlay?.SetCurveDraw(ConfigManager.ApplyCurveDraw);
            App.Overlay?.UpdateTouchMode(ConfigManager.IsTouchscreenMode);
            App.Overlay?.UpdateScreenshotCompatibilityMode(ConfigManager.ScreenshotCompatibilityMode);
            App.Overlay?.RefreshEnvironmentFilterState();
            if (selected.Any(item => item.SettingKey == "Screen")) App.Overlay?.RefreshScreenSelection();
            if (selected.Any(item => item.SettingKey is "AutoStart" or "RunAsAdmin")) ApplyAutoStartSettings(useSavedSettings: true);
            if (selected.Any(item => item.SettingKey == "DarkMode")) ApplyDarkMode();
            if (selected.Any(item => item.SettingKey == "UiLanguage"))
            {
                Localization.ApplyCulture(ConfigManager.UiLanguage);
                _languageAtLoad = ConfigManager.UiLanguage;
                ApplyLocalizedText();
                App.Tray?.RefreshLocalization();
            }
            await ShowMessageAsync(Localization.Get(_resetScope == ResetScope.Visual ? "Msg_VisualResetDone" : "Msg_PageResetDone"));
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to save selected reset settings: {exception.Message}");
            UpdateApplySettingsState(SettingsSections.All);
            await ShowMessageAsync(Localization.Get("Msg_SettingsSaveFailed"));
        }
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

        UpdateEffectScalePanelVisibility(animate: true);
    }

    private void UpdateEffectScalePanelVisibility(bool animate = false) =>
        SetSettingsVisibility(SectionVisual, animate, (PanelUnifiedEffectScale, CheckLinkedEffectScale.IsOn),
            (PanelSplitEffectScale, !CheckLinkedEffectScale.IsOn));

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

        UpdateAnimationSpeedPanelVisibility(animate: true);
    }

    private void UpdateAnimationSpeedPanelVisibility(bool animate = false) =>
        SetSettingsVisibility(SectionVisual, animate, (PanelUnifiedAnimationSpeed, CheckLinkedAnimationSpeed.IsOn),
            (PanelSplitAnimationSpeed, !CheckLinkedAnimationSpeed.IsOn));

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
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = item.Title,
            Style = TryGetAppResource<Style>("BasSettingTitleStyle")
        });
        text.Children.Add(new TextBlock
        {
            Text = item.ResolutionText,
            Style = TryGetAppResource<Style>("BasCaptionStyle")
        });
        text.Children.Add(new TextBlock
        {
            Text = item.DetailText,
            Style = TryGetAppResource<Style>("BasCaptionStyle")
        });

        var toggle = new ToggleSwitch
        {
            IsOn = item.IsEnabled,
            OnContent = string.Empty,
            OffContent = string.Empty,
            Style = TryGetAppResource<Style>("BasSettingToggleStyle")
        };
        AutomationProperties.SetName(toggle, $"{item.Title} - {item.EnableLabel}");
        toggle.Toggled += (_, _) =>
        {
            item.IsEnabled = toggle.IsOn;
            QueueSettingsChangeCheck(SettingsSections.Screens);
        };

        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(text);
        row.Children.Add(toggle);

        var border = new Border
        {
            Child = row
        };

        Style? cardStyle = TryGetAppResource<Style>("BasSettingCardStyle");
        if (cardStyle != null)
        {
            border.Style = cardStyle;
        }
        else
        {
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(6);
            border.Padding = new Thickness(16, 12, 16, 12);
            border.Margin = new Thickness(0, 0, 0, 6);
            border.MinHeight = 60;
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
                AutomationProperties.SetName(toggle, $"{item.Title} - {label}");
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
        Focus(FocusState.Programmatic);
        UpdateApplySettingsState(SettingsSections.All);
        if (!BtnApplySettings.IsEnabled) return;
        _isApplyingSettings = true;
        BtnApplySettings.IsEnabled = false;
        try
        {
            await SaveSettingsCoreAsync();
        }
        finally
        {
            _isApplyingSettings = false;
            if (!_isClosed) UpdateApplySettingsState(SettingsSections.All);
        }
    }

    private async Task SaveSettingsCoreAsync()
    {
        DarkModeOption selectedDarkMode = GetSelectedDarkMode();
        string? selectedLanguage = GetSelectedLanguage() ?? _pendingLanguage;
        bool languageChanged = !string.IsNullOrWhiteSpace(selectedLanguage) &&
            !string.Equals(selectedLanguage, _languageAtLoad, StringComparison.OrdinalIgnoreCase);

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

        double effectOpacity = Math.Round(_effectOpacity, 2);
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
        bool hideTrayIcon = CheckHideTrayIcon.IsOn;
        bool runAsAdminEnabled = CheckRunAsAdmin.IsOn;
        bool isTouchscreenEnabled = CheckTouchscreenMode.IsOn;
        bool middleClickEnabled = CheckMiddleClickTrigger.IsOn;
        bool screenshotCompatibilityEnabled = CheckScreenshotCompatibilityMode.IsOn;
        int clickType = GetSelectedClickTrigger();


        // 保存配置组
        string activeId = (ComboProfiles.SelectedItem as FilterProfile)?.Id ?? string.Empty;
        ConfigManager.SaveProfiles(Profiles.ToList(), activeId);

        ConfigManager.Save("RunAsAdmin", runAsAdminEnabled);
        ConfigManager.Save("IsTouchscreenMode", isTouchscreenEnabled);
        ConfigManager.Save("IsEffectEnabled", CheckMasterSwitch.IsOn);
        ConfigManager.Save("AutoStart", autoStartEnabled);
        ConfigManager.Save("ParticleColor", ColorPickerColorMath.CombineThemeColor(_particleColor, effectOpacity));
        ConfigManager.Save("EffectScale", effectScaleForRegistry);
        ConfigManager.Save("UseLinkedEffectScale", useLinkedEffectScale);
        ConfigManager.Save("TrailEffectScale", trailEffectScale);
        ConfigManager.Save("ClickEffectScale", clickEffectScale);
        ConfigManager.Save("GlowIntensity", glowIntensity);
        ConfigManager.Save("UseLinkedAnimationSpeed", useLinkedAnimationSpeed);
        ConfigManager.Save("EffectSpeed", effectSpeedForRegistry);
        ConfigManager.Save("TrailAnimationSpeed", trailAnimSpeed);
        ConfigManager.Save("ClickAnimationSpeed", clickAnimSpeed);
        ConfigManager.Save("TrailRefreshRate", trailRefreshRate);
        ConfigManager.Save("FollowDisplayRefreshRate", followDisplayRefreshRate);
        ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);
        ConfigManager.Save("EnableAlwaysTrailEffect", CheckAlwaysTrailEffectSwitch.IsOn);
        ApplyScrollbarSettings();
        ConfigManager.Save("DarkMode", selectedDarkMode);
        ConfigManager.Save("StartSilent", startSilentEnabled);
        ConfigManager.Save("HideTrayIcon", hideTrayIcon);
        App.Tray?.SetHidden(hideTrayIcon);
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
            await ShowMessageAsync(Localization.Get("Msg_MinOneScreen"));
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

        CaptureSettingsBaseline();
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            bool isCurrentAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            if (runAsAdminEnabled && !isCurrentAdmin)
            {
                bool restartAsAdmin = await ConfirmAsync(
                    Localization.Get("Msg_AdminRestart"));

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
            LoadScreenOptions();
            _languageAtLoad = selectedLanguage!;

            bool restart = await ConfirmAsync(
                Localization.Get("Msg_LanguageRestart"));

            if (restart)
            {
                _skipSaveOnClosing = true;
                (Application.Current as App)?.RestartApplicationFromPanel();
                return;
            }
        }


        ApplyDarkMode();
    }

    // ==================================================================
    // 自启动
    // ==================================================================

    private void ApplyAutoStartSettings(bool useSavedSettings = false)
    {
        bool autoStart = useSavedSettings ? ConfigManager.AutoStart : CheckAutoStart.IsOn;
        bool runAsAdmin = useSavedSettings ? ConfigManager.RunAsAdmin : CheckRunAsAdmin.IsOn;

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
        string? taskFile = null;
        try
        {
            if (create)
            {
                string userId = WindowsIdentity.GetCurrent().User?.Value
                    ?? throw new InvalidOperationException("Cannot resolve the auto-start task user.");
                taskFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BASpark-autostart-{Guid.NewGuid():N}.xml");
                System.IO.File.WriteAllText(taskFile, AutoStartManager.BuildScheduledTaskXml(exePath, userId));
            }
            string arguments = create
                ? $"/create /tn \"{taskName}\" /xml \"{taskFile}\" /f"
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

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Cannot start the task scheduler configuration process.");
            process.WaitForExit();
            if (create && process.ExitCode != 0)
                throw new InvalidOperationException($"Task scheduler configuration failed (exit code {process.ExitCode}).");
        }
        finally
        {
            if (taskFile != null) System.IO.File.Delete(taskFile);
        }
    }

    // ==================================================================
    // 对话框 / UI 线程
    // ==================================================================

    private Task<bool> SetModalOverlayVisibleAsync(ContentDialog dialog, bool visible)
    {
        if (_isClosed) return Task.FromResult(false);
        if (_modalDialogs.TryGetValue(dialog, out ModalDialogState? state))
        {
            if (visible) return state.Opened.Task;
            state.CloseRequested = true;
            dialog.Hide();
            return state.Closed.Task;
        }
        if (!visible) return Task.FromResult(true);
        state = new ModalDialogState();
        _modalDialogs.Add(dialog, state);
        _ = ShowSelectionDialogAsync(dialog, state);
        return state.Opened.Task;
    }

    private async Task ShowSelectionDialogAsync(ContentDialog dialog, ModalDialogState state)
    {
        bool gateHeld = false;
        void Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            if (state.CloseRequested) sender.Hide();
            else
            {
                state.Opened.TrySetResult(true);
                if (sender == RenameProfileOverlay)
                {
                    NewProfileNameInput.Focus(FocusState.Programmatic);
                    NewProfileNameInput.SelectAll();
                }
            }
        }
        void Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (sender == BackupOverlay && _backupBusy && !state.CloseRequested) args.Cancel = true;
        }
        try
        {
            await _dialogGate.WaitAsync();
            gateHeld = true;
            if (_isClosed || state.CloseRequested) return;
            XamlRoot? root = await EnsureXamlRootAsync();
            if (root == null || _isClosed) return;
            if (dialog.Parent is Panel parent) parent.Children.Remove(dialog);
            if (dialog.XamlRoot != root) dialog.XamlRoot = root;
            dialog.RequestedTheme = RootGrid.ActualTheme;
            ConfigureContentDialog(dialog);
            PrepareContentDialogPresentation(dialog);
            dialog.Opened += Opened;
            dialog.Closing += Closing;
            await dialog.ShowAsync(ContentDialogPlacement.Popup);
            if (!state.CloseRequested && !_isClosed)
            {
                if (dialog == VisualResetOverlay)
                {
                    VisualResetItems.Clear();
                    ListVisualResetItems.ItemsSource = null;
                }
                else if (dialog == BackupOverlay)
                {
                    _backupSource = null;
                    _backupItems.Clear();
                    ListBackupItems.ItemsSource = null;
                }
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to show native selection dialog: {exception.Message}");
        }
        finally
        {
            dialog.Opened -= Opened;
            dialog.Closing -= Closing;
            if (_modalDialogs.TryGetValue(dialog, out ModalDialogState? current) && ReferenceEquals(state, current))
                _modalDialogs.Remove(dialog);
            if (gateHeld) _dialogGate.Release();
            state.Opened.TrySetResult(false);
            state.Closed.TrySetResult(!_isClosed);
        }
    }

    private async Task<ContentDialog?> SuspendSelectionDialogAsync()
    {
        ContentDialog? dialog = _modalDialogs.FirstOrDefault(pair =>
            pair.Value.Opened.Task.IsCompletedSuccessfully && pair.Value.Opened.Task.Result && !pair.Value.CloseRequested).Key;
        if (dialog != null) await SetModalOverlayVisibleAsync(dialog, visible: false);
        return dialog;
    }

    private void ConfigureContentDialog(ContentDialog dialog)
    {
        if (dialog.Resources.ContainsKey("BasNativeDialogConfigured")) return;
        dialog.Resources["BasNativeDialogConfigured"] = true;
        dialog.Title = null;
        dialog.Style = (Style)Application.Current.Resources["BasContentDialogStyle"];
        dialog.Resources["ContentDialogMinHeight"] = 0d;
        int animationVersion = 0;
        EventHandler<object>? readyHandler = null;
        Microsoft.UI.Xaml.Shapes.Rectangle? scrim = null;
        Storyboard? scrimAnimation = null;
        Task AnimateScrim(bool visible)
        {
            if (scrim == null) return Task.CompletedTask;
            double opacity = scrim.Opacity;
            scrimAnimation?.Stop();
            scrim.Opacity = 1;
            var completed = new TaskCompletionSource();
            scrimAnimation = new Storyboard();
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = visible ? 0 : opacity });
            fade.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Parse((string)Application.Current.Resources["ControlFasterAnimationDuration"], CultureInfo.InvariantCulture)),
                Value = visible ? 1 : 0
            });
            Storyboard.SetTarget(fade, scrim);
            Storyboard.SetTargetProperty(fade, "Opacity");
            scrimAnimation.Children.Add(fade);
            scrimAnimation.Completed += (_, _) => completed.TrySetResult();
            scrimAnimation.Begin();
            return completed.Task;
        }
        dialog.Opened += (_, _) =>
        {
            scrim = UpdateDialogScrim(dialog);
            if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
            int version = ++animationVersion;
            if (readyHandler != null) CompositionTarget.Rendering -= readyHandler;
            FrameworkElement? layout = VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot)
                .Select(popup => popup.Child is Grid { Name: "LayoutRoot" } root ? root : FindVisualDescendant<Grid>(popup.Child, "LayoutRoot"))
                .FirstOrDefault(root => root != null);
            if (layout != null) layout.Opacity = 0;
            if (scrim != null) scrim.Opacity = 0;
            readyHandler = (_, _) =>
            {
                CompositionTarget.Rendering -= readyHandler;
                readyHandler = null;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
                {
                    if (_isClosed || version != animationVersion) return;
                    _ = VisualStateManager.GoToState(dialog, "DialogHidden", false);
                    _ = VisualStateManager.GoToState(dialog, "DialogShowing", true);
                    if (layout != null)
                    {
                        layout.ClearValue(OpacityProperty);
                        FindVisualDescendant<Border>(layout, "BackgroundElement")?.ClearValue(OpacityProperty);
                    }
                    _dialogPresentationsPending.Remove(dialog);
                    _ = AnimateScrim(true);
                    await Task.Delay(TimeSpan.Parse((string)Application.Current.Resources["ControlNormalAnimationDuration"], CultureInfo.InvariantCulture));
                    if (_isClosed || version != animationVersion) return;
                    for (DependencyObject? source = FocusManager.GetFocusedElement(dialog.XamlRoot) as DependencyObject; source != null; source = VisualTreeHelper.GetParent(source))
                        if (source == layout || source == dialog) return;
                    if (dialog == RenameProfileOverlay)
                    {
                        NewProfileNameInput.Focus(FocusState.Programmatic);
                        NewProfileNameInput.SelectAll();
                    }
                    else if (layout != null)
                    {
                        string? button = dialog.DefaultButton switch
                        {
                            ContentDialogButton.Primary => "PrimaryButton",
                            ContentDialogButton.Secondary => "SecondaryButton",
                            ContentDialogButton.Close => "CloseButton",
                            _ => null
                        };
                        Control? focus = button != null ? FindVisualDescendant<Button>(layout, button) : FindVisualDescendant<TextBox>(layout);
                        focus?.Focus(FocusState.Programmatic);
                    }
                });
            };
            CompositionTarget.Rendering += readyHandler;
        };
        dialog.Closing += async (_, args) =>
        {
            animationVersion++;
            if (readyHandler != null) CompositionTarget.Rendering -= readyHandler;
            readyHandler = null;
            if (_isClosed || args.Cancel || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ||
                dialog == BackupOverlay && _backupBusy && (!_modalDialogs.TryGetValue(dialog, out ModalDialogState? state) || !state.CloseRequested)) return;
            var deferral = args.GetDeferral();
            _dialogPresentationsPending.Remove(dialog);
            try
            {
                var ready = new TaskCompletionSource();
                if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ready.TrySetResult())) return;
                await ready.Task;
                if (_isClosed || args.Cancel) return;
                dialog.UpdateLayout();
                Task scrimClosed = AnimateScrim(false);
                VisualStateManager.GoToState(dialog, "DialogShowing", false);
                if (VisualStateManager.GoToState(dialog, "DialogHidden", true))
                    await Task.WhenAll(
                        Task.Delay(TimeSpan.Parse((string)Application.Current.Resources["ControlFastAnimationDuration"], CultureInfo.InvariantCulture)),
                        Task.WhenAny(scrimClosed, Task.Delay(TimeSpan.Parse((string)Application.Current.Resources["ControlNormalAnimationDuration"], CultureInfo.InvariantCulture))));
            }
            catch (Exception exception)
            {
                AppLogger.Warn($"Failed to animate native dialog closing: {exception.Message}");
            }
            finally
            {
                try { deferral.Complete(); }
                catch (Exception exception) { AppLogger.Debug($"Native dialog already closed: {exception.Message}"); }
            }
        };
        dialog.Closed += (_, _) =>
        {
            _dialogPresentationsPending.Remove(dialog);
            scrimAnimation?.Stop();
            scrimAnimation = null;
            scrim = null;
            if (string.IsNullOrEmpty(dialog.Name)) _dialogPresentationGuards.Remove(dialog);
        };
        dialog.Loading += (_, _) => UpdateDialogScrim(dialog);
        dialog.Loaded += (_, _) =>
        {
            UpdateDialogScrim(dialog);
            if ((!string.IsNullOrEmpty(dialog.PrimaryButtonText) || !string.IsNullOrEmpty(dialog.SecondaryButtonText) ||
                 !string.IsNullOrEmpty(dialog.CloseButtonText)) &&
                FindVisualDescendant<ScrollViewer>(dialog, "ContentScrollViewer")?.Content is Grid content)
                content.Padding = new Thickness(content.Padding.Left, content.Padding.Top, content.Padding.Right, 0);
            if (FindVisualDescendant<Grid>(dialog, "CommandSpace") is { } commands)
            {
                commands.HorizontalAlignment = HorizontalAlignment.Right;
                foreach (ColumnDefinition column in commands.ColumnDefinitions) column.Width = GridLength.Auto;
                if (!string.IsNullOrEmpty(dialog.PrimaryButtonText) && !string.IsNullOrEmpty(dialog.CloseButtonText) &&
                    string.IsNullOrEmpty(dialog.SecondaryButtonText) && commands.ColumnDefinitions.Count == 5 &&
                    FindVisualDescendant<Button>(dialog, "CloseButton") is { } cancel &&
                    FindVisualDescendant<Button>(dialog, "PrimaryButton") is { } confirm)
                {
                    Grid.SetColumn(cancel, 0);
                    Grid.SetColumn(confirm, 4);
                    commands.ColumnDefinitions[3].Width = new GridLength(8);
                }
            }
            foreach (string name in new[] { "PrimaryButton", "SecondaryButton", "CloseButton" })
            {
                if (FindVisualDescendant<Button>(dialog, name) is { } button)
                {
                    button.MinWidth = 0;
                    button.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }
        };
    }

    private void PrepareContentDialogPresentation(ContentDialog dialog)
    {
        if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) _dialogPresentationsPending.Add(dialog);
        dialog.ApplyTemplate();
        Border? background = FindVisualDescendant<Border>(dialog, "BackgroundElement") ??
            (_dialogPresentationGuards.TryGetValue(dialog, out Border? saved) ? saved : null);
        if (background == null) return;
        _dialogPresentationGuards[dialog] = background;
        background.Opacity = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ? 0 : 1;
    }

    private Microsoft.UI.Xaml.Shapes.Rectangle? UpdateDialogScrim(ContentDialog dialog)
    {
        if (dialog.XamlRoot == null || _isClosed) return null;
        if (FindVisualDescendant<Grid>(dialog, "LayoutRoot") is { } layout)
        {
            layout.Width = RootGrid.ActualWidth;
            layout.Height = RootGrid.ActualHeight;
        }
        Microsoft.UI.Xaml.Shapes.Rectangle? mask = null;
        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot))
        {
            if (popup.Child is Microsoft.UI.Xaml.Shapes.Rectangle scrim)
            {
                scrim.RequestedTheme = dialog.RequestedTheme;
                scrim.Style = (Style)Application.Current.Resources["BasNativeDialogScrimStyle"];
                scrim.ClearValue(Microsoft.UI.Xaml.Shapes.Shape.FillProperty);
                Point origin = PanelBody.TransformToVisual(RootGrid).TransformPoint(new Point());
                scrim.Clip = new RectangleGeometry { Rect = new Rect(origin.X, origin.Y, PanelBody.ActualWidth, PanelBody.ActualHeight) };
                if (_dialogPresentationsPending.Contains(dialog)) scrim.Opacity = 0;
                mask = scrim;
            }
        }
        return mask;
    }

    private async Task ShowMessageAsync(string message)
    {
        bool gateHeld = false;
        ContentDialog? suspended = null;
        try
        {
            suspended = await SuspendSelectionDialogAsync();
            await _dialogGate.WaitAsync();
            gateHeld = true;
            XamlRoot? root = await EnsureXamlRootAsync();
            if (root == null || _isClosed) return;
            _messageDialog = new ContentDialog
            {
                XamlRoot = root,
                RequestedTheme = RootGrid.ActualTheme,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = Localization.Get("ColorPicker_Confirm"),
                DefaultButton = ContentDialogButton.Close
            };
            ConfigureContentDialog(_messageDialog);
            PrepareContentDialogPresentation(_messageDialog);
            await _messageDialog.ShowAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to show in-window message: {exception.Message}");
        }
        finally
        {
            _messageDialog = null;
            if (gateHeld) _dialogGate.Release();
            if (suspended != null && !_isClosed) await SetModalOverlayVisibleAsync(suspended, visible: true);
        }
    }

    private async Task<bool> ConfirmAsync(string message, string? confirmText = null)
    {
        bool gateHeld = false;
        ContentDialog? suspended = null;
        try
        {
            suspended = await SuspendSelectionDialogAsync();
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
                RequestedTheme = RootGrid.ActualTheme,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = confirmText ?? Localization.Get("ColorPicker_Confirm"),
                CloseButtonText = Localization.Get("Overlay_Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            _messageDialog = dialog;
            ConfigureContentDialog(dialog);
            PrepareContentDialogPresentation(dialog);
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to show confirmation dialog: {ex.Message}");
            return false;
        }
        finally
        {
            _messageDialog = null;
            if (gateHeld)
            {
                _dialogGate.Release();
            }
            if (suspended != null && !_isClosed) await SetModalOverlayVisibleAsync(suspended, visible: true);
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

    private void ControlPanelWindow_Closed(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;

        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        RootGrid.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootGrid_PointerPressed));
        RootGrid.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ColorPicker_PointerFinished));
        RootGrid.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ColorPicker_PointerFinished));
        RootGrid.RemoveHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(ToggleSwitch_DoubleTapped));
        RootGrid.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ToggleSwitch_PointerReleased));
        RootGrid.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ToggleSwitch_PointerCanceled));
        EffectColorPicker.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ColorPicker_PointerPressed));
        EffectColorPicker.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ColorPicker_PointerFinished));
        EffectColorPicker.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ColorPicker_PointerFinished));
        EffectColorPicker.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ColorPicker_PointerFinished));
        foreach (var pair in _sliderToBox)
        {
            pair.Key.ValueChanged -= EffectSlider_ValueChanged;
            pair.Value.ValueChanged -= EffectNumberBox_ValueChanged;
        }
        CheckFollowDisplayRefreshRate.Toggled -= FollowDisplayRefreshRate_Toggled;
        foreach (TextBox input in _numberInputs.Values) input.Loaded -= InputControl_Loaded;
        EffectColorHexInput.Loaded -= InputControl_Loaded;
        NewProfileNameInput.Loaded -= InputControl_Loaded;
        if (RootGrid.XamlRoot is { } root) root.Changed -= PanelXamlRoot_Changed;
        RootGrid.Loaded -= RootGrid_Loaded;
        _host.CloseRequested -= ControlPanelWindow_Closed;
        _messageDialog?.Hide();
        StopSettingsAnimations();
        foreach (var pair in _modalDialogs)
        {
            pair.Value.CloseRequested = true;
            pair.Key.Hide();
            pair.Value.Opened.TrySetResult(false);
            pair.Value.Closed.TrySetResult(false);
        }
        _modalDialogs.Clear();
        _dialogPresentationGuards.Clear();
        _dialogPresentationsPending.Clear();
        foreach (var callback in _navigationIndicatorCallbacks) callback.Item.UnregisterPropertyChangedCallback(callback.Property, callback.Token);
        foreach (var state in _navigationIndicators.Values)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(state.Indicator);
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
        }
        _navigationIndicators.Clear();
        foreach (var callback in _settingsCallbacks) callback.Control.UnregisterPropertyChangedCallback(callback.Property, callback.Token);
        Profiles.CollectionChanged -= GeneralSettingsCollectionChanged;
        CurrentProfileProcesses.CollectionChanged -= GeneralSettingsCollectionChanged;
        ScreenOptions.CollectionChanged -= ScreenSettingsCollectionChanged;
        foreach (var selector in _segmentedSelectors) selector.Dispose();
        StopColorPreviewRendering();
        _colorCardGeometry?.StopAnimation("Size.Y");
        if (_colorAlphaSlider != null)
        {
            _colorAlphaSlider.ValueChanged -= ColorAlphaSlider_ValueChanged;
        }

        if (!_skipSaveOnClosing)
        {
            ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);
        }

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        AppLogger.EntryAdded -= OnAppLogEntryAdded;
        App.EffectsPauseChanged -= EffectsPauseChanged;

        try
        {
            _refreshTimer?.Stop();
            if (_refreshTimer != null) _refreshTimer.Tick -= RefreshTimer_OnTick;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to stop panel timers during close: {ex.Message}");
        }

        _host.Dispose();
        Content = null;
        Resources.Clear();
        _backupSource = null;
        _backupItems.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
