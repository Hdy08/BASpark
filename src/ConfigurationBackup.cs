using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BASpark;

public sealed record BackupSettingDefinition(string Key, string GroupKey, string TitleKey);

public sealed class ConfigurationBackupDocument
{
    public string Format { get; init; } = "BASpark.Configuration";
    public int Version { get; init; } = 1;
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, JsonElement> Data { get; init; } = new(StringComparer.Ordinal);
}

public sealed record BackupProfileIdentity(string Id, string Name);
public sealed record BackupProfileList(string ActiveId, List<BackupProfileIdentity> Items);
public sealed record BackupProfileMode(string Id, string Name, ProcessFilterModeOption Mode);
public sealed record BackupProfileProcesses(string Id, string Name, List<string> Processes);

public static class ConfigurationBackup
{
    private const int MaximumFileSize = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<BackupSettingDefinition> Settings { get; } =
    [
        new("TotalClicks", "Backup_Statistics", "Backup_TotalClicks"),
        new("UiLanguage", "Basic_Title", "Basic_Language"),
        new("DarkMode", "Basic_Title", "Basic_DarkMode"),
        new("EnableAlwaysTrailEffect", "Basic_Title", "Basic_TrailSwitch"),
        new("IsEffectEnabled", "Basic_Title", "Basic_MasterSwitch"),
        new("ClickTriggerType", "Basic_Title", "Basic_ClickType"),
        new("EnableMiddleClickTrigger", "Basic_Title", "Basic_MiddleClick"),
        new("ScreenshotCompatibilityMode", "Basic_Title", "Basic_ScreenshotMode"),
        new("AutoStart", "Basic_Title", "Basic_AutoStart"),
        new("StartSilent", "Basic_Title", "Basic_StartSilent"),
        new("HideTrayIcon", "Basic_Title", "Basic_HideTrayIcon"),
        new("RunAsAdmin", "Basic_Title", "Basic_RunAsAdmin"),
        new("IsTouchscreenMode", "Basic_Title", "Basic_Touchscreen"),
        new("UseLinkedEffectScale", "Visual_Title", "Visual_LinkedScale"),
        new("EffectScale", "Visual_Title", "VisualReset_UnifiedScale"),
        new("TrailEffectScale", "Visual_Title", "VisualReset_TrailScale"),
        new("ClickEffectScale", "Visual_Title", "VisualReset_ClickScale"),
        new("GlowIntensity", "Visual_Title", "VisualReset_GlowIntensity"),
        new("UseLinkedAnimationSpeed", "Visual_Title", "Visual_LinkedSpeed"),
        new("EffectSpeed", "Visual_Title", "VisualReset_UnifiedSpeed"),
        new("TrailAnimationSpeed", "Visual_Title", "VisualReset_TrailSpeed"),
        new("ClickAnimationSpeed", "Visual_Title", "VisualReset_ClickSpeed"),
        new("ApplyCurveDraw", "Visual_Title", "Visual_CurveDraw"),
        new("FollowDisplayRefreshRate", "Visual_Title", "Visual_FollowDisplayRefreshRate"),
        new("TrailRefreshRate", "Visual_Title", "VisualReset_TrailRefresh"),
        new("ParticleColor", "Visual_Title", "VisualReset_Color"),
        new("EnableEnvironmentFilter", "Filter_Title", "Filter_Enable"),
        new("HideInFullscreen", "Filter_Title", "Filter_Fullscreen"),
        new("ShowEffectOnDesktop", "Filter_Title", "Filter_Desktop"),
        new("Profiles", "Filter_Title", "Filter_ProfileGroup")
    ];

    private static readonly IReadOnlyDictionary<string, (double Minimum, double Maximum)> NumericRanges =
        new Dictionary<string, (double, double)>(StringComparer.Ordinal)
        {
            ["TotalClicks"] = (0, int.MaxValue),
            ["ClickTriggerType"] = (0, 2),
            ["EffectScale"] = (0.5, 3),
            ["TrailEffectScale"] = (0.5, 3),
            ["ClickEffectScale"] = (0.5, 3),
            ["GlowIntensity"] = (0, 3),
            ["EffectSpeed"] = (0.2, 3),
            ["TrailAnimationSpeed"] = (0.2, 3),
            ["ClickAnimationSpeed"] = (0.2, 3),
            ["EffectOpacity"] = (0.1, 1),
            ["TrailRefreshRate"] = (30, 360)
        };

    public static ConfigurationBackupDocument Capture(IEnumerable<ScreenSelectionState> currentScreens)
    {
        var document = new ConfigurationBackupDocument();
        foreach (BackupSettingDefinition setting in Settings)
        {
            PropertyInfo? property = typeof(ConfigManager).GetProperty(setting.Key);
            if (property == null) continue;
            object value = property.GetValue(null)!;
            if (setting.Key == "ParticleColor") value = ColorPickerColorMath.CombineThemeColor(ConfigManager.ParticleColor, ConfigManager.EffectOpacity);
            if (setting.Key == "UiLanguage" && string.IsNullOrWhiteSpace((string)value))
                value = Localization.CurrentCultureName;
            document.Data[setting.Key] = JsonSerializer.SerializeToElement(value, property.PropertyType, JsonOptions);
        }
        SetProfiles(document, ConfigManager.GetProfiles(), ConfigManager.ActiveProfileId);
        var screens = ConfigManager.GetScreenSelections();
        foreach (ScreenSelectionState screen in currentScreens)
        {
            screens.RemoveAll(existing => GetScreenKey(existing) == GetScreenKey(screen));
            screens.Add(screen);
        }
        SetScreens(document, screens);
        return document;
    }

    public static void SetProfiles(ConfigurationBackupDocument document, IEnumerable<FilterProfile> profiles, string activeId)
    {
        var items = profiles.ToList();
        document.Data["Profiles"] = JsonSerializer.SerializeToElement(
            new BackupProfileList(activeId, items.Select(profile => new BackupProfileIdentity(profile.Id, profile.Name)).ToList()), JsonOptions);
        document.Data["Profile.Mode"] = JsonSerializer.SerializeToElement(
            items.Select(profile => new BackupProfileMode(profile.Id, profile.Name, profile.Mode)).ToList(), JsonOptions);
        document.Data["Profile.Processes"] = JsonSerializer.SerializeToElement(
            items.Select(profile => new BackupProfileProcesses(profile.Id, profile.Name, [.. profile.Processes])).ToList(), JsonOptions);
    }

    public static void SetScreens(ConfigurationBackupDocument document, IEnumerable<ScreenSelectionState> screens)
    {
        foreach (string key in document.Data.Keys.Where(IsScreenKey).ToArray()) document.Data.Remove(key);
        foreach (ScreenSelectionState screen in screens)
            document.Data[GetScreenKey(screen)] = JsonSerializer.SerializeToElement(screen, JsonOptions);
    }

    public static bool IsScreenKey(string key) => key.StartsWith("Screen/", StringComparison.Ordinal);
    public static string GetScreenKey(ScreenSelectionState screen) =>
        "Screen/" + (string.IsNullOrWhiteSpace(screen.IdentityKey) ? screen.DeviceName : screen.IdentityKey);

    public static ConfigurationBackupDocument Select(ConfigurationBackupDocument source, IEnumerable<string> keys)
    {
        string[] selected = keys.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0) throw new InvalidDataException(Localization.Get("Backup_SelectData"));
        var document = new ConfigurationBackupDocument();
        foreach (string key in selected)
        {
            string[] included = key == "Profiles"
                ? new[] { "Profiles", "Profile.Mode", "Profile.Processes" }.Where(source.Data.ContainsKey).ToArray()
                : [key];
            if (included.Length == 0) throw InvalidValue(key);
            foreach (string field in included)
            {
                if (!source.Data.TryGetValue(field, out JsonElement value)) throw InvalidValue(field);
                ValidateValue(field, value);
                document.Data.TryAdd(field, value.Clone());
            }
        }
        return document;
    }

    public static string Serialize(ConfigurationBackupDocument document) => JsonSerializer.Serialize(document, JsonOptions);

    public static ConfigurationBackupDocument Parse(string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaximumFileSize)
            throw new InvalidDataException(Localization.Get("Backup_InvalidFile"));
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(Localization.Get("Backup_InvalidFile"));
            CheckDuplicateProperties(json.RootElement);
            if (!json.RootElement.TryGetProperty("Format", out JsonElement format) ||
                format.ValueKind != JsonValueKind.String || format.GetString() != "BASpark.Configuration" ||
                !json.RootElement.TryGetProperty("Version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int formatVersion) || formatVersion != 1)
                throw new InvalidDataException(Localization.Get("Backup_InvalidFile"));
            if (json.RootElement.TryGetProperty("Data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
                CheckDuplicateProperties(data);
            var document = JsonSerializer.Deserialize<ConfigurationBackupDocument>(content, JsonOptions);
            if (document == null || document.Format != "BASpark.Configuration" || document.Version != 1 ||
                document.Data == null || document.Data.Count is 0 or > 1024)
                throw new InvalidDataException(Localization.Get("Backup_InvalidFile"));
            foreach (var item in document.Data) ValidateValue(item.Key, item.Value);
            if (document.Data.TryGetValue("EffectOpacity", out JsonElement legacyOpacity))
            {
                string color = document.Data.TryGetValue("ParticleColor", out JsonElement legacyColor) ? legacyColor.GetString()! : ConfigManager.ParticleColor;
                if (!ColorPickerColorMath.TryParseArgbHex(color, out _))
                    document.Data["ParticleColor"] = JsonSerializer.SerializeToElement(ColorPickerColorMath.CombineThemeColor(color, legacyOpacity.GetDouble()));
                document.Data.Remove("EffectOpacity");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(Localization.Get("Backup_InvalidFile"), exception);
        }
    }

    private static void CheckDuplicateProperties(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException(Localization.Get("Backup_InvalidFile"));
    }

    public static async Task<ConfigurationBackupDocument> ReadAsync(string path)
    {
        if (new FileInfo(path).Length > MaximumFileSize)
            throw new InvalidDataException(Localization.Get("Backup_InvalidFile"));
        return Parse(await File.ReadAllTextAsync(path, Encoding.UTF8));
    }

    public static async Task<string> WriteAsync(string directory, ConfigurationBackupDocument source, IEnumerable<string> keys)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Serialize(Select(source, keys)));
        while (true)
        {
            DateTime exportedAt = DateTime.Now;
            string path = Path.Combine(directory, $"BASpark_Config_{exportedAt:yyyyMMdd-HHmmss}.json");
            bool created = false;
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                created = true;
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                return path;
            }
            catch (IOException) when (!created && File.Exists(path))
            {
                await Task.Delay(Math.Max(1, 1000 - DateTime.Now.Millisecond));
            }
            catch
            {
                if (created)
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                throw;
            }
        }
    }

    public static Dictionary<string, object> BuildImportValues(ConfigurationBackupDocument source,
        IEnumerable<string> keys, ConfigurationBackupDocument current)
    {
        ConfigurationBackupDocument selected = Select(source, keys);
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var item in selected.Data)
        {
            if (IsScreenKey(item.Key) || item.Key is "Profiles" or "Profile.Mode" or "Profile.Processes") continue;
            values[item.Key] = ReadSimpleValue(item.Key, item.Value);
            if (item.Key == "ParticleColor" && ColorPickerColorMath.TryParseArgbHex((string)values[item.Key], out var themeColor))
            {
                values[item.Key] = ColorPickerColorMath.ToRgbString(themeColor);
                values["EffectOpacity"] = themeColor.A / 255.0;
            }
        }
        if (selected.Data.Keys.Any(key => key is "Profiles" or "Profile.Mode" or "Profile.Processes"))
        {
            List<FilterProfile> profiles = ReadCurrentProfiles(current);
            string activeId = Read<BackupProfileList>("Profiles", current.Data["Profiles"]).ActiveId;
            if (selected.Data.TryGetValue("Profiles", out JsonElement profileData))
            {
                var incoming = Read<BackupProfileList>("Profiles", profileData);
                profiles = incoming.Items.Select(identity =>
                {
                    FilterProfile? previous = FindProfile(profiles, identity.Id, identity.Name);
                    return new FilterProfile
                    {
                        Id = identity.Id, Name = identity.Name,
                        Mode = previous?.Mode ?? ProcessFilterModeOption.Blacklist,
                        Processes = previous?.Processes.ToList() ?? []
                    };
                }).ToList();
                activeId = incoming.ActiveId;
            }
            if (selected.Data.TryGetValue("Profile.Mode", out JsonElement modes))
                foreach (BackupProfileMode mode in Read<List<BackupProfileMode>>("Profile.Mode", modes))
                    FindRequiredProfile(profiles, mode.Id, mode.Name).Mode = mode.Mode;
            if (selected.Data.TryGetValue("Profile.Processes", out JsonElement processes))
                foreach (BackupProfileProcesses entry in Read<List<BackupProfileProcesses>>("Profile.Processes", processes))
                    FindRequiredProfile(profiles, entry.Id, entry.Name).Processes = entry.Processes.ToList();
            values["FilterProfiles"] = JsonSerializer.Serialize(profiles);
            values["ActiveProfileId"] = activeId;
        }
        if (selected.Data.Keys.Any(IsScreenKey))
        {
            var screens = current.Data.Where(item => IsScreenKey(item.Key))
                .ToDictionary(item => item.Key, item => Read<ScreenSelectionState>(item.Key, item.Value), StringComparer.Ordinal);
            foreach (var item in selected.Data.Where(item => IsScreenKey(item.Key)))
                screens[item.Key] = Read<ScreenSelectionState>(item.Key, item.Value);
            values["ScreenSelections"] = JsonSerializer.Serialize(screens.Values);
            values["EnabledScreenIds"] = JsonSerializer.Serialize(screens.Values.Where(screen => screen.IsEnabled)
                .Select(screen => screen.DeviceName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        return values;
    }

    private static FilterProfile? FindProfile(IEnumerable<FilterProfile> profiles, string id, string name) =>
        profiles.FirstOrDefault(profile => profile.Id == id) ?? profiles.FirstOrDefault(profile => profile.Name == name);

    private static FilterProfile FindRequiredProfile(IEnumerable<FilterProfile> profiles, string id, string name) =>
        FindProfile(profiles, id, name) ?? throw new InvalidDataException(Localization.Format("Backup_ProfileMissing", name));

    private static List<FilterProfile> ReadCurrentProfiles(ConfigurationBackupDocument current)
    {
        var identities = Read<BackupProfileList>("Profiles", current.Data["Profiles"]);
        var modes = Read<List<BackupProfileMode>>("Profile.Mode", current.Data["Profile.Mode"]).ToDictionary(item => item.Id);
        var processes = Read<List<BackupProfileProcesses>>("Profile.Processes", current.Data["Profile.Processes"]).ToDictionary(item => item.Id);
        return identities.Items.Select(item => new FilterProfile
        {
            Id = item.Id, Name = item.Name, Mode = modes[item.Id].Mode, Processes = processes[item.Id].Processes.ToList()
        }).ToList();
    }

    private static void ValidateValue(string key, JsonElement value)
    {
        if (IsScreenKey(key))
        {
            var screen = Read<ScreenSelectionState>(key, value);
            if ((string.IsNullOrWhiteSpace(screen.IdentityKey) && string.IsNullOrWhiteSpace(screen.DeviceName)) ||
                screen.IdentityKey == null || screen.DeviceName == null || screen.DisplayName == null ||
                screen.IdentityKey.Length > 2048 || screen.DeviceName.Length > 2048 || screen.DisplayName.Length > 2048 || GetScreenKey(screen) != key ||
                !value.TryGetProperty("IsEnabled", out JsonElement enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw InvalidValue(key);
            return;
        }
        if (key == "Profiles")
        {
            var profiles = Read<BackupProfileList>(key, value);
            if (profiles.Items == null || profiles.Items.Count is 0 or > 256 || profiles.Items.Any(item => item == null) ||
                !profiles.Items.Any(item => item.Id == profiles.ActiveId)) throw InvalidValue(key);
            ValidateProfileIdentities(key, profiles.Items.Select(item => (item.Id, item.Name)));
            return;
        }
        if (key == "Profile.Mode")
        {
            var modes = Read<List<BackupProfileMode>>(key, value);
            if (modes.Count is 0 or > 256 || modes.Any(item => item == null || !Enum.IsDefined(item.Mode))) throw InvalidValue(key);
            ValidateProfileIdentities(key, modes.Select(item => (item.Id, item.Name)));
            return;
        }
        if (key == "Profile.Processes")
        {
            var processes = Read<List<BackupProfileProcesses>>(key, value);
            if (processes.Count is 0 or > 256 || processes.Any(item => item == null || item.Processes == null ||
                item.Processes.Count > 4096 || item.Processes.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 1024)))
                throw InvalidValue(key);
            ValidateProfileIdentities(key, processes.Select(item => (item.Id, item.Name)));
            return;
        }
        _ = ReadSimpleValue(key, value);
    }

    private static void ValidateProfileIdentities(string key, IEnumerable<(string Id, string Name)> identities)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in identities)
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) ||
                item.Id.Length > 512 || item.Name.Length > 512 || !ids.Add(item.Id)) throw InvalidValue(key);
    }

    private static object ReadSimpleValue(string key, JsonElement value)
    {
        if (key != "EffectOpacity" && !Settings.Any(setting => setting.Key == key)) throw InvalidValue(key);
        Type type = typeof(ConfigManager).GetProperty(key)?.PropertyType ?? throw InvalidValue(key);
        try
        {
            if (type == typeof(bool)) return value.GetBoolean();
            if (NumericRanges.TryGetValue(key, out var range))
            {
                double number = value.GetDouble();
                if (!double.IsFinite(number) || number < range.Minimum || number > range.Maximum) throw InvalidValue(key);
                if (type == typeof(int)) return value.GetInt32();
                return Math.Round(number, 2);
            }
            if (type == typeof(DarkModeOption))
            {
                DarkModeOption mode = value.Deserialize<DarkModeOption>(JsonOptions);
                return Enum.IsDefined(mode) ? mode : throw InvalidValue(key);
            }
            string text = value.GetString() ?? throw InvalidValue(key);
            if (key == "UiLanguage" && text is not ("zh-CN" or "en" or "ja")) throw InvalidValue(key);
            if (key == "ParticleColor")
            {
                if (ColorPickerColorMath.TryParseArgbHex(text, out var themeColor))
                {
                    if (themeColor.A < 26) throw InvalidValue(key);
                    return ColorPickerColorMath.ToArgbHex(themeColor);
                }
                if (!ColorPickerColorMath.TryParseRgb(text, out _))
                    throw InvalidValue(key);
            }
            return text;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw InvalidValue(key);
        }
    }

    private static T Read<T>(string key, JsonElement value)
    {
        try { return value.Deserialize<T>(JsonOptions) ?? throw InvalidValue(key); }
        catch (JsonException) { throw InvalidValue(key); }
    }

    private static InvalidDataException InvalidValue(string key) =>
        new(Localization.Format("Backup_InvalidValue", key));
}
