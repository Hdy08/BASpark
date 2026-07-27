using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace BASpark
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CURSORINFO pci);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int CURSOR_SHOWING = 0x00000001;
        private const uint EVENT_OBJECT_REORDER = 0x8004;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private readonly string _screenDeviceName;
        private readonly Rectangle _screenBounds;
        private IntPtr _hwnd;
        private string? _lastReportedInputMode;
        private bool? _lastReportedAlwaysTrail;
        private const string InputModeMouse = "mouse";
        private const string InputModeTouch = "touch";

        private System.Windows.Threading.DispatcherTimer? _topmostTimer;
        private EventHandler<CoreWebView2NavigationCompletedEventArgs>? _navigationCompletedHandler;
        private EventHandler<CoreWebView2ProcessFailedEventArgs>? _processFailedHandler;
        private CoreWebView2? _coreWebView;
        private WinEventDelegate? _winEventDelegate;
        private IntPtr _winEventHook = IntPtr.Zero;
        private long _lastEnsureTopmostTicks;
        private bool _isClosing;
        // WebView2's WPF HwndHost cannot be parented reliably while a UIAccess
        // window is already topmost. Raise the overlay only after navigation
        // has completed successfully.
        private bool _webViewReadyForTopmost;
        private bool _screenshotCompatibilityMode = ConfigManager.ScreenshotCompatibilityMode;
        private static readonly long EnsureTopmostDebounceTicks = TimeSpan.FromMilliseconds(80).Ticks;
        private bool _hiddenForExternalScreenshotCapture;
        private bool _hiddenByEnvironmentSuppression;
        private bool _overlayRuntimePaused;
        private bool _webViewRecoveryPending;
        private int _trailRefreshRate = 60;
        private long _trailMoveIntervalTimestamp = Math.Max(
            1,
            System.Diagnostics.Stopwatch.Frequency / 60);

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        public MainWindow(Screen screen, int trailRefreshRate)
        {
            _screenDeviceName = screen.DeviceName;
            _screenBounds = screen.Bounds;
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            InitializeComponent();
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            UpdateTrailRefreshRate(trailRefreshRate);
            _ = InitWebView();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;

            int style = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
            ApplyScreenshotCompatibilityMode();

            UpdateOverlayBounds();
            InitRealtimeTopmostHook();

            InitTopmostSentinel();
        }

        private void InitRealtimeTopmostHook()
        {
            _winEventDelegate = WinEventProc;
            _winEventHook = SetWinEventHook(
                EVENT_OBJECT_REORDER,
                EVENT_OBJECT_REORDER,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            _ = hWinEventHook;
            _ = eventType;
            _ = hwnd;
            _ = idObject;
            _ = idChild;
            _ = dwEventThread;
            _ = dwmsEventTime;
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - _lastEnsureTopmostTicks < EnsureTopmostDebounceTicks)
            {
                return;
            }
            _lastEnsureTopmostTicks = nowTicks;
            Dispatcher.BeginInvoke(new Action(SafeEnsureTopmost));
        }

        private void InitTopmostSentinel()
        {
            SafeEnsureTopmost();

            _topmostTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _topmostTimer.Tick += (s, e) => SafeEnsureTopmost();
            _topmostTimer.Start();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            SafeEnsureTopmost();
        }

        private void SafeEnsureTopmost()
        {
            if (_hwnd == IntPtr.Zero || !IsVisible || _overlayRuntimePaused || !_webViewReadyForTopmost) return;

            Rectangle bounds = GetScreenBounds();
            SetWindowPos(_hwnd, HWND_TOPMOST,
                bounds.Left,
                bounds.Top - 1,
                bounds.Width,
                bounds.Height,
                SWP_NOACTIVATE | SWP_NOSENDCHANGING);
        }

        public void UpdateColor(string color)
        {
            string colorJson = JsonSerializer.Serialize(color ?? string.Empty);
            ExecuteScript($"if(window.updateColor) window.updateColor({colorJson});");
        }

        public void UpdateEffectSettings(double scale, double opacity, double trailSpeed, double clickSpeed, double trailThickness, double trailDelay, double glowIntensity)
        {
            string scaleStr = FormatScriptNumber(scale, 0.5, 3.0, 1.0);
            string opacityStr = FormatScriptNumber(opacity, 0.1, 1.0, 1.0);
            string trailStr = FormatScriptNumber(trailSpeed, 0.2, 3.0, 1.0);
            string clickStr = FormatScriptNumber(clickSpeed, 0.2, 3.0, 1.0);
            string trailThicknessStr = FormatScriptNumber(trailThickness, 0.5, 3.0, 1.0);
            string trailDelayStr = FormatScriptNumber(trailDelay, 0.0, 2.0, 1.0);
            string glowIntensityStr = FormatScriptNumber(glowIntensity, 0.0, 3.0, 1.0);

            ExecuteScript($"if(window.updateEffectSettings) window.updateEffectSettings({scaleStr}, {opacityStr}, {trailStr}, {clickStr}, {trailThicknessStr}, {trailDelayStr}, {glowIntensityStr});");
        }

        private static string FormatScriptNumber(double value, double min, double max, double fallback)
        {
            double safeValue = double.IsFinite(value) ? value : fallback;
            safeValue = Math.Clamp(safeValue, min, max);
            return safeValue.ToString("F2", CultureInfo.InvariantCulture);
        }

        public void UpdateTrailRefreshRate(int hz)
        {
            _trailRefreshRate = Math.Clamp(hz, 30, 360);
            _trailMoveIntervalTimestamp = Math.Max(
                1,
                System.Diagnostics.Stopwatch.Frequency / _trailRefreshRate);
            ExecuteScript($"if(window.updateTrailRefreshRate) window.updateTrailRefreshRate({_trailRefreshRate});");
        }

        public void UpdateTouchMode(bool enabled)
        {
            ConfigManager.IsTouchscreenMode = enabled;
        }

        public void UpdateScreenshotCompatibilityMode(bool enabled)
        {
            _screenshotCompatibilityMode = enabled;
            ApplyScreenshotCompatibilityMode();
        }

        /// 截图工具框选窗口期间暂时隐藏叠加层
        public void SetHiddenForExternalScreenshotCapture(bool hidden)
        {
            if (_hiddenForExternalScreenshotCapture == hidden)
            {
                return;
            }

            _hiddenForExternalScreenshotCapture = hidden;
            SyncOverlayPresentationState();
        }

        /// 环境过滤时隐藏叠加层并截断当前输入轨迹；已有动画继续按时间自然结束。
        public void SetEnvironmentSuppressed(bool suppressed)
        {
            if (_hiddenByEnvironmentSuppression == suppressed)
            {
                return;
            }

            if (suppressed)
            {
                ExecuteScript("if(window.truncateTrail) window.truncateTrail();");
            }

            _hiddenByEnvironmentSuppression = suppressed;
            SyncOverlayPresentationState();
            if (!suppressed)
            {
                ExecuteScript("if(window.scheduleNextAnimationFrame) window.scheduleNextAnimationFrame();");
            }
        }

        private bool ShouldOverlayBeVisible =>
            !_hiddenForExternalScreenshotCapture && !_hiddenByEnvironmentSuppression;

        private bool ShouldPauseOverlayRuntime => _hiddenForExternalScreenshotCapture;

        private void SyncOverlayPresentationState()
        {
            if (ShouldOverlayBeVisible)
            {
                if (!IsVisible)
                {
                    Show();
                    ApplyScreenshotCompatibilityMode();
                }

                ResumeOverlayRuntime();
            }
            else
            {
                if (ShouldPauseOverlayRuntime)
                {
                    PauseOverlayRuntime();
                }
                else
                {
                    ResumeOverlayRuntime();
                }

                if (IsVisible)
                {
                    Hide();
                }
            }
        }

        private void PauseOverlayRuntime()
        {
            if (_overlayRuntimePaused)
            {
                return;
            }

            _overlayRuntimePaused = true;
            PauseTopmostMonitoring();
            ExecuteScript("if(window.setRenderingPaused) window.setRenderingPaused(true);");
            _ = TrySuspendWebViewAsync();
        }

        private void ResumeOverlayRuntime()
        {
            if (!_overlayRuntimePaused)
            {
                return;
            }

            _overlayRuntimePaused = false;
            if (TryGetCoreWebView2(out CoreWebView2? coreWebView))
            {
                try
                {
                    coreWebView.Resume();
                }
                catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
                {
                }
            }

            ExecuteScript("if(window.setRenderingPaused) window.setRenderingPaused(false);");
            ResumeTopmostMonitoring();
        }

        private async System.Threading.Tasks.Task TrySuspendWebViewAsync()
        {
            if (!TryGetCoreWebView2(out CoreWebView2? coreWebView))
            {
                return;
            }

            try
            {
                bool suspended = await coreWebView.TrySuspendAsync().ConfigureAwait(true);
                if (suspended && !_overlayRuntimePaused)
                {
                    coreWebView.Resume();
                }
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
            }
        }

        private void PauseTopmostMonitoring()
        {
            if (_topmostTimer != null)
            {
                _topmostTimer.Stop();
            }

            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
        }

        private void ResumeTopmostMonitoring()
        {
            if (_hwnd == IntPtr.Zero || !IsVisible)
            {
                return;
            }

            if (_winEventHook == IntPtr.Zero)
            {
                InitRealtimeTopmostHook();
            }

            if (_topmostTimer == null)
            {
                InitTopmostSentinel();
            }
            else if (!_topmostTimer.IsEnabled)
            {
                _topmostTimer.Start();
            }

            SafeEnsureTopmost();
        }

        private void ApplyScreenshotCompatibilityMode()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            // 把特效窗口从系统捕获结果中排除
            uint affinity = _screenshotCompatibilityMode ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
            if (!SetWindowDisplayAffinity(_hwnd, affinity) && _screenshotCompatibilityMode)
            {
                SetWindowDisplayAffinity(_hwnd, WDA_MONITOR);
            }

            SafeEnsureTopmost();
        }

        public IntPtr Handle => _hwnd;

        private async System.Threading.Tasks.Task<bool> InitWebView(bool reportFailure = true)
        {
            try
            {
                var env = await WebView2EnvironmentHolder.GetOrCreateAsync().ConfigureAwait(true);
                if (_isClosing) return false;

                if (webView.CoreWebView2 == null)
                {
                    try
                    {
                        await webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                    }
                    catch (ArgumentException ex) when (IsWebViewEnvironmentConflictException(ex))
                    {
                        AppLogger.Debug($"WebView2 environment conflict ignored: {ex.Message}");
                        if (webView.CoreWebView2 == null)
                        {
                            throw;
                        }
                    }
                }

                if (_isClosing || !TryGetCoreWebView2(out CoreWebView2? coreWebView)) return false;

                _coreWebView = coreWebView;
                coreWebView.Settings.IsZoomControlEnabled = false;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.IsStatusBarEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;
                coreWebView.Settings.AreBrowserAcceleratorKeysEnabled = false;
                coreWebView.Settings.IsWebMessageEnabled = false;
                coreWebView.Settings.AreHostObjectsAllowed = false;
                coreWebView.Settings.IsPasswordAutosaveEnabled = false;
                coreWebView.Settings.IsGeneralAutofillEnabled = false;

                if (_processFailedHandler == null)
                {
                    _processFailedHandler = OnWebViewProcessFailed;
                    coreWebView.ProcessFailed += _processFailedHandler;
                }

                if (_navigationCompletedHandler != null)
                {
                    try { coreWebView.NavigationCompleted -= _navigationCompletedHandler; } catch { /* best-effort */ }
                }

                var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Web/index.html"));
                if (streamInfo == null)
                {
                    throw new InvalidOperationException("The embedded WebView2 content could not be loaded.");
                }

                using var reader = new System.IO.StreamReader(streamInfo.Stream);
                string htmlContent = reader.ReadToEnd();
                htmlContent = InjectTouchEffectAssets(htmlContent);
                var navigationCompletion = new System.Threading.Tasks.TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
                    System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                _navigationCompletedHandler = (s, e) =>
                {
                    navigationCompletion.TrySetResult(e);
                    if (_isClosing) return;

                    if (!e.IsSuccess)
                    {
                        _webViewReadyForTopmost = false;
                        if (!_webViewRecoveryPending)
                        {
                            Dispatcher.BeginInvoke(new Action(() => _ = RecreateWebViewAsync()));
                        }
                        return;
                    }

                    _webViewReadyForTopmost = true;
                    SafeEnsureTopmost();

                    _lastReportedInputMode = null;
                    _lastReportedAlwaysTrail = null;
                    UpdateColor(ConfigManager.ParticleColor);
                    ConfigManager.GetAnimationSpeedsForOverlay(out double trailSp, out double clickSp);
                    UpdateEffectSettings(ConfigManager.EffectScale, ConfigManager.EffectOpacity, trailSp, clickSp, ConfigManager.TrailThickness, ConfigManager.TrailDelay, ConfigManager.GlowIntensity);
                    UpdateTrailRefreshRate(_trailRefreshRate);
                    SyncInputContext(InputModeMouse);
                    if (_overlayRuntimePaused)
                    {
                        ExecuteScript("if(window.setRenderingPaused) window.setRenderingPaused(true);");
                        _ = TrySuspendWebViewAsync();
                    }
                };
                coreWebView.NavigationCompleted += _navigationCompletedHandler;
                coreWebView.NavigateToString(htmlContent);

                CoreWebView2NavigationCompletedEventArgs navigationResult = await navigationCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
                if (!navigationResult.IsSuccess)
                {
                    throw new InvalidOperationException($"WebView2 navigation failed: {navigationResult.WebErrorStatus}.");
                }

                return true;
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
                return false;
            }
            catch (Exception ex)
            {
                if (_isClosing)
                {
                    return false;
                }

                if (reportFailure)
                {
                    System.Windows.MessageBox.Show(Localization.Format("WebView2_InitFailed", ex.Message));
                }
                else
                {
                    AppLogger.Warn($"WebView2 recovery initialization failed: {ex.Message}");
                }
                return false;
            }
        }

        private static string InjectTouchEffectAssets(string htmlContent)
        {
            const string assetBootstrapMarker = "<!-- BASPARK_ASSET_BOOTSTRAP -->";
            if (!htmlContent.Contains(assetBootstrapMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The touch effect asset bootstrap marker is missing.");
            }

            var assets = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["circle"] = ReadEmbeddedAssetDataUrl("Web/Assets/FX_TEX_Circle_01.png"),
                ["ring"] = ReadEmbeddedAssetDataUrl("Web/Assets/FX_TEX_Grad_Ring3.png"),
                ["trail"] = ReadEmbeddedAssetDataUrl("Web/Assets/FX_TEX_Trail_03.png"),
                ["triangle"] = ReadEmbeddedAssetDataUrl("Web/Assets/FX_TEX_Triangle_02_1.png")
            };

            string bootstrap = $"<script>window.__BASPARK_ASSETS={JsonSerializer.Serialize(assets)};</script>";
            return htmlContent.Replace(assetBootstrapMarker, bootstrap, StringComparison.Ordinal);
        }

        private static string ReadEmbeddedAssetDataUrl(string resourcePath)
        {
            var streamInfo = System.Windows.Application.GetResourceStream(
                new Uri($"pack://application:,,,/BASpark;component/{resourcePath}", UriKind.Absolute));
            if (streamInfo == null)
            {
                throw new InvalidOperationException($"The embedded touch effect asset '{resourcePath}' could not be loaded.");
            }

            using var source = streamInfo.Stream;
            using var memory = new System.IO.MemoryStream();
            source.CopyTo(memory);
            return $"data:image/png;base64,{Convert.ToBase64String(memory.ToArray())}";
        }

        private static bool IsWebViewEnvironmentConflictException(Exception ex)
        {
            return ex is ArgumentException &&
                   ex.Message.Contains("CoreWebView2Environment", StringComparison.OrdinalIgnoreCase);
        }

        private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (_isClosing) return;

            AppLogger.Warn($"WebView2 process failure: {e.ProcessFailedKind} ({e.Reason}).");
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RecreateWebViewAsync()));
                return;
            }

            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_isClosing || _webViewRecoveryPending) return;
                    _webViewReadyForTopmost = false;
                    Topmost = false;
                    try
                    {
                        _coreWebView?.Reload();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"WebView2 reload failed; recreating the control: {ex.Message}");
                        _ = RecreateWebViewAsync();
                    }
                }));
            }
        }

        private async System.Threading.Tasks.Task RecreateWebViewAsync()
        {
            if (_isClosing || _webViewRecoveryPending)
            {
                return;
            }

            _webViewRecoveryPending = true;
            try
            {
                for (int attempt = 1; attempt <= 3 && !_isClosing; attempt++)
                {
                    if (attempt > 1)
                    {
                        await System.Threading.Tasks.Task.Delay(250 * attempt).ConfigureAwait(true);
                    }

                    if (_isClosing)
                    {
                        return;
                    }

                    ReplaceWebViewControl();
                    if (await InitWebView(reportFailure: false).ConfigureAwait(true))
                    {
                        return;
                    }

                    AppLogger.Warn($"WebView2 recovery attempt {attempt} failed.");
                }
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to recreate WebView2 after browser process exit.", ex);
            }
            finally
            {
                _webViewRecoveryPending = false;
            }
        }

        private void ReplaceWebViewControl()
        {
            _webViewReadyForTopmost = false;
            Topmost = false;
            DetachWebViewHandlers();

            var oldWebView = webView;
            int childIndex = OverlayRoot.Children.IndexOf(oldWebView);
            OverlayRoot.Children.Remove(oldWebView);
            oldWebView.Dispose();

            webView = new Microsoft.Web.WebView2.Wpf.WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Transparent
            };
            OverlayRoot.Children.Insert(Math.Max(0, childIndex), webView);
        }

        private void DetachWebViewHandlers()
        {
            CoreWebView2? coreWebView = _coreWebView;
            if (coreWebView != null && _navigationCompletedHandler != null)
            {
                try { coreWebView.NavigationCompleted -= _navigationCompletedHandler; }
                catch (Exception ex) { AppLogger.Debug($"Failed to detach WebView2 navigation handler: {ex.Message}"); }
            }

            if (coreWebView != null && _processFailedHandler != null)
            {
                try { coreWebView.ProcessFailed -= _processFailedHandler; }
                catch (Exception ex) { AppLogger.Debug($"Failed to detach WebView2 process handler: {ex.Message}"); }
            }

            _navigationCompletedHandler = null;
            _processFailedHandler = null;
            _coreWebView = null;
        }

        private static bool IsCursorVisible()
        {
            CURSORINFO pci = new CURSORINFO();
            pci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
            if (GetCursorInfo(out pci))
            {
                return (pci.flags & CURSOR_SHOWING) != 0;
            }
            return true;
        }

        private string BuildInputContextScript(string inputMode)
        {
            bool alwaysTrailEnabled = ConfigManager.EnableAlwaysTrailEffect;
            if (_lastReportedInputMode == inputMode && _lastReportedAlwaysTrail == alwaysTrailEnabled)
            {
                return string.Empty;
            }

            _lastReportedInputMode = inputMode;
            _lastReportedAlwaysTrail = alwaysTrailEnabled;
            string inputModeJson = JsonSerializer.Serialize(inputMode);
            string alwaysTrailLiteral = alwaysTrailEnabled ? "true" : "false";
            return $"if(window.setInputContext) window.setInputContext({inputModeJson}, {alwaysTrailLiteral});";
        }

        private void SyncInputContext(string inputMode)
        {
            if (!TryGetCoreWebView2(out CoreWebView2? coreWebView)) return;

            string script = BuildInputContextScript(inputMode);
            if (string.IsNullOrEmpty(script)) return;

            ExecuteScript(coreWebView, script);
        }

        private void ExecuteWithInputContext(string inputMode, string actionScript)
        {
            if (!TryGetCoreWebView2(out CoreWebView2? coreWebView)) return;

            string contextScript = BuildInputContextScript(inputMode);
            ExecuteScript(coreWebView, contextScript + actionScript);
        }

        // 统一 JS 脚本执行入口
        private void ExecuteScript(string script)
        {
            if (string.IsNullOrEmpty(script) || !TryGetCoreWebView2(out CoreWebView2? coreWebView))
            {
                return;
            }

            ExecuteScript(coreWebView, script);
        }

        private void ExecuteScript(CoreWebView2 coreWebView, string script)
        {
            if (string.IsNullOrEmpty(script))
            {
                return;
            }

            try
            {
                _ = coreWebView.ExecuteScriptAsync(script);
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
            }
        }

        private bool TryGetCoreWebView2([NotNullWhen(true)] out CoreWebView2? coreWebView)
        {
            coreWebView = null;
            if (_isClosing)
            {
                return false;
            }

            try
            {
                coreWebView = _coreWebView ?? webView?.CoreWebView2;
                return coreWebView != null;
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
                return false;
            }
        }

        private bool IsExpectedWebViewShutdownException(Exception ex)
        {
            return _isClosing ||
                   ex is ObjectDisposedException ||
                   (ex is InvalidOperationException &&
                    ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase));
        }

        public bool ContainsScreenPoint(int x, int y)
        {
            return _screenBounds.Contains(x, y);
        }

        public void EmitDown(int x, int y)
        {
            if (!TryConvertScreenToOverlayPoint(x, y, out System.Windows.Point clientPoint)) return;
            bool touchLike = !IsCursorVisible();
            string inputMode = touchLike ? InputModeTouch : InputModeMouse;
            string px = FormatCoordinate(clientPoint.X);
            string py = FormatCoordinate(clientPoint.Y);
            ExecuteWithInputContext(inputMode, $"if(window.externalBoom) window.externalBoom({px}, {py});");
        }

        public void EmitMove(int x, int y, bool touchLike)
        {
            if (!TryConvertScreenToOverlayPoint(x, y, out System.Windows.Point clientPoint)) return;
            string inputMode = touchLike ? InputModeTouch : InputModeMouse;
            string px = FormatCoordinate(clientPoint.X);
            string py = FormatCoordinate(clientPoint.Y);
            ExecuteWithInputContext(inputMode, $"if(window.externalMove) window.externalMove({px}, {py});");
        }

        public void EmitTrailStart(int x, int y, bool touchLike, bool pointerDown)
        {
            if (!TryConvertScreenToOverlayPoint(x, y, out System.Windows.Point clientPoint)) return;
            string inputMode = touchLike ? InputModeTouch : InputModeMouse;
            string px = FormatCoordinate(clientPoint.X);
            string py = FormatCoordinate(clientPoint.Y);
            string pointerDownLiteral = pointerDown ? "true" : "false";
            ExecuteWithInputContext(inputMode, $"if(window.externalTrailStart) window.externalTrailStart({px}, {py}, {pointerDownLiteral});");
        }

        public void EmitUp(bool touchLike)
        {
            string inputMode = touchLike ? InputModeTouch : InputModeMouse;
            ExecuteWithInputContext(inputMode, "if(window.externalUp) window.externalUp();");
        }

        private void UpdateOverlayBounds()
        {
            Rectangle bounds = GetScreenBounds();
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = bounds.Left / dpi.DpiScaleX;
            Top = (bounds.Top - 1) / dpi.DpiScaleY;
            Width = bounds.Width / dpi.DpiScaleX;
            Height = bounds.Height / dpi.DpiScaleY;

            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top - 1,
                bounds.Width,
                bounds.Height,
                SWP_NOACTIVATE | SWP_NOZORDER);
        }

        public string ScreenDeviceName => _screenDeviceName;
        public long TrailMoveIntervalTimestamp => _trailMoveIntervalTimestamp;

        private Rectangle GetScreenBounds()
        {
            Screen? current = Screen.AllScreens.FirstOrDefault(s =>
                string.Equals(s.DeviceName, _screenDeviceName, StringComparison.OrdinalIgnoreCase));
            return current?.Bounds ?? _screenBounds;
        }

        private static string FormatCoordinate(double value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private bool TryConvertScreenToOverlayPoint(int screenX, int screenY, out System.Windows.Point percentPoint)
        {
            percentPoint = default;
            try
            {
                if (!GetWindowRect(_hwnd, out RECT rect)) return false;

                double physWidth = rect.Right - rect.Left;
                double physHeight = rect.Bottom - rect.Top;
                if (physWidth <= 0 || physHeight <= 0) return false;

                double percentX = (screenX - rect.Left) / physWidth;
                double percentY = (screenY - rect.Top) / physHeight;

                percentPoint = new System.Windows.Point(
                    Math.Clamp(percentX, 0.0, 1.0),
                    Math.Clamp(percentY, 0.0, 1.0)
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosing = true;

            if (_topmostTimer != null)
            {
                _topmostTimer.Stop();
                _topmostTimer = null;
            }

            DetachWebViewHandlers();

            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            _winEventDelegate = null;
            _hwnd = IntPtr.Zero;

            webView?.Dispose();
            base.OnClosed(e);
        }
    }
}
