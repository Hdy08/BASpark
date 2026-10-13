using System.Text.Json;

namespace BASpark;

internal static class SelectionBatch
{
    internal static void Change(IReadOnlyCollection<SelectionCardItem> items, bool invert)
    {
        bool select = !items.All(item => item.IsSelected);
        foreach (SelectionCardItem item in items) item.IsSelected = invert ? !item.IsSelected : select;
    }
}

[Flags]
internal enum SettingsSections { Basic = 1, Visual = 2, Screens = 4, Filter = 8, All = 15 }

internal sealed record SettingsState(string Basic, string Visual, string Screens, string Filter)
{
    internal string GetPage(SettingsSections page) => page switch
    {
        SettingsSections.Basic => Basic,
        SettingsSections.Visual => Visual,
        SettingsSections.Screens => Screens,
        SettingsSections.Filter => Filter,
        _ => string.Empty
    };

    internal bool HasChanges(SettingsState saved, SettingsSections page) => GetPage(page) != saved.GetPage(page);

    internal SettingsState Accept(SettingsState current, SettingsSections page) => this with
    {
        Basic = page.HasFlag(SettingsSections.Basic) ? current.Basic : Basic,
        Visual = page.HasFlag(SettingsSections.Visual) ? current.Visual : Visual,
        Screens = page.HasFlag(SettingsSections.Screens) ? current.Screens : Screens,
        Filter = page.HasFlag(SettingsSections.Filter) ? current.Filter : Filter
    };

    internal Dictionary<string, object> GetValues(SettingsSections page)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (page == SettingsSections.Screens)
        {
            values["ScreenSelections"] = Screens;
            return values;
        }
        if (GetPage(page).Length == 0) return values;
        using JsonDocument document = JsonDocument.Parse(GetPage(page));
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (AdvancedFxSettings.Find(property.Name) != null) continue;
            string name = property.Name == "Profiles" ? "FilterProfiles" : property.Name;
            if (name == "FilterProfiles") values[name] = property.Value.GetRawText();
            else if (typeof(ConfigManager).GetProperty(name) is { } target && property.Value.ValueKind != JsonValueKind.Null)
                values[name] = property.Value.Deserialize(target.PropertyType)!;
        }
        if (page == SettingsSections.Visual)
        {
            var advanced = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (AdvancedFxSetting definition in AdvancedFxSettings.Definitions)
                if (document.RootElement.TryGetProperty(definition.Key, out JsonElement value))
                    advanced[definition.Path] = AdvancedFxSettings.Validate(definition, value);
            values["BaClickAdvancedSettings"] = AdvancedFxSettings.Serialize(advanced);
        }
        return values;
    }
}
