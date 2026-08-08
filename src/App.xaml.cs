using System;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Interop;
using System.Diagnostics;
using System.Security.Principal;
using System.Linq;

namespace BASpark
{
    public partial class App : System.Windows.Application
    {
        public static OverlayManager? Overlay { get; private set; }
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private ControlPanelWindow? _controlPanel;

        private static readonly TrayMenuPalette LightTrayMenuPalette = new(
            background: System.Drawing.Color.FromArgb(255, 255, 255),
            foreground: System.Drawing.Color.FromArgb(51, 51, 51),
            disabledForeground: System.Drawing.Color.FromArgb(153, 153, 153),
            selectionBackground: System.Drawing.Color.FromArgb(224, 242, 255),
            selectionBorder: System.Drawing.Color.FromArgb(144, 202, 249),
            border: System.Drawing.Color.FromArgb(214, 222, 232),
            separator: System.Drawing.Color.FromArgb(234, 236, 239));

        private static readonly TrayMenuPalette DarkTrayMenuPalette = new(
            background: System.Drawing.Color.FromArgb(32, 39, 49),
            foreground: System.Drawing.Color.FromArgb(231, 237, 244),
            disabledForeground: System.Drawing.Color.FromArgb(135, 147, 160),
            selectionBackground: System.Drawing.Color.FromArgb(38, 56, 71),
            selectionBorder: System.Drawing.Color.FromArgb(53, 85, 108),
            border: System.Drawing.Color.FromArgb(58, 70, 84),
            separator: System.Drawing.Color.FromArgb(58, 70, 84));

        private static Mutex? _mutex;
        private int _isExiting = 0;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = @"Global\BASpark_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
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

            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

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

            if (!ConfigManager.StartSilent)
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

        private void RestartWithAdminPrivileges(string[] args)
        {
            string exePath = System.Environment.ProcessPath ??
                             System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(a => $"\"{a}\""))
            };

            try
            {
                Process.Start(startInfo);
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
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

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(RefreshTrayTheme));
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
            contextMenu.Opening += (s, e) => RefreshTrayTheme();
            _notifyIcon.ContextMenuStrip = contextMenu;
            RefreshTrayTheme();
        }

        private void RefreshTrayLocalization()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = Localization.Get("Tray_Text");
            }
        }

        public void RefreshTrayTheme()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshTrayTheme));
                return;
            }

            System.Windows.Forms.ContextMenuStrip? contextMenu = _notifyIcon?.ContextMenuStrip;
            if (contextMenu == null || contextMenu.IsDisposed)
            {
                return;
            }

            TrayMenuPalette palette = ThemeManager.IsDarkModeEnabled()
                ? DarkTrayMenuPalette
                : LightTrayMenuPalette;

            contextMenu.SuspendLayout();
            contextMenu.BackColor = palette.Background;
            contextMenu.ForeColor = palette.Foreground;
            contextMenu.Renderer = new TrayMenuRenderer(palette);
            ApplyTrayItemColors(contextMenu.Items, palette);
            contextMenu.ResumeLayout(performLayout: true);
            contextMenu.Invalidate();
        }

        private static void ApplyTrayItemColors(
            System.Windows.Forms.ToolStripItemCollection items,
            TrayMenuPalette palette)
        {
            foreach (System.Windows.Forms.ToolStripItem item in items)
            {
                item.BackColor = palette.Background;
                item.ForeColor = item.Enabled ? palette.Foreground : palette.DisabledForeground;

                if (item is System.Windows.Forms.ToolStripDropDownItem dropDownItem && dropDownItem.HasDropDownItems)
                {
                    dropDownItem.DropDown.BackColor = palette.Background;
                    dropDownItem.DropDown.ForeColor = palette.Foreground;
                    dropDownItem.DropDown.Renderer = new TrayMenuRenderer(palette);
                    ApplyTrayItemColors(dropDownItem.DropDownItems, palette);
                }
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

                System.Diagnostics.Process.Start(startInfo);
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
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

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

            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { /* ignore: mutex disposal during shutdown */ }
                _mutex.Dispose();
                _mutex = null;
            }
            System.Windows.Application.Current.Shutdown();
        }

        private readonly struct TrayMenuPalette
        {
            public TrayMenuPalette(
                System.Drawing.Color background,
                System.Drawing.Color foreground,
                System.Drawing.Color disabledForeground,
                System.Drawing.Color selectionBackground,
                System.Drawing.Color selectionBorder,
                System.Drawing.Color border,
                System.Drawing.Color separator)
            {
                Background = background;
                Foreground = foreground;
                DisabledForeground = disabledForeground;
                SelectionBackground = selectionBackground;
                SelectionBorder = selectionBorder;
                Border = border;
                Separator = separator;
            }

            public System.Drawing.Color Background { get; }
            public System.Drawing.Color Foreground { get; }
            public System.Drawing.Color DisabledForeground { get; }
            public System.Drawing.Color SelectionBackground { get; }
            public System.Drawing.Color SelectionBorder { get; }
            public System.Drawing.Color Border { get; }
            public System.Drawing.Color Separator { get; }
        }

        private sealed class TrayMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
        {
            private readonly TrayMenuPalette _palette;

            public TrayMenuRenderer(TrayMenuPalette palette)
            {
                _palette = palette;
                RoundedEdges = false;
            }

            protected override void OnRenderToolStripBackground(
                System.Windows.Forms.ToolStripRenderEventArgs e)
            {
                using var brush = new System.Drawing.SolidBrush(_palette.Background);
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(
                System.Windows.Forms.ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip.Width <= 0 || e.ToolStrip.Height <= 0)
                {
                    return;
                }

                using var pen = new System.Drawing.Pen(_palette.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderImageMargin(
                System.Windows.Forms.ToolStripRenderEventArgs e)
            {
                using var brush = new System.Drawing.SolidBrush(_palette.Background);
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderMenuItemBackground(
                System.Windows.Forms.ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed)
                {
                    return;
                }

                var bounds = new System.Drawing.Rectangle(
                    1,
                    0,
                    Math.Max(0, e.Item.Width - 3),
                    Math.Max(0, e.Item.Height - 1));
                using var brush = new System.Drawing.SolidBrush(_palette.SelectionBackground);
                using var pen = new System.Drawing.Pen(_palette.SelectionBorder);
                e.Graphics.FillRectangle(brush, bounds);
                e.Graphics.DrawRectangle(pen, bounds);
            }

            protected override void OnRenderItemText(
                System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? _palette.Foreground : _palette.DisabledForeground;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(
                System.Windows.Forms.ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item?.Enabled != false
                    ? _palette.Foreground
                    : _palette.DisabledForeground;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderSeparator(
                System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
            {
                using var pen = new System.Drawing.Pen(_palette.Separator);
                if (e.Vertical)
                {
                    int x = e.Item.Width / 2;
                    e.Graphics.DrawLine(pen, x, 3, x, Math.Max(3, e.Item.Height - 4));
                    return;
                }

                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 4, y, Math.Max(4, e.Item.Width - 5), y);
            }
        }

    }
}
