using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BASpark.Tests;

public class DarkThemeUiTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void PrimaryActionButtons_UseSharedThemeStyle()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        string[] buttonNames =
        [
            "BtnOfficialSite",
            "BtnApplySettings",
            "BtnAddProcess",
            "BtnOverlayConfirmAdd",
            "BtnOverlayVisualConfirm",
            "BtnOverlayRenameConfirm",
        ];

        foreach (string buttonName in buttonNames)
        {
            XElement button = GetNamedElement(document, buttonName);
            Assert.Equal("Button", button.Name.LocalName);
            Assert.Equal("{DynamicResource PrimaryActionButton}", (string?)button.Attribute("Style"));
        }
    }

    [Fact]
    public void DarkPrimaryActionButton_HoverMatchesNavigationAndIsThemeManaged()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement template = GetKeyedElement(document, "ControlTemplate", "DarkPrimaryActionButtonTemplate");
        XElement hoverTrigger = Assert.Single(
            template.Descendants(Presentation + "Trigger"),
            element =>
                (string?)element.Attribute("Property") == "IsMouseOver" &&
                (string?)element.Attribute("Value") == "True");
        XElement hoverBackground = Assert.Single(
            hoverTrigger.Descendants(Presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "Background");

        Assert.Equal("{DynamicResource NavHoverBrush}", (string?)hoverBackground.Attribute("Value"));

        XElement lightStyle = GetKeyedElement(document, "Style", "LightPrimaryActionButton");
        XElement darkStyle = GetKeyedElement(document, "Style", "DarkPrimaryActionButton");
        XElement activeStyle = GetKeyedElement(document, "Style", "PrimaryActionButton");
        Assert.NotNull(lightStyle);
        Assert.Contains(
            darkStyle.Descendants(Presentation + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "Template" &&
                (string?)setter.Attribute("Value") == "{StaticResource DarkPrimaryActionButtonTemplate}");
        Assert.Equal("{StaticResource LightPrimaryActionButton}", (string?)activeStyle.Attribute("BasedOn"));

        string themeManager = ReadSource("src", "ThemeManager.cs");
        Assert.Matches(
            @"\(\s*""PrimaryActionButton""\s*,\s*""LightPrimaryActionButton""\s*,\s*""DarkPrimaryActionButton""\s*\)",
            themeManager);
    }

    [Fact]
    public void AnnouncementAndSecurityIcons_UseRoleSpecificMdl2ThemeBrushes()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement noticeBar = GetNamedElement(document, "NoticeBar");
        XElement noticeIcon = GetNamedElement(document, "NoticeIcon");
        XElement securityText = GetNamedElement(document, "TxtSecurityWarning");
        XElement securityIcon = GetNamedElement(document, "SecurityWarningIcon");

        Assert.Contains(noticeIcon, noticeBar.Descendants());
        Assert.Contains(securityIcon, securityText.Parent!.Descendants());
        Assert.Equal("\uE789", (string?)noticeIcon.Attribute("Text"));
        Assert.Equal("\uE7BA", (string?)securityIcon.Attribute("Text"));

        Assert.Equal("Segoe MDL2 Assets", (string?)noticeIcon.Attribute("FontFamily"));
        Assert.Equal("Segoe MDL2 Assets", (string?)securityIcon.Attribute("FontFamily"));
        Assert.Equal(
            "{DynamicResource ThemeNoticeIconBrush}",
            (string?)noticeIcon.Attribute("Foreground"));
        Assert.Equal(
            "{DynamicResource ThemeWarningIconBrush}",
            (string?)securityIcon.Attribute("Foreground"));

        XElement noticeBrush = GetKeyedElement(document, "SolidColorBrush", "ThemeNoticeIconBrush");
        XElement warningBrush = GetKeyedElement(document, "SolidColorBrush", "ThemeWarningIconBrush");
        Assert.Equal("#1976D2", (string?)noticeBrush.Attribute("Color"));
        Assert.Equal("#CC0000", (string?)warningBrush.Attribute("Color"));

        string themeManager = ReadSource("src", "ThemeManager.cs");
        Assert.Matches(
            @"\(\s*""ThemeNoticeIconBrush""\s*,\s*""#1976D2""\s*,\s*""#E7EDF4""\s*\)",
            themeManager);
        Assert.Matches(
            @"\(\s*""ThemeWarningIconBrush""\s*,\s*""#CC0000""\s*,\s*""#E7EDF4""\s*\)",
            themeManager);
    }

    [Fact]
    public void OverlayLists_ReuseThemedThinScrollBar()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");

        foreach (string listName in new[] { "ListRunningProcesses", "ListVisualResetItems" })
        {
            XElement list = GetNamedElement(document, listName);
            Assert.Equal("Auto", (string?)list.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
            Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));

            XElement scrollBarStyle = Assert.Single(
                list.Descendants(Presentation + "Style"),
                style => (string?)style.Attribute("TargetType") is "ScrollBar" or "{x:Type ScrollBar}");
            Assert.Equal("{StaticResource ThinScrollBar}", (string?)scrollBarStyle.Attribute("BasedOn"));
        }
    }

    [Fact]
    public void ColorPicker_UsesThemeResourcesAndCustomColorControls()
    {
        XDocument document = LoadXaml("src", "ColorPickerWindow.xaml");
        XElement window = document.Root ?? throw new Xunit.Sdk.XunitException("ColorPickerWindow.xaml has no root element.");
        Assert.Equal("{DynamicResource ThemePageBackgroundBrush}", (string?)window.Attribute("Background"));

        string[] expectedThemeResources =
        [
            "ThemePageBackgroundBrush",
            "ThemeSurfaceBackgroundBrush",
            "ThemePrimaryTextBrush",
            "ThemeSecondaryTextBrush",
            "ThemeInputBackgroundBrush",
            "ThemeControlBorderBrush",
            "NavHoverBrush",
        ];
        string[] attributeValues = window
            .DescendantsAndSelf()
            .Attributes()
            .Select(attribute => attribute.Value)
            .ToArray();

        foreach (string resourceKey in expectedThemeResources)
        {
            Assert.Contains(attributeValues, value => value.Contains(
                $"DynamicResource {resourceKey}",
                StringComparison.Ordinal));
        }

        string themeManager = ReadSource("src", "ThemeManager.cs");
        foreach (string resourceKey in expectedThemeResources)
        {
            Assert.Contains($"(\"{resourceKey}\",", themeManager, StringComparison.Ordinal);
        }

        XElement colorField = GetNamedElement(document, "ColorField");
        XElement brightnessOverlay = GetNamedElement(document, "BrightnessOverlay");
        XElement colorFieldMarker = GetNamedElement(document, "ColorFieldMarker");
        Assert.Contains(brightnessOverlay, colorField.Descendants());
        Assert.Contains(colorFieldMarker, colorField.Descendants());
        Assert.True(
            colorField.Descendants(Presentation + "LinearGradientBrush").Count() >= 2,
            "The color field must layer hue and saturation gradients.");
        Assert.Equal("Black", (string?)brightnessOverlay.Attribute("Fill"));
        Assert.Equal("False", (string?)brightnessOverlay.Attribute("IsHitTestVisible"));

        XElement brightnessSlider = GetNamedElement(document, "BrightnessSlider");
        Assert.Equal("Slider", brightnessSlider.Name.LocalName);
        Assert.Equal("{StaticResource BrightnessSliderStyle}", (string?)brightnessSlider.Attribute("Style"));
        XElement sliderStyle = GetKeyedElement(document, "Style", "BrightnessSliderStyle");
        Assert.Contains(
            sliderStyle.Elements(Presentation + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "IsDirectionReversed" &&
                (string?)setter.Attribute("Value") == "False");
        Assert.NotEmpty(sliderStyle.Descendants(Presentation + "ControlTemplate"));
        XElement track = Assert.Single(sliderStyle.Descendants(Presentation + "Track"));
        Assert.Equal("{TemplateBinding IsDirectionReversed}", (string?)track.Attribute("IsDirectionReversed"));
        Assert.NotEmpty(sliderStyle.Descendants(Presentation + "Thumb"));

        foreach (string textBoxName in new[] { "TxtRed", "TxtGreen", "TxtBlue", "TxtHex" })
        {
            Assert.Equal("TextBox", GetNamedElement(document, textBoxName).Name.LocalName);
        }

        XElement confirmButton = GetNamedElement(document, "BtnConfirm");
        XElement cancelButton = GetNamedElement(document, "BtnCancel");
        Assert.Equal("Button", confirmButton.Name.LocalName);
        Assert.Equal("Button", cancelButton.Name.LocalName);
        Assert.Equal("Confirm_Click", (string?)confirmButton.Attribute("Click"));
        Assert.Equal("Cancel_Click", (string?)cancelButton.Attribute("Click"));
        Assert.Equal("True", (string?)confirmButton.Attribute("IsDefault"));
        Assert.Equal("True", (string?)cancelButton.Attribute("IsCancel"));
        Assert.Equal("Border", GetNamedElement(document, "ColorPreview").Name.LocalName);

        XElement primaryTemplate = GetKeyedElement(document, "ControlTemplate", "ColorPickerPrimaryButtonTemplate");
        XElement primaryHover = Assert.Single(
            primaryTemplate.Descendants(Presentation + "Trigger"),
            element =>
                (string?)element.Attribute("Property") == "IsMouseOver" &&
                (string?)element.Attribute("Value") == "True");
        Assert.Contains(
            primaryHover.Descendants(Presentation + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource NavHoverBrush}");
    }

    [Fact]
    public void ColorPicker_AppliesWindowThemeAndReplacesWinFormsDialog()
    {
        string colorPickerSource = ReadSource("src", "ColorPickerWindow.xaml.cs");
        Assert.Contains("ThemeManager.ApplyWindow(this);", colorPickerSource, StringComparison.Ordinal);
        Assert.Matches(@"SourceInitialized\s*\+=.*ThemeManager\.ApplyTitleBar\(this\)", colorPickerSource);
        Assert.Matches(@"Activated\s*\+=.*ThemeManager\.ApplyWindow\(this\)", colorPickerSource);
        Assert.Contains("SelectedColor", colorPickerSource, StringComparison.Ordinal);
        Assert.Contains("BrightnessSlider.Background = CreateBrightnessBrush();", colorPickerSource, StringComparison.Ordinal);
        Assert.Matches(@"new\s+(?:Media)?LinearGradientBrush", colorPickerSource);
        Assert.Contains("BtnConfirm.Content = Localization.Get(\"ColorPicker_Confirm\");", colorPickerSource, StringComparison.Ordinal);
        Assert.Contains("BtnCancel.Content = Localization.Get(\"ColorPicker_Cancel\");", colorPickerSource, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseLeftButtonDown=\"Confirm_PreviewMouseLeftButtonDown\"", ReadSource("src", "ColorPickerWindow.xaml"), StringComparison.Ordinal);
        Assert.Contains("DialogResult = true;", colorPickerSource, StringComparison.Ordinal);

        string controlPanelSource = ReadSource("src", "ControlPanelWindow.xaml.cs");
        Assert.Contains("new ColorPickerWindow(", controlPanelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms.ColorDialog", controlPanelSource, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"new\s+(?:System\.Windows\.Forms\.)?ColorDialog\s*\(", controlPanelSource);
    }

    [Fact]
    public void ColorPickerColorMath_RoundTripsCanonicalColorsAndHex()
    {
        System.Windows.Media.Color[] colors =
        [
            System.Windows.Media.Colors.Black,
            System.Windows.Media.Colors.White,
            System.Windows.Media.Colors.Red,
            System.Windows.Media.Colors.Lime,
            System.Windows.Media.Colors.Blue,
            System.Windows.Media.Colors.Gray,
            System.Windows.Media.Colors.Yellow,
            System.Windows.Media.Colors.Cyan,
            System.Windows.Media.Colors.Magenta,
            System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56),
            System.Windows.Media.Color.FromRgb(0x45, 0xAF, 0xFF),
        ];

        foreach (System.Windows.Media.Color color in colors)
        {
            HsvColor hsv = ColorPickerColorMath.RgbToHsv(color);
            Assert.Equal(color, ColorPickerColorMath.HsvToRgb(hsv));
            Assert.True(ColorPickerColorMath.TryParseHex(ColorPickerColorMath.ToHex(color), out System.Windows.Media.Color parsed));
            Assert.Equal(color, parsed);
        }

        Assert.True(ColorPickerColorMath.TryParseHex("#abc", out System.Windows.Media.Color shorthand));
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xAA, 0xBB, 0xCC), shorthand);
        Assert.True(ColorPickerColorMath.TryParseHex("123456", out System.Windows.Media.Color withoutHash));
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56), withoutHash);
        Assert.False(ColorPickerColorMath.TryParseHex("#12GG56", out _));
        Assert.False(ColorPickerColorMath.TryParseHex("#12345", out _));

        Assert.True(ColorPickerColorMath.TryParseRgb(" 95, 197, 255 ", out System.Windows.Media.Color rgb));
        Assert.Equal(System.Windows.Media.Color.FromRgb(95, 197, 255), rgb);
        Assert.Equal("95,197,255", ColorPickerColorMath.ToRgbString(rgb));
        Assert.False(ColorPickerColorMath.TryParseRgb("256,0,0", out _));
        Assert.False(ColorPickerColorMath.TryParseRgb("-1,0,0", out _));
        Assert.False(ColorPickerColorMath.TryParseRgb("1,2", out _));

        Assert.Equal(
            System.Windows.Media.Colors.Black,
            ColorPickerColorMath.HsvToRgb(double.NaN, double.PositiveInfinity, double.NegativeInfinity));
        Assert.Equal(System.Windows.Media.Colors.Red, ColorPickerColorMath.HsvToRgb(0, double.PositiveInfinity, 1));
        Assert.Equal(System.Windows.Media.Colors.Red, ColorPickerColorMath.HsvToRgb(0, 1, double.PositiveInfinity));
        Assert.Equal(System.Windows.Media.Colors.White, ColorPickerColorMath.HsvToRgb(0, double.NegativeInfinity, 1));

        HsvColor gray = ColorPickerColorMath.RgbToHsv(System.Windows.Media.Color.FromRgb(128, 128, 128));
        Assert.Equal(0, gray.Hue);
        Assert.Equal(0, gray.Saturation);
        Assert.Equal(128 / 255.0, gray.Value, 12);
    }

    private static XDocument LoadXaml(params string[] pathParts) =>
        XDocument.Parse(ReadSource(pathParts), LoadOptions.SetLineInfo);

    private static XElement GetNamedElement(XDocument document, string name) =>
        Assert.Single(
            document.Descendants(),
            element =>
                (string?)element.Attribute(Xaml + "Name") == name ||
                (string?)element.Attribute("Name") == name);

    private static XElement GetKeyedElement(XDocument document, string localName, string key) =>
        Assert.Single(
            document.Descendants(Presentation + localName),
            element => (string?)element.Attribute(Xaml + "Key") == key);

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
