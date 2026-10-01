using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace BASpark;

/// <summary>
/// 侧边栏自定义背景图加载。迁移到 WinUI 3 后改用
/// <see cref="BitmapImage"/> + <see cref="ImageBrush"/>（不再有 WPF 的 Freeze）。
/// </summary>
internal static class SidebarBackgroundHelper
{
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const int DecodePixelWidth = 360;

    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    private static readonly SemaphoreSlim LoadGate = new(1, 1);

    private static volatile string? _cachedPath;
    private static volatile ImageBrush? _cachedBrush;

    public static bool IsSupportedImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        return Array.Exists(AllowedExtensions, item =>
            item.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<ImageBrush?> LoadBrushAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsSupportedImage(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaxFileBytes)
            {
                AppLogger.Warn($"Sidebar background ignored: file size out of range ({fileInfo.Length} bytes).");
                return null;
            }

            string normalizedPath = Path.GetFullPath(path);

            if (string.Equals(_cachedPath, normalizedPath, StringComparison.OrdinalIgnoreCase)
                && _cachedBrush != null)
            {
                return _cachedBrush;
            }

            await LoadGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                if (string.Equals(_cachedPath, normalizedPath, StringComparison.OrdinalIgnoreCase)
                    && _cachedBrush != null)
                {
                    return _cachedBrush;
                }

                ImageBrush? brush = await DecodeBrushFromFileAsync(normalizedPath)
                    .ConfigureAwait(true);

                _cachedPath = brush == null ? null : normalizedPath;
                _cachedBrush = brush;

                return brush;
            }
            finally
            {
                LoadGate.Release();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load sidebar background image.", ex);
            return null;
        }
    }

    public static void ClearCache()
    {
        _cachedPath = null;
        _cachedBrush = null;
    }

    private static async Task<ImageBrush?> DecodeBrushFromFileAsync(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = DecodePixelWidth,
                CreateOptions = BitmapCreateOptions.IgnoreImageCache
            };

            // 通过内存流读取，避免 BitmapImage 长期占用文件句柄。
            byte[] bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(true);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask().ConfigureAwait(true);
                await writer.FlushAsync().AsTask().ConfigureAwait(true);
                writer.DetachStream();
            }

            stream.Seek(0);
            await bitmap.SetSourceAsync(stream).AsTask().ConfigureAwait(true);

            return new ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                Opacity = 0.6
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to decode sidebar background image from path: {filePath}", ex);
            return null;
        }
    }
}
