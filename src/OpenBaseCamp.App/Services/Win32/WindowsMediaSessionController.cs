using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace OpenBaseCamp.App.Services.Win32;

/// <summary>
/// Reads and drives the Windows "now playing" session (SMTC). Whatever app currently owns
/// the transport controls is targeted - Spotify, a browser tab, VLC - with no accounts,
/// tokens or configuration. Album art comes back as a thumbnail stream.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsMediaSessionController : IMediaSessionController, IDisposable
{
    private readonly object _lock = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaSessionInfo _current = MediaSessionInfo.Empty;
    private string? _thumbnailKey;
    private bool _disposed;

    public MediaSessionInfo Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            AttachSession(_manager.GetCurrentSession());
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No media session support (server SKU, locked-down policy): stay empty.
        }
    }

    public async Task<bool> ExecuteAsync(MediaSessionCommand command, CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        try
        {
            var ok = command switch
            {
                MediaSessionCommand.PlayPause => await session.TryTogglePlayPauseAsync(),
                MediaSessionCommand.Play => await session.TryPlayAsync(),
                MediaSessionCommand.Pause => await session.TryPauseAsync(),
                MediaSessionCommand.Next => await session.TrySkipNextAsync(),
                MediaSessionCommand.Previous => await session.TrySkipPreviousAsync(),
                MediaSessionCommand.Stop => await session.TryStopAsync(),
                _ => true,
            };

            await RefreshAsync().ConfigureAwait(false);
            return ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
        _ = RefreshAsync();
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        var previous = _session;
        if (previous is not null)
        {
            previous.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            previous.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;

        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = RefreshAsync();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        var session = _session;
        MediaSessionInfo updated;

        if (session is null)
        {
            updated = MediaSessionInfo.Empty;
        }
        else
        {
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();

                var title = properties?.Title;
                var artist = properties?.Artist;
                var key = $"{title}|{artist}";

                // Decoding the thumbnail is the expensive part: only redo it per track.
                byte[]? thumbnail;
                lock (_lock)
                {
                    thumbnail = string.Equals(key, _thumbnailKey, StringComparison.Ordinal) ? _current.Thumbnail : null;
                }

                if (thumbnail is null && properties?.Thumbnail is { } reference)
                {
                    thumbnail = await ReadThumbnailAsync(reference).ConfigureAwait(false);
                    lock (_lock)
                    {
                        _thumbnailKey = key;
                    }
                }

                updated = new MediaSessionInfo(
                    string.IsNullOrWhiteSpace(title) ? null : title,
                    string.IsNullOrWhiteSpace(artist) ? null : artist,
                    playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    session.SourceAppUserModelId,
                    thumbnail);
            }
            catch (Exception)
            {
                updated = MediaSessionInfo.Empty;
            }
        }

        bool changed;
        lock (_lock)
        {
            changed = _current.Title != updated.Title
                      || _current.Artist != updated.Artist
                      || _current.IsPlaying != updated.IsPlaying
                      || !ReferenceEquals(_current.Thumbnail, updated.Thumbnail);
            _current = updated;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024)
            {
                return null;
            }

            var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);
            await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
            return buffer.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AttachSession(null);

        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
    }
}
