using System.Reflection;

namespace BASpark.Tests;

public class ConfigManagerTests
{
    [Fact]
    public void DeserializeProfiles_DropsNullProfilesAndRepairsNullFields()
    {
        const string json = """
            [
              null,
              {
                "Id": "profile-1",
                "Name": null,
                "Mode": 1,
                "Processes": null
              }
            ]
            """;

        MethodInfo deserialize = typeof(ConfigManager).GetMethod(
            "DeserializeProfiles",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var profiles = Assert.IsType<List<FilterProfile>>(deserialize.Invoke(null, [json]));
        FilterProfile profile = Assert.Single(profiles);

        Assert.Equal("profile-1", profile.Id);
        Assert.Equal("", profile.Name);
        Assert.Empty(profile.Processes);
    }

    [Fact]
    public void ProfileAccessors_ReturnDeepCopies()
    {
        FieldInfo profilesField = typeof(ConfigManager).GetField(
            "_profiles",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalProfiles = (List<FilterProfile>)profilesField.GetValue(null)!;
        string originalActiveId = ConfigManager.ActiveProfileId;

        try
        {
            profilesField.SetValue(null, new List<FilterProfile>
            {
                new()
                {
                    Id = "profile-1",
                    Name = "Stored",
                    Processes = new List<string> { "explorer.exe" }
                }
            });
            ConfigManager.ActiveProfileId = "profile-1";

            List<FilterProfile> editableProfiles = ConfigManager.GetProfiles();
            editableProfiles[0].Name = "Edited";
            editableProfiles[0].Processes.Add("notepad.exe");

            FilterProfile storedProfile = Assert.IsType<FilterProfile>(ConfigManager.GetActiveProfile());
            Assert.Equal("Stored", storedProfile.Name);
            Assert.Equal(new[] { "explorer.exe" }, storedProfile.Processes);

            storedProfile.Processes.Clear();
            Assert.Equal(new[] { "explorer.exe" }, ConfigManager.GetActiveProfile()!.Processes);
        }
        finally
        {
            profilesField.SetValue(null, originalProfiles);
            ConfigManager.ActiveProfileId = originalActiveId;
        }
    }

    [Fact]
    public void ProcessSuppression_UsesActiveProfileWithoutExposingStoredProfile()
    {
        FieldInfo profilesField = typeof(ConfigManager).GetField(
            "_profiles",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalProfiles = (List<FilterProfile>)profilesField.GetValue(null)!;
        string originalActiveId = ConfigManager.ActiveProfileId;

        try
        {
            profilesField.SetValue(null, new List<FilterProfile>
            {
                new()
                {
                    Id = "profile-1",
                    Mode = ProcessFilterModeOption.Blacklist,
                    Processes = new List<string> { "Game.exe" }
                }
            });
            ConfigManager.ActiveProfileId = "profile-1";

            Assert.True(ConfigManager.IsProcessSuppressedByActiveProfile("game.EXE"));
            Assert.False(ConfigManager.IsProcessSuppressedByActiveProfile("explorer.exe"));
        }
        finally
        {
            profilesField.SetValue(null, originalProfiles);
            ConfigManager.ActiveProfileId = originalActiveId;
        }
    }

    [Fact]
    public void GetScreenSelections_DropsNullAndUnidentifiedEntries()
    {
        string originalSelections = ConfigManager.ScreenSelections;

        try
        {
            ConfigManager.ScreenSelections = """
                [
                  null,
                  {},
                  {
                    "IdentityKey": null,
                    "DeviceName": "  DISPLAY1  ",
                    "DisplayName": null,
                    "IsEnabled": true
                  }
                ]
                """;

            ScreenSelectionState selection = Assert.Single(ConfigManager.GetScreenSelections());

            Assert.Equal("", selection.IdentityKey);
            Assert.Equal("DISPLAY1", selection.DeviceName);
            Assert.Equal("", selection.DisplayName);
            Assert.True(selection.IsEnabled);
        }
        finally
        {
            ConfigManager.ScreenSelections = originalSelections;
        }
    }
}
