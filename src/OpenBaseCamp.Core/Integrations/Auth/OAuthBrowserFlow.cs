using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace OpenBaseCamp.Core.Integrations.Auth;

/// <summary>Everything needed to run one interactive authorisation.</summary>
public sealed class OAuthRequest
{
    public required string AuthorizeEndpoint { get; init; }

    public required string TokenEndpoint { get; init; }

    public required string ClientId { get; init; }

    /// <summary>Only used by services that do not support PKCE, such as Twitch.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>Must match a redirect URI registered with the service, exactly.</summary>
    public required string RedirectUri { get; init; }

    public required IReadOnlyList<string> Scopes { get; init; }

    public bool UsePkce { get; init; } = true;

    /// <summary>Extra query parameters for the authorize URL (e.g. force_verify).</summary>
    public IReadOnlyDictionary<string, string>? ExtraAuthorizeParameters { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);
}

/// <summary>
/// The authorisation-code half of OAuth 2.0, driven through the user's browser with a
/// loopback listener for the redirect. PKCE is used where the service supports it so no
/// client secret has to be stored; Twitch needs the classic secret instead.
/// </summary>
public static class OAuthBrowserFlow
{
    private const string SuccessPage =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>OpenBaseCamp</title></head>" +
        "<body style=\"font-family:system-ui;background:#14141a;color:#eee;display:flex;" +
        "align-items:center;justify-content:center;height:100vh;margin:0\">" +
        "<div style=\"text-align:center\"><h2>Connected</h2>" +
        "<p>You can close this tab and go back to OpenBaseCamp.</p></div></body></html>";

    private const string FailurePage =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>OpenBaseCamp</title></head>" +
        "<body style=\"font-family:system-ui;background:#14141a;color:#eee;display:flex;" +
        "align-items:center;justify-content:center;height:100vh;margin:0\">" +
        "<div style=\"text-align:center\"><h2>Authorisation failed</h2>" +
        "<p>Go back to OpenBaseCamp for the details.</p></div></body></html>";

    /// <param name="openBrowser">Invoked with the authorize URL; normally opens the default browser.</param>
    public static async Task<OAuthResult> AuthorizeAsync(
        OAuthRequest request,
        Action<string> openBrowser,
        HttpClient http,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return OAuthResult.Fail("No client id has been configured.");
        }

        if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var redirect))
        {
            return OAuthResult.Fail($"'{request.RedirectUri}' is not a valid redirect URI.");
        }

        var state = RandomToken(24);
        var verifier = RandomToken(64);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = request.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = request.RedirectUri,
            ["scope"] = string.Join(' ', request.Scopes),
            ["state"] = state,
        };

        if (request.UsePkce)
        {
            query["code_challenge_method"] = "S256";
            query["code_challenge"] = Challenge(verifier);
        }

        if (request.ExtraAuthorizeParameters is { } extra)
        {
            foreach (var (key, value) in extra)
            {
                query[key] = value;
            }
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(ListenerPrefix(redirect));

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            return OAuthResult.Fail($"Could not listen on {request.RedirectUri}: {ex.Message}");
        }

        try
        {
            openBrowser($"{request.AuthorizeEndpoint}?{Encode(query)}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout);

            var code = await WaitForCodeAsync(listener, redirect.AbsolutePath, state, timeout.Token).ConfigureAwait(false);
            if (code.Error is not null)
            {
                return OAuthResult.Fail(code.Error);
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = request.ClientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code.Code!,
                ["redirect_uri"] = request.RedirectUri,
            };

            if (request.UsePkce)
            {
                form["code_verifier"] = verifier;
            }

            if (!string.IsNullOrEmpty(request.ClientSecret))
            {
                form["client_secret"] = request.ClientSecret;
            }

            return await ExchangeAsync(http, request.TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OAuthResult.Fail("Authorisation timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            return OAuthResult.Fail(ex.Message);
        }
        finally
        {
            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
                // Nothing useful to do while tearing down.
            }
        }
    }

    public static async Task<OAuthResult> RefreshAsync(
        HttpClient http,
        string tokenEndpoint,
        string clientId,
        string? clientSecret,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        if (!string.IsNullOrEmpty(clientSecret))
        {
            form["client_secret"] = clientSecret;
        }

        var result = await ExchangeAsync(http, tokenEndpoint, form, cancellationToken).ConfigureAwait(false);

        // Some services omit the refresh token on renewal; keep the one we already have.
        if (result.Tokens is { } tokens && string.IsNullOrEmpty(tokens.RefreshToken))
        {
            tokens.RefreshToken = refreshToken;
        }

        return result;
    }

    private static async Task<(string? Code, string? Error)> WaitForCodeAsync(
        HttpListener listener,
        string expectedPath,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            if (completed != contextTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var context = await contextTask.ConfigureAwait(false);

            // Browsers ask for /favicon.ico on the same origin; ignore anything but the callback.
            if (!string.Equals(context.Request.Url?.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
            var error = query["error_description"] ?? query["error"];
            var code = query["code"];
            var state = query["state"];

            var ok = error is null && code is not null && string.Equals(state, expectedState, StringComparison.Ordinal);
            await RespondAsync(context, ok ? SuccessPage : FailurePage).ConfigureAwait(false);

            if (error is not null)
            {
                return (null, error);
            }

            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                return (null, "The authorisation response did not match the request (state mismatch).");
            }

            return code is null ? (null, "No authorisation code was returned.") : (code, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (null, "Cancelled.");
    }

    private static async Task RespondAsync(HttpListenerContext context, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static async Task<OAuthResult> ExchangeAsync(
        HttpClient http,
        string tokenEndpoint,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (JsonNode.Parse(body) is not JsonObject json)
        {
            return OAuthResult.Fail($"The token endpoint returned an unexpected response ({(int)response.StatusCode}).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = json["error_description"]?.ToString() ?? json["message"]?.ToString() ?? json["error"]?.ToString();
            return OAuthResult.Fail(message ?? $"The token endpoint returned {(int)response.StatusCode}.");
        }

        var accessToken = json["access_token"]?.ToString();
        if (string.IsNullOrEmpty(accessToken))
        {
            return OAuthResult.Fail("The token endpoint did not return an access token.");
        }

        var expiresIn = 3600;
        if (json["expires_in"] is { } expiryNode && int.TryParse(expiryNode.ToString(), out var parsed))
        {
            expiresIn = parsed;
        }

        return OAuthResult.Ok(new OAuthTokens
        {
            AccessToken = accessToken,
            RefreshToken = json["refresh_token"]?.ToString(),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            Scope = json["scope"]?.ToString(),
        });
    }

    /// <summary>HttpListener needs a prefix ending in '/', and only the host and path matter.</summary>
    internal static string ListenerPrefix(Uri redirect)
    {
        var path = redirect.AbsolutePath;
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        return $"{redirect.Scheme}://{redirect.Host}:{redirect.Port}{path}";
    }

    internal static string RandomToken(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Base64Url(buffer);
    }

    /// <summary>PKCE S256: base64url(sha256(verifier)), unpadded.</summary>
    internal static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string Encode(Dictionary<string, string> query) =>
        string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
}
