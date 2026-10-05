using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinFormsApp = System.Windows.Forms.Application;

namespace BASpark;

/// <summary>
/// 系统托盘图标与右键菜单。
/// WinUI 3 没有托盘 API，因此在独立 STA 线程上运行 NotifyIcon。
/// 右键菜单用 Win32 HMENU 交给 Windows 绘制，并跟随系统主题。
/// 菜单命令通过 App.DispatcherQueue 回到 UI 线程执行。
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private const string TrayWindowTitle = "BASpark.Tray.CommandWindow";
    private const string HiddenTrayWindowTitle = "BASpark.Tray.CommandWindow.Hidden";
    private static readonly uint OpenPanelMessage = RegisterWindowMessage("BASpark.OpenHiddenControlPanel");
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private NotifyIcon? _notifyIcon;
    private TrayMessageWindow? _messageWindow;
    private NativeMenuTheme? _menuTheme;
    private Action? _openPanel;
    private Action? _restart;
    private Action? _exit;
    private bool _disposed;

    public void Initialize(Action openPanel, Action restart, Action exit)
    {
        _openPanel = openPanel;
        _restart = restart;
        _exit = exit;
        _thread = new Thread(TrayThreadMain) { IsBackground = true, Name = "BASpark.Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void TrayThreadMain()
    {
        try
        {
            WinFormsApp.EnableVisualStyles();
            WinFormsApp.SetCompatibleTextRenderingDefault(false);
            _messageWindow = new TrayMessageWindow(() => Dispatch(_openPanel))
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                Text = ConfigManager.HideTrayIcon ? HiddenTrayWindowTitle : TrayWindowTitle
            };
            _ = _messageWindow.Handle;
            ChangeWindowMessageFilterEx(_messageWindow.Handle, OpenPanelMessage, 1, 0);
            _menuTheme = new NativeMenuTheme();
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = Localization.Get("Tray_Text"),
                Visible = !ConfigManager.HideTrayIcon
            };
            _notifyIcon.DoubleClick += (_, _) => Dispatch(_openPanel);
            _notifyIcon.MouseUp += (_, args) =>
            {
                if (args.Button == MouseButtons.Right) ShowNativeMenu();
            };
            _ready.Set();
            WinFormsApp.Run();
        }
        catch (Exception)
        {
        }
        finally
        {
            _ready.Set();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            _messageWindow?.Dispose();
            _messageWindow = null;
            _menuTheme?.Dispose();
            _menuTheme = null;
        }
    }

    private static nint CreateNativeMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (!AppendMenu(menu, 0, 1, Localization.Get("Tray_OpenPanel")) ||
            !AppendMenu(menu, 0x800, 0, null) ||
            !AppendMenu(menu, 0, 4, Localization.Get(App.IsEffectsPaused ? "Effects_Enable" : "Effects_Pause")) ||
            !AppendMenu(menu, 0, 2, Localization.Get("Tray_Restart")) ||
            !AppendMenu(menu, 0, 3, Localization.Get("Tray_Exit")))
        {
            int error = Marshal.GetLastWin32Error();
            DestroyMenu(menu);
            throw new System.ComponentModel.Win32Exception(error);
        }
        return menu;
    }

    private void ShowNativeMenu()
    {
        if (_disposed || _messageWindow == null || !GetCursorPos(out NativePoint cursor)) return;
        nint menu = 0;
        try
        {
            _menuTheme?.Apply(_messageWindow.Handle);
            menu = CreateNativeMenu();
            SetForegroundWindow(_messageWindow.Handle);
            uint command = TrackPopupMenuEx(menu, 0x102, cursor.Left, cursor.Top, _messageWindow.Handle, 0);
            PostMessage(_messageWindow.Handle, 0, 0, 0);
            Dispatch(command switch { 1 => _openPanel, 2 => _restart, 3 => _exit, 4 => App.ToggleEffectsPaused, _ => null });
        }
        catch (Exception)
        {
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
        }
    }

    private static void Dispatch(Action? action)
    {
        if (action != null) App.DispatcherQueue.TryEnqueue(() => action());
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { Length: > 0 } path &&
                System.Drawing.Icon.ExtractAssociatedIcon(path) is { } icon) return icon;
        }
        catch (Exception)
        {
        }
        return System.Drawing.SystemIcons.Application;
    }

    private void InvokeOnTray(Action action)
    {
        var window = _messageWindow;
        if (window == null || window.IsDisposed || !window.IsHandleCreated) return;
        try { window.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    public void SetHidden(bool hidden)
    {
        if (_disposed) return;
        InvokeOnTray(() =>
        {
            if (_notifyIcon != null) _notifyIcon.Visible = !hidden;
            if (_messageWindow != null) _messageWindow.Text = hidden ? HiddenTrayWindowTitle : TrayWindowTitle;
        });
    }

    public static bool TryShowExistingControlPanel()
    {
        nint window = FindWindow(null, HiddenTrayWindowTitle);
        if (window == 0) window = FindWindow(null, TrayWindowTitle);
        if (window == 0 || OpenPanelMessage == 0) return false;
        GetWindowThreadProcessId(window, out uint processId);
        if (processId != 0) AllowSetForegroundWindow(processId);
        return PostMessage(window, OpenPanelMessage, 0, 0);
    }

    public void RefreshLocalization()
    {
        if (_disposed) return;
        InvokeOnTray(() =>
        {
            if (_notifyIcon != null) _notifyIcon.Text = Localization.Get("Tray_Text");
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvokeOnTray(() => { EndMenu(); WinFormsApp.ExitThread(); });
        if (_thread == null || (_thread.ManagedThreadId != Environment.CurrentManagedThreadId && _thread.Join(TimeSpan.FromSeconds(2)))) _ready.Dispose();
    }

    private sealed class TrayMessageWindow(Action openPanel) : Form
    {
        protected override void WndProc(ref Message message)
        {
            if (OpenPanelMessage != 0 && message.Msg == OpenPanelMessage)
            {
                openPanel();
                return;
            }
            base.WndProc(ref message);
        }
    }

    private sealed class NativeMenuTheme : IDisposable
    {
        private readonly nint _library;
        private readonly SetPreferredAppMode? _setPreferredAppMode;
        private readonly AllowDarkModeForWindow? _allowDarkModeForWindow;
        private readonly RefreshImmersiveColorPolicyState? _refreshColorPolicy;
        private readonly FlushMenuThemes? _flushMenuThemes;
        private readonly ShouldAppsUseDarkMode? _shouldAppsUseDarkMode;

        public NativeMenuTheme()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) ||
                !NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "uxtheme.dll"), out nint library)) return;
            _library = library;
            _setPreferredAppMode = Bind<SetPreferredAppMode>(135);
            _allowDarkModeForWindow = Bind<AllowDarkModeForWindow>(133);
            _refreshColorPolicy = Bind<RefreshImmersiveColorPolicyState>(104);
            _flushMenuThemes = Bind<FlushMenuThemes>(136);
            _shouldAppsUseDarkMode = Bind<ShouldAppsUseDarkMode>(132);
            _setPreferredAppMode?.Invoke(PreferredAppMode.AllowDark);
        }

        private T? Bind<T>(int ordinal) where T : Delegate
        {
            nint address = GetProcAddress(_library, ordinal);
            return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        public void Apply(nint owner)
        {
            _refreshColorPolicy?.Invoke();
            bool dark = !SystemInformation.HighContrast && (_shouldAppsUseDarkMode?.Invoke() ?? false);
            _allowDarkModeForWindow?.Invoke(owner, dark);
            _flushMenuThemes?.Invoke();
        }

        public void Dispose()
        {
            if (_library != 0) NativeLibrary.Free(_library);
        }

        private enum PreferredAppMode { Default, AllowDark, ForceDark, ForceLight }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool AllowDarkModeForWindow(nint window, [MarshalAs(UnmanagedType.U1)] bool allow);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void RefreshImmersiveColorPolicyState();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void FlushMenuThemes();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool ShouldAppsUseDarkMode();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int Left; public int Top; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint identifier, string? text);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int left, int top, nint owner, nint parameters);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint parameter, nint data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(nint window, uint message, uint action, nint status);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetProcAddress(nint library, nint ordinal);
}
