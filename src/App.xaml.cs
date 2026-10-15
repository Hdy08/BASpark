using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace BASpark;

/// <summary>
/// 应用入口。迁移到 WinUI 3 后进程模型保持单进程：
///   * 控制面板 = Win32 直合成宿主 + XAML 岛；取色器 / 对话框 = WinUI 3 窗口
///   * 特效叠加层 = 自建 Win32 分层窗口 + WebView2（见 <see cref="LayeredWindowHost"/>）
///   * 托盘图标 = 独立 STA 线程上的 WinForms NotifyIcon
///     （WinUI 3 没有托盘 API；菜单交回系统原生渲染，不再自绘配色）
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\BASpark_SingleInstance_Mutex";

    private static Mutex? _mutex;
    private int _isExiting;
    private bool _isRestarting;

    /// <summary>UI 线程调度器；叠加层与设置层都通过它回到 UI 线程。</summary>
    public static DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static OverlayManager? Overlay { get; private set; }

    public static TrayIconController? Tray { get; private set; }
    private static bool _effectsPaused;
    public static bool IsEffectsPaused => Volatile.Read(ref _effectsPaused);
    public static event EventHandler? EffectsPauseChanged;

    public static void ToggleEffectsPaused()
    {
        Volatile.Write(ref _effectsPaused, !IsEffectsPaused);
        Overlay?.RefreshPauseState();
        EffectsPauseChanged?.Invoke(null, EventArgs.Empty);
    }

    private ControlPanelWindow? _controlPanel;
    private InstallerWindow? _installerWindow;
    private int _backgroundMemoryGeneration;
    private readonly KeeperWindow _keeper = new();
    private Windows.UI.ViewManagement.UISettings? _accentSettings;
    private static readonly string[] AccentResourceKeys = ["SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2",
        "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"];

    public App()
    {
        InitializeComponent();
    }

    internal static void ApplyAccentColor(FrameworkElement? root = null)
    {
        if (Current is not App app) return;
        if (app._accentSettings == null)
        {
            app._accentSettings = new Windows.UI.ViewManagement.UISettings();
            app._accentSettings.ColorValuesChanged += app.AccentSettings_ColorValuesChanged;
        }
        Windows.UI.Color[] colors;
        if (ConfigManager.UseSystemAccentColor)
        {
            colors = new[] { Windows.UI.ViewManagement.UIColorType.Accent, Windows.UI.ViewManagement.UIColorType.AccentLight1,
                Windows.UI.ViewManagement.UIColorType.AccentLight2, Windows.UI.ViewManagement.UIColorType.AccentLight3,
                Windows.UI.ViewManagement.UIColorType.AccentDark1, Windows.UI.ViewManagement.UIColorType.AccentDark2,
                Windows.UI.ViewManagement.UIColorType.AccentDark3 }.Select(app._accentSettings.GetColorValue).ToArray();
        }
        else
        {
            if (!ColorPickerColorMath.TryParseHex(ConfigManager.CustomAccentColor, out var color))
                ColorPickerColorMath.TryParseHex(ConfigManager.DefaultAccentColor, out color);
            colors = ColorPickerColorMath.CreateAccentPalette(color);
        }
        bool changed = false;
        for (int index = 0; index < AccentResourceKeys.Length; index++)
            if (!Equals(app.Resources[AccentResourceKeys[index]], colors[index]))
            {
                app.Resources[AccentResourceKeys[index]] = colors[index];
                changed = true;
            }
        if (!changed) return;
        if (root != null)
        {
            ElementTheme requested = root.RequestedTheme;
            foreach (ElementTheme theme in new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Default, requested })
            {
                root.RequestedTheme = theme;
                root.UpdateLayout();
            }
        }
    }

    private void AccentSettings_ColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Volatile.Read(ref _isExiting) != 0) return;
            if (_controlPanel != null) _controlPanel.RefreshAccentColor();
            else ApplyAccentColor();
        });
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();
        ApplyAccentColor();

        if (Environment.GetCommandLineArgs().Contains("--repair-autostart", StringComparer.Ordinal))
        {
            _ = RepairAutoStartAsync(exitWhenComplete: true);
            return;
        }

        if (InstallerSession.TryCreate(Environment.GetCommandLineArgs()[1..], out InstallerSession? installer))
        {
            _keeper.EnsureCreated();
            _installerWindow = new InstallerWindow(installer!);
            _installerWindow.Closed += (_, _) => Exit();
            _installerWindow.Show();
            return;
        }

        if (TrayIconController.TryShowExistingControlPanel())
        {
            Exit();
            return;
        }

        // 单实例：第二个进程直接退出并提示。
        _mutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            ConfigManager.Load();
            if (!string.IsNullOrWhiteSpace(ConfigManager.UiLanguage))
            {
                Localization.ApplyCulture(ConfigManager.UiLanguage);
            }

            NativeMessageBox.Show(
                Localization.Get("App_AlreadyRunning"),
                Localization.Get("App_AlreadyRunning_Title"));
            Exit();
            return;
        }

        // 启动流程包含可等待的对话框，必须在 UI 线程上以 async 方式展开。
        // 不能用 GetAwaiter().GetResult()：那会在对话框显示前阻塞 UI 线程。
        _ = BootstrapAsync();
    }

    private async Task BootstrapAsync()
    {
        // WinUI 3 在最后一个窗口关闭时会退出进程；BASpark 要常驻托盘，
        // 启动阶段也可能没有任何可见窗口，因此先用一个隐藏窗口保住消息循环。
        _keeper.EnsureCreated();

        ConfigManager.Load();
        AppLogger.Initialize();
        ApplyAccentColor();

        if (string.IsNullOrWhiteSpace(ConfigManager.UiLanguage))
        {
            if (!ConfigManager.AgreedToPrivacy)
            {
                string? culture = await StartupDialogHost.AskLanguageAsync();
                if (culture == null)
                {
                    ExitApplication();
                    return;
                }

                Localization.ApplyCulture(culture);
                ConfigManager.Save("UiLanguage", culture);
            }
            else
            {
                string detected = Localization.DetectCultureFromSystem();
                Localization.ApplyCulture(detected);
                ConfigManager.Save("UiLanguage", detected);
            }
        }
        else
        {
            Localization.ApplyCulture(ConfigManager.UiLanguage);
        }

        if (ConfigManager.RunAsAdmin && !IsRunningAsAdmin() && TryRestartWithAdminPrivileges())
        {
            return;
        }

        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (!ConfigManager.AgreedToPrivacy)
        {
            if (await StartupDialogHost.AskPrivacyAsync())
            {
                ConfigManager.Save("AgreedToPrivacy", true);
            }
            else
            {
                ExitApplication();
                return;
            }
        }

        _ = RepairAutoStartAsync();

        Tray = new TrayIconController();
        Tray.Initialize(
            openPanel: ShowControlPanel,
            restart: RestartApplication,
            exit: ExitApplication);

        Overlay = new OverlayManager();
        Overlay.Start();

        if (!ConfigManager.StartSilent)
        {
            ShowControlPanel();
        }
        else
        {
            ReclaimBackgroundMemory();
        }
    }

    private async Task RepairAutoStartAsync(bool exitWhenComplete = false)
    {
        try
        {
            if (exitWhenComplete) ConfigManager.Load();
            bool autoStart = ConfigManager.AutoStart;
            bool runAsAdmin = ConfigManager.RunAsAdmin;
            string executable = Path.Combine(AppContext.BaseDirectory, "BASpark.exe");
            if (autoStart || exitWhenComplete)
                await Task.Run(() => AutoStartManager.ApplySettings(autoStart, runAsAdmin, allowElevation: false, executablePath: executable));
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to repair auto-start settings: {exception.Message}");
            if (exitWhenComplete) Environment.ExitCode = 1;
        }
        finally
        {
            if (exitWhenComplete) Exit();
        }
    }

    /// <summary>按配置解析当前应使用的 WinUI 元素主题。</summary>
    public static ElementTheme ResolveElementTheme() =>
        ConfigManager.BackgroundColor switch
        {
            BackgroundColorOption.Dark => ElementTheme.Dark,
            BackgroundColorOption.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };

    // ------------------------------------------------------------------
    // 控制面板
    // ------------------------------------------------------------------

    public void ShowControlPanel()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Volatile.Read(ref _isExiting) != 0 || _isRestarting) return;
            Interlocked.Increment(ref _backgroundMemoryGeneration);
            if (_controlPanel == null)
            {
                try
                {
                    _controlPanel = new ControlPanelWindow();
                    _controlPanel.Closed += ControlPanel_Closed;
                    WindowChrome.ApplyAppIcon(_controlPanel.Handle);
                    _controlPanel.Activate();

                    // 标题栏主题需要在窗口建立后才可设置（Win10 走 DWM 回退路径）。
                    WindowChrome.ApplyTitleBarTheme(
                        _controlPanel.Handle,
                        IsEffectiveDarkMode());
                }
                catch (Exception ex)
                {
                    // 控制面板构造/显示失败时不能让异常逃逸（会静默吞掉且界面缺失）。
                    _controlPanel = null;
                    NativeMessageBox.Show(
                        Localization.Format("WebView2_InitFailed", ex.Message),
                        Localization.Get("Msg_Error"),
                        isError: true);
                }
            }
            else
            {
                _controlPanel.Activate();
            }
        });
    }

    private void ControlPanel_Closed(object? sender, EventArgs args)
    {
        if (sender is ControlPanelWindow panel) panel.Closed -= ControlPanel_Closed;
        if (!ReferenceEquals(sender, _controlPanel)) return;
        _controlPanel = null;
        ReclaimBackgroundMemory();
    }

    private void ReclaimBackgroundMemory()
    {
        if (_controlPanel != null || Volatile.Read(ref _isExiting) != 0) return;
        _ = ReclaimBackgroundMemoryAsync(Interlocked.Increment(ref _backgroundMemoryGeneration));
    }

    private async Task ReclaimBackgroundMemoryAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        if (generation != Volatile.Read(ref _backgroundMemoryGeneration) || Volatile.Read(ref _isExiting) != 0) return;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _backgroundMemoryGeneration || _controlPanel != null || _isExiting != 0) return;
            SidebarBackgroundHelper.ClearCache();
            _ = NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
        });
    }

    /// <summary>按配置与系统设置解析当前是否处于深色模式。</summary>
    public static bool IsEffectiveDarkMode() =>
        ConfigManager.BackgroundColor switch
        {
            BackgroundColorOption.Dark => true,
            BackgroundColorOption.Light => false,
            _ => IsSystemAppDarkMode()
        };

    public static bool IsSystemAppDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void RestartApplicationFromPanel() => RestartApplication();

    public static void ReportFatalWebViewFailure(string message) =>
        NativeMessageBox.Show(message, Localization.Get("Msg_Error"));

    // ------------------------------------------------------------------
    // 权限与重启
    // ------------------------------------------------------------------

    private static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private bool TryRestartWithAdminPrivileges()
    {
        try
        {
            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule!.FileName;

            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            });

            ReleaseMutexAndExit();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户拒绝了 UAC，继续以普通权限运行。
            return false;
        }
    }

    private async void RestartApplication()
    {
        if (_isRestarting || Volatile.Read(ref _isExiting) != 0) return;
        _isRestarting = true;
        try
        {
            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule!.FileName;

            var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = true };
            if (ConfigManager.RunAsAdmin)
            {
                startInfo.Verb = "runas";
            }

            if (_controlPanel != null) await _controlPanel.CloseAsync();
            if (Volatile.Read(ref _isExiting) != 0) return;
            Process.Start(startInfo);
            await ExitApplicationAsync();
        }
        catch (Exception ex)
        {
            NativeMessageBox.Show(
                Localization.Format("Tray_RestartFailed", ex.Message),
                Localization.Get("Msg_Error"));
        }
        finally { _isRestarting = false; }
    }

    // ------------------------------------------------------------------
    // 系统事件
    // ------------------------------------------------------------------

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => ExitApplication();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        Tray?.RefreshLocalization();
    }

    // ------------------------------------------------------------------
    // 退出
    // ------------------------------------------------------------------

    public void ExitApplication()
    {
        if (DispatcherQueue.HasThreadAccess) _ = ExitApplicationAsync();
        else DispatcherQueue.TryEnqueue(() => _ = ExitApplicationAsync());
    }

    private async Task ExitApplicationAsync()
    {
        if (Interlocked.Exchange(ref _isExiting, 1) == 1)
        {
            return;
        }

        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (_accentSettings != null) _accentSettings.ColorValuesChanged -= AccentSettings_ColorValuesChanged;

        try
        {
            if (_controlPanel != null) await _controlPanel.CloseAsync();
        }
        catch
        {
            // 关闭阶段尽力清理即可。
        }

        ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);

        Tray?.Dispose();
        Tray = null;

        try
        {
            Overlay?.Dispose();
        }
        catch
        {
            // 关闭阶段尽力清理即可。
        }

        _keeper.Destroy();

        ReleaseMutexAndExit();
    }

    private void ReleaseMutexAndExit()
    {
        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // 未持有互斥量时忽略。
            }

            _mutex.Dispose();
            _mutex = null;
        }

        Exit();
    }
}
