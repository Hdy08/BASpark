using System.Windows.Forms;
using Microsoft.Win32;
using WinFormsApp = System.Windows.Forms.Application;

namespace BASpark;

/// <summary>
/// 系统托盘图标与右键菜单。
///
/// WinUI 3 没有托盘 API，因此这里在一条独立的 STA 线程上运行 WinForms 的
/// <see cref="NotifyIcon"/>。菜单刻意使用系统原生渲染器（<c>SystemRenderer</c>）：
/// 由 Windows 决定背景、悬停高亮与分隔线，与系统托盘菜单完全一致，
/// 不再像迁移前那样自绘一套浅色/深色配色。
/// 菜单命令一律通过 <see cref="App.DispatcherQueue"/> 回到 UI 线程执行。
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _openPanelItem;
    private ToolStripMenuItem? _restartItem;
    private ToolStripMenuItem? _exitItem;
    private Action? _openPanel;
    private Action? _restart;
    private Action? _exit;
    private bool _disposed;

    public void Initialize(Action openPanel, Action restart, Action exit)
    {
        _openPanel = openPanel;
        _restart = restart;
        _exit = exit;

        _thread = new Thread(TrayThreadMain)
        {
            IsBackground = true,
            Name = "BASpark.Tray"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // 等待图标真正建立，避免启动早期托盘短暂缺失。
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void TrayThreadMain()
    {
        try
        {
            WinFormsApp.EnableVisualStyles();
            WinFormsApp.SetCompatibleTextRenderingDefault(false);

            _menu = new ContextMenuStrip
            {
                // 跟随系统：深浅色、高亮、圆角、阴影全部交给 Windows。
                Renderer = new ToolStripSystemRenderer(),
                ShowImageMargin = false
            };

            _openPanelItem = new ToolStripMenuItem(Localization.Get("Tray_OpenPanel"));
            _openPanelItem.Click += (_, _) => Dispatch(_openPanel);

            _restartItem = new ToolStripMenuItem(Localization.Get("Tray_Restart"));
            _restartItem.Click += (_, _) => Dispatch(_restart);

            _exitItem = new ToolStripMenuItem(Localization.Get("Tray_Exit"));
            _exitItem.Click += (_, _) => Dispatch(_exit);

            _menu.Items.Add(_openPanelItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_restartItem);
            _menu.Items.Add(_exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = Localization.Get("Tray_Text"),
                ContextMenuStrip = _menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => Dispatch(_openPanel);

            _openPanelItem.Font = new System.Drawing.Font(_openPanelItem.Font, System.Drawing.FontStyle.Bold);
            _restartItem.Font = new System.Drawing.Font(_restartItem.Font, System.Drawing.FontStyle.Bold);
            _exitItem.Font = new System.Drawing.Font(_exitItem.Font, System.Drawing.FontStyle.Bold);
        }
        catch (Exception ex)
        {
            AppLogger.Error("托盘图标初始化失败。", ex);
        }
        finally
        {
            _ready.Set();
        }

        WinFormsApp.Run();

        // Run() 返回后清理原生资源。
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
        _menu = null;
    }

    /// <summary>把托盘线程上的点击转回 WinUI UI 线程。</summary>
    private static void Dispatch(Action? action)
    {
        if (action == null)
        {
            return;
        }

        App.DispatcherQueue.TryEnqueue(() => action());
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                System.Drawing.Icon? extracted =
                    System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"Failed to extract the application icon: {ex.Message}");
        }

        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>语言或系统主题变化后刷新托盘文本。</summary>
    public void RefreshLocalization()
    {
        if (_disposed || _menu == null)
        {
            return;
        }

        _menu.BeginInvoke(new Action(() =>
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = Localization.Get("Tray_Text");
            }

            if (_openPanelItem != null) _openPanelItem.Text = Localization.Get("Tray_OpenPanel");
            if (_restartItem != null) _restartItem.Text = Localization.Get("Tray_Restart");
            if (_exitItem != null) _exitItem.Text = Localization.Get("Tray_Exit");
        }));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }

        try
        {
            // 结束托盘线程的消息循环，让 TrayThreadMain 走完清理路径。
            _menu?.BeginInvoke(new Action(WinFormsApp.ExitThread));
        }
        catch
        {
            // 线程可能已经结束。
        }

        _ready.Dispose();
    }
}
