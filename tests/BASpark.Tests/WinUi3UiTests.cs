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
        Assert.Contains("BasCardStyle", (string?)GetNamedElement(document, "NoticeBar").Parent?.Attribute("Style") ?? "BasCardStyle");

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

        // 公告与安全提示使用 InfoBar，而不是自绘 Border。
        Assert.Equal("Warning", (string?)GetNamedElement(document, "SecurityWarningBar").Attribute("Severity"));
        Assert.Equal("Informational", (string?)GetNamedElement(document, "NoticeBar").Attribute("Severity"));
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
        string[] mustBeFilled =
        [
            "TabWelcome", "TabSettings", "TabLog", "TabAbout",
            "SubTabBasic", "SubTabVisual", "SubTabFilter", "SubTabMultiScreen", "SubTabMore",
            "BtnApplySettings", "BtnPickColor", "BtnVisualReset", "BtnClearLog", "BtnCheckUpdate",
            "BtnOfficialSite", "BtnGithub", "BtnBilibili", "BtnQQ", "BtnDiscord", "BtnSponsor",
            "BtnResetAll", "BtnRefreshScreens", "BtnAddProfile", "BtnRenameProfile", "BtnDeleteProfile",
        ];

        foreach (string name in mustBeFilled)
        {
            Assert.Contains(name + ".Content", source, StringComparison.Ordinal);
        }
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

        // 主渲染器需要等 DOMContentLoaded 后再初始化 WebGL/WebGPU，低端机上会
        // 明显超过 2 秒。若超时即回退到 legacy，会把「还在初始化」误判为
        // 「渲染器损坏」，用户会直接看不到特效。
        Assert.Contains("ProbeRendererBeforeFallbackAsync", overlaySource, StringComparison.Ordinal);
        Assert.Contains("RendererProbeScript", overlaySource, StringComparison.Ordinal);
        Assert.Contains("window.externalBoom", overlaySource, StringComparison.Ordinal);

        // 探测判定的间隔必须远大于原来的 2 秒。
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
