using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Services;

public sealed record MediaSessionInfo(
    string? Title,
    string? Artist,
    bool IsPlaying,
    string? SourceApp,
    byte[]? Thumbnail)
{
    public static MediaSessionInfo Empty => new(null, null, false, null, null);

    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);

    public string Display => HasTrack
        ? string.IsNullOrWhiteSpace(Artist) ? Title! : $"{Artist} — {Title}"
        : "Nothing playing";
}

/// <summary>
/// The Windows "now playing" session (SMTC): whatever app currently owns the transport
/// controls - Spotify, a browser tab, VLC. Needs no accounts or setup.
/// </summary>
public interface IMediaSessionController
{
    MediaSessionInfo Current { get; }

    /// <summary>Raised when the track, playback state or artwork changes.</summary>
    event Action? Changed;

    Task StartAsync();

    Task<bool> ExecuteAsync(MediaSessionCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Used on builds without the Windows media APIs.</summary>
public sealed class NullMediaSessionController : IMediaSessionController
{
    public MediaSessionInfo Current => MediaSessionInfo.Empty;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task<bool> ExecuteAsync(MediaSessionCommand command, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
