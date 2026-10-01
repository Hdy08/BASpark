using Microsoft.Web.WebView2.Core;

namespace BASpark;

/// <summary>
/// 进程内共享的 WebView2 环境。所有显示器的叠加层复用同一个环境，
/// 避免多屏场景下重复初始化浏览器进程与用户数据目录锁竞争。
///
/// 用户数据目录内残留的损坏 profile 会让 <c>CreateCoreWebView2ControllerAsync</c>
/// 以 E_INVALIDARG 失败（表现为「Value does not fall within the expected range」
/// 且没有任何 WebView2 进程）。此时只重建环境没用，必须换一个干净的目录，
/// 因此这里提供 <see cref="ResetWithFreshUserDataFolderAsync"/> 供恢复路径调用。
/// </summary>
internal static class WebView2EnvironmentHolder
{
    private const string UserDataFolderName = "BASpark_WebView2";

    private static CoreWebView2Environment? _environment;
    private static string _userDataFolder = BuildDefaultUserDataFolder();
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    /// <summary>当前使用的用户数据目录，用于日志与恢复判断。</summary>
    public static string UserDataFolder => _userDataFolder;

    private static string BuildDefaultUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UserDataFolderName);

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
    /// 丢弃当前环境，改用全新的用户数据目录重建。
    /// 用于控制器创建因 profile 损坏而失败后的自动恢复。
    /// </summary>
    public static async Task<CoreWebView2Environment> ResetWithFreshUserDataFolderAsync()
    {
        await InitLock.WaitAsync().ConfigureAwait(true);
        try
        {
            _environment = null;

            string fresh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                $"{UserDataFolderName}_{DateTime.Now:yyyyMMddHHmmss}");

            AppLogger.Warn(
                $"Recreating the WebView2 environment with a fresh user data folder: {fresh}");
            _userDataFolder = fresh;

            _environment = await CreateAsync(fresh).ConfigureAwait(true);
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
