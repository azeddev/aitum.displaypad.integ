namespace OpenBaseCamp.Core.Integrations.Auth;

/// <summary>Credentials returned by a token endpoint.</summary>
public sealed class OAuthTokens
{
    public string AccessToken { get; set; } = string.Empty;

    public string? RefreshToken { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string? Scope { get; set; }

    public bool HasAccessToken => !string.IsNullOrEmpty(AccessToken);

    /// <summary>True a minute before the real expiry, so a request never races the clock.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt - TimeSpan.FromMinutes(1);

    public OAuthTokens Clone() => (OAuthTokens)MemberwiseClone();
}

/// <summary>Outcome of an interactive authorisation.</summary>
public sealed record OAuthResult(OAuthTokens? Tokens, string? Error)
{
    public bool Success => Tokens is { HasAccessToken: true };

    public static OAuthResult Fail(string error) => new(null, error);

    public static OAuthResult Ok(OAuthTokens tokens) => new(tokens, null);
}
