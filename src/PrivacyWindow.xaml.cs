using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace BASpark;

/// <summary>
/// 隐私 / 遥测说明窗口。WinUI 3 没有模态 <c>ShowDialog</c>，
/// 结果通过 <see cref="ShowDialogAsync"/> 返回：同意得到 true，拒绝或关闭得到 false。
/// </summary>
public partial class PrivacyWindow : Window
{
    private const int DesignWidth = 500;
    private const int DesignHeight = 660;

    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _closed;

    /// <summary>用户点“同意”后为 true；拒绝或直接关闭为 false。</summary>
    public bool Agreed { get; private set; }

    public PrivacyWindow()
    {
        InitializeComponent();

        ApplyLocalizedText();

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.ResolveElementTheme();
            root.Loaded += PrivacyWindow_Loaded;
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;

        WindowChrome.ApplyAppIcon(this);
        ApplyTitleBarTheme(App.ResolveElementTheme());

        LoadVersion();
    }

    /// <summary>返回 true 表示同意，false 表示拒绝或直接关闭窗口。</summary>
    public Task<bool> ShowDialogAsync(XamlRoot xamlRoot)
    {
        if (_closed)
        {
            return Task.FromResult(false);
        }

        WindowChrome.SetInitialSize(this, DesignWidth, DesignHeight);

        Closed += PrivacyWindow_Closed;
        Activate();
        return _completion.Task;
    }

    // ------------------------------------------------------------------
    // 文案与版本号
    // ------------------------------------------------------------------

    private void ApplyLocalizedText()
    {
        Title = Localization.Get("Privacy_Title");
        TxtTagline.Text = Localization.Get("Privacy_Tagline");
        TxtIntro.Text = Localization.Get("Privacy_Intro");
        TxtOpenSourceTitle.Text = Localization.Get("Privacy_OpenSource_Title");
        TxtOpenSourceBody.Text = Localization.Get("Privacy_OpenSource_Body");
        TxtSecurityTitle.Text = Localization.Get("Privacy_Security_Title");
        TxtSecurityBody.Text = Localization.Get("Privacy_Security_Body");
        TxtPrivacyTitle.Text = Localization.Get("Privacy_Privacy_Title");
        TxtPrivacyBody.Text = Localization.Get("Privacy_Privacy_Body");
        CheckTelemetry.Content = Localization.Get("Privacy_Telemetry");
        BtnRefuse.Content = Localization.Get("Privacy_Refuse");
        BtnAgree.Content = Localization.Get("Privacy_Agree");
    }

    private void LoadVersion()
    {
        try
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;

            if (version != null)
            {
                VersionText.Text = $"{version.Major}.{version.Minor}.{version.Build}-release";
            }
        }
        catch
        {
            VersionText.Text = Localization.Get("Privacy_VersionFailed");
        }
    }

    // ------------------------------------------------------------------
    // 按钮
    // ------------------------------------------------------------------

    private void BtnAgree_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        ConfigManager.Save("AgreedToPrivacy", true);
        ConfigManager.Save("EnableTelemetry", CheckTelemetry.IsChecked ?? false);

        Agreed = true;
        Complete(true);
    }

    private void BtnRefuse_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        Agreed = false;
        Complete(false);
    }

    private void Complete(bool result)
    {
        _completion.TrySetResult(result);
        Close();
    }

    // ------------------------------------------------------------------
    // 窗口生命周期
    // ------------------------------------------------------------------

    private void PrivacyWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (Content is FrameworkElement root)
        {
            ApplyTitleBarTheme(root.ActualTheme);
        }
    }

    private void PrivacyWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = sender;
        _ = args;
        _closed = true;
        _completion.TrySetResult(false);
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
            AppLogger.Debug($"隐私窗口标题栏主题设置失败：{ex.Message}");
        }
    }
}
