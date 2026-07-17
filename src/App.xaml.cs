using System;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Interop;
using System.Diagnostics;
using System.Security.Principal;

namespace BASpark
{
    public partial class App : System.Windows.Application
    {
        private const string SingleInstanceMutexName = @"Local\BASpark_SingleInstance_Mutex";
        private const string RestartHandoffArgument = "--baspark-restart-handoff";
        private const string ShowControlPanelArgument = "--show-control-panel";
        private const string RepairAutoStartArgument = "--repair-autostart";
        private static readonly TimeSpan RestartHandoffTimeout = TimeSpan.FromSeconds(10);

        public static OverlayManager? Overlay { get; private set; }
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private ControlPanelWindow? _controlPanel;

        private static Mutex? _mutex;
        private static bool _ownsMutex;
        private int _isExiting = 0;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool waitForRestartHandoff = HasArgument(e.Args, RestartHandoffArgument);
            _mutex = new Mutex(false, SingleInstanceMutexName);
            try
            {
                _ownsMutex = _mutex.WaitOne(
                    waitForRestartHandoff ? RestartHandoffTimeout : TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }

            if (!_ownsMutex)
            {
                _mutex.Dispose();
                _mutex = null;
                ConfigManager.Load();
                if (!string.IsNullOrWhiteSpace(ConfigManager.UiLanguage))
                {
                    Localization.ApplyCulture(ConfigManager.UiLanguage);
                }

                System.Windows.MessageBox.Show(
                    Localization.Get("App_AlreadyRunning"),
                    Localization.Get("App_AlreadyRunning_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                System.Windows.Application.Current.Shutdown();
                return;
            }

            ConfigManager.Load();
            AppLogger.Initialize();

            if (HasArgument(e.Args, RepairAutoStartArgument))
            {
                SynchronizeScheduledAutoStart(removeWhenDisabled: true);
                ExitApplication();
                return;
            }

            if (string.IsNullOrWhiteSpace(ConfigManager.UiLanguage))
            {
                if (!ConfigManager.AgreedToPrivacy)
                {
                    var languageWin = new LanguageSelectWindow();
                    bool? langResult = languageWin.ShowDialog();
                    if (langResult != true)
                    {
                        ExitApplication();
                        return;
                    }
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

            if (ConfigManager.RunAsAdmin && !IsRunningAsAdmin())
            {
                try
                {
                    RestartWithAdminPrivileges(e.Args);
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Restart with admin privileges failed.", ex);
                    Debug.WriteLine("自动请求管理员权限被拒绝或失败: " + ex.Message);
                }
            }

            SynchronizeScheduledAutoStart(removeWhenDisabled: false);

            SystemEvents.SessionEnding += OnSessionEnding;

            base.OnStartup(e);

            if (!ConfigManager.AgreedToPrivacy)
            {
                var privacyWin = new PrivacyWindow();
                UiLocalizer.ApplyPrivacy(privacyWin);
                bool? result = privacyWin.ShowDialog();
                if (result == true)
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

            InitTrayIcon();

            Overlay = new OverlayManager();
            Overlay.Start();

            if (!ConfigManager.StartSilent || HasArgument(e.Args, ShowControlPanelArgument))
            {
                ShowControlPanel();
            }
        }

        private bool IsRunningAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private void SynchronizeScheduledAutoStart(bool removeWhenDisabled)
        {
            bool scheduledTaskEnabled = ConfigManager.AutoStart && ConfigManager.RunAsAdmin;
            if ((!scheduledTaskEnabled || !IsRunningAsAdmin()) && !removeWhenDisabled)
            {
                return;
            }

            string? exePath = AutoStartManager.ResolveExecutablePath(
                Environment.ProcessPath,
                Process.GetCurrentProcess().MainModule?.FileName,
                typeof(App).Assembly.Location,
                AppContext.BaseDirectory);
            if (string.IsNullOrEmpty(exePath) ||
                (scheduledTaskEnabled && AutoStartManager.IsScheduledTaskCurrent(exePath)))
            {
                return;
            }

            if (!AutoStartManager.TrySetScheduledTask(exePath, scheduledTaskEnabled, out string error))
            {
                AppLogger.Warn($"Failed to synchronize the auto-start task: {error}");
            }
        }

        private void RestartWithAdminPrivileges(string[] args)
        {
            string exePath = System.Environment.ProcessPath ??
                             System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            foreach (string argument in args)
            {
                if (!string.Equals(argument, RestartHandoffArgument, StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }
            startInfo.ArgumentList.Add(RestartHandoffArgument);

            try
            {
                if (Process.Start(startInfo) == null)
                {
                    throw new InvalidOperationException("Failed to start the elevated process.");
                }
                ReleaseSingleInstanceMutex();
                System.Windows.Application.Current.Shutdown();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new Exception("用户拒绝了管理员授权。");
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            ExitApplication();
        }

        private void InitTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();

            try
            {
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (streamInfo != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                }
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.Visible = true;
            RefreshTrayLocalization();
            _notifyIcon.DoubleClick += (s, e) => ShowControlPanel();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add(Localization.Get("Tray_OpenPanel"), null, (s, e) => ShowControlPanel());
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(Localization.Get("Tray_Restart"), null, (s, e) => RestartApplication());
            contextMenu.Items.Add(Localization.Get("Tray_Exit"), null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void RefreshTrayLocalization()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = Localization.Get("Tray_Text");
            }
        }

        public void ShowControlPanel()
        {
            this.Dispatcher.Invoke(() =>
            {
                if (_controlPanel == null || !_controlPanel.IsLoaded)
                {
                    _controlPanel = new ControlPanelWindow();
                    _controlPanel.Show();
                }
                else
                {
                    if (_controlPanel.WindowState == WindowState.Minimized)
                    {
                        _controlPanel.WindowState = WindowState.Normal;
                    }
                    _controlPanel.Activate();
                }
            });
        }

        public void RestartApplicationFromPanel()
        {
            RestartApplication();
        }

        private void RestartApplication()
        {
            try
            {
                string exePath = System.Environment.ProcessPath ??
                                 System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

                ProcessStartInfo startInfo = new ProcessStartInfo(exePath) { UseShellExecute = true };
                if (ConfigManager.RunAsAdmin)
                {
                    startInfo.Verb = "runas";
                }
                startInfo.ArgumentList.Add(RestartHandoffArgument);

                if (System.Diagnostics.Process.Start(startInfo) == null)
                {
                    throw new InvalidOperationException("Failed to start the replacement process.");
                }
                ExitApplication();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    Localization.Format("Tray_RestartFailed", ex.Message));
            }
        }

        private void ExitApplication()
        {
            if (Interlocked.Exchange(ref _isExiting, 1) == 1) return;

            SystemEvents.SessionEnding -= OnSessionEnding;

            ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.ContextMenuStrip?.Dispose();
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            this.Dispatcher.Invoke(() =>
            {
                try { _controlPanel?.Close(); } catch { /* ignore: best-effort cleanup during shutdown */ }
                try { Overlay?.Dispose(); } catch { /* ignore: best-effort cleanup during shutdown */ }
            });

            ReleaseSingleInstanceMutex();
            System.Windows.Application.Current.Shutdown();
        }

        private static bool HasArgument(string[] args, string expectedArgument)
        {
            foreach (string argument in args)
            {
                if (string.Equals(argument, expectedArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReleaseSingleInstanceMutex()
        {
            if (_mutex == null)
            {
                return;
            }

            if (_ownsMutex)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException ex)
                {
                    AppLogger.Warn($"Failed to release the single-instance mutex: {ex.Message}");
                }
                _ownsMutex = false;
            }

            _mutex.Dispose();
            _mutex = null;
        }

    }
}
