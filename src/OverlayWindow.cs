using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace BASpark;

/// <summary>
/// 单个显示器上的特效叠加层。
///
/// 迁移说明：本类原先继承 WPF <c>Window</c> 并依赖 <c>AllowsTransparency</c>。
/// WinUI 3 无法创建透明顶层窗口，因此改为在 <see cref="LayeredWindowHost"/>
/// 提供的原生分层窗口上直接挂载 WebView2 的 Win32 控制器。
///
/// 渲染链路（HTML、fx-adapter.js、ba-click-fx、注入脚本协议）与迁移前完全一致，
/// 本类只负责宿主侧的窗口、生命周期与脚本转发。
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    private readonly string _screenDeviceName;
    private readonly NativeMethods.RECT _screenBounds;
    private readonly LayeredWindowHost _host;

    private CoreWebView2Controller? _controller;
    private CoreWebView2? _coreWebView;

    private long _trailMoveIntervalTimestamp = Math.Max(1, Stopwatch.Frequency / 60);
    private string? _lastReportedInputMode;
    private bool? _lastReportedAlwaysTrail;

    private const string InputModeMouse = "mouse";
    private const string InputModeTouch = "touch";

    private DispatcherQueueTimer? _topmostTimer;
    private DispatcherQueueTimer? _rendererReadyTimeoutTimer;
    // WinRT 投影暴露的是 TypedEventHandler<TSender, TArgs>，不是 .NET 的 EventHandler<TArgs>。
    private Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>? _navigationStartingHandler;
    private Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? _navigationCompletedHandler;
    private Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs>? _processFailedHandler;
    private Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>? _webMessageReceivedHandler;

    private ulong? _currentNavigationId;
    private string? _rendererGeneration;
    private NativeMethods.WinEventProc? _winEventDelegate;
    private IntPtr _winEventHook = IntPtr.Zero;
    private long _lastEnsureTopmostTicks;
    private bool _isClosing;

    // WebView2 在窗口尚未置顶时完成初始化更稳定；导航成功后才把叠加层抬到最前。
    private bool _webViewReadyForTopmost;
    private bool _screenshotCompatibilityMode = ConfigManager.ScreenshotCompatibilityMode;

    private static readonly long EnsureTopmostDebounceTicks = TimeSpan.FromMilliseconds(80).Ticks;

    private bool _hiddenForExternalScreenshotCapture;
    private bool _environmentInputSuppressed;
    private bool _overlayRuntimePaused;
    private bool _rendererReady;
    private bool _usingLegacyRenderer;
    private bool _legacyFallbackAttempted;
    private bool _processRecoveryPending;
    private bool _controllerInitialized;
    private readonly WebViewUnresponsiveTracker _unresponsiveTracker = new();

    internal OverlayWindow(ScreenInfo screen, int trailRefreshRate)
    {
        _screenDeviceName = screen.DeviceName;
        _screenBounds = new NativeMethods.RECT
        {
            Left = screen.BoundsLeft,
            Top = screen.BoundsTop,
            Right = screen.BoundsLeft + screen.BoundsWidth,
            Bottom = screen.BoundsTop + screen.BoundsHeight
        };

        _host = new LayeredWindowHost();
        UpdateTrailRefreshRate(trailRefreshRate);
        UpdateRendererBounds();

        InitRealtimeTopmostHook();
        InitTopmostSentinel();

        _ = InitWebViewAsync();
    }

    public IntPtr Handle => _host.Handle;

    public string ScreenDeviceName => _screenDeviceName;

    public long TrailMoveIntervalTimestamp => _trailMoveIntervalTimestamp;

    // ------------------------------------------------------------------
    // 窗口几何
    // ------------------------------------------------------------------

    /// <summary>按物理像素把宿主窗口铺满目标显示器。</summary>
    private void UpdateRendererBounds()
    {
        NativeMethods.RECT bounds = GetScreenBounds();
        _host.SetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

        if (_controller != null && _controllerInitialized)
        {
            try
            {
                _controller.RasterizationScale = _host.DpiScale;
                _controller.Bounds = new Windows.Foundation.Rect(0, 0, bounds.Width, bounds.Height);
                _controller.NotifyParentWindowPositionChanged();
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
            }
        }
    }

    private NativeMethods.RECT GetScreenBounds()
    {
        ScreenInfo? current = ScreenInfo.AllScreens.FirstOrDefault(s =>
            string.Equals(s.DeviceName, _screenDeviceName, StringComparison.OrdinalIgnoreCase));

        if (current == null)
        {
            return _screenBounds;
        }

        return new NativeMethods.RECT
        {
            Left = current.BoundsLeft,
            Top = current.BoundsTop,
            Right = current.BoundsLeft + current.BoundsWidth,
            Bottom = current.BoundsTop + current.BoundsHeight
        };
    }

    public bool ContainsScreenPoint(int x, int y)
    {
        NativeMethods.RECT bounds = GetScreenBounds();
        return x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
    }

    // ------------------------------------------------------------------
    // 置顶维护
    // ------------------------------------------------------------------

    private void InitRealtimeTopmostHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            return;
        }

        _winEventDelegate = WinEventProc;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_REORDER,
            NativeMethods.EVENT_OBJECT_REORDER,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
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
        App.DispatcherQueue.TryEnqueue(SafeEnsureTopmost);
    }

    private void InitTopmostSentinel()
    {
        SafeEnsureTopmost();

        _topmostTimer = App.DispatcherQueue.CreateTimer();
        _topmostTimer.Interval = TimeSpan.FromSeconds(5);
        _topmostTimer.Tick += (_, _) => SafeEnsureTopmost();
        _topmostTimer.Start();
    }

    private void SafeEnsureTopmost()
    {
        if (_host.Handle == IntPtr.Zero || !_host.IsVisible
            || _overlayRuntimePaused || !_webViewReadyForTopmost)
        {
            return;
        }

        NativeMethods.RECT bounds = GetScreenBounds();
        _host.SetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        _host.SetTopmost();
    }

    // ------------------------------------------------------------------
    // 渲染器初始化
    // ------------------------------------------------------------------

    // 多显示器会并发初始化多个叠加层。控制器创建失败时需要重建环境，但重建是
    // 进程级的：另一个叠加层可能刚好也在重建。因此这里除了换目录再试，还要在
    // 环境被他人替换后重新取用新环境，否则会拿着已作废的环境反复失败。
    private const int ControllerAttachAttempts = 3;

    private async Task InitWebViewAsync()
    {
        try
        {
            CoreWebView2Environment env = await WebView2EnvironmentHolder.GetOrCreateAsync();
            if (_isClosing)
            {
                return;
            }

            _host.Show();

            bool attached = false;
            bool resetRequested = false;

            for (int attempt = 1; attempt <= ControllerAttachAttempts && !attached; attempt++)
            {
                if (attempt > 1)
                {
                    // 等待并发重建结束，并取用当前有效环境（可能已被另一个叠加层替换）。
                    env = await WebView2EnvironmentHolder.GetOrCreateAsync().ConfigureAwait(true);
                    if (_isClosing)
                    {
                        return;
                    }
                }

                attached = await TryAttachControllerAsync(env).ConfigureAwait(true);
                if (attached || _isClosing)
                {
                    break;
                }

                if (resetRequested)
                {
                    continue;
                }

                // 只在首次失败时重建环境，避免多屏之间反复互踩。
                resetRequested = true;
                AppLogger.Warn(
                    $"WebView2 controller creation failed on '{_screenDeviceName}'; " +
                    "recreating the shared environment with a fresh user data folder.");

                try
                {
                    env = await WebView2EnvironmentHolder
                        .ResetWithFreshUserDataFolderAsync()
                        .ConfigureAwait(true);
                }
                catch (Exception ex) when (!IsExpectedWebViewShutdownException(ex))
                {
                    AppLogger.Warn(
                        $"Recreating the WebView2 environment failed on '{_screenDeviceName}': " +
                        $"{ex.GetType().Name}: {ex.Message}");
                    break;
                }
            }

            if (!attached)
            {
                if (_isClosing)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"WebView2 控制器创建失败：已尝试 {ControllerAttachAttempts} 次（含重建用户数据目录）。");
            }

            if (_isClosing)
            {
                return;
            }

            _host.TryGetWindowRect(out NativeMethods.RECT hostRect);
            AppLogger.Debug(
                $"[overlay:{_screenDeviceName}] controller ready " +
                $"(hwnd=0x{_host.Handle.ToInt64():X}, host={hostRect.Width}x{hostRect.Height}, " +
                $"dpiScale={_host.DpiScale.ToString("F3", CultureInfo.InvariantCulture)}, " +
                $"runtime={env.BrowserVersionString})");

            ConfigureController(env);
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"显示器 '{_screenDeviceName}' 上的叠加层初始化失败。" +
                $" (hwnd=0x{_host.Handle.ToInt64():X}, " +
                $"hwndValid={NativeMethods.IsWindow(_host.Handle)}, " +
                $"userData={WebView2EnvironmentHolder.UserDataFolder}, " +
                $"hresult=0x{ex.HResult:X8})",
                ex);
            App.ReportFatalWebViewFailure(Localization.Format("WebView2_InitFailed", ex.Message));
        }
    }

    /// <summary>
    /// 尝试在宿主窗口上挂载 WebView2 控制器。失败时记录完整上下文并返回 false，
    /// 由调用方决定是否换用户数据目录重试。
    /// </summary>
    private async Task<bool> TryAttachControllerAsync(CoreWebView2Environment env)
    {
        try
        {
            CoreWebView2Controller controller = await env.CreateCoreWebView2ControllerAsync(
                CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)_host.Handle));

            if (_isClosing)
            {
                try
                {
                    controller.Close();
                }
                catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
                {
                }

                return false;
            }

            _controller = controller;
            _controllerInitialized = true;
            return true;
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"CreateCoreWebView2ControllerAsync failed on '{_screenDeviceName}': " +
                $"{ex.GetType().Name}: {ex.Message} (hresult=0x{ex.HResult:X8}, " +
                $"hwnd=0x{_host.Handle.ToInt64():X}, hwndValid={NativeMethods.IsWindow(_host.Handle)}, " +
                $"hostVisible={_host.IsVisible}, dpiScale={_host.DpiScale.ToString("F3", CultureInfo.InvariantCulture)}, " +
                $"userData={WebView2EnvironmentHolder.UserDataFolder})");
            return false;
        }
    }

    /// <summary>控制器挂载成功后的外观与事件装配。</summary>
    private void ConfigureController(CoreWebView2Environment env)
    {
        _ = env;

        // 关键：默认背景透明，叠加层才能真正“透出”桌面。
        _controller!.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        _controller.ShouldDetectMonitorScaleChanges = false;
        _controller.RasterizationScale = _host.DpiScale;

        NativeMethods.RECT bounds = GetScreenBounds();
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, bounds.Width, bounds.Height);
        _controller.IsVisible = true;

        CoreWebView2? coreWebView = _controller.CoreWebView2;
        if (coreWebView == null)
        {
            throw new InvalidOperationException("WebView2 控制器未提供 CoreWebView2 实例。");
        }

        DetachCoreWebViewEvents();
        _coreWebView = coreWebView;
        coreWebView.Settings.IsZoomControlEnabled = false;
        coreWebView.Settings.AreDefaultContextMenusEnabled = false;
        coreWebView.Settings.IsStatusBarEnabled = false;

        _processFailedHandler = OnWebViewProcessFailed;
        _navigationStartingHandler = OnNavigationStarting;
        _navigationCompletedHandler = OnNavigationCompleted;
        _webMessageReceivedHandler = OnWebMessageReceived;
        coreWebView.ProcessFailed += _processFailedHandler;
        coreWebView.NavigationStarting += _navigationStartingHandler;
        coreWebView.NavigationCompleted += _navigationCompletedHandler;
        coreWebView.WebMessageReceived += _webMessageReceivedHandler;

        _host.ApplyCaptureExclusion(_screenshotCompatibilityMode);

        NavigateCurrentRenderer(coreWebView);
    }

    private void NavigateCurrentRenderer(CoreWebView2 coreWebView)
    {
        StopRendererReadyTimeout();
        _rendererReady = _usingLegacyRenderer;

        if (_usingLegacyRenderer)
        {
            _rendererGeneration = null;
            string legacyHtml = WebRendererResourceProvider.ReadResourceText(
                WebRendererResourceProvider.LegacyRendererResourcePath);
            NavigateHtml(coreWebView, legacyHtml);
            return;
        }

        _rendererGeneration = null;
        try
        {
            string rendererGeneration = Guid.NewGuid().ToString("N");
            string rendererHtml = BuildPrimaryRendererHtml(rendererGeneration);
            _rendererGeneration = rendererGeneration;
            NavigateHtml(coreWebView, rendererHtml);
        }
        catch (Exception ex) when (!IsExpectedWebViewShutdownException(ex))
        {
            AppLogger.Error(
                $"Failed to prepare BA click renderer on '{_screenDeviceName}'.",
                ex);
            FallbackToLegacyRenderer("primary renderer resource preparation failed");
            return;
        }

        // 无法上报就绪的渲染器不能让叠加层长期处于不可见状态。
        if (!_rendererReady)
        {
            StartRendererReadyTimeout();
        }
    }

    private static string BuildPrimaryRendererHtml(string rendererGeneration)
    {
        string generationJson = JsonSerializer.Serialize(rendererGeneration);
        string adapterScript =
            $"window.__basparkRendererGeneration = {generationJson};\n" +
            WebRendererResourceProvider.ReadResourceText(
                WebRendererResourceProvider.RendererAdapterResourcePath);

        return WebRendererDocumentBuilder.Build(
            WebRendererResourceProvider.ReadResourceText(
                WebRendererResourceProvider.PrimaryRendererResourcePath),
            WebRendererResourceProvider.ReadResourceText(
                WebRendererResourceProvider.RendererVendorResourcePath),
            adapterScript);
    }

    private void NavigateHtml(CoreWebView2 coreWebView, string html)
    {
        // NavigationStarting 会在任何完成事件之前给出权威 ID。
        _webViewReadyForTopmost = false;
        _currentNavigationId = null;
        coreWebView.NavigateToString(html);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_isClosing || !ReferenceEquals(sender, _coreWebView))
        {
            return;
        }

        _currentNavigationId = e.NavigationId;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_isClosing ||
            !ReferenceEquals(sender, _coreWebView) ||
            _currentNavigationId != e.NavigationId)
        {
            return;
        }

        if (!e.IsSuccess)
        {
            _webViewReadyForTopmost = false;
            if (!_usingLegacyRenderer)
            {
                FallbackToLegacyRenderer($"navigation failed: {e.WebErrorStatus}");
            }
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                AppLogger.Error(
                    $"Legacy renderer navigation failed on '{_screenDeviceName}': {e.WebErrorStatus}.");
            }
            return;
        }

        _webViewReadyForTopmost = true;
        _host.Show();
        SafeEnsureTopmost();

        // 导航会重建 JS 全局对象，因此每个页面都需要重新下发完整的宿主状态。
        _lastReportedInputMode = null;
        _lastReportedAlwaysTrail = null;
        UpdateColor(ConfigManager.ParticleColor);
        ConfigManager.GetEffectScalesForOverlay(out double trailScale, out double clickScale);
        ConfigManager.GetAnimationSpeedsForOverlay(out double trailSp, out double clickSp);
        UpdateEffectSettings(trailScale, clickScale, ConfigManager.EffectOpacity, trailSp, clickSp, ConfigManager.GlowIntensity);
        SyncInputContext(InputModeMouse);
        ApplyEnvironmentInputSuppression();
        if (_overlayRuntimePaused)
        {
            ExecuteScript("if(window.setRenderingPaused) window.setRenderingPaused(true);");
            _ = TrySuspendWebViewAsync();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_isClosing || _usingLegacyRenderer || !ReferenceEquals(sender, _coreWebView))
        {
            return;
        }

        string messageJson;
        try
        {
            messageJson = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // 适配器契约是 JSON 文本，但对象消息也按防御性方式接受。
            messageJson = e.WebMessageAsJson;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(messageJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            if (!string.Equals(GetJsonString(root, "source"), "baspark-fx", StringComparison.Ordinal))
            {
                return;
            }
            if (!string.Equals(
                GetJsonString(root, "generation"),
                _rendererGeneration,
                StringComparison.Ordinal))
            {
                return;
            }

            string? type = GetJsonString(root, "type");
            string requestedEffectBackend =
                GetJsonString(root, "requestedEffectBackend") ?? "unknown";
            string resolvedEffectBackend =
                GetJsonString(root, "resolvedEffectBackend") ?? "unknown";
            string requestedBloomBackend =
                GetJsonString(root, "requestedBloomBackend") ?? "unknown";
            string resolvedBloomBackend =
                GetJsonString(root, "resolvedBloomBackend") ?? "unknown";
            string requestedHostCompositing =
                GetJsonString(root, "requestedHostCompositing") ?? "unknown";
            string resolvedHostCompositing =
                GetJsonString(root, "resolvedHostCompositing") ?? "unknown";
            string hostCompositingSurface =
                GetJsonString(root, "hostCompositingSurface") ?? "unknown";
            string? compositingWarning =
                GetJsonString(root, "compositingWarning");
            string backend = GetJsonString(root, "backend") ??
                (resolvedEffectBackend == "webgl2"
                    ? resolvedEffectBackend
                    : resolvedBloomBackend);

            if (string.Equals(type, "ready", StringComparison.Ordinal))
            {
                bool firstReadyMessage = !_rendererReady;
                _rendererReady = true;
                _unresponsiveTracker.Reset();
                StopRendererReadyTimeout();
                if (firstReadyMessage)
                {
                    AppLogger.Info(
                        $"BA click renderer ready on '{_screenDeviceName}' " +
                        $"(effective: {backend}, effect: {resolvedEffectBackend}, " +
                        $"bloom: {resolvedBloomBackend}, host: " +
                        $"{resolvedHostCompositing} on {hostCompositingSurface}" +
                        $"{FormatCompositingWarning(compositingWarning)}).");
                }
                return;
            }

            if (string.Equals(type, "backend", StringComparison.Ordinal))
            {
                AppLogger.Info(
                    $"BA click renderer backend on '{_screenDeviceName}': " +
                    $"effective {backend}; effect {resolvedEffectBackend} " +
                    $"(requested {requestedEffectBackend}); bloom {resolvedBloomBackend} " +
                    $"(requested {requestedBloomBackend}); host " +
                    $"{resolvedHostCompositing} on {hostCompositingSurface} " +
                    $"(requested {requestedHostCompositing})" +
                    $"{FormatCompositingWarning(compositingWarning)}.");
                return;
            }

            if (string.Equals(type, "error", StringComparison.Ordinal))
            {
                string phase = GetJsonString(root, "phase") ?? "unknown";
                string message = GetJsonString(root, "message") ?? "unspecified renderer error";
                AppLogger.Error(
                    $"BA click renderer error on '{_screenDeviceName}' during '{phase}': {message}.");
                FallbackToLegacyRenderer($"renderer error during '{phase}'");
            }
        }
        catch (JsonException ex)
        {
            AppLogger.Warn(
                $"Ignored malformed renderer message on '{_screenDeviceName}': {ex.Message}");
        }
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string FormatCompositingWarning(string? warning)
    {
        return string.IsNullOrWhiteSpace(warning)
            ? string.Empty
            : $"; warning: {warning}";
    }

    // ------------------------------------------------------------------
    // 渲染器就绪超时与回退
    // ------------------------------------------------------------------

    // 主渲染器需要先等 DOMContentLoaded，再解析 vendor 包并初始化 WebGL/WebGPU，
    // 低端机或首次创建用户数据目录时明显超过 2 秒。首轮给足时间，避免把「还在初始化」
    // 误判为「渲染器损坏」而白白丢掉主渲染器。
    private static readonly TimeSpan RendererReadyTimeout = TimeSpan.FromSeconds(12);

    // 超时后不直接判定失败：先向页面确认宿主 API 是否已经注入。
    private const string RendererProbeScript =
        "(function(){" +
        "  try {" +
        "    return (typeof window.externalBoom === 'function' &&" +
        "            typeof window.externalMove === 'function') ? 'ready' : 'pending';" +
        "  } catch (e) { return 'pending'; }" +
        "})()";

    private void StartRendererReadyTimeout()
    {
        StopRendererReadyTimeout();
        _rendererReadyTimeoutTimer = App.DispatcherQueue.CreateTimer();
        _rendererReadyTimeoutTimer.Interval = RendererReadyTimeout;
        _rendererReadyTimeoutTimer.Tick += OnRendererReadyTimeout;
        _rendererReadyTimeoutTimer.Start();
    }

    private void OnRendererReadyTimeout(DispatcherQueueTimer sender, object args)
    {
        if (!ReferenceEquals(sender, _rendererReadyTimeoutTimer))
        {
            return;
        }

        StopRendererReadyTimeout();
        if (_isClosing || _rendererReady || _usingLegacyRenderer)
        {
            return;
        }

        // ready 消息可能因时序原因丢失，但渲染器本身已经可用。此时回退到 legacy
        // 只会让特效质量下降，因此先探测宿主 API 再决定。
        _ = ProbeRendererBeforeFallbackAsync();
    }

    private async Task ProbeRendererBeforeFallbackAsync()
    {
        CoreWebView2? coreWebView = _coreWebView;
        if (coreWebView == null || _isClosing || _rendererReady || _usingLegacyRenderer)
        {
            return;
        }

        string probeResult = string.Empty;
        try
        {
            string raw = await coreWebView.ExecuteScriptAsync(RendererProbeScript)
                .AsTask()
                .ConfigureAwait(true);
            probeResult = raw.Trim().Trim('"');
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Renderer probe failed on '{_screenDeviceName}': {ex.Message}");
        }

        if (_isClosing || _rendererReady || _usingLegacyRenderer)
        {
            return;
        }

        if (string.Equals(probeResult, "ready", StringComparison.Ordinal))
        {
            // 渲染器已注入宿主 API，视为就绪：停止回退，避免无谓降级。
            _rendererReady = true;
            _unresponsiveTracker.Reset();
            AppLogger.Info(
                $"BA click renderer reports ready via probe on '{_screenDeviceName}' " +
                $"(no ready message within {RendererReadyTimeout.TotalSeconds:F0}s).");
            return;
        }

        AppLogger.Warn(
            $"BA click renderer ready timeout on '{_screenDeviceName}' " +
            $"(probe: {probeResult}); switching to legacy renderer.");
        FallbackToLegacyRenderer("ready timeout");
    }

    private void StopRendererReadyTimeout()
    {
        if (_rendererReadyTimeoutTimer == null)
        {
            return;
        }

        _rendererReadyTimeoutTimer.Stop();
        _rendererReadyTimeoutTimer.Tick -= OnRendererReadyTimeout;
        _rendererReadyTimeoutTimer = null;
    }

    private void FallbackToLegacyRenderer(string reason)
    {
        if (_isClosing || _usingLegacyRenderer || _legacyFallbackAttempted)
        {
            return;
        }

        CoreWebView2? coreWebView = _coreWebView;
        if (coreWebView == null)
        {
            return;
        }

        // 单向回退，避免损坏的 primary/legacy 组合反复导航。
        _legacyFallbackAttempted = true;
        _usingLegacyRenderer = true;
        _rendererReady = true;
        _rendererGeneration = null;
        StopRendererReadyTimeout();
        AppLogger.Warn(
            $"Falling back to legacy renderer on '{_screenDeviceName}' ({reason}).");

        try
        {
            string legacyHtml = WebRendererResourceProvider.ReadResourceText(
                WebRendererResourceProvider.LegacyRendererResourcePath);
            NavigateHtml(coreWebView, legacyHtml);
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Failed to load legacy renderer on '{_screenDeviceName}'.",
                ex);
        }
    }

    // ------------------------------------------------------------------
    // 进程失败恢复
    // ------------------------------------------------------------------

    private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_isClosing ||
            sender is not CoreWebView2 failedCoreWebView ||
            !ReferenceEquals(failedCoreWebView, _coreWebView))
        {
            return;
        }

        CoreWebView2ProcessFailedKind failureKind = e.ProcessFailedKind;
        WebViewProcessRecoveryAction recoveryAction =
            WebViewProcessFailurePolicy.GetRecoveryAction(failureKind);
        if (failureKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            bool shouldRecover = _unresponsiveTracker.Register(
                failedCoreWebView,
                DateTime.UtcNow.Ticks);
            if (!shouldRecover)
            {
                AppLogger.Warn(
                    $"WebView2 renderer unresponsive on '{_screenDeviceName}' " +
                    $"({_unresponsiveTracker.ConsecutiveReports}/3); waiting for runtime recovery.");
                return;
            }

            recoveryAction = WebViewProcessRecoveryAction.RecreateWebViewControl;
        }
        if (recoveryAction == WebViewProcessRecoveryAction.None)
        {
            AppLogger.Warn(
                $"WebView2 process event on '{_screenDeviceName}' ({failureKind}); runtime recovery is left intact.");
            return;
        }

        if (_processRecoveryPending)
        {
            return;
        }

        _webViewReadyForTopmost = false;
        _processRecoveryPending = true;
        App.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_isClosing || !ReferenceEquals(failedCoreWebView, _coreWebView))
                {
                    return;
                }

                AppLogger.Warn(
                    $"WebView2 process failed on '{_screenDeviceName}' ({failureKind}); recovering renderer.");
                if (recoveryAction == WebViewProcessRecoveryAction.RecreateWebViewControl)
                {
                    RecreateWebViewController();
                }
                else
                {
                    NavigateCurrentRenderer(failedCoreWebView);
                }
            }
            catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
            {
                // 进程崩溃恢复与关闭流程存在竞争，绝不能逃逸到 UI 线程之外。
                if (!_isClosing)
                {
                    AppLogger.Warn(
                        $"WebView2 recovery was interrupted on '{_screenDeviceName}': {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(
                    $"WebView2 recovery failed on '{_screenDeviceName}'.",
                    ex);
            }
            finally
            {
                _processRecoveryPending = false;
            }
        });
    }

    /// <summary>
    /// 卸载并重建 WebView2 控制器。渲染进程已经断连，无需再走优雅关闭。
    /// </summary>
    private void RecreateWebViewController()
    {
        _webViewReadyForTopmost = false;
        StopRendererReadyTimeout();
        ClearCoreWebViewEventState();

        try
        {
            _controller?.Close();
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
        }

        _controller = null;
        _coreWebView = null;
        _controllerInitialized = false;

        _ = InitWebViewAsync();
    }

    private void DetachCoreWebViewEvents()
    {
        CoreWebView2? coreWebView = _coreWebView;
        if (coreWebView != null)
        {
            TryDetach(() => coreWebView.NavigationCompleted -= _navigationCompletedHandler);
            TryDetach(() => coreWebView.NavigationStarting -= _navigationStartingHandler);
            TryDetach(() => coreWebView.ProcessFailed -= _processFailedHandler);
            TryDetach(() => coreWebView.WebMessageReceived -= _webMessageReceivedHandler);
        }

        ClearCoreWebViewEventState();
    }

    private void TryDetach(Action detach)
    {
        try
        {
            detach();
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
        }
    }

    private void ClearCoreWebViewEventState()
    {
        _navigationStartingHandler = null;
        _navigationCompletedHandler = null;
        _processFailedHandler = null;
        _webMessageReceivedHandler = null;
        _currentNavigationId = null;
        _rendererGeneration = null;
        _unresponsiveTracker.Reset();
    }

    // ------------------------------------------------------------------
    // 对外状态接口（由 OverlayManager 调用）
    // ------------------------------------------------------------------

    public void UpdateColor(string color)
    {
        // JSON 序列化可防止注册表里的颜色文本变成可执行脚本。
        string colorJson = JsonSerializer.Serialize(color);
        ExecuteScript($"if(window.updateColor) window.updateColor({colorJson});");
    }

    public void UpdateEffectSettings(
        double trailScale,
        double clickScale,
        double opacity,
        double trailSpeed,
        double clickSpeed,
        double glowIntensity)
    {
        string trailScaleStr = trailScale.ToString("F2", CultureInfo.InvariantCulture);
        string clickScaleStr = clickScale.ToString("F2", CultureInfo.InvariantCulture);
        string opacityStr = opacity.ToString("F2", CultureInfo.InvariantCulture);
        string trailStr = trailSpeed.ToString("F2", CultureInfo.InvariantCulture);
        string clickStr = clickSpeed.ToString("F2", CultureInfo.InvariantCulture);
        string glowStr = glowIntensity.ToString("F2", CultureInfo.InvariantCulture);

        ExecuteScript($"if(window.updateEffectSettings) window.updateEffectSettings({trailScaleStr}, {clickScaleStr}, {opacityStr}, {trailStr}, {clickStr}, {glowStr});");
    }

    public void UpdateTrailRefreshRate(int hz)
    {
        int clampedHz = Math.Clamp(hz, 30, 360);
        _trailMoveIntervalTimestamp = Math.Max(1, Stopwatch.Frequency / clampedHz);
    }

    public void SetCurveDraw(bool enabled)
    {
        ExecuteScript($"window.ApplyCurveDraw = {(enabled ? "true" : "false")};");
    }

    public void UpdateTouchMode(bool enabled)
    {
        ConfigManager.IsTouchscreenMode = enabled;
    }

    public void UpdateScreenshotCompatibilityMode(bool enabled)
    {
        _screenshotCompatibilityMode = enabled;
        _host.ApplyCaptureExclusion(enabled);
    }

    /// <summary>截图工具框选期间暂时隐藏叠加层。</summary>
    public void SetHiddenForExternalScreenshotCapture(bool hidden)
    {
        if (_hiddenForExternalScreenshotCapture == hidden)
        {
            return;
        }

        _hiddenForExternalScreenshotCapture = hidden;
        SyncOverlayPresentationState();
    }

    /// <summary>环境过滤期间终止当前输入并阻止后续输入，已创建的动画自然结束。</summary>
    public void SetEnvironmentSuppressed(bool suppressed)
    {
        if (_environmentInputSuppressed == suppressed)
        {
            return;
        }

        _environmentInputSuppressed = suppressed;
        ApplyEnvironmentInputSuppression();
    }

    private bool ShouldOverlayBeVisible => !_hiddenForExternalScreenshotCapture;

    private void SyncOverlayPresentationState()
    {
        if (ShouldOverlayBeVisible)
        {
            _host.Show();
            if (_controller != null && _controllerInitialized)
            {
                try
                {
                    _controller.IsVisible = true;
                    _host.ApplyCaptureExclusion(_screenshotCompatibilityMode);
                }
                catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
                {
                }
            }

            ResumeOverlayRuntime();
        }
        else
        {
            PauseOverlayRuntime();
            _host.Hide();
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

    /// <summary>
    /// 让 WebView2 运行时真正挂起渲染进程，闲置时不再占用 GPU。
    /// 失败（例如运行时版本不支持）时静默忽略，交由脚本层的
    /// setRenderingPaused 兜底。
    /// </summary>
    private async Task TrySuspendWebViewAsync()
    {
        CoreWebView2? coreWebView = _coreWebView;
        if (coreWebView == null)
        {
            return;
        }

        try
        {
            await coreWebView.TrySuspendAsync().AsTask().ConfigureAwait(true);
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
        }
    }

    private void ResumeOverlayRuntime()
    {
        if (!_overlayRuntimePaused)
        {
            return;
        }

        _overlayRuntimePaused = false;

        CoreWebView2? coreWebView = _coreWebView;
        if (coreWebView != null)
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

    private void PauseTopmostMonitoring()
    {
        _topmostTimer?.Stop();

        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }

    private void ResumeTopmostMonitoring()
    {
        if (_host.Handle == IntPtr.Zero || !_host.IsVisible)
        {
            return;
        }

        InitRealtimeTopmostHook();

        if (_topmostTimer == null)
        {
            InitTopmostSentinel();
        }
        else if (!_topmostTimer.IsRunning)
        {
            _topmostTimer.Start();
        }

        SafeEnsureTopmost();
    }

    // ------------------------------------------------------------------
    // 输入事件转发
    // ------------------------------------------------------------------

    public void EmitDown(int x, int y)
    {
        if (!TryConvertScreenToOverlayPoint(x, y, out Windows.Foundation.Point clientPoint))
        {
            return;
        }

        bool touchLike = !NativeMethods.IsCursorVisible();
        string inputMode = touchLike ? InputModeTouch : InputModeMouse;
        ExecuteWithInputContext(
            inputMode,
            $"if(window.externalBoom) window.externalBoom({FormatCoordinate(clientPoint.X)}, {FormatCoordinate(clientPoint.Y)});");
    }

    public void EmitTrailStart(int x, int y, bool touchLike)
    {
        if (!TryConvertScreenToOverlayPoint(x, y, out Windows.Foundation.Point clientPoint))
        {
            return;
        }

        string inputMode = touchLike ? InputModeTouch : InputModeMouse;
        ExecuteWithInputContext(
            inputMode,
            $"if(window.externalTrailStart) window.externalTrailStart({FormatCoordinate(clientPoint.X)}, {FormatCoordinate(clientPoint.Y)});");
    }

    public void EmitMove(int x, int y, bool touchLike)
    {
        if (!TryConvertScreenToOverlayPoint(x, y, out Windows.Foundation.Point clientPoint))
        {
            return;
        }

        string inputMode = touchLike ? InputModeTouch : InputModeMouse;
        ExecuteWithInputContext(
            inputMode,
            $"if(window.externalMove) window.externalMove({FormatCoordinate(clientPoint.X)}, {FormatCoordinate(clientPoint.Y)});");
    }

    public void EmitUp(bool touchLike)
    {
        string inputMode = touchLike ? InputModeTouch : InputModeMouse;
        ExecuteWithInputContext(inputMode, "if(window.externalUp) window.externalUp();");
    }

    public void EmitCancel()
    {
        ExecuteScript(
            "if(window.externalCancel){window.externalCancel();}" +
            "else if(window.spark&&window.spark.clearEffects){window.spark.clearEffects();}" +
            "else if(window.externalUp){window.externalUp();}");
    }

    private static string FormatCoordinate(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    /// <summary>把屏幕物理坐标转换为叠加层的 0..1 百分比坐标。</summary>
    private bool TryConvertScreenToOverlayPoint(
        int screenX,
        int screenY,
        out Windows.Foundation.Point percentPoint)
    {
        percentPoint = default;
        if (!_host.TryGetWindowRect(out NativeMethods.RECT rect))
        {
            return false;
        }

        double physWidth = rect.Width;
        double physHeight = rect.Height;
        if (physWidth <= 0 || physHeight <= 0)
        {
            return false;
        }

        double percentX = (screenX - rect.Left) / physWidth;
        double percentY = (screenY - rect.Top) / physHeight;

        percentPoint = new Windows.Foundation.Point(
            Math.Clamp(percentX, 0.0, 1.0),
            Math.Clamp(percentY, 0.0, 1.0));
        return true;
    }

    // ------------------------------------------------------------------
    // 脚本注入
    // ------------------------------------------------------------------

    private string BuildInputContextScript(string inputMode)
    {
        bool alwaysTrailEnabled = ConfigManager.EnableAlwaysTrailEffect;
        if (_lastReportedInputMode == inputMode && _lastReportedAlwaysTrail == alwaysTrailEnabled)
        {
            return string.Empty;
        }

        _lastReportedInputMode = inputMode;
        _lastReportedAlwaysTrail = alwaysTrailEnabled;
        string alwaysTrailLiteral = alwaysTrailEnabled ? "true" : "false";
        return $"if(window.setInputContext) window.setInputContext('{inputMode}', {alwaysTrailLiteral});";
    }

    private void SyncInputContext(string inputMode)
    {
        string script = BuildInputContextScript(inputMode);
        if (string.IsNullOrEmpty(script))
        {
            return;
        }

        ExecuteScript(script);
    }

    private void ApplyEnvironmentInputSuppression()
    {
        string suppressed = _environmentInputSuppressed ? "true" : "false";
        ExecuteScript($"if(window.setEnvironmentInputSuppressed) window.setEnvironmentInputSuppressed({suppressed});");
    }

    private void ExecuteWithInputContext(string inputMode, string actionScript)
    {
        ExecuteScript(BuildInputContextScript(inputMode) + actionScript);
    }

    /// <summary>统一 JS 脚本执行入口。</summary>
    private void ExecuteScript(string script)
    {
        if (string.IsNullOrEmpty(script))
        {
            return;
        }

        CoreWebView2? coreWebView = _coreWebView;
        if (coreWebView == null)
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

    private bool IsExpectedWebViewShutdownException(Exception ex)
    {
        return _isClosing ||
               ex is ObjectDisposedException ||
               (ex is InvalidOperationException &&
                ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        StopRendererReadyTimeout();

        _topmostTimer?.Stop();
        _topmostTimer = null;

        DetachCoreWebViewEvents();

        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _winEventDelegate = null;

        try
        {
            _controller?.Close();
        }
        catch (Exception ex) when (IsExpectedWebViewShutdownException(ex))
        {
        }
        finally
        {
            _controller = null;
            _coreWebView = null;
            _controllerInitialized = false;
        }

        _host.Dispose();
    }
}
