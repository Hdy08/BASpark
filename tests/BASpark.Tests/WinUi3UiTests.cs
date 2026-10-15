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
    private sealed class MultiTouchScope : IDisposable
    {
        private readonly int _previous = ConfigManager.MaxTouchEffects;
        internal MultiTouchScope(bool enabled) : this(enabled ? 0 : 1) { }
        internal MultiTouchScope(int maximum) => ConfigManager.MaxTouchEffects = maximum;
        public void Dispose() => ConfigManager.MaxTouchEffects = _previous;
    }
    private sealed class SystemGestureCursorScope : IDisposable
    {
        private readonly IntPtr _module = LoadLibraryEx(Path.Combine(Environment.SystemDirectory, "NInput.dll"), IntPtr.Zero, 0x800);
        internal readonly IntPtr Handle;
        internal SystemGestureCursorScope()
        {
            Assert.NotEqual(IntPtr.Zero, _module);
            Handle = LoadCursor(_module, new IntPtr(1));
            Assert.NotEqual(IntPtr.Zero, Handle);
        }
        public void Dispose() => FreeLibrary(_module);
    }
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CreateCursor(IntPtr instance, int hotSpotX, int hotSpotY, int width, int height, byte[] andMask, byte[] xorMask);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr cursor);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr LoadCursor(IntPtr module, IntPtr resource);

    [Fact]
    public void PauseActions_ShareOneRuntimeStateWithoutChangingSavedEffectPreferences()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement action = GetNamedElement(panel, "BtnToggleEffectsPause");
        Assert.Equal("ToggleButton", action.Name.LocalName);
        Assert.Equal("Right", (string?)action.Attribute("HorizontalAlignment"));
        XElement actions = action.Parent!;
        Assert.Same(GetNamedElement(panel, "BtnRestartApplication").Parent, actions);
        Assert.Same(action, actions.Elements().Last());
        Assert.Equal("1", (string?)actions.Attribute("Grid.Column"));
        Assert.Same(GetNamedElement(panel, "TxtWelcomeTitle").Parent, actions.Parent);
        Assert.DoesNotContain(action, GetNamedElement(panel, "StatsHeader").Descendants());
        string application = ReadSource("src", "App.xaml.cs");
        string toggle = application[application.IndexOf("public static void ToggleEffectsPaused()", StringComparison.Ordinal)..application.IndexOf("private ControlPanelWindow?", StringComparison.Ordinal)];
        Assert.Contains("Overlay?.RefreshPauseState()", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigManager.Save", toggle, StringComparison.Ordinal);
        string tray = ReadSource("src", "TrayIconController.cs");
        Assert.True(tray.IndexOf("Localization.Get(App.IsEffectsPaused", StringComparison.Ordinal) < tray.IndexOf("Localization.Get(\"Tray_Restart\")", StringComparison.Ordinal));
        Assert.Contains("4 => App.ToggleEffectsPaused", tray, StringComparison.Ordinal);
        Assert.Contains("App.IsEffectsPaused || _overlays.Count == 0", ReadSource("src", "OverlayManager.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryLinks_UseNativeHyperlinkButtonsWithoutTextDecorations()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal(3, GetNamedElement(panel, "PageAbout").Descendants(Presentation + "HyperlinkButton").Count());
        Assert.DoesNotContain(panel.Descendants(), element => element.Name == Presentation + "Hyperlink");
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasRepositoryLinkStyle");
        Assert.Equal("{StaticResource DefaultHyperlinkButtonStyle}", (string?)style.Attribute("BasedOn"));
        Assert.DoesNotContain(style.Elements(), setter => (string?)setter.Attribute("Property") is "Padding" or "Template" or "CornerRadius" or "FontWeight" or "FontSize");
        Assert.Equal("None", (string?)Assert.Single(style.Descendants(Presentation + "TextBlock")).Attribute("TextDecorations"));
    }

    [Fact]
    public void NumericInputs_KeepTwoDecimalPlacesAndAnIntegerSamplingRateWithTenUnitSteps()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal("10", (string?)GetNamedElement(panel, "SliderTrailSamplingRate").Attribute("StepFrequency"));
        Assert.Equal("10", (string?)GetNamedElement(panel, "TxtTrailSamplingRateValue").Attribute("SmallChange"));
        foreach (XElement input in panel.Descendants(Presentation + "NumberBox").Where(element =>
                     (string?)element.Attribute(Xaml + "Name") is not ("TxtTrailSamplingRateValue" or "TxtMaxTouchEffectsValue")))
            Assert.Equal("0.1", (string?)input.Attribute("SmallChange"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("FractionDigits = precision", source, StringComparison.Ordinal);
        Assert.Contains("GetSliderPrecision(slider)", source, StringComparison.Ordinal);
        Assert.Contains("slider.StepFrequency >= 1 ? 0 : 2", ReadSource("src", "ControlPanelWindow.Advanced.cs"), StringComparison.Ordinal);
        Assert.Contains("IncrementNumberRounder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeInfoBars_ShowNativeIconsAndRemoveTheUnusedHeaderRow()
    {
        XDocument welcome = LoadXaml("src", "PrivacyWindow.xaml");
        foreach (XElement info in welcome.Descendants(Presentation + "InfoBar"))
        {
            Assert.Equal("WelcomeInfoBar_Loaded", (string?)info.Attribute("Loaded"));
            Assert.Equal("0,8,16,8", (string?)info.Element(Presentation + "StackPanel")!.Attribute("Margin"));
        }
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasStartupInfoBarStyle");
        Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "IsIconVisible" && (string?)setter.Attribute("Value") == "True");
        string source = ReadSource("src", "PrivacyWindow.xaml.cs");
        Assert.Contains("StartupDialogHost.AlignInfoBarContent((InfoBar)sender)", source, StringComparison.Ordinal);
        string alignment = ReadSource("src", "StartupDialogHost.cs");
        Assert.Contains("Grid.SetRow(content, 0)", alignment, StringComparison.Ordinal);
        Assert.Contains("panel.Visibility = Visibility.Collapsed", alignment, StringComparison.Ordinal);
    }

    [Fact]
    public void UiFonts_UseNativeWinUiTypographyWhileLogsKeepConsolas()
    {
        XDocument application = LoadXaml("src", "App.xaml");
        Assert.DoesNotContain(application.Descendants(), element => element.Name.LocalName == "FontFamily");
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        foreach ((string key, string nativeStyle) in new[]
        {
            ("BasPageTitleStyle", "TitleTextBlockStyle"),
            ("BasSettingsSectionTitleStyle", "SubtitleTextBlockStyle"),
            ("BasPageSubtitleStyle", "BodyTextBlockStyle"),
            ("BasFieldLabelStyle", "BodyTextBlockStyle"),
            ("BasCaptionStyle", "CaptionTextBlockStyle"),
            ("BasSectionTitleStyle", "SubtitleTextBlockStyle"),
            ("BasDialogMessageStyle", "BodyStrongTextBlockStyle"),
            ("BasSettingTitleStyle", "BodyTextBlockStyle")
        })
        {
            XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == key);
            Assert.Equal("{StaticResource " + nativeStyle + "}", (string?)style.Attribute("BasedOn"));
            Assert.DoesNotContain(style.Elements(), setter => (string?)setter.Attribute("Property") is "FontFamily" or "FontSize" or "FontWeight" or "LineHeight" or "LineStackingStrategy" or "TextLineBounds");
        }
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal("Consolas", (string?)GetNamedElement(panel, "TxtAppLog").Attribute("FontFamily"));
        Assert.DoesNotContain(application.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "SymbolThemeFontFamily");
    }

    [Fact]
    public void SelectionHeading_UsesEqualVerticalSpacingWithoutPageHeadingMargins()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement template = Assert.Single(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "SelectionHeadingTemplate");
        XElement header = Assert.Single(template.Elements());
        XElement title = Assert.Single(header.Elements(), item => item.Name.LocalName == "TextBlock");
        Assert.Equal("{StaticResource BasSectionTitleStyle}", (string?)title.Attribute("Style"));
        Assert.Equal("{StaticResource BasSectionHeaderStyle}", (string?)header.Attribute("Style"));
        Assert.Equal("0,-4,16,0", (string?)header.Attribute("Margin"));
        string source = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("Margin = new Thickness(0, -4, 16, 0)", source, StringComparison.Ordinal);
        Assert.Contains("header.Margin = group.IsFirst ? new Thickness() : new Thickness(0, -4, 16, 0)", source, StringComparison.Ordinal);
        Assert.Contains("header.Height = group.IsFirst ? 0 : 48", source, StringComparison.Ordinal);
        Assert.Equal(2, header.Elements().Count(item => item.Name.LocalName == "Button"));
    }

    [Theory]
    [InlineData("Off", BackgroundColorOption.Light)]
    [InlineData("off", BackgroundColorOption.Light)]
    [InlineData("On", BackgroundColorOption.Dark)]
    [InlineData("ON", BackgroundColorOption.Dark)]
    [InlineData("System", BackgroundColorOption.System)]
    [InlineData("unknown", BackgroundColorOption.System)]
    [InlineData(null, BackgroundColorOption.System)]
    public void ParseBackgroundColor_UsesStableThreeStateValues(string? raw, BackgroundColorOption expected)
    {
        Assert.Equal(expected, ConfigManager.ParseBackgroundColor(raw));
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
            "ItemsControl",
            "ItemsRepeater",
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

        int saveIndex = source.IndexOf("ConfigManager.SaveBackupValues(values)", StringComparison.Ordinal);
        Assert.True(saveIndex >= 0, "SaveSettings must persist DarkMode.");

        int applyIndex = source.IndexOf("ApplyPageRuntimeSettings(page", saveIndex, StringComparison.Ordinal);
        Assert.True(applyIndex > saveIndex, "DarkMode must be applied after it is persisted.");
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("if (page == SettingsSections.Basic)", pages, StringComparison.Ordinal);
        Assert.Contains("ApplyDarkMode();", pages, StringComparison.Ordinal);
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

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int NativeFormatDouble(IntPtr formatter, double value, out IntPtr text);

    [Theory]
    [InlineData(false, 11)]
    [InlineData(true, 0)]
    [InlineData(true, 1010)]
    public void SettingEndpoints_NativeNumberFormatterCallsReturnTheSpecialLabels(bool samplingRate, double position)
    {
        var formatter = new SettingNumberFormatter(samplingRate);
        IntPtr pointer = WinRT.MarshalInterface<Windows.Globalization.NumberFormatting.INumberFormatter2>.FromManaged(formatter);
        IntPtr text = IntPtr.Zero;
        try
        {
            IntPtr vtable = System.Runtime.InteropServices.Marshal.ReadIntPtr(pointer);
            IntPtr method = System.Runtime.InteropServices.Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
            NativeFormatDouble format = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<NativeFormatDouble>(method);
            Assert.Equal(0, format(pointer, position, out text));
            Assert.Equal(formatter.FormatDouble(position), WinRT.MarshalString.FromAbi(text));
        }
        finally
        {
            WinRT.MarshalString.DisposeAbi(text);
            WinRT.MarshalInterface<Windows.Globalization.NumberFormatting.INumberFormatter2>.DisposeAbi(pointer);
        }
    }

    [Theory]
    [InlineData(false, 0, 11)]
    [InlineData(false, 1, 1)]
    [InlineData(false, 5, 5)]
    [InlineData(false, 10, 10)]
    [InlineData(true, -1, 0)]
    [InlineData(true, 0, 1010)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 40, 40)]
    [InlineData(true, 999, 999)]
    [InlineData(true, 1000, 1000)]
    public void SettingEndpoints_RoundTripStorageTextAndSliderPositions(bool samplingRate, int stored, int position)
    {
        var formatter = new SettingNumberFormatter(samplingRate);
        Assert.Equal(position, formatter.ToSliderValue(stored));
        Assert.Equal(stored, formatter.ToStoredValue(position));
        Assert.Equal(position, formatter.ParseDouble(formatter.FormatDouble(position)));
        Assert.Equal(position, formatter.ParseInt(formatter.FormatInt(position)));
        Assert.Equal((ulong)position, formatter.ParseUInt(formatter.FormatUInt((ulong)position)));
        string text = formatter.FormatDouble(position);
        Assert.Equal(text, formatter.Convert(position, typeof(string), null!, Localization.CurrentCultureName));
        Assert.Equal((double)position, formatter.ConvertBack(text, typeof(double), null!, Localization.CurrentCultureName));
    }

    [Theory]
    [InlineData("zh-CN", "显示器", "不限制", "特效数量上限", "拖尾采样率")]
    [InlineData("en", "Display", "Unlimited", "Effect count limit", "Trail sampling rate")]
    [InlineData("ja", "ディスプレイ", "無制限", "エフェクト数の上限", "トレイルサンプリングレート")]
    public void SettingEndpoints_TranslateBothSpecialValuesAndAssociatedTitles(string language, string display, string unlimited,
        string countTitle, string samplingTitle)
    {
        string previous = Localization.CurrentCultureName;
        try
        {
            Localization.ApplyCulture(language);
            var sampling = new SettingNumberFormatter(true);
            var count = new SettingNumberFormatter(false);
            Assert.Equal(display, sampling.FormatDouble(0));
            Assert.Equal(unlimited, sampling.FormatDouble(1010));
            Assert.Equal(unlimited, count.FormatDouble(11));
            Assert.Equal(0, sampling.ParseDouble(display));
            Assert.Equal(1010, sampling.ParseDouble(unlimited));
            Assert.Equal(11, count.ParseDouble(unlimited));
            Assert.Equal(countTitle, Localization.Get("Basic_MaxTouchEffects"));
            Assert.Equal(samplingTitle, Localization.Get("Visual_TrailSamplingRate"));
            Assert.DoesNotContain("拖尾刷新率", Localization.Get("Visual_LinkedSpeedHint"), StringComparison.Ordinal);
        }
        finally
        {
            Localization.ApplyCulture(previous);
        }
    }

    [Theory]
    [InlineData(false, "0", 11)]
    [InlineData(false, "1", 1)]
    [InlineData(false, "10", 10)]
    [InlineData(false, "11", null)]
    [InlineData(false, "-1", null)]
    [InlineData(false, "1.5", null)]
    [InlineData(true, "0", 0)]
    [InlineData(true, "1", 1)]
    [InlineData(true, "1000", 1000)]
    [InlineData(true, "1001", null)]
    [InlineData(true, "1010", null)]
    [InlineData(true, "-1", null)]
    [InlineData(true, "2.5", null)]
    [InlineData(true, "NaN", null)]
    [InlineData(true, "Infinity", null)]
    [InlineData(true, "", null)]
    public void SettingEndpoints_InputsAcceptOnlyIntegersInTheVisibleRange(bool samplingRate, string text, int? expected)
    {
        Assert.Equal((double?)expected, new SettingNumberFormatter(samplingRate).ParseDouble(text));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(400)]
    [InlineData(1000)]
    public void TrailSamplingRate_AppliesToEveryOverlayAndUnlimitedDisablesInputThrottling(int rate)
    {
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var manager = new OverlayManager();
        var overlays = (Dictionary<string, OverlayWindow>)typeof(OverlayManager).GetField("_overlays", members)!.GetValue(manager)!;
        overlays.Add("first-invalid-display", (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow)));
        overlays.Add("second-invalid-display", (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow)));
        try
        {
            manager.UpdateInputSamplingRate(rate);
            int effective = rate == -1 ? 60 : rate;
            long expected = effective == 0 ? 0 : Math.Max(1, System.Diagnostics.Stopwatch.Frequency / effective);
            Assert.All(overlays.Values, overlay => Assert.Equal(expected, overlay.TrailMoveIntervalTimestamp));
            Assert.Equal(expected, typeof(OverlayManager).GetField("_idleMoveIntervalTimestamp", members)!.GetValue(manager));
            Assert.Equal(0L, typeof(OverlayManager).GetField("_lastMoveTimestamp", members)!.GetValue(manager));
            manager.UpdateInputSamplingRate(rate == 0 ? 1000 : 0);
            Assert.All(overlays.Values, overlay => Assert.Equal(rate == 0 ? Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 1000) : 0,
                overlay.TrailMoveIntervalTimestamp));
        }
        finally
        {
            overlays.Clear();
        }
    }

    [Fact]
    public void TrailSamplingRate_CombinesDisplayManualAndUnlimitedModesInOneNativeCard()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement slider = GetNamedElement(document, "SliderTrailSamplingRate");
        XElement numberBox = GetNamedElement(document, "TxtTrailSamplingRateValue");
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "CheckFollowDisplayRefreshRate");
        Assert.Equal("0", (string?)slider.Attribute("Minimum"));
        Assert.Equal("1010", (string?)slider.Attribute("Maximum"));
        Assert.Equal("0", (string?)numberBox.Attribute("Minimum"));
        Assert.Equal("1010", (string?)numberBox.Attribute("Maximum"));
        Assert.Equal("Auto", (string?)numberBox.Attribute("Width"));
        Assert.Equal("84", (string?)numberBox.Attribute("MinWidth"));

        string configSource = ReadSource("src", "ConfigManager.cs");
        string overlaySource = ReadSource("src", "OverlayManager.cs");
        Assert.Contains("InputSamplingRate { get; set; } = -1", configSource, StringComparison.Ordinal);
        Assert.Contains("ScreenIdentity.GetRefreshRate(pair.Key)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("RouteTrailMove(target, cursorX, cursorY", overlaySource, StringComparison.Ordinal);
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
        XElement trailSamplingText = GetNamedElement(document, "TxtTrailSamplingRate");
        XElement effectColor = GetNamedElement(document, "TxtEffectColor");
        List<XElement> children = GetNamedElement(document, "SectionVisual").Descendants().ToList();
        Assert.True(children.IndexOf(trailSamplingText) < children.IndexOf(effectColor));
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
            "BtnApplySettings", "BtnClearLog",
            "BtnResetSettings", "BtnRefreshScreens", "BtnAddProfile", "BtnRenameProfile", "BtnDeleteProfile",
            "BtnRepoDoomVoss", "BtnRepoCialloKing", "BtnRepoWinUI",
        ];

        XDocument panel = XDocument.Parse(xaml);
        foreach (string name in mustBeFilledWithContent)
        {
            GetNamedElement(panel, name);
            Assert.Contains("SetActionIcon(" + name + ",", source, StringComparison.Ordinal);
        }

        foreach (string name in new[] { "BtnBasicReset", "BtnVisualReset", "BtnFilterReset" })
        {
            XElement button = GetNamedElement(panel, name);
            Assert.Null(button.Attribute("Content"));
            Assert.Equal("\uE777", (string?)Assert.Single(button.Elements(Presentation + "FontIcon")).Attribute("Glyph"));
        }
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("string label = Localization.Get(\"Settings_ResetPage\")", pages, StringComparison.Ordinal);
        Assert.Contains("SetResetButtonLabel(button, label)", pages, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(button, label)", pages, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(button, name)", pages, StringComparison.Ordinal);

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
    public void OverlayHost_IsNotAnInteractiveWindowSelectionTargetAndDoesNotProtectCapture()
    {
        using var host = new LayeredWindowHost();
        host.SetBounds(120, 120, 200, 120);
        host.Show();

        Assert.True(host.IsVisible);
        Assert.False(IsWindowEnabled(host.Handle));
        Assert.Equal(new IntPtr(-1), SendWindowMessage(host.Handle, 0x0084, IntPtr.Zero, IntPtr.Zero));
        Assert.Equal(new IntPtr(3), SendWindowMessage(host.Handle, 0x0021, IntPtr.Zero, IntPtr.Zero));
        IntPtr owner = GetWindow(host.Handle, 4);
        Assert.NotEqual(IntPtr.Zero, owner);
        Assert.False(NativeMethods.IsWindowVisible(owner));
        Assert.True(GetWindowDisplayAffinity(host.Handle, out uint affinity));
        Assert.Equal(0u, affinity);

        host.Hide();
        Assert.False(host.IsVisible);
        host.Dispose();
        Assert.False(NativeMethods.IsWindow(owner));
        Assert.Equal(IntPtr.Zero, host.Handle);
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
    public void RendererReadyTimeout_UsesNativeMessagesAndOneShotFallback()
    {
        string overlaySource = ReadSource("src", "OverlayWindow.cs");

        Assert.DoesNotContain("PollRendererReadyAsync", overlaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("RendererProbe", overlaySource, StringComparison.Ordinal);
        Assert.Contains("_webMessageReceivedHandler = (_, args) => OnWebMessageReceived(coreWebView, args)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("RendererReadyTimeout = TimeSpan.FromSeconds(2)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("_rendererReadyTimeoutTimer.IsRepeating = false", overlaySource, StringComparison.Ordinal);
        Assert.Contains("FallbackToLegacyRenderer(\"ready timeout\")", overlaySource, StringComparison.Ordinal);
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

        Assert.Contains("ControllerAttachAttempts", overlaySource, StringComparison.Ordinal);
        Assert.Contains("attempt <= ControllerAttachAttempts && !attached", overlaySource, StringComparison.Ordinal);
        Assert.Contains("if (resetRequested)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("if (_isClosing)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("App.ReportFatalWebViewFailure", overlaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void TouchscreenInput_ReinstallsAndReleasesTheSourceAwareMouseHook()
    {
        bool previousCompatibility = ConfigManager.ScreenshotCompatibilityMode;
        bool previousFilter = ConfigManager.EnableEnvironmentFilter;
        bool previousTouchMode = ConfigManager.IsTouchscreenMode;
        using var manager = new OverlayManager();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var setup = typeof(OverlayManager).GetMethod("SetupGlobalHooks", members)!.CreateDelegate<Action>(manager);
        var hook = typeof(OverlayManager).GetField("_mouseHook", members)!;
        var touchListener = typeof(OverlayManager).GetField("_touchInputListener", members)!;
        var cursorHook = typeof(TouchInputListener).GetField("_cursorWinEventHook", members)!;
        var cursorNameHook = typeof(TouchInputListener).GetField("_cursorNameEventHook", members)!;
        try
        {
            ConfigManager.ScreenshotCompatibilityMode = false;
            ConfigManager.EnableEnvironmentFilter = false;
            ConfigManager.IsTouchscreenMode = true;
            setup();
            Assert.NotEqual(IntPtr.Zero, (IntPtr)hook.GetValue(manager)!);
            var firstListener = Assert.IsType<TouchInputListener>(touchListener.GetValue(manager));
            Assert.NotEqual(IntPtr.Zero, firstListener.Handle);
            Assert.NotEqual(IntPtr.Zero, (IntPtr)cursorHook.GetValue(firstListener)!);
            Assert.NotEqual(IntPtr.Zero, (IntPtr)cursorNameHook.GetValue(firstListener)!);
            setup();
            Assert.NotEqual(IntPtr.Zero, (IntPtr)hook.GetValue(manager)!);
            Assert.Equal(IntPtr.Zero, firstListener.Handle);
            Assert.Equal(IntPtr.Zero, (IntPtr)cursorHook.GetValue(firstListener)!);
            Assert.Equal(IntPtr.Zero, (IntPtr)cursorNameHook.GetValue(firstListener)!);
            manager.UpdateTouchMode(false);
            Assert.Null(touchListener.GetValue(manager));
            manager.UpdateTouchMode(true);
            var lastListener = Assert.IsType<TouchInputListener>(touchListener.GetValue(manager));
            Assert.NotEqual(IntPtr.Zero, lastListener.Handle);
            manager.Dispose();
            Assert.Equal(IntPtr.Zero, (IntPtr)hook.GetValue(manager)!);
            Assert.Equal(IntPtr.Zero, lastListener.Handle);
            Assert.Equal(IntPtr.Zero, (IntPtr)cursorHook.GetValue(lastListener)!);
            Assert.Equal(IntPtr.Zero, (IntPtr)cursorNameHook.GetValue(lastListener)!);
        }
        finally
        {
            ConfigManager.ScreenshotCompatibilityMode = previousCompatibility;
            ConfigManager.EnableEnvironmentFilter = previousFilter;
            ConfigManager.IsTouchscreenMode = previousTouchMode;
        }
    }

    [Theory]
    [InlineData(0UL, false)]
    [InlineData(1UL, false)]
    [InlineData(0xFF515700UL, false)]
    [InlineData(0xFF51577FUL, false)]
    [InlineData(0xFF515780UL, true)]
    [InlineData(0xFF515781UL, true)]
    [InlineData(0xFF5157FFUL, true)]
    [InlineData(0xFF515680UL, false)]
    [InlineData(0xFFFFFFFFFF515780UL, true)]
    public void TouchscreenInput_RecognizesTouchSignaturesWithoutTreatingMouseOrPenAsTouch(ulong extraInfo, bool touch)
    {
        Assert.Equal(touch, NativeMethods.IsTouchInput(new UIntPtr(extraInfo)));
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(NativeMethods.CURSOR_SHOWING, false, true)]
    [InlineData(NativeMethods.CURSOR_SHOWING, true, true)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED, false, false)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED, true, true)]
    [InlineData(NativeMethods.CURSOR_SHOWING | NativeMethods.CURSOR_SUPPRESSED, false, false)]
    [InlineData(NativeMethods.CURSOR_SHOWING | NativeMethods.CURSOR_SUPPRESSED, true, true)]
    public void TouchscreenInput_OnlyAllowsSuppressionCausedByTouchAndKeepsHiddenCursorFiltering(int flags, bool touch, bool visible)
    {
        var cursorInfo = new NativeMethods.CURSORINFO
        {
            flags = flags,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle
        };
        Assert.Equal(visible, NativeMethods.IsCursorVisible(cursorInfo, touch));
        cursorInfo.hCursor = IntPtr.Zero;
        Assert.False(NativeMethods.IsCursorVisible(cursorInfo, touch));
    }

    [Theory]
    [InlineData(NativeMethods.CURSOR_SHOWING, false)]
    [InlineData(NativeMethods.CURSOR_SHOWING, true)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED, false)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED, true)]
    public void TouchscreenInput_StillRejectsTransparentCursorShapes(int flags, bool touch)
    {
        IntPtr transparent = CreateCursor(IntPtr.Zero, 0, 0, 32, 32,
            Enumerable.Repeat((byte)255, 128).ToArray(), new byte[128]);
        Assert.NotEqual(IntPtr.Zero, transparent);
        try
        {
            var cursorInfo = new NativeMethods.CURSORINFO { flags = flags, hCursor = transparent };
            Assert.False(NativeMethods.IsCursorVisible(cursorInfo, touch));
        }
        finally
        {
            DestroyCursor(transparent);
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public void TouchscreenInput_WebViewRetainsItsOwnEligibilityWhenTouchClearsTheCursor(bool visible, bool sameWindow, bool expected)
    {
        var state = new TouchCursorState();
        var window = new IntPtr(123);
        var cursor = new NativeMethods.CURSORINFO
        {
            flags = visible ? NativeMethods.CURSOR_SHOWING : 0,
            hCursor = visible ? System.Windows.Forms.Cursors.Arrow.Handle : IntPtr.Zero
        };
        state.Observe(cursor, window);
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        cursor.hCursor = IntPtr.Zero;
        state.Observe(cursor, window);
        Assert.Equal(expected, state.IsVisible(cursor, sameWindow ? window : new IntPtr(456), true));
        Assert.False(state.IsVisible(cursor, window, false));
        state.Reset();
        Assert.True(state.IsVisible(cursor, window, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TouchscreenInput_WebViewRemembersTheActualTouchWindowInsteadOfTheStationaryMouseWindow(bool suppressedFirst)
    {
        var state = new TouchCursorState();
        var mouseWindow = new IntPtr(123);
        var touchWindow = new IntPtr(456);
        var cursor = new NativeMethods.CURSORINFO
        {
            flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle
        };
        state.Observe(cursor, mouseWindow);
        if (suppressedFirst) cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        Assert.True(state.IsVisible(cursor, touchWindow, true));
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        state.Observe(cursor, mouseWindow);
        cursor.hCursor = IntPtr.Zero;
        for (int tap = 0; tap < 40; tap++)
        {
            state.Observe(cursor, mouseWindow);
            Assert.True(state.IsVisible(cursor, touchWindow, true));
            Assert.True(state.IsVisible(cursor, mouseWindow, true));
            Assert.False(state.IsVisible(cursor, touchWindow, false));
        }
        cursor.flags = 0;
        state.Observe(cursor, mouseWindow);
        Assert.False(state.IsVisible(cursor, touchWindow, true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TouchscreenInput_WebViewCanStartWithAnAlreadySuppressedCursorWithoutMouseWarmup(bool chromium, bool expected)
    {
        var state = new TouchCursorState();
        var window = new IntPtr(123);
        var cursor = new NativeMethods.CURSORINFO { flags = NativeMethods.CURSOR_SUPPRESSED, hCursor = IntPtr.Zero };
        Assert.Equal(expected, state.IsVisible(cursor, window, chromium));
        Assert.False(state.IsVisible(cursor, IntPtr.Zero, chromium));
        cursor.flags = 0;
        Assert.False(state.IsVisible(cursor, window, chromium));
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        Assert.False(state.IsVisible(cursor, window, chromium));
    }

    [Fact]
    public void TouchscreenInput_WebViewRawContactCanStartAfterTouchClearsBothTheCursorAndSuppressionFlag()
    {
        var state = new TouchCursorState();
        var window = new IntPtr(123);
        var cursor = new NativeMethods.CURSORINFO { flags = 0, hCursor = IntPtr.Zero };
        state.BeginContact(window, true);
        Assert.True(state.IsVisible(cursor, window, true));
        state.EndContact();
        Assert.False(state.IsVisible(cursor, window, true));
        state.BeginContact(window, true);
        Assert.False(state.IsVisible(cursor, window, true));
        state.Reset();
        state.BeginContact(window, false);
        Assert.False(state.IsVisible(cursor, window, false));
    }

    [Fact]
    public void TouchscreenInput_WebViewKeepsEachWindowsKnownHiddenStateWhenAnotherWindowIsObserved()
    {
        var state = new TouchCursorState();
        var hiddenWindow = new IntPtr(123);
        var visibleWindow = new IntPtr(456);
        var cursor = new NativeMethods.CURSORINFO { flags = 0, hCursor = IntPtr.Zero };
        state.Observe(cursor, hiddenWindow);
        cursor.flags = NativeMethods.CURSOR_SHOWING;
        cursor.hCursor = System.Windows.Forms.Cursors.Arrow.Handle;
        state.Observe(cursor, visibleWindow);
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        cursor.hCursor = IntPtr.Zero;
        Assert.False(state.IsVisible(cursor, hiddenWindow, true));
        Assert.True(state.IsVisible(cursor, visibleWindow, true));
        state.BeginContact(hiddenWindow, true);
        Assert.False(state.IsVisible(cursor, visibleWindow, true));
        state.EndContact();
        Assert.True(state.IsVisible(cursor, visibleWindow, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TouchscreenInput_WebViewContactSurvivesLeavingItsWindowUntilTheFingerIsReleased(bool clearSuppressionFlag)
    {
        var state = new TouchCursorState();
        var origin = new IntPtr(123);
        var outside = new IntPtr(456);
        var cursor = new NativeMethods.CURSORINFO
        {
            flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle
        };
        state.Observe(cursor, origin);
        state.BeginContact(origin, true);
        Assert.True(state.IsVisible(cursor, origin, true));
        cursor.flags = clearSuppressionFlag ? 0 : NativeMethods.CURSOR_SUPPRESSED;
        cursor.hCursor = IntPtr.Zero;
        Assert.True(state.IsVisible(cursor, outside, false));
        Assert.True(state.IsVisible(cursor, origin, true));
        state.EndContact();
        Assert.False(state.IsVisible(cursor, outside, false));
        state.BeginContact(outside, false);
        Assert.False(state.IsVisible(cursor, outside, false));
        state.Reset();
        Assert.False(state.IsVisible(cursor, outside, false));
    }

    [Fact]
    public void TouchscreenInput_WebViewExplicitHidingAfterSuccessfulTouchStillBlocksEffects()
    {
        var state = new TouchCursorState();
        var window = new IntPtr(123);
        var cursor = new NativeMethods.CURSORINFO
        {
            flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle
        };
        state.Observe(cursor, window);
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        cursor.hCursor = IntPtr.Zero;
        Assert.True(state.IsVisible(cursor, window, true));
        cursor.flags = 0;
        state.Observe(cursor, window);
        Assert.False(state.IsVisible(cursor, window, true));
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        Assert.False(state.IsVisible(cursor, window, true));
        cursor.flags = NativeMethods.CURSOR_SHOWING;
        cursor.hCursor = System.Windows.Forms.Cursors.Arrow.Handle;
        state.Observe(cursor, window);
        Assert.True(state.IsVisible(cursor, window, true));
    }

    [Fact]
    public void TouchscreenInput_WebViewHiddenAndTransparentCursorsStayFilteredAfterTouch()
    {
        var state = new TouchCursorState();
        var window = new IntPtr(123);
        var cursor = new NativeMethods.CURSORINFO { flags = 0, hCursor = IntPtr.Zero };
        state.Observe(cursor, window);
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        cursor.hCursor = System.Windows.Forms.Cursors.Arrow.Handle;
        Assert.False(state.IsVisible(cursor, window, true));
        IntPtr transparent = CreateCursor(IntPtr.Zero, 0, 0, 32, 32,
            Enumerable.Repeat((byte)255, 128).ToArray(), new byte[128]);
        Assert.NotEqual(IntPtr.Zero, transparent);
        try
        {
            cursor.flags = NativeMethods.CURSOR_SHOWING;
            cursor.hCursor = System.Windows.Forms.Cursors.Arrow.Handle;
            state.Observe(cursor, window);
            cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
            cursor.hCursor = transparent;
            Assert.False(state.IsVisible(cursor, window, true));
            cursor.hCursor = IntPtr.Zero;
            Assert.False(state.IsVisible(cursor, window, true));
            cursor.flags = NativeMethods.CURSOR_SHOWING;
            cursor.hCursor = transparent;
            state.Observe(cursor, window);
            cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
            cursor.hCursor = IntPtr.Zero;
            Assert.False(state.IsVisible(cursor, window, true));
        }
        finally
        {
            DestroyCursor(transparent);
        }
    }

    [Fact]
    public void TouchscreenInput_DoesNotMixMouseEventsIntoContactAndCancelsWhenDisabled()
    {
        bool previousMode = ConfigManager.IsTouchscreenMode;
        using var manager = new OverlayManager();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var pointerDown = typeof(OverlayManager).GetField("_isPrimaryPointerDown", members)!;
        var touchInput = typeof(OverlayManager).GetField("_isTouchLikeInput", members)!;
        var history = (Queue<TrailSample>)typeof(OverlayManager).GetField("_trailHistory", members)!.GetValue(manager)!;
        var move = typeof(OverlayManager).GetMethod("OnPointerMove", members)!.CreateDelegate<Action<int, int, bool>>(manager);
        var up = typeof(OverlayManager).GetMethod("OnPointerUp", members)!.CreateDelegate<Action<bool>>(manager);
        try
        {
            manager.UpdateTouchMode(true);
            pointerDown.SetValue(manager, true);
            touchInput.SetValue(manager, true);
            history.Enqueue(new TrailSample(200, 300, System.Diagnostics.Stopwatch.GetTimestamp()));
            move(400, 500, false);
            up(false);
            Assert.True((bool)pointerDown.GetValue(manager)!);
            Assert.True((bool)touchInput.GetValue(manager)!);
            Assert.Equal(200, Assert.Single(history).ScreenX);
            manager.UpdateTouchMode(false);
            Assert.False((bool)pointerDown.GetValue(manager)!);
            Assert.False((bool)touchInput.GetValue(manager)!);
            Assert.Empty(history);
        }
        finally
        {
            ConfigManager.IsTouchscreenMode = previousMode;
        }
    }

    [Theory]
    [InlineData(0, 0.25, 0.75, -1150, 700)]
    [InlineData(1, 0.25, 0.75, -1150, 700)]
    [InlineData(2, 0.25, 0.75, -1150, 300)]
    [InlineData(3, 0.25, 0.75, -650, 300)]
    [InlineData(4, 0.25, 0.75, -650, 700)]
    [InlineData(1, 0.75, 0.25, -650, 300)]
    [InlineData(1, 0, 0, -1400, 100)]
    [InlineData(1, 1, 1, -400, 900)]
    [InlineData(1, -1, 2, -1400, 900)]
    public void TouchscreenInput_MapsRawContactCoordinatesToTheAssignedRotatedDisplay(uint orientation, double normalizedX,
        double normalizedY, int screenX, int screenY)
    {
        var display = new NativeMethods.RECT { Left = -1400, Top = 100, Right = -399, Bottom = 901 };
        var point = TouchInputListener.MapPoint(normalizedX, normalizedY, orientation, display);
        Assert.Equal(screenX, point.x);
        Assert.Equal(screenY, point.y);
    }

    [Theory]
    [InlineData(1, -750, 700, -500, 700)]
    [InlineData(2, -750, 300, -750, 500)]
    [InlineData(3, -250, 300, -500, 300)]
    [InlineData(4, -250, 700, -250, 500)]
    public void TouchscreenInput_ReceivesContactsWithoutMousePromotionAndIgnoresUnusedFingerSlots(uint orientation,
        int downX, int downY, int moveX, int moveY)
    {
        var values = new List<NativeMethods.HIDP_VALUE_CAPS>();
        var buttons = new List<NativeMethods.HIDP_BUTTON_CAPS>();
        for (ushort collection = 1; collection <= 2; collection++)
        {
            ushort firstIndex = (ushort)((collection - 1) * 4);
            values.Add(new NativeMethods.HIDP_VALUE_CAPS
            {
                UsagePage = 1, ReportId = 1, LinkCollection = collection, IsRange = 1, IsAbsolute = 1,
                BitSize = 16, LogicalMin = 0, LogicalMax = 1000, UsageMin = 0x30, UsageMax = 0x31, DataIndexMin = firstIndex
            });
            values.Add(new NativeMethods.HIDP_VALUE_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = collection, UsageMin = 0x51, DataIndexMin = (ushort)(firstIndex + 2)
            });
            buttons.Add(new NativeMethods.HIDP_BUTTON_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = collection, UsageMin = 0x42, DataIndexMin = (ushort)(firstIndex + 3)
            });
        }
        var device = TouchInputListener.TouchDevice.Create(new IntPtr(123), Array.Empty<byte>(),
            new NativeMethods.HIDP_CAPS { InputReportByteLength = 10, NumberInputDataIndices = 8 }, values.ToArray(), buttons.ToArray());
        Assert.NotNull(device);
        var received = new List<(uint Id, bool WasDown, bool Down, int ScreenX, int ScreenY)>();
        void Receive(IntPtr source, uint id, bool wasDown, bool down, int screenX, int screenY)
        {
            Assert.Equal(new IntPtr(123), source);
            received.Add((id, wasDown, down, screenX, screenY));
        }
        var display = new NativeMethods.RECT { Left = -1000, Top = 100, Right = 1, Bottom = 901 };
        var data = new NativeMethods.HIDP_DATA[8];
        for (ushort index = 0; index < data.Length; index++) data[index].DataIndex = index;
        data[0].Value = 250;
        data[1].Value = 750;
        data[3].Value = 1;
        device.ProcessData(1, data, display, orientation, Receive);
        Assert.Equal((0U, false, true, downX, downY), Assert.Single(received));
        data[0].Value = 500;
        device.ProcessData(1, data, display, orientation, Receive);
        Assert.Equal((0U, true, true, moveX, moveY), received[^1]);
        device.ProcessData(2, data, display, orientation, Receive);
        Assert.Equal(2, received.Count);
        data[3].Value = 0;
        device.ProcessData(1, data, display, orientation, Receive);
        Assert.Equal((0U, true, false, moveX, moveY), received[^1]);
        device.ProcessData(1, data, display, orientation, Receive);
        Assert.Equal(3, received.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TouchscreenInput_WebViewRecycledContactIdsDoNotBlockSubsequentTaps(bool clearedId, bool omittedAxes)
    {
        var device = TouchInputListener.TouchDevice.Create(new IntPtr(123), Array.Empty<byte>(),
            new NativeMethods.HIDP_CAPS { InputReportByteLength = 10, NumberInputDataIndices = 4 },
            [
                new NativeMethods.HIDP_VALUE_CAPS
                {
                    UsagePage = 1, ReportId = 1, LinkCollection = 1, IsRange = 1, IsAbsolute = 1,
                    BitSize = 16, LogicalMin = 0, LogicalMax = 1000, UsageMin = 0x30, UsageMax = 0x31, DataIndexMin = 0
                },
                new NativeMethods.HIDP_VALUE_CAPS
                {
                    UsagePage = 13, ReportId = 1, LinkCollection = 1, UsageMin = 0x51, DataIndexMin = 2
                }
            ],
            [new NativeMethods.HIDP_BUTTON_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = 1, UsageMin = 0x42, DataIndexMin = 3
            }]);
        Assert.NotNull(device);
        var phases = new List<TouchInputPhase>();
        using var listener = new TouchInputListener((pointerId, phase, screenX, screenY) => phases.Add(phase));
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var receive = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        var display = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1001, Bottom = 1001 };
        for (uint contactId = 1; contactId <= 12; contactId++)
        {
            NativeMethods.HIDP_DATA[] data =
            [
                new() { DataIndex = 0, Value = 250 }, new() { DataIndex = 1, Value = 750 },
                new() { DataIndex = 2, Value = contactId }, new() { DataIndex = 3, Value = 1 }
            ];
            device.ProcessData(1, data, display, 1, receive);
            device.ProcessData(1, data, display, 1, receive);
            data[2].Value = clearedId ? 0 : contactId;
            data[3].Value = 0;
            device.ProcessData(1, omittedAxes ? data.AsSpan(2) : data, display, 1, receive);
            Assert.Equal(new[] { TouchInputPhase.Down, TouchInputPhase.Move, TouchInputPhase.Up }, phases);
            phases.Clear();
        }
        NativeMethods.HIDP_DATA[] replacement =
        [
            new() { DataIndex = 0, Value = 400 }, new() { DataIndex = 1, Value = 600 },
            new() { DataIndex = 2, Value = 13 }, new() { DataIndex = 3, Value = 1 }
        ];
        device.ProcessData(1, replacement, display, 1, receive);
        device.ProcessData(2, replacement, display, 1, receive);
        Assert.Equal(new[] { TouchInputPhase.Down }, phases);
        replacement[2].Value = 14;
        device.ProcessData(1, replacement, display, 1, receive);
        Assert.Equal(new[] { TouchInputPhase.Down, TouchInputPhase.Up, TouchInputPhase.Down }, phases);
        device.ProcessData(1, ReadOnlySpan<NativeMethods.HIDP_DATA>.Empty, display, 1, receive);
        Assert.Equal(new[] { TouchInputPhase.Down, TouchInputPhase.Up, TouchInputPhase.Down, TouchInputPhase.Up }, phases);
    }

    [Fact]
    public void TouchscreenInput_TracksIndependentContactsAndDeduplicatesTheirPromotedMouseMessages()
    {
        using var mode = new MultiTouchScope(true);
        var received = new List<(long PointerId, TouchInputPhase Phase, int ScreenX, int ScreenY)>();
        using var listener = new TouchInputListener((pointerId, phase, screenX, screenY) => received.Add((pointerId, phase, screenX, screenY)));
        Assert.NotEqual(IntPtr.Zero, listener.Handle);
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contact = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        contact(new IntPtr(1), 0, false, true, 200, 300);
        long first = received[0].PointerId;
        typeof(TouchInputListener).GetField("_lastTouchTime", members)!.SetValue(listener, Environment.TickCount64 - 2000);
        Assert.True(listener.ShouldIgnorePromotedTouch(200, 300));
        Assert.True(listener.ShouldIgnorePromotedTouch(800, 900));
        contact(new IntPtr(1), 1, false, true, 400, 500);
        long second = received[1].PointerId;
        Assert.NotEqual(first, second);
        contact(new IntPtr(1), 0, true, true, 250, 350);
        contact(new IntPtr(1), 1, true, false, 400, 500);
        contact(new IntPtr(1), 0, true, false, 250, 350);
        Assert.Equal(new[] {
            (first, TouchInputPhase.Down, 200, 300), (second, TouchInputPhase.Down, 400, 500),
            (first, TouchInputPhase.Move, 250, 350), (second, TouchInputPhase.Up, 400, 500),
            (first, TouchInputPhase.Up, 250, 350) }, received);
        Assert.True(listener.ShouldIgnorePromotedTouch(200, 300));
        Assert.True(listener.ShouldIgnorePromotedTouch(250, 350));
        Assert.True(listener.ShouldIgnorePromotedTouch(400, 500));
        Assert.False(listener.ShouldIgnorePromotedTouch(800, 900));
        typeof(TouchInputListener).GetField("_lastTouchTime", members)!.SetValue(listener, Environment.TickCount64 - 1001);
        Assert.False(listener.ShouldIgnorePromotedTouch(200, 300));
        listener.Dispose();
        Assert.Equal(IntPtr.Zero, listener.Handle);
    }

    [Fact]
    public void MultiTouch_DeviceRemovalAndRecycledIdsReleaseOnlyTheirOwnContacts()
    {
        using var mode = new MultiTouchScope(true);
        var received = new List<(long PointerId, TouchInputPhase Phase)>();
        using var listener = new TouchInputListener((pointerId, phase, screenX, screenY) => received.Add((pointerId, phase)));
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contact = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        var remove = typeof(TouchInputListener).GetMethod("ReleaseDeviceContacts", members)!.CreateDelegate<Action<IntPtr>>(listener);
        contact(new IntPtr(1), 7, false, true, 200, 300);
        long first = received[^1].PointerId;
        contact(new IntPtr(2), 7, false, true, 400, 500);
        long second = received[^1].PointerId;
        Assert.NotEqual(first, second);
        remove(new IntPtr(1));
        Assert.Equal((first, TouchInputPhase.Up), received[^1]);
        contact(new IntPtr(2), 7, true, true, 450, 550);
        Assert.Equal((second, TouchInputPhase.Move), received[^1]);
        contact(new IntPtr(1), 7, false, true, 200, 300);
        Assert.NotEqual(first, received[^1].PointerId);
        remove(new IntPtr(2));
        Assert.Equal((second, TouchInputPhase.Up), received[^1]);
        remove(new IntPtr(2));
        Assert.Equal(6, received.Count);
    }

    [Fact]
    public void MultiTouch_DisabledRetainsTheFirstContactUntilReleaseWithoutPromotingHeldFingers()
    {
        using var mode = new MultiTouchScope(false);
        var received = new List<(long PointerId, TouchInputPhase Phase)>();
        using var listener = new TouchInputListener((pointerId, phase, screenX, screenY) => received.Add((pointerId, phase)));
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contact = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        contact(new IntPtr(1), 0, false, true, 200, 300);
        long first = Assert.Single(received).PointerId;
        contact(new IntPtr(1), 1, false, true, 400, 500);
        contact(new IntPtr(1), 1, true, true, 450, 550);
        Assert.Single(received);
        contact(new IntPtr(1), 0, true, false, 200, 300);
        Assert.Equal((first, TouchInputPhase.Up), received[^1]);
        contact(new IntPtr(1), 1, true, true, 460, 560);
        Assert.Equal(2, received.Count);
        contact(new IntPtr(1), 2, false, true, 600, 700);
        long next = received[^1].PointerId;
        Assert.NotEqual(first, next);
        Assert.Equal(TouchInputPhase.Down, received[^1].Phase);
        contact(new IntPtr(1), 1, true, false, 460, 560);
        Assert.Equal(3, received.Count);
        contact(new IntPtr(1), 2, true, false, 600, 700);
        Assert.Equal((next, TouchInputPhase.Up), received[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void TouchEffectLimit_AdmitsOnlyTheConfiguredCountAndReusesReleasedSlots(int maximum)
    {
        using var mode = new MultiTouchScope(maximum);
        var received = new List<(long PointerId, TouchInputPhase Phase)>();
        using var listener = new TouchInputListener((pointerId, phase, screenX, screenY) => received.Add((pointerId, phase)));
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contact = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        for (uint index = 0; index < 12; index++) contact(new IntPtr(1), index, false, true, 200, 300);
        int count = maximum == 0 ? 12 : maximum;
        Assert.Equal(count, received.Count);
        Assert.All(received, input => Assert.Equal(TouchInputPhase.Down, input.Phase));
        long[] admitted = received.Select(input => input.PointerId).ToArray();
        received.Clear();
        for (uint index = 0; index < 12; index++) contact(new IntPtr(1), index, true, true, 250, 350);
        Assert.Equal(admitted, received.Select(input => input.PointerId));
        Assert.All(received, input => Assert.Equal(TouchInputPhase.Move, input.Phase));
        received.Clear();
        contact(new IntPtr(1), 0, true, false, 250, 350);
        Assert.Equal((admitted[0], TouchInputPhase.Up), Assert.Single(received));
        received.Clear();
        for (uint index = 1; index < 12; index++) contact(new IntPtr(1), index, true, true, 260, 360);
        Assert.Equal(admitted.Skip(1), received.Select(input => input.PointerId));
        contact(new IntPtr(1), 12, false, true, 300, 400);
        long added = received[^1].PointerId;
        Assert.Equal(TouchInputPhase.Down, received[^1].Phase);
        Assert.DoesNotContain(added, admitted);
        received.Clear();
        for (uint index = 1; index <= 12; index++) contact(new IntPtr(1), index, true, false, 300, 400);
        Assert.Equal(admitted.Skip(1).Append(added), received.Select(input => input.PointerId));
        Assert.All(received, input => Assert.Equal(TouchInputPhase.Up, input.Phase));
    }

    [Fact]
    public void TouchEffectLimit_ChangingTheLimitReleasesOnlyExcessContactsAndDoesNotPromoteHeldFingers()
    {
        using var mode = new MultiTouchScope(0);
        var received = new List<(long PointerId, TouchInputPhase Phase)>();
        using var listener = new TouchInputListener((pointerId, phase, screenX, screenY) => received.Add((pointerId, phase)));
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contact = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        for (uint index = 0; index < 5; index++) contact(new IntPtr(1), index, false, true, 200, 300);
        long[] admitted = received.Select(input => input.PointerId).ToArray();
        received.Clear();
        ConfigManager.MaxTouchEffects = 2;
        listener.UpdateEffectLimit();
        Assert.Equal(admitted.Skip(2), received.Select(input => input.PointerId));
        Assert.All(received, input => Assert.Equal(TouchInputPhase.Up, input.Phase));
        received.Clear();
        for (uint index = 0; index < 5; index++) contact(new IntPtr(1), index, true, true, 250, 350);
        Assert.Equal(admitted.Take(2), received.Select(input => input.PointerId));
        received.Clear();
        ConfigManager.MaxTouchEffects = 0;
        listener.UpdateEffectLimit();
        for (uint index = 2; index < 5; index++) contact(new IntPtr(1), index, true, true, 260, 360);
        Assert.Empty(received);
        contact(new IntPtr(1), 5, false, true, 300, 400);
        Assert.Equal(TouchInputPhase.Down, Assert.Single(received).Phase);
    }

    [Fact]
    public void TouchEffectLimit_RuntimeReductionKeepsExistingAllowedTrails()
    {
        using var mode = new MultiTouchScope(2);
        bool previousTouchMode = ConfigManager.IsTouchscreenMode;
        using var manager = new OverlayManager();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contacts = (Dictionary<long, OverlayManager.TouchTrailState>)typeof(OverlayManager).GetField("_touchContacts", members)!.GetValue(manager)!;
        var trails = Enumerable.Range(2, 5).Select(pointerId => new OverlayManager.TouchTrailState(pointerId, pointerId, new NativeMethods.POINT())).ToArray();
        foreach (OverlayManager.TouchTrailState trail in trails)
        {
            trail.History.Enqueue(new TrailSample(100, 200, 0));
            contacts.Add(trail.PointerId, trail);
        }
        try
        {
            manager.UpdateTouchMode(true);
            Assert.Equal(new long[] { 2, 3 }, contacts.Keys);
            Assert.All(trails.Take(2), trail => Assert.Single(trail.History));
            Assert.All(trails.Skip(2), trail => Assert.Empty(trail.History));
            ConfigManager.MaxTouchEffects = 0;
            manager.UpdateTouchMode(true);
            Assert.Equal(2, contacts.Count);
            ConfigManager.MaxTouchEffects = 1;
            manager.UpdateTouchMode(true);
            Assert.Equal(2, Assert.Single(contacts).Key);
            Assert.Empty(trails[1].History);
        }
        finally
        {
            ConfigManager.IsTouchscreenMode = previousTouchMode;
        }
    }

    [Fact]
    public void MultiTouch_SettingsCardUsesTheExistingConditionalAnimationAndPrecedesScreenshotMode()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement section = GetNamedElement(document, "SectionBasic");
        XElement touch = GetNamedElement(document, "CheckTouchscreenMode");
        XElement multi = GetNamedElement(document, "PanelMaxTouchEffects");
        XElement screenshot = GetNamedElement(document, "CheckScreenshotCompatibilityMode");
        XElement TouchCard(XElement control) => control.Ancestors().Single(element => element.Parent == section);
        var children = section.Elements().ToList();
        Assert.True(children.IndexOf(TouchCard(touch)) < children.IndexOf(multi));
        Assert.True(children.IndexOf(multi) < children.IndexOf(TouchCard(screenshot)));
        Assert.Equal("Collapsed", (string?)multi.Attribute("Visibility"));
        XElement slider = GetNamedElement(document, "SliderMaxTouchEffects");
        XElement input = GetNamedElement(document, "TxtMaxTouchEffectsValue");
        Assert.Equal("Slider", slider.Name.LocalName);
        Assert.Equal("NumberBox", input.Name.LocalName);
        foreach (XElement control in new[] { slider, input })
        {
            Assert.Equal("1", (string?)control.Attribute("Minimum"));
            Assert.Equal("11", (string?)control.Attribute("Maximum"));
            Assert.Equal("1", (string?)control.Attribute("Value"));
        }
        Assert.Equal("1", (string?)slider.Attribute("StepFrequency"));
        Assert.Equal("1", (string?)input.Attribute("SmallChange"));
        Assert.Equal("Compact", (string?)input.Attribute("SpinButtonPlacementMode"));
        Assert.Equal("84", (string?)input.Attribute("MinWidth"));
        Assert.Equal((string?)GetNamedElement(document, "TxtTrailSamplingRateValue").Attribute("Width"), (string?)input.Attribute("Width"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("SetSettingsVisibility(SectionBasic, animate, (PanelMaxTouchEffects, CheckTouchscreenMode.IsOn))", source, StringComparison.Ordinal);
        Assert.Contains("MaxTouchEffects = _touchEffectLimitFormatter.ToStoredValue(GetPendingSliderValue(SliderMaxTouchEffects))", source, StringComparison.Ordinal);
        foreach (string language in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            XDocument strings = XDocument.Parse(ReadSource("src", language));
            Assert.Single(strings.Descendants("data"), element => (string?)element.Attribute("name") == "Basic_MaxTouchEffects");
            Assert.Single(strings.Descendants("data"), element => (string?)element.Attribute("name") == "Basic_MaxTouchEffectsHint");
            if (language == "Strings.resx") Assert.Equal("启用触摸特效", strings.Descendants("data")
                .Single(element => (string?)element.Attribute("name") == "Basic_Touchscreen").Element("value")!.Value);
        }
    }

    [Fact]
    public void MultiTouch_CursorEligibilityAndReleaseAreIndependentPerContact()
    {
        var state = new TouchCursorState();
        var visible = new IntPtr(123);
        var hidden = new IntPtr(456);
        state.Observe(new NativeMethods.CURSORINFO { flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle }, visible);
        state.Observe(new NativeMethods.CURSORINFO(), hidden);
        state.BeginContact(visible, true, 2);
        state.BeginContact(hidden, true, 3);
        var cursor = new NativeMethods.CURSORINFO { flags = NativeMethods.CURSOR_SUPPRESSED };
        Assert.True(state.IsVisible(cursor, hidden, true, 2));
        Assert.False(state.IsVisible(cursor, visible, true, 3));
        state.EndContact(3);
        Assert.True(state.IsVisible(cursor, hidden, false, 2));
        state.BeginContact(hidden, false, 3);
        Assert.False(state.IsVisible(cursor, hidden, false, 3));
        state.Reset();
        Assert.False(state.IsVisible(cursor, hidden, false, 2));
    }

    [Theory]
    [InlineData(NativeMethods.CURSOR_SHOWING)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED)]
    [InlineData(NativeMethods.CURSOR_SHOWING | NativeMethods.CURSOR_SUPPRESSED)]
    public void MultiTouch_SystemGestureCursorKeepsNativeTrailsIndependentUntilRelease(int flags)
    {
        using var gesture = new SystemGestureCursorScope();
        Assert.True(NativeMethods.IsSystemTouchGestureCursor(gesture.Handle));
        var state = new TouchCursorState();
        var origin = new IntPtr(123);
        var outside = new IntPtr(456);
        var blocked = new IntPtr(789);
        var cursor = new NativeMethods.CURSORINFO
        {
            flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle
        };
        state.Observe(cursor, origin);
        state.Observe(cursor, outside);
        state.Observe(new NativeMethods.CURSORINFO(), blocked);
        state.BeginContact(origin, false, 2);
        state.BeginContact(origin, false, 3);
        Assert.True(state.IsVisible(cursor, origin, false, 2));
        Assert.True(state.IsVisible(cursor, origin, false, 3));
        cursor.flags = flags;
        cursor.hCursor = gesture.Handle;
        Assert.False(NativeMethods.IsCursorVisible(cursor, true));
        for (int frame = 0; frame < 30; frame++)
        {
            Assert.True(state.IsVisible(cursor, origin, false, 2));
            Assert.True(state.IsVisible(cursor, origin, false, 3));
        }
        Assert.True(state.IsVisible(cursor, outside, false, 2));
        Assert.False(state.IsVisible(cursor, blocked, false, 2));
        Assert.True(state.IsVisible(cursor, origin, false, 3));
        state.EndContact(2);
        Assert.False(state.IsVisible(cursor, origin, false, 2));
        Assert.True(state.IsVisible(cursor, origin, false, 3));
        state.EndContact(3);
        Assert.False(state.IsVisible(cursor, origin, false, 3));
        cursor.flags = NativeMethods.CURSOR_SHOWING;
        state.Observe(cursor, origin, touchInput: true);
        state.BeginContact(origin, false, 4);
        Assert.True(state.IsVisible(cursor, origin, false, 4));
        state.EndContact(4);
        state.Observe(cursor, origin);
        state.BeginContact(origin, false, 5);
        Assert.False(state.IsVisible(cursor, origin, false, 5));
        state.Reset();
        Assert.False(state.IsVisible(cursor, origin, false, 5));
    }

    [Theory]
    [InlineData(NativeMethods.CURSOR_SHOWING)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED)]
    public void MultiTouch_SystemGestureCursorPreservesExplicitHiddenAndTransparentFiltering(int flags)
    {
        using var gesture = new SystemGestureCursorScope();
        Assert.True(NativeMethods.IsSystemTouchGestureCursor(gesture.Handle));
        var state = new TouchCursorState();
        var allowed = new IntPtr(123);
        var blocked = new IntPtr(456);
        var cursor = new NativeMethods.CURSORINFO
        {
            flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle
        };
        state.Observe(cursor, allowed);
        state.Observe(new NativeMethods.CURSORINFO(), blocked);
        state.BeginContact(allowed, false, 2);
        state.BeginContact(blocked, false, 3);
        Assert.True(state.IsVisible(cursor, allowed, false, 2));
        cursor.flags = flags;
        cursor.hCursor = gesture.Handle;
        Assert.True(state.IsVisible(cursor, allowed, false, 2));
        Assert.False(state.IsVisible(cursor, blocked, false, 3));
        Assert.False(state.IsVisible(cursor, allowed, false, 4));
        cursor.flags = 0;
        state.Observe(cursor, allowed, touchInput: true);
        Assert.False(state.IsVisible(cursor, allowed, false, 2));
        cursor.flags = flags;
        cursor.hCursor = IntPtr.Zero;
        Assert.False(state.IsVisible(cursor, allowed, false, 2));
        IntPtr transparent = CreateCursor(IntPtr.Zero, 0, 0, 32, 32,
            Enumerable.Repeat((byte)255, 128).ToArray(), new byte[128]);
        Assert.NotEqual(IntPtr.Zero, transparent);
        try
        {
            cursor.flags = NativeMethods.CURSOR_SHOWING;
            cursor.hCursor = System.Windows.Forms.Cursors.Arrow.Handle;
            Assert.True(state.IsVisible(cursor, allowed, false, 2));
            cursor.flags = flags;
            cursor.hCursor = transparent;
            state.Observe(cursor, allowed, touchInput: true);
            Assert.False(state.IsVisible(cursor, allowed, false, 2));
            cursor.hCursor = gesture.Handle;
            Assert.False(state.IsVisible(cursor, allowed, false, 2));
        }
        finally
        {
            DestroyCursor(transparent);
        }
    }

    [Theory]
    [InlineData(NativeMethods.CURSOR_SHOWING)]
    [InlineData(NativeMethods.CURSOR_SUPPRESSED)]
    [InlineData(NativeMethods.CURSOR_SHOWING | NativeMethods.CURSOR_SUPPRESSED)]
    public void MultiTouch_GesturesCanStartAdditionalContactsInPreviouslyUnobservedWindows(int flags)
    {
        using var gesture = new SystemGestureCursorScope();
        var state = new TouchCursorState();
        var first = new IntPtr(123);
        var second = new IntPtr(456);
        var hidden = new IntPtr(789);
        var cursor = new NativeMethods.CURSORINFO { flags = NativeMethods.CURSOR_SHOWING,
            hCursor = System.Windows.Forms.Cursors.Arrow.Handle };
        state.Observe(cursor, first);
        state.Observe(new NativeMethods.CURSORINFO(), hidden);
        state.BeginContact(first, false, 2);
        Assert.True(state.IsVisible(cursor, first, false, 2));
        cursor.flags = flags;
        cursor.hCursor = gesture.Handle;
        state.BeginContact(second, false, 3);
        state.BeginContact(hidden, false, 4);
        for (int frame = 0; frame < 30; frame++)
        {
            Assert.True(state.IsVisible(cursor, first, false, 2));
            Assert.True(state.IsVisible(cursor, second, false, 3));
            Assert.False(state.IsVisible(cursor, hidden, false, 4));
        }
        state.EndContact(2);
        Assert.False(state.IsVisible(cursor, first, false, 2));
        Assert.True(state.IsVisible(cursor, second, false, 3));
        state.EndContact(3);
        Assert.False(state.IsVisible(cursor, second, false, 3));
        state.BeginContact(second, false, 5);
        Assert.True(state.IsVisible(cursor, second, false, 5));
        cursor.flags = 0;
        cursor.hCursor = IntPtr.Zero;
        Assert.False(state.IsVisible(cursor, second, false, 5));
        cursor.flags = flags;
        cursor.hCursor = gesture.Handle;
        Assert.False(state.IsVisible(cursor, second, false, 5));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void MultiTouch_GestureCursorCanTransitionToTouchSuppressionWithoutInterruptingContacts(int count, bool chromium)
    {
        using var gesture = new SystemGestureCursorScope();
        var state = new TouchCursorState();
        var cursor = new NativeMethods.CURSORINFO { flags = NativeMethods.CURSOR_SHOWING, hCursor = gesture.Handle };
        for (int index = 0; index < count; index++)
        {
            var window = new IntPtr(123 + index);
            state.BeginContact(window, chromium, index + 2);
            Assert.True(state.IsVisible(cursor, window, chromium, index + 2));
        }
        cursor.flags = NativeMethods.CURSOR_SUPPRESSED;
        cursor.hCursor = IntPtr.Zero;
        for (int frame = 0; frame < 30; frame++)
            for (int index = 0; index < count; index++)
                Assert.True(state.IsVisible(cursor, new IntPtr(123 + index), chromium, index + 2));
        state.EndContact(2);
        for (int index = 1; index < count; index++)
            Assert.True(state.IsVisible(cursor, new IntPtr(123 + index), chromium, index + 2));
        state.Reset();
        Assert.False(state.IsVisible(cursor, new IntPtr(123), false, 2));
    }

    [Theory]
    [InlineData(200, 300, 0, true)]
    [InlineData(224, 324, 0, true)]
    [InlineData(225, 300, 0, false)]
    [InlineData(200, 325, 0, false)]
    [InlineData(200, 300, 200, false)]
    public void MultiTouch_PromotedClicksAreAdoptedOnceWithoutSuppressingOtherContacts(int horizontal, int vertical, int ageMilliseconds, bool expected)
    {
        using var manager = new OverlayManager();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        typeof(OverlayManager).GetField("_lastPromotedTouchTimestamp", members)!.SetValue(manager,
            timestamp - System.Diagnostics.Stopwatch.Frequency * ageMilliseconds / 1000);
        typeof(OverlayManager).GetField("_lastPromotedTouchPoint", members)!.SetValue(manager,
            new NativeMethods.POINT { x = 200, y = 300 });
        var consume = typeof(OverlayManager).GetMethod("TryConsumePromotedTouchClick", members)!
            .CreateDelegate<Func<int, int, long, bool>>(manager);
        Assert.Equal(expected, consume(horizontal, vertical, timestamp));
        if (expected) Assert.False(consume(horizontal, vertical, timestamp));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiTouch_HidReportsRetainTheOtherContactWhenOneContactDisappears(bool omittedAxes)
    {
        var values = new List<NativeMethods.HIDP_VALUE_CAPS>();
        var buttons = new List<NativeMethods.HIDP_BUTTON_CAPS>();
        for (ushort collection = 1; collection <= 2; collection++)
        {
            ushort start = (ushort)((collection - 1) * 4);
            values.Add(new NativeMethods.HIDP_VALUE_CAPS
            {
                UsagePage = 1, ReportId = 1, LinkCollection = collection, IsRange = 1, IsAbsolute = 1,
                BitSize = 16, LogicalMin = 0, LogicalMax = 1000, UsageMin = 0x30, UsageMax = 0x31, DataIndexMin = start
            });
            values.Add(new NativeMethods.HIDP_VALUE_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = collection, UsageMin = 0x51, DataIndexMin = (ushort)(start + 2)
            });
            buttons.Add(new NativeMethods.HIDP_BUTTON_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = collection, UsageMin = 0x42, DataIndexMin = (ushort)(start + 3)
            });
        }
        var device = TouchInputListener.TouchDevice.Create(new IntPtr(123), Array.Empty<byte>(),
            new NativeMethods.HIDP_CAPS { InputReportByteLength = 20, NumberInputDataIndices = 8 }, values.ToArray(), buttons.ToArray());
        Assert.NotNull(device);
        NativeMethods.HIDP_DATA[] data =
        [
            new() { DataIndex = 0, Value = 100 }, new() { DataIndex = 1, Value = 200 },
            new() { DataIndex = 2, Value = 7 }, new() { DataIndex = 3, Value = 1 },
            new() { DataIndex = 4, Value = 700 }, new() { DataIndex = 5, Value = 800 },
            new() { DataIndex = 6, Value = 8 }, new() { DataIndex = 7, Value = 1 }
        ];
        var received = new List<(uint Id, bool WasDown, bool Down, int Horizontal, int Vertical)>();
        void Receive(IntPtr source, uint id, bool wasDown, bool down, int horizontal, int vertical) =>
            received.Add((id, wasDown, down, horizontal, vertical));
        var display = new NativeMethods.RECT { Right = 1001, Bottom = 1001 };
        device.ProcessData(1, data, display, 1, Receive);
        Assert.Equal(new[] { (7U, false, true, 100, 200), (8U, false, true, 700, 800) }, received);
        received.Clear();
        data[2].Value = 0;
        data[3].Value = 0;
        data[4].Value = 750;
        device.ProcessData(1, omittedAxes ? data.AsSpan(4) : data, display, 1, Receive);
        Assert.Equal(new[] { (7U, true, false, 100, 200), (8U, true, true, 750, 800) }, received);
        received.Clear();
        data[2].Value = 7;
        data[3].Value = 1;
        device.ProcessData(1, data, display, 1, Receive);
        Assert.Equal(new[] { (7U, false, true, 100, 200), (8U, true, true, 750, 800) }, received);
    }

    [Fact]
    public void MultiTouch_IndependentTrailsContinueAcrossScreensAndReleaseSeparately()
    {
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var firstHost = new LayeredWindowHost();
        using var secondHost = new LayeredWindowHost();
        using var manager = new OverlayManager();
        OverlayWindow CreateOverlay(int left, LayeredWindowHost host)
        {
            host.SetBounds(left, 0, 400, 600);
            var overlay = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
            typeof(OverlayWindow).GetField("_host", members)!.SetValue(overlay, host);
            typeof(OverlayWindow).GetField("_screenBounds", members)!.SetValue(overlay,
                new NativeMethods.RECT { Left = left, Top = 0, Right = left + 400, Bottom = 600 });
            return overlay;
        }
        OverlayWindow firstOverlay = CreateOverlay(0, firstHost);
        OverlayWindow secondOverlay = CreateOverlay(400, secondHost);
        var overlays = (Dictionary<string, OverlayWindow>)typeof(OverlayManager).GetField("_overlays", members)!.GetValue(manager)!;
        var contacts = (Dictionary<long, OverlayManager.TouchTrailState>)typeof(OverlayManager).GetField("_touchContacts", members)!.GetValue(manager)!;
        var move = typeof(OverlayManager).GetMethod("RouteTouchTrailMove", members)!
            .CreateDelegate<Action<OverlayManager.TouchTrailState, OverlayWindow, int, int, long>>(manager);
        var release = typeof(OverlayManager).GetMethod("ReleaseTouchContact", members)!
            .CreateDelegate<Action<OverlayManager.TouchTrailState, bool>>(manager);
        var first = new OverlayManager.TouchTrailState(2, 42, new NativeMethods.POINT());
        var second = new OverlayManager.TouchTrailState(3, 43, new NativeMethods.POINT());
        overlays.Add("first", firstOverlay);
        overlays.Add("second", secondOverlay);
        contacts.Add(2, first);
        contacts.Add(3, second);
        try
        {
            long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            move(first, firstOverlay, 100, 100, timestamp);
            move(second, secondOverlay, 500, 500, timestamp);
            move(first, firstOverlay, 390, 100, timestamp + 1);
            move(first, secondOverlay, 410, 100, timestamp + 2);
            move(second, secondOverlay, 550, 500, timestamp + 3);
            Assert.Equal(2, first.Participants.Count);
            Assert.Single(second.Participants);
            Assert.All(first.History, sample => Assert.Equal(100, sample.ScreenY));
            Assert.All(second.History, sample => Assert.Equal(500, sample.ScreenY));
            release(first, true);
            Assert.Empty(first.History);
            Assert.Empty(first.Participants);
            Assert.Same(second, Assert.Single(contacts).Value);
            Assert.Equal(2, second.History.Count);
            move(second, firstOverlay, 350, 500, timestamp + 4);
            Assert.Equal(2, second.Participants.Count);
            manager.UpdateTouchMode(false);
            Assert.Empty(contacts);
            Assert.Empty(second.History);
            Assert.Empty(second.Participants);
        }
        finally
        {
            overlays.Clear();
        }
    }

    [Fact]
    public void MultiTouch_HybridHidFramesDoNotReleaseOtherContactsBetweenReports()
    {
        var device = TouchInputListener.TouchDevice.Create(new IntPtr(123), Array.Empty<byte>(),
            new NativeMethods.HIDP_CAPS { InputReportByteLength = 12, NumberInputDataIndices = 5 },
            [
                new NativeMethods.HIDP_VALUE_CAPS
                {
                    UsagePage = 1, ReportId = 1, LinkCollection = 1, IsRange = 1, IsAbsolute = 1,
                    BitSize = 16, LogicalMin = 0, LogicalMax = 1000, UsageMin = 0x30, UsageMax = 0x31, DataIndexMin = 0
                },
                new NativeMethods.HIDP_VALUE_CAPS
                {
                    UsagePage = 13, ReportId = 1, LinkCollection = 1, UsageMin = 0x51, DataIndexMin = 2
                },
                new NativeMethods.HIDP_VALUE_CAPS
                {
                    UsagePage = 13, ReportId = 1, UsageMin = 0x54, DataIndexMin = 4
                }
            ],
            [new NativeMethods.HIDP_BUTTON_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = 1, UsageMin = 0x42, DataIndexMin = 3
            }]);
        Assert.NotNull(device);
        var received = new List<(uint Id, bool WasDown, bool Down)>();
        void Receive(IntPtr source, uint id, bool wasDown, bool down, int horizontal, int vertical) => received.Add((id, wasDown, down));
        var display = new NativeMethods.RECT { Right = 1001, Bottom = 1001 };
        void Report(uint id, uint count, uint down)
        {
            NativeMethods.HIDP_DATA[] data =
            [
                new() { DataIndex = 0, Value = id * 100 }, new() { DataIndex = 1, Value = id * 100 },
                new() { DataIndex = 2, Value = id }, new() { DataIndex = 3, Value = down },
                new() { DataIndex = 4, Value = count }
            ];
            device.ProcessData(1, data, display, 1, Receive);
        }
        Report(1, 2, 1);
        Assert.Empty(received);
        Report(2, 0, 1);
        Assert.Equal(new[] { (1U, false, true), (2U, false, true) }, received);
        received.Clear();
        Report(1, 2, 1);
        Assert.Empty(received);
        Report(2, 0, 1);
        Assert.Equal(new[] { (1U, true, true), (2U, true, true) }, received);
        received.Clear();
        Report(1, 2, 0);
        Assert.Empty(received);
        Report(2, 0, 1);
        Assert.Equal(new[] { (1U, true, false), (2U, true, true) }, received);
        received.Clear();
        Report(2, 1, 1);
        Assert.Equal(new[] { (2U, true, true) }, received);
        received.Clear();
        device.ProcessData(1, ReadOnlySpan<NativeMethods.HIDP_DATA>.Empty, display, 1, Receive);
        Assert.Equal(new[] { (2U, true, false) }, received);
    }

    [Theory]
    [InlineData(0U, 2)]
    [InlineData(0U, 3)]
    [InlineData(7U, 2)]
    [InlineData(7U, 3)]
    public void MultiTouch_HybridHidPaddingPreservesSimultaneousDragContacts(uint firstId, int contactCount)
    {
        using var mode = new MultiTouchScope(true);
        var received = new List<(long PointerId, TouchInputPhase Phase, int Horizontal, int Vertical)>();
        using var listener = new TouchInputListener((pointerId, phase, horizontal, vertical) =>
            received.Add((pointerId, phase, horizontal, vertical)));
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contactInput = typeof(TouchInputListener).GetMethod("HandleContact", members)!
            .CreateDelegate<Action<IntPtr, uint, bool, bool, int, int>>(listener);
        var values = new List<NativeMethods.HIDP_VALUE_CAPS>();
        var buttons = new List<NativeMethods.HIDP_BUTTON_CAPS>();
        for (ushort collection = 1; collection <= 2; collection++)
        {
            ushort start = (ushort)((collection - 1) * 4);
            values.Add(new NativeMethods.HIDP_VALUE_CAPS
            {
                UsagePage = 1, ReportId = 1, LinkCollection = collection, IsRange = 1, IsAbsolute = 1,
                BitSize = 16, LogicalMin = 0, LogicalMax = 1000, UsageMin = 0x30, UsageMax = 0x31, DataIndexMin = start
            });
            values.Add(new NativeMethods.HIDP_VALUE_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = collection, UsageMin = 0x51, DataIndexMin = (ushort)(start + 2)
            });
            buttons.Add(new NativeMethods.HIDP_BUTTON_CAPS
            {
                UsagePage = 13, ReportId = 1, LinkCollection = collection, UsageMin = 0x42, DataIndexMin = (ushort)(start + 3)
            });
        }
        values.Add(new NativeMethods.HIDP_VALUE_CAPS { UsagePage = 13, ReportId = 1, UsageMin = 0x54, DataIndexMin = 8 });
        var device = TouchInputListener.TouchDevice.Create(new IntPtr(123), Array.Empty<byte>(),
            new NativeMethods.HIDP_CAPS { InputReportByteLength = 20, NumberInputDataIndices = 9 }, values.ToArray(), buttons.ToArray());
        Assert.NotNull(device);
        var display = new NativeMethods.RECT { Right = 1001, Bottom = 1001 };
        void Report(int index, uint count, bool down, int step)
        {
            NativeMethods.HIDP_DATA[] data =
            [
                new() { DataIndex = 0, Value = (uint)(100 + index * 200 + step * 10) },
                new() { DataIndex = 1, Value = (uint)(100 + index * 200) },
                new() { DataIndex = 2, Value = firstId + (uint)index }, new() { DataIndex = 3, Value = down ? 1U : 0U },
                new() { DataIndex = 4, Value = 0 }, new() { DataIndex = 5, Value = 0 },
                new() { DataIndex = 6, Value = 0 }, new() { DataIndex = 7, Value = 0 },
                new() { DataIndex = 8, Value = count }
            ];
            device.ProcessData(1, data, display, 1, contactInput);
        }
        for (int index = 0; index < contactCount; index++)
            Report(index, index == 0 ? (uint)contactCount : 0U, true, 0);
        Assert.Equal(contactCount, received.Count);
        Assert.All(received, input => Assert.Equal(TouchInputPhase.Down, input.Phase));
        long[] pointerIds = received.Select(input => input.PointerId).ToArray();
        for (int step = 1; step <= 10; step++)
        {
            received.Clear();
            for (int index = 0; index < contactCount; index++)
            {
                Report(index, index == 0 ? (uint)contactCount : 0U, true, step);
                if (index < contactCount - 1) Assert.Empty(received);
            }
            Assert.Equal(contactCount, received.Count);
            for (int index = 0; index < contactCount; index++)
                Assert.Equal((pointerIds[index], TouchInputPhase.Move, 100 + index * 200 + step * 10, 100 + index * 200), received[index]);
        }
        received.Clear();
        for (int index = 0; index < contactCount; index++)
            Report(index, index == 0 ? (uint)contactCount : 0U, index != 0, 11);
        Assert.Equal((pointerIds[0], TouchInputPhase.Up, 210, 100), received[0]);
        Assert.All(received.Skip(1), input => Assert.Equal(TouchInputPhase.Move, input.Phase));
        received.Clear();
        for (int index = 1; index < contactCount; index++)
            Report(index, index == 1 ? (uint)contactCount - 1 : 0U, true, 12);
        Assert.Equal(contactCount - 1, received.Count);
        for (int index = 1; index < contactCount; index++)
            Assert.Equal((pointerIds[index], TouchInputPhase.Move, 220 + index * 200, 100 + index * 200), received[index - 1]);
        received.Clear();
        for (int index = 1; index < contactCount; index++)
            Report(index, index == 1 ? (uint)contactCount - 1 : 0U, false, 12);
        Assert.Equal(contactCount - 1, received.Count);
        Assert.All(received, input => Assert.Equal(TouchInputPhase.Up, input.Phase));
    }

    [Fact]
    public void MultiTouch_FilteringOneContactDoesNotSuppressOrClearTheOtherContact()
    {
        bool previousMode = ConfigManager.IsTouchscreenMode;
        bool previousFilter = ConfigManager.EnableEnvironmentFilter;
        using var manager = new OverlayManager();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var contacts = (Dictionary<long, OverlayManager.TouchTrailState>)typeof(OverlayManager).GetField("_touchContacts", members)!.GetValue(manager)!;
        var overlays = (Dictionary<string, OverlayWindow>)typeof(OverlayManager).GetField("_overlays", members)!.GetValue(manager)!;
        var input = typeof(OverlayManager).GetMethod("OnTouchInput", members)!.CreateDelegate<Action<long, TouchInputPhase, int, int>>(manager);
        var update = typeof(OverlayManager).GetMethods(members).Single(method => method.Name == "UpdateSuppressionState" && method.GetParameters().Length == 2)
            .CreateDelegate<Action<long, bool>>(manager);
        var blocked = new OverlayManager.TouchTrailState(2, 42, new NativeMethods.POINT { x = -100000, y = -100000 });
        var allowed = new OverlayManager.TouchTrailState(3, 43, new NativeMethods.POINT { x = -100000, y = -100000 });
        blocked.FilterWindow = allowed.FilterWindow = NativeMethods.GetForegroundWindow();
        blocked.FilterValidUntil = allowed.FilterValidUntil = DateTime.UtcNow.AddSeconds(10).Ticks;
        blocked.FilterSuppressed = true;
        blocked.History.Enqueue(new TrailSample(10, 20, 0));
        allowed.History.Enqueue(new TrailSample(30, 40, 0));
        contacts.Add(2, blocked);
        contacts.Add(3, allowed);
        overlays.Add("placeholder", (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow)));
        try
        {
            ConfigManager.IsTouchscreenMode = true;
            ConfigManager.EnableEnvironmentFilter = true;
            update(DateTime.UtcNow.Ticks, true);
            Assert.False(manager.IsEffectSuppressedByEnvironment());
            input(2, TouchInputPhase.Move, -100000, -100000);
            Assert.Same(allowed, Assert.Single(contacts).Value);
            Assert.Empty(blocked.History);
            Assert.Equal(30, Assert.Single(allowed.History).ScreenX);
            Assert.False(manager.IsEffectSuppressedByEnvironment());
            input(3, TouchInputPhase.Up, -100000, -100000);
            Assert.Empty(contacts);
        }
        finally
        {
            overlays.Clear();
            ConfigManager.IsTouchscreenMode = previousMode;
            ConfigManager.EnableEnvironmentFilter = previousFilter;
        }
    }

    [Fact]
    public void CrossScreenTrailHistory_IsBoundedAndRestartsItsAgeAfterIdle()
    {
        using var manager = new OverlayManager();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var record = typeof(OverlayManager).GetMethod("RecordTrailSample", members)!
            .CreateDelegate<Action<int, int, long>>(manager);
        var trim = typeof(OverlayManager).GetMethod("TrimTrailHistory", members)!.CreateDelegate<Action<long>>(manager);
        var clear = typeof(OverlayManager).GetMethod("ClearTrailHistory", members)!.CreateDelegate<Action>(manager);
        var history = (Queue<TrailSample>)typeof(OverlayManager).GetField("_trailHistory", members)!.GetValue(manager)!;
        long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int index = 0; index < 2048; index++) record(index, -index, timestamp + index);
        Assert.Equal(1024, history.Count);
        Assert.Equal(1024, history.Peek().ScreenX);
        long resumed = timestamp + System.Diagnostics.Stopwatch.Frequency * 3;
        trim(resumed);
        TrailSample sample = Assert.Single(history);
        Assert.Equal(2047, sample.ScreenX);
        Assert.Equal(-2047, sample.ScreenY);
        Assert.Equal(resumed, sample.Timestamp);
        clear();
        Assert.Empty(history);
        Assert.Null(typeof(OverlayManager).GetField("_lastTrailSample", members)!.GetValue(manager));
    }

    [Theory]
    [InlineData(-100, 50, 200, 50, true)]
    [InlineData(50, -100, 50, 200, true)]
    [InlineData(-100, -100, 200, 200, true)]
    [InlineData(-100, -100, -10, 200, false)]
    [InlineData(-100, 200, 200, 110, false)]
    [InlineData(0, 50, 0, 50, true)]
    [InlineData(int.MinValue, 50, int.MaxValue, 50, true)]
    public void CrossScreenTrailSegment_OnlyRoutesToIntersectedScreens(
        int startX, int startY, int endX, int endY, bool expected)
    {
        var overlay = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
        typeof(OverlayWindow).GetField("_screenBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(overlay, new NativeMethods.RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 });
        Assert.Equal(expected, overlay.IntersectsScreenSegment(startX, startY, endX, endY));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossScreenTrailRouting_KeepsDepartedScreensUntilTheirTailsExpire(bool pressed)
    {
        using var manager = new OverlayManager();
        using var firstHost = new LayeredWindowHost();
        using var secondHost = new LayeredWindowHost();
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        OverlayWindow CreateOverlay(int left, LayeredWindowHost host)
        {
            host.SetBounds(left, 0, 400, 600);
            var overlay = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
            typeof(OverlayWindow).GetField("_host", members)!.SetValue(overlay, host);
            typeof(OverlayWindow).GetField("_screenBounds", members)!.SetValue(overlay,
                new NativeMethods.RECT { Left = left, Top = 0, Right = left + 400, Bottom = 600 });
            return overlay;
        }
        OverlayWindow first = CreateOverlay(0, firstHost);
        OverlayWindow second = CreateOverlay(400, secondHost);
        var overlays = (Dictionary<string, OverlayWindow>)typeof(OverlayManager).GetField("_overlays", members)!.GetValue(manager)!;
        var participants = (Dictionary<OverlayWindow, long>)typeof(OverlayManager).GetField("_trailParticipants", members)!.GetValue(manager)!;
        var move = typeof(OverlayManager).GetMethod("RouteTrailMove", members)!
            .CreateDelegate<Action<OverlayWindow, int, int, bool>>(manager);
        var clear = typeof(OverlayManager).GetMethod("ClearTrailHistory", members)!.CreateDelegate<Action>(manager);
        var release = typeof(OverlayManager).GetMethod("ReleaseTrailParticipants", members)!.CreateDelegate<Action<bool, bool>>(manager);
        typeof(OverlayManager).GetField("_isPrimaryPointerDown", members)!.SetValue(manager, pressed);
        overlays.Add("first", first);
        overlays.Add("second", second);
        try
        {
            move(first, 100, 300, false);
            move(first, 390, 300, false);
            move(second, 410, 300, false);
            Assert.Equal(2, participants.Count);
            long departure = participants[first];
            move(second, 550, 300, false);
            Assert.Equal(departure, participants[first]);
            Assert.Equal(first.TrailStrokeId, second.TrailStrokeId);
            release(false, true);
            Assert.Equal(2, participants.Count);
            Assert.True((bool)typeof(OverlayManager).GetField("_resumeTrailParticipants", members)!.GetValue(manager)!);
            move(second, 600, 300, false);
            Assert.False((bool)typeof(OverlayManager).GetField("_resumeTrailParticipants", members)!.GetValue(manager)!);
            Assert.Equal(2, participants.Count);
            move(first, 350, 300, false);
            Assert.Equal(2, participants.Count);
            Assert.True(participants[first] >= departure);
            move(second, 550, 300, false);
            participants[first] = System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency * 3;
            move(second, 650, 300, false);
            Assert.False(participants.ContainsKey(first));
            Assert.True(participants.ContainsKey(second));
            clear();
            Assert.Empty(participants);
        }
        finally
        {
            overlays.Clear();
        }
    }

    [Fact]
    public void OverlayHost_RestoresBoundsAfterExternalWindowMovement()
    {
        using var host = new LayeredWindowHost();
        host.SetBounds(120, 130, 400, 240);
        Assert.True(NativeMethods.SetWindowPos(host.Handle, IntPtr.Zero, 180, 190, 320, 200,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER));

        host.SetBounds(120, 130, 400, 240);
        Assert.True(host.TryGetWindowRect(out NativeMethods.RECT bounds));
        Assert.Equal(120, bounds.Left);
        Assert.Equal(130, bounds.Top);
        Assert.Equal(400, bounds.Width);
        Assert.Equal(240, bounds.Height);
    }

    [Fact]
    public void OverlayRouting_RefreshesExistingWindowsWhenDisplayCoordinatesChange()
    {
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var firstHost = new LayeredWindowHost();
        using var secondHost = new LayeredWindowHost();
        using var manager = new OverlayManager();
        manager.UpdateInputSamplingRate(40);
        var first = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
        var second = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
        typeof(OverlayWindow).GetField("_host", members)!.SetValue(first, firstHost);
        typeof(OverlayWindow).GetField("_host", members)!.SetValue(second, secondHost);
        first.UpdateScreen(new ScreenInfo("test-first", 0, 0, 640, 360));
        second.UpdateScreen(new ScreenInfo("test-second", 640, -100, 800, 450));
        var overlays = (Dictionary<string, OverlayWindow>)typeof(OverlayManager).GetField("_overlays", members)!.GetValue(manager)!;
        overlays.Add("test-first", first);
        overlays.Add("test-second", second);
        var synchronize = typeof(OverlayManager).GetMethod("SynchronizeWindows", members)!
            .CreateDelegate<Action<IReadOnlyDictionary<string, ScreenInfo>, bool>>(manager);
        var resolve = typeof(OverlayManager).GetMethod("ResolveTargetOverlay", members)!
            .CreateDelegate<Func<int, int, OverlayWindow?>>(manager);
        System.Reflection.MethodInfo convert = typeof(OverlayWindow).GetMethod("TryConvertScreenToOverlayPoint", members)!;
        ScreenInfo[][] layouts =
        [
            [new("test-first", -640, 100, 640, 360), new("test-second", 0, 0, 800, 450)],
            [new("test-first", 0, -360, 640, 360), new("test-second", 0, 0, 800, 450)],
            [new("test-first", -800, -100, 800, 450), new("test-second", 0, 0, 640, 360)]
        ];
        try
        {
            foreach (ScreenInfo[] screens in layouts)
            {
                typeof(OverlayManager).GetField("_activePointerOverlay", members)!.SetValue(manager, first);
                typeof(OverlayManager).GetField("_lastTrailOverlay", members)!.SetValue(manager, second);
                typeof(OverlayManager).GetField("_lastTrailThrottleOverlay", members)!.SetValue(manager, first);
                typeof(OverlayManager).GetField("_isPrimaryPointerDown", members)!.SetValue(manager, true);
                typeof(OverlayManager).GetField("_lastMoveTimestamp", members)!.SetValue(manager, 123L);
                synchronize(screens.ToDictionary(screen => screen.DeviceName), false);

                Assert.Same(first, overlays["test-first"]);
                Assert.Same(second, overlays["test-second"]);
                Assert.Null(typeof(OverlayManager).GetField("_activePointerOverlay", members)!.GetValue(manager));
                Assert.Null(typeof(OverlayManager).GetField("_lastTrailOverlay", members)!.GetValue(manager));
                Assert.Null(typeof(OverlayManager).GetField("_lastTrailThrottleOverlay", members)!.GetValue(manager));
                Assert.Equal(false, typeof(OverlayManager).GetField("_isPrimaryPointerDown", members)!.GetValue(manager));
                Assert.Equal(0L, typeof(OverlayManager).GetField("_lastMoveTimestamp", members)!.GetValue(manager));
                foreach (ScreenInfo screen in screens)
                {
                    OverlayWindow overlay = overlays[screen.DeviceName];
                    Assert.True(overlay.MatchesScreenBounds(screen));
                    int horizontal = screen.BoundsLeft + screen.BoundsWidth / 2;
                    int vertical = screen.BoundsTop + screen.BoundsHeight / 2;
                    Assert.Same(overlay, resolve(horizontal, vertical));
                    Assert.True(overlay.ContainsScreenPoint(screen.BoundsLeft, screen.BoundsTop));
                    Assert.False(overlay.ContainsScreenPoint(screen.BoundsLeft + screen.BoundsWidth, vertical));
                    object?[] arguments = [horizontal, vertical, null];
                    Assert.Equal(true, convert.Invoke(overlay, arguments));
                    var point = Assert.IsType<Windows.Foundation.Point>(arguments[2]);
                    Assert.Equal(0.5, point.X, 8);
                    Assert.Equal(0.5, point.Y, 8);
                    Assert.Equal(Math.Max(1, System.Diagnostics.Stopwatch.Frequency / 40), overlay.TrailMoveIntervalTimestamp);
                }
            }
        }
        finally
        {
            overlays.Clear();
        }
    }

    [Fact]
    public void OverlayRouting_ReusesBoundsWithoutAllocatingDuringPointerMoves()
    {
        var overlay = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
        typeof(OverlayWindow).GetField("_screenBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(overlay, new NativeMethods.RECT { Left = -1920, Top = -100, Right = 0, Bottom = 980 });
        Assert.True(overlay.ContainsScreenPoint(-1920, -100));
        Assert.False(overlay.ContainsScreenPoint(0, 0));
        Assert.False(overlay.ContainsScreenPoint(-1, 980));
        using var manager = new OverlayManager();
        var overlays = (Dictionary<string, OverlayWindow>)typeof(OverlayManager)
            .GetField("_overlays", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!;
        overlays.Add("test-display", overlay);
        var resolve = typeof(OverlayManager).GetMethod("ResolveTargetOverlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Func<int, int, OverlayWindow?>>(manager);
        Assert.Same(overlay, resolve(-100, 0));
        for (int index = 0; index < 10000; index++) resolve(-100 - index % 1000, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool sameTarget = true;
        for (int index = 0; index < 10000; index++) sameTarget &= ReferenceEquals(overlay, resolve(-100 - index % 1000, 0));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        overlays.Clear();
        Assert.True(sameTarget);
        Assert.Equal(0, allocated);
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

        int messageIndex = overlaySource.IndexOf("private void OnWebMessageReceived", StringComparison.Ordinal);
        Assert.True(messageIndex > initIndex, "Renderer message handler is missing.");
        int messageEnd = overlaySource.IndexOf("private static string? GetJsonString", messageIndex, StringComparison.Ordinal);
        Assert.Contains("EnsureHostPresented()", overlaySource[messageIndex..messageEnd], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Overlay_StartupAndCaptureStateChangesCannotShowAnUnpreparedHost(bool rendererReady, bool controllerInitialized)
    {
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var host = new LayeredWindowHost();
        host.SetBounds(120, 120, 64, 64);
        var overlay = (OverlayWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OverlayWindow));
        typeof(OverlayWindow).GetField("_host", members)!.SetValue(overlay, host);
        typeof(OverlayWindow).GetField("_rendererReady", members)!.SetValue(overlay, rendererReady);
        typeof(OverlayWindow).GetField("_controllerInitialized", members)!.SetValue(overlay, controllerInitialized);

        overlay.RefreshPauseState();
        Assert.False(host.IsVisible);
        overlay.SetHiddenForExternalScreenshotCapture(true);
        overlay.SetHiddenForExternalScreenshotCapture(false);
        Assert.False(host.IsVisible);
        typeof(OverlayWindow).GetMethod("EnsureHostPresented", members)!.CreateDelegate<Action>(overlay)();
        Assert.False(host.IsVisible);
        Assert.False((bool)typeof(OverlayWindow).GetField("_webViewReadyForTopmost", members)!.GetValue(overlay)!);
    }

    [Fact]
    public void SettingsSubNav_UsesNativeHierarchicalNavigationAndItsSubtreeTransitions()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement navigation = GetNamedElement(panel, "SidebarNavigation");
        Assert.Equal("NavigationView", navigation.Name.LocalName);
        Assert.Equal("Left", (string?)navigation.Attribute("PaneDisplayMode"));
        Assert.Equal("False", (string?)navigation.Attribute("IsPaneToggleButtonVisible"));
        Assert.Equal("False", (string?)navigation.Attribute("IsTitleBarAutoPaddingEnabled"));
        XElement items = navigation.Element(Presentation + "NavigationView.MenuItems")!;
        Assert.Equal(new[] { "TabWelcome", "TabSettings", "TabLog", "TabAbout" }, items.Elements().Select(item => (string?)item.Attribute(Xaml + "Name")));
        Assert.DoesNotContain("SubNavAnimator", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "SettingsSubNavHost");
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
        Assert.Contains("DwmExtendFrameIntoClientArea", chromeSource, StringComparison.Ordinal);
        Assert.Contains("WindowChrome.ApplyNativeShadow(_hwnd)", hostSource, StringComparison.Ordinal);
        string startupSource = ReadSource("src", "StartupDialogHost.cs");
        Assert.Contains("SetDwmFlag(handle, DwmwaTransitionsForcedDisabled, false)", startupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimateWindow", startupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StartAnimation", startupSource, StringComparison.Ordinal);
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
        Assert.Contains("_host = new DcompPanelHost(minimumWidth: LanguageMinimumWidth)", source, StringComparison.Ordinal);
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
        Assert.Contains("IsMaximized || _fixedSize ? []", hostSource, StringComparison.Ordinal);
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
                XElement accent = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentBrush");
                Assert.Equal("AccentFillColorDefaultBrush", (string?)accent.Attribute("ResourceKey"));
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
        Assert.Equal("8,0,0,0", (string?)surface.Attribute("CornerRadius"));
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
            "CheckApplyCurveDraw", "SliderTrailSamplingRate",
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
            Assert.Equal(name == "ComboProfiles" ? "Right" : "Stretch", (string?)controls.Attribute("HorizontalAlignment"));
            Assert.Null(controls.Attribute("MaxWidth"));
            XElement columns = Assert.Single(layout.Elements(), element => element.Name.LocalName == "Grid.ColumnDefinitions");
            XElement controlColumn = columns.Elements().Last();
            if (control.Name.LocalName == "RadioButtons")
            {
                Assert.Equal("Auto", (string?)controlColumn.Attribute("Width"));
                Assert.Null(controlColumn.Attribute("MaxWidth"));
                Assert.Equal("140", (string?)controlColumn.Attribute("MinWidth"));
            }
            else if (name is "ComboLanguage" or "ComboProfiles")
            {
                Assert.Equal("Auto", (string?)controlColumn.Attribute("Width"));
                Assert.Null(controlColumn.Attribute("MaxWidth"));
                Assert.Null(controlColumn.Attribute("MinWidth"));
            }
            else if (control.Name.LocalName != "ToggleSwitch")
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
        Assert.Equal("64", setters["MinHeight"]);
        Assert.Equal("16,12", setters["Padding"]);
        Assert.Equal("0,0,0,4", setters["Margin"]);
    }

    [Theory]
    [InlineData("CheckScreenshotCompatibilityMode", "TxtScreenshotHint")]
    [InlineData("CheckRunAsAdmin", "TxtRunAsAdminHint")]
    [InlineData("CheckTouchscreenMode", "TxtTouchscreenHint")]
    [InlineData("CheckLinkedEffectScale", "TxtLinkedScaleHint")]
    [InlineData("CheckLinkedAnimationSpeed", "TxtLinkedSpeedHint")]
    [InlineData("CheckApplyCurveDraw", "TxtCurveDrawHint")]
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
        Assert.Equal(count, container.Elements(Presentation + "RadioButton").Count());
        Assert.All(container.Elements(Presentation + "RadioButton"), item =>
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
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement segment = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSegmentedRadioStyle");
        Assert.Equal("{StaticResource DefaultToggleButtonStyle}", (string?)segment.Attribute("BasedOn"));
        Assert.DoesNotContain(segment.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        Assert.Equal("{ThemeResource BasSettingsAccentBrush}", (string?)GetNamedElement(styles, "SegmentedSelection").Attribute("Background"));
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "RadioDarkMode", "RadioClickType" })
            Assert.Contains(GetNamedElement(panel, name).Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "ToggleButtonBackgroundChecked" && (string?)element.Attribute("Color") == "Transparent");
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
            if (pageName == "PageAbout")
            {
                Assert.Null(page.Attribute("Padding"));
                Assert.Equal("24,24,24,0", (string?)GetNamedElement(document, titleName).Parent!.Attribute("Margin"));
            }
            else if (pageName != "PageSettings")
            {
                Assert.Equal("{StaticResource BasPagePadding}", (string?)page.Attribute("Padding"));
            }
        }
    }

    [Fact]
    public void SegmentedSelectors_FillMeasuredCellsWithoutHeaderOrColumnGaps()
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
        Assert.Contains(style.Elements(), element => (string?)element.Attribute("Property") == "Width" && (string?)element.Attribute("Value") == "40");
        Assert.Equal("{StaticResource DefaultToggleSwitchStyle}", (string?)style.Attribute("BasedOn"));
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
        Assert.Equal("DropDownButton", processes.Name.LocalName);
        XElement flyout = GetNamedElement(document, "ConfiguredProcessesFlyout");
        Assert.Equal("Flyout", flyout.Name.LocalName);
        Assert.Equal("BottomEdgeAlignedRight", (string?)flyout.Attribute("Placement"));
        Assert.Equal("{StaticResource DefaultFlyoutPresenterStyle}", (string?)Assert.Single(flyout.Descendants(Presentation + "Style")).Attribute("BasedOn"));
        Assert.Equal("200", (string?)GetNamedElement(document, "ConfiguredProcessScroller").Attribute("MaxHeight"));
        Assert.Equal("ItemsControl", GetNamedElement(document, "ConfiguredProcessItems").Name.LocalName);
        XElement remove = Assert.Single(processes.Descendants(), element => (string?)element.Attribute("Click") == "RemoveProcess_Click");
        Assert.Equal("{Binding}", (string?)remove.Attribute("Tag"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("ConfiguredProcessItems.ItemsSource = CurrentProfileProcesses", source, StringComparison.Ordinal);
        Assert.Contains("ListConfiguredProcesses.Content = Localization.Format(\"Filter_SelectedProcesses\", CurrentProfileProcesses.Count)", source, StringComparison.Ordinal);
        Assert.Contains("ListConfiguredProcesses.IsEnabled = processFilterEnabled && CurrentProfileProcesses.Count > 0", source, StringComparison.Ordinal);
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
        Assert.Equal("NavigationViewItem", parent.Name.LocalName);
        XElement children = parent.Element(Presentation + "NavigationViewItem.MenuItems")!;
        Assert.Equal(new[] { "SubTabBasic", "SubTabVisual", "SubTabFilter", "SubTabMultiScreen", "SubTabBackup" }, children.Elements().Select(item => (string?)item.Attribute(Xaml + "Name")));
        foreach (XElement item in children.Elements().Prepend(parent))
        {
            Assert.Null(item.Attribute("Style"));
            Assert.Null(item.Attribute("Margin"));
            Assert.Equal("NavigationViewItem", item.Name.LocalName);
        }
    }

    [Fact]
    public void SettingsSubNavigation_AnimatesItsAccentLikeTheParent()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement navigation = GetNamedElement(panel, "SidebarNavigation");
        Assert.Null(navigation.Attribute("Style"));
        Assert.DoesNotContain(navigation.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        Assert.DoesNotContain(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") is "BasNavRadioStyle" or "BasSubNavRadioStyle");
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
        Assert.Equal(16, presets.Elements().Count());
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
        Assert.Contains("private const int Columns = 8", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        XElement details = GetNamedElement(document, "ColorPickerDetails");
        Assert.Null(details.Attribute("Width"));
        Assert.Equal("Auto", (string?)details.Parent!.Element(Presentation + "Grid.ColumnDefinitions")!.Elements().Last().Attribute("Width"));
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
        styleKeys.UnionWith(new[] { "DefaultContentDialogStyle", "DefaultToggleButtonStyle", "DefaultInfoBarStyle", "DefaultToggleSwitchStyle", "DefaultHyperlinkButtonStyle", "DefaultComboBoxItemStyle", "DefaultFlyoutPresenterStyle", "DefaultExpanderStyle", "SubtleButtonStyle",
            "TitleTextBlockStyle", "SubtitleTextBlockStyle", "BodyTextBlockStyle", "BodyStrongTextBlockStyle", "BodyLargeTextBlockStyle", "CaptionTextBlockStyle" });

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
        XElement navigation = GetNamedElement(document, "SidebarNavigation");
        Assert.Equal(4, navigation.Element(Presentation + "NavigationView.MenuItems")!.Elements().Count());
        Assert.All(navigation.Element(Presentation + "NavigationView.MenuItems")!.Elements(), item => Assert.Null(item.Attribute("Margin")));
        XElement close = GetNamedElement(document, "BtnCaptionClose");
        Assert.Equal("#00E81123", (string?)close.Attribute("Background"));
        Assert.Contains(close.Descendants(), resource => (string?)resource.Attribute(Xaml + "Key") == "ButtonBackgroundPointerOver" && (string?)resource.Attribute("Color") == "#E81123");
        Assert.Contains(close.Descendants(), resource => (string?)resource.Attribute(Xaml + "Key") == "ButtonForegroundPointerOver" && (string?)resource.Attribute("Color") == "White");
    }

    [Fact]
    public void ProcessDropdown_KeepsTheCompactCardHeightWhenOpened()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement dropdown = GetNamedElement(document, "ListConfiguredProcesses");
        Assert.Equal("32", (string?)dropdown.Attribute("Height"));
        Assert.Equal("Center", (string?)dropdown.Attribute("VerticalContentAlignment"));
        Assert.Equal("11,0,13,0", (string?)dropdown.Attribute("Padding"));
        XElement remove = Assert.Single(dropdown.Descendants(), element => (string?)element.Attribute("Click") == "RemoveProcess_Click");
        Assert.Equal("20", (string?)remove.Attribute("Height"));
        Assert.Equal("0", (string?)remove.Attribute("MinHeight"));
        Assert.Equal("0", (string?)remove.Attribute("Padding"));
        Assert.Equal("Center", (string?)remove.Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)remove.Attribute("VerticalContentAlignment"));
        Assert.Equal("Center", (string?)remove.Parent!.Attribute("VerticalAlignment"));
        Assert.Equal("11,6,11,6", (string?)remove.Parent!.Attribute("Padding"));
    }

    [Fact]
    public void ControlPanel_MinimumWidthAccommodatesAllSettingsControls()
    {
        Assert.Contains("MinWidthDesign = 800", ReadSource("src", "DcompPanelHost.cs"), StringComparison.Ordinal);
        Assert.Contains("DesignWidth = 800", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        var minimumWidth = typeof(ControlPanelWindow).GetProperty("LanguageMinimumWidth",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        string originalCulture = Localization.CurrentCultureName;
        try
        {
            foreach (var setting in new[] { (Localization.CultureZhCn, 800), (Localization.CultureEn, 900), (Localization.CultureJa, 900) })
            {
                Localization.ApplyCulture(setting.Item1);
                Assert.Equal(setting.Item2, minimumWidth.GetValue(null));
            }
        }
        finally
        {
            Localization.ApplyCulture(originalCulture);
        }
    }

    [Fact]
    public void SettingsText_UsesNativeHeadingAndBodyWeightsAndNoScrollbarPreferenceOrLeadingHintStars()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        foreach (string key in new[] { "BasPageTitleStyle", "BasSectionTitleStyle" })
        {
            XElement style = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == key);
            Assert.Contains((string?)style.Attribute("BasedOn"), new[] { "{StaticResource TitleTextBlockStyle}", "{StaticResource SubtitleTextBlockStyle}", "{StaticResource BodyStrongTextBlockStyle}" });
        }
        XElement body = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingTitleStyle");
        Assert.Equal("{StaticResource BodyTextBlockStyle}", (string?)body.Attribute("BasedOn"));

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
        XElement header = GetNamedElement(document, "ExpanderHeader");
        Assert.Equal("{StaticResource ExpanderHeaderDownStyle}", (string?)header.Attribute("Style"));
        Assert.Equal("Stretch", (string?)header.Attribute("HorizontalContentAlignment"));
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
        string animation = ReadSource("src", "ControlPanelWindow.Accent.cs");
        Assert.Contains("CreateRoundedRectangleGeometry", animation, StringComparison.Ordinal);
        Assert.Contains("_geometry.StartAnimation(\"Size.Y\", animation)", animation, StringComparison.Ordinal);
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
        Assert.Contains("CreateGeometricClip", ReadSource("src", "ControlPanelWindow.Accent.cs"), StringComparison.Ordinal);
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
        Assert.Equal(9, about.Descendants().Count(element => (string?)element.Attribute("Style") == "{StaticResource BasSettingCardStyle}"));
        Assert.DoesNotContain(about.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "BtnResetAll");
        Assert.Equal("{StaticResource BasCaptionStyle}", (string?)GetNamedElement(panel, "AboutVersionText").Attribute("Style"));
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
    public void BuildInformation_ReadsTheVersionFullCommitAndTimestampEmbeddedDuringCompilation()
    {
        Assert.Matches(@"^V\d+\.\d+\.\d+", BuildInformation.Version);
        Assert.Matches(@"^[0-9a-fA-F]{40}$", BuildInformation.CommitHash);
        long timestamp = Assert.IsType<long>(BuildInformation.UnixTimestamp);
        Assert.InRange(timestamp, 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);
    }

    [Theory]
    [InlineData(0, "1970-01-01 00:00:00 UTC+0")]
    [InlineData(480, "1970-01-01 08:00:00 UTC+8")]
    [InlineData(-480, "1969-12-31 16:00:00 UTC-8")]
    [InlineData(330, "1970-01-01 05:30:00 UTC+5:30")]
    [InlineData(-30, "1969-12-31 23:30:00 UTC-0:30")]
    public void BuildInformation_ConvertsUtcTimestampsToTheRequestedSystemTimeZone(int offsetMinutes, string expected)
    {
        TimeZoneInfo timeZone = TimeZoneInfo.CreateCustomTimeZone("BuildTime", TimeSpan.FromMinutes(offsetMinutes), "BuildTime", "BuildTime");
        Assert.Equal(expected, BuildInformation.FormatBuildTime(0, timeZone));
    }

    [Fact]
    public void BuildInformation_CommitLinksUseTheFullHashInTheRequestedRepository()
    {
        const string commitHash = "0123456789abcdef0123456789abcdef01234567";
        Assert.Equal("https://github.com/Hdy08/BASpark/commit/" + commitHash, BuildInformation.GetCommitUrl(commitHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("f541903")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    public void BuildInformation_UnavailableOrInvalidCommitsDoNotCreateMisleadingLinks(string commitHash)
    {
        Assert.Null(BuildInformation.GetCommitUrl(commitHash));
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
            XElement link = Assert.Single(card.Descendants(Presentation + "HyperlinkButton"));
            Assert.Equal(url, (string?)link.Attribute("NavigateUri"));
            Assert.Equal("{Binding Tag, ElementName=" + name + "}", (string?)link.Attribute("Content"));
            Assert.Equal("{StaticResource BasRepositoryLinkStyle}", (string?)link.Attribute("Style"));
            Assert.Contains("SetActionIcon(" + name + ", \"\\uE774\", \"About_OpenRepository\")", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
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
        Assert.Contains("HideTrayIcon = CheckHideTrayIcon.IsOn", source, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, object> values = current.GetValues(page)", source, StringComparison.Ordinal);
        Assert.Contains("ConfigManager.SaveBackupValues(values)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPageRuntimeSettings(page, screensChanged)", source, StringComparison.Ordinal);
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("if (page == SettingsSections.Basic)", pages, StringComparison.Ordinal);
        Assert.Contains("App.Tray?.SetHidden(ConfigManager.HideTrayIcon)", pages, StringComparison.Ordinal);
        foreach (bool hideTrayIcon in new[] { false, true })
        {
            var state = new SettingsState(System.Text.Json.JsonSerializer.Serialize(new { HideTrayIcon = hideTrayIcon }), "{}", "[]", "{}");
            Assert.Equal(hideTrayIcon, Assert.IsType<bool>(state.GetValues(SettingsSections.Basic)["HideTrayIcon"]));
            Assert.DoesNotContain("HideTrayIcon", state.GetValues(SettingsSections.Visual).Keys);
        }
        string tray = ReadSource("src", "TrayIconController.cs");
        Assert.Contains("Visible = !ConfigManager.HideTrayIcon", tray, StringComparison.Ordinal);
        Assert.Contains("TrayMessageWindow(Action openPanel) : Form", tray, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false", tray, StringComparison.Ordinal);
        Assert.Contains("FindWindow(null, HiddenTrayWindowTitle)", tray, StringComparison.Ordinal);
        Assert.Contains("FindWindow(null, TrayWindowTitle)", tray, StringComparison.Ordinal);
        Assert.Contains("TrayIconController.TryShowExistingControlPanel()", ReadSource("src", "App.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ResetActions_UseOneSearchableCardListWithAllFourSettingsGroups()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement overlay = GetNamedElement(panel, "VisualResetOverlay");
        Assert.Equal("ContentDialog", overlay.Name.LocalName);
        Assert.Equal("{StaticResource BasContentDialogStyle}", (string?)overlay.Attribute("Style"));
        foreach (string name in new[] { "TxtOverlayVisualReset", "SearchVisualReset", "ListVisualResetItems", "BtnOverlayVisualCancel", "BtnOverlayVisualConfirm" })
            Assert.Contains(overlay, GetNamedElement(panel, name).Ancestors());
        Assert.Equal("{StaticResource AccentButtonStyle}", (string?)GetNamedElement(panel, "BtnOverlayVisualConfirm").Attribute("Style"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        foreach (string group in new[] { "Basic", "Visual", "Filter", "Screen" })
            Assert.Contains("Rebuild" + group + "ResetItems()", source, StringComparison.Ordinal);
        Assert.Contains("item.Group.Contains(filter", source, StringComparison.Ordinal);
        Assert.Contains("SetGroupedSelectionRows(ListVisualResetItems", source, StringComparison.Ordinal);
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("items.GroupBy(groupName)", pages, StringComparison.Ordinal);
        Assert.Contains("rows.AddRange(heading.Items)", pages, StringComparison.Ordinal);
        Assert.Contains("list.ItemsSource = rows", pages, StringComparison.Ordinal);
        XElement card = GetNamedElement(panel, "SelectionCard");
        Assert.Equal("{StaticResource BasSettingCardStyle}", (string?)card.Attribute("Style"));
        Assert.Equal("SelectionCard_Loaded", (string?)card.Attribute("Loaded"));
        Assert.Equal("SelectionCard_DataContextChanged", (string?)card.Attribute("DataContextChanged"));
        Assert.Contains(card.Descendants(), element => (string?)element.Attribute("Style") == "{StaticResource BasSettingTitleStyle}");
        Assert.Contains(card.Descendants(), element => (string?)element.Attribute("Style") == "{StaticResource BasCaptionStyle}");
        XElement checkHost = GetNamedElement(panel, "SelectionCardCheckHost");
        Assert.Equal("Right", (string?)checkHost.Attribute("HorizontalAlignment"));
        Assert.Equal("1", (string?)checkHost.Attribute("Grid.Column"));
        Assert.Empty(checkHost.Elements());
        Assert.Contains("check.SetBinding(CheckBox.IsCheckedProperty, new Binding", pages, StringComparison.Ordinal);
        Assert.Contains("Source = item", pages, StringComparison.Ordinal);
        Assert.Contains("Path = new PropertyPath(nameof(SelectionCardItem.IsSelected))", pages, StringComparison.Ordinal);
        Assert.Contains("Mode = BindingMode.TwoWay", pages, StringComparison.Ordinal);
        Assert.Contains("check.ClearValue(CheckBox.IsCheckedProperty)", pages, StringComparison.Ordinal);
        Assert.Contains("IsChecked = item.IsSelected", pages, StringComparison.Ordinal);
        Assert.Contains("ClearSelectionCardCheck(host)", pages, StringComparison.Ordinal);
        Assert.Contains("host.Child = check", pages, StringComparison.Ordinal);
        int binding = pages.IndexOf("check.SetBinding(CheckBox.IsCheckedProperty", StringComparison.Ordinal);
        Assert.True(pages.IndexOf("check.ApplyTemplate()", binding, StringComparison.Ordinal) <
            pages.IndexOf("host.Child = check", binding, StringComparison.Ordinal));
        int detached = pages.IndexOf("host.Child = null", StringComparison.Ordinal);
        Assert.True(detached < pages.IndexOf("check.ClearValue(CheckBox.IsCheckedProperty)", detached, StringComparison.Ordinal));
        Assert.Contains("UpdateSelectionCardBinding(sender)", pages, StringComparison.Ordinal);
        Assert.Contains("if (!ReferenceEquals(row.DataContext, item)) row.DataContext = item", pages, StringComparison.Ordinal);
        Assert.Contains("GetRowElement(context, index)", pages, StringComparison.Ordinal);
        Assert.Contains("GetRowElement(context, anchor)", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionCheck_StateChanged", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("glyph.Source = null", pages, StringComparison.Ordinal);
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
    public void PopupSelectionLists_UseStableScrollRangesAndToggleWholeCards()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "ListVisualResetItems", "ListRunningProcesses", "ListBackupItems" })
        {
            XElement list = GetNamedElement(panel, name);
            Assert.Equal("ItemsControl", list.Name.LocalName);
            Assert.Equal("{StaticResource SelectionCardTemplates}", (string?)list.Attribute("ItemTemplateSelector"));
            Assert.Equal("{StaticResource SelectionListStyle}", (string?)list.Attribute("Style"));
            Assert.Equal("ScrollViewer", list.Parent!.Name.LocalName);
            Assert.Equal("0,0,-16,0", (string?)list.Parent.Attribute("Margin"));
            Assert.Equal("Auto", (string?)list.Parent.Attribute("VerticalScrollBarVisibility"));
            Assert.Empty(list.Elements());
        }
        XElement style = Assert.Single(panel.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "SelectionListStyle");
        XElement repeater = Assert.Single(style.Descendants(), element => element.Name.LocalName == "ItemsRepeater");
        Assert.Equal("{TemplateBinding ItemsSource}", (string?)repeater.Attribute("ItemsSource"));
        Assert.Equal("{TemplateBinding ItemTemplateSelector}", (string?)repeater.Attribute("ItemTemplate"));
        Assert.Single(repeater.Descendants(), element => element.Name.LocalName == "SelectionCardLayout");
        XElement card = GetNamedElement(panel, "SelectionCard");
        Assert.Equal("SelectionCard_PointerPressed", (string?)card.Attribute("PointerPressed"));
        Assert.Equal("SelectionCard_PointerReleased", (string?)card.Attribute("PointerReleased"));
        Assert.Null(card.Attribute("Tapped"));
        Assert.Null(card.Attribute("MinHeight"));
        Assert.Equal("0", (string?)card.Attribute("Margin"));
        Assert.Null(card.Attribute("Height"));
        XElement[] labels = card.Descendants(Presentation + "TextBlock").ToArray();
        Assert.Equal("{Binding Title}", (string?)labels[0].Attribute("Text"));
        Assert.Equal("{Binding Subtitle}", (string?)labels[1].Attribute("Text"));
        Assert.Equal("{Binding SubtitleVisibility}", (string?)labels[1].Attribute("Visibility"));
        XElement heading = GetNamedElement(panel, "SelectionHeading");
        Assert.Equal("{Binding TitleWithCount}", (string?)heading.Element(Presentation + "TextBlock")!.Attribute("Text"));
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        string itemsChanged = pages[pages.IndexOf("protected override void OnItemsChangedCore", StringComparison.Ordinal)..
            pages.IndexOf("internal SelectionGroup? GetActiveGroup", StringComparison.Ordinal)];
        Assert.Contains("context.RecycleElement(entry.Element)", itemsChanged, StringComparison.Ordinal);
        Assert.Contains("state.Realized.Clear()", itemsChanged, StringComparison.Ordinal);
        Assert.Contains("InvalidateMeasure()", itemsChanged, StringComparison.Ordinal);
        Assert.Contains("check.ApplyTemplate()", pages, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Resources[\"DefaultCheckBoxStyle\"]", pages, StringComparison.Ordinal);
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("if (source is CheckBox) return false", source, StringComparison.Ordinal);
        Assert.Contains("item.IsSelected = !item.IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Border { Child = layout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListRunningProcesses.SelectedItems", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PageScrollbars_KeepNativeTracksWithinTheViewportAndOutsideAboutCards()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement about = GetNamedElement(panel, "PageAbout");
        Assert.Null(about.Attribute("Padding"));
        Assert.Same(about, GetNamedElement(panel, "AboutScrollViewer").Parent);
        foreach ((string scrollName, string contentName) in new[]
                 { ("SettingsContentScrollViewer", "SettingsContent"), ("AboutScrollViewer", "AboutContent") })
        {
            XElement scroll = GetNamedElement(panel, scrollName);
            Assert.Equal("24,0,24,0", (string?)scroll.Attribute("Padding"));
            Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
            Assert.Null(scroll.Attribute("Style"));
            Assert.Null(scroll.Attribute("Template"));
            XElement content = GetNamedElement(panel, contentName);
            Assert.Same(scroll, content.Parent);
            Assert.Equal("0,0,0,24", (string?)content.Attribute("Padding"));
        }
    }

    [Fact]
    public void HeaderAndProcessButtons_UseContentWidthsAndNativeSizing()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement apply = GetNamedElement(panel, "BtnApplySettings");
        Assert.Null(apply.Attribute("Width"));
        Assert.Null(GetNamedElement(panel, "BtnResetSettings").Attribute("SizeChanged"));
        Assert.DoesNotContain("BtnApplySettings.Width = args.NewSize.Width", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        foreach (string name in new[] { "BtnResetSettings", "BtnApplySettings", "BtnExitApplication", "BtnRestartApplication", "BtnToggleEffectsPause", "BtnBasicReset", "BtnVisualReset", "BtnFilterReset", "BtnClearLog", "BtnCopyLog" })
        {
            XElement button = GetNamedElement(panel, name);
            Assert.Null(button.Attribute("Width"));
            Assert.Equal("0", (string?)button.Attribute("MinWidth"));
        }
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
        Assert.Contains("new[] { BtnResetSettings, BtnDeleteProfile, BtnExitApplication, BtnClearLog }", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
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
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "RunningProcessOverlay", "VisualResetOverlay", "BackupOverlay", "RenameProfileOverlay" })
        {
            XElement dialog = GetNamedElement(panel, name);
            Assert.Equal("ContentDialog", dialog.Name.LocalName);
            Assert.Equal("{StaticResource BasContentDialogStyle}", (string?)dialog.Attribute("Style"));
            Assert.Null(dialog.Attribute("Background"));
            Assert.DoesNotContain(dialog.Descendants(), element => element.Name.LocalName is "Storyboard" or "ControlTemplate");
        }
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("await dialog.ShowAsync(ContentDialogPlacement.Popup)", source, StringComparison.Ordinal);
        Assert.Contains("SuspendSelectionDialogAsync()", source, StringComparison.Ordinal);
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
            Assert.DoesNotContain(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") is "ContentDialogSmokeFill" or "BasModalScrimBrush");
            Assert.Contains(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "ContentDialogTopOverlay" && (string?)element.Attribute("ResourceKey") == "BasLayerBrush");
        }
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("dialog.Title = null", source, StringComparison.Ordinal);
        Assert.Contains("ShowMessageAsync(Localization.Get(_resetScope == ResetScope.Visual ? \"Msg_VisualResetDone\" : \"Msg_PageResetDone\"))", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[\"ContentDialogMinHeight\"] = 0d", source, StringComparison.Ordinal);
        Assert.Contains("PanelBody.TransformToVisual(RootGrid)", source, StringComparison.Ordinal);
        Assert.Contains("scrim.Clip = new RectangleGeometry", source, StringComparison.Ordinal);
        Assert.Contains("column.Width = GridLength.Auto", source, StringComparison.Ordinal);
        Assert.Contains("button.MinWidth = 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog.Resources[\"ContentDialogSmokeFill\"] = VisualResetOverlay.Background", source, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("scrim.Fill = VisualResetOverlay.Background", source, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog.Resources[\"ContentDialogTopOverlay\"] = ((Border)VisualResetOverlay.Children[0]).Background", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccentControls_UseSystemThemeResourcesWithoutOverridingNativeInteractionBrushes()
    {
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        foreach (XElement theme in styles.Root!.Element(Presentation + "ResourceDictionary.ThemeDictionaries")!.Elements().Where(element => (string?)element.Attribute(Xaml + "Key") is "Light" or "Dark"))
        {
            XElement accent = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentBrush");
            XElement foreground = Assert.Single(theme.Elements(), element => (string?)element.Attribute(Xaml + "Key") == "BasSettingsAccentTextBrush");
            Assert.Equal("AccentFillColorDefaultBrush", (string?)accent.Attribute("ResourceKey"));
            Assert.Equal("TextOnAccentFillColorPrimaryBrush", (string?)foreground.Attribute("ResourceKey"));
            foreach (XElement brush in theme.Elements().Where(element => ((string?)element.Attribute(Xaml + "Key"))?.StartsWith("BasAccentSubtle", StringComparison.Ordinal) == true))
                Assert.Equal("{ThemeResource SystemAccentColor}", (string?)brush.Attribute("Color"));
        }
        Assert.DoesNotContain(styles.Descendants(), element => ((string?)element.Attribute(Xaml + "Key"))?.StartsWith("AccentButton", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(styles.Descendants(), element => ((string?)element.Attribute(Xaml + "Key"))?.StartsWith("BasSettingsAccent", StringComparison.Ordinal) == true && element.Name.LocalName == "Color");
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string prefix in new[] { "ToggleSwitchFillOn", "ToggleSwitchKnobFillOn", "SliderTrackValueFill" })
            Assert.DoesNotContain(panel.Descendants(), element => ((string?)element.Attribute(Xaml + "Key"))?.StartsWith(prefix, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ColorDragging_CoalescesSnapshotsAndLimitsOnlyTextLayoutWork()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("if ((_isColorDragging || _pendingSliderFeedback.Count != 0) && sections == SettingsSections.Visual) return", source, StringComparison.Ordinal);
        Assert.Contains("if (_colorPreviewSubscribed) return", source, StringComparison.Ordinal);
        Assert.Contains("if (!_isColorDragging && _pendingSliderFeedback.Count == 0) StopColorPreviewRendering()", source, StringComparison.Ordinal);
        Assert.Contains("now - _lastColorLabelUpdate >= Stopwatch.Frequency / 20", source, StringComparison.Ordinal);
        Assert.Contains("if (_previewColorBrush.Color != color) _previewColorBrush.Color = color", source, StringComparison.Ordinal);
        Assert.Contains("_presetColorIndices.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("UIElement.PointerCaptureLostEvent", source, StringComparison.Ordinal);
        Assert.Contains("if ((_isColorDragging || sliderFeedbackPending) && _pendingSettingsSections != 0)", source, StringComparison.Ordinal);
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
        foreach (string name in new[] { "BtnBasicReset", "BtnVisualReset", "BtnFilterReset" })
        {
            Assert.Equal("{StaticResource BasInlineResetButtonStyle}", (string?)GetNamedElement(panel, name).Attribute("Style"));
            Assert.Single(GetNamedElement(panel, name).Elements(), item => item.Name.LocalName == "FontIcon");
        }
        Assert.Contains("Localization.Get(\"Settings_ResetPage\")", ReadSource("src", "ControlPanelWindow.Pages.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(panel.Descendants(), item => (string?)item.Attribute(Xaml + "Name") == "BtnScreensReset");
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
        Assert.Contains("ListVisualResetItems.Items.OfType<VisualResetItem>().Where(item => item.IsSelected)", confirm, StringComparison.Ordinal);
        Assert.Contains("foreach (VisualResetItem item in selected) item.Restore?.Invoke()", confirm, StringComparison.Ordinal);
        Assert.Contains("foreach (VisualResetItem item in selected) item.Save?.Invoke()", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_resetScope == ResetScope.Visual)", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSettings()", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeMessageBox", confirm, StringComparison.Ordinal);
        Assert.Contains("MarkResetSettingsSaved(selected)", confirm, StringComparison.Ordinal);
        Assert.Contains("visual[key] = currentVisual[key]?.DeepClone()", source, StringComparison.Ordinal);
        Assert.Contains("basic[key] = currentBasic[key]?.DeepClone()", source, StringComparison.Ordinal);
        Assert.Contains("filter[key] = currentFilter[key]?.DeepClone()", source, StringComparison.Ordinal);
        Assert.Contains("_particleColor = \"76,167,255\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySettings_RemainsDisabledUntilSettingsDifferFromTheSavedBaseline()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal("False", (string?)GetNamedElement(panel, "BtnApplySettings").Attribute("IsEnabled"));
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("InitializeSettingsTracking()", source, StringComparison.Ordinal);
        Assert.Contains("_currentSettingsState.HasChanges(_savedSettingsState, CurrentSettingsPage)", source, StringComparison.Ordinal);
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
        Assert.Contains("ConfigManager.SaveProfiles(Profiles.ToList()", source, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Deserialize<List<ScreenSelectionState>>(_savedSettingsState!.Screens)", source, StringComparison.Ordinal);
        Assert.Contains("if (!ConfigManager.Save(key, value)) throw", source, StringComparison.Ordinal);
        string config = ReadSource("src", "ConfigManager.cs");
        Assert.Contains("Processes = new List<string>(profile.Processes)", config, StringComparison.Ordinal);
        Assert.Contains("public static bool Save(string name, object value)", config, StringComparison.Ordinal);
        Assert.Contains("public static bool SaveProfiles", config, StringComparison.Ordinal);
        XDocument resources = LoadXaml("src", "Strings.resx");
        Assert.Equal("所选设置项已恢复为默认值并应用。", resources.Descendants("data").Single(item => (string?)item.Attribute("name") == "Msg_PageResetDone").Element("value")!.Value);
    }

    [Fact]
    public void RendererStartup_ReappliesSavedRuntimeSettingsAfterNavigationAndReadiness()
    {
        string source = ReadSource("src", "OverlayWindow.cs");
        Assert.Equal(2, source.Split("ApplySavedRendererSettings();", StringSplitOptions.None).Length - 1);
        string sync = source[source.IndexOf("private void ApplySavedRendererSettings()", StringComparison.Ordinal)..source.IndexOf("private void OnWebMessageReceived", StringComparison.Ordinal)];
        foreach (string setting in new[] { "ConfigManager.ParticleColor", "ConfigManager.EffectOpacity", "ConfigManager.GlowIntensity", "SetCurveDraw(ConfigManager.ApplyCurveDraw)", "SyncInputContext(InputModeMouse)" })
            Assert.Contains(setting, sync, StringComparison.Ordinal);
    }

    [Fact]
    public void UiRenderClock_CoversAllWindowsAndUsesSupportedNativeRefreshRequests()
    {
        string source = ReadSource("src", "DcompPanelHost.cs");
        string chrome = ReadSource("src", "WindowChrome.cs");
        Assert.Contains("OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)", chrome, StringComparison.Ordinal);
        Assert.Contains("DCompositionBoostCompositorClock(true)", chrome, StringComparison.Ordinal);
        Assert.Contains("DCompositionBoostCompositorClock(false)", chrome, StringComparison.Ordinal);
        Assert.Contains("TimeBeginPeriod(1) == 0", chrome, StringComparison.Ordinal);
        Assert.Contains("TimeEndPeriod(1)", chrome, StringComparison.Ordinal);
        Assert.Contains("new RenderClock(window)", chrome, StringComparison.Ordinal);
        Assert.Contains("WindowChrome.ApplyAppIcon(this)", ReadSource("src", "ColorPickerWindow.xaml.cs"), StringComparison.Ordinal);
        foreach (string window in new[] { "LanguageSelectWindow.xaml.cs", "PrivacyWindow.xaml.cs" })
            Assert.Contains("_host.SetContent(this)", ReadSource("src", window), StringComparison.Ordinal);
        Assert.Contains("SetRenderClockActive(IsWindowVisible(_hwnd) && !IsIconic(_hwnd))", source, StringComparison.Ordinal);
        Assert.Contains("case WmShowWindow:", source, StringComparison.Ordinal);
        Assert.Contains("case WmWindowPosChanged:", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split("SetRenderClockActive(false);", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void UiRenderClock_KeepsSharedRequestsUntilTheLastVisibleWindowIsReleased()
    {
        T ReadShared<T>(string name) => (T)typeof(WindowChrome.RenderClock)
            .GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;

        int initial = ReadShared<int>("_activeWindows");
        using var first = new WindowChrome.RenderClock();
        using var second = new WindowChrome.RenderClock();
        first.SetActive(true);
        first.SetActive(true);
        second.SetActive(true);
        Assert.True(first.IsActive);
        Assert.True(second.IsActive);
        Assert.Equal(initial + 2, ReadShared<int>("_activeWindows"));
        Assert.True(ReadShared<bool>("_precisionRequested"));
        first.SetActive(false);
        first.SetActive(false);
        Assert.False(first.IsActive);
        Assert.True(ReadShared<bool>("_precisionRequested"));
        Assert.Equal(initial + 1, ReadShared<int>("_activeWindows"));
        first.SetActive(true);
        second.Dispose();
        second.SetActive(true);
        Assert.False(second.IsActive);
        Assert.Equal(initial + 1, ReadShared<int>("_activeWindows"));
        first.Dispose();
        Assert.Equal(initial, ReadShared<int>("_activeWindows"));
        if (initial == 0)
        {
            Assert.False(ReadShared<bool>("_precisionRequested"));
            Assert.False(ReadShared<bool>("_boostRequested"));
        }
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
        XElement header = GetNamedElement(styles, "ExpanderHeader");
        Assert.Equal("{StaticResource ExpanderHeaderDownStyle}", (string?)header.Attribute("Style"));
        Assert.Equal("{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}", (string?)header.Attribute("IsChecked"));
        Assert.DoesNotContain(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "HeaderChevron");
        Assert.DoesNotContain(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasColorExpanderHeaderStyle");
    }

    [Fact]
    public void ColorExpander_LayoutChangesDoNotCancelExpandOrCollapseAnimation()
    {
        string source = ReadSource("src", "ControlPanelWindow.Accent.cs");
        Assert.Contains("_geometry != null && !_animating", source, StringComparison.Ordinal);
        string animate = source[source.IndexOf("internal void Animate(bool expanding)", StringComparison.Ordinal)..source.IndexOf("private ColorCardAnimation _effectColorCardAnimation", StringComparison.Ordinal)];
        Assert.True(animate.IndexOf("_animating = true", StringComparison.Ordinal) < animate.IndexOf("_contentHost.Height = contentHeight", StringComparison.Ordinal));
        Assert.Contains("animation.InsertExpressionKeyFrame(0, \"this.StartingValue\")", animate, StringComparison.Ordinal);
        Assert.Contains("_geometry.StartAnimation(\"Size.Y\", animation)", animate, StringComparison.Ordinal);
        Assert.Contains("generation != _generation", animate, StringComparison.Ordinal);
        Assert.True(animate.IndexOf("card.UpdateLayout()", StringComparison.Ordinal) < animate.IndexOf("_geometry.StartAnimation", StringComparison.Ordinal));
        Assert.Contains("float targetHeight = expanding ? (float)card.ActualHeight : (float)card.MinHeight", animate, StringComparison.Ordinal);
        string loaded = source[source.IndexOf("internal void Loaded()", StringComparison.Ordinal)..source.IndexOf("internal void SizeChanged", StringComparison.Ordinal)];
        Assert.Contains("_geometry ??=", loaded, StringComparison.Ordinal);
        Assert.DoesNotContain(".Animate(", loaded, StringComparison.Ordinal);
        Assert.Contains("new(EffectColorCard, EffectColorExpander, ColorPickerBody", source, StringComparison.Ordinal);
        Assert.Contains("new(AccentColorCard, AccentColorExpander, AccentColorPickerBody", source, StringComparison.Ordinal);
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
        Assert.Contains("""confirmText: Localization.Get("Msg_ConfirmDelete_Title")""", delete, StringComparison.Ordinal);
        Assert.Contains("""PrimaryButtonText = confirmText ?? Localization.Get("ColorPicker_Confirm")""", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(cancel, 0)", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(confirm, 4)", source, StringComparison.Ordinal);
        Assert.Contains("commands.ColumnDefinitions[3].Width = new GridLength(8)", source, StringComparison.Ordinal);
        Assert.Contains("return await dialog.ShowAsync() == ContentDialogResult.Primary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentedSelectors_MeasureFullLocalizedLabelsAndRefreshTheirSelectionAfterLanguageChanges()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string layout = source[source.IndexOf("public sealed class SegmentedRadioLayout", StringComparison.Ordinal)..source.IndexOf("public abstract class SelectionCardItem", StringComparison.Ordinal)];
        Assert.Contains("child.Measure(new Size(double.PositiveInfinity, availableSize.Height))", layout, StringComparison.Ordinal);
        Assert.Contains("Math.Ceiling(child.DesiredSize.Width * scale) / scale", layout, StringComparison.Ordinal);
        Assert.Contains("context.LayoutState = desiredWidths", layout, StringComparison.Ordinal);
        Assert.Contains("return new Size(desiredWidths.Sum(), height)", layout, StringComparison.Ordinal);
        Assert.Contains("position += desiredWidths[index]", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("finalSize.Width / count", layout, StringComparison.Ordinal);
        Assert.DoesNotContain(": 240", layout, StringComparison.Ordinal);
        Assert.Contains("_repeater?.InvalidateMeasure()", source, StringComparison.Ordinal);
        Assert.Contains("_selector.InvalidateMeasure()", source, StringComparison.Ordinal);
        string localized = source[source.IndexOf("private void ApplyLocalizedText()", StringComparison.Ordinal)..source.IndexOf("private static void SetRadioContent", StringComparison.Ordinal)];
        Assert.Contains("foreach (var selector in _segmentedSelectors) selector.RefreshLayout()", localized, StringComparison.Ordinal);
        string update = source[source.IndexOf("private void Update(bool animate)", StringComparison.Ordinal)..source.IndexOf("private const string UserAgent", StringComparison.Ordinal)];
        Assert.True(update.IndexOf("_selector.UpdateLayout()", StringComparison.Ordinal) < update.IndexOf("_selection.Width = cell.ActualWidth", StringComparison.Ordinal));
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement frame = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSegmentedSelectorStyle");
        Assert.Contains(frame.Elements(), element => (string?)element.Attribute("Property") == "MinWidth" && (string?)element.Attribute("Value") == "0");
        Assert.Contains(frame.Elements(), element => (string?)element.Attribute("Property") == "HorizontalAlignment" && (string?)element.Attribute("Value") == "Right");
        Assert.DoesNotContain(frame.Elements(), element => (string?)element.Attribute("Property") == "MaxWidth");
        XElement segment = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSegmentedRadioStyle");
        XElement label = Assert.Single(segment.Descendants(), element => element.Name == Presentation + "TextBlock");
        Assert.Equal("NoWrap", (string?)label.Attribute("TextWrapping"));
        Assert.Null(label.Attribute("TextTrimming"));
    }

    [Fact]
    public void PanelHost_ClosingReturnsForegroundToAnExternalWindowWithoutStealingFromAnotherApplication()
    {
        string source = ReadSource("src", "DcompPanelHost.cs");
        Assert.Contains("if (CanActivateExternalWindow(foreground)) _previousForegroundWindow = foreground", source, StringComparison.Ordinal);
        string hide = source[source.IndexOf("public void Hide()", StringComparison.Ordinal)..source.IndexOf("private static bool CanActivateExternalWindow", StringComparison.Ordinal)];
        Assert.True(hide.IndexOf("RestoreExternalForeground()", StringComparison.Ordinal) < hide.IndexOf("ShowWindow(_hwnd, SW_HIDE)", StringComparison.Ordinal));
        Assert.Contains("IsIconic(window) || !IsWindowEnabled(window)", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.WS_EX_NOACTIVATE", source, StringComparison.Ordinal);
        Assert.Contains("cloaked == 0", source, StringComparison.Ordinal);
        Assert.Contains("processId == 0 || processId == Environment.ProcessId", source, StringComparison.Ordinal);
        Assert.Contains("if (processId != Environment.ProcessId) return", source, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow(target)", source, StringComparison.Ordinal);
        string dispose = source[source.IndexOf("public void Dispose()", StringComparison.Ordinal)..];
        Assert.True(dispose.IndexOf("RestoreExternalForeground()", StringComparison.Ordinal) < dispose.IndexOf("_xamlSource.Dispose()", StringComparison.Ordinal));
        Assert.DoesNotContain("AttachThreadInput", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchToThisWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LastProfileDeletion_ShowsTheNativeMessageWithoutAnInformationHeading()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string delete = source[source.IndexOf("private async void DeleteProfile_Click", StringComparison.Ordinal)..source.IndexOf("private void RemoveProcess_Click", StringComparison.Ordinal)];
        Assert.Contains("""await ShowMessageAsync(Localization.Get("Msg_KeepOneProfile"));""", delete, StringComparison.Ordinal);
        Assert.Contains("dialog.Title = null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Msg_Info", delete, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileRename_KeepsOnlyTheTitleInputAndActionButtons()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement overlay = GetNamedElement(panel, "RenameProfileOverlay");
        Assert.Single(overlay.Descendants(Presentation + "TextBlock"));
        Assert.Same(GetNamedElement(panel, "TxtOverlayRename"), overlay.Descendants(Presentation + "TextBlock").Single());
        Assert.Equal("0,24,0,0", (string?)GetNamedElement(panel, "NewProfileNameInput").Attribute("Margin"));
        Assert.DoesNotContain("TxtOverlayRenamePrompt", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PanelDialogs_UseSubtitleHeadingsAndBodyStrongMessagesWithTheNativeModalStyle()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "RunningProcessOverlay", "VisualResetOverlay", "BackupOverlay", "RenameProfileOverlay" })
        {
            XElement overlay = GetNamedElement(panel, name);
            XElement heading = overlay.Descendants(Presentation + "TextBlock").First();
            Assert.Equal("{StaticResource BasSectionTitleStyle}", (string?)heading.Attribute("Style"));
            Assert.Null(heading.Attribute("FontSize"));
            Assert.Null(overlay.Attribute("Background"));
            Assert.Equal("ContentDialog", overlay.Name.LocalName);
            Assert.Equal("{StaticResource BasContentDialogStyle}", (string?)overlay.Attribute("Style"));
            XElement buttons = overlay.Descendants(Presentation + "StackPanel").Single(element => (string?)element.Attribute("Orientation") == "Horizontal");
            Assert.Equal("8", (string?)buttons.Attribute("Spacing"));
            Assert.Equal("0,16,0,0", (string?)buttons.Attribute("Margin"));
            Assert.Equal("Right", (string?)buttons.Attribute("HorizontalAlignment"));
        }
        foreach (string name in new[] { "SearchRunningProcess", "SearchVisualReset", "SearchBackupItems" })
        {
            XElement search = GetNamedElement(panel, name);
            XElement toolbar = search.Parent!;
            Assert.Equal("1", (string?)toolbar.Attribute("Grid.Row"));
            Assert.Equal(name == "SearchRunningProcess" ? "0,12,0,8" : "0,12,0,0", (string?)toolbar.Attribute("Margin"));
            Assert.Equal(2, toolbar.Elements(Presentation + "Button").Count());
            Assert.Equal(4, toolbar.Parent!.Elements(Presentation + "Grid.RowDefinitions").Single().Elements().Count());
        }
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("dialog.Title = null", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Style = (Style)Application.Current.Resources[\"BasContentDialogStyle\"]", source, StringComparison.Ordinal);
        string dialogs = source[source.IndexOf("private void ConfigureContentDialog", StringComparison.Ordinal)..source.IndexOf("private async Task<XamlRoot?> EnsureXamlRootAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain("Title = title", dialogs, StringComparison.Ordinal);
        foreach ((string list, string heading, string resource) in new[]
        {
            ("ListRunningProcesses", "TxtOverlayRunning", "Overlay_RunningProcess"),
            ("ListVisualResetItems", "TxtOverlayVisualReset", "Overlay_VisualReset")
        })
        {
            Assert.Contains($"SetSelectionTitle({list}, Localization.Get(\"{resource}\"))", source, StringComparison.Ordinal);
            Assert.Contains("Heading = " + heading, source, StringComparison.Ordinal);
        }
        Assert.Contains("toolbar.HeadingText = title", source, StringComparison.Ordinal);
        Assert.Contains("toolbar.Heading.Text = SelectionGroup.FormatTitle(title, toolbar.Items)", source, StringComparison.Ordinal);
        Assert.Contains("TxtOverlayRename.Text = Localization.Get(\"Overlay_RenameProfile\")", source, StringComparison.Ordinal);
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement sectionTitle = Assert.Single(styles.Descendants(), element => (string?)element.Attribute(Xaml + "Key") == "BasSectionTitleStyle");
        Assert.Equal("{StaticResource SubtitleTextBlockStyle}", (string?)sectionTitle.Attribute("BasedOn"));
        Assert.Equal(2, source.Split("Style = TryGetAppResource<Style>(\"BasDialogMessageStyle\")").Length - 1);
    }

    [Fact]
    public void StartupDialogs_ShareCompactCardSurfacesAndNativeRightAlignedActions()
    {
        XDocument language = LoadXaml("src", "LanguageSelectWindow.xaml");
        Assert.DoesNotContain(language.Descendants(), element => (string?)element.Attribute(Xaml + "Name") == "TxtTitle");
        Assert.Equal("{StaticResource BodyStrongTextBlockStyle}", (string?)GetNamedElement(language, "TxtSubtitle").Attribute("Style"));
        XDocument privacy = LoadXaml("src", "PrivacyWindow.xaml");
        foreach (XDocument document in new[] { language, privacy })
        {
            Assert.Equal("UserControl", document.Root?.Name.LocalName);
            Assert.Equal("{ThemeResource BasSidebarBackgroundBrush}", (string?)GetNamedElement(document, "RootGrid").Attribute("Background"));
            Assert.Equal("{ThemeResource BasSidebarBackgroundBrush}", (string?)GetNamedElement(document, "AppTitleBar").Attribute("Background"));
            XElement scroller = Assert.Single(document.Descendants(Presentation + "ScrollViewer"));
            Assert.Equal("0,0,-24,0", (string?)scroller.Attribute("Margin"));
            Assert.Equal("0,0,24,0", (string?)scroller.Attribute("Padding"));
            Assert.Equal("{StaticResource BasStartupContentMargin}", (string?)scroller.Parent!.Attribute("Margin"));
            foreach (XElement title in document.Descendants(Presentation + "TextBlock").Where(element =>
                         (string?)element.Attribute(Xaml + "Name") is "TxtSubtitle" or "VersionText" or "TxtTagline"))
                Assert.DoesNotContain(scroller.Descendants(), element => ReferenceEquals(element, title));
            XElement footer = Assert.Single(document.Descendants(Presentation + "Border"), element => (string?)element.Attribute("Style") == "{StaticResource BasStartupActionBarStyle}");
            Assert.Equal("2", (string?)footer.Attribute("Grid.Row"));
            Assert.Equal("Right", (string?)footer.Elements().Single().Attribute("HorizontalAlignment"));
            foreach (XElement button in footer.Descendants(Presentation + "Button"))
            {
                Assert.Null(button.Attribute("MinWidth"));
                Assert.Null(button.Attribute("MinHeight"));
            }
        }
        Assert.Equal("{StaticResource BasPageTitleStyle}", (string?)GetNamedElement(privacy, "VersionText").Attribute("Style"));
        Assert.Equal("{StaticResource BasCaptionStyle}", (string?)GetNamedElement(privacy, "TxtTagline").Attribute("Style"));
        Assert.Equal(new[] { "Informational", "Warning", "Success" }, privacy.Descendants(Presentation + "InfoBar").Select(element => (string?)element.Attribute("Severity")));
        foreach (string name in new[] { "VersionText", "TxtTagline", "TxtIntro", "TxtOpenSourceTitle", "TxtSecurityTitle", "TxtPrivacyTitle" })
            Assert.Equal(Presentation + "TextBlock", GetNamedElement(privacy, name).Name);
        Assert.Equal("{StaticResource BodyStrongTextBlockStyle}", (string?)GetNamedElement(privacy, "TxtIntro").Attribute("Style"));
        foreach (string name in new[] { "TxtOpenSourceTitle", "TxtSecurityTitle", "TxtPrivacyTitle" })
            Assert.Equal("{StaticResource BodyTextBlockStyle}", (string?)GetNamedElement(privacy, name).Attribute("Style"));
        Assert.Contains("Version {version.Major}.{version.Minor}.{version.Build}-release", ReadSource("src", "PrivacyWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("root.Measure(new Size(width / scale, double.PositiveInfinity))", ReadSource("src", "StartupDialogHost.cs"), StringComparison.Ordinal);
        Assert.Contains("appWindow.Resize(new SizeInt32(width, height))", ReadSource("src", "StartupDialogHost.cs"), StringComparison.Ordinal);
        foreach (string source in new[] { "LanguageSelectWindow.xaml.cs", "PrivacyWindow.xaml.cs" })
        {
            Assert.Contains("StartupDialogHost.FitToContent(_host, root)", ReadSource("src", source), StringComparison.Ordinal);
            Assert.Contains("_host.SetContent(this)", ReadSource("src", source), StringComparison.Ordinal);
            Assert.Contains("WindowChrome.ApplyAppIcon(Handle)", ReadSource("src", source), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StartupLanguage_UsesTheSettingsLanguageCardWithAllThreeCulturesAndPreviewBeforeSave()
    {
        XDocument language = LoadXaml("src", "LanguageSelectWindow.xaml");
        XElement combo = GetNamedElement(language, "ComboLanguage");
        Assert.Equal(Presentation + "ComboBox", combo.Name);
        Assert.Equal(new[] { "zh-CN", "en", "ja" }, combo.Elements().Select(element => (string?)element.Attribute("Tag")).ToArray());
        XElement card = GetNamedElement(language, "LanguageCard");
        Assert.Equal("{StaticResource BasSettingCardStyle}", (string?)card.Attribute("Style"));
        Assert.Equal("{StaticResource BasSettingTitleStyle}", (string?)GetNamedElement(language, "TxtLanguageLabel").Attribute("Style"));
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement settings = GetNamedElement(panel, "ComboLanguage");
        foreach (string attribute in new[] { "Background", "BorderBrush", "MinWidth", "HorizontalAlignment" })
            Assert.Equal((string?)settings.Attribute(attribute), (string?)combo.Attribute(attribute));
        string source = ReadSource("src", "LanguageSelectWindow.xaml.cs");
        string preview = source[source.IndexOf("private void ComboLanguage_SelectionChanged", StringComparison.Ordinal)..source.IndexOf("private void BtnContinue_Click", StringComparison.Ordinal)];
        Assert.Contains("Localization.ApplyCulture(culture)", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigManager.Save", preview, StringComparison.Ordinal);
        Assert.Contains("ConfigManager.Save(\"UiLanguage\", selected)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SilentStartup_KeepsTheLifetimeWindowHiddenAndNonActivating()
    {
        string source = ReadSource("src", "KeeperWindow.cs");
        Assert.Contains("_window.AppWindow?.Hide()", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE", source, StringComparison.Ordinal);
        Assert.Contains("& ~WsExAppWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_window.Activate()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("-32000", source, StringComparison.Ordinal);
        Assert.Contains("_window?.Close()", source, StringComparison.Ordinal);
        Assert.Contains("if (!ConfigManager.StartSilent)", ReadSource("src", "App.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void BackupPage_UsesTheExistingNavigationCardsAndSelectionDialog()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement navigation = GetNamedElement(panel, "SubTabBackup");
        Assert.Equal("NavigationViewItem", navigation.Name.LocalName);
        Assert.Contains(GetNamedElement(panel, "TabSettings"), navigation.Ancestors());
        XElement page = GetNamedElement(panel, "SectionBackup");
        Assert.Equal(2, page.Descendants(Presentation + "Border").Count(element => (string?)element.Attribute("Style") == "{StaticResource BasSettingCardStyle}"));
        XElement overlay = GetNamedElement(panel, "BackupOverlay");
        Assert.Null(overlay.Attribute("Background"));
        XElement list = GetNamedElement(panel, "ListBackupItems");
        Assert.Equal("{StaticResource SelectionCardTemplates}", (string?)list.Attribute("ItemTemplateSelector"));
        Assert.Equal("{StaticResource SelectionListStyle}", (string?)list.Attribute("Style"));
        Assert.Equal("ItemsControl", list.Name.LocalName);
        foreach (string name in new[] { "BtnExportConfiguration", "BtnImportConfiguration" })
        {
            XElement button = GetNamedElement(panel, name);
            Assert.Equal("1", (string?)button.Attribute("Grid.Column"));
            Assert.Equal("Right", (string?)button.Attribute("HorizontalAlignment"));
        }
        string source = ReadSource("src", "ControlPanelWindow.Backup.cs");
        Assert.Contains("new Microsoft.Windows.Storage.Pickers.FolderPicker(windowId)", source, StringComparison.Ordinal);
        Assert.Contains("new Microsoft.Windows.Storage.Pickers.FileOpenPicker(windowId)", source, StringComparison.Ordinal);
        Assert.Contains("ConfigurationBackup.BuildImportValues(source, selected, saved)", source, StringComparison.Ordinal);
        Assert.Contains("ConfigurationBackup.BuildImportValues(source, selected, pending)", source, StringComparison.Ordinal);
        Assert.Contains("ConfigManager.SaveBackupValues(persistedValues)", source, StringComparison.Ordinal);
        Assert.Contains("MarkImportedSettingsSaved(selected)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeAndLogActions_UseNativeButtonsAndSharedDangerStyling()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "BtnExitApplication", "BtnClearLog" })
            Assert.Equal("{StaticResource BasDangerButtonStyle}", (string?)GetNamedElement(panel, name).Attribute("Style"));
        Assert.Same(GetNamedElement(panel, "BtnExitApplication").Parent, GetNamedElement(panel, "BtnRestartApplication").Parent);
        Assert.Same(GetNamedElement(panel, "BtnClearLog").Parent, GetNamedElement(panel, "BtnCopyLog").Parent);
        string source = ReadSource("src", "ControlPanelWindow.Backup.cs");
        Assert.Contains("app.ExitApplication()", source, StringComparison.Ordinal);
        Assert.Contains("app.RestartApplicationFromPanel()", source, StringComparison.Ordinal);
        Assert.Contains("data.SetText(TxtAppLog.Text)", source, StringComparison.Ordinal);
        Assert.Contains("Clipboard.Flush()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupWindows_BlockDoubleClickMaximizeAndSizingCommandsWithoutChangingTheMainPanel()
    {
        string source = ReadSource("src", "DcompPanelHost.cs");
        Assert.Contains("case 0x00A3 when InstanceOf(hwnd)?._fixedSize == true", source, StringComparison.Ordinal);
        Assert.Contains("0xF000 or 0xF030 or 0xF020", source, StringComparison.Ordinal);
        Assert.Contains("if (_fixedSize) style &= ~(WsThickFrame | WsMinimizeBox | WsMaximizeBox)", source, StringComparison.Ordinal);
        Assert.Contains("bool fixedSize = false, int minimumWidth = MinWidthDesign, int minimumHeight = MinHeightDesign", source, StringComparison.Ordinal);
        string close = source[source.IndexOf("public void RequestClose()", StringComparison.Ordinal)..source.IndexOf("public void Dispose()", StringComparison.Ordinal)];
        Assert.DoesNotContain("Hide()", close, StringComparison.Ordinal);
        Assert.Contains("root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity))", ReadSource("src", "StartupDialogHost.cs"), StringComparison.Ordinal);
        foreach (string file in new[] { "LanguageSelectWindow.xaml.cs", "PrivacyWindow.xaml.cs" })
            Assert.Contains("new DcompPanelHost(fixedSize: true, minimumWidth: 1, minimumHeight: 1)", ReadSource("src", file), StringComparison.Ordinal);
    }

    [Fact]
    public void PanelClose_ReleasesDispatcherAndXamlRootHandlersAndDetachesNativeContent()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("_refreshTimer.Tick += RefreshTimer_OnTick", source, StringComparison.Ordinal);
        Assert.Contains("_refreshTimer.Tick -= RefreshTimer_OnTick", source, StringComparison.Ordinal);
        Assert.Contains("root.Changed -= PanelXamlRoot_Changed", source, StringComparison.Ordinal);
        Assert.Contains("_host.CloseRequested -= ControlPanelWindow_Closed", source, StringComparison.Ordinal);
        Assert.Contains("_xamlSource.Content = null", ReadSource("src", "DcompPanelHost.cs"), StringComparison.Ordinal);
        Assert.Contains("Content = null", source, StringComparison.Ordinal);
        Assert.Contains("Resources.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("RootGrid.RemoveHandler(UIElement.PointerPressedEvent", source, StringComparison.Ordinal);
        Assert.Contains("ExclusiveUserDataFolderAccess = true", ReadSource("src", "WebView2EnvironmentHolder.cs"), StringComparison.Ordinal);
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

    [Theory]
    [InlineData("ControlPanelWindow.xaml", "LogScrollViewer", "TxtAppLog")]
    [InlineData("InstallerWindow.xaml", "OutputScroller", "OutputText")]
    public void LogOutput_KeepsTheNativeScrollbarAtTheCardEdge(string file, string scrollName, string textName)
    {
        XDocument view = LoadXaml("src", file);
        XElement scroll = GetNamedElement(view, scrollName);
        Assert.Equal("{StaticResource BasLogCardStyle}", (string?)scroll.Parent!.Attribute("Style"));
        Assert.Null(scroll.Attribute("Margin"));
        Assert.Null(scroll.Attribute("Padding"));
        Assert.Equal("16", (string?)GetNamedElement(view, textName).Attribute("Margin"));
        XElement style = LoadXaml("src", "DesignSystem.xaml").Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "BasLogCardStyle");
        Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "Padding" && (string?)setter.Attribute("Value") == "0");
    }

    [Fact]
    public void AutoStart_IsReconciledAtLaunchInstallationAndImportWithoutOpeningThePanel()
    {
        string app = ReadSource("src", "App.xaml.cs");
        Assert.Contains("_ = RepairAutoStartAsync();", app, StringComparison.Ordinal);
        Assert.Contains("--repair-autostart", app, StringComparison.Ordinal);
        Assert.Contains("AutoStartManager.ApplySettings(autoStart, runAsAdmin, allowElevation: false, executablePath: executable)", app, StringComparison.Ordinal);
        Assert.Contains("Task.Run", app, StringComparison.Ordinal);
        string setup = ReadSource("setup.iss");
        Assert.Contains("Parameters: \"--repair-autostart\"; Flags: runhidden runascurrentuser waituntilterminated", setup, StringComparison.Ordinal);
        Assert.Contains("ApplyAutoStartSettings(useSavedSettings: true)", ReadSource("src", "ControlPanelWindow.Backup.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedFx_UsesNativeCollapsedExpandersAndRequestedHeadingLevels()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement toggle = GetNamedElement(panel, "CheckBaClickAdvancedSettings");
        Assert.Equal(Presentation + "ToggleSwitch", toggle.Name);
        Assert.Same(GetNamedElement(panel, "SectionVisual"), GetSettingCard(toggle).Parent);
        XElement list = GetNamedElement(panel, "BaClickAdvancedList");
        Assert.Equal(Presentation + "Expander", list.Name);
        Assert.Equal("False", (string?)list.Attribute("IsExpanded"));
        Assert.Equal("ConditionalSettingsList_Loaded", (string?)list.Attribute("Loaded"));
        Assert.Same(list, GetNamedElement(panel, "PanelBaClickAdvanced").Parent);
        Assert.Null(GetNamedElement(panel, "PanelBaClickAdvanced").Attribute("Visibility"));
        XDocument design = LoadXaml("src", "DesignSystem.xaml");
        XElement group = Assert.Single(design.Descendants(Presentation + "Style"),
            item => (string?)item.Attribute(Xaml + "Key") == "BasAdvancedGroupTitleStyle");
        Assert.Equal("{StaticResource BodyLargeTextBlockStyle}", (string?)group.Attribute("BasedOn"));
        Assert.Contains(group.Elements(), item => (string?)item.Attribute("Property") == "FontWeight" &&
            (string?)item.Attribute("Value") == "SemiBold");
        Assert.Equal("{StaticResource BodyStrongTextBlockStyle}",
            (string?)Assert.Single(design.Descendants(Presentation + "Style"),
                item => (string?)item.Attribute(Xaml + "Key") == "BasAdvancedSectionTitleStyle").Attribute("BasedOn"));
        string source = ReadSource("src", "ControlPanelWindow.Advanced.cs");
        Assert.Contains("new Expander", source, StringComparison.Ordinal);
        Assert.Contains("IsExpanded = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlTemplate", source, StringComparison.Ordinal);
        Assert.Contains("BaClickAdvancedList.IsExpanded = CheckBaClickAdvancedSettings.IsOn", source, StringComparison.Ordinal);
        Assert.Contains("header.Visibility = Visibility.Collapsed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingsVisibility", source, StringComparison.Ordinal);
        Assert.Contains("CreateAdvancedLabel(setting.DescriptionKey, \"BasCaptionStyle\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Advanced_DisplayHint", source, StringComparison.Ordinal);
        XElement expanderStyle = Assert.Single(design.Descendants(Presentation + "Style"),
            item => (string?)item.Attribute(Xaml + "Key") == "BasAdvancedExpanderStyle");
        Assert.Null(expanderStyle.Attribute("BasedOn"));
        Assert.DoesNotContain(expanderStyle.Elements(), item => (string?)item.Attribute("Property") == "Template");
        XElement cardStyle = Assert.Single(design.Descendants(Presentation + "Style"),
            item => (string?)item.Attribute(Xaml + "Key") == "BasSettingCardStyle");
        Assert.Equal((string?)cardStyle.Elements().Single(item => (string?)item.Attribute("Property") == "CornerRadius").Attribute("Value"),
            (string?)expanderStyle.Elements().Single(item => (string?)item.Attribute("Property") == "CornerRadius").Attribute("Value"));
        Assert.Contains("Application.Current.Resources[\"BasAdvancedExpanderStyle\"]", source, StringComparison.Ordinal);
        Assert.Equal(new[] { "Advanced_sectionBasic", "Advanced_sectionClick", "Advanced_sectionShards", "Advanced_sectionTrail", "Advanced_sectionBloom" },
            AdvancedFxSettings.Definitions.Select(item => item.GroupKey).Distinct().ToArray());
        Assert.Equal(10, AdvancedFxSettings.Definitions.Select(item => item.SectionKey).Distinct().Count());
    }

    [Fact]
    public void AdvancedFx_AllLabelsAndOptionsAreLocalized()
    {
        foreach (string file in new[] { "Strings.resx", "Strings.en.resx", "Strings.ja.resx" })
        {
            XDocument resources = XDocument.Parse(ReadSource("src", file));
            var values = resources.Root!.Elements("data").ToDictionary(item => (string)item.Attribute("name")!, item => item.Element("value")!.Value);
            foreach (AdvancedFxSetting definition in AdvancedFxSettings.Definitions)
                foreach (string key in new[] { definition.GroupKey, definition.SectionKey, definition.TitleKey, definition.DescriptionKey }
                             .Concat(definition.Options?.Select(item => item.TitleKey) ?? []))
                    Assert.False(string.IsNullOrWhiteSpace(values[key]));
            Assert.False(string.IsNullOrWhiteSpace(values["Advanced_Enable"]));
            Assert.False(string.IsNullOrWhiteSpace(values["Advanced_EnableDescription"]));
            Assert.DoesNotContain("Advanced_DisplayHint", values.Keys);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(4)]
    public void PageSettings_AcceptOnlyTheSelectedPageAndKeepOtherDrafts(int pageId)
    {
        SettingsSections page = (SettingsSections)pageId;
        var saved = new SettingsState("basic-saved", "visual-saved", "screens-saved", "filter-saved");
        var pending = new SettingsState("basic-pending", "visual-pending", "screens-pending", "filter-pending");
        SettingsState accepted = saved.Accept(pending, page);
        foreach (SettingsSections section in new[] { SettingsSections.Basic, SettingsSections.Visual, SettingsSections.Filter, SettingsSections.Screens })
        {
            Assert.Equal(section == page ? pending.GetPage(section) : saved.GetPage(section), accepted.GetPage(section));
            Assert.Equal(section != page, pending.HasChanges(accepted, section));
        }
        Assert.False(pending.HasChanges(accepted, 0));
    }

    [Fact]
    public void PageSettings_SaveAndDiscardValuesPreservePageBoundariesAndAdvancedPrecision()
    {
        var saved = new SettingsState(
            "{\"UiLanguage\":\"zh-CN\",\"BackgroundColor\":2,\"MaxTouchEffects\":1}",
            "{\"EffectScale\":1.2,\"ParticleColor\":\"76,167,255\",\"EffectOpacity\":0.75,\"EnableBaClickAdvancedSettings\":true,\"BaClickFx/overlayAlphaLimit\":0.85}",
            "[{\"IdentityKey\":\"screen\",\"DeviceName\":\"DISPLAY1\",\"IsEnabled\":true}]",
            "{\"EnableEnvironmentFilter\":true,\"ShowEffectOnApp\":false,\"Profiles\":[{\"Id\":\"profile\",\"Name\":\"Saved\",\"Mode\":1,\"Processes\":[\"saved.exe\"]}],\"ActiveProfileId\":\"profile\"}");
        Dictionary<string, object> basic = saved.GetValues(SettingsSections.Basic);
        Assert.Equal(new[] { "BackgroundColor", "MaxTouchEffects", "UiLanguage" }, basic.Keys.Order().ToArray());
        Assert.IsType<BackgroundColorOption>(basic["BackgroundColor"]);
        Dictionary<string, object> visual = saved.GetValues(SettingsSections.Visual);
        Assert.Equal(0.75, visual["EffectOpacity"]);
        Assert.Equal(0.85, AdvancedFxSettings.Parse((string)visual["BaClickAdvancedSettings"])["overlayAlphaLimit"].GetDouble());
        Assert.DoesNotContain("MaxTouchEffects", visual.Keys);
        Assert.DoesNotContain("FilterProfiles", visual.Keys);
        Dictionary<string, object> filter = saved.GetValues(SettingsSections.Filter);
        Assert.Equal(new[] { "ActiveProfileId", "EnableEnvironmentFilter", "FilterProfiles", "ShowEffectOnApp" }, filter.Keys.Order().ToArray());
        Assert.False((bool)filter["ShowEffectOnApp"]);
        FilterProfile profile = System.Text.Json.JsonSerializer.Deserialize<List<FilterProfile>>((string)filter["FilterProfiles"])!.Single();
        Assert.Equal(new[] { "saved.exe" }, profile.Processes);
        Assert.Equal(saved.Screens, saved.GetValues(SettingsSections.Screens)["ScreenSelections"]);
        Assert.Empty(saved.GetValues(0));
    }

    [Fact]
    public void PageSettings_SectionResetsAndDiscardDoNotPersistOrApply()
    {
        string source = ReadSource("src", "ControlPanelWindow.Pages.cs");
        string reset = source[source.IndexOf("private void ResetSection_Click", StringComparison.Ordinal)..source.IndexOf("private void ApplyPageRuntimeSettings", StringComparison.Ordinal)];
        Assert.Contains("foreach (Action restore in restores) restore()", reset, StringComparison.Ordinal);
        Assert.Contains("ApplyBackupValuesToPanel(_savedSettingsState.GetValues(CurrentSettingsPage), animate: true)", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigManager.Save", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("App.Overlay", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDarkMode", reset, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Save", reset, StringComparison.Ordinal);
        Assert.Contains("_savedSettingsState = _savedSettingsState!.Accept(current, page)", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PageSettings_KeepPageTitlesSectionActionsAndResetButtonsOutsideTheScrollContent()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string name in new[] { "TxtWelcomeTitle", "TxtSettingsTitle", "TxtLogTitle", "TxtAboutTitle", "TxtStatsTitle",
                     "TxtActiveSettingsSection", "TxtActiveAboutSection", "BtnBasicReset", "BtnVisualReset", "BtnFilterReset", "BtnResetSection" })
            Assert.DoesNotContain(GetNamedElement(panel, name).Ancestors(), item => item.Name.LocalName == "ScrollViewer");
        XElement discard = GetNamedElement(panel, "BtnUndoSettings");
        XElement apply = GetNamedElement(panel, "BtnApplySettings");
        Assert.Same(apply.Parent, discard.Parent);
        Assert.Same(apply, discard.ElementsAfterSelf().First());
        Assert.Null(discard.Attribute("Style"));
        Assert.Equal("False", (string?)discard.Attribute("IsEnabled"));
        Assert.DoesNotContain(panel.Descendants(), item => (string?)item.Attribute(Xaml + "Name") == "BtnScreensReset");
        Assert.Contains("TxtScreensTitle.Text = Localization.Get(\"MultiScreen_Displays\")", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Equal("放弃更改", LoadXaml("src", "Strings.resx").Descendants("data").Single(item =>
            (string?)item.Attribute("name") == "Settings_Undo").Element("value")!.Value);
    }

    [Fact]
    public void PageSettings_DialogGroupButtonsAlignWithGlobalButtonsAndRetainNativeScrolling()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        foreach (string prefix in new[] { "Reset", "Backup" })
        {
            XElement group = GetNamedElement(panel, prefix + "GroupHeader");
            Assert.DoesNotContain(group.Ancestors(), item => item.Name.LocalName == "ScrollViewer");
            Assert.Equal("8", (string?)group.Attribute("ColumnSpacing"));
            foreach (string suffix in new[] { "Invert", "SelectAll" })
            {
                XElement local = GetNamedElement(panel, "Btn" + prefix + "Group" + suffix);
                XElement global = GetNamedElement(panel, "Btn" + prefix + suffix);
                foreach (string property in new[] { "Width", "Height", "Padding", "MinWidth", "Grid.Column", "Click" })
                    Assert.Equal((string?)global.Attribute(property), (string?)local.Attribute(property));
            }
        }
        string source = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("scroll.ViewChanged += ViewChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherQueueTimer", source, StringComparison.Ordinal);
        Assert.Contains("selectedGroup.Items.Where(toolbar.Items.Contains)", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PageSettings_GroupSelectionChangesImmediatelyWithoutAffectingOtherOrHiddenItems()
    {
        var first = new ProcessItem { ProcessName = "first.exe" };
        var second = new ProcessItem { ProcessName = "second.exe", IsSelected = true };
        var otherGroup = new ProcessItem { ProcessName = "other.exe", IsSelected = true };
        var hidden = new ProcessItem { ProcessName = "hidden.exe" };
        SelectionCardItem[] visibleGroup = [first, second];
        SelectionBatch.Change(visibleGroup, invert: false);
        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        SelectionBatch.Change(visibleGroup, invert: false);
        Assert.False(first.IsSelected);
        Assert.False(second.IsSelected);
        for (int index = 0; index < 51; index++) SelectionBatch.Change(visibleGroup, invert: true);
        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(otherGroup.IsSelected);
        Assert.False(hidden.IsSelected);
    }

    [Fact]
    public void SelectionCounts_ReflectOnlyTheRowsTargetedByEachToolbar()
    {
        SelectionCardItem[] items = [new ProcessItem { IsSelected = true }, new ProcessItem(), new ProcessItem { IsSelected = true }];
        Assert.Equal("Settings (2/3)", SelectionGroup.FormatTitle("Settings", items));
        Assert.Equal("Settings (1/2)", SelectionGroup.FormatTitle("Settings", items.Take(2).ToArray()));
        var group = new SelectionGroup("Visual", null!, items.ToList(), false);
        string? property = null;
        group.PropertyChanged += (_, args) => property = args.PropertyName;
        SelectionBatch.Change(items, invert: true);
        group.RefreshSelectionCount();
        Assert.Equal("Visual (1/3)", group.TitleWithCount);
        Assert.Equal(nameof(SelectionGroup.TitleWithCount), property);
        SelectionBatch.Change(items, invert: false);
        Assert.Equal("Settings (3/3)", SelectionGroup.FormatTitle("Settings", items));
        Assert.Equal("Empty (0/0)", SelectionGroup.FormatTitle("Empty", []));
    }

    [Theory]
    [InlineData("1", "1.00", true)]
    [InlineData("0.30000000000000004", "0.3", true)]
    [InlineData("0.31", "0.3", false)]
    [InlineData("true", "false", false)]
    [InlineData("true", "true", true)]
    [InlineData("\"1\"", "1", false)]
    public void InlineReset_DefaultChecksRespectValueTypesAndNumericPrecision(string current, string expected, bool equal)
    {
        using var value = System.Text.Json.JsonDocument.Parse(current);
        using var defaultValue = System.Text.Json.JsonDocument.Parse(expected);
        Assert.Equal(equal, ControlPanelWindow.SettingValuesEqual(value.RootElement, defaultValue.RootElement));
    }

    [Fact]
    public void InlineReset_PageIconsUseStandardSizingAndOtherIconsFollowTypography()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement title = GetNamedElement(panel, "TxtSettingsTitle");
        foreach (string name in new[] { "BtnBasicReset", "BtnVisualReset", "BtnFilterReset" })
        {
            XElement button = GetNamedElement(panel, name);
            Assert.Same(title.Parent, button.Parent);
            Assert.Null(button.Attribute("Content"));
            Assert.Equal("{StaticResource BasInlineResetButtonStyle}", (string?)button.Attribute("Style"));
            Assert.Single(button.Elements(), item => item.Name.LocalName == "FontIcon");
        }
        XElement style = Assert.Single(LoadXaml("src", "DesignSystem.xaml").Descendants(Presentation + "Style"),
            item => (string?)item.Attribute(Xaml + "Key") == "BasInlineResetButtonStyle");
        Assert.Equal("{StaticResource SubtleButtonStyle}", (string?)style.Attribute("BasedOn"));
        Assert.Contains(style.Elements(), item => (string?)item.Attribute("Property") == "Background" && (string?)item.Attribute("Value") == "Transparent");
        Assert.Contains(style.Elements(), item => (string?)item.Attribute("Property") == "BorderThickness" && (string?)item.Attribute("Value") == "0");
        Assert.DoesNotContain(style.Elements(), item => (string?)item.Attribute("Property") == "Template");
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        string sizing = pages[pages.IndexOf("private void ConfigureResetButton", StringComparison.Ordinal)..
            pages.IndexOf("private static void SetResetButtonLabel", StringComparison.Ordinal)];
        Assert.Contains("label.FontSize / reference.FontSize", sizing, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Resources[\"BasSettingsPageTitleStyle\"]", sizing, StringComparison.Ordinal);
        Assert.Contains("button.Height = button.Width = 32 * scale", sizing, StringComparison.Ordinal);
        Assert.Contains("button.Padding = new Thickness(4 * scale)", sizing, StringComparison.Ordinal);
        Assert.Contains("ConfigureResetButton(button, TxtSettingsTitle, standardSize: true)", pages, StringComparison.Ordinal);
        Assert.Contains("icon.FontSize = 16 * scale", sizing, StringComparison.Ordinal);
        Assert.Contains("icon.IsTextScaleFactorEnabled = false", sizing, StringComparison.Ordinal);
        Assert.Contains("ConfigureResetButtonAnimations(button)", sizing, StringComparison.Ordinal);
        Assert.Contains("new DoubleAnimationUsingKeyFrames", pages, StringComparison.Ordinal);
        Assert.Contains("Value = visible ? 1 : 0", pages, StringComparison.Ordinal);
        Assert.Contains("Storyboard.SetTargetProperty(fade, \"Opacity\")", pages, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState(button, \"Normal\", false)", pages, StringComparison.Ordinal);
        Assert.Contains("ControlFastAnimationDuration", pages, StringComparison.Ordinal);
        Assert.Contains("UISettings().AnimationsEnabled", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("SetImplicitShowAnimation", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("SetImplicitHideAnimation", pages, StringComparison.Ordinal);
        Assert.Contains("button.Visibility = _visible ? Visibility.Visible : Visibility.Collapsed", pages, StringComparison.Ordinal);
        Assert.Contains("!entry.IsDefault(_currentSettingsState)", pages, StringComparison.Ordinal);
        Assert.Contains("private void RefreshPageHeaders(bool animateResetButtons = false)", pages, StringComparison.Ordinal);
        Assert.Contains("internal void Update(bool refresh = false, bool animateResetButtons = false)", pages, StringComparison.Ordinal);
        Assert.Contains("_show(active, animateResetButtons)", pages, StringComparison.Ordinal);
        Assert.Contains("UpdateApplySettingsState(SettingsSections.All, animateResetButtons: true)", pages, StringComparison.Ordinal);
        Assert.Contains("RefreshResetButtonVisibility(animateResetButtons)", ReadSource("src", "ControlPanelWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedFx_GroupAndSectionHeadingsResetOnlyTheirOwnPendingSettings()
    {
        string source = ReadSource("src", "ControlPanelWindow.Advanced.cs");
        Assert.Contains("CreateAdvancedHeading(group.Key, \"BasAdvancedGroupTitleStyle\", group)", source, StringComparison.Ordinal);
        Assert.Contains("CreateAdvancedHeading(section.Key, \"BasAdvancedSectionTitleStyle\", section)", source, StringComparison.Ordinal);
        string heading = source[source.IndexOf("private StackPanel CreateAdvancedHeading", StringComparison.Ordinal)..
            source.IndexOf("private TextBlock CreateAdvancedLabel", StringComparison.Ordinal)];
        Assert.Contains("AddCardResetButton(label, heading, ResetScope.Visual", heading, StringComparison.Ordinal);
        Assert.Contains("settings.ToArray()", heading, StringComparison.Ordinal);
        Assert.Contains("foreach (AdvancedFxSetting setting in definitions)", heading, StringComparison.Ordinal);
        Assert.Contains("SetAdvancedControlValue(setting.Path, setting.DefaultValue)", heading, StringComparison.Ordinal);
        Assert.Contains("definitions.All(setting => IsDefaultSettingValue(state, setting.Key, setting.DefaultValue))", heading, StringComparison.Ordinal);
        Assert.Contains("isGroup: true", heading, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigManager.Save", heading, StringComparison.Ordinal);
        Assert.DoesNotContain("App.Overlay", heading, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelRows_CenterTextUsingCachedFontMetricsWithoutTrimmingDisplayedGlyphs()
    {
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        string alignment = pages[pages.IndexOf("private void SetupRowTextAlignment", StringComparison.Ordinal)..
            pages.IndexOf("private void ConfigureResetButtonAnimations", StringComparison.Ordinal)];
        Assert.Contains("label.VerticalAlignment = VerticalAlignment.Center", alignment, StringComparison.Ordinal);
        Assert.Contains("_rowTextOffsets.TryGetValue", alignment, StringComparison.Ordinal);
        Assert.Contains("TextLineBounds = TextLineBounds.TrimToCapHeight", alignment, StringComparison.Ordinal);
        Assert.Contains("measurement.TextLineBounds = TextLineBounds.TrimToBaseline", alignment, StringComparison.Ordinal);
        Assert.Contains("transform.Y = offset", alignment, StringComparison.Ordinal);
        Assert.DoesNotContain("label.TextLineBounds =", alignment, StringComparison.Ordinal);
        string panel = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("SetupRowTextAlignment()", panel, StringComparison.Ordinal);
        Assert.Contains("label.SizeChanged -= RowText_SizeChanged", panel, StringComparison.Ordinal);
        Assert.Contains("_rowTextOffsets.Clear()", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void PageHeadings_RefreshAfterNavigationAndAfterTheirAnchorsLoad()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string navigation = source[source.IndexOf("private void UpdatePageVisibility()", StringComparison.Ordinal)..
            source.IndexOf("private void SetPageVisible", StringComparison.Ordinal)];
        Assert.True(navigation.LastIndexOf("RefreshPageHeaders()", StringComparison.Ordinal) >
            navigation.IndexOf("SetPageVisible(PageAbout", StringComparison.Ordinal));
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("heading.Anchor.Loaded += Heading_Loaded", pages, StringComparison.Ordinal);
        Assert.Contains("heading.Anchor.Loaded -= Heading_Loaded", pages, StringComparison.Ordinal);
        Assert.Contains("private void Heading_Loaded(object sender, RoutedEventArgs args) => RequestUpdate(true)", pages, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeSection_UsesTheSameHeadingGapAsOtherPages()
    {
        XDocument panel = LoadXaml("src", "ControlPanelWindow.xaml");
        Assert.Equal("0,4,0,0", (string?)GetNamedElement(panel, "TxtWelcomeSubtitle").Attribute("Margin"));
        Assert.Equal("{StaticResource BasCaptionStyle}", (string?)GetNamedElement(panel, "TxtWelcomeSubtitle").Attribute("Style"));
        XDocument styles = LoadXaml("src", "DesignSystem.xaml");
        XElement style = Assert.Single(styles.Descendants(Presentation + "Style"),
            item => (string?)item.Attribute(Xaml + "Key") == "BasSectionHeaderStyle");
        Assert.Equal("48", (string?)Assert.Single(style.Elements(Presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Height").Attribute("Value"));
        foreach (string name in new[] { "StatsHeader", "SettingsSectionHeader", "AboutSectionHeader", "ResetGroupHeader", "BackupGroupHeader" })
            Assert.Equal("{StaticResource BasSectionHeaderStyle}", (string?)GetNamedElement(panel, name).Attribute("Style"));
        string pages = ReadSource("src", "ControlPanelWindow.Pages.cs");
        Assert.Contains("StartupDialogHost.AlignContentSpacing(StatsHeader, TxtStatsTitle, TxtWelcomeSubtitle)", pages, StringComparison.Ordinal);
        Assert.Contains("StartupDialogHost.AlignContentSpacing(SettingsSectionHeader, TxtActiveSettingsSection, TxtSettingsTitle)", pages, StringComparison.Ordinal);
        Assert.Contains("StartupDialogHost.AlignContentSpacing(AboutSectionHeader, TxtActiveAboutSection, TxtAboutDescription)", pages, StringComparison.Ordinal);
    }

    [Fact]
    public void RunningProcessPicker_PreparesTheFirstSnapshotBeforeShowingTheList()
    {
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string open = source[source.IndexOf("private async void SelectRunningProcess_Click", StringComparison.Ordinal)..
            source.IndexOf("private void CloseRunningProcessOverlay_Click", StringComparison.Ordinal)];
        Assert.True(open.IndexOf("await GetRunningProcessesAsync()", StringComparison.Ordinal) < open.IndexOf("ApplyRunningProcessFilter(null)", StringComparison.Ordinal));
        Assert.True(open.IndexOf("ApplyRunningProcessFilter(null)", StringComparison.Ordinal) < open.IndexOf("SetModalOverlayVisibleAsync", StringComparison.Ordinal));
        Assert.Contains("Task.Run(GetRunningProcesses)", open, StringComparison.Ordinal);
        Assert.Contains("generation != _runningProcessLoadGeneration", open, StringComparison.Ordinal);
    }

    [Fact]
    public void MouseInputHook_CopiesNativeInputAndLetsTheUiDrainItWithoutBlockingTheHook()
    {
        var work = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var inputs = new List<NativeMethods.MSLLHOOKSTRUCT>();
        using var hook = new MouseInputHook(action => { work.Enqueue(action); return true; }, (_, input) => inputs.Add(input));
        Assert.NotEqual(IntPtr.Zero, hook.Handle);
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var thread = Assert.IsType<Thread>(typeof(MouseInputHook).GetField("_thread", members)!.GetValue(hook));
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread.ManagedThreadId);
        var callback = Assert.IsType<NativeMethods.MouseHookProc>(typeof(MouseInputHook).GetField("_callback", members)!.GetValue(hook));
        var input = new NativeMethods.MSLLHOOKSTRUCT { pt = new NativeMethods.POINT { x = -120, y = 480 }, dwExtraInfo = new UIntPtr(0xBA5A7755) };
        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MSLLHOOKSTRUCT>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(input, buffer, false);
            callback(0, new IntPtr(NativeMethods.WM_MOUSEMOVE), buffer);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        Assert.Empty(inputs);
        while (work.TryDequeue(out Action? drain)) drain();
        var copied = Assert.Single(inputs, item => item.dwExtraInfo == input.dwExtraInfo);
        Assert.Equal(input.pt.x, copied.pt.x);
        Assert.Equal(input.pt.y, copied.pt.y);
        hook.Dispose();
        Assert.False(thread.IsAlive);
        Assert.Equal(IntPtr.Zero, hook.Handle);
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
