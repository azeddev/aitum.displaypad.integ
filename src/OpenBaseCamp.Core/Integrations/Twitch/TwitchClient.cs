using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using OpenBaseCamp.Core.Integrations.Auth;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Integrations.Twitch;

public sealed record TwitchChannelState(
    bool IsLive,
    int Viewers,
    string? Title,
    string? Category,
    string? DisplayName)
{
    public static TwitchChannelState Empty => new(false, 0, null, null, null);
}

/// <summary>
/// Twitch Helix client.
///
/// Twitch does not support PKCE, so this uses the authorization-code flow with a client id
/// and secret from an application the user registers at dev.twitch.tv. That is what gives a
/// refresh token, so the connection survives past the four-hour access token lifetime.
/// </summary>
public sealed class TwitchClient : IDisposable
{
    private const string Authorize = "https://id.twitch.tv/oauth2/authorize";
    private const string Token = "https://id.twitch.tv/oauth2/token";
    private const string Api = "https://api.twitch.tv/helix";

    private static readonly string[] RequiredScopes =
    {
        "user:read:email",
        "clips:edit",
        "channel:manage:broadcast",
        "channel:edit:commercial",
        "user:write:chat",
        "user:bot",
        "channel:bot",
        "moderator:manage:chat_settings",
        "moderator:manage:shoutouts",
        "moderator:manage:announcements",
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _stateLock = new();

    private TwitchSettings _settings = new();
    private OAuthTokens? _tokens;
    private TwitchChannelState _state = TwitchChannelState.Empty;
    private DateTimeOffset _stateFetched = DateTimeOffset.MinValue;
    private string? _userId;
    private string? _login;

    public bool Enabled => _settings.Enabled;

    public bool IsAuthorized => _tokens is { HasAccessToken: true };

    public string? LastError { get; private set; }

    public IReadOnlyList<string> Scopes => RequiredScopes;

    public event Action? StateChanged;

    public TwitchChannelState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public void ApplySettings(TwitchSettings settings)
    {
        _settings = settings;
        _tokens = settings.Tokens;
        _userId = string.IsNullOrEmpty(settings.UserId) ? null : settings.UserId;
        _login = string.IsNullOrEmpty(settings.Login) ? null : settings.Login;
    }

    public async Task<string?> AuthorizeAsync(Action<string> openBrowser, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            return "Twitch needs a client secret as well as a client id.";
        }

        var result = await OAuthBrowserFlow.AuthorizeAsync(new OAuthRequest
        {
            AuthorizeEndpoint = Authorize,
            TokenEndpoint = Token,
            ClientId = _settings.ClientId,
            ClientSecret = _settings.ClientSecret,
            RedirectUri = _settings.RedirectUri,
            Scopes = RequiredScopes,
            UsePkce = false,
            ExtraAuthorizeParameters = new Dictionary<string, string> { ["force_verify"] = "true" },
        }, openBrowser, _http, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            LastError = result.Error;
            return result.Error;
        }

        _tokens = result.Tokens;
        _settings.Tokens = result.Tokens;

        var identified = await IdentifyAsync(cancellationToken).ConfigureAwait(false);
        if (identified is not null)
        {
            LastError = identified;
            return identified;
        }

        LastError = null;
        return null;
    }

    public void SignOut()
    {
        _tokens = null;
        _settings.Tokens = null;
        _settings.UserId = null;
        _settings.Login = null;
        _userId = null;
        _login = null;

        lock (_stateLock)
        {
            _state = TwitchChannelState.Empty;
        }
    }

    /// <summary>Resolves and caches the broadcaster id, which nearly every Helix call needs.</summary>
    private async Task<string?> IdentifyAsync(CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, "/users", null, cancellationToken).ConfigureAwait(false);
        var user = (json?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        if (user is null)
        {
            return LastError ?? "Twitch did not return the signed-in user.";
        }

        _userId = user["id"]?.ToString();
        _login = user["login"]?.ToString();
        _settings.UserId = _userId;
        _settings.Login = _login;
        return _userId is null ? "Twitch did not return a user id." : null;
    }

    // ---- Commands -----------------------------------------------------------

    public async Task<bool> ExecuteAsync(ActionSettings settings, CancellationToken cancellationToken = default)
    {
        if (!await EnsureIdentifiedAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var id = _userId!;

        switch (settings.TwitchCommand)
        {
            case TwitchCommand.CreateClip:
                return await PostAsync($"/clips?broadcaster_id={id}", null, cancellationToken).ConfigureAwait(false);

            case TwitchCommand.CreateMarker:
            {
                var body = new JsonObject { ["user_id"] = id };
                if (!string.IsNullOrWhiteSpace(settings.TwitchText))
                {
                    body["description"] = settings.TwitchText;
                }

                return await PostAsync("/streams/markers", body, cancellationToken).ConfigureAwait(false);
            }

            case TwitchCommand.StartCommercial:
                return await PostAsync("/channels/commercial", new JsonObject
                {
                    ["broadcaster_id"] = id,
                    ["length"] = Math.Clamp(settings.TwitchCommercialSeconds, 30, 180),
                }, cancellationToken).ConfigureAwait(false);

            case TwitchCommand.SetTitle:
                if (string.IsNullOrWhiteSpace(settings.TwitchText))
                {
                    LastError = "No title was set.";
                    return false;
                }

                return await PatchAsync($"/channels?broadcaster_id={id}",
                    new JsonObject { ["title"] = settings.TwitchText }, cancellationToken).ConfigureAwait(false);

            case TwitchCommand.SetCategory:
            {
                if (string.IsNullOrWhiteSpace(settings.TwitchText))
                {
                    LastError = "No category was set.";
                    return false;
                }

                var gameId = await ResolveGameIdAsync(settings.TwitchText!, cancellationToken).ConfigureAwait(false);
                if (gameId is null)
                {
                    LastError = $"Twitch has no category called '{settings.TwitchText}'.";
                    return false;
                }

                return await PatchAsync($"/channels?broadcaster_id={id}",
                    new JsonObject { ["game_id"] = gameId }, cancellationToken).ConfigureAwait(false);
            }

            case TwitchCommand.SendChatMessage:
                if (string.IsNullOrWhiteSpace(settings.TwitchText))
                {
                    LastError = "No message was set.";
                    return false;
                }

                return await PostAsync("/chat/messages", new JsonObject
                {
                    ["broadcaster_id"] = id,
                    ["sender_id"] = id,
                    ["message"] = settings.TwitchText,
                }, cancellationToken).ConfigureAwait(false);

            case TwitchCommand.SendAnnouncement:
                if (string.IsNullOrWhiteSpace(settings.TwitchText))
                {
                    LastError = "No announcement was set.";
                    return false;
                }

                return await PostAsync($"/chat/announcements?broadcaster_id={id}&moderator_id={id}", new JsonObject
                {
                    ["message"] = settings.TwitchText,
                }, cancellationToken).ConfigureAwait(false);

            case TwitchCommand.Shoutout:
            {
                if (string.IsNullOrWhiteSpace(settings.TwitchText))
                {
                    LastError = "No channel to shout out was set.";
                    return false;
                }

                var target = await ResolveUserIdAsync(settings.TwitchText!, cancellationToken).ConfigureAwait(false);
                if (target is null)
                {
                    LastError = $"Twitch has no user called '{settings.TwitchText}'.";
                    return false;
                }

                return await PostAsync(
                    $"/chat/shoutouts?from_broadcaster_id={id}&to_broadcaster_id={target}&moderator_id={id}",
                    null, cancellationToken).ConfigureAwait(false);
            }

            case TwitchCommand.ToggleEmoteOnly:
            case TwitchCommand.ToggleSubscriberOnly:
            case TwitchCommand.ToggleFollowerOnly:
            case TwitchCommand.ToggleSlowMode:
                return await ToggleChatSettingAsync(settings, id, cancellationToken).ConfigureAwait(false);

            case TwitchCommand.ShowViewerCount:
            case TwitchCommand.ShowLiveStatus:
                await RefreshStateAsync(force: true, cancellationToken).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    private async Task<bool> ToggleChatSettingAsync(ActionSettings settings, string id, CancellationToken cancellationToken)
    {
        var current = await SendAsync(HttpMethod.Get,
            $"/chat/settings?broadcaster_id={id}&moderator_id={id}", null, cancellationToken).ConfigureAwait(false);

        var chat = (current?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        if (chat is null)
        {
            LastError ??= "Twitch did not return the chat settings.";
            return false;
        }

        var body = new JsonObject();
        switch (settings.TwitchCommand)
        {
            case TwitchCommand.ToggleEmoteOnly:
                body["emote_mode"] = !(chat["emote_mode"]?.GetValue<bool>() ?? false);
                break;

            case TwitchCommand.ToggleSubscriberOnly:
                body["subscriber_mode"] = !(chat["subscriber_mode"]?.GetValue<bool>() ?? false);
                break;

            case TwitchCommand.ToggleFollowerOnly:
                body["follower_mode"] = !(chat["follower_mode"]?.GetValue<bool>() ?? false);
                break;

            case TwitchCommand.ToggleSlowMode:
            {
                var on = chat["slow_mode"]?.GetValue<bool>() ?? false;
                body["slow_mode"] = !on;
                if (!on)
                {
                    body["slow_mode_wait_time"] = Math.Clamp(settings.TwitchSlowModeSeconds, 3, 120);
                }

                break;
            }
        }

        return await PatchAsync($"/chat/settings?broadcaster_id={id}&moderator_id={id}", body, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RefreshStateAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!Enabled || !IsAuthorized)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _stateFetched < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _stateFetched = DateTimeOffset.UtcNow;

        if (!await EnsureIdentifiedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var streams = await SendAsync(HttpMethod.Get, $"/streams?user_id={_userId}", null, cancellationToken).ConfigureAwait(false);
        var stream = (streams?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();

        var channels = await SendAsync(HttpMethod.Get, $"/channels?broadcaster_id={_userId}", null, cancellationToken).ConfigureAwait(false);
        var channel = (channels?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();

        var updated = new TwitchChannelState(
            stream is not null,
            stream?["viewer_count"] is { } viewers && int.TryParse(viewers.ToString(), out var count) ? count : 0,
            channel?["title"]?.ToString(),
            channel?["game_name"]?.ToString(),
            channel?["broadcaster_name"]?.ToString() ?? _login);

        bool changed;
        lock (_stateLock)
        {
            changed = !Equals(_state, updated);
            _state = updated;
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    public bool IsActive(ActionSettings settings) => IsAuthorized && settings.TwitchCommand switch
    {
        TwitchCommand.ShowLiveStatus or TwitchCommand.ShowViewerCount => State.IsLive,
        _ => false,
    };

    private async Task<string?> ResolveGameIdAsync(string name, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, $"/games?name={Uri.EscapeDataString(name)}", null, cancellationToken)
            .ConfigureAwait(false);
        return (json?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["id"]?.ToString();
    }

    private async Task<string?> ResolveUserIdAsync(string login, CancellationToken cancellationToken)
    {
        var json = await SendAsync(HttpMethod.Get, $"/users?login={Uri.EscapeDataString(login.TrimStart('@'))}", null, cancellationToken)
            .ConfigureAwait(false);
        return (json?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["id"]?.ToString();
    }

    private async Task<bool> EnsureIdentifiedAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            LastError = "The Twitch integration is disabled.";
            return false;
        }

        if (!IsAuthorized)
        {
            LastError = "Twitch is not connected. Authorise it in Settings.";
            return false;
        }

        if (_userId is not null)
        {
            return true;
        }

        return await IdentifyAsync(cancellationToken).ConfigureAwait(false) is null;
    }

    // ---- HTTP ---------------------------------------------------------------

    private Task<bool> PostAsync(string path, JsonObject? body, CancellationToken cancellationToken) =>
        SucceedsAsync(HttpMethod.Post, path, body, cancellationToken);

    private Task<bool> PatchAsync(string path, JsonObject? body, CancellationToken cancellationToken) =>
        SucceedsAsync(HttpMethod.Patch, path, body, cancellationToken);

    private async Task<bool> SucceedsAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        var json = await SendAsync(method, path, body, cancellationToken).ConfigureAwait(false);

        // A 204 gives no body, which SendAsync reports as an empty object.
        return json is not null;
    }

    private async Task<JsonObject?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        if (!await EnsureFreshTokenAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(method, Api + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
            request.Headers.Add("Client-Id", _settings.ClientId);

            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = (JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) as JsonObject)?["message"]?.ToString();
                LastError = message ?? $"Twitch returned {(int)response.StatusCode}.";
                return null;
            }

            LastError = null;
            return string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text) as JsonObject ?? new JsonObject();
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
            LastError = "Twitch is not connected. Authorise it in Settings.";
            return false;
        }

        if (!_tokens.IsExpired)
        {
            return true;
        }

        if (string.IsNullOrEmpty(_tokens.RefreshToken))
        {
            LastError = "The Twitch session expired. Authorise it again in Settings.";
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
                _http, Token, _settings.ClientId, _settings.ClientSecret, _tokens.RefreshToken!, cancellationToken)
                .ConfigureAwait(false);

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

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
