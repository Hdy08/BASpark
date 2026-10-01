using System.Text;
using System.Xml.Linq;
using Windows.UI;

namespace BASpark.Tests;

/// <summary>
/// 界面契约测试。迁移到 WinUI 3 后：
///   * 主题不再由 ThemeManager 手动切换，而是依赖 DesignSystem.xaml 的
///     ThemeDictionaries + 原生控件模板，因此不再断言调色板实现细节。
///   * 静态文案不能写在 XAML 里（WinUI 3 的 XAML 编译器不支持自定义
///     MarkupExtension，会报 WMC0615），改由 ControlPanelWindow.ApplyLocalizedText
///     在加载时填充，因此断言的是「元素存在 + 文案来源集中」。
///   * 颜色类型从 System.Windows.Media.Color 换成 Windows.UI.Color。
/// </summary>
public class WinUi3UiTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Off", DarkModeOption.Off)]
    [InlineData("off", DarkModeOption.Off)]
    [InlineData("On", DarkModeOption.On)]
    [InlineData("ON", DarkModeOption.On)]
    [InlineData("System", DarkModeOption.System)]
    [InlineData("unknown", DarkModeOption.System)]
    [InlineData(null, DarkModeOption.System)]
    public void ParseDarkMode_UsesStableThreeStateValues(string? raw, DarkModeOption expected)
    {
        Assert.Equal(expected, ConfigManager.ParseDarkMode(raw));
    }

    [Fact]
    public void ControlPanel_UsesThemeResourcesAndNativeDarkModeChoices()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement root = Assert.IsType<XElement>(document.Root);

        // 页面背景必须是主题资源，才能随深浅色切换。
        XElement rootGrid = GetNamedElement(document, "RootGrid");
        Assert.Contains("BasPageBackgroundBrush", (string?)rootGrid.Attribute("Background"));

        // 侧边栏与卡片同样走主题资源。
        Assert.Contains("BasSidebarBackgroundBrush", (string?)GetNamedElement(document, "Sidebar").Attribute("Background"));
        Assert.Contains("BasCardStyle", (string?)GetNamedElement(document, "ColorPreview").Parent?.Parent?.Attribute("Style") ?? "BasCardStyle");

        // 深色模式三态：使用 WinUI 的 RadioButtons 容器，SelectedIndex 即状态；
        // 文案由代码填充（见 StaticText_IsFilledFromCodeBecauseWinUi3RejectsMarkupExtensions）。
        XElement darkModeContainer = GetNamedElement(document, "RadioDarkMode");
        Assert.Equal("RadioButtons", darkModeContainer.Name.LocalName);
        Assert.Equal(3, darkModeContainer.Elements().Count(e => e.Name.LocalName == "RadioButton"));
        Assert.Equal("True", (string?)GetNamedElement(document, "RadioDarkModeSystem").Attribute("IsChecked"));
    }

    [Fact]
    public void ControlPanel_UsesNativeWinUiControlsOnly()
    {
        string xaml = ReadSource("src", "ControlPanelWindow.xaml");

        // 不得残留任何 WPF 专有标记。
        string[] wpfOnlyMarkers =
        [
            "DynamicResource",
            "StaticResource}",
            "x:Static",
            "ControlTemplate",
            "DataTrigger",
            "WindowStartupLocation",
            "AllowsTransparency",
            "System.Windows",
            "Segoe MDL2 Assets",
        ];

        foreach (string marker in wpfOnlyMarkers)
        {
            Assert.DoesNotContain(marker, xaml, StringComparison.Ordinal);
        }

        // 关键交互必须是原生 WinUI 控件，而不是自绘。
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string[] nativeTypes =
        [
            "InfoBar",
            "ToggleSwitch",
            "NumberBox",
            "Slider",
            "ComboBox",
            "RadioButtons",
            "ListView",
            "ScrollViewer",
        ];

        foreach (string nativeType in nativeTypes)
        {
            Assert.Contains(
                document.Descendants(),
                element => element.Name.LocalName == nativeType);
        }

        // 安全提示使用 InfoBar，而不是自绘 Border。
        // （首页公告栏已按要求移除，这里只断言仍然存在的安全提示。）
        Assert.Equal("Warning", (string?)GetNamedElement(document, "SecurityWarningBar").Attribute("Severity"));
        Assert.Equal("InfoBar", GetNamedElement(document, "SecurityWarningBar").Name.LocalName);

        // 首页公告栏不得再出现。
        Assert.DoesNotContain("NoticeBar", ReadSource("src", "ControlPanelWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystem_DeclaresLightDarkAndHighContrastDictionaries()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");

        var themeDictionaries = document.Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .SelectMany(e => e.Elements())
            .Select(e => (string?)e.Attribute(Xaml + "Key"))
            .ToList();

        Assert.Contains("Light", themeDictionaries);
        Assert.Contains("Dark", themeDictionaries);
        Assert.Contains("HighContrast", themeDictionaries);

        // 三套主题必须覆盖同一组键，否则切换主题会丢资源。
        var keysPerTheme = document.Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .SelectMany(e => e.Elements())
            .ToDictionary(
                theme => (string?)theme.Attribute(Xaml + "Key") ?? "",
                theme => theme.Elements()
                    .Select(brush => (string?)brush.Attribute(Xaml + "Key"))
                    .Where(key => key != null)
                    .ToHashSet(StringComparer.Ordinal));

        Assert.Equal(keysPerTheme["Light"], keysPerTheme["Dark"]);
        Assert.Equal(keysPerTheme["Light"], keysPerTheme["HighContrast"]);
    }

    [Fact]
    public void ApplyingSettings_RefreshesThemeWithoutChangingItAtRadioSelection()
    {
        string xaml = ReadSource("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");

        // 选择深色模式时不得立即改主题，必须等「应用更改」。
        Assert.DoesNotContain("DarkMode_Changed", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkMode_Changed", source, StringComparison.Ordinal);

        int saveIndex = source.IndexOf("ConfigManager.Save(\"DarkMode\"", StringComparison.Ordinal);
        Assert.True(saveIndex >= 0, "SaveSettings must persist DarkMode.");

        int applyIndex = source.IndexOf("ApplyDarkMode(", saveIndex, StringComparison.Ordinal);
        Assert.True(applyIndex > saveIndex, "DarkMode must be applied after it is persisted.");
    }

    [Fact]
    public void AnimationSpeedHint_FollowsTheLinkedSpeedToggle()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement toggle = GetNamedElement(document, "CheckLinkedAnimationSpeed");
        XElement hint = GetNamedElement(document, "TxtLinkedSpeedHint");
        XElement parent = Assert.IsType<XElement>(toggle.Parent);
        List<XElement> children = parent.Elements().ToList();

        Assert.Same(parent, hint.Parent);
        Assert.Equal(children.IndexOf(toggle) + 1, children.IndexOf(hint));
        Assert.True(children.IndexOf(hint) < children.IndexOf(GetNamedElement(document, "PanelUnifiedAnimationSpeed")));
        Assert.True(children.IndexOf(hint) < children.IndexOf(GetNamedElement(document, "PanelSplitAnimationSpeed")));
    }

    [Fact]
    public void TrailRefreshRate_CanFollowTheDisplayAndKeepsTheManualRange()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement toggle = GetNamedElement(document, "CheckFollowDisplayRefreshRate");
        XElement hint = GetNamedElement(document, "TxtFollowDisplayRefreshRateHint");
        XElement slider = GetNamedElement(document, "SliderTrailRefresh");
        XElement numberBox = GetNamedElement(document, "TxtTrailRefreshValue");
        List<XElement> children = Assert.IsType<XElement>(toggle.Parent).Elements().ToList();

        Assert.Equal(children.IndexOf(toggle) + 1, children.IndexOf(hint));
        Assert.Equal("30", (string?)slider.Attribute("Minimum"));
        Assert.Equal("360", (string?)slider.Attribute("Maximum"));
        Assert.Equal("30", (string?)numberBox.Attribute("Minimum"));
        Assert.Equal("360", (string?)numberBox.Attribute("Maximum"));

        string configSource = ReadSource("src", "ConfigManager.cs");
        string overlaySource = ReadSource("src", "OverlayManager.cs");
        Assert.Contains("TrailRefreshRate { get; set; } = 60", configSource, StringComparison.Ordinal);
        Assert.Contains("FollowDisplayRefreshRate { get; set; } = true", configSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(hz, 30, 360)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ScreenIdentity.GetRefreshRate(pair.Key, _manualTrailRefreshRate)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("hoveredTarget.EmitTrailStart", overlaySource, StringComparison.Ordinal);
        Assert.Contains("long moveInterval = target?.TrailMoveIntervalTimestamp", overlaySource, StringComparison.Ordinal);
        Assert.Contains("RestartDisplaySettingsRecoveryTimer", overlaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualReset_SeparatesUnifiedAndIndependentScaleDefaults()
    {
        string panelSource = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string configSource = ReadSource("src", "ConfigManager.cs");

        Assert.Contains("VisualAppearanceResetFlags.UnifiedEffectScale", panelSource, StringComparison.Ordinal);
        Assert.Contains("VisualAppearanceResetFlags.TrailEffectScale", panelSource, StringComparison.Ordinal);
        Assert.Contains("VisualAppearanceResetFlags.ClickEffectScale", panelSource, StringComparison.Ordinal);
        Assert.Contains("Save(\"EffectScale\", 1.0)", configSource, StringComparison.Ordinal);
        Assert.Contains("Save(\"TrailEffectScale\", 1.0)", configSource, StringComparison.Ordinal);
        Assert.Contains("Save(\"ClickEffectScale\", 1.0)", configSource, StringComparison.Ordinal);

        foreach (string resourcePath in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            string resources = ReadSource("src", resourcePath);
            Assert.Contains("VisualReset_UnifiedScale", resources, StringComparison.Ordinal);
            Assert.Contains("VisualReset_TrailScale", resources, StringComparison.Ordinal);
            Assert.Contains("VisualReset_ClickScale", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GlowBrightness_UsesSingleControlWithExpectedDefaults()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement slider = GetNamedElement(document, "SliderGlow");
        XElement numberBox = GetNamedElement(document, "TxtGlowValue");
        XElement trailRefreshText = GetNamedElement(document, "TxtTrailRefresh");
        XElement effectColor = GetNamedElement(document, "TxtEffectColor");
        List<XElement> children = Assert.IsType<XElement>(slider.Parent?.Parent).Elements().ToList();

        // 辉光亮度位于拖尾刷新率之后、特效颜色之前。
        Assert.True(children.IndexOf(trailRefreshText) < children.IndexOf(effectColor));
        Assert.Equal("0", (string?)slider.Attribute("Minimum"));
        Assert.Equal("3", (string?)slider.Attribute("Maximum"));
        Assert.Equal("0", (string?)numberBox.Attribute("Minimum"));
        Assert.Equal("3", (string?)numberBox.Attribute("Maximum"));

        string xamlSource = ReadSource("src", "ControlPanelWindow.xaml");
        string panelSource = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string configSource = ReadSource("src", "ConfigManager.cs");
        Assert.Contains("GlowIntensity { get; set; } = 1.0", configSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckLinkedGlowIntensity", xamlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PanelSplitGlowIntensity", xamlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SliderTrailGlowIntensity", xamlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SliderClickGlowIntensity", xamlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UseLinkedGlowIntensity", configSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TrailGlowIntensity", configSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetGlowIntensitiesForOverlay", configSource, StringComparison.Ordinal);
        Assert.Contains("VisualAppearanceResetFlags.GlowIntensity", panelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UnifiedGlowIntensity", panelSource, StringComparison.Ordinal);
        Assert.Contains("Save(\"GlowIntensity\", 1.0)", configSource, StringComparison.Ordinal);

        foreach (string resourcePath in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            string resources = ReadSource("src", resourcePath);
            Assert.Contains("Visual_GlowIntensity", resources, StringComparison.Ordinal);
            Assert.Contains("VisualReset_GlowIntensity", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("GlowIntensityHint", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("TrailGlowIntensity", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StaticText_IsFilledFromCodeBecauseWinUi3RejectsMarkupExtensions()
    {
        string xaml = ReadSource("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");

        // WinUI 3 的 XAML 编译器不支持自定义 MarkupExtension（WMC0615），
        // 因此标记里不能再出现 {loc:...}，改由 ApplyLocalizedText 统一填充。
        Assert.DoesNotContain("{loc:", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationExtension", xaml, StringComparison.Ordinal);
        Assert.Contains("void ApplyLocalizedText()", source, StringComparison.Ordinal);

        // 空文案的具名元素必须在代码里被赋值，否则界面上会出现空白按钮。
        string[] mustBeFilledWithContent =
        [
            "BtnApplySettings", "BtnPickColor", "BtnVisualReset", "BtnClearLog", "BtnCheckUpdate",
            "BtnOfficialSite", "BtnGithub", "BtnBilibili", "BtnQQ", "BtnDiscord", "BtnSponsor",
            "BtnResetAll", "BtnRefreshScreens", "BtnAddProfile", "BtnRenameProfile", "BtnDeleteProfile",
        ];

        foreach (string name in mustBeFilledWithContent)
        {
            Assert.Contains(name + ".Content", source, StringComparison.Ordinal);
        }

        // 侧边栏导航项带原生图标，文案写在内部 TextBlock 上（不再是 RadioButton.Content），
        // 因此断言的是对应的 Label 元素被赋值。
        string[] navLabels =
        [
            "TabWelcomeLabel", "TabSettingsLabel", "TabLogLabel", "TabAboutLabel",
            "SubTabBasicLabel", "SubTabVisualLabel", "SubTabFilterLabel", "SubTabMultiScreenLabel",
        ];

        foreach (string name in navLabels)
        {
            Assert.Contains(name + ".Text", source, StringComparison.Ordinal);
        }

        // 导航项必须真的有图标，否则「加图标」这项需求会静默退化。
        Assert.Contains("<FontIcon", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_UsesNativeLayeredWindowInsteadOfXamlWindow()
    {
        string overlaySource = ReadSource("src", "OverlayWindow.cs");
        string hostSource = ReadSource("src", "LayeredWindowHost.cs");

        // WinUI 3 的合成器窗口无法启用 WS_EX_LAYERED，因此叠加层必须是
        // 自建的原生 Win32 分层窗口。
        Assert.Contains("WS_EX_LAYERED", hostSource, StringComparison.Ordinal);
        Assert.Contains("WS_EX_TRANSPARENT", hostSource, StringComparison.Ordinal);
        Assert.Contains("SetLayeredWindowAttributes", hostSource, StringComparison.Ordinal);
        Assert.Contains("CreateWindowEx", hostSource, StringComparison.Ordinal);

        // 透明背景与 Win32 控制器宿主是透明穿透的前提。
        Assert.Contains("Color.FromArgb(0, 0, 0, 0)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("CreateFromWindowHandle", overlaySource, StringComparison.Ordinal);

        // 不得退化成 XAML 窗口。
        Assert.DoesNotContain("Microsoft.UI.Xaml.Window", overlaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceTree_HasNoWpfOrWinFormsUiDependencies()
    {
        string root = FindWorkspaceRoot();
        string sourceDirectory = Path.Combine(root, "src");

        string[] offenders = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                          StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                          StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path, Encoding.UTF8)
                .Contains("System.Windows.Media", StringComparison.Ordinal)
                || File.ReadAllText(path, Encoding.UTF8)
                    .Contains("System.Windows.Controls", StringComparison.Ordinal)
                || File.ReadAllText(path, Encoding.UTF8)
                    .Contains("System.Windows.Interop", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);

        // 工程文件里不能再启用 WPF。
        string project = ReadSource("src", "BASpark.csproj");
        Assert.DoesNotContain("<UseWPF>true</UseWPF>", project, StringComparison.Ordinal);
        Assert.Contains("<UseWinUI>true</UseWinUI>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorPicker_UsesWinUiColorAndHasNoWpfDependency()
    {
        Color input = Color.FromArgb(255, 0x45, 0xAF, 0xFF);
        HsvColor hsv = ColorPickerColorMath.RgbToHsv(input);
        Assert.Equal(input, ColorPickerColorMath.HsvToRgb(hsv));
        Assert.Equal("69,175,255", ColorPickerColorMath.ToRgbString(input));
        Assert.True(ColorPickerColorMath.TryParseRgb("69,175,255", out Color parsed));
        Assert.Equal(input, parsed);

        string panelSource = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string pickerSource = ReadSource("src", "ColorPickerWindow.xaml.cs");
        Assert.Contains("new ColorPickerWindow(", panelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms.ColorDialog", panelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Windows", pickerSource, StringComparison.Ordinal);
        Assert.Contains("Windows.UI", pickerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RendererReadyTimeout_ProbesBeforeFallingBackToLegacy()
    {
        string overlaySource = ReadSource("src", "OverlayWindow.cs");

        // 主渲染器需要等 DOMContentLoaded 后再初始化 WebGL/WebGPU。原先「固定等待
        // 满 12 秒才探测一次」会让启动整整慢 12 秒；现改为轮询，就绪即显示，
        // 12 秒仅作为渲染器确实起不来时的兜底。
        Assert.Contains("PollRendererReadyAsync", overlaySource, StringComparison.Ordinal);
        Assert.Contains("RendererProbeInterval", overlaySource, StringComparison.Ordinal);
        Assert.Contains("RendererProbeScript", overlaySource, StringComparison.Ordinal);
        Assert.Contains("window.externalBoom", overlaySource, StringComparison.Ordinal);

        // 轮询间隔必须远小于兜底超时，否则又退化成干等。
        int intervalIndex = overlaySource.IndexOf(
            "RendererProbeInterval = TimeSpan.FromMilliseconds(",
            StringComparison.Ordinal);
        Assert.True(intervalIndex >= 0, "Renderer probe interval constant is missing.");

        string intervalLine = overlaySource[intervalIndex..overlaySource.IndexOf(';', intervalIndex)];
        Assert.Contains("FromMilliseconds(200)", intervalLine, StringComparison.Ordinal);

        // 兜底超时仍须保留，供渲染器确实起不来时回退。
        int timeoutIndex = overlaySource.IndexOf(
            "RendererReadyTimeout = TimeSpan.FromSeconds(",
            StringComparison.Ordinal);
        Assert.True(timeoutIndex >= 0, "Renderer ready timeout constant is missing.");

        string timeoutLine = overlaySource[timeoutIndex..overlaySource.IndexOf(';', timeoutIndex)];
        Assert.DoesNotContain("FromSeconds(2)", timeoutLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_RecoversFromControllerCreationFailure()
    {
        string overlaySource = ReadSource("src", "OverlayWindow.cs");
        string holderSource = ReadSource("src", "WebView2EnvironmentHolder.cs");

        // CreateCoreWebView2ControllerAsync 是叠加层最脆弱的一步：用户数据目录里的
        // 损坏 profile 会让它以 E_INVALIDARG 失败，且重建环境对象无效，必须换用
        // 干净的用户数据目录。缺少恢复路径时一次失败就永久没有特效。
        Assert.Contains("TryAttachControllerAsync", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ResetWithFreshUserDataFolderAsync", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ResetWithFreshUserDataFolderAsync", holderSource, StringComparison.Ordinal);

        // 失败时必须留下足以定位的上下文（句柄、句柄有效性、HRESULT、用户数据目录）。
        Assert.Contains("hresult=", overlaySource, StringComparison.Ordinal);
        Assert.Contains("hwndValid=", overlaySource, StringComparison.Ordinal);
        Assert.Contains("userData=", overlaySource, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.IsWindow(_host.Handle)", overlaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void WebView2Environment_IsSharedAndSessionUnique()
    {
        string holderSource = ReadSource("src", "WebView2EnvironmentHolder.cs");
        string overlaySource = ReadSource("src", "OverlayWindow.cs");

        // 同一个用户数据目录同时只能有一个 WebView2 环境。多显示器会并发初始化
        // 多个叠加层，若各自建环境并指向同一目录，只有先到者成功，其余以
        // E_INVALIDARG 失败 —— 实测双屏环境下必然复现。
        Assert.Contains("一个环境 + 多个控制器", holderSource, StringComparison.Ordinal);

        // 环境按会话唯一，避免与其它实例或历史残留争用。
        Assert.Contains("Environment.ProcessId", holderSource, StringComparison.Ordinal);
        Assert.Contains("Guid.NewGuid()", holderSource, StringComparison.Ordinal);

        // 恢复目录名必须唯一：秒级时间戳会让同秒并发恢复撞名。
        Assert.DoesNotContain("yyyyMMddHHmmss", holderSource, StringComparison.Ordinal);

        // 控制器挂载要能在并发重建后重试并取用新环境。
        Assert.Contains("ControllerAttachAttempts", overlaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_AttachesControllerWhileHostWindowIsHidden()
    {
        string overlaySource = ReadSource("src", "OverlayWindow.cs");

        // UIAccess 进程（安装包带 uiAccess="true" 清单）下，若宿主窗口已经显示/置顶
        // 再创建 WebView2 控制器，CreateCoreWebView2ControllerAsync 会立即以
        // E_INVALIDARG 失败。必须保持隐藏挂载，显示与置顶推迟到导航成功之后。
        int initIndex = overlaySource.IndexOf("private async Task InitWebViewAsync()", StringComparison.Ordinal);
        Assert.True(initIndex >= 0, "InitWebViewAsync is missing.");

        int initEnd = overlaySource.IndexOf("private async Task<bool> TryAttachControllerAsync", initIndex, StringComparison.Ordinal);
        Assert.True(initEnd > initIndex, "InitWebViewAsync structure changed unexpectedly.");

        string initBody = overlaySource[initIndex..initEnd];
        Assert.DoesNotContain("_host.Show()", initBody, StringComparison.Ordinal);

        // 显示必须通过 EnsureHostPresented 汇聚，并由多个就绪信号触发：
        // 只依赖 NavigationCompleted 会让叠加层在该事件不到达时永久隐藏
        // （宿主窗口创建后始终未 Show，实测窗口 visible=False、无特效）。
        Assert.Contains("EnsureHostPresented", overlaySource, StringComparison.Ordinal);

        int navIndex = overlaySource.IndexOf("private void OnNavigationCompleted", StringComparison.Ordinal);
        Assert.True(navIndex > initIndex, "OnNavigationCompleted is missing.");
        Assert.Contains("EnsureHostPresented()", overlaySource[navIndex..], StringComparison.Ordinal);

        // 渲染器探测成功也必须把窗口显示出来。
        int probeIndex = overlaySource.IndexOf("PollRendererReadyAsync", StringComparison.Ordinal);
        Assert.True(probeIndex > initIndex, "Renderer probe is missing.");
        Assert.Contains("EnsureHostPresented()", overlaySource[probeIndex..], StringComparison.Ordinal);
    }

    [Fact]
    public void AnimatedSubNav_AvoidsCrashProneAnimationPatterns()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");

        int classIndex = source.IndexOf("private sealed class AnimatedSubNav", StringComparison.Ordinal);
        Assert.True(classIndex >= 0, "AnimatedSubNav is missing.");

        int classEnd = source.IndexOf("private AnimatedSubNav? _subNav;", classIndex, StringComparison.Ordinal);
        Assert.True(classEnd > classIndex, "AnimatedSubNav structure changed unexpectedly.");
        string body = source[classIndex..classEnd];

        // 1. 收起态用「容器高度 0 + 裁剪」实现，不靠 Visibility 切换（那会让过渡
        //    动画失去可见的起止状态）；绝不给 Border.Height 赋 NaN 作为「收起」值。
        Assert.Contains("_host.Height = 0;", body, StringComparison.Ordinal);
        Assert.Contains("RectangleGeometry", body, StringComparison.Ordinal);
        Assert.Contains("UpdateClip", body, StringComparison.Ordinal);

        // 2. Storyboard.Completed 是异步回调，不在 try/catch 栈上；其中的异常会
        //    直接终结进程。必须有代次号来忽略过期回调，否则快速点击会让旧回调
        //    覆盖新状态。
        Assert.Contains("_generation", body, StringComparison.Ordinal);
        Assert.Contains("generation != _generation", body, StringComparison.Ordinal);

        // 3. 动画对象不得跨 Storyboard 复用（快速切换时会产生竞态）：
        //    动画必须是局部变量、每次新建。
        Assert.Contains("var height = new DoubleAnimation", body, StringComparison.Ordinal);
        Assert.Contains("var slide = new DoubleAnimation", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly DoubleAnimation", body, StringComparison.Ordinal);

        // 4. SetExpanded 必须包在 try/catch 中，动画失败不能拖垮界面。
        int setIndex = body.IndexOf("public void SetExpanded(bool expanded)", StringComparison.Ordinal);
        Assert.True(setIndex >= 0, "SetExpanded is missing.");
        Assert.Contains("catch (Exception", body[setIndex..], StringComparison.Ordinal);

        // 5. 展开前必须先量出内容自然高度：容器被压到 0 时 ActualHeight 也是 0。
        Assert.Contains("MeasureNaturalHeight", body, StringComparison.Ordinal);

        // 6. 展开过程必须逐帧改变容器高度，下面的「日志 / 关于」才会被连续推开，
        //    而不是等动画结束才瞬移 —— 因此必须有 Height 的 DoubleAnimation。
        Assert.Contains("Storyboard.SetTargetProperty(height, \"Height\")", body, StringComparison.Ordinal);
    }

    private static XDocument LoadXaml(params string[] pathParts) =>
        XDocument.Parse(ReadSource(pathParts), LoadOptions.SetLineInfo);

    private static XElement GetNamedElement(XDocument document, string name) =>
        Assert.Single(
            document.Descendants(),
            element =>
                (string?)element.Attribute(Xaml + "Name") == name ||
                (string?)element.Attribute("Name") == name);

    private static string ReadSource(params string[] pathParts) =>
        File.ReadAllText(Path.Combine([FindWorkspaceRoot(), .. pathParts]), Encoding.UTF8);

    private static string FindWorkspaceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BASpark.sln")))
            {
                return directory.FullName;
            }
        }

        throw new Xunit.Sdk.XunitException("Could not locate the BASpark workspace root.");
    }
}
