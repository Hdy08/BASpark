using System.Runtime.InteropServices;

namespace BASpark;

/// <summary>
/// 系统级提示框。使用原生 Win32 MessageBox，因此不需要 XamlRoot，
/// 在启动早期（任何 WinUI 窗口建立之前）以及叠加层线程上都能安全调用。
/// 视觉上跟随系统，而非自绘。
/// </summary>
internal static class NativeMessageBox
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_TOPMOST = 0x00040000;

    public static void Show(string message, string? title = null, bool isError = false)
    {
        uint flags = MB_OK | MB_TOPMOST |
            (isError ? MB_ICONERROR : MB_ICONINFORMATION);

        try
        {
            _ = MessageBoxW(IntPtr.Zero, message, title ?? "BASpark", flags);
        }
        catch (Exception)
        {
        }
    }

    public static void ShowWarning(string message, string? title = null)
    {
        try
        {
            _ = MessageBoxW(IntPtr.Zero, message, title ?? "BASpark", MB_OK | MB_TOPMOST | MB_ICONWARNING);
        }
        catch (Exception)
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
