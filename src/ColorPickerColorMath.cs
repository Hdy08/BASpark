using System.Globalization;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace BASpark
{
    public readonly record struct HsvColor(double Hue, double Saturation, double Value);

    public static class ColorPickerColorMath
    {
        public static HsvColor RgbToHsv(MediaColor color)
        {
            double red = color.R / 255.0;
            double green = color.G / 255.0;
            double blue = color.B / 255.0;
            double maximum = Math.Max(red, Math.Max(green, blue));
            double minimum = Math.Min(red, Math.Min(green, blue));
            double delta = maximum - minimum;

            double hue;
            if (delta <= double.Epsilon)
            {
                hue = 0;
            }
            else if (maximum == red)
            {
                hue = 60 * (((green - blue) / delta) % 6);
            }
            else if (maximum == green)
            {
                hue = 60 * (((blue - red) / delta) + 2);
            }
            else
            {
                hue = 60 * (((red - green) / delta) + 4);
            }

            if (hue < 0)
            {
                hue += 360;
            }

            double saturation = maximum <= double.Epsilon ? 0 : delta / maximum;
            return new HsvColor(hue, saturation, maximum);
        }

        public static MediaColor HsvToRgb(HsvColor hsv) =>
            HsvToRgb(hsv.Hue, hsv.Saturation, hsv.Value);

        public static MediaColor HsvToRgb(double hue, double saturation, double value)
        {
            hue = NormalizeHue(hue);
            saturation = ClampUnit(saturation);
            value = ClampUnit(value);

            double chroma = value * saturation;
            double sector = hue / 60;
            double secondary = chroma * (1 - Math.Abs((sector % 2) - 1));
            (double red, double green, double blue) = sector switch
            {
                < 1 => (chroma, secondary, 0.0),
                < 2 => (secondary, chroma, 0.0),
                < 3 => (0.0, chroma, secondary),
                < 4 => (0.0, secondary, chroma),
                < 5 => (secondary, 0.0, chroma),
                _ => (chroma, 0.0, secondary)
            };

            double match = value - chroma;
            return MediaColor.FromRgb(
                ToByte(red + match),
                ToByte(green + match),
                ToByte(blue + match));
        }

        public static string ToHex(MediaColor color) =>
            $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        public static bool TryParseHex(string? text, out MediaColor color)
        {
            color = MediaColors.Transparent;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();
            if (value.StartsWith('#'))
            {
                value = value[1..];
            }

            if (value.Length == 3)
            {
                value = string.Concat(
                    value[0], value[0],
                    value[1], value[1],
                    value[2], value[2]);
            }

            if (value.Length != 6 ||
                !byte.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) ||
                !byte.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) ||
                !byte.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
            {
                return false;
            }

            color = MediaColor.FromRgb(red, green, blue);
            return true;
        }

        public static bool TryParseRgb(string? text, out MediaColor color)
        {
            color = MediaColors.Transparent;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Split(',');
            if (parts.Length != 3 ||
                !byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red) ||
                !byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green) ||
                !byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue))
            {
                return false;
            }

            color = MediaColor.FromRgb(red, green, blue);
            return true;
        }

        public static string ToRgbString(MediaColor color) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{color.R},{color.G},{color.B}");

        private static double NormalizeHue(double hue)
        {
            if (!double.IsFinite(hue))
            {
                return 0;
            }

            double normalized = hue % 360;
            return normalized < 0 ? normalized + 360 : normalized;
        }

        private static double ClampUnit(double value)
        {
            if (double.IsNaN(value) || double.IsNegativeInfinity(value))
            {
                return 0;
            }

            return double.IsPositiveInfinity(value) ? 1 : Math.Clamp(value, 0, 1);
        }

        private static byte ToByte(double value) =>
            (byte)Math.Clamp(
                Math.Round(value * 255, MidpointRounding.AwayFromZero),
                byte.MinValue,
                byte.MaxValue);
    }
}
