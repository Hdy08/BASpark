using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace BASpark;

public sealed class BackupSelectionItem(string key, string group, string title, string subtitle) : SelectionCardItem
{
    public string Key { get; } = key;
    public string Group { get; } = group;
    public override string Title { get; } = title;
    public override string Subtitle { get; } = subtitle;
}

public sealed partial class ControlPanelWindow
{
    private readonly List<BackupSelectionItem> _backupItems = [];
    private ConfigurationBackupDocument? _backupSource;
    private string? _backupDirectory;
    private bool _backupImport;
    private bool _backupBusy;
    public IReadOnlyList<BackupSelectionItem> BackupItems => _backupItems;

    private void ApplyBackupLocalizedText()
    {
        SubTabBackupLabel.Text = Localization.Get("Backup_Title");
        TxtBackupTitle.Text = Localization.Get("Backup_Title");
        TxtExportConfiguration.Text = Localization.Get("Backup_ExportTitle");
        TxtExportConfigurationHint.Text = Localization.Get("Backup_ExportHint");
        TxtImportConfiguration.Text = Localization.Get("Backup_ImportTitle");
        TxtImportConfigurationHint.Text = Localization.Get("Backup_ImportHint");
        BtnExportConfiguration.Content = Localization.Get("Backup_Export");
        BtnImportConfiguration.Content = Localization.Get("Backup_Import");
        BtnBackupCancel.Content = Localization.Get("Overlay_Cancel");
        BtnBackupConfirm.Content = Localization.Get(_backupImport ? "Backup_Import" : "Backup_Export");
        TxtBackupSelectionTitle.Text = Localization.Get(_backupImport ? "Backup_SelectImport" : "Backup_SelectExport");
        SearchBackupItems.PlaceholderText = Localization.Get("Overlay_SearchSettings");
        BtnExitApplication.Content = Localization.Get("Welcome_Exit");
        BtnRestartApplication.Content = Localization.Get("Welcome_Restart");
        BtnCopyLog.Content = Localization.Get("Log_Copy");
    }

    private void ExitApplication_Click(object sender, RoutedEventArgs args)
    {
        if (Application.Current is App app) app.ExitApplication();
    }

    private void RestartApplication_Click(object sender, RoutedEventArgs args)
    {
        if (Application.Current is App app) app.RestartApplicationFromPanel();
    }

    private async void CopyLog_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(TxtAppLog.Text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to copy log: {exception.Message}");
            await ShowMessageAsync(Localization.Format("Log_CopyFailed", exception.Message));
        }
    }

    private async void ExportConfiguration_Click(object sender, RoutedEventArgs args) => await PickBackupAsync(import: false);
    private async void ImportConfiguration_Click(object sender, RoutedEventArgs args) => await PickBackupAsync(import: true);

    private async Task PickBackupAsync(bool import)
    {
        if (_backupBusy || _isClosed) return;
        SetBackupBusy(true);
        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(Handle);
            ConfigurationBackupDocument document;
            string? directory = null;
            if (import)
            {
                var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(windowId);
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file == null || _isClosed) return;
                document = await ConfigurationBackup.ReadAsync(file.Path);
            }
            else
            {
                var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(windowId);
                var folder = await picker.PickSingleFolderAsync();
                if (folder == null || _isClosed) return;
                directory = folder.Path;
                document = CaptureSavedBackup();
            }
            if (!_isClosed) OpenBackupSelection(document, import, directory);
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to prepare configuration backup: {exception.Message}");
            await ShowMessageAsync(Localization.Format(import ? "Backup_ImportFailed" : "Backup_ExportFailed", exception.Message));
        }
        finally
        {
            if (!_isClosed) SetBackupBusy(false);
        }
    }

    private ConfigurationBackupDocument CaptureSavedBackup()
    {
        var enabled = ConfigManager.ResolveEnabledScreenDeviceNames(ScreenOptions.Select(CreateScreenIdentityInfo));
        return ConfigurationBackup.Capture(ScreenOptions.Select(screen => new ScreenSelectionState
        {
            IdentityKey = screen.IdentityKey, DeviceName = screen.DeviceName, DisplayName = screen.Title,
            IsEnabled = enabled.Contains(screen.DeviceName)
        }));
    }

    private ConfigurationBackupDocument CapturePendingBackup()
    {
        ConfigurationBackupDocument document = CaptureSavedBackup();
        foreach (string content in new[] { CaptureGeneralSettings(), CaptureVisualSettings() })
        {
            using JsonDocument json = JsonDocument.Parse(content);
            foreach (BackupSettingDefinition definition in ConfigurationBackup.Settings)
                if (json.RootElement.TryGetProperty(definition.Key, out JsonElement value))
                    document.Data[definition.Key] = value.Clone();
        }
        using (JsonDocument visual = JsonDocument.Parse(CaptureVisualSettings()))
            document.Data["ParticleColor"] = JsonSerializer.SerializeToElement(ColorPickerColorMath.CombineThemeColor(
                visual.RootElement.GetProperty("ParticleColor").GetString()!, visual.RootElement.GetProperty("EffectOpacity").GetDouble()));
        ConfigurationBackup.SetProfiles(document, Profiles, (ComboProfiles.SelectedItem as FilterProfile)?.Id ?? string.Empty);
        var screens = document.Data.Where(item => ConfigurationBackup.IsScreenKey(item.Key))
            .Select(item => item.Value.Deserialize<ScreenSelectionState>()!).ToList();
        foreach (ScreenOptionItem screen in ScreenOptions)
        {
            var state = new ScreenSelectionState
            {
                IdentityKey = screen.IdentityKey, DeviceName = screen.DeviceName, DisplayName = screen.Title, IsEnabled = screen.IsEnabled
            };
            screens.RemoveAll(item => ConfigurationBackup.GetScreenKey(item) == ConfigurationBackup.GetScreenKey(state));
            screens.Add(state);
        }
        ConfigurationBackup.SetScreens(document, screens);
        return document;
    }

    private void OpenBackupSelection(ConfigurationBackupDocument document, bool import, string? directory)
    {
        _backupSource = document;
        _backupDirectory = directory;
        _backupImport = import;
        _backupItems.Clear();
        foreach (BackupSettingDefinition definition in ConfigurationBackup.Settings)
        {
            if (!document.Data.TryGetValue(definition.Key, out JsonElement value) &&
                !(definition.Key == "Profiles" && (document.Data.TryGetValue("Profile.Mode", out value) ||
                    document.Data.TryGetValue("Profile.Processes", out value)))) continue;
            _backupItems.Add(new BackupSelectionItem(definition.Key, Localization.Get(definition.GroupKey),
                Localization.Get(definition.TitleKey).TrimEnd(':', '：'), DescribeBackupValue(definition.Key, value)) { IsSelected = true });
        }
        foreach (var item in document.Data.Where(item => ConfigurationBackup.IsScreenKey(item.Key)))
        {
            var screen = item.Value.Deserialize<ScreenSelectionState>()!;
            _backupItems.Add(new BackupSelectionItem(item.Key, Localization.Get("MultiScreen_Title"),
                string.IsNullOrWhiteSpace(screen.DisplayName) ? screen.DeviceName : screen.DisplayName,
                DescribeBackupValue(item.Key, item.Value)) { IsSelected = true });
        }
        ApplyBackupLocalizedText();
        SearchBackupItems.Text = string.Empty;
        RefreshBackupRows();
        _ = SetModalOverlayVisibleAsync(BackupOverlay, visible: true);
    }

    private static string DescribeBackupValue(string key, JsonElement value)
    {
        string description;
        if (ConfigurationBackup.IsScreenKey(key))
            description = Localization.Get(value.GetProperty("IsEnabled").GetBoolean() ? "Basic_DarkModeOn" : "Basic_DarkModeOff");
        else if (key is "Profiles" or "Profile.Mode" or "Profile.Processes")
        {
            int count = value.ValueKind == JsonValueKind.Object ? value.GetProperty("Items").GetArrayLength() : value.GetArrayLength();
            description = Localization.Format("Backup_ProfileCount", count);
        }
        else if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            description = Localization.Get(value.GetBoolean() ? "Basic_DarkModeOn" : "Basic_DarkModeOff");
        else if (value.ValueKind == JsonValueKind.Number)
            description = value.GetDouble().ToString("0.##", CultureInfo.CurrentCulture);
        else
            description = value.GetString() ?? string.Empty;
        return Localization.Format("Backup_DataValue", description);
    }

    private void SearchBackupItems_TextChanged(object sender, TextChangedEventArgs args) => RefreshBackupRows();

    private void RefreshBackupRows()
    {
        if (ListBackupItems == null) return;
        string filter = SearchBackupItems.Text.Trim();
        var rows = new List<object>();
        string? group = null;
        foreach (BackupSelectionItem item in _backupItems.Where(item => string.IsNullOrEmpty(filter) ||
                     item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     item.Group.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     item.Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            if (group != item.Group) { group = item.Group; rows.Add(group); }
            rows.Add(item);
        }
        ListBackupItems.ItemsSource = rows;
    }

    private async void CloseBackupOverlay_Click(object sender, RoutedEventArgs args)
    {
        if (_backupBusy) return;
        if (await SetModalOverlayVisibleAsync(BackupOverlay, visible: false))
        {
            _backupSource = null;
            _backupItems.Clear();
            ListBackupItems.ItemsSource = null;
        }
    }

    private void SetBackupBusy(bool busy)
    {
        _backupBusy = busy;
        BtnExportConfiguration.IsEnabled = BtnImportConfiguration.IsEnabled = !busy;
        BtnBackupConfirm.IsEnabled = BtnBackupCancel.IsEnabled = !busy;
    }

    private async void ConfirmBackup_Click(object sender, RoutedEventArgs args)
    {
        if (_backupBusy || _backupSource == null || _isClosed) return;
        string[] selected = _backupItems.Where(item => item.IsSelected).Select(item => item.Key).ToArray();
        if (selected.Length == 0)
        {
            await ShowMessageAsync(Localization.Get("Backup_SelectData"));
            return;
        }
        SetBackupBusy(true);
        try
        {
            if (_backupImport) ImportBackupAndApply(_backupSource, selected);
            else await ConfigurationBackup.WriteAsync(_backupDirectory!, _backupSource, selected);
            if (!await SetModalOverlayVisibleAsync(BackupOverlay, visible: false) || _isClosed) return;
            await ShowMessageAsync(Localization.Get(_backupImport ? "Backup_ImportDone" : "Backup_ExportDone"));
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to transfer configuration data: {exception.Message}");
            await ShowMessageAsync(Localization.Format(_backupImport ? "Backup_ImportFailed" : "Backup_ExportFailed", exception.Message));
        }
        finally
        {
            if (!_isClosed) SetBackupBusy(false);
        }
    }

    private void ImportBackupAndApply(ConfigurationBackupDocument source, IReadOnlyCollection<string> selected)
    {
        ConfigurationBackupDocument saved = CaptureSavedBackup();
        ConfigurationBackupDocument pending = CapturePendingBackup();
        Dictionary<string, object> persistedValues = ConfigurationBackup.BuildImportValues(source, selected, saved);
        Dictionary<string, object> panelValues = ConfigurationBackup.BuildImportValues(source, selected, pending);
        if (panelValues.TryGetValue("ScreenSelections", out object? screenData))
        {
            var screens = JsonSerializer.Deserialize<List<ScreenSelectionState>>((string)screenData)!;
            if (!ScreenOptions.Any(screen => screens.Any(item => ConfigurationBackup.GetScreenKey(item) ==
                    ConfigurationBackup.GetScreenKey(new ScreenSelectionState { IdentityKey = screen.IdentityKey, DeviceName = screen.DeviceName }) && item.IsEnabled)))
                throw new InvalidDataException(Localization.Get("Msg_MinOneScreen"));
        }
        var previous = persistedValues.Keys.ToDictionary(key => key, key => typeof(ConfigManager).GetProperty(key)!.GetValue(null)!);
        SettingsState? baseline = _savedSettingsState;
        if (!ConfigManager.SaveBackupValues(persistedValues))
            throw new IOException(Localization.Get("Backup_SaveFailed"));
        try
        {
            ApplyBackupValuesToPanel(panelValues);
            ApplyImportedRuntimeSettings(selected);
            MarkImportedSettingsSaved(selected);
        }
        catch
        {
            if (!ConfigManager.SaveBackupValues(previous))
                AppLogger.Warn("Failed to roll back imported configuration.");
            ApplyBackupValuesToPanel(ConfigurationBackup.BuildImportValues(pending, selected, CapturePendingBackup()));
            ApplyImportedRuntimeSettings(selected);
            _savedSettingsState = baseline;
            UpdateApplySettingsState(SettingsSections.All);
            throw;
        }
    }

    private void ApplyBackupValuesToPanel(IReadOnlyDictionary<string, object> values)
    {
        _isLoading = true;
        _suppressValueSync = true;
        try
        {
            foreach (var item in new[]
                     {
                         ("IsEffectEnabled", CheckMasterSwitch), ("AutoStart", CheckAutoStart), ("StartSilent", CheckStartSilent),
                         ("HideTrayIcon", CheckHideTrayIcon), ("EnableAlwaysTrailEffect", CheckAlwaysTrailEffectSwitch),
                         ("RunAsAdmin", CheckRunAsAdmin), ("IsTouchscreenMode", CheckTouchscreenMode),
                         ("EnableMiddleClickTrigger", CheckMiddleClickTrigger), ("ScreenshotCompatibilityMode", CheckScreenshotCompatibilityMode),
                         ("EnableEnvironmentFilter", CheckEnvironmentFilter), ("HideInFullscreen", CheckHideInFullscreen),
                         ("ShowEffectOnDesktop", CheckShowEffectOnDesktop), ("UseLinkedEffectScale", CheckLinkedEffectScale),
                         ("UseLinkedAnimationSpeed", CheckLinkedAnimationSpeed), ("ApplyCurveDraw", CheckApplyCurveDraw),
                         ("FollowDisplayRefreshRate", CheckFollowDisplayRefreshRate)
                     })
                if (values.TryGetValue(item.Item1, out object? value)) item.Item2.IsOn = (bool)value;
            foreach (var item in new[]
                     {
                         ("EffectScale", SliderScale), ("TrailEffectScale", SliderTrailScale), ("ClickEffectScale", SliderClickScale),
                         ("GlowIntensity", SliderGlow), ("EffectSpeed", SliderSpeed), ("TrailAnimationSpeed", SliderTrailAnimSpeed),
                         ("ClickAnimationSpeed", SliderClickAnimSpeed), ("TrailRefreshRate", SliderTrailRefresh)
                     })
                if (values.TryGetValue(item.Item1, out object? value)) item.Item2.Value = Convert.ToDouble(value);
            if (values.TryGetValue("DarkMode", out object? darkMode)) SelectDarkMode((DarkModeOption)darkMode);
            if (values.TryGetValue("ClickTriggerType", out object? trigger)) RadioClickType.SelectedIndex = (int)trigger;
            if (values.TryGetValue("UiLanguage", out object? language))
                ComboLanguage.SelectedItem = ComboLanguage.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == (string)language);
            if (values.TryGetValue("ParticleColor", out object? color)) _particleColor = (string)color;
            if (values.TryGetValue("EffectOpacity", out object? opacity)) _effectOpacity = (double)opacity;
            if (values.TryGetValue("FilterProfiles", out object? profiles))
            {
                Profiles.Clear();
                foreach (FilterProfile profile in JsonSerializer.Deserialize<List<FilterProfile>>((string)profiles)!) Profiles.Add(profile);
                string active = (string)values["ActiveProfileId"];
                ComboProfiles.SelectedItem = Profiles.First(profile => profile.Id == active);
            }
            if (values.TryGetValue("ScreenSelections", out object? screenData))
            {
                var screens = JsonSerializer.Deserialize<List<ScreenSelectionState>>((string)screenData)!;
                foreach (ScreenOptionItem screen in ScreenOptions)
                {
                    var identity = new ScreenSelectionState { IdentityKey = screen.IdentityKey, DeviceName = screen.DeviceName };
                    ScreenSelectionState? saved = screens.FirstOrDefault(item => ConfigurationBackup.GetScreenKey(item) == ConfigurationBackup.GetScreenKey(identity));
                    if (saved != null) screen.IsEnabled = saved.IsEnabled;
                }
                SyncScreenToggles();
            }
            SyncSliderAndBoxValues();
            UpdateColorPreview(_particleColor);
        }
        finally
        {
            _suppressValueSync = false;
            _isLoading = false;
        }
        UpdateClickEffectPanelVisibility();
        UpdateEnvironmentFilterInterlock();
        UpdateEffectScalePanelVisibility();
        UpdateAnimationSpeedPanelVisibility();
        UpdateTrailRefreshInterlock();
        ClickCountText.Text = Localization.Format("Welcome_ClicksUnit", ConfigManager.TotalClicks);
    }

    private void ApplyImportedRuntimeSettings(IReadOnlyCollection<string> selected)
    {
        App.Tray?.SetHidden(ConfigManager.HideTrayIcon);
        ConfigManager.GetEffectScalesForOverlay(out double trailScale, out double clickScale);
        ConfigManager.GetAnimationSpeedsForOverlay(out double trailSpeed, out double clickSpeed);
        App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
        App.Overlay?.UpdateEffectSettings(trailScale, clickScale, ConfigManager.EffectOpacity, trailSpeed, clickSpeed, ConfigManager.GlowIntensity);
        App.Overlay?.UpdateTrailRefreshRate(ConfigManager.TrailRefreshRate, ConfigManager.FollowDisplayRefreshRate);
        App.Overlay?.SetCurveDraw(ConfigManager.ApplyCurveDraw);
        App.Overlay?.UpdateTouchMode(ConfigManager.IsTouchscreenMode);
        App.Overlay?.UpdateScreenshotCompatibilityMode(ConfigManager.ScreenshotCompatibilityMode);
        App.Overlay?.RefreshEnvironmentFilterState();
        if (selected.Any(ConfigurationBackup.IsScreenKey)) App.Overlay?.RefreshScreenSelection();
        if (selected.Any(key => key is "AutoStart" or "RunAsAdmin")) ApplyAutoStartSettings(useSavedSettings: true);
        if (selected.Contains("DarkMode")) ApplyDarkMode();
        if (selected.Contains("UiLanguage"))
        {
            Localization.ApplyCulture(ConfigManager.UiLanguage);
            _languageAtLoad = ConfigManager.UiLanguage;
            _pendingLanguage = ConfigManager.UiLanguage;
            ApplyLocalizedText();
            App.Tray?.RefreshLocalization();
        }
    }

    private void MarkImportedSettingsSaved(IReadOnlyCollection<string> selected)
    {
        if (_savedSettingsState == null) return;
        var general = JsonNode.Parse(_savedSettingsState.General)!.AsObject();
        var visual = JsonNode.Parse(_savedSettingsState.Visual)!.AsObject();
        var screens = JsonNode.Parse(_savedSettingsState.Screens)!.AsArray();
        var currentGeneral = JsonNode.Parse(CaptureGeneralSettings())!.AsObject();
        var currentVisual = JsonNode.Parse(CaptureVisualSettings())!.AsObject();
        foreach (string key in selected)
        {
            if (currentGeneral.ContainsKey(key)) general[key] = currentGeneral[key]?.DeepClone();
            if (currentVisual.ContainsKey(key)) visual[key] = currentVisual[key]?.DeepClone();
            if (key == "ParticleColor") visual["EffectOpacity"] = currentVisual["EffectOpacity"]?.DeepClone();
        }
        if (selected.Any(key => key is "Profiles" or "Profile.Mode" or "Profile.Processes"))
        {
            general["Profiles"] = JsonSerializer.SerializeToNode(ConfigManager.GetProfiles().OrderBy(profile => profile.Id, StringComparer.Ordinal)
                .Select(profile => new
                {
                    profile.Id, profile.Name, profile.Mode,
                    Processes = profile.Processes.Select(name => name.ToUpperInvariant()).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                }));
            general["ActiveProfileId"] = ConfigManager.ActiveProfileId;
        }
        var currentScreens = JsonNode.Parse(CaptureScreenSettings())!.AsArray();
        foreach (string key in selected.Where(ConfigurationBackup.IsScreenKey))
        {
            var savedScreen = screens.FirstOrDefault(item => ConfigurationBackup.GetScreenKey(item!.Deserialize<ScreenSelectionState>()!) == key);
            var currentScreen = currentScreens.FirstOrDefault(item => ConfigurationBackup.GetScreenKey(item!.Deserialize<ScreenSelectionState>()!) == key);
            if (savedScreen != null && currentScreen != null) savedScreen["IsEnabled"] = currentScreen["IsEnabled"]!.DeepClone();
        }
        _savedSettingsState = new SettingsState(general.ToJsonString(), visual.ToJsonString(), screens.ToJsonString());
        UpdateApplySettingsState(SettingsSections.All);
    }
}
