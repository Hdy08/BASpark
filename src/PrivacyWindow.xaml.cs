using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BASpark;

/// <summary>
/// 隐私 / 遥测说明窗口。WinUI 3 没有模态 <c>ShowDialog</c>，
/// 结果通过 <see cref="ShowDialogAsync"/> 返回：同意得到 true，拒绝或关闭得到 false。
/// </summary>
public partial class PrivacyWindow : UserControl
{
    private const int DesignWidth = 560;
    private const int DesignHeight = 560;

    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _closed;
    private readonly DcompPanelHost _host;
    public IntPtr Handle => _host.Handle;
    public AppWindow AppWindow => _host.AppWindow!;
    public event EventHandler? Closed;

    /// <summary>用户点“同意”后为 true；拒绝或直接关闭为 false。</summary>
    public bool Agreed { get; private set; }

    public PrivacyWindow()
    {
        InitializeComponent();
        _host = new DcompPanelHost(fixedSize: true, minimumWidth: 1, minimumHeight: 1);
        WindowChrome.ApplyTitleBarIcon(AppTitleBar);
        StartupDialogHost.ConfigureCaptionButton(_host, RootGrid, BtnCaptionClose);

        RootGrid.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => StartupDialogHost.FitToContent(_host, RootGrid));

        ApplyLocalizedText();

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.ResolveElementTheme();
            root.Loaded += PrivacyWindow_Loaded;
        }

        _host.CloseRequested += (_, _) => _ = CloseAsync();
        _host.SetContent(this);
        WindowChrome.ApplyAppIcon(Handle);
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

        _host.CenterOnCurrentDisplay(DesignWidth, DesignHeight);
        StartupDialogHost.ShowWhenReady(_host, RootGrid);
        return _completion.Task;
    }

    // ------------------------------------------------------------------
    // 文案与版本号
    // ------------------------------------------------------------------

    private void ApplyLocalizedText()
    {
        _host.SetTitle(Localization.Get("Privacy_Title"));
        AppTitleBar.Title = Localization.Get("Privacy_Title");
        ToolTipService.SetToolTip(BtnCaptionClose, Localization.Get("Window_Close"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BtnCaptionClose, Localization.Get("Window_Close"));
        TxtTagline.Text = Localization.Get("Privacy_Tagline");
        TxtIntro.Text = Localization.Get("Privacy_Intro");
        TxtOpenSourceTitle.Text = Localization.Get("Privacy_OpenSource_Title");
        TxtOpenSourceBody.Text = Localization.Get("Privacy_OpenSource_Body");
        TxtSecurityTitle.Text = Localization.Get("Privacy_Security_Title");
        TxtSecurityBody.Text = Localization.Get("Privacy_Security_Body");
        TxtPrivacyTitle.Text = Localization.Get("Privacy_Privacy_Title");
        TxtPrivacyBody.Text = Localization.Get("Privacy_Privacy_Body");
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
                VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}-release";
            }
        }
        catch
        {
            VersionText.Text = Localization.Get("Privacy_VersionFailed");
        }
    }

    private void WelcomeInfoBar_Loaded(object sender, RoutedEventArgs args)
    {
        void AlignContent(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is ContentPresenter { Name: "ContentArea" } content) Grid.SetRow(content, 0);
                else if (child is InfoBarPanel panel) panel.Visibility = Visibility.Collapsed;
                AlignContent(child);
            }
        }
        AlignContent((InfoBar)sender);
    }

    // ------------------------------------------------------------------
    // 按钮
    // ------------------------------------------------------------------

    private void BtnAgree_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        ConfigManager.Save("AgreedToPrivacy", true);

        Agreed = true;
        Complete(true);
    }

    private void CaptionClose_Click(object sender, RoutedEventArgs args) => _ = CloseAsync();

    private void BtnRefuse_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        Agreed = false;
        Complete(false);
    }

    private void Complete(bool result)
    {
        Agreed = result;
        _ = CloseAsync();
    }

    public void Close() => _ = CloseAsync();

    private async Task CloseAsync()
    {
        if (_closed) return;
        _closed = true;
        await StartupDialogHost.CloseAsync(_host);
        Closed?.Invoke(this, EventArgs.Empty);
        _completion.TrySetResult(Agreed);
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
