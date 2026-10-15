using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;

namespace BASpark;

public sealed partial class InstallerWindow : UserControl
{
    private readonly InstallerSession _session;
    private readonly IInstallerBackend _backend;
    private readonly DcompPanelHost _host;
    private readonly UIElement[] _pages;
    private readonly Queue<string> _output = new();
    private Panel? _errorMessages;
    private Storyboard? _errorStoryboard;
    private bool _errorVisible;
    private bool _initializing = true;
    private bool _closed;
    private bool _busy;
    private bool _failed;
    private int _step;
    public event EventHandler? Closed;

    internal InstallerWindow(InstallerSession session, IInstallerBackend? backend = null)
    {
        _session = session;
        _backend = backend ?? session;
        InitializeComponent();
        _pages = [LanguagePage, WelcomePage, ConfirmPage, ProgressPage, CompletePage, UninstallPage];
        PagePool.Children.Clear();
        _host = new DcompPanelHost(fixedSize: true, minimumWidth: 1, minimumHeight: 1);
        if (_host.AppWindow is { } appWindow)
        {
            appWindow.IsShownInSwitchers = true;
            if (appWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = false;
        }
        WindowChrome.ApplyTitleBarIcon(AppTitleBar);
        StartupDialogHost.ConfigureCaptionButton(_host, RootGrid, BtnCaptionClose);
        RootGrid.ActualThemeChanged += (_, _) => WindowChrome.ApplyTitleBarTheme(_host.Handle, RootGrid.ActualTheme == ElementTheme.Dark);
        DirectoryInput.Text = session.Directory;
        ComboLanguage.SelectedItem = ComboLanguage.Items.OfType<ComboBoxItem>().First(item =>
            item.Tag as string == Localization.DetectCultureFromSystem());
        Localization.ApplyCulture((ComboLanguage.SelectedItem as ComboBoxItem)?.Tag as string);
        _host.SetContent(this);
        WindowChrome.ApplyAppIcon(_host.Handle);
        _host.CloseRequested += (_, _) => { if (!_busy) _ = CloseAsync(); };
        _initializing = false;
        ApplyText();
        Navigate(session.IsUninstall ? 5 : 0);
    }

    public void Show()
    {
        _host.CenterOnCurrentDisplay(_session.IsUninstall ? 640 : 800, _session.IsUninstall ? 360 : 450);
        async void Ready(object sender, RoutedEventArgs args)
        {
            RootGrid.Loaded -= Ready;
            RootGrid.UpdateLayout();
            try { await new RenderTargetBitmap().RenderAsync(RootGrid); }
            catch (Exception) { }
            if (!_closed) StartupDialogHost.ShowWhenReady(_host, RootGrid, fitToContent: false);
        }
        if (RootGrid.IsLoaded) Ready(RootGrid, new RoutedEventArgs());
        else RootGrid.Loaded += Ready;
    }

    private string Text(string key) => Localization.Get(key);

    private void ApplyText()
    {
        string title = Text(_session.IsUninstall ? "Installer_UninstallerTitle" : "Installer_Title");
        AppTitleBar.Title = title;
        _host.SetTitle(title);
        ToolTipService.SetToolTip(BtnCaptionClose, Text("Window_Close"));
        AutomationProperties.SetName(BtnCaptionClose, Text("Window_Close"));
        LanguageTitle.Text = Text("Installer_LanguageTitle");
        LanguagePrompt.Text = Text("LangSelect_Subtitle");
        LanguageLabel.Text = Text("Basic_Language");
        WelcomeTitle.Text = "BASpark V" + _session.Version;
        WelcomeTagline.Text = Text("Privacy_Tagline");
        WelcomeIntro.Text = Text("Privacy_Intro");
        OpenSourceTitle.Text = Text("Privacy_OpenSource_Title");
        OpenSourceBody.Text = Text("Privacy_OpenSource_Body");
        SecurityTitle.Text = Text("Privacy_Security_Title");
        SecurityBody.Text = Text("Privacy_Security_Body");
        PrivacyTitle.Text = Text("Privacy_Privacy_Title");
        PrivacyBody.Text = Text("Privacy_Privacy_Body");
        CheckAgreement.Content = Text("Installer_Agreement");
        ConfirmTitle.Text = Text("Installer_ConfirmTitle");
        ConfirmDescription.Text = Text(_session.IsUpdate ? "Installer_UpdateDescription" : "Installer_ConfirmDescription");
        DirectoryLabel.Text = CompleteDirectoryLabel.Text = Text("Installer_Directory");
        ToolTipService.SetToolTip(BtnBrowse, Text("Installer_Browse"));
        AutomationProperties.SetName(BtnBrowse, Text("Installer_Browse"));
        StartMenuLabel.Text = Text("Installer_StartMenu");
        CheckDesktop.Content = Text("Installer_DesktopShortcut");
        CertificateConsent.Text = Text("Installer_CertificateConsent");
        CheckLaunch.Content = Text("Installer_Launch");
        CheckKeepData.Content = Text("Installer_KeepData");
        UninstallTitle.Text = Text("Installer_UninstallTitle");
        UninstallDescription.Text = Text("Installer_UninstallDescription");
        UninstallProduct.Text = "BASpark V" + _session.Version;
        UninstallDirectory.Text = _session.Directory;
        BtnCancel.Content = Text("Installer_Cancel");
        BtnBack.Content = Text("Installer_Back");
        AutomationProperties.SetName(DirectoryInput, DirectoryLabel.Text);
        AutomationProperties.SetName(StartMenuInput, StartMenuLabel.Text);
        AutomationProperties.SetName(ToggleStartMenu, StartMenuLabel.Text);
        AutomationProperties.SetName(ComboLanguage, LanguageLabel.Text);
        foreach (CheckBox choice in new[] { CheckAgreement, CheckDesktop, CheckCertificate, CheckLaunch, CheckKeepData })
            if (choice.IsLoaded) StartupDialogHost.AlignChoiceContent(choice);
        UpdateActions();
    }

    private void Navigate(int step)
    {
        _step = step;
        SetErrorMessage(null, animate: false);
        Panel messages = step == 2 ? ConfirmCards : step == 4 ? CompleteMessages : ProgressOutput;
        if (!ReferenceEquals(_errorMessages, messages))
        {
            _errorMessages?.Children.Remove(ErrorInfo);
            Grid.SetRow(ErrorInfo, messages == ProgressOutput ? 1 : 0);
            ErrorInfo.Margin = messages == ProgressOutput ? new Thickness(0, 4, 0, 0) : new Thickness(0);
            messages.Children.Add(ErrorInfo);
            _errorMessages = messages;
        }
        PageContent.Content = _pages[step];
        DirectoryCard.Visibility = _session.IsUpdate ? Visibility.Collapsed : Visibility.Visible;
        CheckCertificate.Visibility = _session.RequiresCertificate ? Visibility.Visible : Visibility.Collapsed;
        if (step == 2) Directory_Changed(this, null!);
        UpdateActions();
    }

    private void UpdateActions()
    {
        if (_initializing) return;
        static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
        CheckAgreement.Visibility = Show(_step == 1);
        InstallChoices.Visibility = Show(_step == 2);
        CheckKeepData.Visibility = Show(_step == 5);
        CheckLaunch.Visibility = Show(_step == 4 && !_session.IsUninstall);
        ActionButtons.Visibility = Show(!_busy);
        BtnCaptionClose.IsEnabled = !_busy;
        BtnCancel.Visibility = Show(_step != 4);
        BtnBack.Visibility = Show(_step is 1 or 2);
        BtnNext.Content = Text(_step == 4 ? "Installer_Finish" : _failed ? "Installer_Retry" :
            _step == 5 ? "Installer_Uninstall" : _step == 2 ? "Installer_Install" : "LangSelect_Continue");
        BtnNext.IsEnabled = !_busy && (_step != 1 || CheckAgreement.IsChecked == true) &&
            (_step != 2 || _session.Validate(GetChoices()) == null);
    }

    private InstallerChoices GetChoices() => new(DirectoryInput.Text.Trim(), StartMenuInput.Text.Trim(),
        ToggleStartMenu.IsOn, CheckDesktop.IsChecked == true, CheckCertificate.IsChecked == true,
        CheckKeepData.IsChecked == true);

    private void Language_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_initializing) return;
        Localization.ApplyCulture((ComboLanguage.SelectedItem as ComboBoxItem)?.Tag as string);
        ApplyText();
    }

    private void Choice_Changed(object sender, RoutedEventArgs args)
    {
        if (!_initializing && _step == 2) Directory_Changed(sender, null!);
        else UpdateActions();
    }

    private void Directory_Changed(object sender, TextChangedEventArgs args)
    {
        if (_initializing) return;
        string? invalid = _session.Validate(GetChoices());
        SetErrorMessage(invalid is null or "Installer_CertificateRequired" ? null : Text(invalid));
        UpdateActions();
    }

    private void Input_Loaded(object sender, RoutedEventArgs args)
    {
        var input = (TextBox)sender;
        input.ApplyTemplate();
        input.Padding = new Thickness(10, 0, 10, 0);
        static void Align(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is ScrollViewer { Name: "ContentElement" } content)
                {
                    content.VerticalAlignment = VerticalAlignment.Center;
                    content.VerticalContentAlignment = VerticalAlignment.Center;
                }
                else if (child is TextBlock { Name: "PlaceholderTextContentPresenter" } placeholder)
                    placeholder.VerticalAlignment = VerticalAlignment.Center;
                Align(child);
            }
        }
        Align(input);
    }

    private void StartMenu_Toggled(object sender, RoutedEventArgs args)
    {
        if (_initializing) return;
        StartMenuInput.IsEnabled = ToggleStartMenu.IsOn;
        Directory_Changed(sender, null!);
    }

    private async void Browse_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(_host.AppWindow!.Id);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) DirectoryInput.Text = folder.Path;
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void Back_Click(object sender, RoutedEventArgs args) => Navigate(_step - 1);

    private async void Next_Click(object sender, RoutedEventArgs args)
    {
        if (_busy) return;
        if (_step == 4)
        {
            if (!_session.IsUninstall && CheckLaunch.IsChecked == true)
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(GetChoices().Directory, "BASpark.exe")) { UseShellExecute = true }); }
                catch (Exception exception) { ShowError(exception); return; }
            }
            await CloseAsync();
        }
        else if (_step is 2 or 5 || _failed) await RunAsync();
        else Navigate(_step + 1);
    }

    private async Task RunAsync()
    {
        InstallerChoices choices = GetChoices();
        string? invalid = _session.Validate(choices);
        if (invalid != null) { ShowError(new InvalidOperationException(Text(invalid))); return; }
        _busy = true;
        _failed = false;
        _output.Clear();
        OutputText.Text = "";
        InstallProgress.Value = 0;
        InstallProgress.IsIndeterminate = true;
        ProgressTitle.Text = Text(_session.IsUninstall ? "Installer_Uninstalling" : "Installer_Installing");
        ProgressDescription.Text = Text("Installer_ProgressDescription");
        Navigate(3);
        var progress = new Progress<InstallerProgress>(update => DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed) return;
            if (update.Percentage is double percentage)
            {
                InstallProgress.IsIndeterminate = false;
                InstallProgress.Value = percentage;
            }
            if (update.Message is string message)
            {
                foreach (string line in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    _output.Enqueue(line.TrimEnd('\r'));
                while (_output.Count > 800) _output.Dequeue();
                OutputText.Text = string.Join(Environment.NewLine, _output);
                OutputScroller.ChangeView(null, OutputScroller.ScrollableHeight, null, disableAnimation: true);
            }
        }));
        try
        {
            await _backend.RunAsync(choices, Localization.CurrentCultureName, progress);
            _busy = false;
            CompleteTitle.Text = Text(_session.IsUninstall ? "Installer_UninstallComplete" : "Installer_InstallComplete");
            CompleteInfo.Message = Text(_session.IsUninstall ? "Installer_UninstallSuccess" : "Installer_InstallSuccess");
            CompleteDirectory.Text = choices.Directory;
            CompleteDirectoryCard.Visibility = _session.IsUninstall ? Visibility.Collapsed : Visibility.Visible;
            Navigate(4);
        }
        catch (Exception exception)
        {
            _busy = false;
            _failed = true;
            InstallProgress.IsIndeterminate = false;
            ProgressTitle.Text = Text("Installer_OperationFailed");
            ProgressDescription.Text = Text("Installer_RetryDescription");
            ShowError(exception);
            UpdateActions();
        }
    }

    private void ShowError(Exception exception) => SetErrorMessage(exception.Message);

    private void SetErrorMessage(string? message, bool animate = true)
    {
        bool visible = !string.IsNullOrEmpty(message);
        bool changed = _errorVisible != visible;
        _errorVisible = visible;
        if (visible) ErrorInfo.Message = message!;
        if (!animate || !RootGrid.IsLoaded || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            CompleteErrorTransition();
            return;
        }
        if (!changed) return;
        double opacity = ErrorInfo.Visibility == Visibility.Collapsed ? 0 : ErrorInfo.Opacity;
        _errorStoryboard?.Stop();
        ErrorInfo.IsOpen = true;
        ErrorInfo.Visibility = Visibility.Visible;
        ErrorInfo.Opacity = 1;
        WelcomeInfo_Loaded(ErrorInfo, new RoutedEventArgs());
        var storyboard = new Storyboard();
        var duration = TimeSpan.Parse((string)Application.Current.Resources["ControlFastAnimationDuration"], System.Globalization.CultureInfo.InvariantCulture);
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = opacity });
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(duration), Value = visible ? 1 : 0 });
        Storyboard.SetTarget(fade, ErrorInfo);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        _errorStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(_errorStoryboard, storyboard)) CompleteErrorTransition();
        };
        storyboard.Begin();
    }

    private void CompleteErrorTransition()
    {
        _errorStoryboard?.Stop();
        _errorStoryboard = null;
        ErrorInfo.IsOpen = _errorVisible;
        ErrorInfo.Visibility = _errorVisible ? Visibility.Visible : Visibility.Collapsed;
        ErrorInfo.Opacity = 1;
        if (_errorVisible) WelcomeInfo_Loaded(ErrorInfo, new RoutedEventArgs());
    }

    private void Cancel_Click(object sender, RoutedEventArgs args) { if (!_busy) _ = CloseAsync(); }

    private async Task CloseAsync()
    {
        if (_closed) return;
        _closed = true;
        _errorStoryboard?.Stop();
        _errorStoryboard = null;
        await StartupDialogHost.CloseAsync(_host);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void WelcomeInfo_Loaded(object sender, RoutedEventArgs args) =>
        StartupDialogHost.AlignInfoBarContent((InfoBar)sender);

    private void Choice_Loaded(object sender, RoutedEventArgs args) =>
        StartupDialogHost.AlignChoiceContent((CheckBox)sender);
}
