using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BASpark;

/// <summary>
/// 与页面底色一致的纯色系统背景（<see cref="SystemBackdrop"/>）。
///
/// 为什么需要它：WinUI 3 的窗口内容跑在子窗口里，顶层窗口自身的重定向表面是空的，
/// 而 DWM 做最小化/还原动画时用的正是顶层窗口那一层 —— 于是动画期间整窗显示为
/// **纯黑**（用户录屏确认：同一台机器上参照程序动画期间仍能看到界面，本程序是黑块）。
/// 给窗口挂一个纯色 SystemBackdrop 后，DWM 在动画里合成的是这层颜色，黑块因此变成
/// 与应用一致的底色；顺带把 9px 非客户区边框从「透出桌面壁纸」变成同一底色。
///
/// 注意：系统背景用的是系统合成器（<see cref="Windows.UI.Composition"/>），
/// 不是 XAML 的 <c>Microsoft.UI.Composition</c>，两者类型不通用。
///
/// Windows 11 上仍然优先使用 Mica（真正受支持），只有不支持时才回退到纯色。
/// </summary>
internal sealed class AppBackdrop : SystemBackdrop
{
    // 与 DesignSystem.xaml 里 BasPageBackgroundBrush 的深浅色取值保持一致。
    private static readonly Color DarkColor = Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
    private static readonly Color LightColor = Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

    private readonly Windows.UI.Composition.Compositor _compositor = new();

    // 必须持有画刷引用：系统背景属性只做弱引用式持有，被 GC 回收后背景会消失。
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private ICompositionSupportsSystemBackdrop? _target;
    private ElementTheme _theme = ElementTheme.Default;

    /// <summary>跟随窗口实际主题切换底色。</summary>
    public void SetTheme(ElementTheme theme)
    {
        _theme = theme;
        Apply();
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _target = connectedTarget;
        Apply();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _target = null;
        _brush = null;
    }

    private void Apply()
    {
        ICompositionSupportsSystemBackdrop? target = _target;
        if (target == null)
        {
            return;
        }

        // 设置系统背景可能被宿主拒绝（例如 XAML 岛不接受系统合成器画刷，返回“拒绝访问”）：
        // 这种情况只应降级为无背景，绝不能把异常抛到主题变化等回调里。
        try
        {
            bool dark = _theme == ElementTheme.Dark;
            _brush = _compositor.CreateColorBrush(dark ? DarkColor : LightColor);
            target.SystemBackdrop = _brush;
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"System backdrop rejected by the host: {ex.Message}");
        }
    }
}
