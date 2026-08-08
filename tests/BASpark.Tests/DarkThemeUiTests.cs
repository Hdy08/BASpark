using System.Text;
using System.Xml.Linq;

namespace BASpark.Tests;

public class DarkThemeUiTests
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
    public void ControlPanel_DeclaresDynamicThemeResourcesAndDarkModeChoices()
    {
        XDocument document = LoadXaml("src", "ControlPanelWindow.xaml");
        XElement window = Assert.IsType<XElement>(document.Root);
        Assert.Equal("{DynamicResource ThemePageBackgroundBrush}", (string?)window.Attribute("Background"));

        string[] resources =
        [
            "ThemePageBackgroundBrush",
            "ThemeSurfaceBackgroundBrush",
            "ThemePrimaryTextBrush",
            "ThemeControlBorderBrush",
            "DarkComboBoxStyle",
            "DarkTextBoxStyle",
            "DarkListBoxStyle",
            "DarkCheckBoxStyle",
            "DarkPrimaryActionButton",
            "DarkSecondaryActionButton",
            "DarkDangerActionButton",
        ];

        foreach (string key in resources)
        {
            Assert.Single(
                document.Descendants(),
                element => (string?)element.Attribute(Xaml + "Key") == key);
        }

        Assert.Equal("DarkMode", (string?)GetNamedElement(document, "RadioDarkModeOff").Attribute("GroupName"));
        Assert.Equal("DarkMode", (string?)GetNamedElement(document, "RadioDarkModeOn").Attribute("GroupName"));
        Assert.Equal("DarkMode", (string?)GetNamedElement(document, "RadioDarkModeSystem").Attribute("GroupName"));
        Assert.Null((string?)GetNamedElement(document, "RadioDarkModeOn").Attribute("Checked"));

        Assert.Equal("Segoe MDL2 Assets", (string?)GetNamedElement(document, "NoticeIcon").Attribute("FontFamily"));
        Assert.Equal("{DynamicResource ThemeNoticeIconBrush}", (string?)GetNamedElement(document, "NoticeIcon").Attribute("Foreground"));
        Assert.Equal("Segoe MDL2 Assets", (string?)GetNamedElement(document, "SecurityWarningIcon").Attribute("FontFamily"));
        Assert.Equal("{DynamicResource ThemeWarningIconBrush}", (string?)GetNamedElement(document, "SecurityWarningIcon").Attribute("Foreground"));

        const string dynamicControlTextBrush = "{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}";
        Assert.Equal(dynamicControlTextBrush, (string?)GetNamedElement(document, "TxtAboutTitle").Attribute("Foreground"));
        Assert.Equal(dynamicControlTextBrush, (string?)GetNamedElement(document, "TxtOverlayRunning").Attribute("Foreground"));
        Assert.Equal(dynamicControlTextBrush, (string?)GetNamedElement(document, "TxtOverlayVisualReset").Attribute("Foreground"));
        Assert.Equal("{DynamicResource ThemeSidebarVersionBrush}", (string?)GetNamedElement(document, "TxtSidebarVersion").Attribute("Foreground"));

        XElement versionText = GetNamedElement(document, "VersionText");
        Assert.Equal(dynamicControlTextBrush, (string?)versionText.Parent?.Attribute("Foreground"));
    }

    [Fact]
    public void ApplyingSettings_RefreshesThemeWithoutChangingItAtRadioSelection()
    {
        string xaml = ReadSource("src", "ControlPanelWindow.xaml");
        string source = ReadSource("src", "ControlPanelWindow.xaml.cs");

        Assert.DoesNotContain("DarkMode_Changed", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkMode_Changed", source, StringComparison.Ordinal);

        int saveIndex = source.IndexOf("ConfigManager.Save(\"DarkMode\", selectedDarkMode);", StringComparison.Ordinal);
        int applyIndex = source.IndexOf("ApplyDarkMode();", saveIndex, StringComparison.Ordinal);
        int titleBarIndex = source.IndexOf("ThemeManager.RefreshTitleBarAfterInput(this);", applyIndex, StringComparison.Ordinal);
        int trayIndex = source.IndexOf("RefreshTrayTheme();", titleBarIndex, StringComparison.Ordinal);

        Assert.True(saveIndex >= 0);
        Assert.True(applyIndex > saveIndex);
        Assert.True(titleBarIndex > applyIndex);
        Assert.True(trayIndex > titleBarIndex);
    }

    [Fact]
    public void ThemeManager_UsesSeparatePalettesAndRestoresLightControlStyles()
    {
        string source = ReadSource("src", "ThemeManager.cs");

        Assert.Contains("LightPalette = BuildPalette(dark: false)", source, StringComparison.Ordinal);
        Assert.Contains("DarkPalette = BuildPalette(dark: true)", source, StringComparison.Ordinal);
        Assert.Contains("window.Resources.Remove(entry.ActiveKey);", source, StringComparison.Ordinal);
        Assert.Contains("ConfigManager.DarkMode switch", source, StringComparison.Ordinal);
        Assert.Contains("RefreshTitleBarAfterInput", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorPicker_ReplacesTheSystemColorDialogAndRoundTripsRgbValues()
    {
        System.Windows.Media.Color input = System.Windows.Media.Color.FromRgb(0x45, 0xAF, 0xFF);
        HsvColor hsv = ColorPickerColorMath.RgbToHsv(input);
        Assert.Equal(input, ColorPickerColorMath.HsvToRgb(hsv));
        Assert.Equal("69,175,255", ColorPickerColorMath.ToRgbString(input));
        Assert.True(ColorPickerColorMath.TryParseRgb("69,175,255", out System.Windows.Media.Color parsed));
        Assert.Equal(input, parsed);

        string panelSource = ReadSource("src", "ControlPanelWindow.xaml.cs");
        string pickerSource = ReadSource("src", "ColorPickerWindow.xaml.cs");
        Assert.Contains("new ColorPickerWindow(", panelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms.ColorDialog", panelSource, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.ApplyWindow(this);", pickerSource, StringComparison.Ordinal);
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
