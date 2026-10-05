using Microsoft.Web.WebView2.Core;

namespace BASpark;

/// <summary>
/// 进程内共享的 WebView2 环境。
///
/// 关键约束（多屏场景下最容易踩坑）：**同一个用户数据目录同时只能有一个
/// WebView2 环境**。多显示器会并发初始化多个叠加层，若各自创建环境并指向同
/// 一个目录，只有先到者能成功，其余都会以 E_INVALIDARG
/// （"Value does not fall within the expected range"）失败。
///
/// 正确用法是「一个环境 + 多个控制器」：
///   * 环境按会话唯一（目录名带 GUID），避免与其它进程/历史运行争用；
///   * 所有叠加层共用同一个环境实例，只在控制器创建真正失败时才整体重建。
/// </summary>
internal static class WebView2EnvironmentHolder
{
    private const string UserDataFolderPrefix = "BASpark_WebView2";

    private static CoreWebView2Environment? _environment;
    private static string _userDataFolder = BuildSessionUserDataFolder();
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    /// <summary>当前使用的用户数据目录，用于日志与恢复判断。</summary>
    public static string UserDataFolder => _userDataFolder;

    /// <summary>
    /// 会话唯一目录：进程号 + GUID，保证同一台机器上不会与其它实例或历史
    /// 运行残留争用同一个目录。
    /// </summary>
    private static string BuildSessionUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            $"{UserDataFolderPrefix}_{Environment.ProcessId}_" +
            Guid.NewGuid().ToString("N")[..8]);

    public static async Task<CoreWebView2Environment> GetOrCreateAsync()
    {
        if (_environment != null)
        {
            return _environment;
        }

        await InitLock.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_environment != null)
            {
                return _environment;
            }

            _environment = await CreateAsync(_userDataFolder).ConfigureAwait(true);
            return _environment;
        }
        finally
        {
            InitLock.Release();
        }
    }

    /// <summary>
    /// 丢弃当前环境并换用全新的唯一目录重建。仅在控制器创建真正失败时调用。
    ///
    /// 这里会把目录名换成新的 GUID，避免多显示器在同一秒内并发恢复时撞名
    /// （旧实现用秒级时间戳，两个叠加层会拿到同一个目录而互相破坏）。
    /// </summary>
    public static async Task<CoreWebView2Environment> ResetWithFreshUserDataFolderAsync()
    {
        await InitLock.WaitAsync().ConfigureAwait(true);
        try
        {
            _environment = null;
            _userDataFolder = BuildSessionUserDataFolder();

            _environment = await CreateAsync(_userDataFolder).ConfigureAwait(true);
            return _environment;
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static async Task<CoreWebView2Environment> CreateAsync(string userDataFolder)
    {
        // WinRT 投影的 CoreWebView2EnvironmentOptions 只提供无参构造 + 属性。
        var options = new CoreWebView2EnvironmentOptions
        {
            ExclusiveUserDataFolderAccess = true,
            AdditionalBrowserArguments =
                "--disable-background-timer-throttling " +
                "--disable-features=CalculateNativeWinOcclusion " +
                "--enable-begin-frame-scheduling"
        };

        // WinRT 投影只提供 CreateWithOptionsAsync，且返回 IAsyncOperation
        // （需要 AsTask 才能 ConfigureAwait）。
        return await CoreWebView2Environment
            .CreateWithOptionsAsync(null, userDataFolder, options)
            .AsTask()
            .ConfigureAwait(true);
    }
}
