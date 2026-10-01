using System;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace BASpark
{
    public static class Localization
    {
        private static readonly ResourceManager ResourceManager = new(
            "BASpark.Strings",
            typeof(Localization).Assembly);

        public const string CultureZhCn = "zh-CN";
        public const string CultureEn = "en";
        public const string CultureJa = "ja";

        public static string CurrentCultureName { get; private set; } = CultureZhCn;

        public static bool IsChineseLocale =>
            CurrentCultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        public static CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(CurrentCultureName);

        public static string Get(string key) =>
            ResourceManager.GetString(key, CurrentCulture) ?? key;

        public static string Get(string key, string cultureName) =>
            ResourceManager.GetString(key, CultureInfo.GetCultureInfo(NormalizeCulture(cultureName))) ?? key;

        public static string Format(string key, params object[] args) =>
            string.Format(CurrentCulture, Get(key), args);

        public static void ApplyCulture(string? cultureName)
        {
            CurrentCultureName = NormalizeCulture(cultureName);
            var culture = CurrentCulture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        public static string NormalizeCulture(string? cultureName)
        {
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                return CultureZhCn;
            }

            string normalized = cultureName.Trim();
            if (normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("zh-hans", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
            {
                return CultureZhCn;
            }

            if (normalized.Equals("ja", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ja-jp", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("ja-", StringComparison.OrdinalIgnoreCase))
            {
                return CultureJa;
            }

            if (normalized.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            {
                return CultureEn;
            }

            return CultureEn;
        }

        public static string DetectCultureFromSystem() =>
            NormalizeCulture(CultureInfo.CurrentUICulture.Name);
    }
}
