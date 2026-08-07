using OpenBaseCamp.Core.Config;
using OpenBaseCamp.Core.Model;
using Xunit;

namespace OpenBaseCamp.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obc-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Round_trips_a_configuration_including_nested_actions()
    {
        var store = new ConfigStore(_directory);
        var config = ConfigStore.CreateDefault();

        var profile = config.Profiles[0];
        profile.AutoSwitchProcesses.Add("obs64.exe");

        var slot = profile.Root.Keys[0];
        slot.Action = new KeyAction
        {
            Kind = ActionKind.MultiAction,
            Settings =
            {
                Steps =
                {
                    new KeyAction { Kind = ActionKind.Hotkey, Settings = { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey = 'S' } },
                    new KeyAction { Kind = ActionKind.Delay, Settings = { DelayMs = 500 } },
                    new KeyAction { Kind = ActionKind.Obs, Settings = { ObsCommand = ObsCommand.SetScene, ObsScene = "Starting Soon" } },
                },
            },
        };
        slot.Appearance.Title = "Go live";
        slot.Appearance.BackgroundColor = "#FF203040";

        config.Settings.Obs.Enabled = true;
        config.Settings.Obs.Password = "hunter2";
        config.Settings.Device.KeyMatrixMap["37"] = 4;

        store.Save(config);
        var loaded = store.Load();

        var reloaded = loaded.Profiles[0].Root.Keys[0];
        Assert.Equal(ActionKind.MultiAction, reloaded.Action.Kind);
        Assert.Equal(3, reloaded.Action.Settings.Steps.Count);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, reloaded.Action.Settings.Steps[0].Settings.Modifiers);
        Assert.Equal(500, reloaded.Action.Settings.Steps[1].Settings.DelayMs);
        Assert.Equal("Starting Soon", reloaded.Action.Settings.Steps[2].Settings.ObsScene);
        Assert.Equal("Go live", reloaded.Appearance.Title);
        Assert.Equal("#FF203040", reloaded.Appearance.BackgroundColor);
        Assert.True(loaded.Settings.Obs.Enabled);
        Assert.Equal("hunter2", loaded.Settings.Obs.Password);
        Assert.Equal(4, loaded.Settings.Device.KeyMatrixMap["37"]);
        Assert.Contains("obs64.exe", loaded.Profiles[0].AutoSwitchProcesses);
    }

    [Fact]
    public void Enums_are_written_as_names_so_the_file_stays_readable()
    {
        var store = new ConfigStore(_directory);
        var config = ConfigStore.CreateDefault();
        config.Profiles[0].Root.Keys[0].Action = new KeyAction { Kind = ActionKind.OpenWebsite };

        store.Save(config);
        var json = File.ReadAllText(store.ConfigFile);

        Assert.Contains("\"OpenWebsite\"", json);
        Assert.DoesNotContain("\"Kind\": 13", json);
    }

    [Fact]
    public void A_corrupt_file_is_backed_up_rather_than_silently_replaced()
    {
        var store = new ConfigStore(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.ConfigFile, "{ this is not json");

        var config = store.Load();

        Assert.NotEmpty(config.Profiles);
        Assert.True(File.Exists(store.ConfigFile + ".broken"));
    }

    [Fact]
    public void Saving_twice_replaces_the_file_without_leaving_a_temp_behind()
    {
        var store = new ConfigStore(_directory);
        var config = ConfigStore.CreateDefault();

        store.Save(config);
        config.Profiles[0].Name = "Second";
        store.Save(config);

        Assert.False(File.Exists(store.ConfigFile + ".tmp"));
        Assert.Equal("Second", store.Load().Profiles[0].Name);
    }

    [Fact]
    public void Exported_profiles_import_with_fresh_identifiers()
    {
        var store = new ConfigStore(_directory);
        var config = ConfigStore.CreateDefault();
        var profile = config.Profiles[0];
        var path = Path.Combine(_directory, "profile.json");
        Directory.CreateDirectory(_directory);

        store.ExportProfile(profile, path);
        var imported = store.ImportProfile(path);

        Assert.NotEqual(profile.Id, imported.Id);
        Assert.Equal(profile.Name, imported.Name);
        Assert.Equal(profile.Root.Keys.Count, imported.Root.Keys.Count);
    }

    [Fact]
    public void ImportImage_copies_the_file_under_a_unique_name()
    {
        var store = new ConfigStore(_directory);
        store.Load();

        var source = Path.Combine(_directory, "icon.png");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });

        var first = store.ImportImage(source);
        var second = store.ImportImage(source);

        Assert.NotEqual(first, second);
        Assert.NotNull(store.ResolveImage(first));
        Assert.NotNull(store.ResolveImage(second));
        Assert.Null(store.ResolveImage("nope.png"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only.
        }
    }
}
