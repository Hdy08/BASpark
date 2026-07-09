using Microsoft.Win32;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using RadioButton = System.Windows.Controls.RadioButton;
using SystemColors = System.Windows.SystemColors;
using TextBox = System.Windows.Controls.TextBox;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

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
        private static readonly object MissingResource = new();
        private static readonly ConditionalWeakTable<DependencyObject, ThemeValues> OriginalValues = new();

        private sealed class ThemeValues
        {
            public Dictionary<DependencyProperty, object> Values { get; } = new();
            public Dictionary<object, object> Resources { get; } = new();
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
            ApplyControlPanelResources(window, dark);

            if (!dark)
            {
                RestoreElementTree(window);
                return;
            }

            var pageBrush = CreateBrush("#151A20");
            var panelBrush = CreateBrush("#202731");
            var elevatedBrush = CreateBrush("#27313C");
            var inputBrush = CreateBrush("#1B222B");
            var borderBrush = CreateBrush("#3A4654");
            var primaryText = CreateBrush("#E7EDF4");
            var secondaryText = CreateBrush("#B7C2CE");
            var mutedText = CreateBrush("#8793A0");

            SetThemeValue(window, Control.BackgroundProperty, pageBrush);
            SetThemeValue(window.SettingsHeaderBackground, Border.BackgroundProperty, pageBrush);

            foreach (DependencyObject item in EnumerateTree(window))
            {
                bool isControlTemplateChrome = IsControlTemplateChrome(item);
                switch (item)
                {
                    case Border border:
                        if (isControlTemplateChrome)
                        {
                            break;
                        }
                        if (IsSegmentContainerBorder(border))
                        {
                            SetThemeValue(border, Border.BackgroundProperty, pageBrush);
                        }
                        if (IsLightSurfaceBrush(border.Background))
                        {
                            SetThemeValue(border, Border.BackgroundProperty, panelBrush);
                        }
                        if (IsLightBorderBrush(border.BorderBrush))
                        {
                            SetThemeValue(border, Border.BorderBrushProperty, borderBrush);
                        }
                        break;

                    case Grid grid:
                        if (isControlTemplateChrome)
                        {
                            break;
                        }
                        if (IsLightSurfaceBrush(grid.Background))
                        {
                            SetThemeValue(grid, Panel.BackgroundProperty, pageBrush);
                        }
                        break;

                    case Panel panel:
                        if (isControlTemplateChrome)
                        {
                            break;
                        }
                        if (IsLightSurfaceBrush(panel.Background))
                        {
                            SetThemeValue(panel, Panel.BackgroundProperty, panelBrush);
                        }
                        break;

                    case TextBlock textBlock:
                        if (isControlTemplateChrome)
                        {
                            break;
                        }
                        ApplyDarkText(textBlock, primaryText, secondaryText, mutedText);
                        break;

                    case TextBox textBox:
                        SetThemeValue(
                            textBox,
                            Control.BackgroundProperty,
                            textBox.Name == "TxtAppLog" ? System.Windows.Media.Brushes.Transparent : inputBrush);
                        SetThemeValue(textBox, Control.ForegroundProperty, primaryText);
                        SetThemeValue(textBox, Control.BorderBrushProperty, borderBrush);
                        break;

                    case ComboBox comboBox:
                        ApplyDarkControlResources(comboBox, inputBrush, borderBrush, primaryText, mutedText);
                        SetThemeValue(comboBox, Control.BackgroundProperty, inputBrush);
                        SetThemeValue(comboBox, Control.ForegroundProperty, primaryText);
                        SetThemeValue(comboBox, Control.BorderBrushProperty, borderBrush);
                        comboBox.ApplyTemplate();
                        break;

                    case ListBox listBox:
                        SetThemeValue(listBox, Control.BackgroundProperty, inputBrush);
                        SetThemeValue(listBox, Control.ForegroundProperty, primaryText);
                        SetThemeValue(listBox, Control.BorderBrushProperty, borderBrush);
                        break;

                    case Button button:
                        ApplyDarkControlResources(button, elevatedBrush, borderBrush, primaryText, mutedText);
                        if (IsLightSurfaceBrush(button.Background))
                        {
                            SetThemeValue(button, Control.BackgroundProperty, elevatedBrush);
                        }
                        if (IsLightBorderBrush(button.BorderBrush))
                        {
                            SetThemeValue(button, Control.BorderBrushProperty, borderBrush);
                        }
                        if (IsNeutralTextBrush(button.Foreground))
                        {
                            SetThemeValue(button, Control.ForegroundProperty, primaryText);
                        }
                        button.ApplyTemplate();
                        break;

                    case CheckBox checkBox:
                        if (IsNeutralTextBrush(checkBox.Foreground))
                        {
                            SetThemeValue(checkBox, Control.ForegroundProperty, primaryText);
                        }
                        break;

                    case Separator separator:
                        SetThemeValue(separator, Control.BackgroundProperty, borderBrush);
                        break;
                }
            }
        }

        private static void ApplyControlPanelResources(ControlPanelWindow window, bool dark)
        {
            SetResource(window, "NavForegroundBrush", dark ? "#C5CED8" : "#666666");
            SetResource(window, "NavHoverBrush", dark ? "#263847" : "#E0F2FF");
            SetResource(window, "SegmentNormalForegroundBrush", dark ? "#E7EDF4" : "#333333");
            SetResource(window, "SegmentSelectedBackgroundBrush", dark ? "#202731" : "#FFFFFF");
            SetResource(window, "SegmentSelectedForegroundBrush", "#45AFFF");
            SetResource(window, "ThemeInputBackgroundBrush", dark ? "#1B222B" : "#FFFFFF");
            SetResource(window, "ThemeItemHoverBrush", dark ? "#263847" : "#E0F2FF");
            SetResource(window, "ThemeItemSelectedBrush", dark ? "#27313C" : "#EBF5FF");
            SetResource(window, "ThemePrimaryTextBrush", dark ? "#E7EDF4" : "#333333");
            SetResource(window, "ThemeSecondaryTextBrush", dark ? "#B7C2CE" : "#666666");
            SetResource(window, "ThemeMutedTextBrush", dark ? "#8793A0" : "#999999");
            SetResource(window, "ThemeCardBackgroundBrush", dark ? "#202731" : "#FAFCFF");
            SetResource(window, "ThemeCardBorderBrush", dark ? "#3A4654" : "#DCE5F2");
            SetResource(window, "ThemeCardTitleBrush", dark ? "#E7EDF4" : "#263238");
            SetResource(window, "ThemeCardSecondaryTextBrush", dark ? "#B7C2CE" : "#455A64");
            SetResource(window, "ThemeCardDetailBrush", dark ? "#8793A0" : "#78909C");
            SetResource(window, "ThemeDangerButtonBackgroundBrush", dark ? "#202731" : "#FFF0F0");
            SetResource(window, "ThemeDangerButtonForegroundBrush", dark ? "#FF6B6B" : "#CC0000");
            SetResource(window, "ThemeDangerButtonBorderBrush", dark ? "#6A3A47" : "#FFCCCC");
            SetResource(window, "ThemeSecondaryButtonHoverBrush", dark ? "#314050" : "#F2F7FF");
            SetResource(window, "ThemeSecondaryButtonHoverBorderBrush", dark ? "#5A6A7F" : "#BFD6EC");
            SetResource(window, "ThemeSecondaryButtonHoverForegroundBrush", dark ? "#F8FBFF" : "#333333");
            SetResource(window, "ThemeSecondaryButtonPressedBrush", dark ? "#2A3745" : "#E8F1FB");
            SetResource(window, "ThemeSecondaryButtonPressedBorderBrush", dark ? "#4E5D71" : "#AFC8E2");
            SetResource(window, "ThemeDangerButtonHoverBrush", dark ? "#3C2931" : "#FFE6E6");
            SetResource(window, "ThemeDangerButtonHoverBorderBrush", dark ? "#8B4A5A" : "#FFBDBD");
            SetResource(window, "ThemeDangerButtonHoverForegroundBrush", dark ? "#FFD7D7" : "#B91C1C");
        }

        private static void ApplyDarkText(
            TextBlock textBlock,
            MediaBrush primaryText,
            MediaBrush secondaryText,
            MediaBrush mutedText)
        {
            if (textBlock.Foreground is not SolidColorBrush brush || !IsNeutralTextColor(brush.Color))
            {
                return;
            }

            double luminance = GetLuminance(brush.Color);
            MediaBrush replacement = luminance > 0.55 ? mutedText :
                luminance > 0.18 ? secondaryText :
                primaryText;

            SetThemeValue(textBlock, TextBlock.ForegroundProperty, replacement);
        }

        private static void ApplyDarkControlResources(
            FrameworkElement element,
            MediaBrush background,
            MediaBrush border,
            MediaBrush primaryText,
            MediaBrush mutedText)
        {
            SetThemeResource(element, SystemColors.WindowBrushKey, background);
            SetThemeResource(element, SystemColors.ControlBrushKey, background);
            SetThemeResource(element, SystemColors.ControlLightBrushKey, background);
            SetThemeResource(element, SystemColors.ControlDarkBrushKey, border);
            SetThemeResource(element, SystemColors.ControlTextBrushKey, primaryText);
            SetThemeResource(element, SystemColors.GrayTextBrushKey, mutedText);
            SetThemeResource(element, SystemColors.HighlightBrushKey, CreateBrush("#263847"));
            SetThemeResource(element, SystemColors.HighlightTextBrushKey, primaryText);
        }

        private static bool IsSystemAppDarkMode()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? raw = key?.GetValue("AppsUseLightTheme");
                return raw is int intValue && intValue == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SetTitleBarDarkMode(Window window, bool dark)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                void ApplyWhenReady(object? sender, EventArgs args)
                {
                    window.SourceInitialized -= ApplyWhenReady;
                    SetTitleBarDarkMode(window, IsDarkModeEnabled());
                }

                window.SourceInitialized += ApplyWhenReady;
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

        private static void SetResource(FrameworkElement element, string key, string color)
        {
            if (element.Resources.Contains(key))
            {
                element.Resources[key] = CreateBrush(color);
            }
        }

        private static void SetThemeValue(DependencyObject item, DependencyProperty property, object value)
        {
            ThemeValues values = OriginalValues.GetOrCreateValue(item);
            if (!values.Values.ContainsKey(property))
            {
                values.Values[property] = item.ReadLocalValue(property);
            }

            item.SetValue(property, value);
        }

        private static void SetThemeResource(FrameworkElement element, object key, object value)
        {
            ThemeValues values = OriginalValues.GetOrCreateValue(element);
            if (!values.Resources.ContainsKey(key))
            {
                values.Resources[key] = element.Resources.Contains(key)
                    ? element.Resources[key]!
                    : MissingResource;
            }

            element.Resources[key] = value;
        }

        private static void RestoreElementTree(DependencyObject root)
        {
            foreach (DependencyObject item in EnumerateTree(root))
            {
                RestoreThemeValues(item);
            }
        }

        private static void RestoreThemeValues(DependencyObject item)
        {
            if (!OriginalValues.TryGetValue(item, out ThemeValues? values))
            {
                return;
            }

            foreach (var pair in values.Values)
            {
                if (pair.Value == DependencyProperty.UnsetValue)
                {
                    item.ClearValue(pair.Key);
                }
                else
                {
                    item.SetValue(pair.Key, pair.Value);
                }
            }

            if (item is FrameworkElement element)
            {
                foreach (var pair in values.Resources)
                {
                    if (ReferenceEquals(pair.Value, MissingResource))
                    {
                        element.Resources.Remove(pair.Key);
                    }
                    else
                    {
                        element.Resources[pair.Key] = pair.Value;
                    }
                }
            }

            if (item is Control control)
            {
                control.ApplyTemplate();
            }
        }

        private static IEnumerable<DependencyObject> EnumerateTree(DependencyObject root)
        {
            var visited = new HashSet<DependencyObject>();
            foreach (DependencyObject item in EnumerateTree(root, visited))
            {
                yield return item;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateTree(DependencyObject root, HashSet<DependencyObject> visited)
        {
            if (!visited.Add(root))
            {
                yield break;
            }

            yield return root;

            int count = 0;
            try
            {
                count = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                count = 0;
            }

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                foreach (DependencyObject descendant in EnumerateTree(child, visited))
                {
                    yield return descendant;
                }
            }

            foreach (object logicalChild in LogicalTreeHelper.GetChildren(root))
            {
                if (logicalChild is DependencyObject child)
                {
                    foreach (DependencyObject descendant in EnumerateTree(child, visited))
                    {
                        yield return descendant;
                    }
                }
            }
        }

        private static bool IsLightSurfaceBrush(MediaBrush? brush) =>
            brush switch
            {
                SolidColorBrush solid => IsLightSurfaceColor(solid.Color),
                GradientBrush gradient => gradient.GradientStops.Count > 0 &&
                    gradient.GradientStops.All(stop => IsLightSurfaceColor(stop.Color)),
                _ => false
            };

        private static bool IsLightBorderBrush(MediaBrush? brush) =>
            brush switch
            {
                SolidColorBrush solid => IsLightBorderColor(solid.Color),
                GradientBrush gradient => gradient.GradientStops.Count > 0 &&
                    gradient.GradientStops.All(stop => IsLightBorderColor(stop.Color)),
                _ => false
            };

        private static bool IsNeutralTextBrush(MediaBrush? brush) =>
            brush is SolidColorBrush solid && IsNeutralTextColor(solid.Color);

        private static bool IsControlTemplateChrome(DependencyObject item) =>
            item is FrameworkElement { TemplatedParent: Button or CheckBox or RadioButton };

        private static bool IsSegmentContainerBorder(Border border) =>
            border.Child is StackPanel { Orientation: Orientation.Horizontal } stackPanel &&
            stackPanel.Children.OfType<RadioButton>().Any();

        private static bool IsLightSurfaceColor(MediaColor color)
        {
            double luminance = GetLuminance(color);
            double saturation = GetSaturation(color);
            bool lightNeutral = luminance > 0.82 && saturation < 0.45;
            bool paleBlue = luminance > 0.84 && color.B >= color.R && color.G > 220;
            return color.A > 0 && (lightNeutral || paleBlue);
        }

        private static bool IsNeutralTextColor(MediaColor color) =>
            color.A > 0 &&
            GetSaturation(color) < 0.45 &&
            GetLuminance(color) < 0.78;

        private static bool IsLightBorderColor(MediaColor color) =>
            color.A > 0 &&
            GetLuminance(color) > 0.7 &&
            GetSaturation(color) < 0.5;

        private static double GetLuminance(MediaColor color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double GetSaturation(MediaColor color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            return max == 0 ? 0 : (max - min) / max;
        }

        private static SolidColorBrush CreateBrush(string color) =>
            new((MediaColor)ColorConverter.ConvertFromString(color));

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
