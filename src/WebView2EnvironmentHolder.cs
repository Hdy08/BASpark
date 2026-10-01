using Microsoft.Web.WebView2.Core;

namespace BASpark;

/// <summary>
/// 进程内共享的 WebView2 环境。所有显示器的叠加层复用同一个环境，
/// 避免多屏场景下重复初始化浏览器进程与用户数据目录锁竞争。
/// </summary>
internal static class WebView2EnvironmentHolder
{
    private static CoreWebView2Environment? _environment;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

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

            // WinRT 投影的 CoreWebView2EnvironmentOptions 只提供无参构造 + 属性。
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-background-timer-throttling " +
                    "--disable-features=CalculateNativeWinOcclusion " +
                    "--enable-begin-frame-scheduling"
            };

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BASpark_WebView2");

            // WinRT 投影只提供 CreateWithOptionsAsync，且返回 IAsyncOperation
            // （需要 AsTask 才能 ConfigureAwait）。
            _environment = await CoreWebView2Environment
                .CreateWithOptionsAsync(null, userDataFolder, options)
                .AsTask()
                .ConfigureAwait(true);

            return _environment;
        }
        finally
        {
            InitLock.Release();
        }
    }
}
