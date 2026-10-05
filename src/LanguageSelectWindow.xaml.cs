using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BASpark;

/// <summary>
/// 语言选择窗口。WinUI 3 没有模态 <c>ShowDialog</c>，
/// 结果通过 <see cref="ShowDialogAsync"/> 返回：继续得到所选区域标记，关闭得到 null。
/// </summary>
public partial class LanguageSelectWindow : UserControl
{
    private const int DesignWidth = 560;
    private const int DesignHeight = 260;

    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly string _displayCulture;

    private bool _closed;
    private readonly DcompPanelHost _host;
    public IntPtr Handle => _host.Handle;
    public AppWindow AppWindow => _host.AppWindow!;
    public event EventHandler? Closed;

    /// <summary>构造期间（默认选中项）不切换全局语言，只有用户勾选才预览。</summary>
    private bool _initializing = true;

    /// <summary>用户点“继续”后选中的语言标记；未确认时为 null。</summary>
    public string? SelectedCulture { get; private set; }

    public LanguageSelectWindow()
    {
        _displayCulture = Localization.DetectCultureFromSystem();
        InitializeComponent();
        _host = new DcompPanelHost(fixedSize: true, minimumWidth: DesignWidth, minimumHeight: 1);
        WindowChrome.ApplyTitleBarIcon(AppTitleBar);
        StartupDialogHost.ConfigureCaptionButton(_host, RootGrid, BtnCaptionClose);

        RootGrid.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(_host, RootGrid));

        AppTitleBar.Title = Localization.Get("LangSelect_Title", _displayCulture);
        ApplyLanguageText(_displayCulture);

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.ResolveElementTheme();
            root.Loaded += LanguageSelectWindow_Loaded;
        }

        _host.CloseRequested += (_, _) => _ = CloseAsync();
        _host.SetContent(this);
        WindowChrome.ApplyAppIcon(Handle);
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

        _host.CenterOnCurrentDisplay(DesignWidth, DesignHeight);
        StartupDialogHost.ShowWhenReady(_host, RootGrid);
        return _completion.Task;
    }

    // ------------------------------------------------------------------
    // 文案与默认选中项
    // ------------------------------------------------------------------

    private void ApplyLanguageText(string cultureName)
    {
        _host.SetTitle(Localization.Get("LangSelect_Title", cultureName));
        AppTitleBar.Title = Localization.Get("LangSelect_Title", cultureName);
        ToolTipService.SetToolTip(BtnCaptionClose, Localization.Get("Window_Close", cultureName));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BtnCaptionClose, Localization.Get("Window_Close", cultureName));
        TxtSubtitle.Text = Localization.Get("LangSelect_Subtitle", cultureName);
        TxtLanguageLabel.Text = Localization.Get("Basic_Language", cultureName);
        LanguageChinese.Content = Localization.Get("LangSelect_Chinese", cultureName);
        LanguageEnglish.Content = Localization.Get("LangSelect_English", cultureName);
        LanguageJapanese.Content = Localization.Get("LangSelect_Japanese", cultureName);
        BtnContinue.Content = Localization.Get("LangSelect_Continue", cultureName);
        if (RootGrid.IsLoaded) DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(_host, RootGrid));
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
        _ = CloseAsync();
    }

    private void CaptionClose_Click(object sender, RoutedEventArgs args) => _ = CloseAsync();

    public void Close() => _ = CloseAsync();

    private async Task CloseAsync()
    {
        if (_closed) return;
        _closed = true;
        await StartupDialogHost.CloseAsync(_host);
        Closed?.Invoke(this, EventArgs.Empty);
        _completion.TrySetResult(SelectedCulture);
    }

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
            DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(_host, root));
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
            WindowChrome.ApplyTitleBarTheme(Handle, theme == ElementTheme.Dark);
        }
        catch (Exception)
        {
        }
    }
}
