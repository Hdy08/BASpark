using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace BASpark;

/// <summary>
/// 应用入口。迁移到 WinUI 3 后进程模型保持单进程：
///   * 控制面板 / 取色器 / 对话框 = 真正的 WinUI 3 窗口
///   * 特效叠加层 = 自建 Win32 分层窗口 + WebView2（见 <see cref="LayeredWindowHost"/>）
///   * 托盘图标 = 独立 STA 线程上的 WinForms NotifyIcon
///     （WinUI 3 没有托盘 API；菜单交回系统原生渲染，不再自绘配色）
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\BASpark_SingleInstance_Mutex";

    private static Mutex? _mutex;
    private int _isExiting;

    /// <summary>UI 线程调度器；叠加层与设置层都通过它回到 UI 线程。</summary>
    public static DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static OverlayManager? Overlay { get; private set; }

    public static TrayIconController? Tray { get; private set; }

    private ControlPanelWindow? _controlPanel;
    private readonly KeeperWindow _keeper = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();

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

        TelemetryHelper.SendStartupData();

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
    }

    /// <summary>按配置解析当前应使用的 WinUI 元素主题。</summary>
    public static ElementTheme ResolveElementTheme() =>
        ConfigManager.DarkMode switch
        {
            DarkModeOption.On => ElementTheme.Dark,
            DarkModeOption.Off => ElementTheme.Light,
            _ => ElementTheme.Default
        };

    // ------------------------------------------------------------------
    // 控制面板
    // ------------------------------------------------------------------

    public void ShowControlPanel()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_controlPanel == null)
            {
                _controlPanel = new ControlPanelWindow();
                _controlPanel.Closed += (_, _) => _controlPanel = null;
                WindowChrome.ApplyAppIcon(_controlPanel);
                _controlPanel.Activate();

                // 标题栏主题需要在窗口建立后才可设置（Win10 走 DWM 回退路径）。
                WindowChrome.ApplyTitleBarTheme(
                    _controlPanel,
                    IsEffectiveDarkMode());
            }
            else
            {
                _controlPanel.Activate();
            }
        });
    }

    /// <summary>按配置与系统设置解析当前是否处于深色模式。</summary>
    public static bool IsEffectiveDarkMode() =>
        ConfigManager.DarkMode switch
        {
            DarkModeOption.On => true,
            DarkModeOption.Off => false,
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
        catch (Exception ex)
        {
            AppLogger.Debug($"Failed to read the Windows app theme: {ex.Message}");
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

    private void RestartApplication()
    {
        try
        {
            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule!.FileName;

            var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = true };
            if (ConfigManager.RunAsAdmin)
            {
                startInfo.Verb = "runas";
            }

            Process.Start(startInfo);
            ExitApplication();
        }
        catch (Exception ex)
        {
            NativeMessageBox.Show(
                Localization.Format("Tray_RestartFailed", ex.Message),
                Localization.Get("Msg_Error"));
        }
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
        if (Interlocked.Exchange(ref _isExiting, 1) == 1)
        {
            return;
        }

        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);

        Tray?.Dispose();
        Tray = null;

        try
        {
            _controlPanel?.Close();
        }
        catch
        {
            // 关闭阶段尽力清理即可。
        }

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
