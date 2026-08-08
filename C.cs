using Windows.Media.Control;
public static class C {
  public static async Task<string?> NowPlayingAsync() {
    var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    var s = mgr.GetCurrentSession();
    if (s is null) return null;
    var p = await s.TryGetMediaPropertiesAsync();
    await s.TryTogglePlayPauseAsync();
    var st = s.GetPlaybackInfo().PlaybackStatus;
    return $"{p.Artist} - {p.Title} [{st}] thumb={p.Thumbnail is not null}";
  }
}
