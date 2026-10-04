using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BASpark;

/// <summary>
/// 语言选择窗口。WinUI 3 没有模态 <c>ShowDialog</c>，
/// 结果通过 <see cref="ShowDialogAsync"/> 返回：继续得到所选区域标记，关闭得到 null。
/// </summary>
public partial class LanguageSelectWindow : Window
{
    private const int DesignWidth = 560;
    private const int DesignHeight = 260;

    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly string _displayCulture;

    private bool _closed;
    private bool _titleBarApplied;

    /// <summary>构造期间（默认选中项）不切换全局语言，只有用户勾选才预览。</summary>
    private bool _initializing = true;

    /// <summary>用户点“继续”后选中的语言标记；未确认时为 null。</summary>
    public string? SelectedCulture { get; private set; }

    public LanguageSelectWindow()
    {
        _displayCulture = Localization.DetectCultureFromSystem();
        InitializeComponent();
        AppTitleBar.IconSource = WindowChrome.CreateAppIconSource();
        StartupDialogHost.ConfigureCaptionButton(this, RootGrid, BtnCaptionClose);

        // 去掉系统标题栏、改用原生 TitleBar 控件；必须在视觉树加载后执行。
        RootGrid.Loaded += (_, _) => ApplyCustomTitleBar();
        RootGrid.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(this, RootGrid));

        Title = Localization.Get("LangSelect_Title", _displayCulture);
        AppTitleBar.Title = Localization.Get("LangSelect_Title", _displayCulture);
        ApplyLanguageText(_displayCulture);

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.ResolveElementTheme();
            root.Loaded += LanguageSelectWindow_Loaded;
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        StartupDialogHost.LockWindow(this);

        WindowChrome.ApplyAppIcon(this);
        ApplyTitleBarTheme(App.ResolveElementTheme());

        SelectDefaultLanguage();
        _initializing = false;
    }

    /// <summary>返回所选语言标记（zh-CN / en / ja）；窗口被关闭时返回 null。</summary>
    public Task<string?> ShowDialogAsync(XamlRoot xamlRoot)
    {
        if (_closed)
        {
            return Task.FromResult<string?>(null);
        }

        WindowChrome.SetInitialSize(this, DesignWidth, DesignHeight);

        Closed += LanguageSelectWindow_Closed;
        StartupDialogHost.ShowWhenReady(this, RootGrid);
        return _completion.Task;
    }

    // ------------------------------------------------------------------
    // 文案与默认选中项
    // ------------------------------------------------------------------

    private void ApplyLanguageText(string cultureName)
    {
        Title = Localization.Get("LangSelect_Title", cultureName);
        AppTitleBar.Title = Localization.Get("LangSelect_Title", cultureName);
        ToolTipService.SetToolTip(BtnCaptionClose, Localization.Get("Window_Close", cultureName));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BtnCaptionClose, Localization.Get("Window_Close", cultureName));
        TxtSubtitle.Text = Localization.Get("LangSelect_Subtitle", cultureName);
        TxtLanguageLabel.Text = Localization.Get("Basic_Language", cultureName);
        LanguageChinese.Content = Localization.Get("LangSelect_Chinese", cultureName);
        LanguageEnglish.Content = Localization.Get("LangSelect_English", cultureName);
        LanguageJapanese.Content = Localization.Get("LangSelect_Japanese", cultureName);
        BtnContinue.Content = Localization.Get("LangSelect_Continue", cultureName);
        if (RootGrid.IsLoaded) DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(this, RootGrid));
    }

    private void SelectDefaultLanguage()
    {
        ComboLanguage.SelectedItem = ComboLanguage.Items.OfType<ComboBoxItem>()
            .First(item => item.Tag as string == _displayCulture);
    }

    /// <summary>选择即预览：立刻按目标语言刷新对话框文案；用户操作时同步全局语言。</summary>
    private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = e;
        if (ComboLanguage.SelectedItem is not ComboBoxItem { Tag: string culture } || string.IsNullOrEmpty(culture))
        {
            return;
        }

        ApplyLanguageText(culture);
        if (!_initializing)
        {
            Localization.ApplyCulture(culture);
        }
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        string selected = (ComboLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? _displayCulture;

        ConfigManager.Save("UiLanguage", selected);
        Localization.ApplyCulture(selected);
        SelectedCulture = selected;
        _ = StartupDialogHost.CloseAsync(this);
    }

    private void CaptionClose_Click(object sender, RoutedEventArgs args) => _ = StartupDialogHost.CloseAsync(this);

    // ------------------------------------------------------------------
    // 窗口生命周期
    // ------------------------------------------------------------------

    private void LanguageSelectWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (Content is FrameworkElement root)
        {
            ApplyTitleBarTheme(root.ActualTheme);
            DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(this, root));
        }
    }

    private void LanguageSelectWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;
        _closed = true;
        _completion.TrySetResult(SelectedCulture);
    }

    /// <summary>
    /// 去掉系统标题栏、改用原生 <c>TitleBar</c> 控件。
    ///
    /// 两个必须遵守的约束（已在 ControlPanelWindow 上实测）：
    ///   1. 只设置 <c>ExtendsContentIntoTitleBar</c>，**不要**再调用
    ///      <c>SetTitleBar(AppTitleBar)</c>。SetTitleBar 只适用于普通 UIElement
    ///      拖拽区域；对 TitleBar 控件调用会抛 E_BOUNDS（0x800f1000），异常在
    ///      Microsoft.UI.Xaml.dll 内未被捕获，进程直接崩溃。
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
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            StartupDialogHost.LockWindow(this);
        }
        catch (Exception ex)
        {
            // 失败时回退到系统标题栏，功能不受影响。
            AppLogger.Warn($"Failed to extend content into the title bar: {ex.Message}");
        }
    }

    /// <summary>
    /// WinUI 只负责内容区主题，非客户区标题栏要自己跟随；
    /// <see cref="ElementTheme.Default"/> 时留给 <c>Loaded</c> 用实际主题校准。
    /// </summary>
    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (theme == ElementTheme.Default)
        {
            return;
        }

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

            titleBar.PreferredTheme = theme == ElementTheme.Dark
                ? TitleBarTheme.Dark
                : TitleBarTheme.Light;
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"语言选择标题栏主题设置失败：{ex.Message}");
        }
    }
}
