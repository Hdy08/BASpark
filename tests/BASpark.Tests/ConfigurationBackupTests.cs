using System.Text.Json;

namespace BASpark.Tests;

public sealed class ConfigurationBackupTests
{
    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#FFFF00")]
    [InlineData("#0000FF")]
    [InlineData("#D13438")]
    public void AccentColor_PalettePreservesTheSelectedRgbAndReadableNativeControlForegrounds(string hex)
    {
        Assert.True(ColorPickerColorMath.TryParseHex(hex, out var color));
        var palette = ColorPickerColorMath.CreateAccentPalette(color);
        Assert.Equal(7, palette.Length);
        Assert.Equal(color, palette[0]);
        Assert.All(palette, shade => Assert.Equal(255, shade.A));
        Assert.True((ColorPickerColorMath.AccentLuminance(palette[2]) + 0.05) / 0.05 >= 4.5);
        Assert.True(1.05 / (ColorPickerColorMath.AccentLuminance(palette[4]) + 0.05) >= 4.5);
        Assert.True(ColorPickerColorMath.AccentLuminance(palette[3]) >= ColorPickerColorMath.AccentLuminance(palette[2]));
        Assert.True(ColorPickerColorMath.AccentLuminance(palette[4]) >= ColorPickerColorMath.AccentLuminance(palette[5]));
        Assert.True(ColorPickerColorMath.AccentLuminance(palette[5]) >= ColorPickerColorMath.AccentLuminance(palette[6]));
    }

    [Theory]
    [InlineData("Off", BackgroundColorOption.Light)]
    [InlineData("On", BackgroundColorOption.Dark)]
    [InlineData("System", BackgroundColorOption.System)]
    [InlineData("Light", BackgroundColorOption.Light)]
    [InlineData("Dark", BackgroundColorOption.Dark)]
    [InlineData("0", BackgroundColorOption.Light)]
    [InlineData("1", BackgroundColorOption.Dark)]
    [InlineData("2", BackgroundColorOption.System)]
    public void BackgroundColor_LegacyValuesMigrateToTheRenamedBackupKey(string legacy, BackgroundColorOption expected)
    {
        Assert.Equal(expected, ConfigManager.ParseBackgroundColor(legacy));
        var source = new ConfigurationBackupDocument();
        source.Data["DarkMode"] = JsonSerializer.SerializeToElement(legacy);
        ConfigurationBackupDocument parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Assert.DoesNotContain("DarkMode", parsed.Data.Keys);
        Assert.Equal(expected.ToString(), parsed.Data["BackgroundColor"].GetString());
        var values = ConfigurationBackup.BuildImportValues(parsed, ["BackgroundColor"], CreateFixture());
        Assert.Equal(expected, Assert.Single(values).Value);
    }

    [Theory]
    [InlineData(BackgroundColorOption.Light)]
    [InlineData(BackgroundColorOption.Dark)]
    [InlineData(BackgroundColorOption.System)]
    public void BackgroundColor_BackupUsesLightDarkSystemAndTheNewKey(BackgroundColorOption expected)
    {
        BackgroundColorOption previous = ConfigManager.BackgroundColor;
        try
        {
            ConfigManager.BackgroundColor = expected;
            var document = ConfigurationBackup.Select(ConfigurationBackup.Capture([]), ["BackgroundColor"]);
            Assert.Equal(expected.ToString(), Assert.Single(document.Data).Value.GetString());
            Assert.DoesNotContain("DarkMode", ConfigurationBackup.Serialize(document), StringComparison.Ordinal);
            var values = ConfigurationBackup.BuildImportValues(ConfigurationBackup.Parse(ConfigurationBackup.Serialize(document)),
                ["BackgroundColor"], CreateFixture());
            Assert.Equal(expected, Assert.Single(values).Value);
        }
        finally { ConfigManager.BackgroundColor = previous; }
    }

    [Fact]
    public void BackgroundColor_CurrentValueTakesPrecedenceWhenImportingALegacyBackup()
    {
        var source = new ConfigurationBackupDocument();
        source.Data["DarkMode"] = JsonSerializer.SerializeToElement("On");
        source.Data["BackgroundColor"] = JsonSerializer.SerializeToElement("Light");
        ConfigurationBackupDocument parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Assert.Equal("Light", Assert.Single(parsed.Data).Value.GetString());
    }

    [Theory]
    [InlineData(true, "#0078D4")]
    [InlineData(false, "#FF0000")]
    [InlineData(false, "#000000")]
    [InlineData(false, "#FFFFFF")]
    public void AccentColor_BackupRoundTripPreservesModeAndOpaqueRgbIndependently(bool useSystem, string hex)
    {
        bool previousMode = ConfigManager.UseSystemAccentColor;
        string previousColor = ConfigManager.CustomAccentColor;
        try
        {
            ConfigManager.UseSystemAccentColor = useSystem;
            ConfigManager.CustomAccentColor = hex;
            ConfigurationBackupDocument captured = ConfigurationBackup.Capture([]);
            ConfigurationBackupDocument selected = ConfigurationBackup.Select(captured, ["UseSystemAccentColor", "CustomAccentColor"]);
            ConfigurationBackupDocument parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(selected));
            Assert.Equal(2, parsed.Data.Count);
            Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(parsed, parsed.Data.Keys, CreateFixture());
            Assert.Equal(useSystem, values["UseSystemAccentColor"]);
            Assert.Equal(hex, values["CustomAccentColor"]);
            Assert.DoesNotContain("ParticleColor", values.Keys);
            Assert.DoesNotContain("EffectOpacity", values.Keys);
            Assert.Equal(useSystem, ConfigManager.UseSystemAccentColor);
            Assert.Equal(hex, ConfigManager.CustomAccentColor);
            foreach (string key in parsed.Data.Keys)
                Assert.Equal("Nav_Basic", ConfigurationBackup.Settings.Single(setting => setting.Key == key).GroupKey);
        }
        finally
        {
            ConfigManager.UseSystemAccentColor = previousMode;
            ConfigManager.CustomAccentColor = previousColor;
        }
    }

    [Theory]
    [InlineData("UseSystemAccentColor")]
    [InlineData("CustomAccentColor")]
    public void AccentColor_PartialImportLeavesOtherValuesUntouched(string key)
    {
        var source = new ConfigurationBackupDocument();
        source.Data["UseSystemAccentColor"] = JsonSerializer.SerializeToElement(false);
        source.Data["CustomAccentColor"] = JsonSerializer.SerializeToElement("#c0ffee");
        var parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(parsed, [key], CreateFixture());
        Assert.Equal(key, Assert.Single(values).Key);
        if (key == "CustomAccentColor") Assert.Equal("#C0FFEE", values[key]);
        else Assert.Equal(false, values[key]);
    }

    [Theory]
    [InlineData("#FF0078D4")]
    [InlineData("#800078D4")]
    [InlineData("#GG78D4")]
    [InlineData("76,167,255")]
    [InlineData("")]
    public void AccentColor_ImportRejectsAlphaAndInvalidRgb(string hex)
    {
        var source = new ConfigurationBackupDocument();
        source.Data["CustomAccentColor"] = JsonSerializer.SerializeToElement(hex);
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowEffectOnApp_BackupAndImportPreserveTheChoiceWithoutChangingOtherSettings(bool enabled)
    {
        bool previousValue = ConfigManager.ShowEffectOnApp;
        try
        {
            ConfigManager.ShowEffectOnApp = enabled;
            ConfigurationBackupDocument captured = ConfigurationBackup.Capture([]);
            ConfigurationBackupDocument selected = ConfigurationBackup.Select(captured, ["ShowEffectOnApp"]);
            ConfigurationBackupDocument parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(selected));
            Assert.Equal("ShowEffectOnApp", Assert.Single(parsed.Data).Key);
            Assert.Equal(enabled, parsed.Data["ShowEffectOnApp"].GetBoolean());
            Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(parsed, ["ShowEffectOnApp"], CreateFixture());
            Assert.Equal(enabled, Assert.Single(values).Value);
            Assert.Equal(enabled, ConfigManager.ShowEffectOnApp);
            Assert.Equal("Nav_Filter", ConfigurationBackup.Settings.Single(setting => setting.Key == "ShowEffectOnApp").GroupKey);
        }
        finally
        {
            ConfigManager.ShowEffectOnApp = previousValue;
        }
    }

    [Fact]
    public void AdvancedFx_DisplayDefaultsMatchTheFullLibrary()
    {
        Assert.Equal(1, AdvancedFxSettings.Find("BaClickFx/maxDpr")!.DefaultValue.GetDouble());
        Assert.Equal("relative-oklch", AdvancedFxSettings.Find("BaClickFx/themeColorMode")!.DefaultValue.GetString());
        Assert.Equal(250.0 / 255, AdvancedFxSettings.Find("BaClickFx/overlayAlphaLimit")!.DefaultValue.GetDouble());
        Assert.Equal("{}", AdvancedFxSettings.Normalize("{\"maxDpr\":1,\"themeColorMode\":\"relative-oklch\",\"overlayAlphaLimit\":0.9803921568627451}"));
        Assert.Contains("hue-only", AdvancedFxSettings.Normalize("{\"maxDpr\":2,\"themeColorMode\":\"hue-only\",\"overlayAlphaLimit\":0.85}"), StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedFx_DefaultsProduceNoRendererOverrides()
    {
        Assert.Equal(72, AdvancedFxSettings.Definitions.Count);
        Assert.Equal(72, AdvancedFxSettings.Definitions.Select(item => item.Path).Distinct().Count());
        var defaults = AdvancedFxSettings.Definitions.ToDictionary(item => item.Path,
            item => AdvancedFxSettings.Validate(item, item.DefaultValue));
        Assert.Equal("{}", AdvancedFxSettings.Serialize(defaults));
        using JsonDocument renderer = JsonDocument.Parse(AdvancedFxSettings.RendererPayload(AdvancedFxSettings.Serialize(defaults)));
        Assert.Empty(renderer.RootElement.GetProperty("config").EnumerateObject());
        Assert.Empty(renderer.RootElement.GetProperty("parameters").EnumerateObject());
        Assert.DoesNotContain(AdvancedFxSettings.Definitions, item => item.Path is "scale" or "opacity" or "themeColor" or
            "clickTimeScale" or "trailTimeScale" or "inputSamplingRate" or "bloom.intensity" or "inputSource" or
            "outputCompositing" or "hostCompositing" or "hostCompositingSurface");
    }

    [Fact]
    public void AdvancedFx_PersistencePreservesExactDefaultsAndSeparatesRenderParameters()
    {
        const string content = "{\"rings.count\":4,\"trail.width\":7.23,\"bloom.resolutionScale\":0.25,\"renderMode\":\"native-bloom\",\"rings.hdrIntensity\":5.992157}";
        string normalized = AdvancedFxSettings.Serialize(AdvancedFxSettings.Parse(content, strict: true));
        Assert.Equal(normalized, AdvancedFxSettings.Normalize(normalized));
        Assert.DoesNotContain("rings.hdrIntensity", normalized, StringComparison.Ordinal);
        using JsonDocument payload = JsonDocument.Parse(AdvancedFxSettings.RendererPayload(normalized));
        Assert.Equal("native-bloom", payload.RootElement.GetProperty("config").GetProperty("renderMode").GetString());
        Assert.Equal(7.23, payload.RootElement.GetProperty("parameters").GetProperty("trail.width").GetDouble());
        Assert.Equal(4, payload.RootElement.GetProperty("parameters").GetProperty("rings.count").GetInt32());
        Assert.False(payload.RootElement.GetProperty("parameters").TryGetProperty("bloom.intensity", out _));
        Assert.Equal("{}", AdvancedFxSettings.Normalize("invalid saved data"));
        Assert.Equal("{}", AdvancedFxSettings.Normalize("{\"rings.count\":99}"));
    }

    [Theory]
    [InlineData("rings.count", "6.1")]
    [InlineData("rings.count", "7")]
    [InlineData("trail.width", "0")]
    [InlineData("bloom.resolutionScale", "0.01")]
    [InlineData("rings.rotationDirection", "0")]
    [InlineData("maxDpr", "4")]
    [InlineData("renderMode", "\"unknown\"")]
    [InlineData("themeColorMode", "true")]
    [InlineData("isolatedCompositing", "1")]
    public void AdvancedFx_InvalidPersistedAndImportedValuesAreRejected(string path, string value)
    {
        string content = "{\"" + path + "\":" + value + "}";
        Assert.Throws<InvalidDataException>(() => AdvancedFxSettings.Parse(content, strict: true));
        var backup = new ConfigurationBackupDocument();
        using JsonDocument data = JsonDocument.Parse(value);
        backup.Data[AdvancedFxSettings.KeyPrefix + path] = data.RootElement.Clone();
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(ConfigurationBackup.Serialize(backup)));
    }

    [Fact]
    public void AdvancedFx_IndividualImportPreservesUnselectedSettingsAndMasterSwitch()
    {
        ConfigurationBackupDocument source = CreateFixture();
        source.Data["BaClickFx/rings.count"] = JsonSerializer.SerializeToElement(3);
        source.Data["BaClickFx/trail.width"] = JsonSerializer.SerializeToElement(9.1);
        source.Data["EnableBaClickAdvancedSettings"] = JsonSerializer.SerializeToElement(true);
        ConfigurationBackupDocument current = CreateFixture();
        current.Data["BaClickFx/rings.count"] = JsonSerializer.SerializeToElement(5);
        current.Data["BaClickFx/trail.width"] = JsonSerializer.SerializeToElement(7.23);
        current.Data["BaClickFx/bloom.threshold"] = JsonSerializer.SerializeToElement(1.4);
        var values = ConfigurationBackup.BuildImportValues(source, ["BaClickFx/rings.count"], current);
        Assert.Equal("BaClickAdvancedSettings", Assert.Single(values.Keys));
        Dictionary<string, JsonElement> advanced = AdvancedFxSettings.Parse((string)values["BaClickAdvancedSettings"], strict: true);
        Assert.Equal(3, advanced["rings.count"].GetInt32());
        Assert.Equal(7.23, advanced["trail.width"].GetDouble());
        Assert.Equal(1.4, advanced["bloom.threshold"].GetDouble());
        Assert.DoesNotContain("EnableBaClickAdvancedSettings", values.Keys);
    }

    [Fact]
    public void AdvancedFx_AllDefaultsCanBeBackedUpAndLegacyBackupsRemainSelective()
    {
        var source = new ConfigurationBackupDocument();
        foreach (AdvancedFxSetting definition in AdvancedFxSettings.Definitions)
            source.Data[definition.Key] = definition.DefaultValue;
        source.Data["EnableBaClickAdvancedSettings"] = JsonSerializer.SerializeToElement(false);
        ConfigurationBackupDocument parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Assert.Equal(73, parsed.Data.Count);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(parsed, parsed.Data.Keys, CreateFixture());
        Assert.False((bool)values["EnableBaClickAdvancedSettings"]);
        Assert.Equal("{}", values["BaClickAdvancedSettings"]);
        ConfigurationBackupDocument selected = ConfigurationBackup.Select(parsed, ["BaClickFx/disk.radius"]);
        Assert.Equal("BaClickFx/disk.radius", Assert.Single(selected.Data.Keys));
        Dictionary<string, object> legacy = ConfigurationBackup.BuildImportValues(CreateFixture(), ["GlowIntensity"], parsed);
        Assert.Equal(1.25, legacy["GlowIntensity"]);
        Assert.DoesNotContain("BaClickAdvancedSettings", legacy.Keys);
    }

    private static ConfigurationBackupDocument CreateFixture(string id = "profile-a", string name = "Profile A",
        ProcessFilterModeOption mode = ProcessFilterModeOption.Blacklist, params string[] processes)
    {
        var document = new ConfigurationBackupDocument();
        document.Data["TotalClicks"] = JsonSerializer.SerializeToElement(1250);
        document.Data["StartSilent"] = JsonSerializer.SerializeToElement(false);
        document.Data["GlowIntensity"] = JsonSerializer.SerializeToElement(1.25);
        document.Data["UiLanguage"] = JsonSerializer.SerializeToElement("zh-CN");
        ConfigurationBackup.SetProfiles(document,
            [new FilterProfile { Id = id, Name = name, Mode = mode, Processes = processes.ToList() }], id);
        ConfigurationBackup.SetScreens(document,
            [new ScreenSelectionState { IdentityKey = "monitor-a", DeviceName = "DISPLAY1", DisplayName = "Display A", IsEnabled = true }]);
        return document;
    }

    [Fact]
    public void Select_RoundTripsOnlyCheckedDataAndIndependentProfileFields()
    {
        ConfigurationBackupDocument source = CreateFixture(processes: ["editor.exe"]);
        ConfigurationBackupDocument selected = ConfigurationBackup.Select(source, ["TotalClicks", "Profile.Processes"]);
        ConfigurationBackupDocument imported = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(selected));
        Assert.Equal(new[] { "Profile.Processes", "TotalClicks" }, imported.Data.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1250, imported.Data["TotalClicks"].GetInt32());
        Assert.Contains("editor.exe", imported.Data["Profile.Processes"].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Profiles", imported.Data.Keys);
    }

    [Fact]
    public void Import_LeavesUncheckedSettingsAndProfileListsUntouched()
    {
        ConfigurationBackupDocument source = CreateFixture(mode: ProcessFilterModeOption.Whitelist, processes: ["new.exe"]);
        ConfigurationBackupDocument current = CreateFixture(processes: ["existing.exe"]);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(source, ["Profile.Mode", "GlowIntensity"], current);
        Assert.Equal(1.25, values["GlowIntensity"]);
        Assert.DoesNotContain("TotalClicks", values.Keys);
        Assert.DoesNotContain("StartSilent", values.Keys);
        List<FilterProfile> profiles = JsonSerializer.Deserialize<List<FilterProfile>>((string)values["FilterProfiles"])!;
        Assert.Equal(ProcessFilterModeOption.Whitelist, Assert.Single(profiles).Mode);
        Assert.Equal(["existing.exe"], profiles[0].Processes);
        Assert.Equal("profile-a", values["ActiveProfileId"]);
    }

    [Fact]
    public void Import_ProfileNamesCanMatchAnExistingConfigurationWithDifferentIdentifiers()
    {
        ConfigurationBackupDocument source = CreateFixture("source-id", "Same name", processes: ["imported.exe"]);
        ConfigurationBackupDocument current = CreateFixture("current-id", "Same name", processes: ["old.exe"]);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(source, ["Profile.Processes"], current);
        FilterProfile profile = Assert.Single(JsonSerializer.Deserialize<List<FilterProfile>>((string)values["FilterProfiles"])!);
        Assert.Equal("current-id", profile.Id);
        Assert.Equal(["imported.exe"], profile.Processes);
    }

    [Fact]
    public void Import_AProfilesSelectionIncludesNamesModesAndProcessesTogether()
    {
        ConfigurationBackupDocument source = CreateFixture("profile-a", "Renamed", ProcessFilterModeOption.Whitelist, "incoming.exe");
        ConfigurationBackupDocument current = CreateFixture(processes: ["keep.exe"]);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(source, ["Profiles"], current);
        FilterProfile profile = Assert.Single(JsonSerializer.Deserialize<List<FilterProfile>>((string)values["FilterProfiles"])!);
        Assert.Equal("Renamed", profile.Name);
        Assert.Equal(ProcessFilterModeOption.Whitelist, profile.Mode);
        Assert.Equal(["incoming.exe"], profile.Processes);
        Assert.DoesNotContain("GlowIntensity", values.Keys);
    }

    [Fact]
    public void Select_ProfilesIncludesAllThreeFieldsAndSupportsLegacyPartialGroups()
    {
        ConfigurationBackupDocument source = CreateFixture(mode: ProcessFilterModeOption.Whitelist, processes: ["incoming.exe"]);
        ConfigurationBackupDocument selected = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(ConfigurationBackup.Select(source, ["Profiles"])));
        Assert.Equal(new[] { "Profile.Mode", "Profile.Processes", "Profiles" }, selected.Data.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("TotalClicks", selected.Data.Keys);
        source.Data.Remove("Profiles");
        ConfigurationBackupDocument legacy = ConfigurationBackup.Select(source, ["Profiles"]);
        Assert.Equal(new[] { "Profile.Mode", "Profile.Processes" }, legacy.Data.Keys.Order(StringComparer.Ordinal).ToArray());
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(legacy, ["Profiles"], CreateFixture(processes: ["keep.exe"]));
        FilterProfile profile = Assert.Single(JsonSerializer.Deserialize<List<FilterProfile>>((string)values["FilterProfiles"])!);
        Assert.Equal(ProcessFilterModeOption.Whitelist, profile.Mode);
        Assert.Equal(["incoming.exe"], profile.Processes);
    }

    [Fact]
    public void Import_MissingProfileDoesNotSilentlyDiscardItsModeOrProcessList()
    {
        ConfigurationBackupDocument source = CreateFixture("missing", "Missing", processes: ["new.exe"]);
        ConfigurationBackupDocument current = CreateFixture();
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.BuildImportValues(source, ["Profile.Processes"], current));
        Dictionary<string, object> complete = ConfigurationBackup.BuildImportValues(source, ["Profiles", "Profile.Processes"], current);
        FilterProfile profile = Assert.Single(JsonSerializer.Deserialize<List<FilterProfile>>((string)complete["FilterProfiles"])!);
        Assert.Equal("missing", profile.Id);
        Assert.Equal(["new.exe"], profile.Processes);
    }

    [Fact]
    public void Import_IndividualScreenDataPreservesOtherDisplaysAndOfflineMemory()
    {
        ConfigurationBackupDocument source = CreateFixture();
        ConfigurationBackup.SetScreens(source,
            [new ScreenSelectionState { IdentityKey = "monitor-a", DeviceName = "DISPLAY1", IsEnabled = false }]);
        ConfigurationBackupDocument current = CreateFixture();
        current.Data["Screen/monitor-b"] = JsonSerializer.SerializeToElement(
            new ScreenSelectionState { IdentityKey = "monitor-b", DeviceName = "DISPLAY2", IsEnabled = true });
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(source, ["Screen/monitor-a"], current);
        List<ScreenSelectionState> screens = JsonSerializer.Deserialize<List<ScreenSelectionState>>((string)values["ScreenSelections"])!;
        Assert.False(screens.Single(screen => screen.IdentityKey == "monitor-a").IsEnabled);
        Assert.True(screens.Single(screen => screen.IdentityKey == "monitor-b").IsEnabled);
        Assert.Equal(["DISPLAY2"], JsonSerializer.Deserialize<List<string>>((string)values["EnabledScreenIds"])!);
    }

    [Theory]
    [InlineData("TotalClicks", "-1")]
    [InlineData("TotalClicks", "1.5")]
    [InlineData("TotalClicks", "2147483648")]
    [InlineData("EffectScale", "0.1")]
    [InlineData("InputSamplingRate", "1001")]
    [InlineData("EffectOpacity", "0")]
    [InlineData("GlowIntensity", "\"invalid\"")]
    [InlineData("StartSilent", "\"true\"")]
    [InlineData("DarkMode", "99")]
    [InlineData("UiLanguage", "\"unsupported\"")]
    [InlineData("ParticleColor", "\"256,0,0\"")]
    [InlineData("AgreedToPrivacy", "true")]
    [InlineData("UnknownSetting", "true")]
    [InlineData("ShowEffectOnApp", "0")]
    [InlineData("ShowEffectOnApp", "\"false\"")]
    [InlineData("ShowEffectOnApp", "null")]
    public void Parse_RejectsInvalidOrNonConfigurationValuesBeforeAnyPersistence(string key, string json)
    {
        var document = new ConfigurationBackupDocument();
        document.Data[key] = JsonDocument.Parse(json).RootElement.Clone();
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(ConfigurationBackup.Serialize(document)));
    }

    [Theory]
    [InlineData("{\"Format\":\"OtherApp\",\"Version\":1,\"Data\":{\"StartSilent\":true}}")]
    [InlineData("{\"Format\":\"BASpark.Configuration\",\"Version\":2,\"Data\":{\"StartSilent\":true}}")]
    [InlineData("{\"Data\":{\"StartSilent\":true}}")]
    [InlineData("{\"Format\":\"BASpark.Configuration\",\"Data\":{\"StartSilent\":true}}")]
    [InlineData("{\"Data\":{\"StartSilent\":true,\"StartSilent\":false}}")]
    [InlineData("{\"Format\":\"BASpark.Configuration\",\"Data\":null}")]
    [InlineData("{\"Format\":\"BASpark.Configuration\",\"Data\":{}}")]
    [InlineData("not-json")]
    public void Parse_RejectsWrongFormatVersionEmptyDataAndDuplicateChoices(string content)
    {
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(content));
    }

    [Fact]
    public void Select_RequiresAChoiceAndDoesNotModifyTheOriginalDocument()
    {
        ConfigurationBackupDocument original = CreateFixture();
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Select(original, []));
        ConfigurationBackupDocument selected = ConfigurationBackup.Select(original, ["GlowIntensity"]);
        selected.Data["GlowIntensity"] = JsonSerializer.SerializeToElement(2.5);
        Assert.Equal(1.25, original.Data["GlowIntensity"].GetDouble());
    }

    [Fact]
    public async Task WriteAndRead_UseUniqueFilesAndPreserveExistingBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BASpark.BackupTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ConfigurationBackupDocument source = CreateFixture();
            string first = await ConfigurationBackup.WriteAsync(directory, source, ["TotalClicks"]);
            string original = await File.ReadAllTextAsync(first);
            string second = await ConfigurationBackup.WriteAsync(directory, source, ["GlowIntensity"]);
            Assert.NotEqual(first, second);
            Assert.Matches(@"^BASpark_Config_\d{8}-\d{6}\.json$", Path.GetFileName(first));
            Assert.Matches(@"^BASpark_Config_\d{8}-\d{6}\.json$", Path.GetFileName(second));
            Assert.Equal(original, await File.ReadAllTextAsync(first));
            Assert.Equal(1250, (await ConfigurationBackup.ReadAsync(first)).Data["TotalClicks"].GetInt32());
            Assert.Equal(1.25, (await ConfigurationBackup.ReadAsync(second)).Data["GlowIntensity"].GetDouble());
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                ConfigurationBackup.WriteAsync(Path.Combine(directory, "missing"), source, ["TotalClicks"]));
        }
        finally
        {
            if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SettingCatalog_CoversAllEditableSettingsAndStatisticsWithUniqueKeys()
    {
        Assert.Equal(34 + AdvancedFxSettings.Definitions.Count, ConfigurationBackup.Settings.Count);
        Assert.Equal(ConfigurationBackup.Settings.Count, ConfigurationBackup.Settings.Select(item => item.Key).Distinct().Count());
        Assert.Equal(new[] { "Backup_Statistics", "Nav_Basic", "Nav_Visual", "Nav_Filter" },
            ConfigurationBackup.Settings.Select(item => item.GroupKey).Distinct());
        Assert.Contains(ConfigurationBackup.Settings, item => item.Key == "TotalClicks");
        foreach (string key in new[] { "Profiles", "ParticleColor",
                     "HideTrayIcon", "UiLanguage", "ScreenshotCompatibilityMode", "MaxTouchEffects", "InputSamplingRate", "ShowEffectOnApp",
                     "BackgroundColor", "UseSystemAccentColor", "CustomAccentColor" })
            Assert.Contains(ConfigurationBackup.Settings, item => item.Key == key);
        Assert.DoesNotContain(ConfigurationBackup.Settings, item => item.Key == "EffectOpacity");
        Assert.DoesNotContain(ConfigurationBackup.Settings, item => item.Key == "DarkMode");
        Assert.DoesNotContain(ConfigurationBackup.Settings, item => item.Key is "TrailRefreshRate" or "FollowDisplayRefreshRate");
        Assert.DoesNotContain(ConfigurationBackup.Settings, item => item.Key is "Profile.Mode" or "Profile.Processes");
        Assert.Equal("Filter_ProfileGroup", Assert.Single(ConfigurationBackup.Settings, item => item.Key == "Profiles").TitleKey);
        foreach (BackupSettingDefinition item in ConfigurationBackup.Settings)
        {
            Assert.NotEqual(item.GroupKey, Localization.Get(item.GroupKey));
            Assert.NotEqual(item.TitleKey, Localization.Get(item.TitleKey));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void TouchEffectLimit_BackupSelectionRoundTripsTheIndependentCount(int maximum)
    {
        ConfigurationBackupDocument source = CreateFixture();
        source.Data["MaxTouchEffects"] = JsonSerializer.SerializeToElement(maximum);
        source.Data["IsTouchscreenMode"] = JsonSerializer.SerializeToElement(false);
        ConfigurationBackupDocument imported = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(
            ConfigurationBackup.Select(source, ["MaxTouchEffects"])));
        Assert.Equal(maximum, imported.Data["MaxTouchEffects"].GetInt32());
        Assert.DoesNotContain("IsTouchscreenMode", imported.Data.Keys);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(imported, ["MaxTouchEffects"], CreateFixture());
        Assert.Equal(maximum, values["MaxTouchEffects"]);
        Assert.DoesNotContain("IsTouchscreenMode", values.Keys);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void TouchEffectLimit_LegacyBackupsMigrateTheToggle(bool enabled, int maximum)
    {
        var source = new ConfigurationBackupDocument();
        source.Data["EnableMultiTouchEffects"] = JsonSerializer.SerializeToElement(enabled);
        var migrated = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Assert.Equal(maximum, migrated.Data["MaxTouchEffects"].GetInt32());
        Assert.DoesNotContain("EnableMultiTouchEffects", migrated.Data.Keys);
        source.Data["MaxTouchEffects"] = JsonSerializer.SerializeToElement(3);
        Assert.Equal(3, ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source)).Data["MaxTouchEffects"].GetInt32());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("11")]
    [InlineData("2.5")]
    [InlineData("true")]
    [InlineData("\"2\"")]
    public void TouchEffectLimit_BackupRejectsOutOfRangeAndNonIntegerCounts(string json)
    {
        var source = new ConfigurationBackupDocument();
        source.Data["MaxTouchEffects"] = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source)));
    }

    [Theory]
    [InlineData(null, null, 1)]
    [InlineData(null, false, 1)]
    [InlineData(null, true, 0)]
    [InlineData(0, true, 0)]
    [InlineData(5, true, 5)]
    [InlineData(10, false, 10)]
    [InlineData(-1, false, 0)]
    [InlineData(11, false, 10)]
    public void TouchEffectLimit_StoredValuesPreferTheNewCountAndNormalizeTheRange(int? value, bool? legacy, int expected)
    {
        Assert.Equal(expected, ConfigManager.ParseMaxTouchEffects(value, legacy));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(400)]
    [InlineData(1000)]
    public void InputSamplingRate_BackupRoundTripsOnlyTheSelectedUnifiedSetting(int rate)
    {
        ConfigurationBackupDocument source = CreateFixture();
        source.Data["InputSamplingRate"] = JsonSerializer.SerializeToElement(rate);
        ConfigurationBackupDocument imported = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(
            ConfigurationBackup.Select(source, ["InputSamplingRate"])));
        Assert.Equal("InputSamplingRate", Assert.Single(imported.Data).Key);
        Assert.Equal(rate, imported.Data["InputSamplingRate"].GetInt32());
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(imported, ["InputSamplingRate"], CreateFixture());
        Assert.Equal(rate, Assert.Single(values).Value);
    }

    [Theory]
    [InlineData(true, 40, -1)]
    [InlineData(false, 40, 40)]
    [InlineData(false, 0, 0)]
    [InlineData(false, 1000, 1000)]
    public void InputSamplingRate_LegacyBackupsMigrateDisplayFollowingAndKeepManualValues(bool follow, int rate, int expected)
    {
        ConfigurationBackupDocument source = CreateFixture();
        source.Data["FollowDisplayRefreshRate"] = JsonSerializer.SerializeToElement(follow);
        source.Data["TrailRefreshRate"] = JsonSerializer.SerializeToElement(rate);
        ConfigurationBackupDocument migrated = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Assert.Equal(expected, migrated.Data["InputSamplingRate"].GetInt32());
        Assert.DoesNotContain("FollowDisplayRefreshRate", migrated.Data.Keys);
        Assert.DoesNotContain("TrailRefreshRate", migrated.Data.Keys);
        source.Data["InputSamplingRate"] = JsonSerializer.SerializeToElement(0);
        Assert.Equal(0, ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source)).Data["InputSamplingRate"].GetInt32());
    }

    [Theory]
    [InlineData(null, null, null, -1)]
    [InlineData(null, 40, true, -1)]
    [InlineData(null, 40, false, 40)]
    [InlineData(null, 1000, null, 1000)]
    [InlineData(null, null, true, -1)]
    [InlineData(null, null, false, 60)]
    [InlineData(0, 40, true, 0)]
    [InlineData(-1, 40, false, -1)]
    [InlineData(400, 40, true, 400)]
    [InlineData(-2, 40, false, -1)]
    [InlineData(1001, 40, false, 1000)]
    public void InputSamplingRate_StoredValuesNormalizeAndOverrideBothLegacyKeys(int? value, int? legacyRate, bool? legacyFollow, int expected)
    {
        Assert.Equal(expected, ConfigManager.ParseInputSamplingRate(value, legacyRate, legacyFollow));
    }

    [Theory]
    [InlineData("InputSamplingRate", "-2")]
    [InlineData("InputSamplingRate", "1001")]
    [InlineData("InputSamplingRate", "1.5")]
    [InlineData("InputSamplingRate", "true")]
    [InlineData("InputSamplingRate", "\"0\"")]
    [InlineData("TrailRefreshRate", "1001")]
    [InlineData("TrailRefreshRate", "1.5")]
    [InlineData("TrailRefreshRate", "true")]
    [InlineData("TrailRefreshRate", "\"40\"")]
    [InlineData("FollowDisplayRefreshRate", "1")]
    public void InputSamplingRate_BackupsRejectInvalidNewAndLegacyValues(string key, string json)
    {
        ConfigurationBackupDocument source = CreateFixture();
        source.Data[key] = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source)));
    }

    [Theory]
    [InlineData("#804CA7FF", "76,167,255", 128)]
    [InlineData("#FF0055FF", "0,85,255", 255)]
    public void Import_ArgbThemeColorAppliesColorAndOpacityTogether(string hex, string rgb, int alpha)
    {
        var source = new ConfigurationBackupDocument();
        source.Data["ParticleColor"] = JsonSerializer.SerializeToElement(hex);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(source, ["ParticleColor"], CreateFixture());
        Assert.Equal(rgb, values["ParticleColor"]);
        Assert.Equal(alpha / 255.0, values["EffectOpacity"]);
    }

    [Fact]
    public void Parse_LegacyColorAndOpacityBecomeOneHexField()
    {
        var source = new ConfigurationBackupDocument();
        source.Data["ParticleColor"] = JsonSerializer.SerializeToElement("76,167,255");
        source.Data["EffectOpacity"] = JsonSerializer.SerializeToElement(0.5);
        ConfigurationBackupDocument parsed = ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source));
        Assert.Equal("#804CA7FF", parsed.Data["ParticleColor"].GetString());
        Assert.DoesNotContain("EffectOpacity", parsed.Data.Keys);
        Assert.Equal("#804CA7FF", ConfigurationBackup.Parse(ConfigurationBackup.Serialize(parsed)).Data["ParticleColor"].GetString());
    }

    [Theory]
    [InlineData("#004CA7FF")]
    [InlineData("#104CA7FF")]
    [InlineData("#GG4CA7FF")]
    public void Parse_InvalidArgbColorsAreRejected(string hex)
    {
        var source = new ConfigurationBackupDocument();
        source.Data["ParticleColor"] = JsonSerializer.SerializeToElement(hex);
        Assert.Throws<InvalidDataException>(() => ConfigurationBackup.Parse(ConfigurationBackup.Serialize(source)));
    }
}
