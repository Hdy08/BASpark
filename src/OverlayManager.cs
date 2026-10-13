using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace BASpark
{
    internal readonly record struct TrailSample(int ScreenX, int ScreenY, long Timestamp);

    public sealed class OverlayManager : IDisposable
    {
        // 全部 Win32 声明集中在 NativeMethods；此处只保留业务常量。
        private const int FullscreenTolerance = 2;
        private const int DisplaySettingsRecoveryDebounceMilliseconds = 400;
        private static readonly long SuppressionCacheDurationTicks = TimeSpan.FromMilliseconds(250).Ticks;
        private const long ClickIntervalTicks = 300000;

        private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);
        private IKeyboardMouseEvents? _globalHook;
        private IntPtr _mouseHook;
        private NativeMethods.MouseHookProc? _mouseHookDelegate;
        private TouchInputListener? _touchInputListener;
        private NativeMethods.POINT? _touchPoint;
        private OverlayWindow? _activePointerOverlay;
        private OverlayWindow? _lastTrailOverlay;
        private OverlayWindow? _lastTrailThrottleOverlay;
        private readonly Queue<TrailSample> _trailHistory = new(1024);
        private readonly Dictionary<OverlayWindow, long> _trailParticipants = new();
        private bool _resumeTrailParticipants;
        private TrailSample? _lastTrailSample;
        private long _trailStrokeId;
        private static readonly long TrailHistoryRetentionTicks = Stopwatch.Frequency * 2;
        private long _lastMoveTimestamp;
        private long _lastClickTicks;
        private long _idleMoveIntervalTimestamp = Math.Max(1, Stopwatch.Frequency / 60);
        private int _manualTrailRefreshRate = 60;
        private bool _followDisplayRefreshRate = true;
        private bool _isPrimaryPointerDown;
        private bool _isTouchLikeInput;
        private bool _isSuppressedByEnvironment;
        private long _suppressionCacheValidUntilTicks;
        private IntPtr _lastForegroundWindow = IntPtr.Zero;
        private bool _disposed;

        private bool _screenshotCompatCaptureWired;
        private bool _screenshotCaptureSessionActive;
        private bool _winKeyDown;
        private bool _shiftKeyDown;
        private IntPtr _foregroundWinEventHook = IntPtr.Zero;
        private NativeMethods.WinEventProc? _foregroundWinEventDelegate;
        private System.Threading.Timer? _screenshotFailsafeTimer;
        private DispatcherQueueTimer? _screenshotEndDebounceTimer;
        private DispatcherQueueTimer? _displaySettingsRecoveryTimer;
        private DispatcherQueueTimer? _displayTopologyTimer;
        private IReadOnlyList<ScreenInfo> _lastDisplayScreens = Array.Empty<ScreenInfo>();
        private long _lastResumeRecoveryTicks;
        private static readonly long ResumeRecoveryDebounceTicks = TimeSpan.FromSeconds(2).Ticks;

        private static readonly HashSet<string> ScreenshotForegroundHostExe = new(StringComparer.OrdinalIgnoreCase)
        {
            "SnippingTool.exe",
            "ScreenSketch.exe",
            "ScreenClippingHost.exe",
            "ms-screenclip.exe",
        };

        public void Start()
        {
            UpdateTrailRefreshRate(ConfigManager.TrailRefreshRate, ConfigManager.FollowDisplayRefreshRate);
            RebuildWindows(forceRebuild: true);
            SetupGlobalHooks();
            RefreshEnvironmentFilterState();
            SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += HandlePowerModeChanged;
            SystemEvents.SessionSwitch += HandleSessionSwitch;
            _displayTopologyTimer = App.DispatcherQueue.CreateTimer();
            _displayTopologyTimer.Interval = TimeSpan.FromSeconds(2);
            _displayTopologyTimer.Tick += DisplayTopologyTimer_Tick;
            _displayTopologyTimer.Start();
        }

        public void UpdateColor(string color) => ForEachOverlay(w => w.UpdateColor(color));
        public void UpdateEffectSettings(double trailScale, double clickScale, double opacity, double trailSpeed, double clickSpeed, double glowIntensity) =>
            ForEachOverlay(w => w.UpdateEffectSettings(trailScale, clickScale, opacity, trailSpeed, clickSpeed, glowIntensity));
        public void UpdateTrailRefreshRate(int hz, bool followDisplayRefreshRate)
        {
            _manualTrailRefreshRate = Math.Clamp(hz, 30, 360);
            _followDisplayRefreshRate = followDisplayRefreshRate;
            _idleMoveIntervalTimestamp = Math.Max(1, Stopwatch.Frequency / _manualTrailRefreshRate);
            _lastTrailThrottleOverlay = null;
            _lastMoveTimestamp = 0;

            foreach (var pair in _overlays)
            {
                int effectiveRefreshRate = _followDisplayRefreshRate
                    ? ScreenIdentity.GetRefreshRate(pair.Key, _manualTrailRefreshRate)
                    : _manualTrailRefreshRate;
                pair.Value.UpdateTrailRefreshRate(effectiveRefreshRate);
            }
        }
        public void UpdateTouchMode(bool enabled)
        {
            ConfigManager.IsTouchscreenMode = enabled;
            if (!enabled && _isTouchLikeInput) ReleasePointerStateSilent();
            SyncTouchInputListener();
            ForEachOverlay(w => w.UpdateTouchMode(enabled));
        }
        public void UpdateScreenshotCompatibilityMode(bool enabled)
        {
            SyncScreenshotCompatCaptureSurfaces(enabled);
        }
        public void SetCurveDraw(bool enabled) => ForEachOverlay(w => w.SetCurveDraw(enabled));
        public bool IsEffectSuppressedByEnvironment() => _isSuppressedByEnvironment;
        public void RefreshPauseState()
        {
            if (App.IsEffectsPaused) ReleasePointerState();
            ForEachOverlay(window => window.RefreshPauseState());
        }
        public void RefreshEnvironmentFilterState()
        {
            _suppressionCacheValidUntilTicks = 0;
            _lastForegroundWindow = IntPtr.Zero;
            ShouldSuppressEffects(forceRefresh: true);
            SyncForegroundWinEventHook();
        }
        public void RefreshScreenSelection()
        {
            RebuildWindows(forceRebuild: false);
        }

        private void SetupGlobalHooks()
        {
            TeardownScreenshotCompatCaptureSurfaces();
            UninstallMouseHook();
            _globalHook?.Dispose();
            _globalHook = Hook.GlobalEvents();
            _mouseHookDelegate ??= MouseHookCallback;
            _mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _mouseHookDelegate, NativeMethods.GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SyncTouchInputListener();
            SyncScreenshotCompatCaptureSurfaces(ConfigManager.ScreenshotCompatibilityMode);
        }

        private void SyncTouchInputListener()
        {
            if (_mouseHook != IntPtr.Zero && ConfigManager.IsTouchscreenMode)
            {
                _touchInputListener ??= new TouchInputListener(OnTouchInput);
            }
            else
            {
                _touchInputListener?.Dispose();
                _touchInputListener = null;
            }
        }

        private void OnTouchInput(TouchInputPhase phase, int screenX, int screenY)
        {
            switch (phase)
            {
                case TouchInputPhase.Down:
                    if (_isPrimaryPointerDown && _isTouchLikeInput) OnPointerMove(screenX, screenY, true);
                    else OnPointerDown(MouseButtons.Left, screenX, screenY, true);
                    break;
                case TouchInputPhase.Move:
                    OnPointerMove(screenX, screenY, true);
                    break;
                case TouchInputPhase.Up:
                    OnPointerUp(true);
                    break;
            }
        }

        private void UninstallMouseHook()
        {
            _touchInputListener?.Dispose();
            _touchInputListener = null;
            if (_mouseHook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !_disposed)
            {
                try
                {
                    HandleMouseInput(unchecked((uint)message.ToInt64()), Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(data));
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Mouse input routing failed: " + exception.Message);
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
        }

        private void HandleMouseInput(uint message, NativeMethods.MSLLHOOKSTRUCT input)
        {
            bool touchLike = NativeMethods.IsTouchInput(input.dwExtraInfo);
            if (!touchLike) _touchInputListener?.ObserveCursor(mouseInput: true);
            if (touchLike && _touchInputListener?.ShouldIgnorePromotedTouch(input.pt.x, input.pt.y) == true) return;
            switch (message)
            {
                case NativeMethods.WM_MOUSEMOVE:
                    OnPointerMove(input.pt.x, input.pt.y, touchLike);
                    break;
                case NativeMethods.WM_LBUTTONDOWN:
                    OnPointerDown(MouseButtons.Left, input.pt.x, input.pt.y, touchLike);
                    break;
                case NativeMethods.WM_RBUTTONDOWN:
                    OnPointerDown(MouseButtons.Right, input.pt.x, input.pt.y, touchLike);
                    break;
                case NativeMethods.WM_MBUTTONDOWN:
                    OnPointerDown(MouseButtons.Middle, input.pt.x, input.pt.y, touchLike);
                    break;
                case NativeMethods.WM_LBUTTONUP:
                case NativeMethods.WM_RBUTTONUP:
                case NativeMethods.WM_MBUTTONUP:
                case NativeMethods.WM_XBUTTONUP:
                    OnPointerUp(touchLike);
                    break;
            }
        }

        private void SyncScreenshotCompatCaptureSurfaces(bool enabled)
        {
            if (!enabled)
            {
                TeardownScreenshotCompatCaptureSurfaces();
                EndScreenshotCaptureSession();
                return;
            }

            if (_globalHook == null)
            {
                return;
            }

            if (!_screenshotCompatCaptureWired)
            {
                _globalHook.KeyDown += OnScreenshotCompatKeyDown;
                _globalHook.KeyUp += OnScreenshotCompatKeyUp;
                _screenshotCompatCaptureWired = true;
                SyncForegroundWinEventHook();
            }

            EvaluateScreenshotForegroundForCapture();
        }

        private void TeardownScreenshotCompatCaptureSurfaces()
        {
            if (!_screenshotCompatCaptureWired)
            {
                return;
            }

            if (_globalHook != null)
            {
                _globalHook.KeyDown -= OnScreenshotCompatKeyDown;
                _globalHook.KeyUp -= OnScreenshotCompatKeyUp;
            }
            _screenshotCompatCaptureWired = false;
            _winKeyDown = false;
            _shiftKeyDown = false;
            SyncForegroundWinEventHook();
            StopScreenshotEndDebounce();
            CancelScreenshotFailsafeTimer();
        }

        private void SyncForegroundWinEventHook()
        {
            bool needHook = ConfigManager.EnableEnvironmentFilter || _screenshotCompatCaptureWired;
            if (needHook)
            {
                InstallForegroundWinEventHook();
            }
            else
            {
                UninstallForegroundWinEventHook();
            }
        }

        private void InstallForegroundWinEventHook()
        {
            if (_foregroundWinEventHook != IntPtr.Zero)
            {
                return;
            }

            _foregroundWinEventDelegate ??= ForegroundWinEventProc;
            _foregroundWinEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _foregroundWinEventDelegate,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        private void UninstallForegroundWinEventHook()
        {
            if (_foregroundWinEventHook == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.UnhookWinEvent(_foregroundWinEventHook);
            _foregroundWinEventHook = IntPtr.Zero;
        }

        private void ForegroundWinEventProc(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            _ = hWinEventHook;
            _ = hwnd;
            _ = idObject;
            _ = idChild;
            _ = dwEventThread;
            _ = dwmsEventTime;
            if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                return;
            }

            App.DispatcherQueue.TryEnqueue(HandleForegroundWindowChanged);
        }

        private void HandleForegroundWindowChanged()
        {
            if (_disposed)
            {
                return;
            }

            if (ConfigManager.EnableEnvironmentFilter)
            {
                _suppressionCacheValidUntilTicks = 0;
                _lastForegroundWindow = IntPtr.Zero;
                ShouldSuppressEffects(forceRefresh: true);
            }

            if (_screenshotCompatCaptureWired)
            {
                EvaluateScreenshotForegroundForCapture();
            }
        }

        private void EvaluateScreenshotForegroundForCapture()
        {
            if (_disposed || !_screenshotCompatCaptureWired)
            {
                return;
            }

            if (IsForegroundScreenshotHost())
            {
                StopScreenshotEndDebounce();
                BeginScreenshotCaptureSession();
                return;
            }

            if (_screenshotCaptureSessionActive)
            {
                ScheduleEndScreenshotCaptureSessionDebounced();
            }
        }

        private bool IsForegroundScreenshotHost()
        {
            return IsKnownScreenshotHostWindow(NativeMethods.GetForegroundWindow());
        }

        private bool IsKnownScreenshotHostWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero)
            {
                root = hwnd;
            }

            if (IsOverlayWindow(root))
            {
                return false;
            }

            if (!NativeMethods.IsWindow(root) || !NativeMethods.IsWindowVisible(root))
            {
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(root, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
            {
                return false;
            }

            string exe = GetProcessExecutableName(pid);
            return ScreenshotForegroundHostExe.Contains(exe);
        }

        private void OnScreenshotCompatKeyDown(object? sender, KeyEventArgs e)
        {
            if (_disposed || !_screenshotCompatCaptureWired)
            {
                return;
            }

            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                _winKeyDown = true;
                return;
            }

            if (e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey || e.KeyCode == Keys.ShiftKey)
            {
                _shiftKeyDown = true;
                return;
            }

            if (e.KeyCode == Keys.S && _winKeyDown && (e.Shift || _shiftKeyDown))
            {
                BeginScreenshotCaptureSession();
                return;
            }

            if (e.KeyCode == Keys.PrintScreen)
            {
                BeginScreenshotCaptureSession();
                ScheduleEndScreenshotCaptureSessionDebounced();
                return;
            }

            if (ConfigManager.ScreenshotCompatibilityMode && e.KeyCode == Keys.A && e.Control && e.Alt)
            {
                BeginScreenshotCaptureSession();
                return;
            }

            if (ConfigManager.ScreenshotCompatibilityMode && e.KeyCode == Keys.A && e.Alt && !e.Control)
            {
                BeginScreenshotCaptureSession();
                return;
            }

            if (e.KeyCode == Keys.Escape && _screenshotCaptureSessionActive)
            {
                ScheduleEndScreenshotCaptureSessionDebounced();
            }
        }

        private void OnScreenshotCompatKeyUp(object? sender, KeyEventArgs e)
        {
            if (_disposed || !_screenshotCompatCaptureWired)
            {
                return;
            }

            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                _winKeyDown = false;
            }

            if (e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey || e.KeyCode == Keys.ShiftKey)
            {
                _shiftKeyDown = false;
            }
        }

        private void BeginScreenshotCaptureSession()
        {
            if (_disposed || !_screenshotCompatCaptureWired)
            {
                return;
            }

            if (_screenshotCaptureSessionActive)
            {
                ArmScreenshotFailsafeTimer();
                return;
            }

            ReleasePointerState();
            _screenshotCaptureSessionActive = true;
            ForEachOverlay(w => w.SetHiddenForExternalScreenshotCapture(true));
            NativeMethods.DwmFlush();
            ArmScreenshotFailsafeTimer();
        }

        private void EndScreenshotCaptureSession()
        {
            if (!_screenshotCaptureSessionActive)
            {
                return;
            }

            _screenshotCaptureSessionActive = false;
            StopScreenshotEndDebounce();
            CancelScreenshotFailsafeTimer();
            ForEachOverlay(w => w.SetHiddenForExternalScreenshotCapture(false));
        }

        private void ScheduleEndScreenshotCaptureSessionDebounced()
        {
            App.DispatcherQueue.TryEnqueue(ScheduleEndOnDispatcherThread);
        }

        private void ScheduleEndOnDispatcherThread()
        {
            if (_disposed || !_screenshotCaptureSessionActive)
            {
                return;
            }

            StopScreenshotEndDebounce();
            _screenshotEndDebounceTimer = App.DispatcherQueue.CreateTimer();
            _screenshotEndDebounceTimer.Interval = TimeSpan.FromMilliseconds(360);
            _screenshotEndDebounceTimer.Tick += OnScreenshotEndDebounceTick;
            _screenshotEndDebounceTimer.Start();
        }

        private void OnScreenshotEndDebounceTick(DispatcherQueueTimer sender, object args)
        {
            _ = sender;
            _ = args;
            StopScreenshotEndDebounce();
            if (_disposed)
            {
                return;
            }

            if (!IsForegroundScreenshotHost())
            {
                EndScreenshotCaptureSession();
            }
        }

        private void StopScreenshotEndDebounce()
        {
            if (_screenshotEndDebounceTimer != null)
            {
                _screenshotEndDebounceTimer.Stop();
                _screenshotEndDebounceTimer.Tick -= OnScreenshotEndDebounceTick;
                _screenshotEndDebounceTimer = null;
            }
        }

        private void ArmScreenshotFailsafeTimer()
        {
            CancelScreenshotFailsafeTimer();
            _screenshotFailsafeTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    App.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        EndScreenshotCaptureSession();
                    });
                }
                catch
                {
                }
            }, null, 90000, Timeout.Infinite);
        }

        private void CancelScreenshotFailsafeTimer()
        {
            _screenshotFailsafeTimer?.Dispose();
            _screenshotFailsafeTimer = null;
        }

        private void OnPointerDown(MouseButtons button, int cursorX, int cursorY, bool touchLike)
        {
            // 点击特效开关
            if (!ConfigManager.IsClickEffectActive) return;
            if (touchLike && !ConfigManager.IsTouchscreenMode) return;
            if (touchLike) _touchPoint = new NativeMethods.POINT { x = cursorX, y = cursorY };
            // 每次点击都检测环境 避免切窗后首次点击渲染
            if (ShouldSuppressEffects(forceRefresh: true, inputPoint: touchLike ? _touchPoint : null)) return;
            if (!CanRenderEffects(touchLike, skipSuppressionCheck: true)) return;

            bool isLeft = button == MouseButtons.Left;
            bool isRight = button == MouseButtons.Right;
            bool isMiddle = button == MouseButtons.Middle;
            bool shouldPrimaryTrigger = ConfigManager.ClickTriggerType switch
            {
                1 => isRight,
                2 => isLeft || isRight,
                _ => isLeft
            };
            // 中键触发
            bool shouldTrigger = shouldPrimaryTrigger || (ConfigManager.EnableMiddleClickTrigger && isMiddle);
            if (!shouldTrigger) return;

            if (!touchLike && TryGetPhysicalCursorPosition(out int physicalX, out int physicalY))
            {
                cursorX = physicalX;
                cursorY = physicalY;
            }

            OverlayWindow? target = ResolveTargetOverlay(cursorX, cursorY);
            if (target == null)
            {
                return;
            }

            if (ConfigManager.EnableAlwaysTrailEffect)
            {
                SwitchAlwaysTrailOverlay(target);
            }
            else
            {
                SwitchAlwaysTrailOverlay(null);
            }

            _isPrimaryPointerDown = true;
            _isTouchLikeInput = touchLike;
            _activePointerOverlay = target;

            long currentTicks = DateTime.Now.Ticks;
            if (currentTicks - _lastClickTicks < ClickIntervalTicks) return;
            _lastClickTicks = currentTicks;

            ConfigManager.TotalClicks++;
            ClearTrailHistory();
            target.TrailStrokeId = _trailStrokeId;
            target.EmitDown(cursorX, cursorY, touchLike);
            long timestamp = Stopwatch.GetTimestamp();
            _trailParticipants[target] = timestamp;
            RecordTrailSample(cursorX, cursorY, timestamp);
        }

        private void OnPointerMove(int cursorX, int cursorY, bool touchLike)
        {
            if (touchLike && (!ConfigManager.IsTouchscreenMode || !_isPrimaryPointerDown || !_isTouchLikeInput)) return;
            if (_isPrimaryPointerDown && touchLike != _isTouchLikeInput) return;
            if (touchLike) _touchPoint = new NativeMethods.POINT { x = cursorX, y = cursorY };
            // 拖尾仅在点击特效开启或常驻拖尾开启时渲染
            if (!ConfigManager.IsTrailEffectActive)
            {
                if (ConfigManager.EnableEnvironmentFilter)
                {
                    ShouldSuppressEffects();
                }

                SwitchAlwaysTrailOverlay(null);
                _lastTrailThrottleOverlay = null;
                return;
            }
            if (!_isPrimaryPointerDown && !ConfigManager.EnableAlwaysTrailEffect)
            {
                if (ConfigManager.EnableEnvironmentFilter) ShouldSuppressEffects();
                SwitchAlwaysTrailOverlay(null);
                _lastTrailThrottleOverlay = null;
                return;
            }
            if (!CanRenderEffects(touchLike))
            {
                _lastTrailThrottleOverlay = null;
                return;
            }

            if (!touchLike && TryGetPhysicalCursorPosition(out int physicalX, out int physicalY))
            {
                cursorX = physicalX;
                cursorY = physicalY;
            }

            OverlayWindow? hoveredTarget = ResolveTargetOverlay(cursorX, cursorY);
            OverlayWindow? target = hoveredTarget;
            bool targetChanged = !ReferenceEquals(target, _lastTrailThrottleOverlay);
            long currentTimestamp = Stopwatch.GetTimestamp();
            long moveInterval = target?.TrailMoveIntervalTimestamp ?? _idleMoveIntervalTimestamp;
            if (!targetChanged && currentTimestamp - _lastMoveTimestamp < moveInterval)
            {
                return;
            }

            _lastMoveTimestamp = currentTimestamp;
            _lastTrailThrottleOverlay = target;

            RouteTrailMove(target, cursorX, cursorY, touchLike);
        }

        private void RouteTrailMove(OverlayWindow? target, int cursorX, int cursorY, bool touchLike)
        {
            long timestamp = Stopwatch.GetTimestamp();
            TrimTrailHistory(timestamp);
            foreach (OverlayWindow overlay in _overlays.Values)
            {
                bool participating = _trailParticipants.TryGetValue(overlay, out long lastIntersection);
                bool intersects = ReferenceEquals(overlay, target)
                    || (_lastTrailSample is TrailSample lastSample
                        && overlay.IntersectsScreenSegment(lastSample.ScreenX, lastSample.ScreenY, cursorX, cursorY));
                if (!intersects)
                {
                    if (!participating) continue;
                    if (timestamp - lastIntersection > TrailHistoryRetentionTicks)
                    {
                        overlay.EmitUp(touchLike);
                        _trailParticipants.Remove(overlay);
                        continue;
                    }
                }

                overlay.TrailStrokeId = _trailStrokeId;
                bool continuing = participating && !_resumeTrailParticipants;
                if (ReferenceEquals(overlay, target) && (continuing || _lastTrailSample == null))
                {
                    if (!participating && _isPrimaryPointerDown)
                    {
                        overlay.EmitTrailStart(cursorX, cursorY, touchLike);
                    }
                    else
                    {
                        overlay.EmitMove(cursorX, cursorY, touchLike);
                    }
                }
                else
                {
                    overlay.EmitTrailContinuation(_trailHistory, timestamp, cursorX, cursorY,
                        touchLike, _isPrimaryPointerDown, beginStroke: !continuing, finishStroke: false);
                }
                if (intersects) _trailParticipants[overlay] = timestamp;
            }
            _resumeTrailParticipants = false;

            if (_isPrimaryPointerDown) _activePointerOverlay = target;
            _lastTrailOverlay = ConfigManager.EnableAlwaysTrailEffect ? target : null;
            RecordTrailSample(cursorX, cursorY, timestamp);
        }

        private void RecordTrailSample(int cursorX, int cursorY, long timestamp)
        {
            TrimTrailHistory(timestamp);
            if (_trailHistory.Count >= 1024) _trailHistory.Dequeue();
            var sample = new TrailSample(cursorX, cursorY, timestamp);
            _trailHistory.Enqueue(sample);
            _lastTrailSample = sample;
        }

        private void TrimTrailHistory(long timestamp)
        {
            while (_trailHistory.TryPeek(out TrailSample oldest)
                && timestamp - oldest.Timestamp > TrailHistoryRetentionTicks)
            {
                _trailHistory.Dequeue();
            }
            if (_trailHistory.Count == 0 && _lastTrailSample is TrailSample lastSample)
            {
                var renewed = lastSample with { Timestamp = timestamp };
                _trailHistory.Enqueue(renewed);
                _lastTrailSample = renewed;
            }
        }

        private void ClearTrailHistory()
        {
            ReleaseTrailParticipants();
            _trailHistory.Clear();
            _lastTrailSample = null;
            _trailStrokeId++;
        }

        private void ReleaseTrailParticipants(bool cancel = false, bool resume = false)
        {
            foreach (OverlayWindow overlay in _trailParticipants.Keys)
            {
                if (cancel) overlay.EmitCancel();
                else overlay.EmitUp(_isTouchLikeInput);
            }
            _resumeTrailParticipants = resume && _trailParticipants.Count > 0;
            if (!resume) _trailParticipants.Clear();
        }

        private void OnPointerUp(bool touchLike)
        {
            if (!_isPrimaryPointerDown)
            {
                _isTouchLikeInput = false;
                return;
            }
            if (touchLike != _isTouchLikeInput) return;

            if (ShouldSuppressEffects())
            {
                ReleasePointerStateForEnvironmentSuppression();
                return;
            }

            _activePointerOverlay?.EmitUp(_isTouchLikeInput);
            ReleaseTrailParticipants(resume: ConfigManager.EnableAlwaysTrailEffect);
            ResetPrimaryPointerState();
            if (!ConfigManager.EnableAlwaysTrailEffect) ClearTrailHistory();
        }

        /// <summary>
        /// 检查渲染环境条件（覆盖窗口存在性、环境过滤、光标可见性）。
        /// 业务开关（点击特效、常驻拖尾）由各调用点自行判断。
        /// </summary>
        private bool CanRenderEffects(bool touchLike, bool skipSuppressionCheck = false)
        {
            if (App.IsEffectsPaused || _overlays.Count == 0)
            {
                ReleasePointerStateSilent();
                return false;
            }

            if (!skipSuppressionCheck && ShouldSuppressEffects())
            {
                ReleasePointerStateForEnvironmentSuppression();
                return false;
            }

            bool cursorVisible = touchLike && ConfigManager.IsTouchscreenMode && _touchPoint is NativeMethods.POINT touchPoint
                && _touchInputListener != null ? _touchInputListener.IsCursorVisible(touchPoint)
                : NativeMethods.IsCursorVisible(allowTouchSuppression: touchLike && ConfigManager.IsTouchscreenMode);
            if (!cursorVisible)
            {
                ReleasePointerStateSilent();
                return false;
            }

            return true;
        }

        private void ReleasePointerStateSilent()
        {
            OverlayWindow? activePointerOverlay = _activePointerOverlay;
            activePointerOverlay?.EmitCancel();
            if (_lastTrailOverlay != null &&
                !ReferenceEquals(_lastTrailOverlay, activePointerOverlay))
            {
                _lastTrailOverlay.EmitCancel();
            }

            ReleaseTrailParticipants(cancel: true);
            ResetPrimaryPointerState();
            _lastTrailOverlay = null;
            _lastTrailThrottleOverlay = null;
            _lastMoveTimestamp = 0;
            ClearTrailHistory();
        }

        // 环境过滤通过 SetEnvironmentSuppressed 释放渲染器，此处只清理宿主侧路由状态。
        private void ReleasePointerStateForEnvironmentSuppression()
        {
            ResetPrimaryPointerState();
            _lastTrailOverlay = null;
            _lastTrailThrottleOverlay = null;
            _lastMoveTimestamp = 0;
            ClearTrailHistory();
        }

        private void ReleasePointerState()
        {
            if (!_isPrimaryPointerDown)
            {
                ReleasePointerStateSilent();
                return;
            }

            _activePointerOverlay?.EmitUp(_isTouchLikeInput);
            ResetPrimaryPointerState();
            ClearTrailHistory();
        }

        private void ResetPrimaryPointerState()
        {
            _isPrimaryPointerDown = false;
            _isTouchLikeInput = false;
            _activePointerOverlay = null;
            _touchPoint = null;
        }

        private void SwitchAlwaysTrailOverlay(OverlayWindow? target)
        {
            if (ReferenceEquals(_lastTrailOverlay, target))
            {
                return;
            }

            _lastTrailOverlay?.EmitUp(_isTouchLikeInput);
            _lastTrailOverlay = target;
            if (target == null && !_isPrimaryPointerDown) ClearTrailHistory();
        }

        private OverlayWindow? ResolveTargetOverlay(int x, int y)
        {
            foreach (OverlayWindow overlay in _overlays.Values)
            {
                if (overlay.ContainsScreenPoint(x, y)) return overlay;
            }

            ScreenInfo? nearest = ScreenInfo.FromPoint(x, y);
            if (nearest == null)
            {
                foreach (OverlayWindow overlay in _overlays.Values) return overlay;
                return null;
            }

            if (_overlays.TryGetValue(nearest.DeviceName, out OverlayWindow? byDevice))
            {
                return byDevice;
            }

            foreach (OverlayWindow overlay in _overlays.Values)
            {
                if (overlay.ContainsScreenPoint(nearest.BoundsLeft, nearest.BoundsTop)) return overlay;
            }
            return null;
        }

        private void RebuildWindows(bool forceRebuild)
        {
            IReadOnlyList<ScreenInfo> screens = ScreenInfo.AllScreens;
            if (screens.Count == 0)
            {
                RestartDisplaySettingsRecoveryTimer();
                return;
            }

            var screenInfos = screens
                .Select(screen => new { Screen = screen, Identity = ScreenIdentity.FromScreen(screen) })
                .ToList();
            var enabledIds = ConfigManager.ResolveEnabledScreenDeviceNames(screenInfos.Select(item => item.Identity));

            var targetScreens = screenInfos
                .Where(item => enabledIds.Contains(item.Screen.DeviceName))
                .Select(item => item.Screen)
                .ToDictionary(screen => screen.DeviceName, screen => screen, StringComparer.OrdinalIgnoreCase);

            SynchronizeWindows(targetScreens, forceRebuild);
            _lastDisplayScreens = screens;
        }

        private void SynchronizeWindows(IReadOnlyDictionary<string, ScreenInfo> targetScreens, bool forceRebuild)
        {
            bool topologyChanged = forceRebuild ||
                _overlays.Keys.Except(targetScreens.Keys, StringComparer.OrdinalIgnoreCase).Any() ||
                targetScreens.Keys.Except(_overlays.Keys, StringComparer.OrdinalIgnoreCase).Any() ||
                targetScreens.Any(pair => _overlays.TryGetValue(pair.Key, out OverlayWindow? overlay)
                    && !overlay.MatchesScreenBounds(pair.Value));
            if (topologyChanged)
            {
                ReleasePointerStateSilent();
            }

            if (forceRebuild)
            {
                CloseWindows();
            }

            foreach (string staleKey in _overlays.Keys.Except(targetScreens.Keys, StringComparer.OrdinalIgnoreCase).ToList())
            {
                CloseOverlay(staleKey);
            }

            foreach (var pair in targetScreens)
            {
                int refreshRate = _followDisplayRefreshRate
                    ? ScreenIdentity.GetRefreshRate(pair.Key, _manualTrailRefreshRate)
                    : _manualTrailRefreshRate;
                if (_overlays.TryGetValue(pair.Key, out OverlayWindow? existing))
                {
                    existing.UpdateScreen(pair.Value);
                    existing.UpdateTrailRefreshRate(refreshRate);
                    continue;
                }

                var win = new OverlayWindow(pair.Value, refreshRate);
                _overlays[pair.Key] = win;
                win.SetEnvironmentSuppressed(_isSuppressedByEnvironment);
                if (_screenshotCaptureSessionActive) win.SetHiddenForExternalScreenshotCapture(true);
                if (App.IsEffectsPaused) win.RefreshPauseState();
            }
        }

        private void CloseWindows()
        {
            foreach (string deviceName in _overlays.Keys.ToList())
            {
                CloseOverlay(deviceName);
            }
        }

        private void CloseOverlay(string deviceName)
        {
            if (!_overlays.TryGetValue(deviceName, out OverlayWindow? overlay))
            {
                return;
            }

            // Cancel before disposal because a closed WebView can no longer clear retained trail state.
            overlay.EmitCancel();
            _trailParticipants.Remove(overlay);
            if (ReferenceEquals(_activePointerOverlay, overlay))
            {
                ResetPrimaryPointerState();
            }
            if (ReferenceEquals(_lastTrailOverlay, overlay))
            {
                _lastTrailOverlay = null;
            }
            if (ReferenceEquals(_lastTrailThrottleOverlay, overlay))
            {
                _lastTrailThrottleOverlay = null;
                _lastMoveTimestamp = 0;
            }

            try
            {
                overlay.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to close overlay for '{deviceName}': {ex.Message}");
            }
            _overlays.Remove(deviceName);
        }

        private bool ShouldSuppressEffects(bool forceRefresh = false, NativeMethods.POINT? inputPoint = null)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (!ConfigManager.EnableEnvironmentFilter)
            {
                UpdateSuppressionState(nowTicks, false);
                return false;
            }

            NativeMethods.POINT pt;
            if ((inputPoint ?? (_isTouchLikeInput ? _touchPoint : null)) is NativeMethods.POINT touchPoint) pt = touchPoint;
            else NativeMethods.GetCursorPos(out pt);
            IntPtr cursorHwnd = NativeMethods.WindowFromPoint(pt);
            IntPtr targetWindow = NativeMethods.GetAncestor(cursorHwnd, NativeMethods.GA_ROOT);

            if (targetWindow == IntPtr.Zero || IsOverlayWindow(targetWindow))
            {
                targetWindow = NativeMethods.GetForegroundWindow();
            }

            if (targetWindow != _lastForegroundWindow)
            {
                forceRefresh = true;
                _lastForegroundWindow = targetWindow;
            }
            if (!forceRefresh && nowTicks < _suppressionCacheValidUntilTicks)
            {
                return _isSuppressedByEnvironment;
            }

            string className = GetWindowClassName(targetWindow);
            if (string.IsNullOrEmpty(className))
            {
                className = GetWindowClassName(NativeMethods.GetForegroundWindow());
            }

            bool isDesktop = string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);
            if (isDesktop)
            {
                UpdateSuppressionState(nowTicks, !ConfigManager.ShowEffectOnDesktop);
                return _isSuppressedByEnvironment;
            }

            if (IsCurrentProcessWindow(targetWindow))
            {
                UpdateSuppressionState(nowTicks, false);
                return false;
            }

            if (!TryGetForegroundProcessName(targetWindow, out string processName))
            {
                if (!TryGetForegroundProcessName(NativeMethods.GetForegroundWindow(), out processName))
                {
                    UpdateSuppressionState(nowTicks, false);
                    return false;
                }
            }

            IntPtr actualForeground = NativeMethods.GetForegroundWindow();
            if (ConfigManager.HideInFullscreen && IsEffectiveFullscreenWindow(actualForeground))
            {
                UpdateSuppressionState(nowTicks, true);
                return true;
            }

            bool isSuppressedByProcessFilter = IsSuppressedByProcessFilter(processName);
            UpdateSuppressionState(nowTicks, isSuppressedByProcessFilter);
            return _isSuppressedByEnvironment;
        }

        private bool IsOverlayWindow(IntPtr hwnd)
        {
            return _overlays.Values.Any(o => o.Handle == hwnd);
        }

        private static bool IsCurrentProcessWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == (uint)Environment.ProcessId;
        }

        private static bool IsSuppressedByProcessFilter(string processName)
        {
            var profile = ConfigManager.GetActiveProfile();
            if (profile == null || profile.Mode == ProcessFilterModeOption.Disabled)
            {
                return false;
            }

            bool isListed = profile.Processes.Contains(processName, StringComparer.OrdinalIgnoreCase);
            return profile.Mode switch
            {
                ProcessFilterModeOption.Blacklist => isListed,
                ProcessFilterModeOption.Whitelist => !isListed,
                _ => false
            };
        }

        private static void UpdateSuppressionState(long nowTicks, bool isSuppressed, ref bool suppressed, ref long cacheUntil)
        {
            suppressed = isSuppressed;
            cacheUntil = nowTicks + SuppressionCacheDurationTicks;
        }

        private void UpdateSuppressionState(long nowTicks, bool isSuppressed)
        {
            bool changed = _isSuppressedByEnvironment != isSuppressed;
            UpdateSuppressionState(nowTicks, isSuppressed, ref _isSuppressedByEnvironment, ref _suppressionCacheValidUntilTicks);
            if (changed)
            {
                ApplySuppressionSideEffects(isSuppressed);
            }
        }

        private void ApplySuppressionSideEffects(bool isSuppressed)
        {
            if (isSuppressed)
            {
                ReleasePointerStateForEnvironmentSuppression();
            }

            ForEachOverlay(w => w.SetEnvironmentSuppressed(isSuppressed));
        }

        private bool TryGetForegroundProcessName(IntPtr hwnd, out string processName)
        {
            processName = string.Empty;
            if (!IsEligibleForegroundWindow(hwnd))
            {
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                return false;
            }

            processName = GetProcessExecutableName(processId);
            return !string.IsNullOrWhiteSpace(processName);
        }

        private bool IsEligibleForegroundWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || IsOverlayWindow(hwnd))
            {
                return false;
            }
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return false;
            }
            if (hwnd == NativeMethods.GetDesktopWindow() || hwnd == NativeMethods.GetShellWindow())
            {
                return false;
            }

            string className = GetWindowClassName(hwnd);
            return !string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetWindowClassName(IntPtr hwnd) => NativeMethods.GetWindowClassName(hwnd);

        private static string GetProcessExecutableName(uint processId) =>
            NativeMethods.GetProcessExecutableName(processId);

        private static bool IsEffectiveFullscreenWindow(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT windowRect))
            {
                return false;
            }

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new NativeMethods.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };
            if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            return Math.Abs(windowRect.Left - monitorInfo.rcMonitor.Left) <= FullscreenTolerance &&
                   Math.Abs(windowRect.Top - monitorInfo.rcMonitor.Top) <= FullscreenTolerance &&
                   Math.Abs(windowRect.Right - monitorInfo.rcMonitor.Right) <= FullscreenTolerance &&
                   Math.Abs(windowRect.Bottom - monitorInfo.rcMonitor.Bottom) <= FullscreenTolerance;
        }

        private static bool TryGetPhysicalCursorPosition(out int x, out int y) =>
            NativeMethods.TryGetCursorPosition(out x, out y);

        private void HandleDisplaySettingsChanged(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            if (_disposed)
            {
                return;
            }

            App.DispatcherQueue.TryEnqueue(RestartDisplaySettingsRecoveryTimer);
        }

        private void RestartDisplaySettingsRecoveryTimer()
        {
            if (_disposed)
            {
                return;
            }

            if (_displaySettingsRecoveryTimer == null)
            {
                _displaySettingsRecoveryTimer = App.DispatcherQueue.CreateTimer();
                _displaySettingsRecoveryTimer.Interval =
                    TimeSpan.FromMilliseconds(DisplaySettingsRecoveryDebounceMilliseconds);
                _displaySettingsRecoveryTimer.Tick += DisplaySettingsRecoveryTimer_Tick;
            }

            _displaySettingsRecoveryTimer.Stop();
            _displaySettingsRecoveryTimer.Start();
        }

        private void DisplaySettingsRecoveryTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            _ = sender;
            _ = args;
            _displaySettingsRecoveryTimer?.Stop();
            if (!_disposed)
            {
                RebuildWindows(forceRebuild: false);
                RefreshEnvironmentFilterState();
            }
        }

        private void DisplayTopologyTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            _ = sender;
            _ = args;
            if (_disposed)
            {
                return;
            }

            IReadOnlyList<ScreenInfo> screens = ScreenInfo.AllScreens;
            if (screens.Count > 0 && (screens.Count != _lastDisplayScreens.Count
                || screens.Any(screen => !_lastDisplayScreens.Contains(screen))))
            {
                RebuildWindows(forceRebuild: false);
                RefreshEnvironmentFilterState();
            }
        }

        private void HandlePowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                ScheduleResumeRecovery();
            }
        }

        private void HandleSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                ScheduleResumeRecovery();
            }
        }

        private void ScheduleResumeRecovery()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - _lastResumeRecoveryTicks < ResumeRecoveryDebounceTicks)
            {
                return;
            }

            _lastResumeRecoveryTicks = nowTicks;
            System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
            {
                var dispatcher = App.DispatcherQueue;
                if (dispatcher == null) return;

                dispatcher.TryEnqueue(() =>
                {
                    if (_disposed) return;
                    try
                    {
                        RecoverAfterSystemResume();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Resume recovery failed: " + ex.Message);
                    }
                });
            });
        }

        private void RecoverAfterSystemResume()
        {
            UpdateTrailRefreshRate(ConfigManager.TrailRefreshRate, ConfigManager.FollowDisplayRefreshRate);
            RebuildWindows(forceRebuild: true);
            SetupGlobalHooks();
            RefreshEnvironmentFilterState();
        }

        private void ForEachOverlay(Action<OverlayWindow> action)
        {
            foreach (var overlay in _overlays.Values.ToList())
            {
                action(overlay);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
            SystemEvents.SessionSwitch -= HandleSessionSwitch;
            if (_displayTopologyTimer != null)
            {
                _displayTopologyTimer.Stop();
                _displayTopologyTimer.Tick -= DisplayTopologyTimer_Tick;
                _displayTopologyTimer = null;
            }
            if (_displaySettingsRecoveryTimer != null)
            {
                _displaySettingsRecoveryTimer.Stop();
                _displaySettingsRecoveryTimer.Tick -= DisplaySettingsRecoveryTimer_Tick;
                _displaySettingsRecoveryTimer = null;
            }
            TeardownScreenshotCompatCaptureSurfaces();
            EndScreenshotCaptureSession();
            UninstallMouseHook();
            if (_globalHook != null)
            {
                _globalHook.Dispose();
                _globalHook = null;
            }

            UninstallForegroundWinEventHook();

            CloseWindows();
        }
    }

    internal enum TouchInputPhase { Down, Move, Up }

    internal sealed class TouchInputListener : NativeWindow, IDisposable
    {
        private readonly Action<TouchInputPhase, int, int> _input;
        private readonly Action<IntPtr, uint, bool, bool, int, int> _contactInput;
        private readonly TouchCursorState _cursorState = new();
        private readonly NativeMethods.WinEventProc _cursorEventProc;
        private IntPtr _cursorWinEventHook;
        private IntPtr _cursorNameEventHook;
        private readonly Dictionary<IntPtr, TouchDevice?> _devices = new();
        private byte[] _buffer = Array.Empty<byte>();
        private (IntPtr Device, uint Id)? _primary;
        private int _lastX;
        private int _lastY;
        private int _minX;
        private int _minY;
        private int _maxX;
        private int _maxY;
        private long _lastTouchTime;
        private bool _hasRecentTouch;
        private bool _touchSuppressesCursor;
        private bool _hasMouseCursor;

        internal TouchInputListener(Action<TouchInputPhase, int, int> input)
        {
            _input = input;
            _contactInput = HandleContact;
            _cursorEventProc = CursorWinEventProc;
            CreateHandle(new CreateParams { Caption = "BASpark.TouchInput", Parent = new IntPtr(-3) });
            var device = new NativeMethods.RAWINPUTDEVICE { UsagePage = 13, Usage = 4, Flags = 0x2100, Target = Handle };
            if (!NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
            {
                Debug.WriteLine("Touch input registration failed: " + Marshal.GetLastWin32Error());
                DestroyHandle();
            }
            if (Handle != IntPtr.Zero)
            {
                _cursorWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_SHOW,
                    NativeMethods.EVENT_OBJECT_HIDE, IntPtr.Zero, _cursorEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
                _cursorNameEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_NAMECHANGE,
                    NativeMethods.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _cursorEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
                ObserveCursor();
            }
        }

        internal void ObserveCursor(bool mouseInput = false)
        {
            if (mouseInput)
            {
                _touchSuppressesCursor = false;
                _hasMouseCursor = true;
            }
            if (_primary != null) return;
            var cursor = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (NativeMethods.GetCursorInfo(out cursor))
            {
                if (_touchSuppressesCursor && cursor.hCursor == IntPtr.Zero) return;
                var target = ResolveCursorWindow(cursor.ptScreenPos);
                if (target.Chromium && !_hasMouseCursor && cursor.hCursor == IntPtr.Zero) return;
                _cursorState.Observe(cursor, target.Window);
            }
        }

        internal bool IsCursorVisible(NativeMethods.POINT point)
        {
            var cursor = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (!NativeMethods.GetCursorInfo(out cursor))
            {
                _cursorState.Reset();
                return false;
            }
            var target = ResolveCursorWindow(point);
            return _cursorState.IsVisible(cursor, target.Window, target.Chromium);
        }

        private static (IntPtr Window, bool Chromium) ResolveCursorWindow(NativeMethods.POINT point)
        {
            IntPtr window = NativeMethods.WindowFromPoint(point);
            string className = NativeMethods.GetWindowClassName(window);
            bool chromium = className is "Chrome_RenderWidgetHostHWND" or "Chrome_WidgetWin_0" or "Chrome_WidgetWin_1";
            if (chromium)
            {
                IntPtr root = NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
                if (root != IntPtr.Zero) window = root;
            }
            return (window, chromium);
        }

        private void CursorWinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
            uint threadId, uint eventTime)
        {
            if (objectId == -9) ObserveCursor();
        }

        internal bool ShouldIgnorePromotedTouch(int screenX, int screenY) => _hasRecentTouch
            && (_primary != null || Environment.TickCount64 - _lastTouchTime <= 1000)
            && screenX >= _minX - 24 && screenX <= _maxX + 24 && screenY >= _minY - 24 && screenY <= _maxY + 24;

        protected override void WndProc(ref Message message)
        {
            try
            {
                if (message.Msg == 0x00FF) ReadInput(message.LParam);
                else if (message.Msg == 0x00FE)
                {
                    _devices.Remove(message.LParam);
                    if (_primary?.Device == message.LParam)
                    {
                        _primary = null;
                        _cursorState.EndContact();
                        _input(TouchInputPhase.Up, _lastX, _lastY);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Touch input routing failed: " + exception.Message);
            }
            base.WndProc(ref message);
        }

        private void ReadInput(IntPtr input)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            if (NativeMethods.GetRawInputData(input, 0x10000003, null, ref size, headerSize) == uint.MaxValue
                || size < headerSize + 8 || size > 1024 * 1024) return;
            if (_buffer.Length < size) _buffer = new byte[size];
            if (NativeMethods.GetRawInputData(input, 0x10000003, _buffer, ref size, headerSize) != size) return;
            var header = MemoryMarshal.Read<NativeMethods.RAWINPUTHEADER>(_buffer);
            if (header.Type != 2) return;
            if (!_devices.TryGetValue(header.Device, out TouchDevice? device))
            {
                device = TouchDevice.TryCreate(header.Device);
                _devices[header.Device] = device;
            }
            if (device == null) return;
            int reportSize = BitConverter.ToInt32(_buffer, (int)headerSize);
            int reportCount = BitConverter.ToInt32(_buffer, (int)headerSize + 4);
            if (reportSize <= 0 || reportCount <= 0 || (long)reportSize * reportCount > size - headerSize - 8) return;
            for (int index = 0; index < reportCount; index++)
                device.ReadReport(_buffer, (int)headerSize + 8 + index * reportSize, reportSize, _contactInput);
        }

        private void HandleContact(IntPtr device, uint id, bool wasDown, bool down, int screenX, int screenY)
        {
            var contact = (device, id);
            if (!wasDown && down && _primary == null)
            {
                _primary = contact;
                var target = ResolveCursorWindow(new NativeMethods.POINT { x = screenX, y = screenY });
                _cursorState.BeginContact(target.Window, target.Chromium);
                _minX = _maxX = screenX;
                _minY = _maxY = screenY;
            }
            else if (_primary != contact) return;
            _lastX = screenX;
            _lastY = screenY;
            _minX = Math.Min(_minX, screenX);
            _minY = Math.Min(_minY, screenY);
            _maxX = Math.Max(_maxX, screenX);
            _maxY = Math.Max(_maxY, screenY);
            _lastTouchTime = Environment.TickCount64;
            _hasRecentTouch = true;
            _touchSuppressesCursor = true;
            if (!down)
            {
                _primary = null;
                _cursorState.EndContact();
            }
            _input(down ? wasDown ? TouchInputPhase.Move : TouchInputPhase.Down : TouchInputPhase.Up, screenX, screenY);
        }

        public void Dispose()
        {
            if (_cursorNameEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_cursorNameEventHook);
                _cursorNameEventHook = IntPtr.Zero;
            }
            if (_cursorWinEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_cursorWinEventHook);
                _cursorWinEventHook = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero)
            {
                var device = new NativeMethods.RAWINPUTDEVICE { UsagePage = 13, Usage = 4, Flags = 1 };
                NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
                DestroyHandle();
            }
            _primary = null;
            _cursorState.Reset();
            _devices.Clear();
            _buffer = Array.Empty<byte>();
        }

        internal static NativeMethods.POINT MapPoint(double normalizedX, double normalizedY, uint orientation, NativeMethods.RECT display)
        {
            (normalizedX, normalizedY) = orientation switch
            {
                2 => (1 - normalizedY, normalizedX), 3 => (1 - normalizedX, 1 - normalizedY),
                4 => (normalizedY, 1 - normalizedX), _ => (normalizedX, normalizedY)
            };
            return new NativeMethods.POINT
            {
                x = display.Left + (int)Math.Round(Math.Clamp(normalizedX, 0, 1) * Math.Max(0, display.Width - 1)),
                y = display.Top + (int)Math.Round(Math.Clamp(normalizedY, 0, 1) * Math.Max(0, display.Height - 1))
            };
        }

        internal sealed class TouchDevice
        {
            private readonly IntPtr _device;
            private readonly byte[] _preparsed;
            private readonly byte[] _report;
            private readonly NativeMethods.HIDP_DATA[] _data;
            private readonly Dictionary<ushort, uint> _values = new();
            private readonly Dictionary<uint, (Contact Contact, uint RawX, uint RawY)> _activeCollections = new();
            private readonly Dictionary<uint, (bool Down, uint RawX, uint RawY, Contact Contact)> _frame = new();
            private readonly Contact[] _contacts;
            private uint _orientation;

            private readonly record struct Contact(byte ReportId, ushort Collection, NativeMethods.HIDP_VALUE_CAPS AxisX,
                NativeMethods.HIDP_VALUE_CAPS AxisY, ushort Tip, ushort? Id);

            private TouchDevice(IntPtr device, byte[] preparsed, NativeMethods.HIDP_CAPS caps, Contact[] contacts)
            {
                _device = device;
                _preparsed = preparsed;
                _report = new byte[caps.InputReportByteLength];
                _data = new NativeMethods.HIDP_DATA[caps.NumberInputDataIndices];
                _contacts = contacts;
            }

            internal static TouchDevice? TryCreate(IntPtr device)
            {
                uint size = 0;
                if (NativeMethods.GetRawInputDeviceInfo(device, 0x20000005, null, ref size) == uint.MaxValue
                    || size == 0 || size > 1024 * 1024) return null;
                var preparsed = new byte[size];
                if (NativeMethods.GetRawInputDeviceInfo(device, 0x20000005, preparsed, ref size) == uint.MaxValue
                    || NativeMethods.HidP_GetCaps(preparsed, out var caps) != 0x110000
                    || caps.UsagePage != 13 || caps.Usage != 4 || caps.InputReportByteLength == 0) return null;
                var values = new NativeMethods.HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
                var buttons = new NativeMethods.HIDP_BUTTON_CAPS[caps.NumberInputButtonCaps];
                ushort valueCount = caps.NumberInputValueCaps;
                ushort buttonCount = caps.NumberInputButtonCaps;
                if (NativeMethods.HidP_GetValueCaps(0, values, ref valueCount, preparsed) != 0x110000
                    || NativeMethods.HidP_GetButtonCaps(0, buttons, ref buttonCount, preparsed) != 0x110000) return null;
                return Create(device, preparsed, caps, values.Take(valueCount).ToArray(), buttons.Take(buttonCount).ToArray());
            }

            internal static TouchDevice? Create(IntPtr device, byte[] preparsed, NativeMethods.HIDP_CAPS caps,
                NativeMethods.HIDP_VALUE_CAPS[] values, NativeMethods.HIDP_BUTTON_CAPS[] buttons)
            {
                var contacts = new List<Contact>();
                foreach (var tip in buttons.Where(cap => cap.UsagePage == 13 && ContainsUsage(cap.UsageMin, cap.UsageMax, cap.IsRange, 0x42)))
                {
                    var axes = values.Where(cap => cap.ReportId == tip.ReportId && cap.LinkCollection == tip.LinkCollection).ToArray();
                    var axisX = axes.FirstOrDefault(cap => cap.UsagePage == 1 && ContainsUsage(cap.UsageMin, cap.UsageMax, cap.IsRange, 0x30));
                    var axisY = axes.FirstOrDefault(cap => cap.UsagePage == 1 && ContainsUsage(cap.UsageMin, cap.UsageMax, cap.IsRange, 0x31));
                    if (axisX.LogicalMax <= axisX.LogicalMin || axisY.LogicalMax <= axisY.LogicalMin || axisX.IsAbsolute == 0 || axisY.IsAbsolute == 0) continue;
                    axisX.DataIndexMin += (ushort)(0x30 - axisX.UsageMin);
                    axisY.DataIndexMin += (ushort)(0x31 - axisY.UsageMin);
                    var identifier = axes.FirstOrDefault(cap => cap.UsagePage == 13 && ContainsUsage(cap.UsageMin, cap.UsageMax, cap.IsRange, 0x51));
                    contacts.Add(new Contact(tip.ReportId, tip.LinkCollection, axisX, axisY,
                        (ushort)(tip.DataIndexMin + 0x42 - tip.UsageMin), identifier.UsagePage == 13
                            ? (ushort)(identifier.DataIndexMin + 0x51 - identifier.UsageMin) : null));
                }
                return contacts.Count == 0 ? null : new TouchDevice(device, preparsed, caps, contacts.ToArray());
            }

            private static bool ContainsUsage(ushort minimum, ushort maximum, byte range, ushort usage) =>
                minimum == usage || (range != 0 && usage >= minimum && usage <= maximum);

            internal void ReadReport(byte[] buffer, int offset, int length, Action<IntPtr, uint, bool, bool, int, int> contactInput)
            {
                if (length != _report.Length) return;
                Buffer.BlockCopy(buffer, offset, _report, 0, length);
                uint count = (uint)_data.Length;
                if (NativeMethods.HidP_GetData(0, _data, ref count, _preparsed, _report, (uint)length) != 0x110000) return;
                if (!NativeMethods.GetPointerDeviceRects(_device, out _, out var display) || display.Width <= 0 || display.Height <= 0) return;
                if (_activeCollections.Count == 0 && NativeMethods.GetPointerDevice(_device, out var info)) _orientation = info.DisplayOrientation;
                ProcessData(_report[0], _data.AsSpan(0, (int)count), display, _orientation, contactInput);
            }

            internal void ProcessData(byte reportId, ReadOnlySpan<NativeMethods.HIDP_DATA> data, NativeMethods.RECT display,
                uint orientation, Action<IntPtr, uint, bool, bool, int, int> contactInput)
            {
                _values.Clear();
                foreach (var item in data) _values[item.DataIndex] = item.Value;
                _frame.Clear();
                foreach (Contact contact in _contacts)
                {
                    if (contact.ReportId != reportId
                        || !_values.TryGetValue(contact.AxisX.DataIndexMin, out uint rawX)
                        || !_values.TryGetValue(contact.AxisY.DataIndexMin, out uint rawY)) continue;
                    uint id = contact.Id is ushort identifier ? _values.GetValueOrDefault(identifier) : contact.Collection;
                    bool down = _values.GetValueOrDefault(contact.Tip) != 0;
                    if (!_frame.TryGetValue(id, out var existing) || down
                        || (!existing.Down && _activeCollections.GetValueOrDefault(id).Contact.Collection == contact.Collection))
                        _frame[id] = (down, rawX, rawY, contact);
                }
                foreach (var pair in _activeCollections)
                {
                    var previous = pair.Value;
                    if (previous.Contact.ReportId != reportId || (_frame.TryGetValue(pair.Key, out var sample) && sample.Down)) continue;
                    if (!_frame.ContainsKey(pair.Key))
                        _frame[pair.Key] = (false, previous.RawX, previous.RawY, previous.Contact);
                }
                foreach (var pair in _frame)
                {
                    var sample = pair.Value;
                    if (sample.Down || !_activeCollections.Remove(pair.Key)) continue;
                    var point = MapPoint(Normalize(sample.RawX, sample.Contact.AxisX), Normalize(sample.RawY, sample.Contact.AxisY), orientation, display);
                    contactInput(_device, pair.Key, true, false, point.x, point.y);
                }
                foreach (var pair in _frame)
                {
                    uint id = pair.Key;
                    var sample = pair.Value;
                    if (!sample.Down) continue;
                    bool wasDown = _activeCollections.ContainsKey(id);
                    _activeCollections[id] = (sample.Contact, sample.RawX, sample.RawY);
                    var point = MapPoint(Normalize(sample.RawX, sample.Contact.AxisX), Normalize(sample.RawY, sample.Contact.AxisY), orientation, display);
                    contactInput(_device, id, wasDown, true, point.x, point.y);
                }
            }

            private static double Normalize(uint value, NativeMethods.HIDP_VALUE_CAPS axis)
            {
                long signed = axis.LogicalMin < 0 && axis.BitSize is > 0 and <= 32
                    ? (int)(value << (32 - axis.BitSize)) >> (32 - axis.BitSize) : value;
                return (signed - (double)axis.LogicalMin) / ((double)axis.LogicalMax - axis.LogicalMin);
            }
        }
    }
}
