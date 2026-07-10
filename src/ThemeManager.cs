using Microsoft.Win32;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfControls = System.Windows.Controls;
using WpfSystemColors = System.Windows.SystemColors;

namespace BASpark
{
    internal static class ThemeManager
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;
        private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
        private const int DarkCaptionColor = 0x00201A15;
        private const int DarkCaptionTextColor = 0x00F4EDE7;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        private static readonly (object Key, string Light, string Dark)[] PaletteEntries =
        [
            ("ThemePageBackgroundBrush", "#F5F7FA", "#151A20"),
            ("ThemeSurfaceBackgroundBrush", "#FFFFFF", "#202731"),
            ("ThemeElevatedBackgroundBrush", "#F8FAFF", "#27313C"),
            ("ThemeSubtleBackgroundBrush", "#F0F2F5", "#1B222B"),
            ("ThemeSidebarOverlayBrush", "#FFFFFF", "#151A20"),
            ("ThemeInputBackgroundBrush", "#FFFFFF", "#1B222B"),
            ("ThemeListBackgroundBrush", "#FDFDFF", "#1B222B"),
            ("ThemeListBorderBrush", "#E0E8F0", "#3A4654"),
            ("ThemeBorderBrush", "#EAECEF", "#3A4654"),
            ("ThemeControlBorderBrush", "#D6DEE8", "#3A4654"),
            ("ThemeSeparatorBrush", "#F0F2F5", "#3A4654"),
            ("ThemePrimaryTextBrush", "#333333", "#E7EDF4"),
            ("ThemeSecondaryTextBrush", "#666666", "#B7C2CE"),
            ("ThemeMutedTextBrush", "#999999", "#8793A0"),
            ("ThemeItemHoverBrush", "#E0F2FF", "#263847"),
            ("ThemeItemSelectedBrush", "#EBF5FF", "#27313C"),
            ("ThemeCardBackgroundBrush", "#FAFCFF", "#202731"),
            ("ThemeCardBorderBrush", "#DCE5F2", "#3A4654"),
            ("ThemeCardTitleBrush", "#263238", "#E7EDF4"),
            ("ThemeCardSecondaryTextBrush", "#455A64", "#B7C2CE"),
            ("ThemeCardDetailBrush", "#78909C", "#8793A0"),
            ("ThemeNoticeBackgroundBrush", "#E3F2FD", "#202C38"),
            ("ThemeNoticeBorderBrush", "#90CAF9", "#35556C"),
            ("ThemeNoticeTitleBrush", "#1976D2", "#6DB8FF"),
            ("ThemeNoticeTextBrush", "#424242", "#E7EDF4"),
            ("ThemeNoticeDateBrush", "#90CAF9", "#8FB9D8"),
            ("ThemeStatsBackgroundBrush", "#EBF5FF", "#202731"),
            ("ThemeScrollbarThumbBrush", "#B8C4CE", "#5A6A7F"),
            ("ThemeToggleOffBrush", "#DDDDDD", "#52606D"),
            ("ThemeToggleDisabledBrush", "#E0E0E0", "#3A4654"),
            ("ThemeToggleThumbBrush", "#FFFFFF", "#E7EDF4"),
            ("ThemeToggleDisabledThumbBrush", "#F5F5F5", "#8793A0"),
            ("ThemeDisabledTextBrush", "#A0A0A0", "#8793A0"),
            ("ThemeDangerButtonBackgroundBrush", "#FFF0F0", "#202731"),
            ("ThemeDangerButtonForegroundBrush", "#CC0000", "#FF6B6B"),
            ("ThemeDangerButtonBorderBrush", "#FFCCCC", "#6A3A47"),
            ("ThemeSecondaryButtonHoverBrush", "#F2F7FF", "#314050"),
            ("ThemeSecondaryButtonHoverBorderBrush", "#BFD6EC", "#5A6A7F"),
            ("ThemeSecondaryButtonHoverForegroundBrush", "#333333", "#F8FBFF"),
            ("ThemeSecondaryButtonPressedBrush", "#E8F1FB", "#2A3745"),
            ("ThemeSecondaryButtonPressedBorderBrush", "#AFC8E2", "#4E5D71"),
            ("ThemeButtonBackgroundBrush", "#FFFFFF", "#27313C"),
            ("ThemeUpdateButtonBackgroundBrush", "#E3F2FD", "#27313C"),
            ("ThemeNeutralBorderBrush", "#DDDDDD", "#3A4654"),
            ("ThemeSidebarBorderBrush", "#E0E0E0", "#3A4654"),
            ("ThemeDangerSoftBackgroundBrush", "#FFF5F5", "#202731"),
            ("ThemeDangerStrongBackgroundBrush", "#FFE0E0", "#202731"),
            ("ThemeTertiaryTextBrush", "#555555", "#B7C2CE"),
            ("ThemeLogHintBrush", "#888888", "#8793A0"),
            ("ThemeHintTextBrush", "#9BA3AF", "#8793A0"),
            ("ThemeSidebarVersionBrush", "#B0B8C3", "#8793A0"),
            ("ThemeSidebarCopyrightBrush", "#A0A8B3", "#8793A0"),
            ("NavForegroundBrush", "#666666", "#C5CED8"),
            ("NavHoverBrush", "#E0F2FF", "#263847"),
            ("SegmentSelectedBackgroundBrush", "#FFFFFF", "#202731"),
            ("SegmentSelectedForegroundBrush", "#45AFFF", "#45AFFF"),
            (WpfSystemColors.WindowBrushKey, "#FFFFFF", "#1B222B"),
            (WpfSystemColors.ControlBrushKey, "#FFFFFF", "#1B222B"),
            (WpfSystemColors.ControlLightBrushKey, "#F8FAFF", "#27313C"),
            (WpfSystemColors.ControlDarkBrushKey, "#D6DEE8", "#3A4654"),
            (WpfSystemColors.ControlTextBrushKey, "#333333", "#E7EDF4"),
            (WpfSystemColors.GrayTextBrushKey, "#999999", "#8793A0"),
            (WpfSystemColors.HighlightBrushKey, "#E0F2FF", "#263847"),
            (WpfSystemColors.HighlightTextBrushKey, "#333333", "#E7EDF4"),
        ];

        private static readonly IReadOnlyDictionary<object, SolidColorBrush> LightPalette = BuildPalette(dark: false);
        private static readonly IReadOnlyDictionary<object, SolidColorBrush> DarkPalette = BuildPalette(dark: true);
        private static readonly (object ActiveKey, string LightKey, string DarkKey)[] ThemeStyleEntries =
        [
            (typeof(WpfControls.ComboBoxItem), "LightComboBoxItemStyle", "DarkComboBoxItemStyle"),
            (typeof(WpfControls.ComboBox), "LightComboBoxStyle", "DarkComboBoxStyle"),
            (typeof(WpfControls.TextBox), "LightTextBoxStyle", "DarkTextBoxStyle"),
            (typeof(WpfControls.ListBox), "LightListBoxStyle", "DarkListBoxStyle"),
            (typeof(WpfControls.CheckBox), "LightCheckBoxStyle", "DarkCheckBoxStyle"),
            (typeof(WpfControls.ListBoxItem), "LightListBoxItemStyle", "DarkListBoxItemStyle"),
            ("SecondaryActionButton", "LightSecondaryActionButton", "DarkSecondaryActionButton"),
            ("DangerActionButton", "LightDangerActionButton", "DarkDangerActionButton"),
        ];
        private static readonly ConditionalWeakTable<ControlPanelWindow, AppliedTheme> AppliedThemes = new();

        private sealed class AppliedTheme
        {
            public bool? IsDark { get; set; }
        }

        public static bool IsDarkModeEnabled() =>
            ConfigManager.DarkMode switch
            {
                DarkModeOption.On => true,
                DarkModeOption.Off => false,
                _ => IsSystemAppDarkMode()
            };

        public static void ApplyTitleBar(Window window) =>
            SetTitleBarDarkMode(window, IsDarkModeEnabled());

        public static void ApplyControlPanel(ControlPanelWindow window)
        {
            bool dark = IsDarkModeEnabled();
            SetTitleBarDarkMode(window, dark);

            AppliedTheme state = AppliedThemes.GetValue(window, static _ => new AppliedTheme());
            if (state.IsDark == dark)
            {
                return;
            }

            IReadOnlyDictionary<object, SolidColorBrush> palette = dark ? DarkPalette : LightPalette;
            foreach (var pair in palette)
            {
                window.Resources[pair.Key] = pair.Value;
            }

            foreach (var entry in ThemeStyleEntries)
            {
                window.Resources[entry.ActiveKey] = window.Resources[dark ? entry.DarkKey : entry.LightKey];
            }

            state.IsDark = dark;
        }

        private static IReadOnlyDictionary<object, SolidColorBrush> BuildPalette(bool dark)
        {
            var palette = new Dictionary<object, SolidColorBrush>(PaletteEntries.Length);
            foreach (var entry in PaletteEntries)
            {
                var brush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(dark ? entry.Dark : entry.Light));
                brush.Freeze();
                palette[entry.Key] = brush;
            }

            return palette;
        }

        private static bool IsSystemAppDarkMode()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Failed to read the Windows app theme: {ex.Message}");
                return false;
            }
        }

        private static void SetTitleBarDarkMode(Window window, bool dark)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
            }

            int captionColor = dark ? DarkCaptionColor : DwmColorDefault;
            int textColor = dark ? DarkCaptionTextColor : DwmColorDefault;
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
            _ = SetWindowPos(
                handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);
    }
}
