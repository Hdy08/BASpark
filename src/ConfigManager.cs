using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace BASpark
{
    [Flags]
    public enum VisualAppearanceResetFlags
    {
        None = 0,
        EffectScale = 1 << 0,
        EffectOpacity = 1 << 1,
        UnifiedAnimationSpeed = 1 << 2,
        TrailRefreshRate = 1 << 3,
        ParticleColor = 1 << 4,
        TrailAnimationSpeed = 1 << 5,
        ClickAnimationSpeed = 1 << 6,
        TrailThickness = 1 << 7
    }

    public enum ProcessFilterModeOption
    {
        Disabled,
        Blacklist,
        Whitelist
    }

    public enum PanelScrollbarVisibility
    {
        Always,
        OnScroll
    }

    public enum NetworkRegionOption
    {
        Auto,
        China,
        Global
    }

    public enum DarkModeOption
    {
        Off,
        On,
        System
    }

    public class FilterProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public ProcessFilterModeOption Mode { get; set; } = ProcessFilterModeOption.Blacklist;
        public List<string> Processes { get; set; } = new List<string>();
    }

    // 新增：多屏记忆
    public class ScreenSelectionState
    {
        public string IdentityKey { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    public static class ConfigManager
    {
        private const string RegPath = @"Software\BASpark";

        public static string ParticleColor { get; set; } = "45,175,255";
        public static bool IsEffectEnabled { get; set; } = true;
        public static bool AutoStart { get; set; } = false;
        public static bool AgreedToPrivacy { get; set; } = false;
        public static bool EnableTelemetry { get; set; } = false;
        public static int TotalClicks { get; set; } = 0;
        public static string LastNoticeContent { get; set; } = "";
        public static bool EnableAlwaysTrailEffect { get; set; } = false;
        public static bool StartSilent { get; set; } = false;
        public static bool RunAsAdmin { get; set; } = false;
        public static double EffectScale { get; set; } = 1.0;
        public static double TrailThickness { get; set; } = 1.0;
        public static double EffectOpacity { get; set; } = 1.0;
        public static double EffectSpeed { get; set; } = 1.0;
        public static bool UseLinkedAnimationSpeed { get; set; } = true;
        public static double TrailAnimationSpeed { get; set; } = 1.0;
        public static double ClickAnimationSpeed { get; set; } = 1.0;
        public static int TrailRefreshRate { get; set; } = 40;
        public static bool EnableEnvironmentFilter { get; set; } = false;
        public static bool HideInFullscreen { get; set; } = true;
        public static bool ShowEffectOnDesktop { get; set; } = true;
        public static string FilterProfiles { get; set; } = "";
        public static string ActiveProfileId { get; set; } = "";
        public static bool IsTouchscreenMode { get; set; } = false;
        public static int ClickTriggerType { get; set; } = 0; // 0:左, 1:右, 2:左右
        public static bool EnableMiddleClickTrigger { get; set; } = false;
        public static bool ScreenshotCompatibilityMode { get; set; } = false;
        public static string EnabledScreenIds { get; set; } = "";
        public static string ScreenSelections { get; set; } = "";
        public static string UiLanguage { get; set; } = "";
        public static NetworkRegionOption NetworkRegion { get; set; } = NetworkRegionOption.Auto;
        public static DarkModeOption DarkMode { get; set; } = DarkModeOption.System;
        public static PanelScrollbarVisibility ScrollbarVisibility { get; set; } = PanelScrollbarVisibility.OnScroll;
        public static string SidebarBackgroundImagePath { get; set; } = "";
        public static string TelemetryClientId { get; set; } = "";
        public static string LastTelemetrySentUtc { get; set; } = "";

        /// 点击特效是否处于激活状态（点击特效开关）
        public static bool IsClickEffectActive => IsEffectEnabled;

        /// 拖尾效果是否处于激活状态（点击特效或常驻拖尾任意一个开启）
        public static bool IsTrailEffectActive => IsEffectEnabled || EnableAlwaysTrailEffect;

        private static List<FilterProfile> _profiles = new List<FilterProfile>();

        // 缓存属性元数据，避免 Save() 每次都反射查找
        private static readonly ConcurrentDictionary<string, PropertyInfo?> _propertyCache = new();

        // 保护并发 Save / _profiles 访问
        private static readonly object _syncLock = new();

        public static void Load()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (key != null)
                    {
                        ParticleColor = key.GetValue("ParticleColor", "45,175,255")?.ToString() ?? "45,175,255";

                        IsEffectEnabled = ReadBool(key, "IsEffectEnabled", true);
                        AutoStart = ReadBool(key, "AutoStart", false);
                        AgreedToPrivacy = ReadBool(key, "AgreedToPrivacy", false);
                        EnableTelemetry = ReadBool(key, "EnableTelemetry", false);
                        TotalClicks = ReadClampedInt(key, "TotalClicks", 0, 0, int.MaxValue);
                        LastNoticeContent = key.GetValue("LastNoticeContent", "")?.ToString() ?? "";
                        EnableAlwaysTrailEffect = ReadBool(key, "EnableAlwaysTrailEffect", false);
                        StartSilent = ReadBool(key, "StartSilent", false);
                        RunAsAdmin = ReadBool(key, "RunAsAdmin", false);
                        EffectScale = ReadClampedDouble(key, "EffectScale", 1.0, 0.5, 3.0);
                        TrailThickness = ReadClampedDouble(key, "TrailThickness", 1.0, 0.5, 3.0);
                        EffectOpacity = ReadClampedDouble(key, "EffectOpacity", 1.0, 0.1, 1.0);
                        EffectSpeed = ReadClampedDouble(key, "EffectSpeed", 1.0, 0.2, 3.0);
                        UseLinkedAnimationSpeed = ReadBool(key, "UseLinkedAnimationSpeed", true);
                        TrailAnimationSpeed = ReadClampedDouble(key, "TrailAnimationSpeed", EffectSpeed, 0.2, 3.0);
                        ClickAnimationSpeed = ReadClampedDouble(key, "ClickAnimationSpeed", EffectSpeed, 0.2, 3.0);
                        TrailRefreshRate = ReadClampedInt(key, "TrailRefreshRate", 40, 10, 240);
                        EnableEnvironmentFilter = ReadBool(key, "EnableEnvironmentFilter", false);
                        HideInFullscreen = ReadBool(key, "HideInFullscreen", true);
                        ShowEffectOnDesktop = ReadBool(key, "ShowEffectOnDesktop", true);
                        IsTouchscreenMode = ReadBool(key, "IsTouchscreenMode", false);
                        ClickTriggerType = ReadClampedInt(key, "ClickTriggerType", 0, 0, 2);
                        EnableMiddleClickTrigger = ReadBool(key, "EnableMiddleClickTrigger", false);
                        ScreenshotCompatibilityMode = ReadBool(key, "ScreenshotCompatibilityMode", false);
                        EnabledScreenIds = key.GetValue("EnabledScreenIds", "")?.ToString() ?? "";
                        ScreenSelections = key.GetValue("ScreenSelections", "")?.ToString() ?? "";
                        UiLanguage = key.GetValue("UiLanguage", "")?.ToString() ?? "";
                        NetworkRegion = ParseNetworkRegion(key.GetValue("NetworkRegion", "Auto")?.ToString());
                        DarkMode = ParseDarkMode(key.GetValue("DarkMode", "System")?.ToString());
                        ScrollbarVisibility = ParseScrollbarVisibility(key.GetValue("ScrollbarVisibility", "OnScroll")?.ToString());
                        SidebarBackgroundImagePath = key.GetValue("SidebarBackgroundImagePath", "")?.ToString() ?? "";
                        TelemetryClientId = key.GetValue("TelemetryClientId", "")?.ToString() ?? "";
                        LastTelemetrySentUtc = key.GetValue("LastTelemetrySentUtc", "")?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(UiLanguage))
                        {
                            Localization.ApplyCulture(UiLanguage);
                        }

                        FilterProfiles = key.GetValue("FilterProfiles", "")?.ToString() ?? "";
                        ActiveProfileId = key.GetValue("ActiveProfileId", "")?.ToString() ?? "";

                        if (!string.IsNullOrEmpty(FilterProfiles))
                        {
                            try
                            {
                                _profiles = System.Text.Json.JsonSerializer.Deserialize<List<FilterProfile>>(FilterProfiles) ?? new List<FilterProfile>();
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn($"Failed to deserialize FilterProfiles; fallback to empty list: {ex.Message}");
                                _profiles = new List<FilterProfile>();
                            }
                        }

                        // 向后兼容处理
                        if (_profiles.Count == 0)
                        {
                            string processFilterModeRaw = key.GetValue("ProcessFilterMode", "Disabled")?.ToString() ?? "Disabled";
                            ProcessFilterModeOption oldMode;
                            if (!Enum.TryParse(processFilterModeRaw, true, out oldMode))
                            {
                                oldMode = ProcessFilterModeOption.Disabled;
                            }
                            string oldListRaw = key.GetValue("ProcessFilterList", "")?.ToString() ?? "";
                            var oldList = oldListRaw
                                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(s => s.ToLowerInvariant())
                                .Distinct()
                                .ToList();

                            var defaultProfile = new FilterProfile
                            {
                                Name = Localization.Get("Profile_Default"),
                                Mode = oldMode == ProcessFilterModeOption.Disabled ? ProcessFilterModeOption.Blacklist : oldMode,
                                Processes = oldList
                            };
                            _profiles.Add(defaultProfile);
                            ActiveProfileId = defaultProfile.Id;
                        }

                        if (string.IsNullOrEmpty(ActiveProfileId) && _profiles.Count > 0)
                        {
                            ActiveProfileId = _profiles[0].Id;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load config; some values may be missing: {ex.Message}");
            }
        }

        private static bool ReadBool(RegistryKey key, string name, bool fallback)
        {
            object? value = key.GetValue(name, fallback);
            if (value is string text)
            {
                if (bool.TryParse(text, out bool parsedBool))
                {
                    return parsedBool;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                {
                    return parsedInt != 0;
                }
            }

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            object? value = key.GetValue(name, fallback);
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static int ReadClampedInt(RegistryKey key, string name, int fallback, int min, int max)
        {
            return Math.Clamp(ReadInt(key, name, fallback), min, max);
        }

        private static double ReadClampedDouble(RegistryKey key, string name, double fallback, double min, double max)
        {
            object? value = key.GetValue(name, fallback);
            double parsed = TryReadDouble(value, out double result) ? result : fallback;
            if (!double.IsFinite(parsed))
            {
                parsed = fallback;
            }

            return Math.Clamp(parsed, min, max);
        }

        private static bool TryReadDouble(object? value, out double result)
        {
            if (value is string text)
            {
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                       double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
            }

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }

        public static PanelScrollbarVisibility ParseScrollbarVisibility(string? raw)
        {
            if (string.Equals(raw, "Always", StringComparison.OrdinalIgnoreCase))
            {
                return PanelScrollbarVisibility.Always;
            }

            return PanelScrollbarVisibility.OnScroll;
        }

        public static NetworkRegionOption ParseNetworkRegion(string? raw)
        {
            if (string.Equals(raw, "China", StringComparison.OrdinalIgnoreCase))
            {
                return NetworkRegionOption.China;
            }

            if (string.Equals(raw, "Global", StringComparison.OrdinalIgnoreCase))
            {
                return NetworkRegionOption.Global;
            }

            return NetworkRegionOption.Auto;
        }

        public static DarkModeOption ParseDarkMode(string? raw)
        {
            if (string.Equals(raw, "Off", StringComparison.OrdinalIgnoreCase))
            {
                return DarkModeOption.Off;
            }

            if (string.Equals(raw, "On", StringComparison.OrdinalIgnoreCase))
            {
                return DarkModeOption.On;
            }

            return DarkModeOption.System;
        }

        public static void GetAnimationSpeedsForOverlay(out double trailSpeed, out double clickSpeed)
        {
            if (UseLinkedAnimationSpeed)
            {
                trailSpeed = EffectSpeed;
                clickSpeed = EffectSpeed;
            }
            else
            {
                trailSpeed = TrailAnimationSpeed;
                clickSpeed = ClickAnimationSpeed;
            }
        }

        public static List<FilterProfile> GetProfiles()
        {
            lock (_syncLock) { return [.. _profiles]; }
        }

        public static FilterProfile? GetActiveProfile()
        {
            lock (_syncLock)
            {
                return _profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? _profiles.FirstOrDefault();
            }
        }

        public static void SaveProfiles(List<FilterProfile> profiles, string activeId)
        {
            lock (_syncLock)
            {
                _profiles = profiles;
                ActiveProfileId = activeId;
                string json = System.Text.Json.JsonSerializer.Serialize(_profiles);
                Save("FilterProfiles", json);
                Save("ActiveProfileId", activeId);
            }
        }

        /// 将选中的视觉表现项恢复默认值并写入注册表
        public static void ApplyVisualAppearanceDefaults(VisualAppearanceResetFlags flags)
        {
            if (flags == VisualAppearanceResetFlags.None)
            {
                return;
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.EffectScale))
            {
                Save("EffectScale", 1.0);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.EffectOpacity))
            {
                Save("EffectOpacity", 1.0);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.TrailThickness))
            {
                Save("TrailThickness", 1.0);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.UnifiedAnimationSpeed))
            {
                Save("UseLinkedAnimationSpeed", true);
                Save("EffectSpeed", 1.0);
                Save("TrailAnimationSpeed", 1.0);
                Save("ClickAnimationSpeed", 1.0);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.TrailAnimationSpeed))
            {
                Save("TrailAnimationSpeed", 1.0);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.ClickAnimationSpeed))
            {
                Save("ClickAnimationSpeed", 1.0);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.TrailRefreshRate))
            {
                Save("TrailRefreshRate", 40);
            }

            if (flags.HasFlag(VisualAppearanceResetFlags.ParticleColor))
            {
                Save("ParticleColor", "45,175,255");
            }
        }

        public static void Save(string name, object value)
        {
            try
            {
                lock (_syncLock)
                {
                    using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                    if (key == null)
                    {
                        AppLogger.Warn($"Failed to open config registry key while saving '{name}'.");
                        return;
                    }

                    key.SetValue(name, ToRegistryValue(value));

                    var prop = _propertyCache.GetOrAdd(name, n => typeof(ConfigManager).GetProperty(n));
                    if (prop != null)
                    {
                        object propertyValue = value;
                        if (prop.PropertyType.IsEnum)
                        {
                            if (value is string stringValue)
                            {
                                propertyValue = Enum.Parse(prop.PropertyType, stringValue, ignoreCase: true);
                            }
                            else
                            {
                                propertyValue = Enum.ToObject(prop.PropertyType, value);
                            }
                        }

                        prop.SetValue(null, propertyValue);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save config entry '{name}': {ex.Message}");
            }
        }

        private static object ToRegistryValue(object value)
        {
            return value switch
            {
                Enum enumValue => enumValue.ToString(),
                double doubleValue => doubleValue.ToString("R", CultureInfo.InvariantCulture),
                float floatValue => floatValue.ToString("R", CultureInfo.InvariantCulture),
                decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
                _ => value
            };
        }

        public static IReadOnlySet<string> GetProcessFilterEntries()
        {
            var profile = GetActiveProfile();
            if (profile == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return profile.Processes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static HashSet<string> GetEnabledScreenIds()
        {
            if (string.IsNullOrWhiteSpace(EnabledScreenIds))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(EnabledScreenIds) ?? new List<string>();
                return parsed
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to parse EnabledScreenIds; fallback to empty set: {ex.Message}");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void SaveEnabledScreenIds(IEnumerable<string> screenIds)
        {
            var normalized = screenIds
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string json = System.Text.Json.JsonSerializer.Serialize(normalized);
            Save("EnabledScreenIds", json);
        }

        public static List<ScreenSelectionState> GetScreenSelections()
        {
            if (string.IsNullOrWhiteSpace(ScreenSelections))
            {
                return new List<ScreenSelectionState>();
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<ScreenSelectionState>>(ScreenSelections) ?? new List<ScreenSelectionState>();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to parse ScreenSelections; fallback to empty list: {ex.Message}");
                return new List<ScreenSelectionState>();
            }
        }

        public static HashSet<string> ResolveEnabledScreenDeviceNames(IEnumerable<ScreenIdentityInfo> currentScreens)
        {
            var screens = currentScreens.ToList();
            var selections = GetScreenSelections();
            var legacyEnabledIds = GetEnabledScreenIds();
            bool hasSavedPreference = selections.Count > 0 || legacyEnabledIds.Count > 0;

            // 没有已记忆的可用屏幕时自动启用现有屏幕，避免 Sunshine 虚拟屏场景丢失特效(Issue #104)
            var enabled = screens
                .Where(screen => IsScreenEnabledByPreference(screen, selections, legacyEnabledIds))
                .Select(screen => screen.DeviceName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (enabled.Count == 0 && hasSavedPreference)
            {
                enabled = screens.Select(screen => screen.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return enabled;
        }

        public static void SaveScreenSelections(IEnumerable<ScreenSelectionState> screenSelections)
        {
            var incoming = screenSelections
                .Where(s => !string.IsNullOrWhiteSpace(s.IdentityKey) || !string.IsNullOrWhiteSpace(s.DeviceName))
                .Select(s => new ScreenSelectionState
                {
                    IdentityKey = NormalizeScreenValue(s.IdentityKey),
                    DeviceName = NormalizeScreenValue(s.DeviceName),
                    DisplayName = NormalizeScreenValue(s.DisplayName),
                    IsEnabled = s.IsEnabled
                })
                .ToList();

            var merged = GetScreenSelections();
            foreach (var item in incoming)
            {
                merged.RemoveAll(existing => IsSameSavedScreen(existing, item));
                merged.Add(item);
            }

            string json = System.Text.Json.JsonSerializer.Serialize(merged);
            Save("ScreenSelections", json);
            SaveEnabledScreenIds(incoming.Where(s => s.IsEnabled).Select(s => s.DeviceName));
        }

        private static bool IsScreenEnabledByPreference(
            ScreenIdentityInfo screen,
            List<ScreenSelectionState> selections,
            HashSet<string> legacyEnabledIds)
        {
            ScreenSelectionState? saved = selections.FirstOrDefault(selection => IsSameScreen(selection, screen));
            if (saved != null)
            {
                return saved.IsEnabled;
            }

            if (selections.Count > 0)
            {
                return true;
            }

            return legacyEnabledIds.Count == 0 || legacyEnabledIds.Contains(screen.DeviceName);
        }

        private static bool IsSameScreen(ScreenSelectionState selection, ScreenIdentityInfo screen)
        {
            return (!string.IsNullOrWhiteSpace(selection.IdentityKey) &&
                    string.Equals(selection.IdentityKey, screen.IdentityKey, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(selection.DeviceName) &&
                    string.Equals(selection.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSameSavedScreen(ScreenSelectionState left, ScreenSelectionState right)
        {
            return (!string.IsNullOrWhiteSpace(left.IdentityKey) &&
                    string.Equals(left.IdentityKey, right.IdentityKey, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(left.DeviceName) &&
                    string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeScreenValue(string value) => value.Trim();

        public static void ResetAndClear()
        {
            try
            {
                lock (_syncLock)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(RegPath, false);

                    string oldJson = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                    if (System.IO.File.Exists(oldJson))
                    {
                        System.IO.File.Delete(oldJson);
                    }

                    ParticleColor = "45,175,255";
                    IsEffectEnabled = true;
                    AutoStart = false;
                    AgreedToPrivacy = false;
                    EnableTelemetry = false;
                    TotalClicks = 0;
                    LastNoticeContent = "";
                    EnableAlwaysTrailEffect = false;
                    StartSilent = false;
                    RunAsAdmin = false;
                    EffectScale = 1.0;
                    TrailThickness = 1.0;
                    EffectOpacity = 1.0;
                    EffectSpeed = 1.0;
                    UseLinkedAnimationSpeed = true;
                    TrailAnimationSpeed = 1.0;
                    ClickAnimationSpeed = 1.0;
                    TrailRefreshRate = 40;
                    EnableEnvironmentFilter = false;
                    HideInFullscreen = true;
                    ShowEffectOnDesktop = true;
                    FilterProfiles = "";
                    ActiveProfileId = "";
                    _profiles.Clear();
                    IsTouchscreenMode = false;
                    ClickTriggerType = 0;
                    EnableMiddleClickTrigger = false;
                    ScreenshotCompatibilityMode = false;
                    EnabledScreenIds = "";
                    ScreenSelections = "";
                    UiLanguage = "";
                    NetworkRegion = NetworkRegionOption.Auto;
                    DarkMode = DarkModeOption.System;
                    SidebarBackgroundImagePath = "";
                    TelemetryClientId = "";
                    LastTelemetrySentUtc = "";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to reset config; registry/data may be partially cleared: {ex.Message}");
            }
        }
    }
}
