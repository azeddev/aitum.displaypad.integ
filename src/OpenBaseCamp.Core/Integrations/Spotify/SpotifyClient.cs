using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using OpenBaseCamp.Core.Integrations.Auth;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Integrations.Spotify;

public sealed record SpotifyNowPlaying(
    string? Track,
    string? Artist,
    string? Album,
    bool IsPlaying,
    bool ShuffleOn,
    string RepeatState,
    int VolumePercent,
    string? ArtworkUrl,
    string? TrackId)
{
    public static SpotifyNowPlaying Empty => new(null, null, null, false, false, "off", -1, null, null);

    public string Display => string.IsNullOrEmpty(Track)
        ? "Nothing playing"
        : string.IsNullOrEmpty(Artist) ? Track! : $"{Artist} — {Track}";
}

/// <summary>
/// Spotify Web API client.
///
/// Authorisation uses the PKCE flow, so only a client id is needed - no secret is stored.
/// The user registers their own app at developer.spotify.com and adds the redirect URI.
/// Playback control requires a Spotify Premium account; the "now playing" readout does not.
/// </summary>
public sealed class SpotifyClient : IDisposable
{
    private const string Authorize = "https://accounts.spotify.com/authorize";
    private const string Token = "https://accounts.spotify.com/api/token";
    private const string Api = "https://api.spotify.com/v1";

    private static readonly string[] RequiredScopes =
    {
        "user-read-playback-state",
        "user-modify-playback-state",
        "user-read-currently-playing",
        "user-library-read",
        "user-library-modify",
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _stateLock = new();

    private SpotifySettings _settings = new();
    private OAuthTokens? _tokens;
    private SpotifyNowPlaying _nowPlaying = SpotifyNowPlaying.Empty;
    private DateTimeOffset _nowPlayingFetched = DateTimeOffset.MinValue;

    private string? _artworkUrl;
    private byte[]? _artwork;

    public bool Enabled => _settings.Enabled;

    public bool IsAuthorized => _tokens is { HasAccessToken: true };

    public string? LastError { get; private set; }

    public IReadOnlyList<string> Scopes => RequiredScopes;

    /// <summary>Raised when the cached now-playing state changes.</summary>
    public event Action? StateChanged;

    public SpotifyNowPlaying NowPlaying
    {
        get
        {
            lock (_stateLock)
            {
                return _nowPlaying;
            }
        }
    }

    /// <summary>Album artwork for the current track, already downloaded. Null when unavailable.</summary>
    public byte[]? Artwork
    {
        get
        {
            lock (_stateLock)
            {
                return _artwork;
            }
        }
    }

    public void ApplySettings(SpotifySettings settings)
    {
        _settings = settings;
        _tokens = settings.Tokens;
    }

    /// <summary>Runs the browser authorisation and stores the resulting tokens in settings.</summary>
    public async Task<string?> AuthorizeAsync(Action<string> openBrowser, CancellationToken cancellationToken = default)
    {
        var result = await OAuthBrowserFlow.AuthorizeAsync(new OAuthRequest
        {
            AuthorizeEndpoint = Authorize,
            TokenEndpoint = Token,
            ClientId = _settings.ClientId,
            RedirectUri = _settings.RedirectUri,
            Scopes = RequiredScopes,
            UsePkce = true,
        }, openBrowser, _http, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            LastError = result.Error;
            return result.Error;
        }

        _tokens = result.Tokens;
        _settings.Tokens = result.Tokens;
        LastError = null;
        return null;
    }

    public void SignOut()
    {
        _tokens = null;
        _settings.Tokens = null;
        lock (_stateLock)
        {
            _nowPlaying = SpotifyNowPlaying.Empty;
            _artwork = null;
            _artworkUrl = null;
        }
    }

    // ---- Commands -----------------------------------------------------------

    public async Task<bool> ExecuteAsync(ActionSettings settings, CancellationToken cancellationToken = default)
    {
        var state = NowPlaying;

        switch (settings.SpotifyCommand)
        {
            case SpotifyCommand.PlayPause:
                return state.IsPlaying
                    ? await SendAsync(HttpMethod.Put, "/me/player/pause", cancellationToken: cancellationToken).ConfigureAwait(false)
                    : await SendAsync(HttpMethod.Put, "/me/player/play", cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.Play:
                return await SendAsync(HttpMethod.Put, "/me/player/play", cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.Pause:
                return await SendAsync(HttpMethod.Put, "/me/player/pause", cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.Next:
                return await SendAsync(HttpMethod.Post, "/me/player/next", cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.Previous:
                return await SendAsync(HttpMethod.Post, "/me/player/previous", cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.ToggleShuffle:
                return await SendAsync(HttpMethod.Put,
                    $"/me/player/shuffle?state={(!state.ShuffleOn).ToString().ToLowerInvariant()}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.CycleRepeat:
            {
                var next = state.RepeatState switch
                {
                    "off" => "context",
                    "context" => "track",
                    _ => "off",
                };

                return await SendAsync(HttpMethod.Put, $"/me/player/repeat?state={next}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            case SpotifyCommand.VolumeUp:
            case SpotifyCommand.VolumeDown:
            {
                if (state.VolumePercent < 0)
                {
                    LastError = "Spotify did not report a volume for the active device.";
                    return false;
                }

                var step = Math.Max(1, settings.VolumeStep);
                var target = Math.Clamp(
                    state.VolumePercent + (settings.SpotifyCommand == SpotifyCommand.VolumeUp ? step : -step), 0, 100);
                return await SendAsync(HttpMethod.Put, $"/me/player/volume?volume_percent={target}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            case SpotifyCommand.SetVolume:
                return await SendAsync(HttpMethod.Put,
                    $"/me/player/volume?volume_percent={Math.Clamp(settings.VolumeLevel, 0, 100)}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            case SpotifyCommand.SaveTrack:
            {
                if (state.TrackId is null)
                {
                    LastError = "There is no current track to save.";
                    return false;
                }

                return await SendAsync(HttpMethod.Put, $"/me/tracks?ids={state.TrackId}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            case SpotifyCommand.PlayContext:
            {
                if (string.IsNullOrWhiteSpace(settings.SpotifyUri))
                {
                    LastError = "No playlist, album or track URI was set.";
                    return false;
                }

                // Tracks go in "uris", everything else is a context.
                var uri = settings.SpotifyUri!.Trim();
                var body = uri.Contains(":track:", StringComparison.Ordinal)
                    ? new JsonObject { ["uris"] = new JsonArray(uri) }
                    : new JsonObject { ["context_uri"] = uri };

                return await SendAsync(HttpMethod.Put, "/me/player/play", body, cancellationToken).ConfigureAwait(false);
            }

            case SpotifyCommand.ShowNowPlaying:
                await RefreshNowPlayingAsync(force: true, cancellationToken).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Refreshes the cached now-playing state, at most once every two seconds.</summary>
    public async Task RefreshNowPlayingAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!Enabled || !IsAuthorized)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _nowPlayingFetched < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _nowPlayingFetched = DateTimeOffset.UtcNow;

        var json = await GetAsync("/me/player", cancellationToken).ConfigureAwait(false);
        SpotifyNowPlaying updated;

        if (json is null)
        {
            updated = SpotifyNowPlaying.Empty;
        }
        else
        {
            var item = json["item"] as JsonObject;
            var artists = item?["artists"] as JsonArray;
            var artwork = (item?["album"]?["images"] as JsonArray)?
                .OfType<JsonObject>()
                .Select(image => image["url"]?.ToString())
                .LastOrDefault(url => url is not null);

            updated = new SpotifyNowPlaying(
                item?["name"]?.ToString(),
                artists is null ? null : string.Join(", ", artists.OfType<JsonObject>().Select(a => a["name"]?.ToString()).Where(n => n is not null)),
                item?["album"]?["name"]?.ToString(),
                json["is_playing"]?.GetValue<bool>() ?? false,
                json["shuffle_state"]?.GetValue<bool>() ?? false,
                json["repeat_state"]?.ToString() ?? "off",
                TryInt(json["device"]?["volume_percent"]) ?? -1,
                artwork,
                item?["id"]?.ToString());
        }

        bool changed;
        lock (_stateLock)
        {
            changed = !Equals(_nowPlaying, updated);
            _nowPlaying = updated;
        }

        await EnsureArtworkAsync(updated.ArtworkUrl, cancellationToken).ConfigureAwait(false);

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    private async Task EnsureArtworkAsync(string? url, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (url is null)
            {
                _artwork = null;
                _artworkUrl = null;
                return;
            }

            if (string.Equals(url, _artworkUrl, StringComparison.Ordinal))
            {
                return;
            }
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _artwork = bytes;
                _artworkUrl = url;
            }

            StateChanged?.Invoke();
        }
        catch (Exception)
        {
            lock (_stateLock)
            {
                _artwork = null;
                _artworkUrl = null;
            }
        }
    }

    /// <summary>Whether a key bound to this command should be drawn in its "on" state.</summary>
    public bool IsActive(ActionSettings settings)
    {
        if (!IsAuthorized)
        {
            return false;
        }

        var state = NowPlaying;
        return settings.SpotifyCommand switch
        {
            SpotifyCommand.PlayPause or SpotifyCommand.Play => state.IsPlaying,
            SpotifyCommand.Pause => !state.IsPlaying,
            SpotifyCommand.ToggleShuffle => state.ShuffleOn,
            SpotifyCommand.CycleRepeat => state.RepeatState is "context" or "track",
            _ => false,
        };
    }

    // ---- HTTP ---------------------------------------------------------------

    private async Task<JsonObject?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        using (response)
        {
            // 204 means nothing is playing at all.
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LastError = await DescribeAsync(response).ConfigureAwait(false);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(body) as JsonObject;
        }
    }

    private async Task<bool> SendAsync(HttpMethod method, string path, JsonObject? body = null, CancellationToken cancellationToken = default)
    {
        var response = await SendCoreAsync(method, path, body, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return false;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                LastError = null;

                // Playback changes are asynchronous on Spotify's side; give it a moment
                // before the next poll so the key does not flash the old state.
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                await RefreshNowPlayingAsync(force: true, cancellationToken).ConfigureAwait(false);
                return true;
            }

            LastError = await DescribeAsync(response).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<HttpResponseMessage?> SendCoreAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            LastError = "The Spotify integration is disabled.";
            return null;
        }

        if (!await EnsureFreshTokenAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(method, Api + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);

            // Spotify rejects PUT/POST without a body on some endpoints, so always send one.
            request.Content = new StringContent(body?.ToJsonString() ?? "{}", Encoding.UTF8, "application/json");

            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    private async Task<bool> EnsureFreshTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokens is not { HasAccessToken: true })
        {
            LastError = "Spotify is not connected. Authorise it in Settings.";
            return false;
        }

        if (!_tokens.IsExpired)
        {
            return true;
        }

        if (string.IsNullOrEmpty(_tokens.RefreshToken))
        {
            LastError = "The Spotify session expired. Authorise it again in Settings.";
            return false;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_tokens.IsExpired)
            {
                return true;
            }

            var result = await OAuthBrowserFlow.RefreshAsync(
                _http, Token, _settings.ClientId, null, _tokens.RefreshToken!, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                LastError = result.Error;
                return false;
            }

            _tokens = result.Tokens;
            _settings.Tokens = result.Tokens;
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return "No active Spotify device. Start playback on a device first.";
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return "Spotify refused the request. Playback control requires Premium.";
        }

        try
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var message = (JsonNode.Parse(body) as JsonObject)?["error"]?["message"]?.ToString();
            return message ?? $"Spotify returned {(int)response.StatusCode}.";
        }
        catch (Exception)
        {
            return $"Spotify returned {(int)response.StatusCode}.";
        }
    }

    private static int? TryInt(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return int.TryParse(node.ToString(), out var value) ? value : null;
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
