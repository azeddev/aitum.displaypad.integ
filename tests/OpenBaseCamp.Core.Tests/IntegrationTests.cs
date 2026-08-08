using System.Security.Cryptography;
using System.Text;
using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Config;
using OpenBaseCamp.Core.Integrations.Auth;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Rendering;
using Xunit;

namespace OpenBaseCamp.Core.Tests;

public class OAuthFlowTests
{
    [Fact]
    public void The_pkce_challenge_is_unpadded_base64url_of_the_sha256_verifier()
    {
        const string verifier = "a-known-verifier-value-for-the-test-1234567890";
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var challenge = OAuthBrowserFlow.Challenge(verifier);

        Assert.Equal(expected, challenge);
        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
    }

    [Fact]
    public void Random_tokens_are_url_safe_and_unique()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => OAuthBrowserFlow.RandomToken(32)).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, token => Assert.DoesNotContain(token, "+/="));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8888/callback", "http://127.0.0.1:8888/callback/")]
    [InlineData("http://localhost:3000/callback/", "http://localhost:3000/callback/")]
    [InlineData("http://127.0.0.1:5000/", "http://127.0.0.1:5000/")]
    public void The_listener_prefix_always_ends_in_a_slash(string redirect, string expected) =>
        Assert.Equal(expected, OAuthBrowserFlow.ListenerPrefix(new Uri(redirect)));

    [Fact]
    public void A_token_is_treated_as_expired_a_minute_early()
    {
        var almost = new OAuthTokens { AccessToken = "x", ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) };
        var fresh = new OAuthTokens { AccessToken = "x", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) };

        Assert.True(almost.IsExpired);
        Assert.False(fresh.IsExpired);
    }
}

public class PresetPackTests
{
    [Fact]
    public void Every_pack_fills_a_full_page()
    {
        foreach (var pack in PresetPacks.All)
        {
            Assert.Equal(DisplayPadLayout.KeyCount, pack.Keys.Count);
        }
    }

    [Fact]
    public void Every_pack_icon_exists()
    {
        foreach (var pack in PresetPacks.All)
        {
            Assert.True(BuiltInIcons.Contains(pack.Icon), $"pack '{pack.Id}' uses missing icon '{pack.Icon}'");

            foreach (var key in pack.Keys)
            {
                Assert.True(BuiltInIcons.Contains(key.Icon), $"'{pack.Id}/{key.Title}' uses missing icon '{key.Icon}'");
            }
        }
    }

    [Fact]
    public void Pack_ids_are_unique()
    {
        var ids = PresetPacks.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CreatePage_produces_a_normalised_page_with_the_configured_actions()
    {
        var pack = PresetPacks.Find("photoshop")!;
        var page = PresetPacks.CreatePage(pack, "parent");

        Assert.Equal(DisplayPadLayout.KeyCount, page.Keys.Count);
        Assert.Equal("parent", page.ParentPageId);
        Assert.Equal("Adobe Photoshop", page.Name);

        var undo = page.Keys[0];
        Assert.Equal(ActionKind.Hotkey, undo.Action.Kind);
        Assert.Equal(HotkeyModifiers.Control, undo.Action.Settings.Modifiers);
        Assert.Equal('Z', undo.Action.Settings.VirtualKey);
        Assert.Equal("Undo", undo.Appearance.Title);
    }

    [Fact]
    public void Blank_slots_in_a_pack_stay_unassigned()
    {
        var page = PresetPacks.CreatePage(PresetPacks.Find("discord")!, null);

        Assert.Equal(ActionKind.Hotkey, page.Keys[0].Action.Kind);
        Assert.Equal(ActionKind.None, page.Keys[5].Action.Kind);
        Assert.True(page.Keys[5].IsEmpty);
    }

    [Fact]
    public void Packs_that_navigate_include_a_way_back()
    {
        foreach (var pack in PresetPacks.All.Where(p => p.Keys.Any(k => k.Kind == ActionKind.NavigateBack)))
        {
            var page = PresetPacks.CreatePage(pack, "parent");
            Assert.Contains(page.Keys, k => k.Action.Kind == ActionKind.NavigateBack);
        }
    }

    [Fact]
    public void Each_preset_action_summarises_without_throwing()
    {
        foreach (var pack in PresetPacks.All)
        {
            var page = PresetPacks.CreatePage(pack, null);
            foreach (var slot in page.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(ActionCatalog.Summarize(slot.Action)));
            }
        }
    }
}

public class ArtworkRenderingTests
{
    [Fact]
    public void An_image_override_is_drawn_in_place_of_the_icon()
    {
        // A solid red square standing in for album art.
        var art = SolidPng(64, new SkiaSharp.SKColor(220, 20, 20));

        var request = new KeyRenderRequest
        {
            Appearance = new KeyAppearance
            {
                BackgroundColor = "#FF000000",
                IconId = "play",
                ShowTitle = false,
                IconFit = IconFit.Cover,
            },
            ImageOverride = art,
        };

        using var bitmap = KeyImageRenderer.Render(request);
        var centre = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

        Assert.True(centre.Red > 180, $"expected the artwork, got {centre}");
        Assert.True(centre.Green < 80);
    }

    [Fact]
    public void A_corrupt_image_override_falls_back_to_the_icon()
    {
        var request = new KeyRenderRequest
        {
            Appearance = new KeyAppearance { BackgroundColor = "#FF000000", IconId = "play", ShowTitle = false },
            ImageOverride = new byte[] { 1, 2, 3, 4, 5 },
        };

        using var bitmap = KeyImageRenderer.Render(request);

        var lit = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Red > 120)
                {
                    lit++;
                }
            }
        }

        Assert.True(lit > 15, "the play icon should still have been drawn");
    }

    private static byte[] SolidPng(int size, SkiaSharp.SKColor color)
    {
        using var bitmap = new SkiaSharp.SKBitmap(size, size);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

public class LayoutTests
{
    [Fact]
    public void The_default_grid_is_the_only_one_that_fits_a_key_in_the_panel()
    {
        // The SetPanelImage defaults describe an 800x240 panel and each key image is
        // 102x102, so 6x2 is the only twelve-key arrangement whose cells can hold one.
        var cellWidth = DisplayPadLayout.PanelWidth / (double)DisplayPadLayout.DefaultColumns;
        var cellHeight = DisplayPadLayout.PanelHeight / (double)DisplayPadLayout.DefaultRows;

        Assert.True(cellWidth >= DisplayPadLayout.KeyPixels);
        Assert.True(cellHeight >= DisplayPadLayout.KeyPixels);
        Assert.Equal(DisplayPadLayout.KeyCount, DisplayPadLayout.DefaultColumns * DisplayPadLayout.DefaultRows);
    }

    [Theory]
    [InlineData(6, 2)]
    [InlineData(4, 3)]
    [InlineData(3, 4)]
    [InlineData(2, 6)]
    [InlineData(0, 2)]
    public void RowsFor_pairs_with_the_column_count(int columns, int expectedRows) =>
        Assert.Equal(expectedRows, DisplayPadLayout.RowsFor(columns));
}
