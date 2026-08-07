using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Integrations.Obs;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Monitoring;
using OpenBaseCamp.Core.Rendering;
using OpenBaseCamp.Core.Services;
using SkiaSharp;
using Xunit;

namespace OpenBaseCamp.Core.Tests;

public class KeyImageRendererTests
{
    private static KeyRenderRequest Request(KeyAppearance appearance, int size = DisplayPadLayout.KeyPixels) =>
        new() { Appearance = appearance, Size = size };

    [Fact]
    public void Renders_at_the_size_the_pad_expects()
    {
        using var bitmap = KeyImageRenderer.Render(Request(new KeyAppearance()));

        Assert.Equal(DisplayPadLayout.KeyPixels, bitmap.Width);
        Assert.Equal(DisplayPadLayout.KeyPixels, bitmap.Height);
    }

    [Fact]
    public void A_bare_key_is_filled_with_its_background_colour()
    {
        using var bitmap = KeyImageRenderer.Render(Request(new KeyAppearance
        {
            BackgroundColor = "#FF204080",
            ShowTitle = false,
            IconId = null,
        }));

        var pixel = bitmap.GetPixel(4, 4);
        Assert.Equal(0x20, pixel.Red);
        Assert.Equal(0x40, pixel.Green);
        Assert.Equal(0x80, pixel.Blue);
    }

    [Fact]
    public void A_title_puts_light_pixels_in_the_bottom_band()
    {
        var appearance = new KeyAppearance
        {
            BackgroundColor = "#FF000000",
            Title = "REC",
            ShowTitle = true,
            TitlePosition = TitlePosition.Bottom,
            IconId = null,
        };

        using var bitmap = KeyImageRenderer.Render(Request(appearance));

        var bright = 0;
        for (var y = DisplayPadLayout.KeyPixels - 26; y < DisplayPadLayout.KeyPixels; y++)
        {
            for (var x = 0; x < DisplayPadLayout.KeyPixels; x++)
            {
                if (bitmap.GetPixel(x, y).Red > 180)
                {
                    bright++;
                }
            }
        }

        Assert.True(bright > 40, $"expected drawn title pixels, found {bright}");
    }

    [Fact]
    public void Every_built_in_icon_draws_something()
    {
        foreach (var icon in BuiltInIcons.All.Where(i => i.Id != "blank"))
        {
            using var bitmap = KeyImageRenderer.Render(Request(new KeyAppearance
            {
                BackgroundColor = "#FF000000",
                IconId = icon.Id,
                ShowTitle = false,
                IconScale = 0.8,
            }));

            var drawn = 0;
            for (var y = 0; y < bitmap.Height; y += 2)
            {
                for (var x = 0; x < bitmap.Width; x += 2)
                {
                    if (bitmap.GetPixel(x, y).Red > 120)
                    {
                        drawn++;
                    }
                }
            }

            Assert.True(drawn > 15, $"icon '{icon.Id}' rendered {drawn} lit pixels");
        }
    }

    [Fact]
    public void An_unknown_icon_id_still_renders_a_key()
    {
        using var bitmap = KeyImageRenderer.Render(Request(new KeyAppearance { IconId = "does-not-exist" }));
        Assert.Equal(DisplayPadLayout.KeyPixels, bitmap.Width);
    }

    [Fact]
    public void A_missing_image_file_falls_back_instead_of_throwing()
    {
        var request = new KeyRenderRequest
        {
            Appearance = new KeyAppearance { ImageFile = "gone.png" },
            ResolveImagePath = _ => "/definitely/not/here.png",
        };

        using var bitmap = KeyImageRenderer.Render(request);
        Assert.Equal(DisplayPadLayout.KeyPixels, bitmap.Width);
    }

    [Fact]
    public void A_very_long_title_is_wrapped_and_ellipsised_without_throwing()
    {
        using var bitmap = KeyImageRenderer.Render(Request(new KeyAppearance
        {
            Title = "An extremely long key title that could never fit on a 102 pixel button",
        }));

        Assert.Equal(DisplayPadLayout.KeyPixels, bitmap.Width);
    }

    [Fact]
    public void Png_output_is_a_real_png()
    {
        var png = KeyImageRenderer.RenderPng(Request(new KeyAppearance()));

        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }

    [Fact]
    public void The_gauge_only_appears_when_a_value_is_supplied()
    {
        var appearance = new KeyAppearance { BackgroundColor = "#FF000000", ShowTitle = false, IconId = null };

        using var without = KeyImageRenderer.Render(new KeyRenderRequest { Appearance = appearance });
        using var with = KeyImageRenderer.Render(new KeyRenderRequest { Appearance = appearance, Gauge = 1.0 });

        Assert.NotEqual(CountLit(without), CountLit(with));
    }

    private static int CountLit(SKBitmap bitmap)
    {
        var lit = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red + pixel.Green + pixel.Blue > 60)
                {
                    lit++;
                }
            }
        }

        return lit;
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0, 255)]
    [InlineData("#80FF0000", 255, 0, 0, 128)]
    [InlineData("garbage", 1, 2, 3, 255)]
    [InlineData(null, 1, 2, 3, 255)]
    public void ParseColor_handles_both_forms_and_falls_back(string? input, byte r, byte g, byte b, byte a)
    {
        var fallback = new SKColor(1, 2, 3);
        var color = KeyImageRenderer.ParseColor(input, fallback);

        Assert.Equal(r, color.Red);
        Assert.Equal(g, color.Green);
        Assert.Equal(b, color.Blue);
        Assert.Equal(a, color.Alpha);
    }
}

public class MetricFormatterTests
{
    private static readonly SystemMetrics Sample = new(
        CpuPercent: 42.4,
        RamPercent: 61.6,
        RamUsedGb: 19.7,
        RamTotalGb: 32,
        GpuPercent: -1,
        DiskUsedPercent: 73.2,
        DiskFreeGb: 512.7,
        NetworkDownKbps: 8400,
        NetworkUpKbps: 96.4);

    private static readonly DateTime Now = new(2026, 8, 7, 14, 5, 0);

    [Fact]
    public void Cpu_is_shown_as_a_percentage_with_a_matching_gauge()
    {
        var display = MetricFormatter.Format(MonitorMetric.CpuUsage, Sample, 0.5f, false, Now);

        Assert.Equal("42%", display.Value);
        Assert.Equal(0.424, display.Gauge!.Value, 3);
    }

    [Fact]
    public void An_unavailable_gpu_reads_as_not_available()
    {
        var display = MetricFormatter.Format(MonitorMetric.GpuUsage, Sample, 0.5f, false, Now);

        Assert.Equal("n/a", display.Value);
        Assert.Null(display.Gauge);
    }

    [Fact]
    public void Network_rates_switch_to_megabits_above_a_thousand()
    {
        Assert.Equal("8.4M", MetricFormatter.Format(MonitorMetric.NetworkDown, Sample, 0.5f, false, Now).Value);
        Assert.Equal("96.4k", MetricFormatter.Format(MonitorMetric.NetworkUp, Sample, 0.5f, false, Now).Value);
    }

    [Fact]
    public void Muted_output_is_labelled_rather_than_shown_as_a_percentage()
    {
        Assert.Equal("Mute", MetricFormatter.Format(MonitorMetric.MasterVolume, Sample, 0.8f, true, Now).Value);
        Assert.Equal("80%", MetricFormatter.Format(MonitorMetric.MasterVolume, Sample, 0.8f, false, Now).Value);
    }

    [Fact]
    public void Time_and_date_use_a_compact_form()
    {
        Assert.Equal("14:05", MetricFormatter.Format(MonitorMetric.Time, Sample, 0.5f, false, Now).Value);
        Assert.Equal("07 Aug", MetricFormatter.Format(MonitorMetric.Date, Sample, 0.5f, false, Now).Value);
    }

    [Fact]
    public void An_aitum_state_value_is_shortened_to_fit_the_key()
    {
        var display = MetricFormatter.Format(MonitorMetric.AitumState, Sample, 0.5f, false, Now, "\"a-very-long-value\"");
        Assert.Equal(7, display.Value.Length);
    }

    [Fact]
    public void A_missing_aitum_value_shows_a_placeholder() =>
        Assert.Equal("-", MetricFormatter.Format(MonitorMetric.AitumState, Sample, 0.5f, false, Now, null).Value);
}

public class ObsTests
{
    [Fact]
    public void Authentication_matches_the_obs_websocket_specification()
    {
        // Salt and challenge from the obs-websocket protocol document, with the documented
        // password. The expected value was computed independently with Python's hashlib
        // following the spec: base64(sha256(base64(sha256(password + salt)) + challenge)).
        var auth = ObsWebSocketClient.BuildAuthentication(
            "supersecretpassword",
            "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=",
            "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=");

        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=", auth);
    }

    [Fact]
    public void An_empty_password_still_produces_a_deterministic_string()
    {
        var first = ObsWebSocketClient.BuildAuthentication(string.Empty, "salt", "challenge");
        var second = ObsWebSocketClient.BuildAuthentication(string.Empty, "salt", "challenge");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void A_disconnected_client_reports_nothing_as_active()
    {
        var client = new ObsWebSocketClient();
        var settings = new ActionSettings { ObsCommand = ObsCommand.ToggleStream };

        Assert.False(ObsCommandRunner.IsActive(client, settings));
        Assert.Equal(ObsConnectionState.Disconnected, client.ConnectionState);
    }
}

public class ActionCatalogTests
{
    [Fact]
    public void Every_action_kind_appears_in_the_catalog()
    {
        foreach (var kind in Enum.GetValues<ActionKind>())
        {
            Assert.Contains(ActionCatalog.All, a => a.Kind == kind);
        }
    }

    [Fact]
    public void Every_catalog_entry_names_an_icon_that_exists()
    {
        foreach (var descriptor in ActionCatalog.All)
        {
            Assert.True(BuiltInIcons.Contains(descriptor.DefaultIcon),
                $"{descriptor.Name} refers to missing icon '{descriptor.DefaultIcon}'");
        }
    }

    [Fact]
    public void Summarize_describes_a_hotkey_in_a_readable_way()
    {
        var summary = ActionCatalog.Summarize(new KeyAction
        {
            Kind = ActionKind.Hotkey,
            Settings = { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey = 'M' },
        });

        Assert.Equal("Ctrl + Shift + M", summary);
    }

    [Fact]
    public void Summarize_resolves_a_folder_to_its_page_name()
    {
        var summary = ActionCatalog.Summarize(
            new KeyAction { Kind = ActionKind.Folder, Settings = { TargetPageId = "abc" } },
            pageName: id => id == "abc" ? "Streaming" : null);

        Assert.Equal("Streaming", summary);
    }

    [Fact]
    public void Summarize_flags_an_unconfigured_action() =>
        Assert.Equal("Not configured", ActionCatalog.Summarize(new KeyAction { Kind = ActionKind.OpenWebsite }));
}

public class KeyCodesTests
{
    [Fact]
    public void Every_multimedia_command_maps_to_a_virtual_key()
    {
        foreach (var command in Enum.GetValues<MultimediaCommand>())
        {
            Assert.NotEqual(0, KeyCodes.ForMultimedia(command));
        }
    }

    [Fact]
    public void Letters_and_digits_use_their_ascii_codes()
    {
        Assert.Equal('A', KeyCodes.FromName("A"));
        Assert.Equal('7', KeyCodes.FromName("7"));
    }

    [Fact]
    public void The_key_table_has_no_duplicate_names()
    {
        var duplicates = KeyCodes.All
            .GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Arrow_and_navigation_keys_are_marked_extended()
    {
        Assert.True(KeyCodes.IsExtended(0x25));
        Assert.True(KeyCodes.IsExtended(0x2E));
        Assert.False(KeyCodes.IsExtended('A'));
    }

    [Fact]
    public void Describe_handles_modifier_only_and_empty_bindings()
    {
        Assert.Equal("(none)", KeyCodes.Describe(HotkeyModifiers.None, 0));
        Assert.Equal("Ctrl", KeyCodes.Describe(HotkeyModifiers.Control, 0));
    }
}
