using OpenBaseCamp.Core.Config;
using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Model;
using Xunit;

namespace OpenBaseCamp.Core.Tests;

public class BrightnessLevelsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(13, 25)]
    [InlineData(40, 50)]
    [InlineData(84, 75)]
    [InlineData(88, 100)]
    [InlineData(97, 100)]
    [InlineData(1000, 100)]
    [InlineData(-40, 0)]
    public void Snap_picks_the_nearest_firmware_level(int input, int expected) =>
        Assert.Equal(expected, BrightnessLevels.Snap(input));

    [Fact]
    public void Step_clamps_at_both_ends()
    {
        Assert.Equal(0, BrightnessLevels.Step(0, -1));
        Assert.Equal(25, BrightnessLevels.Step(0, +1));
        Assert.Equal(100, BrightnessLevels.Step(100, +1));
        Assert.Equal(75, BrightnessLevels.Step(100, -1));
    }

    [Fact]
    public void Cycle_wraps_back_to_zero()
    {
        Assert.Equal(25, BrightnessLevels.Cycle(0));
        Assert.Equal(0, BrightnessLevels.Cycle(100));
    }
}

public class PageTests
{
    [Fact]
    public void EnsureKeys_produces_exactly_twelve_slots_in_order()
    {
        var page = new Page();
        page.EnsureKeys();

        Assert.Equal(DisplayPadLayout.KeyCount, page.Keys.Count);
        Assert.Equal(Enumerable.Range(0, DisplayPadLayout.KeyCount), page.Keys.Select(k => k.Index));
    }

    [Fact]
    public void EnsureKeys_keeps_existing_slots_and_drops_out_of_range_ones()
    {
        var page = new Page
        {
            Keys =
            {
                new KeySlot { Index = 3, Action = new KeyAction { Kind = ActionKind.Text } },
                new KeySlot { Index = 99, Action = new KeyAction { Kind = ActionKind.Mouse } },
            },
        };

        page.EnsureKeys();

        Assert.Equal(DisplayPadLayout.KeyCount, page.Keys.Count);
        Assert.Equal(ActionKind.Text, page.Keys[3].Action.Kind);
        Assert.DoesNotContain(page.Keys, k => k.Action.Kind == ActionKind.Mouse);
    }
}

public class ProfileTests
{
    [Fact]
    public void Normalize_reparents_pages_whose_parent_disappeared()
    {
        var profile = Profile.Create("Test");
        var orphan = Page.Create("Orphan", "does-not-exist");
        profile.Pages.Add(orphan);

        profile.Normalize();

        Assert.Equal(profile.RootPageId, orphan.ParentPageId);
    }

    [Fact]
    public void Normalize_repairs_a_missing_root()
    {
        var profile = Profile.Create("Test");
        profile.RootPageId = "gone";

        profile.Normalize();

        Assert.NotNull(profile.FindPage(profile.RootPageId));
    }

    [Fact]
    public void Clone_rewires_folder_keys_to_the_copied_pages()
    {
        var profile = Profile.Create("Test");
        var child = Page.Create("Child", profile.RootPageId);
        profile.Pages.Add(child);
        profile.Root.Keys[0].Action = new KeyAction
        {
            Kind = ActionKind.Folder,
            Settings = { TargetPageId = child.Id },
        };

        var copy = profile.Clone();
        var copiedTarget = copy.Root.Keys[0].Action.Settings.TargetPageId;

        Assert.NotNull(copiedTarget);
        Assert.NotEqual(child.Id, copiedTarget);
        Assert.NotNull(copy.FindPage(copiedTarget));
        Assert.Equal("Child", copy.FindPage(copiedTarget)!.Name);

        // The original must be untouched.
        Assert.Equal(child.Id, profile.Root.Keys[0].Action.Settings.TargetPageId);
    }

    [Fact]
    public void FindOrphanPages_reports_pages_no_folder_points_at()
    {
        var profile = Profile.Create("Test");
        var linked = Page.Create("Linked", profile.RootPageId);
        var stray = Page.Create("Stray", profile.RootPageId);
        profile.Pages.Add(linked);
        profile.Pages.Add(stray);
        profile.Root.Keys[0].Action = new KeyAction
        {
            Kind = ActionKind.Folder,
            Settings = { TargetPageId = linked.Id },
        };

        var orphans = profile.FindOrphanPages();

        Assert.Single(orphans);
        Assert.Equal("Stray", orphans[0].Name);
    }
}

public class KeyMatrixMapTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(11, 11)]
    public void Guess_accepts_a_zero_based_matrix(int matrix, int expected) =>
        Assert.Equal(expected, KeyMatrixMap.Guess(matrix));

    [Fact]
    public void Guess_accepts_a_one_based_matrix_beyond_the_zero_based_range() =>
        Assert.Equal(11, KeyMatrixMap.Guess(12));

    // Row in the high byte, column in the low byte, over the pad's 6 x 2 grid.
    [Theory]
    [InlineData(0x0101, 0)]
    [InlineData(0x0106, 5)]
    [InlineData(0x0201, 6)]
    [InlineData(0x0206, 11)]
    public void Guess_decodes_a_one_based_row_column_matrix(int matrix, int expected) =>
        Assert.Equal(expected, KeyMatrixMap.Guess(matrix));

    [Theory]
    [InlineData(0x7F7F)]
    [InlineData(0x0304)] // a third row does not exist on a 6 x 2 pad
    public void Guess_returns_unknown_for_a_code_outside_the_pad(int matrix) =>
        Assert.Equal(KeyMatrixMap.Unknown, KeyMatrixMap.Guess(matrix));

    [Fact]
    public void Guess_follows_a_non_default_column_count()
    {
        // Same code, read as a 4-column pad: row 2, column 1 is index 4 rather than 6.
        Assert.Equal(6, KeyMatrixMap.Guess(0x0201));
        Assert.Equal(4, KeyMatrixMap.Guess(0x0201, columns: 4));
    }

    [Fact]
    public void A_learned_map_wins_over_the_heuristic()
    {
        var settings = new DeviceSettings();
        KeyMatrixMap.Learn(settings, matrix: 500, keyIndex: 0);

        Assert.Equal(0, KeyMatrixMap.Resolve(settings, 500));

        // 3 would resolve to index 3 by the heuristic, but a learned map is authoritative.
        Assert.Equal(KeyMatrixMap.Unknown, KeyMatrixMap.Resolve(settings, 3));
    }

    [Fact]
    public void Learning_the_same_index_twice_replaces_the_earlier_code()
    {
        var settings = new DeviceSettings();
        KeyMatrixMap.Learn(settings, 100, 0);
        KeyMatrixMap.Learn(settings, 200, 0);

        Assert.Equal(KeyMatrixMap.Unknown, KeyMatrixMap.Resolve(settings, 100));
        Assert.Equal(0, KeyMatrixMap.Resolve(settings, 200));
        Assert.Single(settings.KeyMatrixMap);
    }

    [Fact]
    public void IsComplete_needs_all_twelve_keys()
    {
        var settings = new DeviceSettings();
        for (var i = 0; i < DisplayPadLayout.KeyCount - 1; i++)
        {
            KeyMatrixMap.Learn(settings, 1000 + i, i);
        }

        Assert.False(KeyMatrixMap.IsComplete(settings));

        KeyMatrixMap.Learn(settings, 1011, DisplayPadLayout.KeyCount - 1);
        Assert.True(KeyMatrixMap.IsComplete(settings));
    }

    [Fact]
    public void Learn_ignores_an_index_outside_the_pad()
    {
        var settings = new DeviceSettings();
        KeyMatrixMap.Learn(settings, 1, 12);
        Assert.Empty(settings.KeyMatrixMap);
    }
}

public class AppConfigTests
{
    [Fact]
    public void Normalize_creates_a_starter_profile_when_there_are_none()
    {
        var config = new AppConfig();
        config.Normalize();

        Assert.Single(config.Profiles);
        Assert.Equal(config.Profiles[0].Id, config.ActiveProfileId);
    }

    [Fact]
    public void Normalize_snaps_brightness_and_clamps_the_refresh_rate()
    {
        var config = ConfigStore.CreateDefault();
        config.Settings.Device.Brightness = 63;
        config.Settings.MonitorRefreshMs = 5;

        config.Normalize();

        Assert.Equal(75, config.Settings.Device.Brightness);
        Assert.Equal(250, config.Settings.MonitorRefreshMs);
    }

    [Fact]
    public void Normalize_falls_back_to_the_first_profile_when_the_active_one_is_gone()
    {
        var config = ConfigStore.CreateDefault();
        config.ActiveProfileId = "missing";

        config.Normalize();

        Assert.Equal(config.Profiles[0].Id, config.ActiveProfileId);
    }
}
