using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BASpark;

public sealed partial class ControlPanelWindow
{
    private readonly Dictionary<string, Control> _advancedControls = new(StringComparer.Ordinal);
    private readonly Dictionary<TextBlock, string> _advancedLabels = new();
    private ComboBox? _hdrPreset;
    private bool _updatingAdvancedControls;
    private static readonly string[] HdrPaths = ["webgpuHdrPeak", "webgpuHdrBrightness", "webgpuHdrColorPreservation",
        "webgpuHdrWhiteCore", "webgpuHdrWhiteStart", "webgpuHdrWhiteEnd"];
    private static readonly double[][] HdrPresets =
    [
        [3, 1, 0, 0.6, 1, 5],
        [3.5, 1, 0, 0.8, 0.75, 4],
        [3, 1, 1, 0, 1, 5]
    ];
    private static readonly (string Minimum, string Maximum, double Gap)[] AdvancedLinkedRanges =
    [
        ("rings.radiusMin", "rings.radiusMax", 0),
        ("shards.clickLifetimeMinMs", "shards.clickLifetimeMaxMs", 0),
        ("shards.clickSpeedMin", "shards.clickSpeedMax", 0),
        ("shards.trailLifetimeMinMs", "shards.trailLifetimeMaxMs", 0),
        ("shards.trailSpeedMin", "shards.trailSpeedMax", 0),
        ("shards.sizeMin", "shards.sizeMax", 0),
        ("webgpuHdrWhiteStart", "webgpuHdrWhiteEnd", 0.01)
    ];

    private void SetupAdvancedSettings()
    {
        foreach (var group in AdvancedFxSettings.Definitions.GroupBy(item => item.GroupKey))
        {
            PanelBaClickAdvanced.Children.Add(CreateAdvancedHeading(group.Key, "BasAdvancedGroupTitleStyle", group));
            foreach (var section in group.GroupBy(item => item.SectionKey))
            {
                var content = new StackPanel { Spacing = 4 };
                foreach (AdvancedFxSetting setting in section)
                {
                    if (setting.Path == HdrPaths[0]) content.Children.Add(CreateHdrPresetCard());
                    content.Children.Add(CreateAdvancedSettingCard(setting));
                }
                var expander = new Expander
                {
                    Style = (Style)Application.Current.Resources["BasAdvancedExpanderStyle"],
                    Header = CreateAdvancedHeading(section.Key, "BasAdvancedSectionTitleStyle", section),
                    Content = content, IsExpanded = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                expander.Loaded += AdvancedSettingsExpander_Loaded;
                PanelBaClickAdvanced.Children.Add(expander);
            }
        }
    }

    private void AdvancedSettingsExpander_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Expander expander) return;
        expander.ApplyTemplate();
        if (FindVisualDescendant<ToggleButton>(expander, "ExpanderHeader") is ToggleButton header)
            header.BorderThickness = new Thickness(0);
        if (FindVisualDescendant<Border>(expander, "ExpanderContent") is Border content)
            content.BorderThickness = new Thickness(0);
    }

    private StackPanel CreateAdvancedHeading(string key, string styleKey, IEnumerable<AdvancedFxSetting> settings)
    {
        bool group = styleKey == "BasAdvancedGroupTitleStyle";
        var heading = new StackPanel { Orientation = Orientation.Horizontal,
            Height = group ? 36 : double.NaN, Margin = group ? new Thickness(0, -4, 0, 0) : new Thickness() };
        TextBlock label = CreateAdvancedLabel(key, styleKey);
        heading.Children.Add(label);
        AdvancedFxSetting[] definitions = settings.ToArray();
        AddCardResetButton(label, heading, ResetScope.Visual, AdvancedFxSettings.KeyPrefix + key,
            () =>
            {
                foreach (AdvancedFxSetting setting in definitions)
                    SetAdvancedControlValue(setting.Path, setting.DefaultValue);
            }, state => definitions.All(setting => IsDefaultSettingValue(state, setting.Key, setting.DefaultValue)), isGroup: true);
        return heading;
    }

    private TextBlock CreateAdvancedLabel(string key, string styleKey)
    {
        var label = new TextBlock { Style = (Style)Application.Current.Resources[styleKey], Text = Localization.Get(key) };
        _advancedLabels[label] = key;
        return label;
    }

    private static Grid CreateAdvancedCardGrid()
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140, MaxWidth = 240 });
        return grid;
    }

    private Border CreateAdvancedSettingCard(AdvancedFxSetting setting)
    {
        Grid grid = CreateAdvancedCardGrid();
        TextBlock label = CreateAdvancedLabel(setting.TitleKey, "BasSettingTitleStyle");
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(label);
        text.Children.Add(CreateAdvancedLabel(setting.DescriptionKey, "BasCaptionStyle"));
        grid.Children.Add(text);
        FrameworkElement content;
        Control control;
        if (setting.Options != null || setting.Path is "rings.rotationDirection" or "rings.dissolveDirection")
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (setting.Options != null)
                foreach (AdvancedFxOption option in setting.Options)
                    combo.Items.Add(new ComboBoxItem { Tag = option.Value, Content = Localization.Get(option.TitleKey) });
            else
                foreach (double direction in new[] { -1.0, 1.0 })
                    combo.Items.Add(new ComboBoxItem { Tag = direction,
                        Content = AdvancedFxSettings.Describe(setting, JsonSerializer.SerializeToElement(direction)) });
            combo.SelectionChanged += (_, _) => AdvancedSettingChanged(setting);
            _comboWidthTrackers.Add(new(combo));
            content = control = combo;
        }
        else if (setting.DefaultValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var toggle = new ToggleSwitch { Style = (Style)Application.Current.Resources["BasSettingToggleStyle"] };
            toggle.Toggled += (_, _) => AdvancedSettingChanged(setting);
            content = control = toggle;
        }
        else
        {
            var slider = new Slider { Minimum = setting.Minimum, Maximum = setting.Maximum,
                StepFrequency = setting.Step, MinWidth = 40, VerticalAlignment = VerticalAlignment.Center, Tag = setting };
            var box = new NumberBox { Minimum = setting.Minimum, Maximum = setting.Maximum,
                SmallChange = setting.Step, LargeChange = Math.Max(setting.Step, setting.Step * 10), Width = 96,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, VerticalAlignment = VerticalAlignment.Center };
            RegisterSliderPair(slider, box);
            slider.ValueChanged += (_, _) => AdvancedSettingChanged(setting);
            var pair = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pair.Children.Add(slider);
            Grid.SetColumn(box, 1);
            pair.Children.Add(box);
            AutomationProperties.SetName(box, Localization.Get(setting.TitleKey));
            content = pair;
            control = slider;
        }
        control.Tag = setting;
        _advancedControls[setting.Path] = control;
        AddCardResetButton(label, text, ResetScope.Visual, setting.Key,
            () => SetAdvancedControlValue(setting.Path, setting.DefaultValue),
            state => IsDefaultSettingValue(state, setting.Key, setting.DefaultValue));
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return new Border { Style = (Style)Application.Current.Resources["BasSettingCardStyle"], Margin = new Thickness(0), Child = grid };
    }

    private Border CreateHdrPresetCard()
    {
        Grid grid = CreateAdvancedCardGrid();
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        TextBlock title = CreateAdvancedLabel("Advanced_HdrPreset", "BasSettingTitleStyle");
        text.Children.Add(title);
        text.Children.Add(CreateAdvancedLabel("Advanced_HdrHint", "BasCaptionStyle"));
        grid.Children.Add(text);
        _hdrPreset = new ComboBox { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (string key in new[] { "Advanced_HdrBalanced", "Advanced_HdrBright", "Advanced_HdrColor", "Advanced_HdrCustom" })
            _hdrPreset.Items.Add(new ComboBoxItem { Tag = key, Content = Localization.Get(key) });
        _hdrPreset.SelectionChanged += (_, _) =>
        {
            if (_updatingAdvancedControls || _isLoading || !IsUiReady || _hdrPreset.SelectedIndex is < 0 or > 2) return;
            _updatingAdvancedControls = true;
            try
            {
                for (int index = 0; index < HdrPaths.Length; index++)
                    SetAdvancedControlValue(HdrPaths[index], JsonSerializer.SerializeToElement(HdrPresets[_hdrPreset.SelectedIndex][index]));
            }
            finally { _updatingAdvancedControls = false; }
            QueueSettingsChangeCheck(SettingsSections.Visual);
        };
        _comboWidthTrackers.Add(new(_hdrPreset));
        Grid.SetColumn(_hdrPreset, 1);
        grid.Children.Add(_hdrPreset);
        AddCardResetButton(title, text, ResetScope.Visual, "HdrPreset", () =>
        {
            foreach (string path in HdrPaths)
                SetAdvancedControlValue(path, AdvancedFxSettings.Definitions.Single(item => item.Path == path).DefaultValue);
        }, state => HdrPaths.Select(path => AdvancedFxSettings.Definitions.Single(item => item.Path == path))
            .All(setting => IsDefaultSettingValue(state, setting.Key, setting.DefaultValue)));
        return new Border { Style = (Style)Application.Current.Resources["BasSettingCardStyle"], Margin = new Thickness(0), Child = grid };
    }

    private void ApplyAdvancedLocalizedText()
    {
        TxtVisualAdvancedTitle.Text = Localization.Get("Visual_AdvancedSection");
        CheckBaClickAdvancedSettings.Header = Localization.Get("Advanced_Enable");
        TxtBaClickAdvancedDescription.Text = Localization.Get("Advanced_EnableDescription");
        foreach (var pair in _advancedLabels) pair.Key.Text = Localization.Get(pair.Value);
        foreach (AdvancedFxSetting setting in AdvancedFxSettings.Definitions)
        {
            if (!_advancedControls.TryGetValue(setting.Path, out Control? control)) continue;
            string title = Localization.Get(setting.TitleKey);
            AutomationProperties.SetName(control, title);
            if (control is ToggleSwitch toggle) toggle.Header = title;
            if (control is Slider slider) AutomationProperties.SetName(_sliderToBox[slider], title);
            if (control is not ComboBox combo) continue;
            foreach (ComboBoxItem item in combo.Items)
                item.Content = setting.Options is null
                    ? AdvancedFxSettings.Describe(setting, JsonSerializer.SerializeToElement((double)item.Tag))
                    : Localization.Get(setting.Options.First(option => option.Value == (string)item.Tag).TitleKey);
        }
        if (_hdrPreset != null)
        {
            AutomationProperties.SetName(_hdrPreset, Localization.Get("Advanced_HdrPreset"));
            foreach (ComboBoxItem item in _hdrPreset.Items) item.Content = Localization.Get((string)item.Tag);
        }
    }

    private void LoadAdvancedSettings(bool enabled, string content)
    {
        bool previous = _updatingAdvancedControls;
        _updatingAdvancedControls = true;
        try
        {
            CheckBaClickAdvancedSettings.IsOn = enabled;
            Dictionary<string, JsonElement> values = AdvancedFxSettings.Parse(content);
            foreach (AdvancedFxSetting definition in AdvancedFxSettings.Definitions)
                SetAdvancedControlValue(definition.Path, AdvancedFxSettings.GetValue(values, definition));
        }
        finally { _updatingAdvancedControls = previous; }
        UpdateAdvancedSettingsVisibility();
        UpdateAdvancedHdrControls();
    }

    private void SetAdvancedControlValue(string path, JsonElement value)
    {
        bool previous = _suppressValueSync;
        _suppressValueSync = true;
        try
        {
            switch (_advancedControls[path])
            {
                case Slider slider:
                    slider.Value = value.GetDouble();
                    _sliderToBox[slider].Value = value.GetDouble();
                    break;
                case ToggleSwitch toggle:
                    toggle.IsOn = value.GetBoolean();
                    break;
                case ComboBox combo:
                    combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().First(item =>
                        value.ValueKind == JsonValueKind.String ? (string)item.Tag == value.GetString() : (double)item.Tag == value.GetDouble());
                    break;
            }
        }
        finally { _suppressValueSync = previous; }
    }

    private JsonElement CaptureAdvancedValue(AdvancedFxSetting definition)
    {
        Control control = _advancedControls[definition.Path];
        if (control is ToggleSwitch toggle) return JsonSerializer.SerializeToElement(toggle.IsOn);
        if (control is ComboBox combo) return JsonSerializer.SerializeToElement(((ComboBoxItem)combo.SelectedItem).Tag);
        var slider = (Slider)control;
        double value = slider.Value;
        NumberBox box = _sliderToBox[slider];
        if (_numberInputs.TryGetValue(box, out TextBox? input) && input.FocusState != FocusState.Unfocused &&
            input.Text != box.NumberFormatter.FormatDouble(box.Value) &&
            box.NumberFormatter is Windows.Globalization.NumberFormatting.INumberParser parser &&
            parser.ParseDouble(input.Text) is double parsed && double.IsFinite(parsed))
            value = Math.Round(Math.Clamp(parsed, slider.Minimum, slider.Maximum), definition.Precision);
        return JsonSerializer.SerializeToElement(value);
    }

    private string CaptureAdvancedSettings() => AdvancedFxSettings.Serialize(AdvancedFxSettings.Definitions
        .ToDictionary(item => item.Path, CaptureAdvancedValue, StringComparer.Ordinal));

    private void AdvancedSettingChanged(AdvancedFxSetting definition)
    {
        if (_updatingAdvancedControls || _isLoading || _suppressValueSync || !IsUiReady) return;
        _updatingAdvancedControls = true;
        try
        {
            foreach (var link in AdvancedLinkedRanges.Where(link => definition.Path == link.Minimum || definition.Path == link.Maximum))
            {
                var minimum = (Slider)_advancedControls[link.Minimum];
                var maximum = (Slider)_advancedControls[link.Maximum];
                if (minimum.Value + link.Gap <= maximum.Value) continue;
                string other = definition.Path == link.Minimum ? link.Maximum : link.Minimum;
                double value = definition.Path == link.Minimum ? minimum.Value + link.Gap : maximum.Value - link.Gap;
                SetAdvancedControlValue(other, JsonSerializer.SerializeToElement(value));
            }
        }
        finally { _updatingAdvancedControls = false; }
        if (!definition.IsEffectParameter) UpdateAdvancedHdrControls();
        QueueSettingsChangeCheck(SettingsSections.Visual);
    }

    private void UpdateAdvancedHdrControls()
    {
        if (_hdrPreset == null || !_advancedControls.TryGetValue("renderMode", out Control? mode)) return;
        bool enabled = ((ComboBoxItem)((ComboBox)mode).SelectedItem)?.Tag as string == "full-webgpu";
        _hdrPreset.IsEnabled = enabled;
        foreach (string path in HdrPaths)
        {
            _advancedControls[path].IsEnabled = enabled;
            _sliderToBox[(Slider)_advancedControls[path]].IsEnabled = enabled;
        }
        bool previous = _updatingAdvancedControls;
        _updatingAdvancedControls = true;
        try
        {
            int selected = Array.FindIndex(HdrPresets, values => HdrPaths.Select((path, index) =>
                Math.Abs(((Slider)_advancedControls[path]).Value - values[index]) < 0.000000001).All(matches => matches));
            _hdrPreset.SelectedIndex = selected < 0 ? 3 : selected;
        }
        finally { _updatingAdvancedControls = previous; }
    }

    private void ConditionalSettingsList_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Expander expander) return;
        expander.ApplyTemplate();
        if (FindVisualDescendant<ToggleButton>(expander, "ExpanderHeader") is ToggleButton header)
            header.Visibility = Visibility.Collapsed;
        if (FindVisualDescendant<Border>(expander, "ExpanderContent") is Border content)
            content.BorderThickness = new Thickness(0);
    }

    private void BaClickAdvancedSettings_Changed(object sender, RoutedEventArgs args) => UpdateAdvancedSettingsVisibility();

    private void UpdateAdvancedSettingsVisibility() =>
        BaClickAdvancedList.IsExpanded = CheckBaClickAdvancedSettings.IsOn;

    private static int GetSliderPrecision(Slider slider) => slider.Tag is AdvancedFxSetting setting
        ? setting.Precision : slider.StepFrequency >= 1 ? 0 : 2;

    private void RebuildAdvancedResetItems()
    {
        string group = GetResetGroup("Nav_Visual", "Visual_AdvancedSection");
        AddToggleResetItem(group, CheckBaClickAdvancedSettings, false, "EnableBaClickAdvancedSettings");
        foreach (AdvancedFxSetting definition in AdvancedFxSettings.Definitions)
            AddPageResetItem(group, Localization.Get(definition.TitleKey), AdvancedFxSettings.Describe(definition, definition.DefaultValue),
                definition.Key, () => SetAdvancedControlValue(definition.Path, definition.DefaultValue), () =>
                {
                    Dictionary<string, JsonElement> values = AdvancedFxSettings.Parse(ConfigManager.BaClickAdvancedSettings);
                    values.Remove(definition.Path);
                    SaveResetValue("BaClickAdvancedSettings", AdvancedFxSettings.Serialize(values));
                }, defaultValue: definition.DefaultValue);
    }
}
