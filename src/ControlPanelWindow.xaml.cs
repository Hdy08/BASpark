using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Security.Principal;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;

namespace BASpark
{
    public class ProcessItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public class VisualResetItem
    {
        public VisualAppearanceResetFlags Flags { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public bool IsSelected { get; set; }

        public VisualResetItem(VisualAppearanceResetFlags flags, string title, string subtitle)
        {
            Flags = flags;
            Title = title;
            Subtitle = subtitle;
        }
    }

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

    public partial class ControlPanelWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int MaxRemoteJsonBytes = 256 * 1024;
        private static readonly HttpClient RemoteContentClient = CreateRemoteContentClient();
        private static readonly SolidColorBrush FilteredStatusBrush = CreateFrozenBrush(0xD9, 0x77, 0x06);

        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _noticeTimer;
        private readonly DispatcherTimer _scrollbarHideTimer;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private bool _isCheckingUpdate = false;
        private bool _suspendLinkedAnimationUiHandlers;
        private string _languageAtLoad = Localization.CultureZhCn;
        private NetworkRegionOption _networkRegionAtLoad = NetworkRegionOption.Auto;
        private bool _autoNetworkFailurePromptShown;
        private bool _logViewInitialized;
        private int _lastRenderedStatus = -1;
        private int _lastRenderedClickCount = -1;
        private int _themeRefreshPending;
        private readonly object _networkPromptLock = new();

        public ObservableCollection<FilterProfile> Profiles { get; set; } = new ObservableCollection<FilterProfile>();
        public ObservableCollection<string> CurrentProfileProcesses { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<ProcessItem> RunningProcessList { get; set; } = new ObservableCollection<ProcessItem>();
        public ObservableCollection<VisualResetItem> VisualResetItems { get; set; } = new ObservableCollection<VisualResetItem>();
        public ObservableCollection<ScreenOptionItem> ScreenOptions { get; set; } = new ObservableCollection<ScreenOptionItem>();

        public ControlPanelWindow()
        {
            InitializeComponent();
            _scrollbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _scrollbarHideTimer.Tick += ScrollbarHideTimer_Tick;
            SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
            Activated += (_, _) => ThemeManager.ApplyTitleBar(this);

            _languageAtLoad = string.IsNullOrWhiteSpace(ConfigManager.UiLanguage)
                ? Localization.CurrentCultureName
                : ConfigManager.UiLanguage;
            _networkRegionAtLoad = ConfigManager.NetworkRegion;

            ComboProfiles.ItemsSource = Profiles;
            ListConfiguredProcesses.ItemsSource = CurrentProfileProcesses;
            ListRunningProcesses.ItemsSource = RunningProcessList;
            ListVisualResetItems.ItemsSource = VisualResetItems;
            ListScreenOptions.ItemsSource = ScreenOptions;
            LoadVersion();
            LoadSettings();
            ApplyScrollbarSettings();
            UiLocalizer.ApplyControlPanel(this);
            LoadScreenOptions();
            ApplyDarkMode();
            CheckAdminStatus();
            InitLogView();
            AppLogger.EntryAdded += OnAppLogEntryAdded;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            Closed += ControlPanelWindow_Closed;
            _ = ApplySidebarBackgroundAsync(ConfigManager.SidebarBackgroundImagePath);
            _ = LoadRemoteNoticeAsync(isManual: false);

            _ = CheckForUpdates(isManual: false);

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(500);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            _noticeTimer = new DispatcherTimer();
            _noticeTimer.Interval = TimeSpan.FromHours(3);
            _noticeTimer.Tick += (_, _) => _ = LoadRemoteNoticeAsync(isManual: false);
            _noticeTimer.Start();
        }

        private static HttpClient CreateRemoteContentClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5),
                MaxResponseContentBufferSize = MaxRemoteJsonBytes
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppVersionInfo.UserAgent);
            return client;
        }

        private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private void ControlPanelWindow_Closed(object? sender, EventArgs e)
        {
            _lifetimeCts.Cancel();
            _refreshTimer.Stop();
            _noticeTimer.Stop();
            _scrollbarHideTimer.Stop();
            AppLogger.EntryAdded -= OnAppLogEntryAdded;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _lifetimeCts.Dispose();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9.]+");
            bool isInvalid = regex.IsMatch(e.Text);

            if (!isInvalid && e.Text == ".")
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                if (textBox != null && textBox.Text.Contains("."))
                {
                    isInvalid = true;
                }
            }

            e.Handled = isInvalid;
        }

        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                if (textBox != null)
                {
                    System.Windows.Input.Keyboard.ClearFocus();
                    var binding = System.Windows.Data.BindingOperations.GetBindingExpression(textBox, System.Windows.Controls.TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                e.Handled = true;
            }
        }

        private void CheckAdminStatus()
        {
            if (CheckRunAsAdmin != null)
            {
                CheckRunAsAdmin.IsChecked = ConfigManager.RunAsAdmin;
            }
        }

        private async Task CheckForUpdates(bool isManual)
        {
            string updateUrl = Localization.GetRemoteUpdateUrl();
            try
            {
                string json = await RemoteContentClient.GetStringAsync(updateUrl, _lifetimeCts.Token);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string latestVersionStr = ReadJsonString(root, "version", 64);
                string downloadUrl = ReadJsonString(root, "url", 2048);
                string updateNotes = ReadJsonString(root, "notes", 8000);
                if (string.IsNullOrWhiteSpace(updateNotes))
                {
                    updateNotes = Localization.Get("Msg_NoUpdateNotes");
                }

                if (!AppVersionInfo.TryCompare(latestVersionStr, AppVersionInfo.DisplayVersion, out int versionCompare))
                {
                    throw new FormatException($"Invalid update version: {latestVersionStr}");
                }
                if (versionCompare > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var result = System.Windows.MessageBox.Show(
                            Localization.Format("Msg_UpdateAvailable", latestVersionStr, updateNotes),
                            Localization.Get("Msg_UpdateAvailable_Title"),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes && TryCreateHttpUri(downloadUrl, out Uri? uri))
                        {
                            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                        }
                    });
                }
                else if (isManual)
                {
                    Dispatcher.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show(
                            Localization.Get("Msg_UpToDate"),
                            Localization.Get("Msg_CheckUpdate_Title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Update check failed: {ex.Message}");
                HandleNetworkFetchFailure(isManual, ex.Message);
            }
        }

        private static string ReadJsonString(JsonElement root, string propertyName, int maxLength)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            string text = value.GetString() ?? string.Empty;
            return text.Length <= maxLength ? text : text[..maxLength];
        }

        private static bool TryCreateHttpUri(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
        {
            uri = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
            {
                return false;
            }

            if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdate) return;
            var btn = BtnCheckUpdate ?? sender as System.Windows.Controls.Button;
            try
            {
                _isCheckingUpdate = true;
                if (btn != null)
                {
                    btn.IsEnabled = false;
                    btn.Content = Localization.Get("About_CheckingUpdate");
                }
                await CheckForUpdates(isManual: true);
            }
            finally
            {
                _isCheckingUpdate = false;
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Content = Localization.Get("About_CheckUpdate");
                }
            }
        }

        private async Task LoadRemoteNoticeAsync(bool isManual)
        {
            string noticeUrl = Localization.GetRemoteNoticeUrl();
            try
            {
                string json = await RemoteContentClient.GetStringAsync(noticeUrl, _lifetimeCts.Token);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string title = ReadJsonString(root, "title", 200);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = Localization.Get("Msg_DefaultNoticeTitle");
                }
                string content = ReadJsonString(root, "content", 8000);
                string date = ReadJsonString(root, "date", 64);
                string lastContent = ConfigManager.LastNoticeContent;

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(content))
                    {
                        NoticeTitle.Text = title;
                        NoticeContent.Text = content;
                        NoticeDate.Text = date;
                        NoticeBar.Visibility = Visibility.Visible;
                        if (content != lastContent)
                        {
                            ShowWindowsNotification(title, content);
                            ConfigManager.Save("LastNoticeContent", content);
                        }
                    }
                });
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Notice fetch failed: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrEmpty(NoticeContent.Text) || NoticeContent.Text == "...")
                    {
                        NoticeBar.Visibility = Visibility.Collapsed;
                    }
                });
                HandleNetworkFetchFailure(isManual, ex.Message);
            }
        }

        private void HandleNetworkFetchFailure(bool isManual, string errorMessage)
        {
            if (_lifetimeCts.IsCancellationRequested || !IsLoaded)
            {
                return;
            }

            lock (_networkPromptLock)
            {
                if (!isManual && _autoNetworkFailurePromptShown)
                {
                    return;
                }

                if (!isManual)
                {
                    _autoNetworkFailurePromptShown = true;
                }
            }

            Dispatcher.Invoke(() => PromptSwitchNetworkSource(isManual, errorMessage));
        }

        private void PromptSwitchNetworkSource(bool isManual, string errorMessage)
        {
            NetworkRegionOption alternateRegion = GetAlternateNetworkRegion();
            string currentLabel = GetNetworkRegionLabel(ConfigManager.NetworkRegion);
            string alternateLabel = GetNetworkRegionLabel(alternateRegion);
            string message = isManual
                ? Localization.Format("Msg_SwitchNetworkSourcePromptManual", errorMessage, currentLabel, alternateLabel)
                : Localization.Format("Msg_SwitchNetworkSourcePromptAuto", alternateLabel);

            var result = System.Windows.MessageBox.Show(
                this,
                message,
                Localization.Get("Msg_SwitchNetworkSource_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            ConfigManager.Save("NetworkRegion", alternateRegion);
            _networkRegionAtLoad = alternateRegion;
            SelectNetworkRegion(alternateRegion);
            UiLocalizer.ApplyControlPanel(this);
            ConfigManager.Save("LastNoticeContent", string.Empty);
            AppLogger.Info($"Network source switched to {alternateRegion}.");
            _ = LoadRemoteNoticeAsync(isManual: false);
            if (isManual)
            {
                _ = CheckForUpdates(isManual: true);
            }
        }

        private static NetworkRegionOption GetAlternateNetworkRegion() =>
            Localization.UseChinaNetworkEndpoint()
                ? NetworkRegionOption.Global
                : NetworkRegionOption.China;

        private static string GetNetworkRegionLabel(NetworkRegionOption region) =>
            region switch
            {
                NetworkRegionOption.China => Localization.Get("Basic_NetworkRegionChina"),
                NetworkRegionOption.Global => Localization.Get("Basic_NetworkRegionGlobal"),
                _ => Localization.Get("Basic_NetworkRegionAuto")
            };

        private void ShowWindowsNotification(string title, string content)
        {
            try
            {
                new ToastContentBuilder().AddText(title).AddText(content).Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("通知推送失败: " + ex.Message);
            }
        }

        private void LoadVersion()
        {
            try
            {
                string version = AppVersionInfo.DisplayVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    string versionNum = $"V{version}";
                    string versionText = $"BASpark V{version}";

                    if (VersionText != null)
                    {
                        VersionText.Text = versionNum;
                    }

                    if (TxtSidebarVersion != null)
                    {
                        TxtSidebarVersion.Text = versionText;
                    }
                }
            }
            catch
            {
                string failed = Localization.Get("Version_ReadFailed");
                if (VersionText != null) VersionText.Text = failed;
                if (TxtSidebarVersion != null) TxtSidebarVersion.Text = failed;
            }
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            int clickCount = ConfigManager.TotalClicks;
            if (ClickCountText != null && clickCount != _lastRenderedClickCount)
            {
                _lastRenderedClickCount = clickCount;
                ClickCountText.Text = Localization.Format("Welcome_ClicksUnit", clickCount);
            }

            if (StatusText != null)
            {
                bool suppressedByEnvironment = ConfigManager.IsEffectEnabled &&
                    App.Overlay?.IsEffectSuppressedByEnvironment() == true;
                int status = !ConfigManager.IsEffectEnabled ? 0 : suppressedByEnvironment ? 1 : 2;
                if (status == _lastRenderedStatus)
                {
                    return;
                }

                _lastRenderedStatus = status;
                if (status == 0)
                {
                    StatusText.Text = Localization.Get("Status_Paused");
                    StatusText.Foreground = System.Windows.Media.Brushes.Gray;
                }
                else if (status == 1)
                {
                    StatusText.Text = Localization.Get("Status_Filtered");
                    StatusText.Foreground = FilteredStatusBrush;
                }
                else
                {
                    StatusText.Text = Localization.Get("Status_Active");
                    StatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
        }

        private void RefreshRunningProcessList()
        {
            RunningProcessList.Clear();
            try
            {
                int selfId = Environment.ProcessId;
                foreach (Process p in Process.GetProcesses().OrderBy(x => x.ProcessName))
                {
                    try
                    {
                        if (p.Id == selfId)
                        {
                            continue;
                        }

                        IntPtr hwnd;
                        try
                        {
                            hwnd = p.MainWindowHandle;
                        }
                        catch
                        {
                            continue;
                        }

                        if (hwnd == IntPtr.Zero)
                        {
                            continue;
                        }

                        string baseName = p.ProcessName;
                        if (string.IsNullOrEmpty(baseName))
                        {
                            continue;
                        }

                        string pName = baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? baseName.ToLowerInvariant()
                            : (baseName + ".exe").ToLowerInvariant();

                        if (RunningProcessList.Any(item => item.ProcessName.Equals(pName, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        string dName = pName;
                        try
                        {
                            string? desc = p.MainModule?.FileVersionInfo.FileDescription;
                            if (!string.IsNullOrWhiteSpace(desc))
                            {
                                dName = desc;
                            }
                            else if (!string.IsNullOrEmpty(p.MainWindowTitle))
                            {
                                dName = p.MainWindowTitle;
                            }
                        }
                        catch
                        {
                            if (!string.IsNullOrEmpty(p.MainWindowTitle))
                            {
                                dName = p.MainWindowTitle;
                            }
                        }

                        RunningProcessList.Add(new ProcessItem
                        {
                            DisplayName = dName,
                            ProcessName = pName,
                            IsSelected = false
                        });
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex) { AppLogger.Warn($"Failed to enumerate running processes: {ex.Message}"); }
        }

        private void SearchRunningProcess_TextChanged(object sender, TextChangedEventArgs e)
        {
            string? filter = (sender as System.Windows.Controls.TextBox)?.Text;
            ICollectionView view = CollectionViewSource.GetDefaultView(RunningProcessList);
            if (view == null) return;

            if (string.IsNullOrWhiteSpace(filter))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = obj =>
                {
                    if (obj is ProcessItem item)
                    {
                        return item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               item.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                };
            }
        }

        private void LoadSettings()
        {
            _suspendLinkedAnimationUiHandlers = true;
            try
            {
                LoadSettingsCore();
            }
            finally
            {
                _suspendLinkedAnimationUiHandlers = false;
            }
        }

        private void LoadSettingsCore()
        {
            CheckMasterSwitch.IsChecked = ConfigManager.IsEffectEnabled;
            CheckAutoStart.IsChecked = ConfigManager.AutoStart;
            CheckStartSilent.IsChecked = ConfigManager.StartSilent;
            CheckTelemetry.IsChecked = ConfigManager.EnableTelemetry;
            CheckAlwaysTrailEffectSwitch.IsChecked = ConfigManager.EnableAlwaysTrailEffect;
            CheckEnvironmentFilter.IsChecked = ConfigManager.EnableEnvironmentFilter;
            CheckHideInFullscreen.IsChecked = ConfigManager.HideInFullscreen;
            CheckShowEffectOnDesktop.IsChecked = ConfigManager.ShowEffectOnDesktop;
            CheckRunAsAdmin.IsChecked = ConfigManager.RunAsAdmin;
            CheckTouchscreenMode.IsChecked = ConfigManager.IsTouchscreenMode;
            CheckMiddleClickTrigger.IsChecked = ConfigManager.EnableMiddleClickTrigger;
            CheckScreenshotCompatibilityMode.IsChecked = ConfigManager.ScreenshotCompatibilityMode;

            int mode = ConfigManager.ClickTriggerType;
            if (mode == 1) RadioRightClick.IsChecked = true;
            else if (mode == 2) RadioBothClick.IsChecked = true;
            else RadioLeftClick.IsChecked = true;

            Profiles.Clear();
            foreach (var p in ConfigManager.GetProfiles()) Profiles.Add(p);

            string? activeId = ConfigManager.GetActiveProfile()?.Id;
            ComboProfiles.SelectedItem = Profiles.FirstOrDefault(p => p.Id == activeId) ?? Profiles.FirstOrDefault();

            UpdateColorPreview(ConfigManager.ParticleColor);
            UpdateClickEffectPanelVisibility();
            UpdateEnvironmentFilterInterlock();

            SliderScale.Value = ConfigManager.EffectScale;
            SliderTrailThickness.Value = ConfigManager.TrailThickness;
            SliderGlowIntensity.Value = ConfigManager.GlowIntensity;
            SliderOpacity.Value = ConfigManager.EffectOpacity;
            CheckLinkedAnimationSpeed.IsChecked = ConfigManager.UseLinkedAnimationSpeed;
            CheckApplyCurveDraw.IsChecked = ConfigManager.ApplyCurveDraw;
            SliderSpeed.Value = ConfigManager.EffectSpeed;
            SliderTrailAnimSpeed.Value = ConfigManager.TrailAnimationSpeed;
            SliderClickAnimSpeed.Value = ConfigManager.ClickAnimationSpeed;
            CheckFollowDisplayRefreshRate.IsChecked = ConfigManager.FollowDisplayRefreshRate;
            SliderTrailRefresh.Value = ConfigManager.TrailRefreshRate;
            SliderTrailDelay.Value = ConfigManager.TrailDelay;
            UpdateAnimationSpeedPanelVisibility();

            if (ConfigManager.ScrollbarVisibility == PanelScrollbarVisibility.Always)
            {
                RadioScrollbarAlways.IsChecked = true;
            }
            else
            {
                RadioScrollbarOnScroll.IsChecked = true;
            }

            SelectDarkMode(ConfigManager.DarkMode);
            SelectNetworkRegion(ConfigManager.NetworkRegion);
            if (TxtSidebarBackgroundPath != null)
            {
                TxtSidebarBackgroundPath.Text = ConfigManager.SidebarBackgroundImagePath;
            }
        }

        private void SelectDarkMode(DarkModeOption mode)
        {
            switch (mode)
            {
                case DarkModeOption.Off:
                    RadioDarkModeOff.IsChecked = true;
                    break;
                case DarkModeOption.On:
                    RadioDarkModeOn.IsChecked = true;
                    break;
                default:
                    RadioDarkModeSystem.IsChecked = true;
                    break;
            }
        }

        private DarkModeOption GetSelectedDarkMode()
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

        private void ApplyDarkMode()
        {
            ThemeManager.ApplyControlPanel(this);
            UpdateEnvironmentFilterInterlock();
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (ConfigManager.DarkMode != DarkModeOption.System)
            {
                return;
            }

            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.VisualStyle ||
                e.Category == UserPreferenceCategory.Color)
            {
                if (Interlocked.Exchange(ref _themeRefreshPending, 1) != 0)
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref _themeRefreshPending, 0);
                    if (ConfigManager.DarkMode == DarkModeOption.System)
                    {
                        ApplyDarkMode();
                    }
                }), DispatcherPriority.Normal);
            }
        }

        private void SelectNetworkRegion(NetworkRegionOption region)
        {
            switch (region)
            {
                case NetworkRegionOption.China:
                    RadioNetworkRegionChina.IsChecked = true;
                    break;
                case NetworkRegionOption.Global:
                    RadioNetworkRegionGlobal.IsChecked = true;
                    break;
                default:
                    RadioNetworkRegionAuto.IsChecked = true;
                    break;
            }
        }

        private NetworkRegionOption GetSelectedNetworkRegion()
        {
            if (RadioNetworkRegionChina.IsChecked == true)
            {
                return NetworkRegionOption.China;
            }

            if (RadioNetworkRegionGlobal.IsChecked == true)
            {
                return NetworkRegionOption.Global;
            }

            return NetworkRegionOption.Auto;
        }

        private void ApplyScrollbarSettings()
        {
            _scrollbarHideTimer.Stop();
            ApplyScrollbarVisibility(MainContentScrollViewer);
            ApplyScrollbarVisibility(SettingsContentScrollViewer);
        }

        private void ApplyScrollbarVisibility(ScrollViewer scrollViewer)
        {
            scrollViewer.VerticalScrollBarVisibility = ConfigManager.ScrollbarVisibility == PanelScrollbarVisibility.Always
                ? ScrollBarVisibility.Visible
                : ScrollBarVisibility.Hidden;
        }

        private void ShowScrollbarTemporarily(ScrollViewer scrollViewer)
        {
            if (ConfigManager.ScrollbarVisibility != PanelScrollbarVisibility.OnScroll)
            {
                return;
            }

            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            _scrollbarHideTimer.Stop();
            _scrollbarHideTimer.Start();
        }

        private void ScrollbarHideTimer_Tick(object? sender, EventArgs e)
        {
            _scrollbarHideTimer.Stop();
            if (ConfigManager.ScrollbarVisibility == PanelScrollbarVisibility.OnScroll)
            {
                ApplyScrollbarVisibility(MainContentScrollViewer);
                ApplyScrollbarVisibility(SettingsContentScrollViewer);
            }
        }

        private void MainContentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) > 0.01 || Math.Abs(e.HorizontalChange) > 0.01)
            {
                ShowScrollbarTemporarily(MainContentScrollViewer);
            }
        }

        private void MainContentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ShowScrollbarTemporarily(MainContentScrollViewer);
        }

        private void SettingsContentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) > 0.01 || Math.Abs(e.HorizontalChange) > 0.01)
            {
                ShowScrollbarTemporarily(SettingsContentScrollViewer);
            }
        }

        private void SettingsContentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ShowScrollbarTemporarily(SettingsContentScrollViewer);
        }

        private void LoadScreenOptions()
        {
            ScreenOptions.Clear();
            var screens = Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();
            var screenInfos = screens
                .Select(screen => new { Screen = screen, Identity = ScreenIdentity.FromScreen(screen) })
                .ToList();
            var enabledDeviceNames = ConfigManager.ResolveEnabledScreenDeviceNames(screenInfos.Select(item => item.Identity));

            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screenInfos[i].Screen;
                var identity = screenInfos[i].Identity;
                bool enabled = enabledDeviceNames.Contains(screen.DeviceName);
                // 显示真实显示器名称，减少 DISPLAY1/2 变化误判
                string title = identity.DisplayName + (screen.Primary ? Localization.Get("MultiScreen_Primary") : string.Empty);
                string resolution = $"{screen.Bounds.Width} x {screen.Bounds.Height}";
                string detail = Localization.Format(
                    "MultiScreen_Detail",
                    GetScaleText(screen),
                    screen.Bounds.Left,
                    screen.Bounds.Top,
                    screen.DeviceName);

                ScreenOptions.Add(new ScreenOptionItem
                {
                    DisplayIndex = i + 1,
                    Title = title,
                    ResolutionText = resolution,
                    DetailText = detail,
                    DeviceName = screen.DeviceName,
                    IdentityKey = identity.IdentityKey,
                    DisplayName = identity.DisplayName,
                    IsEnabled = enabled,
                    EnableLabel = Localization.Get("MultiScreen_Enable")
                });
            }
        }

        private static string GetScaleText(Screen screen)
        {
            try
            {
                var center = new POINT
                {
                    X = screen.Bounds.Left + (screen.Bounds.Width / 2),
                    Y = screen.Bounds.Top + (screen.Bounds.Height / 2)
                };
                IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                {
                    return Localization.Format("MultiScreen_Scale", 100);
                }

                int hr = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
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

        private void CheckMasterSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateClickEffectPanelVisibility();
        }

        private void UpdateClickEffectPanelVisibility()
        {
            bool enabled = CheckMasterSwitch.IsChecked == true;
            PanelClickEffectOptions.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CheckRunAsAdmin_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            if (CheckRunAsAdmin.IsChecked == true)
            {
                StatusText.Text = Localization.Get("Status_AdminPending");
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }

        private void EnvironmentFilterSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateEnvironmentFilterInterlock();
        }

        private void ProcessFilterMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ComboProfiles.SelectedItem is FilterProfile active)
            {
                active.Mode = GetSelectedProcessFilterMode();
            }
            UpdateEnvironmentFilterInterlock();
        }

        private void UpdateEnvironmentFilterInterlock()
        {
            bool environmentFilterEnabled = CheckEnvironmentFilter.IsChecked == true;
            ProcessFilterModeOption selectedMode = GetSelectedProcessFilterMode();
            bool processFilterEnabled = environmentFilterEnabled && selectedMode != ProcessFilterModeOption.Disabled;

            CheckHideInFullscreen.IsEnabled = environmentFilterEnabled;
            CheckShowEffectOnDesktop.IsEnabled = environmentFilterEnabled;
            ComboProfiles.IsEnabled = environmentFilterEnabled;
            ComboProcessFilterMode.IsEnabled = environmentFilterEnabled;

            if (ListConfiguredProcesses != null)
            {
                bool useDarkDisabledTemplate = !processFilterEnabled && ThemeManager.IsDarkModeEnabled();
                if (useDarkDisabledTemplate &&
                    TryFindResource("DarkDisabledListBoxTemplate") is ControlTemplate disabledTemplate)
                {
                    ListConfiguredProcesses.Template = disabledTemplate;
                }
                else
                {
                    ListConfiguredProcesses.ClearValue(System.Windows.Controls.Control.TemplateProperty);
                }

                ListConfiguredProcesses.IsEnabled = processFilterEnabled;
                ListConfiguredProcesses.Opacity = processFilterEnabled
                    ? 1.0
                    : useDarkDisabledTemplate ? 0.6 : 0.65;
            }
            ManualProcessInput.IsEnabled = processFilterEnabled;
        }

        private void SelectProcessFilterMode(ProcessFilterModeOption mode)
        {
            ComboBoxItem? selectedItem = ComboProcessFilterMode.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase));

            ComboProcessFilterMode.SelectedItem = selectedItem ?? ComboProcessFilterMode.Items[0];
        }

        private ProcessFilterModeOption GetSelectedProcessFilterMode()
        {
            if (ComboProcessFilterMode.SelectedItem is ComboBoxItem item &&
                Enum.TryParse(item.Tag?.ToString(), true, out ProcessFilterModeOption mode))
            {
                return mode;
            }
            return ProcessFilterModeOption.Disabled;
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (PageWelcome == null) return;
            PageWelcome.Visibility = Visibility.Collapsed;
            PageSettings.Visibility = Visibility.Collapsed;
            PageLog.Visibility = Visibility.Collapsed;
            PageAbout.Visibility = Visibility.Collapsed;

            if (TabWelcome.IsChecked == true) PageWelcome.Visibility = Visibility.Visible;
            else if (TabSettings.IsChecked == true) PageSettings.Visibility = Visibility.Visible;
            else if (TabLog.IsChecked == true) PageLog.Visibility = Visibility.Visible;
            else if (TabAbout.IsChecked == true) PageAbout.Visibility = Visibility.Visible;
        }

        private void InitLogView()
        {
            if (_logViewInitialized || TxtAppLog == null)
            {
                return;
            }

            _logViewInitialized = true;
            TxtAppLog.Text = string.Join(Environment.NewLine, AppLogger.GetEntries());
            TxtAppLog.ScrollToEnd();
        }

        private void OnAppLogEntryAdded(string line)
        {
            Dispatcher.BeginInvoke(() =>
            {
                AppendLogLine(line);
            });
        }

        private void AppendLogLine(string line)
        {
            if (TxtAppLog == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(TxtAppLog.Text))
            {
                TxtAppLog.Text = line;
            }
            else
            {
                TxtAppLog.AppendText(Environment.NewLine + line);
            }

            TxtAppLog.ScrollToEnd();
            LogScrollViewer?.ScrollToEnd();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (TxtAppLog != null)
            {
                TxtAppLog.Clear();
            }

            AppLogger.Clear();
            AppLogger.Info("Log view cleared by user.");
        }

        private async Task ApplySidebarBackgroundAsync(string? path)
        {
            try
            {
                SidebarBackgroundHelper.ClearCache();
                var brush = await SidebarBackgroundHelper.LoadBrushAsync(path);
                Dispatcher.Invoke(() =>
                {
                    if (SidebarBackgroundHost == null)
                    {
                        return;
                    }

                    if (brush == null)
                    {
                        SidebarBackgroundHost.SetResourceReference(
                            System.Windows.Controls.Panel.BackgroundProperty,
                            "ThemeSurfaceBackgroundBrush");
                    }
                    else
                    {
                        SidebarBackgroundHost.Background = brush;
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to apply sidebar background.", ex);
            }
        }

        private void BrowseSidebarBackground_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Localization.Get("Dialog_ImageFilter"),
                Title = Localization.Get("More_SidebarBackgroundBrowse")
            };

            if (dialog.ShowDialog() == true)
            {
                TxtSidebarBackgroundPath.Text = dialog.FileName;
            }
        }

        private void ClearSidebarBackground_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            TxtSidebarBackgroundPath.Text = string.Empty;
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.ColorDialog();
            dialog.FullOpen = true;
            try
            {
                var parts = ConfigManager.ParticleColor.Split(',');
                dialog.Color = System.Drawing.Color.FromArgb(
                    byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
            }
            catch { /* ignore: fallback to default color */ }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string newColor = $"{dialog.Color.R},{dialog.Color.G},{dialog.Color.B}";
                ConfigManager.ParticleColor = newColor;
                UpdateColorPreview(newColor);
            }
        }

        private void UpdateColorPreview(string rgbString)
        {
            try
            {
                var parts = rgbString.Split(',');
                if (parts.Length == 3)
                {
                    byte r = byte.Parse(parts[0].Trim());
                    byte g = byte.Parse(parts[1].Trim());
                    byte b = byte.Parse(parts[2].Trim());
                    ColorPreview.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(r, g, b));
                }
            }
            catch
            {
                ColorPreview.Background = System.Windows.Media.Brushes.Gray;
            }
        }

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn)
            {
                return;
            }

            string? url = btn.Tag as string;
            if (btn == BtnOfficialSite)
            {
                url = Localization.GetOfficialWebsiteUrl();
            }
            else if (btn == BtnDiscord)
            {
                url = Localization.GetDiscordUrl();
            }

            if (string.IsNullOrWhiteSpace(url) || !TryCreateHttpUri(url, out Uri? uri))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Localization.Format("Msg_OpenLinkFailed", ex.Message));
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

        public void RefreshScreenEnableLabels()
        {
            string label = Localization.Get("MultiScreen_Enable");
            foreach (var item in ScreenOptions)
            {
                item.EnableLabel = label;
            }

            ListScreenOptions.Items.Refresh();
        }

        public void ApplyAboutLinkVisibility()
        {
            BtnOfficialSite.Tag = Localization.GetOfficialWebsiteUrl();
            bool isChinese = Localization.IsChineseLocale;
            BtnBilibili.Visibility = isChinese ? Visibility.Visible : Visibility.Collapsed;
            BtnQQ.Visibility = isChinese ? Visibility.Visible : Visibility.Collapsed;
            BtnSponsor.Visibility = isChinese ? Visibility.Visible : Visibility.Collapsed;
            BtnDiscord.Visibility = isChinese ? Visibility.Collapsed : Visibility.Visible;

            string? discordUrl = Localization.GetDiscordUrl();
            BtnDiscord.IsEnabled = !string.IsNullOrWhiteSpace(discordUrl);
            BtnDiscord.Tag = discordUrl ?? string.Empty;
        }

        private string? GetSelectedLanguage()
        {
            if (ComboLanguage.SelectedItem is ComboBoxItem item)
            {
                return item.Tag?.ToString();
            }

            return null;
        }

        // --- 配置组管理 ---
        private void ComboProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboProfiles.SelectedItem is FilterProfile selected)
            {
                CurrentProfileProcesses.Clear();
                foreach (var p in selected.Processes) CurrentProfileProcesses.Add(p);
                SelectProcessFilterMode(selected.Mode);
                UpdateEnvironmentFilterInterlock();
            }
        }

        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            var newProfile = new FilterProfile { Name = Localization.Format("Profile_NewNumbered", Profiles.Count + 1) };
            Profiles.Add(newProfile);
            ComboProfiles.SelectedItem = newProfile;
        }

        private void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ComboProfiles.SelectedItem is FilterProfile active)
            {
                // 使用自定义输入框
                NewProfileNameInput.Text = active.Name;
                RenameProfileOverlay.Visibility = Visibility.Visible;
                NewProfileNameInput.Focus();
                NewProfileNameInput.SelectAll();
            }
        }

        private void ConfirmRenameProfile_Click(object sender, RoutedEventArgs e)
        {
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
            RenameProfileOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseRenameOverlay_Click(object sender, RoutedEventArgs e)
        {
            RenameProfileOverlay.Visibility = Visibility.Collapsed;
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Profiles.Count <= 1)
            {
                System.Windows.MessageBox.Show(
                    Localization.Get("Msg_KeepOneProfile"),
                    Localization.Get("Msg_Info"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (ComboProfiles.SelectedItem is FilterProfile active)
            {
                if (System.Windows.MessageBox.Show(
                        Localization.Format("Msg_ConfirmDeleteProfile", active.Name),
                        Localization.Get("Msg_ConfirmDelete_Title"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Profiles.Remove(active);
                    ComboProfiles.SelectedIndex = 0;
                }
            }
        }

        // --- 进程管理 ---
        private void RemoveProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is string processName)
            {
                CurrentProfileProcesses.Remove(processName);
                if (ComboProfiles.SelectedItem is FilterProfile active)
                {
                    active.Processes.Remove(processName);
                }
            }
        }

        private void AddManualProcess_Click(object sender, RoutedEventArgs e)
        {
            string input = ManualProcessInput.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(input)) return;
            if (!input.EndsWith(".exe")) input += ".exe";

            AddProcessToActiveProfile(input);
            ManualProcessInput.Clear();
        }

        private void BrowseProcess_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Localization.Get("Dialog_ExeFilter"),
                Title = Localization.Get("Dialog_SelectProcess")
            };

            if (dialog.ShowDialog() == true)
            {
                string fileName = System.IO.Path.GetFileName(dialog.FileName).ToLowerInvariant();
                AddProcessToActiveProfile(fileName);
            }
        }

        private void SelectRunningProcess_Click(object sender, RoutedEventArgs e)
        {
            RefreshRunningProcessList();
            RunningProcessOverlay.Visibility = Visibility.Visible;
        }

        private void CloseRunningProcessOverlay_Click(object sender, RoutedEventArgs e)
        {
            RunningProcessOverlay.Visibility = Visibility.Collapsed;
        }

        private void ConfirmAddRunningProcesses_Click(object sender, RoutedEventArgs e)
        {
            var selected = RunningProcessList.Where(p => p.IsSelected).Select(p => p.ProcessName).ToList();
            foreach (var p in selected)
            {
                AddProcessToActiveProfile(p);
            }
            RunningProcessOverlay.Visibility = Visibility.Collapsed;
        }

        private void RebuildVisualResetItems()
        {
            VisualResetItems.Clear();
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.EffectScale, Localization.Get("VisualReset_Scale"), Localization.Get("VisualReset_Scale_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailThickness, Localization.Get("VisualReset_TrailThickness"), Localization.Get("VisualReset_TrailThickness_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.GlowIntensity, Localization.Get("VisualReset_GlowIntensity"), Localization.Get("VisualReset_GlowIntensity_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.EffectOpacity, Localization.Get("VisualReset_Opacity"), Localization.Get("VisualReset_Opacity_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.UnifiedAnimationSpeed, Localization.Get("VisualReset_UnifiedSpeed"), Localization.Get("VisualReset_UnifiedSpeed_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailAnimationSpeed, Localization.Get("VisualReset_TrailSpeed"), Localization.Get("VisualReset_TrailSpeed_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.ClickAnimationSpeed, Localization.Get("VisualReset_ClickSpeed"), Localization.Get("VisualReset_ClickSpeed_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailRefreshRate, Localization.Get("VisualReset_TrailRefresh"), Localization.Get("VisualReset_TrailRefresh_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.TrailDelay, Localization.Get("VisualReset_TrailDelay"), Localization.Get("VisualReset_TrailDelay_Sub")));
            VisualResetItems.Add(new VisualResetItem(VisualAppearanceResetFlags.ParticleColor, Localization.Get("VisualReset_Color"), Localization.Get("VisualReset_Color_Sub")));
        }

        private void OpenVisualResetOverlay_Click(object sender, RoutedEventArgs e)
        {
            RebuildVisualResetItems();
            foreach (var item in VisualResetItems)
            {
                item.IsSelected = true;
            }

            SearchVisualReset.Text = string.Empty;
            ICollectionView view = CollectionViewSource.GetDefaultView(VisualResetItems);
            if (view != null)
            {
                view.Filter = null;
            }

            VisualResetOverlay.Visibility = Visibility.Visible;
        }

        private void CloseVisualResetOverlay_Click(object sender, RoutedEventArgs e)
        {
            VisualResetOverlay.Visibility = Visibility.Collapsed;
        }

        private void SearchVisualReset_TextChanged(object sender, TextChangedEventArgs e)
        {
            string? filter = (sender as System.Windows.Controls.TextBox)?.Text;
            ICollectionView view = CollectionViewSource.GetDefaultView(VisualResetItems);
            if (view == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(filter))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = obj =>
                {
                    if (obj is VisualResetItem item)
                    {
                        return item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               item.Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                };
            }
        }

        private void ConfirmVisualReset_Click(object sender, RoutedEventArgs e)
        {
            VisualAppearanceResetFlags flags = VisualAppearanceResetFlags.None;
            foreach (var item in VisualResetItems)
            {
                if (item.IsSelected)
                {
                    flags |= item.Flags;
                }
            }

            if (flags == VisualAppearanceResetFlags.None)
            {
                System.Windows.MessageBox.Show(
                    this,
                    Localization.Get("Msg_SelectVisualReset"),
                    Localization.Get("Msg_VisualReset_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ConfigManager.ApplyVisualAppearanceDefaults(flags);
            LoadSettings();

            int trailRefreshRate = (int)Math.Round(SliderTrailRefresh.Value);
            bool followDisplayRefreshRate = CheckFollowDisplayRefreshRate.IsChecked == true;
            ConfigManager.GetAnimationSpeedsForOverlay(out double trailSp, out double clickSp);
            double effectScale = Math.Round(SliderScale.Value, 2);
            double trailThickness = Math.Round(SliderTrailThickness.Value, 2);
            double glowIntensity = Math.Round(SliderGlowIntensity.Value, 2);
            double trailDelay = Math.Round(SliderTrailDelay.Value, 2);
            double effectOpacity = Math.Round(SliderOpacity.Value, 2);
            App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
            App.Overlay?.UpdateEffectSettings(effectScale, effectOpacity, trailSp, clickSp, trailThickness, trailDelay, glowIntensity);
            App.Overlay?.UpdateTrailRefreshRate(trailRefreshRate, followDisplayRefreshRate);

            VisualResetOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show(
                this,
                Localization.Get("Msg_VisualResetDone"),
                Localization.Get("Msg_VisualReset_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void AddProcessToActiveProfile(string processName)
        {
            if (ComboProfiles.SelectedItem is FilterProfile active)
            {
                if (!active.Processes.Contains(processName, StringComparer.OrdinalIgnoreCase))
                {
                    active.Processes.Add(processName);
                    CurrentProfileProcesses.Add(processName);
                }
                else
                {
                    // 已存在，不重复添加
                }
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            bool autoStartWasEnabled = ConfigManager.AutoStart;
            bool runAsAdminWasEnabled = ConfigManager.RunAsAdmin;
            DarkModeOption previousDarkMode = ConfigManager.DarkMode;
            string? selectedLanguage = GetSelectedLanguage();
            bool languageChanged = !string.IsNullOrWhiteSpace(selectedLanguage) &&
                !string.Equals(selectedLanguage, _languageAtLoad, StringComparison.OrdinalIgnoreCase);
            NetworkRegionOption selectedNetworkRegion = GetSelectedNetworkRegion();
            bool networkRegionChanged = selectedNetworkRegion != _networkRegionAtLoad;
            DarkModeOption selectedDarkMode = GetSelectedDarkMode();

            double effectScale = Math.Round(SliderScale.Value, 2);
            double trailThickness = Math.Round(SliderTrailThickness.Value, 2);
            double glowIntensity = Math.Round(SliderGlowIntensity.Value, 2);
            double trailDelay = Math.Round(SliderTrailDelay.Value, 2);
            double effectOpacity = Math.Round(SliderOpacity.Value, 2);
            bool useLinkedAnimationSpeed = CheckLinkedAnimationSpeed.IsChecked == true;
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
            bool followDisplayRefreshRate = CheckFollowDisplayRefreshRate.IsChecked == true;
            bool autoStartEnabled = CheckAutoStart.IsChecked ?? false;
            bool startSilentEnabled = CheckStartSilent.IsChecked ?? false;
            bool runAsAdminEnabled = CheckRunAsAdmin.IsChecked ?? false;
            bool isTouchscreenEnabled = CheckTouchscreenMode?.IsChecked ?? false;
            bool middleClickEnabled = CheckMiddleClickTrigger.IsChecked ?? false;
            bool screenshotCompatibilityEnabled = CheckScreenshotCompatibilityMode.IsChecked ?? false;
            bool curveDrawEnabled = CheckApplyCurveDraw.IsChecked == true;

            int clickType = 0;
            if (RadioRightClick.IsChecked == true) clickType = 1;
            else if (RadioBothClick.IsChecked == true) clickType = 2;

            bool telemetryEnabled = CheckTelemetry.IsChecked ?? false;
            bool telemetryWasEnabled = ConfigManager.EnableTelemetry;

            string sidebarBackgroundPath = TxtSidebarBackgroundPath?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sidebarBackgroundPath))
            {
                if (!SidebarBackgroundHelper.IsSupportedImage(sidebarBackgroundPath) || !System.IO.File.Exists(sidebarBackgroundPath))
                {
                    System.Windows.MessageBox.Show(
                        this,
                        Localization.Get("Msg_InvalidSidebarBackground"),
                        Localization.Get("More_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var selectedIds = ScreenOptions
                .Where(s => s.IsEnabled)
                .Select(s => s.DeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (selectedIds.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    this,
                    Localization.Get("Msg_MinOneScreen"),
                    Localization.Get("Msg_MultiScreen_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AutoStartPlan previousAutoStartPlan = AutoStartManager.CreatePlan(autoStartWasEnabled, runAsAdminWasEnabled);
            AutoStartPlan requestedAutoStartPlan = AutoStartManager.CreatePlan(autoStartEnabled, runAsAdminEnabled);
            bool autoStartPlanChanged = previousAutoStartPlan != requestedAutoStartPlan;
            if (autoStartPlanChanged &&
                !ApplyAutoStartSettings(previousAutoStartPlan, requestedAutoStartPlan, showError: true))
            {
                return;
            }

            if (!ConfigManager.SaveAutoStartSettings(autoStartEnabled, runAsAdminEnabled))
            {
                bool restored = !autoStartPlanChanged ||
                    ApplyAutoStartSettings(requestedAutoStartPlan, previousAutoStartPlan, showError: false);
                ConfigManager.AutoStart = autoStartWasEnabled;
                ConfigManager.RunAsAdmin = runAsAdminWasEnabled;
                string details = restored
                    ? "The application configuration could not be saved."
                    : "The application configuration could not be saved, and the previous operating-system auto-start state could not be fully restored.";
                ShowAutoStartError(details);
                return;
            }

            bool settingsSaved = true;
            if (!string.IsNullOrWhiteSpace(selectedLanguage))
            {
                settingsSaved &= ConfigManager.Save("UiLanguage", selectedLanguage);
            }

            // 保存配置组
            string activeId = (ComboProfiles.SelectedItem as FilterProfile)?.Id ?? "";
            settingsSaved &= ConfigManager.SaveProfiles(Profiles.ToList(), activeId);

            settingsSaved &= ConfigManager.Save("IsTouchscreenMode", isTouchscreenEnabled);
            settingsSaved &= ConfigManager.Save("IsEffectEnabled", CheckMasterSwitch.IsChecked ?? true);
            settingsSaved &= ConfigManager.Save("EnableTelemetry", telemetryEnabled);
            settingsSaved &= ConfigManager.Save("ParticleColor", ConfigManager.ParticleColor);
            settingsSaved &= ConfigManager.Save("EffectScale", effectScale);
            settingsSaved &= ConfigManager.Save("TrailThickness", trailThickness);
            settingsSaved &= ConfigManager.Save("GlowIntensity", glowIntensity);
            settingsSaved &= ConfigManager.Save("TrailDelay", trailDelay);
            settingsSaved &= ConfigManager.Save("EffectOpacity", effectOpacity);
            settingsSaved &= ConfigManager.Save("UseLinkedAnimationSpeed", useLinkedAnimationSpeed);
            settingsSaved &= ConfigManager.Save("EffectSpeed", effectSpeedForRegistry);
            settingsSaved &= ConfigManager.Save("TrailAnimationSpeed", trailAnimSpeed);
            settingsSaved &= ConfigManager.Save("ClickAnimationSpeed", clickAnimSpeed);
            settingsSaved &= ConfigManager.Save("TrailRefreshRate", trailRefreshRate);
            settingsSaved &= ConfigManager.Save("FollowDisplayRefreshRate", followDisplayRefreshRate);
            settingsSaved &= ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);
            settingsSaved &= ConfigManager.Save("EnableAlwaysTrailEffect", CheckAlwaysTrailEffectSwitch.IsChecked ?? false);
            var scrollbarVisibility = RadioScrollbarAlways.IsChecked == true
                ? PanelScrollbarVisibility.Always
                : PanelScrollbarVisibility.OnScroll;
            settingsSaved &= ConfigManager.Save("ScrollbarVisibility", scrollbarVisibility);
            settingsSaved &= ConfigManager.Save("NetworkRegion", selectedNetworkRegion);
            settingsSaved &= ConfigManager.Save("DarkMode", selectedDarkMode);
            settingsSaved &= ConfigManager.Save("StartSilent", startSilentEnabled);
            settingsSaved &= ConfigManager.Save("EnableEnvironmentFilter", CheckEnvironmentFilter.IsChecked ?? false);
            settingsSaved &= ConfigManager.Save("HideInFullscreen", CheckHideInFullscreen.IsChecked ?? true);
            settingsSaved &= ConfigManager.Save("ShowEffectOnDesktop", CheckShowEffectOnDesktop.IsChecked ?? true);
            settingsSaved &= ConfigManager.Save("ClickTriggerType", clickType);
            settingsSaved &= ConfigManager.Save("EnableMiddleClickTrigger", middleClickEnabled);
            settingsSaved &= ConfigManager.Save("ScreenshotCompatibilityMode", screenshotCompatibilityEnabled);
            settingsSaved &= ConfigManager.Save("ApplyCurveDraw", curveDrawEnabled);

            bool sidebarBackgroundChanged = !string.Equals(
                sidebarBackgroundPath,
                ConfigManager.SidebarBackgroundImagePath,
                StringComparison.OrdinalIgnoreCase);
            settingsSaved &= ConfigManager.Save("SidebarBackgroundImagePath", sidebarBackgroundPath);

            var previousEnabledScreenIds = ConfigManager.ResolveEnabledScreenDeviceNames(ScreenOptions.Select(CreateScreenIdentityInfo));

            // 保存当前可见屏幕的启用状态，离线屏幕的旧设置会在 ConfigManager 中保留
            settingsSaved &= ConfigManager.SaveScreenSelections(ScreenOptions.Select(item => new ScreenSelectionState
            {
                IdentityKey = item.IdentityKey,
                DeviceName = item.DeviceName,
                DisplayName = item.DisplayName,
                IsEnabled = item.IsEnabled
            }));

            if (!settingsSaved)
            {
                System.Windows.MessageBox.Show(
                    this,
                    Localization.Get("Msg_SettingsSaveFailed"),
                    Localization.Get("Msg_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedLanguage))
            {
                Localization.ApplyCulture(selectedLanguage);
            }
            ApplyScrollbarSettings();
            ApplyDarkMode();
            if (selectedDarkMode != previousDarkMode)
            {
                ThemeManager.RefreshTitleBarAfterInput(this);
            }

            (System.Windows.Application.Current as App)?.RefreshTrayTheme();
            if (sidebarBackgroundChanged)
            {
                _ = ApplySidebarBackgroundAsync(sidebarBackgroundPath);
            }

            App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
            GetUiAnimationSpeeds(out double overlayTrail, out double overlayClick);
            App.Overlay?.UpdateEffectSettings(effectScale, effectOpacity, overlayTrail, overlayClick, trailThickness, trailDelay, glowIntensity);
            App.Overlay?.UpdateTrailRefreshRate(trailRefreshRate, followDisplayRefreshRate);
            App.Overlay?.RefreshEnvironmentFilterState();
            App.Overlay?.UpdateTouchMode(isTouchscreenEnabled);
            App.Overlay?.UpdateScreenshotCompatibilityMode(screenshotCompatibilityEnabled);
            App.Overlay?.SetCurveDraw(curveDrawEnabled);
            if (!previousEnabledScreenIds.SetEquals(selectedIds))
            {
                App.Overlay?.RefreshScreenSelection();
            }

            bool isCurrentAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (runAsAdminEnabled && !isCurrentAdmin)
            {
                var res = System.Windows.MessageBox.Show(
                    this,
                    Localization.Get("Msg_AdminRestart"),
                    Localization.Get("Msg_AdminRestart_Title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    RestartAsAdmin();
                    return;
                }
            }

            if (languageChanged)
            {
                UiLocalizer.ApplyControlPanel(this);
                LoadScreenOptions();
                ConfigManager.Save("LastNoticeContent", string.Empty);
                _ = LoadRemoteNoticeAsync(isManual: false);
                _languageAtLoad = selectedLanguage!;

                var restartRes = System.Windows.MessageBox.Show(
                    this,
                    Localization.Get("Msg_LanguageRestart"),
                    Localization.Get("Msg_LanguageRestart_Title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (restartRes == MessageBoxResult.Yes)
                {
                    if (System.Windows.Application.Current is App app)
                    {
                        app.RestartApplicationFromPanel();
                    }
                    return;
                }
            }
            else if (networkRegionChanged)
            {
                UiLocalizer.ApplyControlPanel(this);
                ConfigManager.Save("LastNoticeContent", string.Empty);
                _ = LoadRemoteNoticeAsync(isManual: false);
                _networkRegionAtLoad = selectedNetworkRegion;
            }

            if (telemetryEnabled && (!telemetryWasEnabled || networkRegionChanged))
            {
                TelemetryHelper.SendStartupData();
            }
        }

        private void RefreshScreenOptions_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            var selectedKeys = ScreenOptions.Where(s => s.IsEnabled).Select(s => s.IdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedIds = ScreenOptions.Where(s => s.IsEnabled).Select(s => s.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            LoadScreenOptions();
            foreach (var item in ScreenOptions)
            {
                // 手动刷新时保留当前勾选，避免刷新列表本身改变尚未保存的选择
                item.IsEnabled = (selectedKeys.Count == 0 && selectedIds.Count == 0) ||
                                 selectedKeys.Contains(item.IdentityKey) ||
                                 selectedIds.Contains(item.DeviceName);
            }
            ListScreenOptions.Items.Refresh();
        }

        private static ScreenIdentityInfo CreateScreenIdentityInfo(ScreenOptionItem item)
        {
            return new ScreenIdentityInfo
            {
                DeviceName = item.DeviceName,
                IdentityKey = item.IdentityKey,
                DisplayName = item.DisplayName
            };
        }

        private bool ApplyAutoStartSettings(
            AutoStartPlan previousPlan,
            AutoStartPlan requestedPlan,
            bool showError)
        {
            string? exePath = AutoStartManager.ResolveExecutablePath(
                Environment.ProcessPath,
                Process.GetCurrentProcess().MainModule?.FileName,
                Assembly.GetExecutingAssembly().Location,
                AppDomain.CurrentDomain.BaseDirectory);

            if (string.IsNullOrEmpty(exePath))
            {
                if (showError)
                {
                    ShowAutoStartError("The application executable path could not be resolved.");
                }
                return false;
            }

            string regKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
            bool scheduledTaskChanged = requestedPlan.ScheduledTaskEnabled != previousPlan.ScheduledTaskEnabled;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(regKeyPath, true);
                if (key == null)
                {
                    throw new InvalidOperationException($"Failed to open registry key for auto-start: {regKeyPath}");
                }

                if (scheduledTaskChanged &&
                    !AutoStartManager.TrySetScheduledTask(exePath, requestedPlan.ScheduledTaskEnabled, out string taskError))
                {
                    if (showError)
                    {
                        ShowAutoStartError(taskError);
                    }
                    return false;
                }

                if (requestedPlan.RegistryRunEnabled)
                {
                    key.SetValue(AutoStartManager.RunValueName, AutoStartManager.BuildRunCommand(exePath));
                }
                else
                {
                    key.DeleteValue(AutoStartManager.RunValueName, false);
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to apply auto-start settings: {ex.Message}");
                string details = ex.Message;
                if (scheduledTaskChanged)
                {
                    if (!AutoStartManager.TrySetScheduledTask(exePath, previousPlan.ScheduledTaskEnabled, out string taskRollbackError))
                    {
                        details += $" Task rollback failed: {taskRollbackError}";
                    }
                }

                try
                {
                    using RegistryKey? rollbackKey = Registry.CurrentUser.CreateSubKey(regKeyPath, true);
                    if (rollbackKey == null)
                    {
                        throw new InvalidOperationException("The auto-start registry key could not be reopened for rollback.");
                    }

                    if (previousPlan.RegistryRunEnabled)
                    {
                        rollbackKey.SetValue(AutoStartManager.RunValueName, AutoStartManager.BuildRunCommand(exePath));
                    }
                    else
                    {
                        rollbackKey.DeleteValue(AutoStartManager.RunValueName, false);
                    }
                }
                catch (Exception rollbackEx)
                {
                    details += $" Registry rollback failed: {rollbackEx.Message}";
                }

                if (showError)
                {
                    ShowAutoStartError(details);
                }
                return false;
            }
        }

        private void ShowAutoStartError(string details)
        {
            System.Windows.MessageBox.Show(
                this,
                Localization.Format("Msg_AutoStartUpdateFailed", details),
                Localization.Get("Msg_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void RestartAsAdmin()
        {
            if (System.Windows.Application.Current is App app)
            {
                app.RestartApplicationFromPanel();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ConfigManager.Save("TotalClicks", ConfigManager.TotalClicks);
            base.OnClosing(e);
        }

        private void EffectSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
        }

        private void CurveDraw_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
        }

        private void LinkedAnimationSpeed_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _suspendLinkedAnimationUiHandlers)
            {
                return;
            }

            bool linked = CheckLinkedAnimationSpeed.IsChecked == true;
            if (linked)
            {
                double avg = Math.Round((SliderTrailAnimSpeed.Value + SliderClickAnimSpeed.Value) / 2.0, 2);
                avg = Math.Clamp(avg, 0.2, 3.0);
                SliderSpeed.Value = avg;
            }
            else
            {
                double v = Math.Round(SliderSpeed.Value, 2);
                v = Math.Clamp(v, 0.2, 3.0);
                SliderTrailAnimSpeed.Value = v;
                SliderClickAnimSpeed.Value = v;
            }

            UpdateAnimationSpeedPanelVisibility();
        }

        private void UpdateAnimationSpeedPanelVisibility()
        {
            bool linked = CheckLinkedAnimationSpeed.IsChecked == true;
            PanelUnifiedAnimationSpeed.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            PanelSplitAnimationSpeed.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
        }

        private void GetUiAnimationSpeeds(out double trailSpeed, out double clickSpeed)
        {
            if (CheckLinkedAnimationSpeed.IsChecked == true)
            {
                double v = Math.Round(SliderSpeed.Value, 2);
                trailSpeed = v;
                clickSpeed = v;
            }
            else
            {
                trailSpeed = Math.Round(SliderTrailAnimSpeed.Value, 2);
                clickSpeed = Math.Round(SliderClickAnimSpeed.Value, 2);
            }
        }

        private void ResetConfig_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                Localization.Get("Msg_ConfirmReset"),
                Localization.Get("Msg_ConfirmReset_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    ConfigManager.ResetAndClear();
                    System.Windows.Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(Localization.Format("Msg_DeleteFailed", ex.Message));
                }
            }
        }
    }
}
