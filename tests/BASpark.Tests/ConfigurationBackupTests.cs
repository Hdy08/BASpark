using System.Text.Json;

namespace BASpark.Tests;

public sealed class ConfigurationBackupTests
{
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
    public void Import_AProfilesSelectionKeepsUncheckedPropertiesOfMatchingProfiles()
    {
        ConfigurationBackupDocument source = CreateFixture("profile-a", "Renamed", ProcessFilterModeOption.Whitelist, "incoming.exe");
        ConfigurationBackupDocument current = CreateFixture(processes: ["keep.exe"]);
        Dictionary<string, object> values = ConfigurationBackup.BuildImportValues(source, ["Profiles"], current);
        FilterProfile profile = Assert.Single(JsonSerializer.Deserialize<List<FilterProfile>>((string)values["FilterProfiles"])!);
        Assert.Equal("Renamed", profile.Name);
        Assert.Equal(ProcessFilterModeOption.Blacklist, profile.Mode);
        Assert.Equal(["keep.exe"], profile.Processes);
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
    [InlineData("TrailRefreshRate", "400")]
    [InlineData("EffectOpacity", "0")]
    [InlineData("GlowIntensity", "\"invalid\"")]
    [InlineData("StartSilent", "\"true\"")]
    [InlineData("DarkMode", "99")]
    [InlineData("UiLanguage", "\"unsupported\"")]
    [InlineData("ParticleColor", "\"256,0,0\"")]
    [InlineData("AgreedToPrivacy", "true")]
    [InlineData("UnknownSetting", "true")]
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
        Assert.Equal(32, ConfigurationBackup.Settings.Count);
        Assert.Equal(ConfigurationBackup.Settings.Count, ConfigurationBackup.Settings.Select(item => item.Key).Distinct().Count());
        Assert.Contains(ConfigurationBackup.Settings, item => item.Key == "TotalClicks");
        foreach (string key in new[] { "Profiles", "Profile.Mode", "Profile.Processes", "ParticleColor",
                     "HideTrayIcon", "UiLanguage", "ScreenshotCompatibilityMode" })
            Assert.Contains(ConfigurationBackup.Settings, item => item.Key == key);
        Assert.DoesNotContain(ConfigurationBackup.Settings, item => item.Key == "EffectOpacity");
        foreach (BackupSettingDefinition item in ConfigurationBackup.Settings)
        {
            Assert.NotEqual(item.GroupKey, Localization.Get(item.GroupKey));
            Assert.NotEqual(item.TitleKey, Localization.Get(item.TitleKey));
        }
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
