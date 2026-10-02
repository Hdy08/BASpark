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
        Assert.Equal("{StaticResource BasSettingCardStyle}", (string?)GetSettingCard(GetNamedElement(document, "ColorPreview")).Attribute("Style"));

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
            "ToggleSwitch",
            "NumberBox",
            "Slider",
            "ComboBox",
            "RadioButtons",
            "Expander",
            "ColorPicker",
            "ListView",
            "ScrollViewer",
        ];

        foreach (string nativeType in nativeTypes)
        {
            Assert.Contains(
                document.Descendants(),
                element => element.Name.LocalName == nativeType);
        }

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
        XElement card = GetSettingCard(toggle);
        List<XElement> children = GetNamedElement(document, "SectionVisual").Elements().ToList();

        AssertSettingHint(toggle, hint);
        Assert.True(children.IndexOf(card) < children.IndexOf(GetNamedElement(document, "PanelUnifiedAnimationSpeed")));
        Assert.True(children.IndexOf(card) < children.IndexOf(GetNamedElement(document, "PanelSplitAnimationSpeed")));
    }

    [Fact]
    public void TrailRefreshRate_CanFollowTheDisplayAndKeepsTheManualRange()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement toggle = GetNamedElement(document, "CheckFollowDisplayRefreshRate");
        XElement hint = GetNamedElement(document, "TxtFollowDisplayRefreshRateHint");
        XElement slider = GetNamedElement(document, "SliderTrailRefresh");
        XElement numberBox = GetNamedElement(document, "TxtTrailRefreshValue");
        AssertSettingHint(toggle, hint);
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
        List<XElement> children = GetNamedElement(document, "SectionVisual").Descendants().ToList();
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
            "BtnApplySettings", "BtnVisualReset", "BtnClearLog",
            "BtnResetSettings", "BtnScreensReset", "BtnRefreshScreens", "BtnAddProfile", "BtnRenameProfile", "BtnDeleteProfile",
            "BtnRepoDoomVoss", "BtnRepoCialloKing", "BtnRepoWinUI",
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
        Assert.DoesNotContain("new ColorPickerWindow(", panelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowColorPickerAsync", panelSource, StringComparison.Ordinal);
        Assert.Contains("EffectColorPicker_ColorChanged", panelSource, StringComparison.Ordinal);
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
    public void SettingsSubNav_AnimatesOnCompositorThreadWithoutLayoutAnimation()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string xaml = ReadSource("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");

        // 容器与被推开的「日志 / 关于」分组都必须存在，且由导航选中状态驱动。
        Assert.NotNull(GetNamedElement(document, "SettingsSubNavHost"));
        Assert.NotNull(GetNamedElement(document, "SettingsSubNav"));
        Assert.NotNull(GetNamedElement(document, "NavAfterSettings"));
        Assert.Contains("new SubNavAnimator(SettingsSubNavHost, SettingsSubNav, NavAfterSettings)", source, StringComparison.Ordinal);
        Assert.Contains("_subNav?.SetExpanded(settings);", source, StringComparison.Ordinal);

        int animatorIndex = source.IndexOf("private sealed class SubNavAnimator", StringComparison.Ordinal);
        Assert.True(animatorIndex >= 0, "SubNavAnimator is missing.");
        int animatorEnd = source.IndexOf("private SubNavAnimator? _subNav;", animatorIndex, StringComparison.Ordinal);
        Assert.True(animatorEnd > animatorIndex, "SubNavAnimator structure changed unexpectedly.");
        string animator = source[animatorIndex..animatorEnd];

        // 关键：不得再用布局属性做动画。对 Height 的依赖动画每帧都要在 UI 线程跑
        // measure/arrange，实测只有约 30Hz，在 180Hz 屏上就是掉帧。
        Assert.DoesNotContain("EnableDependentAnimation", animator, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Height\"", animator, StringComparison.Ordinal);
        Assert.DoesNotContain("_host.Clip", animator, StringComparison.Ordinal);

        // 露出改用合成器 InsetClip（合成器线程按屏幕刷新率插值）。
        Assert.Contains("ElementCompositionPreview.GetElementVisual", animator, StringComparison.Ordinal);
        Assert.Contains("CreateInsetClip", animator, StringComparison.Ordinal);
        Assert.Contains("StartAnimation(\"BottomInset\"", animator, StringComparison.Ordinal);

        // 「日志 / 关于」靠独立平移动画让位，布局只在切换那一刻改一次。
        Assert.Contains("_belowShift.Y = -height;", animator, StringComparison.Ordinal);
        Assert.Contains("_host.Height = height;", animator, StringComparison.Ordinal);
        Assert.Contains("_host.Height = 0;", animator, StringComparison.Ordinal);

        // 独立动画不写 From：被打断时从当前值继续，不会跳回起点。
        Assert.Contains("To = shiftTo,", animator, StringComparison.Ordinal);
        Assert.DoesNotContain("From = ", animator, StringComparison.Ordinal);

        // 不得给内容本身加平移动画：展开时会让 4 个子项先下沉几像素再回位。
        Assert.DoesNotContain("_content.RenderTransform", animator, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsSubNav.RenderTransform", source, StringComparison.Ordinal);

        // Completed 是异步回调，必须有代次号忽略过期回调。
        Assert.Contains("generation != _generation", animator, StringComparison.Ordinal);

        // 动画对象每次新建，不得跨 Storyboard 复用。
        Assert.Contains("new DoubleAnimation", animator, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly DoubleAnimation", animator, StringComparison.Ordinal);

        // 不得改用原生 Expander 承载：逐帧实测它不做布局动画（下方导航项在一帧内
        // 整段位移 168px），而在它外面套高度动画会破坏它自己的内容定位与裁剪。
        Assert.DoesNotContain(GetNamedElement(document, "SettingsSubNavHost").DescendantsAndSelf(),
            element => element.Name.LocalName == "Expander");
        Assert.DoesNotContain("NavExpander", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NavExpander", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded", animator, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayHost_SkipsRedundantFullScreenRepositioning()
    {
        string hostSource = ReadSource("src", "LayeredWindowHost.cs");

        // 叠加层每 5 秒重新断言一次置顶；若每次都无条件 SetWindowPos，两个全屏分层
        // 窗口会被重复定位，连带整屏重新合成 —— 拖动别的窗口时正好撞上就卡一下。
        // 因此位置尺寸未变时只刷新 DPI，不再重复下发。
        int boundsIndex = hostSource.IndexOf("public void SetBounds", StringComparison.Ordinal);
        Assert.True(boundsIndex >= 0, "SetBounds is missing.");
        Assert.Contains("_boundsApplied", hostSource[boundsIndex..], StringComparison.Ordinal);
        Assert.Contains("_boundsWidth == width", hostSource[boundsIndex..], StringComparison.Ordinal);
    }

    [Fact]
    public void WindowChrome_KeepsCaptionStylesBecauseTheyOwnWindowAnimations()
    {
        string chromeSource = ReadSource("src", "WindowChrome.cs");
        string hostSource = ReadSource("src", "DcompPanelHost.cs");

        Assert.DoesNotContain("SetWindowLong", chromeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DwmExtendFrameIntoClientArea", chromeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("InputNonClientPointerSource", chromeSource, StringComparison.Ordinal);
        Assert.Contains("int style = WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlPanel_AttachesXamlToTheDirectCompositionHost()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string hostSource = ReadSource("src", "DcompPanelHost.cs");

        Assert.Equal("UserControl", document.Root?.Name.LocalName);
        Assert.Contains("ControlPanelWindow : UserControl", source, StringComparison.Ordinal);
        Assert.Contains("_host = new DcompPanelHost()", source, StringComparison.Ordinal);
        Assert.Contains("_host.SetContent(this)", source, StringComparison.Ordinal);
        Assert.Contains("WsExNoRedirectionBitmap = 0x00200000", hostSource, StringComparison.Ordinal);
        Assert.Contains("_xamlSource.SiteBridge.MoveAndResize", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WsVisible", hostSource, StringComparison.Ordinal);
        Assert.Contains("InitializeWithWindow.Initialize(picker, Handle)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlPanel_CaptionButtonsRemainInteractiveAndTrackMaximization()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string hostSource = ReadSource("src", "DcompPanelHost.cs");

        Assert.Equal("CaptionMinimize_Click", (string?)GetNamedElement(document, "BtnCaptionMinimize").Attribute("Click"));
        Assert.Equal("CaptionMaximize_Click", (string?)GetNamedElement(document, "BtnCaptionMaximize").Attribute("Click"));
        Assert.Equal("CaptionClose_Click", (string?)GetNamedElement(document, "BtnCaptionClose").Attribute("Click"));
        Assert.Contains("CaptionMaximizeIcon.Glyph = maximized", source, StringComparison.Ordinal);
        Assert.Contains("XamlRoot.RasterizationScale", source, StringComparison.Ordinal);
        Assert.Contains("CaptionButtons.SizeChanged", source, StringComparison.Ordinal);
        Assert.Contains("NonClientRegionKind.Passthrough", hostSource, StringComparison.Ordinal);
        Assert.Contains("IsMaximized ? []", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlPanel_KeepsTheCompactTitleBarAndRightAlignedCaptionButtons()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement titleBarHost = GetNamedElement(document, "TitleBarHost");
        XElement titleBar = GetNamedElement(document, "AppTitleBar");
        XElement captionButtons = GetNamedElement(document, "CaptionButtons");
        string hostSource = ReadSource("src", "DcompPanelHost.cs");

        Assert.Equal("32", (string?)titleBarHost.Attribute("Height"));
        Assert.Equal("0", (string?)titleBar.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)captionButtons.Attribute("Grid.Column"));
        Assert.Equal(titleBarHost, captionButtons.Parent);
        Assert.Equal("Right", (string?)captionButtons.Attribute("HorizontalAlignment"));
        Assert.Equal("Top", (string?)captionButtons.Attribute("VerticalAlignment"));
        Assert.Equal(
            new[] { "*", "Auto" },
            titleBarHost.Element(Presentation + "Grid.ColumnDefinitions")!
                .Elements().Select(column => (string?)column.Attribute("Width")));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "TitleBar.RightHeader");
        foreach (XElement button in captionButtons.Elements())
        {
            Assert.Equal("46", (string?)button.Attribute("Width"));
            Assert.Equal("32", (string?)button.Attribute("Height"));
        }

        Assert.Contains("CaptionHeight = 32", hostSource, StringComparison.Ordinal);
        Assert.Contains("new RectInt32(0, 0, captionWidth, Math.Min(caption, height))", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelHost_PreservesIslandContentsWhileMinimizedAndRestoresOnShow()
    {
        string hostSource = ReadSource("src", "DcompPanelHost.cs");
        int handlerIndex = hostSource.IndexOf("private void OnWindowSizeChanged()", StringComparison.Ordinal);
        Assert.True(handlerIndex >= 0);
        string handler = hostSource[handlerIndex..hostSource.IndexOf("private void UpdateXamlIslandBounds()", handlerIndex, StringComparison.Ordinal)];

        Assert.Contains("if (IsIconic(_hwnd))", handler, StringComparison.Ordinal);
        Assert.True(handler.IndexOf("return;", StringComparison.Ordinal) < handler.IndexOf("UpdateXamlIslandBounds()", StringComparison.Ordinal));
        Assert.Contains("IsIconic(_hwnd) ? SW_RESTORE : SW_SHOW", hostSource, StringComparison.Ordinal);
        Assert.Contains("Marshal.StructureToPtr(info.rcWork, lParam", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Marshal.StructureToPtr(window, lParam", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlPanel_ClosingReleasesTheIslandAndNativeHost()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string hostSource = ReadSource("src", "DcompPanelHost.cs");

        Assert.Contains("_host.CloseRequested += ControlPanelWindow_Closed", source, StringComparison.Ordinal);
        Assert.Contains("_host.Dispose()", source, StringComparison.Ordinal);
        Assert.Contains("Closed?.Invoke(this, EventArgs.Empty)", source, StringComparison.Ordinal);
        Assert.Contains("_xamlSource.Dispose()", hostSource, StringComparison.Ordinal);
        Assert.Contains("DestroyWindow(hwnd)", hostSource, StringComparison.Ordinal);
        Assert.Contains("Instances.Remove(hwnd)", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelHost_RestoresKeyboardFocusAndWrapsTabNavigation()
    {
        string hostSource = ReadSource("src", "DcompPanelHost.cs");

        Assert.Contains("_xamlSource.TakeFocusRequested +=", hostSource, StringComparison.Ordinal);
        Assert.Contains("XamlSourceFocusNavigationReason.First or XamlSourceFocusNavigationReason.Last", hostSource, StringComparison.Ordinal);
        Assert.Contains("XamlSourceFocusNavigationReason.Restore", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPalette_MatchesTheReferenceAndKeepsTheSidebarWithTheTitleBar()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        XElement themes = Assert.Single(document.Descendants(), element => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries");
        foreach (XElement theme in themes.Elements())
        {
            var colors = theme.Elements().ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => (string?)element.Attribute("Color"));
            Assert.Equal(colors["BasPageBackgroundBrush"], colors["BasSidebarBackgroundBrush"]);
            if ((string?)theme.Attribute(Xaml + "Key") == "Dark")
            {
                Assert.Equal("#202020", colors["BasSidebarBackgroundBrush"]);
                Assert.Equal("#272727", colors["BasSettingsPageBackgroundBrush"]);
                Assert.Equal("#323232", colors["BasSettingsCardBackgroundBrush"]);
                Assert.Equal("#2D2D2D", colors["BasNavigationSelectedBrush"]);
                XElement accent = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentColor");
                Assert.Equal("#ADACF0", accent.Value);
            }
            if ((string?)theme.Attribute(Xaml + "Key") == "HighContrast")
            {
                Assert.Equal("{ThemeResource SystemColorWindowTextColor}", colors["BasSettingsCardBorderBrush"]);
                Assert.Equal("{ThemeResource SystemColorHighlightTextColor}", colors["BasNavigationSelectedTextBrush"]);
            }
        }

        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement surface = GetNamedElement(panel, "ContentSurface");
        Assert.Equal("{ThemeResource BasSettingsPageBackgroundBrush}", (string?)surface.Attribute("Background"));
        Assert.Equal("6,0,0,0", (string?)surface.Attribute("CornerRadius"));
        Assert.Equal("{StaticResource BasSettingsPageTitleStyle}", (string?)GetNamedElement(panel, "TxtSettingsTitle").Attribute("Style"));
    }

    [Fact]
    public void Settings_KeepIndependentCardsWithTextOnTheLeftAndControlsOnTheRight()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string[] controlNames =
        [
            "ComboLanguage", "RadioDarkMode", "CheckAlwaysTrailEffectSwitch",
            "CheckMasterSwitch", "RadioClickType", "CheckMiddleClickTrigger", "CheckScreenshotCompatibilityMode",
            "CheckAutoStart", "CheckStartSilent", "CheckHideTrayIcon", "CheckRunAsAdmin", "CheckTouchscreenMode",
            "CheckLinkedEffectScale", "SliderScale", "SliderTrailScale", "SliderClickScale",
            "SliderGlow", "CheckLinkedAnimationSpeed", "SliderSpeed", "SliderTrailAnimSpeed", "SliderClickAnimSpeed",
            "CheckApplyCurveDraw", "CheckFollowDisplayRefreshRate", "SliderTrailRefresh",
            "CheckEnvironmentFilter", "CheckHideInFullscreen", "CheckShowEffectOnDesktop",
            "ComboProfiles", "ComboProcessFilterMode", "ListConfiguredProcesses"
        ];
        var cards = new HashSet<XElement>();
        foreach (string name in controlNames)
        {
            XElement control = GetNamedElement(document, name);
            XElement card = GetSettingCard(control);
            Assert.True(cards.Add(card), $"{name} must have its own setting card.");
            XElement layout = Assert.Single(card.Elements());
            Assert.Equal("Grid", layout.Name.LocalName);
            XElement text = Assert.Single(layout.Elements(), element => (string?)element.Attribute("Grid.Column") == "0");
            XElement controls = Assert.Single(layout.Elements(), element => (string?)element.Attribute("Grid.Column") == "1");
            Assert.Equal("StackPanel", text.Name.LocalName);
            Assert.Equal("TextBlock", text.Elements().First().Name.LocalName);
            Assert.Contains(control, controls.Descendants());
            Assert.Equal("Center", (string?)controls.Attribute("VerticalAlignment"));
            Assert.Equal("Stretch", (string?)controls.Attribute("HorizontalAlignment"));
            Assert.Null(controls.Attribute("MaxWidth"));
            XElement columns = Assert.Single(layout.Elements(), element => element.Name.LocalName == "Grid.ColumnDefinitions");
            XElement controlColumn = columns.Elements().Last();
            if (control.Name.LocalName != "ToggleSwitch")
            {
                Assert.Equal("*", (string?)controlColumn.Attribute("Width"));
                Assert.Equal("240", (string?)controlColumn.Attribute("MaxWidth"));
                Assert.Equal("140", (string?)controlColumn.Attribute("MinWidth"));
            }
            if (control.Name.LocalName == "ToggleSwitch")
            {
                Assert.Equal("{StaticResource BasSettingToggleStyle}", (string?)control.Attribute("Style"));
                Assert.Equal("{Binding Header, ElementName=" + name + "}", (string?)text.Elements().First().Attribute("Text"));
            }
        }

        XElement styles = Assert.IsType<XElement>(LoadXaml("src", "DesignSystem.xaml").Root);
        XElement cardStyle = Assert.Single(styles.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingCardStyle");
        var setters = cardStyle.Elements().ToDictionary(element => (string)element.Attribute("Property")!, element => (string?)element.Attribute("Value"));
        Assert.Equal("60", setters["MinHeight"]);
        Assert.Equal("16,12", setters["Padding"]);
        Assert.Equal("0,0,0,6", setters["Margin"]);
    }

    [Theory]
    [InlineData("CheckScreenshotCompatibilityMode", "TxtScreenshotHint")]
    [InlineData("CheckRunAsAdmin", "TxtRunAsAdminHint")]
    [InlineData("CheckTouchscreenMode", "TxtTouchscreenHint")]
    [InlineData("CheckLinkedEffectScale", "TxtLinkedScaleHint")]
    [InlineData("CheckLinkedAnimationSpeed", "TxtLinkedSpeedHint")]
    [InlineData("CheckApplyCurveDraw", "TxtCurveDrawHint")]
    [InlineData("CheckFollowDisplayRefreshRate", "TxtFollowDisplayRefreshRateHint")]
    public void SettingHints_RemainUnderTheTitleInTheSameCard(string controlName, string hintName)
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        AssertSettingHint(GetNamedElement(document, controlName), GetNamedElement(document, hintName));
    }

    [Theory]
    [InlineData("RadioDarkMode", 3)]
    [InlineData("RadioClickType", 3)]
    public void PresetChoices_UseNativeSegmentedRadioButtons(string name, int count)
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement container = GetNamedElement(document, name);
        Assert.Equal("RadioButtons", container.Name.LocalName);
        Assert.Equal("{StaticResource BasSegmentedRadioButtonsStyle}", (string?)container.Attribute("Style"));
        Assert.Equal(count.ToString(), (string?)container.Attribute("MaxColumns"));
        Assert.Equal("{StaticResource BasSegmentedSelectorStyle}", (string?)container.Parent?.Attribute("Style"));
        Assert.Equal(count, container.Elements().Count());
        Assert.All(container.Elements(), item =>
        {
            Assert.Equal("RadioButton", item.Name.LocalName);
            Assert.Equal("{StaticResource BasSegmentedRadioStyle}", (string?)item.Attribute("Style"));
        });
    }

    [Fact]
    public void SettingCards_PreserveConditionalGroupsAndDynamicScreenRows()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string[] groupNames = ["PanelClickEffectOptions", "PanelUnifiedEffectScale", "PanelSplitEffectScale", "PanelUnifiedAnimationSpeed", "PanelSplitAnimationSpeed"];
        foreach (string name in groupNames)
        {
            XElement group = GetNamedElement(document, name);
            Assert.Equal("StackPanel", group.Name.LocalName);
            Assert.Contains(group.Elements(), element => (string?)element.Attribute("Style") == "{StaticResource BasSettingCardStyle}");
            if (name.StartsWith("PanelSplit", StringComparison.Ordinal))
            {
                Assert.Equal("Collapsed", (string?)group.Attribute("Visibility"));
            }
        }
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("""TryGetAppResource<Style>("BasSettingCardStyle")""", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(toggle,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedNavigationAndSegments_KeepTheirFillWhileHovering()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        foreach (string key in new[] { "BasNavRadioStyle", "BasSubNavRadioStyle" })
        {
            XElement style = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == key);
            XElement selected = Assert.Single(style.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "Checked");
            Assert.Contains(selected.Descendants(), element => (string?)element.Attribute("Storyboard.TargetName") == "SelectionBackground");
            XElement common = Assert.Single(style.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "CommonStates");
            Assert.DoesNotContain(common.Descendants(), element => (string?)element.Attribute("Storyboard.TargetName") == "SelectionBackground");
        }
        XElement segmentStyle = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSegmentedRadioStyle");
        XElement selection = GetNamedElement(document, "SegmentedSelection");
        Assert.Equal("{ThemeResource BasSettingsAccentBrush}", (string?)selection.Attribute("Background"));
        Assert.DoesNotContain(segmentStyle.Descendants(), element => ((string?)element.Attribute("Target"))?.EndsWith(".Background", StringComparison.Ordinal) == true);
        Assert.Contains(segmentStyle.Descendants(), element => (string?)element.Attribute("Target") == "SegmentHover.Opacity");
    }

    [Fact]
    public void AllNavigationPages_UseTheFixedSurfaceAndConsistentTitles()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement surface = GetNamedElement(document, "ContentSurface");
        Assert.Equal("Border", surface.Name.LocalName);
        Assert.Equal("1", (string?)surface.Attribute("Grid.Column"));
        Assert.Null(surface.Attribute("Margin"));
        Assert.Equal("1", (string?)surface.Parent?.Attribute("Grid.Row"));
        foreach ((string pageName, string titleName) in new[]
                 {
                     ("PageWelcome", "TxtWelcomeTitle"), ("PageSettings", "TxtSettingsTitle"),
                     ("PageLog", "TxtLogTitle"), ("PageAbout", "TxtAboutTitle")
                 })
        {
            XElement page = GetNamedElement(document, pageName);
            Assert.Contains(surface, page.Ancestors());
            Assert.Null(page.Attribute("Margin"));
            Assert.Null(page.Attribute("Background"));
            Assert.Equal("{StaticResource BasSettingsPageTitleStyle}", (string?)GetNamedElement(document, titleName).Attribute("Style"));
            if (pageName != "PageSettings")
            {
                Assert.Equal("28,20,28,28", (string?)page.Attribute("Padding"));
            }
        }
    }

    [Fact]
    public void SegmentedSelectors_FillEqualCellsWithoutHeaderOrColumnGaps()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSegmentedRadioButtonsStyle");
        Assert.Null(style.Attribute("BasedOn"));
        XElement repeater = Assert.Single(style.Descendants(), element => element.Name.LocalName == "ItemsRepeater");
        Assert.Equal("InnerRepeater", (string?)repeater.Attribute(Xaml + "Name"));
        Assert.Single(style.Descendants(), element => element.Name.LocalName == "SegmentedRadioLayout");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("class SegmentedRadioLayout : NonVirtualizingLayout", source, StringComparison.Ordinal);
        Assert.Contains("index == count - 1 ? finalSize.Width", source, StringComparison.Ordinal);
        Assert.Contains("new Rect(left, 0, right - left, finalSize.Height)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(style.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "HeaderContentPresenter");
    }

    [Fact]
    public void CompactSwitches_KeepTheirVisibleEdgeAtTheCardPadding()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingToggleStyle");
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "Width" && (string?)element.Attribute("Value") == "44");
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "HorizontalAlignment" && (string?)element.Attribute("Value") == "Right");
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string key in new[] { "ToggleSwitchTopHeaderMargin", "ToggleSwitchPreContentMargin", "ToggleSwitchPostContentMargin" })
        {
            XElement spacing = Assert.Single(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == key);
            Assert.Equal("0", spacing.Value);
        }
    }

    [Fact]
    public void FilterDropdowns_PreserveTheModeOrderAndProcessDeletion()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement mode = GetNamedElement(document, "ComboProcessFilterMode");
        Assert.Equal("ComboBox", mode.Name.LocalName);
        Assert.Equal("ProcessFilterMode_Changed", (string?)mode.Attribute("SelectionChanged"));
        Assert.Equal(3, mode.Elements().Count());
        Assert.All(mode.Elements(), item => Assert.Equal("ComboBoxItem", item.Name.LocalName));
        XElement processes = GetNamedElement(document, "ListConfiguredProcesses");
        Assert.Equal("ComboBox", processes.Name.LocalName);
        Assert.Equal("200", (string?)processes.Attribute("MaxDropDownHeight"));
        Assert.Equal("{Binding Text, ElementName=TxtProcessList}", (string?)processes.Attribute("PlaceholderText"));
        XElement remove = Assert.Single(processes.Descendants(), element => (string?)element.Attribute("Click") == "RemoveProcess_Click");
        Assert.Equal("{Binding}", (string?)remove.Attribute("Tag"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("ListConfiguredProcesses.ItemsSource = CurrentProfileProcesses", source, StringComparison.Ordinal);
        Assert.Contains("ListConfiguredProcesses.SelectedIndex = CurrentProfileProcesses.Count > 0 ? 0 : -1", source, StringComparison.Ordinal);
        foreach ((int index, string key) in new[] { (0, "Filter_Mode_Disabled"), (1, "Filter_Mode_Blacklist"), (2, "Filter_Mode_Whitelist") })
        {
            Assert.Contains($"SetComboItemContent(ComboProcessFilterMode, {index}, \"{key}\")", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.en.resx")]
    [InlineData("Strings.ja.resx")]
    public void FilterModeLabels_HaveNoSymbolsAndRemovedWarningsHaveNoUiReferences(string resourceName)
    {
        XDocument resources = XDocument.Parse(ReadSource("src", resourceName));
        foreach (string key in new[] { "Filter_Mode_Disabled", "Filter_Mode_Blacklist", "Filter_Mode_Whitelist" })
        {
            XElement entry = Assert.Single(resources.Root!.Elements("data"), element => (string?)element.Attribute("name") == key);
            string text = entry.Element("value")!.Value;
            Assert.NotEmpty(text);
            Assert.True(char.IsLetter(text[0]));
        }
        Assert.DoesNotContain(resources.Root!.Elements("data"), element => (string?)element.Attribute("name") is "Settings_ApplyHint" or "About_SecurityWarning");
        string xaml = ReadSource("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        foreach (string name in new[] { "TxtSettingsHint", "SecurityWarningBar", "TxtSecurityWarning" })
        {
            Assert.DoesNotContain(name, xaml, StringComparison.Ordinal);
            Assert.DoesNotContain(name, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HomeStatistics_UseFullWidthSettingCardsWithRightAlignedValues()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach ((string labelName, string valueName) in new[] { ("TxtStatusLabel", "StatusText"), ("TxtClicksLabel", "ClickCountText") })
        {
            XElement label = GetNamedElement(document, labelName);
            XElement value = GetNamedElement(document, valueName);
            XElement card = GetSettingCard(value);
            Assert.Same(card, GetSettingCard(label));
            Assert.Null(card.Attribute("Width"));
            Assert.Null(card.Attribute("MinWidth"));
            Assert.Null(card.Attribute("HorizontalAlignment"));
            Assert.Equal("0", (string?)label.Attribute("Grid.Column"));
            Assert.Equal("1", (string?)value.Attribute("Grid.Column"));
            Assert.Equal("Right", (string?)value.Attribute("HorizontalAlignment"));
            Assert.Equal((string?)label.Attribute("Style"), (string?)value.Attribute("Style"));
            Assert.Equal("{StaticResource BasSettingTitleStyle}", (string?)value.Attribute("Style"));
            Assert.Null(value.Attribute("FontSize"));
            Assert.Null(value.Attribute("FontWeight"));
        }
    }

    [Fact]
    public void SettingsSubNavigation_AlignsTextAndRightEdgeWithItsParent()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement parent = GetNamedElement(panel, "TabSettings");
        XElement parentContent = Assert.Single(parent.Elements());
        XElement icon = Assert.Single(parentContent.Elements(), element => element.Name.LocalName == "FontIcon");
        Assert.Equal("16", (string?)icon.Attribute("Width"));
        Assert.Equal("12", (string?)parentContent.Attribute("Spacing"));
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement parentStyle = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasNavRadioStyle");
        XElement childStyle = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSubNavRadioStyle");
        Assert.Contains(parentStyle.Elements(), element => (string?)element.Attribute("Property") == "Margin" && (string?)element.Attribute("Value") == "2,0");
        Assert.Contains(childStyle.Elements(), element => (string?)element.Attribute("Property") == "Margin" && (string?)element.Attribute("Value") == "30,0,2,0");
        XElement parentPresenter = Assert.Single(parentStyle.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "ContentPresenter");
        XElement childPresenter = Assert.Single(childStyle.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "ContentPresenter");
        Assert.Equal("9,0,12,0", (string?)parentPresenter.Attribute("Margin"));
        Assert.Equal("12,0,12,0", (string?)childPresenter.Attribute("Margin"));
        XElement indicatorColumn = Assert.Single(parentStyle.Descendants(), element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().First();
        Assert.Equal("3", (string?)indicatorColumn.Attribute("Width"));
        Assert.Equal(2 + 3 + 9 + 16 + 12, 30 + 12);
        XElement childNavigation = GetNamedElement(panel, "SettingsSubNav");
        Assert.Equal(4, childNavigation.Elements().Count());
        Assert.All(childNavigation.Elements(), element => Assert.Equal("{StaticResource BasSubNavRadioStyle}", (string?)element.Attribute("Style")));
    }

    [Fact]
    public void SettingsSubNavigation_AnimatesItsAccentLikeTheParent()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        foreach (string key in new[] { "BasNavRadioStyle", "BasSubNavRadioStyle" })
        {
            XElement style = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == key);
            XElement indicator = Assert.Single(style.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "SelectionIndicator");
            Assert.Equal("0.5,0.5", (string?)indicator.Attribute("RenderTransformOrigin"));
            XElement scale = Assert.Single(indicator.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "IndicatorScale");
            Assert.Equal("0.35", (string?)scale.Attribute("ScaleY"));
            XElement selected = Assert.Single(style.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "Checked");
            XElement grow = Assert.Single(selected.Descendants(), element => element.Name.LocalName == "DoubleAnimation" && (string?)element.Attribute("Storyboard.TargetName") == "IndicatorScale");
            Assert.Equal("ScaleY", (string?)grow.Attribute("Storyboard.TargetProperty"));
            Assert.Equal("1", (string?)grow.Attribute("To"));
            Assert.Equal("0:0:0.22", (string?)grow.Attribute("Duration"));
            Assert.Contains(grow.Descendants(), element => element.Name.LocalName == "CubicEase");
            XElement uncheckedState = Assert.Single(style.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "Unchecked");
            Assert.Contains(uncheckedState.Descendants(), element => (string?)element.Attribute("Storyboard.TargetName") == "IndicatorScale" && (string?)element.Attribute("To") == "0.35");
            Assert.DoesNotContain(style.Descendants(), element => (string?)element.Attribute("EnableDependentAnimation") == "True");
        }
    }

    [Fact]
    public void ControlPanel_UsesTheSystemFontWithAMonospaceLogException()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal("{ThemeResource ContentControlThemeFontFamily}", (string?)document.Root?.Attribute("FontFamily"));
        Assert.Equal("Consolas", (string?)GetNamedElement(document, "TxtAppLog").Attribute("FontFamily"));
        Assert.DoesNotContain("Cascadia Mono", ReadSource("src", "ControlPanelWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void EffectColor_UsesANativeInlineExpanderAndPicker()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement expander = GetNamedElement(document, "EffectColorExpander");
        Assert.Equal("Expander", expander.Name.LocalName);
        Assert.Equal("{StaticResource BasSettingExpanderStyle}", (string?)expander.Attribute("Style"));
        XElement card = GetSettingCard(expander);
        Assert.Equal("0", (string?)card.Attribute("Padding"));
        Assert.Contains(GetNamedElement(document, "SectionVisual"), card.Ancestors());
        XElement picker = GetNamedElement(document, "EffectColorPicker");
        Assert.Equal("ColorPicker", picker.Name.LocalName);
        Assert.Contains(expander, picker.Ancestors());
        Assert.Equal("{StaticResource BasColorPickerStyle}", (string?)picker.Attribute("Style"));
        Assert.Equal("SaturationValue", (string?)picker.Attribute("ColorSpectrumComponents"));
        Assert.Equal("True", (string?)picker.Attribute("IsAlphaEnabled"));
        Assert.Equal("True", (string?)picker.Attribute("IsAlphaSliderVisible"));
        Assert.Equal("EffectColorPicker_ColorChanged", (string?)picker.Attribute("ColorChanged"));
        XElement hex = GetNamedElement(document, "EffectColorHexInput");
        Assert.Equal("9", (string?)hex.Attribute("MaxLength"));
        Assert.Equal("EffectColorHexInput_LostFocus", (string?)hex.Attribute("LostFocus"));
        Assert.Equal("EffectColorHexInput_KeyDown", (string?)hex.Attribute("KeyDown"));
        Assert.Equal("Grid", picker.Parent?.Name.LocalName);
        Assert.Equal("12", (string?)picker.Parent?.Attribute("ColumnSpacing"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.DoesNotContain("PickColor_Click", source, StringComparison.Ordinal);
        Assert.Contains("_effectOpacity = Math.Clamp((_colorAlphaSlider?.Value", source, StringComparison.Ordinal);
        Assert.Contains("_syncingColorControls", source, StringComparison.Ordinal);
        Assert.Contains("UpdateColorPreview(_particleColor)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SliderOpacity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectColorPresets_UseAccessibleNativeSwatchesWithValidRgbValues()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement presets = GetNamedElement(document, "EffectColorPresets");
        Assert.Equal("RadioButtons", presets.Name.LocalName);
        Assert.Equal("{StaticResource BasColorPresetsStyle}", (string?)presets.Attribute("Style"));
        Assert.Equal(12, presets.Elements().Count());
        Assert.All(presets.Elements(), swatch =>
        {
            Assert.Equal("RadioButton", swatch.Name.LocalName);
            Assert.Equal("{StaticResource BasColorSwatchStyle}", (string?)swatch.Attribute("Style"));
            string? hex = (string?)swatch.Attribute("Tag");
            Assert.True(ColorPickerColorMath.TryParseHex(hex, out Color color));
            Assert.Equal(hex, ColorPickerColorMath.ToHex(color));
            Assert.Equal(hex, (string?)swatch.Attribute("Background"));
            Assert.Equal(hex, (string?)swatch.Attribute("AutomationProperties.Name"));
        });
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorPresetsStyle");
        Assert.Single(style.Descendants(), element => element.Name.LocalName == "ColorPresetLayout");
        Assert.Contains("private const int Columns = 6", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystem_StyleInheritanceResolvesWithinApplicationResources()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        XElement[] styles = document.Descendants(Presentation + "Style").ToArray();
        HashSet<string> styleKeys = styles
            .Select(style => (string?)style.Attribute(Xaml + "Key"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        styleKeys.Add("DefaultContentDialogStyle");

        foreach (XElement style in styles.Where(style => style.Attribute("BasedOn") is not null))
        {
            string reference = style.Attribute("BasedOn")!.Value;
            Assert.StartsWith("{StaticResource ", reference, StringComparison.Ordinal);
            Assert.EndsWith("}", reference, StringComparison.Ordinal);
            Assert.Contains(reference["{StaticResource ".Length..^1], styleKeys);
        }

        XElement expander = Assert.Single(styles, style => (string?)style.Attribute(Xaml + "Key") == "BasSettingExpanderStyle");
        Assert.Null(expander.Attribute("BasedOn"));
        Assert.Contains(expander.Elements(), setter => (string?)setter.Attribute("Property") == "Template");
        Assert.Single(expander.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "ExpanderContentHost");
    }

    [Fact]
    public void MainNavigation_UsesEqualCollapsedGapsAndARedCloseHover()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement before = GetNamedElement(document, "NavBeforeSettings");
        XElement after = GetNamedElement(document, "NavAfterSettings");
        Assert.Equal("4", (string?)before.Attribute("Spacing"));
        Assert.Equal("4", (string?)after.Attribute("Spacing"));
        Assert.Equal("4", (string?)after.Attribute("Margin")?.Value.Split(',')[1]);
        Assert.Equal("0", (string?)before.Parent?.Attribute("Spacing"));
        XElement close = GetNamedElement(document, "BtnCaptionClose");
        Assert.Contains(close.Descendants(), resource => (string?)resource.Attribute(Xaml + "Key") == "ButtonBackgroundPointerOver" && (string?)resource.Attribute("Color") == "#C42B1C");
        Assert.Contains(close.Descendants(), resource => (string?)resource.Attribute(Xaml + "Key") == "ButtonForegroundPointerOver" && (string?)resource.Attribute("Color") == "White");
    }

    [Fact]
    public void ProcessDropdown_KeepsTheCompactCardHeightWhenOpened()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement dropdown = GetNamedElement(document, "ListConfiguredProcesses");
        Assert.Equal("32", (string?)dropdown.Attribute("Height"));
        Assert.Equal("Center", (string?)dropdown.Attribute("VerticalContentAlignment"));
        Assert.Equal("10,0,28,0", (string?)dropdown.Attribute("Padding"));
        XElement remove = Assert.Single(dropdown.Descendants(), element => (string?)element.Attribute("Click") == "RemoveProcess_Click");
        Assert.Equal("20", (string?)remove.Attribute("Height"));
        Assert.Equal("0", (string?)remove.Attribute("MinHeight"));
        Assert.Equal("0", (string?)remove.Attribute("Padding"));
        Assert.Equal("Center", (string?)remove.Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)remove.Attribute("VerticalContentAlignment"));
        Assert.Equal("Center", (string?)remove.Parent!.Attribute("VerticalAlignment"));
    }

    [Fact]
    public void ControlPanel_MinimumWidthAccommodatesAllSettingsControls()
    {
        Assert.Contains("MinWidthDesign = 800", ReadSource("src", "DcompPanelHost.cs"), StringComparison.Ordinal);
        Assert.Contains("DesignWidth = 800", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsText_UsesSemiboldTitlesAndNoScrollbarPreferenceOrLeadingHintStars()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        foreach (string key in new[] { "BasPageTitleStyle", "BasSectionTitleStyle", "BasSettingTitleStyle" })
        {
            XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == key);
            Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "FontWeight" && (string?)setter.Attribute("Value") == "SemiBold");
        }

        Assert.DoesNotContain("RadioScrollbar", ReadSource("src", "ControlPanelWindow.xaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetSelectedScrollbarVisibility", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        foreach (string file in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            XDocument resources = LoadXaml("src", file);
            Assert.DoesNotContain(resources.Descendants("data"), element => ((string?)element.Attribute("name"))?.StartsWith("Basic_Scrollbar", StringComparison.Ordinal) == true);
            foreach (string key in new[] { "Basic_RunAsAdminHint", "Basic_ScreenshotHint", "Basic_TouchscreenHint" })
            {
                XElement hint = Assert.Single(resources.Descendants("data"), element => (string?)element.Attribute("name") == key);
                Assert.False(hint.Element("value")!.Value.StartsWith('*'));
            }
        }
    }

    [Fact]
    public void EffectColor_UsesCompactNativeSlidersWithUnclippedSpectrumAndSquareSwatches()
    {
        XDocument document = LoadXaml("src", "DesignSystem.xaml");
        XElement headerStyle = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorExpanderHeaderStyle");
        Assert.Contains(headerStyle.Elements(), setter => (string?)setter.Attribute("Property") == "HorizontalAlignment" && (string?)setter.Attribute("Value") == "Stretch");
        XElement picker = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorPickerStyle");
        XElement spectrum = Assert.Single(picker.Descendants(), element => element.Name.LocalName == "ColorSpectrum");
        Assert.Equal("148", (string?)spectrum.Attribute("Width"));
        Assert.Equal("148", (string?)spectrum.Attribute("Height"));
        Assert.Equal("8", (string?)spectrum.Attribute("Margin"));
        Assert.Equal(2, picker.Descendants().Count(element => element.Name.LocalName == "ColorPickerSlider" && (string?)element.Attribute("Orientation") == "Vertical"));
        Assert.All(picker.Descendants().Where(element => element.Name.LocalName == "ColorPickerSlider"),
            slider => Assert.Equal("{StaticResource BasColorPickerSliderStyle}", (string?)slider.Attribute("Style")));
        XElement sliderStyle = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorPickerSliderStyle");
        XElement thumb = Assert.Single(sliderStyle.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "VerticalThumb");
        Assert.Equal("20", (string?)thumb.Attribute("Width"));
        Assert.Equal("6", (string?)thumb.Attribute("Height"));
        Assert.Equal("White", (string?)thumb.Attribute("BorderBrush"));
        Assert.Equal("3", (string?)Assert.Single(thumb.Descendants(), element => element.Name.LocalName == "Border").Attribute("CornerRadius"));
        XElement swatch = Assert.Single(document.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorSwatchStyle");
        foreach (string dimension in new[] { "Width", "Height" })
        {
            Assert.Contains(swatch.Elements(), setter => (string?)setter.Attribute("Property") == dimension && (string?)setter.Attribute("Value") == "28");
        }

        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("new Rect(-8, -8, spectrum.ActualWidth + 16, spectrum.ActualHeight + 16)", source, StringComparison.Ordinal);
        Assert.Contains("CreateRoundedRectangleGeometry", source, StringComparison.Ordinal);
        Assert.Contains("_colorCardGeometry.StartAnimation(\"Size.Y\", animation)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectColor_DragFeedbackIsFrameCoalescedWithoutNativePickerWriteback()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        int begin = source.IndexOf("private void EffectColorPicker_ColorChanged", StringComparison.Ordinal);
        int end = source.IndexOf("private void EffectColorPresets_SelectionChanged", begin, StringComparison.Ordinal);
        string feedback = source[begin..end];
        Assert.Contains("CompositionTarget.Rendering", feedback, StringComparison.Ordinal);
        Assert.Contains("updatePicker: false", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("EffectColorPicker.Color =", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("EffectColorCard.Height =", source, StringComparison.Ordinal);
        Assert.Contains("CreateGeometricClip", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectColor_PreviewsHaveCheckersAndAlphaStopsAboveTheTrackBottom()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "ColorPreview", "ExpandedColorPreview" })
        {
            XElement preview = GetNamedElement(panel, name);
            Assert.Equal("Top", (string?)preview.Attribute("VerticalAlignment"));
            Assert.Contains(preview.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == name + "Checkers");
            Assert.Contains(preview.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == name + "Fill");
        }

        Assert.DoesNotContain(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "SliderOpacity");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("args.NewValue < 10", source, StringComparison.Ordinal);
        Assert.Contains("_colorAlphaSlider.Value = 10", source, StringComparison.Ordinal);
        XDocument design = LoadXaml("src", "DesignSystem.xaml");
        XElement slider = Assert.Single(design.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorPickerSliderStyle");
        XElement thumb = Assert.Single(slider.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "VerticalThumb");
        Assert.Equal("Transparent", (string?)thumb.Attribute("Background"));
    }

    [Fact]
    public void AboutAndApplicationTypography_FollowTheSharedSettingsDesign()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement about = GetNamedElement(panel, "PageAbout");
        Assert.Equal(4, about.Descendants().Count(element => (string?)element.Attribute("Style") == "{StaticResource BasSettingCardStyle}"));
        Assert.DoesNotContain(about.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "BtnResetAll");
        Assert.Equal("{StaticResource BasSettingTitleStyle}", (string?)GetNamedElement(panel, "AboutVersionText").Attribute("Style"));
        Assert.DoesNotContain("TxtDevOptions", ReadSource("src", "ControlPanelWindow.xaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("TxtDevOptions", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        XDocument app = LoadXaml("src", "App.xaml");
        Assert.Contains(app.Descendants(), element => element.Name.LocalName == "XamlControlsResources");
        Assert.DoesNotContain(app.Descendants(), element => element.Name.LocalName == "Style" && element.Attribute(Xaml + "Key") == null);
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.DoesNotContain("ApplySystemBackdrop", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to apply the solid backdrop", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutRepositoryCards_ShowRequestedUrlsWithRightAlignedOpenButtons()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement about = GetNamedElement(panel, "PageAbout");
        Assert.Equal("{StaticResource BasSettingsSectionTitleStyle}", (string?)GetNamedElement(panel, "TxtRepositoryLinks").Attribute("Style"));
        foreach ((string name, string url) in new[]
                 {
                     ("BtnRepoDoomVoss", "https://github.com/DoomVoss/BASpark"),
                     ("BtnRepoCialloKing", "https://github.com/CialloKing/BASpark"),
                     ("BtnRepoWinUI", "https://github.com/Hdy08/BASpark/tree/WinUI3")
                 })
        {
            XElement button = GetNamedElement(panel, name);
            Assert.Equal(url, (string?)button.Attribute("Tag"));
            Assert.Equal("OpenRepository_Click", (string?)button.Attribute("Click"));
            Assert.Equal("1", (string?)button.Attribute("Grid.Column"));
            Assert.Equal("Right", (string?)button.Attribute("HorizontalAlignment"));
            XElement card = GetSettingCard(button);
            Assert.Contains(about, card.Ancestors());
            XElement title = Assert.Single(card.Descendants(), element => element.Name.LocalName == "TextBlock");
            Assert.Equal("{Binding Tag, ElementName=" + name + "}", (string?)title.Attribute("Text"));
            Assert.Equal("{StaticResource BasSettingTitleStyle}", (string?)title.Attribute("Style"));
            Assert.Contains(name + ".Content = Localization.Get(\"About_OpenRepository\")", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        }
        foreach (string file in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            XDocument resources = LoadXaml("src", file);
            Assert.DoesNotContain(resources.Descendants("data"), element => (string?)element.Attribute("name") == "About_DevOptions");
            foreach (string key in new[] { "About_Repositories", "About_OpenRepository" })
            {
                XElement item = Assert.Single(resources.Descendants("data"), element => (string?)element.Attribute("name") == key);
                Assert.False(string.IsNullOrWhiteSpace(item.Element("value")?.Value));
            }
        }
    }

    [Fact]
    public void AboutRepositoryActions_UseTheSystemBrowserAndLocalizedFailureFeedback()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string handler = source[source.IndexOf("private async void OpenRepository_Click", StringComparison.Ordinal)..source.IndexOf("private void LoadVersion()", StringComparison.Ordinal)];
        Assert.Contains("sender is not Button { Tag: string url }", handler, StringComparison.Ordinal);
        Assert.Contains("Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })", handler, StringComparison.Ordinal);
        Assert.Contains("Localization.Format(\"Msg_OpenLinkFailed\", exception.Message)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeTrayMenu_UsesSystemRenderingAndThemeWithNoOwnerDraw()
    {
        string source = ReadSource("src", "TrayIconController.cs");
        Assert.Contains("CreatePopupMenu()", source, StringComparison.Ordinal);
        Assert.Contains("TrackPopupMenuEx(menu, 0x102", source, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow(_messageWindow.Handle)", source, StringComparison.Ordinal);
        Assert.Contains("DestroyMenu(menu)", source, StringComparison.Ordinal);
        Assert.Contains("PreferredAppMode.AllowDark", source, StringComparison.Ordinal);
        Assert.Contains("SystemInformation.HighContrast", source, StringComparison.Ordinal);
        Assert.Contains("_refreshColorPolicy?.Invoke()", source, StringComparison.Ordinal);
        Assert.Contains("_flushMenuThemes?.Invoke()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextMenuStrip", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolStripRenderer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenTrayPreference_FollowsSilentStartupAndCanReopenThePanel()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement silent = GetSettingCard(GetNamedElement(panel, "CheckStartSilent"));
        XElement hidden = GetSettingCard(GetNamedElement(panel, "CheckHideTrayIcon"));
        Assert.Same(hidden, silent.ElementsAfterSelf().First());
        AssertSettingHint(GetNamedElement(panel, "CheckHideTrayIcon"), GetNamedElement(panel, "TxtHideTrayHint"));
        string config = ReadSource("src", "ConfigManager.cs");
        Assert.Contains("public static bool HideTrayIcon { get; set; } = false", config, StringComparison.Ordinal);
        Assert.Contains("key.GetValue(\"HideTrayIcon\", false)", config, StringComparison.Ordinal);
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("CheckHideTrayIcon.IsOn = ConfigManager.HideTrayIcon", source, StringComparison.Ordinal);
        Assert.Contains("ConfigManager.Save(\"HideTrayIcon\", hideTrayIcon)", source, StringComparison.Ordinal);
        Assert.Contains("App.Tray?.SetHidden(hideTrayIcon)", source, StringComparison.Ordinal);
        string tray = ReadSource("src", "TrayIconController.cs");
        Assert.Contains("Visible = !ConfigManager.HideTrayIcon", tray, StringComparison.Ordinal);
        Assert.Contains("TrayMessageWindow(Action openPanel) : Form", tray, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false", tray, StringComparison.Ordinal);
        Assert.Contains("FindWindow(null, HiddenTrayWindowTitle)", tray, StringComparison.Ordinal);
        Assert.Contains("TrayIconController.TryShowHiddenControlPanel()", ReadSource("src", "App.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ResetActions_UseOneSearchableCardListWithAllFourSettingsGroups()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement overlay = GetNamedElement(panel, "VisualResetOverlay");
        Assert.Equal("{StaticResource BasModalCardStyle}", (string?)overlay.Elements().Single().Attribute("Style"));
        foreach (string name in new[] { "TxtOverlayVisualReset", "SearchVisualReset", "ListVisualResetItems", "BtnOverlayVisualCancel", "BtnOverlayVisualConfirm" })
            Assert.Contains(overlay, GetNamedElement(panel, name).Ancestors());
        Assert.Equal("{StaticResource AccentButtonStyle}", (string?)GetNamedElement(panel, "BtnOverlayVisualConfirm").Attribute("Style"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        foreach (string group in new[] { "Basic", "Visual", "Filter", "Screen" })
            Assert.Contains("Rebuild" + group + "ResetItems()", source, StringComparison.Ordinal);
        Assert.Contains("item.Group.Contains(filter", source, StringComparison.Ordinal);
        Assert.Contains("_resetScope == ResetScope.All && group != item.Group", source, StringComparison.Ordinal);
        Assert.Contains("rows.Add(group)", source, StringComparison.Ordinal);
        Assert.Contains("rows.Add(item)", source, StringComparison.Ordinal);
        Assert.Contains("ListVisualResetItems.ItemsSource = rows", source, StringComparison.Ordinal);
        XElement card = GetNamedElement(panel, "SelectionCard");
        Assert.Equal("{StaticResource BasSettingCardStyle}", (string?)card.Attribute("Style"));
        Assert.Contains(card.Descendants(), element => (string?)element.Attribute("Style") == "{StaticResource BasSettingTitleStyle}");
        Assert.Contains(card.Descendants(), element => (string?)element.Attribute("Style") == "{StaticResource BasCaptionStyle}");
        XElement check = GetNamedElement(panel, "SelectionCardCheck");
        Assert.Equal("Right", (string?)check.Attribute("HorizontalAlignment"));
        Assert.Equal("1", (string?)check.Attribute("Grid.Column"));
        Assert.Equal("{Binding IsSelected, Mode=TwoWay}", (string?)check.Attribute("IsChecked"));
    }

    [Fact]
    public void SelectionCards_NotifyOnlyActualChangesAndPreserveProcessNames()
    {
        var item = new ProcessItem { DisplayName = "Example application", ProcessName = "example.exe" };
        int notifications = 0;
        item.PropertyChanged += (_, args) =>
        {
            Assert.Equal(nameof(ProcessItem.IsSelected), args.PropertyName);
            notifications++;
        };
        item.IsSelected = true;
        item.IsSelected = true;
        item.IsSelected = false;
        Assert.Equal(2, notifications);
        Assert.Equal("Example application", item.Title);
        Assert.Equal("example.exe", item.Subtitle);
        item.DisplayName = "EXAMPLE.EXE";
        Assert.Empty(item.Subtitle);
    }

    [Fact]
    public void PopupSelectionLists_UseVirtualizedDataTemplatesAndToggleWholeCards()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "ListVisualResetItems", "ListRunningProcesses" })
        {
            XElement list = GetNamedElement(panel, name);
            Assert.Equal("None", (string?)list.Attribute("SelectionMode"));
            Assert.Equal("{StaticResource SelectionCardTemplates}", (string?)list.Attribute("ItemTemplateSelector"));
            Assert.Equal("{StaticResource SelectionCardContainerStyle}", (string?)list.Attribute("ItemContainerStyle"));
            Assert.Equal("0,0,-16,0", (string?)list.Attribute("Margin"));
            Assert.Single(list.Descendants(), element => element.Name.LocalName == "ItemsStackPanel");
            Assert.Single(list.Descendants(), element => element.Name.LocalName == "TransitionCollection");
        }
        XElement container = Assert.Single(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "SelectionCardContainerStyle");
        Assert.Contains(container.Elements(), element => (string?)element.Attribute("Property") == "Margin" && (string?)element.Attribute("Value") == "0,0,16,0");
        XElement card = GetNamedElement(panel, "SelectionCard");
        Assert.Equal("SelectionCard_Tapped", (string?)card.Attribute("Tapped"));
        Assert.Equal("0", (string?)card.Attribute("MinHeight"));
        Assert.Null(card.Attribute("Height"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("if (source is CheckBox) return false", source, StringComparison.Ordinal);
        Assert.Contains("item.IsSelected = !item.IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Border { Child = layout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListRunningProcesses.SelectedItems", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderAndProcessButtons_UseMatchedWidthsAndNativeSizing()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement apply = GetNamedElement(panel, "BtnApplySettings");
        Assert.Null(apply.Attribute("Width"));
        Assert.Equal("ResetSettings_SizeChanged", (string?)GetNamedElement(panel, "BtnResetSettings").Attribute("SizeChanged"));
        Assert.Contains("BtnApplySettings.Width = args.NewSize.Width", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Equal("0", (string?)apply.Attribute("MinWidth"));
        foreach (string name in new[] { "BtnBrowseProcess", "BtnSelectRunningProcess" })
        {
            XElement button = GetNamedElement(panel, name);
            foreach (string property in new[] { "FontSize", "Padding", "Height", "MinWidth" })
                Assert.Null(button.Attribute(property));
        }
        XElement reset = GetNamedElement(panel, "BtnResetSettings");
        Assert.Null(reset.Attribute("Template"));
        XElement dangerResources = Assert.Single(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "DangerButtonResources");
        Assert.Contains(dangerResources.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "ButtonBackgroundPointerOver" && (string?)element.Attribute("Color") == "#C42B1C");
        Assert.Contains(dangerResources.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "ButtonForegroundPointerOver" && (string?)element.Attribute("Color") == "White");
        Assert.Contains(dangerResources.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "HighContrast");
        Assert.Contains("new[] { BtnResetSettings, BtnDeleteProfile }", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PopupCopy_UsesDefaultRestorationAndProcessNameSearch()
    {
        XDocument chinese = LoadXaml("src", "Strings.resx");
        XElement title = Assert.Single(chinese.Descendants("data"), element => (string?)element.Attribute("name") == "Overlay_AllReset");
        Assert.Equal("选择要恢复默认的设置项", title.Element("value")!.Value);
        XElement search = Assert.Single(chinese.Descendants("data"), element => (string?)element.Attribute("name") == "Overlay_SearchProcesses");
        Assert.Equal("搜索进程名", search.Element("value")!.Value);
        foreach (string file in new[] { "Strings.en.resx", "Strings.ja.resx" })
            Assert.Single(LoadXaml("src", file).Descendants("data"), element => (string?)element.Attribute("name") == "Overlay_SearchProcesses");
        Assert.Contains("SearchRunningProcess.PlaceholderText = Localization.Get(\"Overlay_SearchProcesses\")", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DependentSettings_AnimateOnlyExplicitToggleChangesWithoutPermanentLayoutTransitions()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        Assert.DoesNotContain(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") is "BasSettingsListStyle" or "BasCollapsibleSettingsStyle");
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "SectionBasic", "SectionVisual", "SectionFilter", "SectionMultiScreen", "PanelClickEffectOptions", "PanelUnifiedEffectScale", "PanelSplitEffectScale", "PanelUnifiedAnimationSpeed", "PanelSplitAnimationSpeed" })
        {
            XElement element = GetNamedElement(panel, name);
            Assert.Null(element.Attribute("Style"));
            Assert.DoesNotContain(element.Elements(), child => child.Name.LocalName.EndsWith("Transitions", StringComparison.Ordinal));
        }
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        foreach (string name in new[] { "UpdateClickEffectPanelVisibility", "UpdateEffectScalePanelVisibility", "UpdateAnimationSpeedPanelVisibility" })
        {
            Assert.Contains($"private void {name}(bool animate = false)", source, StringComparison.Ordinal);
            Assert.Equal(1, source.Split($"{name}(animate: true)", StringSplitOptions.None).Length - 1);
        }
        Assert.Contains("animate && !_isLoading && IsUiReady && PageSettings.Visibility == Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("private void UpdatePageVisibility()\n    {\n        StopSettingsAnimations()", source, StringComparison.Ordinal);
        Assert.Contains("private void UpdateSettingsSectionVisibility()\n    {\n        StopSettingsAnimations()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DependentSettings_RepositionExistingCardsAndFadeNewCardsAtTheirFinalLayoutSlots()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string animate = source[source.IndexOf("private void SetSettingsVisibility", StringComparison.Ordinal)..source.IndexOf("private void StopSettingsAnimation(", StringComparison.Ordinal)];
        Assert.True(animate.IndexOf("var positions", StringComparison.Ordinal) < animate.IndexOf("foreach (var change in changes) change.Element.Visibility =", StringComparison.Ordinal));
        Assert.Contains("previousTop - element.TransformToVisual(section).TransformPoint(new Point()).Y : 0", animate, StringComparison.Ordinal);
        Assert.Contains("new RepositionThemeAnimation", animate, StringComparison.Ordinal);
        Assert.Contains("new FadeInThemeAnimation()", animate, StringComparison.Ordinal);
        Assert.Equal(1, animate.Split("storyboard.Begin()", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("EntranceThemeTransition", animate, StringComparison.Ordinal);
    }

    [Fact]
    public void ModalOverlays_UseNativeOpenAndCloseAnimationsAndWaitBeforeHidingContent()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("card.RenderTransformOrigin = new Point(0.5, 0.5)", source, StringComparison.Ordinal);
        Assert.Contains("new SplineDoubleKeyFrame", source, StringComparison.Ordinal);
        Assert.Contains("Value = visible ? 1.05 : 1", source, StringComparison.Ordinal);
        Assert.Contains("ControlNormalAnimationDuration", source, StringComparison.Ordinal);
        Assert.Contains("ControlFasterAnimationDuration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PopInThemeAnimation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PopOutThemeAnimation", source, StringComparison.Ordinal);
        Assert.Contains("previous.Completion.TrySetResult(false)", source, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(current, state)", source, StringComparison.Ordinal);
        Assert.Contains("animation.Completion.TrySetResult(false)", source, StringComparison.Ordinal);
        foreach (string name in new[] { "RunningProcessOverlay", "VisualResetOverlay", "RenameProfileOverlay" })
        {
            Assert.Contains($"SetModalOverlayVisibleAsync({name}, visible: true)", source, StringComparison.Ordinal);
            Assert.Contains($"SetModalOverlayVisibleAsync({name}, visible: false)", source, StringComparison.Ordinal);
            Assert.DoesNotContain($"{name}.Visibility =", source, StringComparison.Ordinal);
        }
        string close = source[source.IndexOf("private async void CloseVisualResetOverlay_Click", StringComparison.Ordinal)..source.IndexOf("private void SearchVisualReset_TextChanged", StringComparison.Ordinal)];
        Assert.True(close.IndexOf("await SetModalOverlayVisibleAsync", StringComparison.Ordinal) < close.IndexOf("VisualResetItems.Clear()", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeMessageDialogs_ShareModalBrushesNativeButtonSizesAndOmitResetSuccessTitles()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasContentDialogStyle");
        Assert.Equal("{StaticResource DefaultContentDialogStyle}", (string?)style.Attribute("BasedOn"));
        Assert.DoesNotContain(style.Elements(), element => (string?)element.Attribute("Property") == "Template");
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "Background" && (string?)element.Attribute("Value") == "{ThemeResource BasLayerBrush}");
        foreach (XElement theme in styles.Root!.Element(Presentation + "ResourceDictionary.ThemeDictionaries")!.Elements())
        {
            Assert.Contains(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "ContentDialogSmokeFill" && (string?)element.Attribute("ResourceKey") == "BasModalScrimBrush");
            Assert.Contains(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "ContentDialogTopOverlay" && (string?)element.Attribute("ResourceKey") == "BasLayerBrush");
        }
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("Title = title == string.Empty ? null : title ?? Localization.Get(\"Msg_Info\")", source, StringComparison.Ordinal);
        Assert.Contains("ShowMessageAsync(Localization.Get(_resetScope == ResetScope.Visual ? \"Msg_VisualResetDone\" : \"Msg_PageResetDone\"), string.Empty)", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[\"ContentDialogMinHeight\"] = 0d", source, StringComparison.Ordinal);
        Assert.Contains("PanelBody.TransformToVisual(RootGrid)", source, StringComparison.Ordinal);
        Assert.Contains("scrim.Clip = new RectangleGeometry", source, StringComparison.Ordinal);
        Assert.Contains("column.Width = GridLength.Auto", source, StringComparison.Ordinal);
        Assert.Contains("button.MinWidth = 0", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[\"ContentDialogSmokeFill\"] = VisualResetOverlay.Background", source, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot)", source, StringComparison.Ordinal);
        Assert.Contains("scrim.Fill = VisualResetOverlay.Background", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[\"ContentDialogTopOverlay\"] = ((Border)VisualResetOverlay.Children[0]).Background", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccentButtons_KeepNativeTemplatesWithDistinctLightAndDarkInteractionColors()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        foreach (XElement theme in styles.Root!.Element(Presentation + "ResourceDictionary.ThemeDictionaries")!.Elements().Where(element => (string?)element.Attribute(Xaml + "Key") is "Light" or "Dark"))
        {
            string color = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentColor").Value;
            string hover = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentHoverColor").Value;
            string pressed = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentPressedColor").Value;
            Assert.NotEqual(color, hover);
            Assert.NotEqual(hover, pressed);
            Assert.NotEqual(color, pressed);
        }
        Assert.Contains(styles.Root!.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "AccentButtonBackgroundPointerOver" && (string?)element.Attribute("Color") == "{ThemeResource BasSettingsAccentHoverColor}");
        Assert.Contains(styles.Root!.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "AccentButtonBackgroundPressed" && (string?)element.Attribute("Color") == "{ThemeResource BasSettingsAccentPressedColor}");
    }

    [Fact]
    public void ColorDragging_CoalescesSnapshotsAndLimitsOnlyTextLayoutWork()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("if (_isColorDragging && sections == SettingsSections.Visual) return", source, StringComparison.Ordinal);
        Assert.Contains("if (_colorPreviewSubscribed) return", source, StringComparison.Ordinal);
        Assert.Contains("if (!_isColorDragging) StopColorPreviewRendering()", source, StringComparison.Ordinal);
        Assert.Contains("now - _lastColorLabelUpdate >= Stopwatch.Frequency / 20", source, StringComparison.Ordinal);
        Assert.Contains("if (_previewColorBrush.Color != color) _previewColorBrush.Color = color", source, StringComparison.Ordinal);
        Assert.Contains("_presetColorIndices.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("UIElement.PointerCaptureLostEvent", source, StringComparison.Ordinal);
        Assert.Contains("if (_isColorDragging && _pendingSettingsSections != 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetButtons_MoveGlobalResetToApplyAndUseLocalizedPageLabels()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement reset = GetNamedElement(panel, "BtnResetSettings");
        XElement apply = GetNamedElement(panel, "BtnApplySettings");
        Assert.Same(reset.Parent, apply.Parent);
        Assert.Contains(apply, reset.ElementsAfterSelf());
        Assert.Equal("{StaticResource BasDangerButtonStyle}", (string?)reset.Attribute("Style"));
        Assert.Equal("OpenAllResetOverlay_Click", (string?)reset.Attribute("Click"));
        Assert.DoesNotContain("BtnResetAll", ReadSource("src", "ControlPanelWindow.xaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("ResetConfig_Click", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        foreach (string name in new[] { "BtnBasicReset", "BtnVisualReset", "BtnFilterReset", "BtnScreensReset" })
            Assert.Contains(name + ".Content = Localization.Get(\"Settings_ResetPage\")", source, StringComparison.Ordinal);
        foreach (string file in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            XDocument resources = LoadXaml("src", file);
            foreach (string key in new[] { "Settings_ResetPage", "Settings_Reset", "Overlay_AllReset", "Overlay_ScreenReset", "Overlay_SearchSettings", "Reset_DefaultValue", "Reset_DefaultProfile", "Msg_PageResetDone" })
            {
                XElement item = Assert.Single(resources.Descendants("data"), element => (string?)element.Attribute("name") == key);
                Assert.False(string.IsNullOrWhiteSpace(item.Element("value")?.Value));
            }
        }
        XElement label = Assert.Single(LoadXaml("src", "Strings.resx").Descendants("data"), item => (string?)item.Attribute("name") == "Settings_ResetPage");
        Assert.Equal("重置本页设置", label.Element("value")!.Value);
    }

    [Fact]
    public void ResetSelection_ChangesOnlySelectedItemsAndPreservesUnselectedPendingChanges()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string confirm = source[source.IndexOf("private async void ConfirmVisualReset_Click", StringComparison.Ordinal)..source.IndexOf("private void LinkedEffectScale_Changed", StringComparison.Ordinal)];
        Assert.Contains("VisualResetItems.Where(item => item.IsSelected)", confirm, StringComparison.Ordinal);
        Assert.Contains("foreach (VisualResetItem item in selected) item.Restore?.Invoke()", confirm, StringComparison.Ordinal);
        Assert.Contains("foreach (VisualResetItem item in selected) item.Save?.Invoke()", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_resetScope == ResetScope.Visual)", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSettings()", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeMessageBox", confirm, StringComparison.Ordinal);
        Assert.Contains("MarkResetSettingsSaved(selected)", confirm, StringComparison.Ordinal);
        Assert.Contains("visual[key] = currentVisual[key]?.DeepClone()", source, StringComparison.Ordinal);
        Assert.Contains("general[key] = currentGeneral[key]?.DeepClone()", source, StringComparison.Ordinal);
        Assert.Contains("_particleColor = \"76,167,255\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySettings_RemainsDisabledUntilSettingsDifferFromTheSavedBaseline()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal("False", (string?)GetNamedElement(panel, "BtnApplySettings").Attribute("IsEnabled"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("InitializeSettingsTracking()", source, StringComparison.Ordinal);
        Assert.Contains("_currentSettingsState != _savedSettingsState", source, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", source, StringComparison.Ordinal);
        Assert.Contains("Profiles.CollectionChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("CurrentProfileProcesses.CollectionChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("CaptureScreenSettings()", source, StringComparison.Ordinal);
        Assert.Contains("GetPendingSliderValue", source, StringComparison.Ordinal);
        Assert.Contains("CaptureSettingsBaseline();", source, StringComparison.Ordinal);
        Assert.Contains("UnregisterPropertyChangedCallback", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileSnapshots_KeepPendingEditsOutOfRuntimeSettings()
    {
        var field = typeof(ConfigManager).GetField("_profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        object previous = field.GetValue(null)!;
        var profile = new FilterProfile { Name = "Saved", Mode = ProcessFilterModeOption.Blacklist, Processes = ["saved.exe"] };
        try
        {
            field.SetValue(null, new List<FilterProfile> { profile });
            List<FilterProfile> pending = ConfigManager.GetProfiles();
            Assert.NotSame(profile, pending[0]);
            Assert.NotSame(profile.Processes, pending[0].Processes);
            pending[0].Name = "Pending";
            pending[0].Mode = ProcessFilterModeOption.Whitelist;
            pending[0].Processes.Clear();
            Assert.Equal("Saved", profile.Name);
            Assert.Equal(ProcessFilterModeOption.Blacklist, profile.Mode);
            Assert.Equal(new[] { "saved.exe" }, profile.Processes);
        }
        finally
        {
            field.SetValue(null, previous);
        }
    }

    [Fact]
    public void ResetPersistence_UsesSavedValuesWithoutLeakingPendingProfilesOrColors()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("private string _particleColor = ConfigManager.ParticleColor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigManager.ParticleColor =", source, StringComparison.Ordinal);
        Assert.Contains("SaveResetProfileProperty(profileId, resetProcesses: true)", source, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Deserialize<List<ScreenSelectionState>>(_savedSettingsState!.Screens)", source, StringComparison.Ordinal);
        Assert.Contains("if (!ConfigManager.Save(key, value)) throw", source, StringComparison.Ordinal);
        string config = ReadSource("src", "ConfigManager.cs");
        Assert.Contains("Processes = new List<string>(profile.Processes)", config, StringComparison.Ordinal);
        Assert.Contains("public static bool Save(string name, object value)", config, StringComparison.Ordinal);
        Assert.Contains("public static bool SaveProfiles", config, StringComparison.Ordinal);
        XDocument resources = LoadXaml("src", "Strings.resx");
        Assert.Equal("所选设置项已恢复为默认值并已保存。", resources.Descendants("data").Single(item => (string?)item.Attribute("name") == "Msg_PageResetDone").Element("value")!.Value);
    }

    [Fact]
    public void RendererStartup_ReappliesSavedRuntimeSettingsAfterNavigationAndReadiness()
    {
        string source = ReadSource("src", "OverlayWindow.cs");
        Assert.Equal(3, source.Split("ApplySavedRendererSettings();", StringSplitOptions.None).Length - 1);
        string sync = source[source.IndexOf("private void ApplySavedRendererSettings()", StringComparison.Ordinal)..source.IndexOf("private void OnWebMessageReceived", StringComparison.Ordinal)];
        foreach (string setting in new[] { "ConfigManager.ParticleColor", "ConfigManager.EffectOpacity", "ConfigManager.GlowIntensity", "SetCurveDraw(ConfigManager.ApplyCurveDraw)", "ConfigManager.ScreenshotCompatibilityMode", "ConfigManager.IsTouchscreenMode ? InputModeTouch : InputModeMouse" })
            Assert.Contains(setting, sync, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelRenderClock_UsesBalancedWin10PrecisionRequestsWithoutFixedFrameRateTimers()
    {
        string source = ReadSource("src", "DcompPanelHost.cs");
        Assert.Contains("OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)", source, StringComparison.Ordinal);
        Assert.Contains("TimeBeginPeriod(1) == 0", source, StringComparison.Ordinal);
        Assert.Contains("TimeEndPeriod(1)", source, StringComparison.Ordinal);
        Assert.Contains("SetRenderClockActive(IsWindowVisible(_hwnd) && !IsIconic(_hwnd))", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split("SetRenderClockActive(false);", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InWindowMessages_UseNativeContentDialogsBoundToTheCurrentXamlRootAndTheme()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.DoesNotContain("NativeMessageBox", source, StringComparison.Ordinal);
        string dialog = source[source.IndexOf("private async Task ShowMessageAsync", StringComparison.Ordinal)..source.IndexOf("private async Task<bool> ConfirmAsync", StringComparison.Ordinal)];
        Assert.Contains("_messageDialog = new ContentDialog", dialog, StringComparison.Ordinal);
        Assert.Contains("XamlRoot = root", dialog, StringComparison.Ordinal);
        Assert.Contains("RequestedTheme = RootGrid.ActualTheme", dialog, StringComparison.Ordinal);
        Assert.Contains("await _messageDialog.ShowAsync()", dialog, StringComparison.Ordinal);
        Assert.Contains("_dialogGate.WaitAsync()", dialog, StringComparison.Ordinal);
        Assert.Contains("await ShowMessageAsync(Localization.Get(_resetScope == ResetScope.Visual ? \"Msg_VisualResetDone\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessActions_KeepOnlyTwoButtonsOnTheSameRowWithoutManualEntry()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement browse = GetNamedElement(panel, "BtnBrowseProcess");
        XElement running = GetNamedElement(panel, "BtnSelectRunningProcess");
        Assert.Same(browse.Parent, running.Parent);
        Assert.Equal("Grid", browse.Parent!.Name.LocalName);
        Assert.Equal("1", (string?)running.Attribute("Grid.Column"));
        Assert.Equal(2, browse.Parent.Elements().Count(element => element.Name.LocalName == "Button"));
        Assert.DoesNotContain(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Name") is "ManualProcessInput" or "BtnAddProcess" or "TxtVisualInputHint");
        Assert.DoesNotContain("AddManualProcess_Click", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentedSelection_UsesCompositionAnimationAndPhysicalPixelSymmetricInsets()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement selection = GetNamedElement(styles, "SegmentedSelection");
        Assert.Equal("Border", selection.Name.LocalName);
        Assert.Equal("False", (string?)selection.Attribute("IsHitTestVisible"));
        XElement style = Assert.Single(selection.Ancestors(), element => element.Name.LocalName == "Style");
        Assert.Equal("BasSegmentedRadioButtonsStyle", (string?)style.Attribute(Xaml + "Key"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("SetIsTranslationEnabled(_selection, true)", source, StringComparison.Ordinal);
        Assert.Contains("_translation.StartAnimation(\"Translation.X\", animation)", source, StringComparison.Ordinal);
        Assert.Contains("Math.Round(2 * scale, MidpointRounding.AwayFromZero) / scale", source, StringComparison.Ordinal);
        Assert.Contains("frame.Padding = inset", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var selector in _segmentedSelectors) selector.Dispose()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorExpander_ChevronTracksEveryNativeToggleState()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement header = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorExpanderHeaderStyle");
        XElement states = Assert.Single(header.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "CommonStates");
        foreach (string name in new[] { "Normal", "PointerOver", "Pressed", "Disabled", "Checked", "CheckedPointerOver", "CheckedPressed", "CheckedDisabled" })
        {
            XElement state = Assert.Single(states.Elements(), element => (string?)element.Attribute(Xaml + "Name") == name);
            XElement glyph = Assert.Single(state.Descendants(), element => (string?)element.Attribute("Target") == "HeaderChevron.Glyph");
            Assert.Equal(name.StartsWith("Checked", StringComparison.Ordinal) ? "\uE70E" : "\uE70D", (string?)glyph.Attribute("Value"));
        }
        Assert.DoesNotContain(header.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "CheckStates");
    }

    [Fact]
    public void ColorExpander_LayoutChangesDoNotCancelExpandOrCollapseAnimation()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("_colorCardGeometry != null && !_colorCardAnimating", source, StringComparison.Ordinal);
        string animate = source[source.IndexOf("private void AnimateColorCard(bool expanding)", StringComparison.Ordinal)..source.IndexOf("private void EffectColorPicker_Loaded", StringComparison.Ordinal)];
        Assert.True(animate.IndexOf("_colorCardAnimating = true", StringComparison.Ordinal) < animate.IndexOf("_colorContentHost.Height = contentHeight", StringComparison.Ordinal));
        Assert.Contains("animation.InsertExpressionKeyFrame(0, \"this.StartingValue\")", animate, StringComparison.Ordinal);
        Assert.Contains("_colorCardGeometry.StartAnimation(\"Size.Y\", animation)", animate, StringComparison.Ordinal);
        Assert.Contains("generation != _colorCardAnimationGeneration", animate, StringComparison.Ordinal);
        Assert.True(animate.IndexOf("EffectColorCard.UpdateLayout()", StringComparison.Ordinal) < animate.IndexOf("_colorCardGeometry.StartAnimation", StringComparison.Ordinal));
        Assert.Contains("float targetHeight = expanding ? (float)EffectColorCard.ActualHeight : 60", animate, StringComparison.Ordinal);
        string loaded = source[source.IndexOf("private void EffectColorExpander_Loaded", StringComparison.Ordinal)..source.IndexOf("private void EffectColorCard_SizeChanged", StringComparison.Ordinal)];
        Assert.Contains("_colorCardGeometry ??=", loaded, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateColorCard", loaded, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleLineInputs_CenterTextSuppressClearButtonsAndCommitOnOutsideClicks()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("content.VerticalContentAlignment = VerticalAlignment.Center", source, StringComparison.Ordinal);
        Assert.Contains("content.VerticalAlignment = VerticalAlignment.Center", source, StringComparison.Ordinal);
        Assert.Contains("placeholder.VerticalAlignment = VerticalAlignment.Center", source, StringComparison.Ordinal);
        Assert.Contains("clearButton.MaxWidth = 0", source, StringComparison.Ordinal);
        Assert.Contains("RootGrid.AddHandler(UIElement.PointerPressedEvent", source, StringComparison.Ordinal);
        Assert.Contains("FocusManager.GetFocusedElement(XamlRoot)", source, StringComparison.Ordinal);
        Assert.Contains("Focus(FocusState.Programmatic)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelHost_RestoresMaximizedClientBoundsUsingTheProposedRectangleMonitor()
    {
        string source = ReadSource("src", "DcompPanelHost.cs");
        string calculate = source[source.IndexOf("case WmNcCalcSize:", StringComparison.Ordinal)..source.IndexOf("case WmGetMinMaxInfo:", StringComparison.Ordinal)];
        Assert.Contains("RECT proposed = Marshal.PtrToStructure<RECT>(lParam)", calculate, StringComparison.Ordinal);
        Assert.Contains("MonitorFromRect(ref proposed, MonitorDefaultToNearest)", calculate, StringComparison.Ordinal);
        Assert.Contains("Marshal.StructureToPtr(info.rcWork, lParam", calculate, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitorFromWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWindowRect", calculate, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentFilter_DisablesProfileActionsAlongWithTheNativeSelectors()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string interlock = source[source.IndexOf("private void UpdateEnvironmentFilterInterlock()", StringComparison.Ordinal)..source.IndexOf("private void SelectProcessFilterMode", StringComparison.Ordinal)];
        foreach (string name in new[] { "CheckHideInFullscreen", "CheckShowEffectOnDesktop", "ComboProfiles", "ComboProcessFilterMode", "BtnAddProfile", "BtnRenameProfile", "BtnDeleteProfile" })
            Assert.Contains($"{name}.IsEnabled = environmentFilterEnabled", interlock, StringComparison.Ordinal);
        foreach (string name in new[] { "ListConfiguredProcesses", "BtnBrowseProcess", "BtnSelectRunningProcess" })
            Assert.Contains($"{name}.IsEnabled = processFilterEnabled", interlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileDeletion_UsesATitlelessNativeConfirmationWithCancelOnTheLeftAndAGap()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string delete = source[source.IndexOf("private async void DeleteProfile_Click", StringComparison.Ordinal)..source.IndexOf("private void RemoveProcess_Click", StringComparison.Ordinal)];
        Assert.Contains("""title: null, confirmText: Localization.Get("Msg_ConfirmDelete_Title")""", delete, StringComparison.Ordinal);
        Assert.Contains("""PrimaryButtonText = confirmText ?? Localization.Get("ColorPicker_Confirm")""", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(cancel, 0)", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(confirm, 4)", source, StringComparison.Ordinal);
        Assert.Contains("commands.ColumnDefinitions[3].Width = new GridLength(8)", source, StringComparison.Ordinal);
        Assert.Contains("return await dialog.ShowAsync() == ContentDialogResult.Primary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LastProfileDeletion_ShowsTheNativeMessageWithoutAnInformationHeading()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string delete = source[source.IndexOf("private async void DeleteProfile_Click", StringComparison.Ordinal)..source.IndexOf("private void RemoveProcess_Click", StringComparison.Ordinal)];
        Assert.Contains("""await ShowMessageAsync(Localization.Get("Msg_KeepOneProfile"), string.Empty);""", delete, StringComparison.Ordinal);
        Assert.Contains("Title = title == string.Empty ? null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Msg_Info", delete, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileRename_KeepsOnlyTheTitleInputAndActionButtons()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement overlay = GetNamedElement(panel, "RenameProfileOverlay");
        Assert.Single(overlay.Descendants(Presentation + "TextBlock"));
        Assert.Same(GetNamedElement(panel, "TxtOverlayRename"), overlay.Descendants(Presentation + "TextBlock").Single());
        Assert.Equal("0,12,0,0", (string?)GetNamedElement(panel, "NewProfileNameInput").Attribute("Margin"));
        Assert.DoesNotContain("TxtOverlayRenamePrompt", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void LogCard_UsesSelectableMonospaceDisplayTextInsteadOfAnInputTemplate()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement log = GetNamedElement(panel, "TxtAppLog");
        Assert.Equal(Presentation + "TextBlock", log.Name);
        Assert.Equal("True", (string?)log.Attribute("IsTextSelectionEnabled"));
        Assert.Equal("Consolas", (string?)log.Attribute("FontFamily"));
        Assert.Equal("Wrap", (string?)log.Attribute("TextWrapping"));
        Assert.Same(GetNamedElement(panel, "LogScrollViewer"), log.Parent);
        Assert.DoesNotContain(GetNamedElement(panel, "PageLog").Descendants(), element => element.Name == Presentation + "TextBox");
    }

    private static XElement GetSettingCard(XElement control) =>
        Assert.Single(control.Ancestors(), element => (string?)element.Attribute("Style") == "{StaticResource BasSettingCardStyle}");

    private static void AssertSettingHint(XElement control, XElement hint)
    {
        XElement card = GetSettingCard(control);
        Assert.Same(card, GetSettingCard(hint));
        XElement layout = Assert.Single(card.Elements());
        XElement text = Assert.Single(layout.Elements(), element => (string?)element.Attribute("Grid.Column") == "0");
        Assert.Same(text, hint.Parent);
        Assert.Equal("4", (string?)text.Attribute("Spacing"));
        Assert.Equal(2, text.Elements().Count());
        Assert.Same(hint, text.Elements().Last());
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
