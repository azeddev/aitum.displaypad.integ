using System.Collections.Concurrent;
using OpenBaseCamp.Core.Integrations.Obs;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;

namespace OpenBaseCamp.Core.Actions;

/// <summary>Identifies the physical key a running macro belongs to.</summary>
public readonly record struct KeyIdentity(string ProfileId, string PageId, int Index);

/// <summary>
/// Runs key actions. Press and release are separate so macros can support
/// toggle and hold-to-repeat play modes.
/// </summary>
public sealed class ActionExecutor
{
    private readonly ActionServices _services;
    private readonly ConcurrentDictionary<KeyIdentity, CancellationTokenSource> _running = new();

    public ActionExecutor(ActionServices services) => _services = services;

    /// <summary>Raised when a long-running action (a toggled macro) starts or stops.</summary>
    public event Action<KeyIdentity, bool>? RunningChanged;

    public bool IsRunning(KeyIdentity key) => _running.ContainsKey(key);

    public async Task OnKeyDownAsync(KeyIdentity key, KeyAction action)
    {
        try
        {
            if (action.Kind == ActionKind.Macro)
            {
                await HandleMacroDownAsync(key, action.Settings).ConfigureAwait(false);
                return;
            }

            await ExecuteAsync(action, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _services.ReportError?.Invoke($"{ActionCatalog.Describe(action.Kind).Name}: {ex.Message}");
        }
    }

    public Task OnKeyUpAsync(KeyIdentity key, KeyAction action)
    {
        if (action.Kind == ActionKind.Macro && action.Settings.MacroPlayMode == MacroPlayMode.HoldToRepeat)
        {
            Stop(key);
        }

        return Task.CompletedTask;
    }

    public void Stop(KeyIdentity key)
    {
        if (_running.TryRemove(key, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            RunningChanged?.Invoke(key, false);
        }
    }

    public void StopAll()
    {
        foreach (var key in _running.Keys.ToList())
        {
            Stop(key);
        }
    }

    private async Task HandleMacroDownAsync(KeyIdentity key, ActionSettings settings)
    {
        var mode = settings.MacroPlayMode;

        if (mode is MacroPlayMode.Toggle or MacroPlayMode.HoldToRepeat)
        {
            if (mode == MacroPlayMode.Toggle && _running.ContainsKey(key))
            {
                Stop(key);
                return;
            }

            var cts = new CancellationTokenSource();
            if (!_running.TryAdd(key, cts))
            {
                cts.Dispose();
                return;
            }

            RunningChanged?.Invoke(key, true);

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        await PlayMacroAsync(settings, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _services.ReportError?.Invoke($"Macro: {ex.Message}");
                }
                finally
                {
                    Stop(key);
                }
            }, CancellationToken.None);

            return;
        }

        var repeats = mode == MacroPlayMode.RepeatCount ? Math.Max(1, settings.MacroRepeatCount) : 1;
        for (var i = 0; i < repeats; i++)
        {
            await PlayMacroAsync(settings, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Executes a single action to completion.</summary>
    public async Task ExecuteAsync(KeyAction action, CancellationToken cancellationToken)
    {
        var s = action.Settings;

        switch (action.Kind)
        {
            case ActionKind.None:
                return;

            case ActionKind.Hotkey:
                if (s.VirtualKey != 0 || s.Modifiers != HotkeyModifiers.None)
                {
                    _services.Input.SendHotkey(s.Modifiers, s.VirtualKey);
                }

                return;

            case ActionKind.Text:
                if (!string.IsNullOrEmpty(s.Text))
                {
                    _services.Input.TypeText(s.Text!);
                    if (s.PressEnterAfterText)
                    {
                        _services.Input.TapKey(0x0D);
                    }
                }

                return;

            case ActionKind.Macro:
                await PlayMacroAsync(s, cancellationToken).ConfigureAwait(false);
                return;

            case ActionKind.Mouse:
                RunMouse(s.MouseCommand);
                return;

            case ActionKind.Multimedia:
            {
                var vk = KeyCodes.ForMultimedia(s.MultimediaCommand);
                if (vk != 0)
                {
                    _services.Input.TapKey(vk);
                }

                return;
            }

            case ActionKind.Volume:
                RunVolume(s);
                return;

            case ActionKind.LaunchProgram:
                if (!string.IsNullOrWhiteSpace(s.Path))
                {
                    _services.Launcher.Launch(s.Path!, s.Arguments, s.WorkingDirectory, s.RunAsAdministrator);
                }

                return;

            case ActionKind.OpenFile:
            case ActionKind.OpenFolder:
                if (!string.IsNullOrWhiteSpace(s.Path))
                {
                    _services.Launcher.OpenPath(s.Path!);
                }

                return;

            case ActionKind.OpenWebsite:
                if (!string.IsNullOrWhiteSpace(s.Url))
                {
                    _services.Launcher.OpenUrl(NormalizeUrl(s.Url!));
                }

                return;

            case ActionKind.SystemCommand:
                _services.System.Execute(s.SystemCommand);
                return;

            case ActionKind.Folder:
                if (!string.IsNullOrWhiteSpace(s.TargetPageId))
                {
                    await _services.Navigation.OpenPageAsync(s.TargetPageId!).ConfigureAwait(false);
                }

                return;

            case ActionKind.NavigateBack:
                await _services.Navigation.GoBackAsync().ConfigureAwait(false);
                return;

            case ActionKind.NavigateHome:
                await _services.Navigation.GoHomeAsync().ConfigureAwait(false);
                return;

            case ActionKind.SwitchProfile:
                if (!string.IsNullOrWhiteSpace(s.TargetProfileId))
                {
                    await _services.Navigation.SwitchProfileAsync(s.TargetProfileId!).ConfigureAwait(false);
                }

                return;

            case ActionKind.NextProfile:
                await _services.Navigation.NextProfileAsync().ConfigureAwait(false);
                return;

            case ActionKind.PreviousProfile:
                await _services.Navigation.PreviousProfileAsync().ConfigureAwait(false);
                return;

            case ActionKind.Brightness:
            {
                var current = _services.Device.Brightness;
                var target = s.BrightnessCommand switch
                {
                    BrightnessCommand.Increase => BrightnessLevels.Step(current, +1),
                    BrightnessCommand.Decrease => BrightnessLevels.Step(current, -1),
                    BrightnessCommand.Cycle => BrightnessLevels.Cycle(current),
                    _ => BrightnessLevels.Snap(s.BrightnessLevel),
                };

                await _services.Device.SetBrightnessAsync(target).ConfigureAwait(false);
                return;
            }

            case ActionKind.DisplaySleep:
                await _services.Device.SetSleepTimerAsync(true, 0, 0, 1).ConfigureAwait(false);
                return;

            case ActionKind.Monitor:
            case ActionKind.AitumState:
                // Display-only keys.
                return;

            case ActionKind.MediaSession:
            {
                if (s.MediaSessionCommand == MediaSessionCommand.ShowNowPlaying)
                {
                    return;
                }

                if (!await _services.MediaSession.ExecuteAsync(s.MediaSessionCommand, cancellationToken).ConfigureAwait(false))
                {
                    _services.ReportError?.Invoke("No app is currently registered with Windows media controls.");
                }

                return;
            }

            case ActionKind.Spotify:
            {
                if (s.SpotifyCommand == SpotifyCommand.ShowNowPlaying)
                {
                    await _services.Spotify.RefreshNowPlayingAsync(force: true, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!await _services.Spotify.ExecuteAsync(s, cancellationToken).ConfigureAwait(false))
                {
                    _services.ReportError?.Invoke($"Spotify: {_services.Spotify.LastError ?? "the request failed."}");
                }

                return;
            }

            case ActionKind.Twitch:
            {
                if (s.TwitchCommand is TwitchCommand.ShowViewerCount or TwitchCommand.ShowLiveStatus)
                {
                    await _services.Twitch.RefreshStateAsync(force: true, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!await _services.Twitch.ExecuteAsync(s, cancellationToken).ConfigureAwait(false))
                {
                    _services.ReportError?.Invoke($"Twitch: {_services.Twitch.LastError ?? "the request failed."}");
                }

                return;
            }

            case ActionKind.Delay:
                await Task.Delay(Math.Max(0, s.DelayMs), cancellationToken).ConfigureAwait(false);
                return;

            case ActionKind.MultiAction:
                foreach (var step in s.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteAsync(step, cancellationToken).ConfigureAwait(false);
                }

                return;

            case ActionKind.Obs:
            {
                var ok = await ObsCommandRunner.ExecuteAsync(_services.Obs, s, cancellationToken).ConfigureAwait(false);
                if (!ok)
                {
                    _services.ReportError?.Invoke(_services.Obs.IsConnected
                        ? $"OBS refused {s.ObsCommand}: {_services.Obs.LastError}"
                        : "OBS Studio is not connected.");
                }

                return;
            }

            case ActionKind.AitumRule:
            {
                if (string.IsNullOrWhiteSpace(s.AitumRuleId))
                {
                    return;
                }

                var ok = await _services.Aitum.TriggerRuleAsync(s.AitumRuleId!, cancellationToken).ConfigureAwait(false);
                if (!ok)
                {
                    _services.ReportError?.Invoke($"Aitum: {_services.Aitum.LastError ?? "the rule could not be triggered."}");
                }

                return;
            }
        }
    }

    private void RunMouse(MouseCommand command)
    {
        switch (command)
        {
            case MouseCommand.LeftClick: _services.Input.MouseClick(1); break;
            case MouseCommand.RightClick: _services.Input.MouseClick(2); break;
            case MouseCommand.MiddleClick: _services.Input.MouseClick(3); break;
            case MouseCommand.Button4: _services.Input.MouseClick(4); break;
            case MouseCommand.Button5: _services.Input.MouseClick(5); break;
            case MouseCommand.DoubleClick:
                _services.Input.MouseClick(1);
                _services.Input.MouseClick(1);
                break;
            case MouseCommand.ScrollUp: _services.Input.MouseWheel(120); break;
            case MouseCommand.ScrollDown: _services.Input.MouseWheel(-120); break;
        }
    }

    private void RunVolume(ActionSettings s)
    {
        var step = Math.Abs(s.VolumeStep);
        if (step == 0)
        {
            step = 2;
        }

        switch (s.VolumeCommand)
        {
            case VolumeCommand.Up:
                _services.Audio.StepVolume(step);
                break;
            case VolumeCommand.Down:
                _services.Audio.StepVolume(-step);
                break;
            case VolumeCommand.ToggleMute:
                _services.Audio.ToggleMute();
                break;
            case VolumeCommand.SetLevel:
                _services.Audio.SetVolume(Math.Clamp(s.VolumeLevel, 0, 100) / 100f);
                break;
        }
    }

    private async Task PlayMacroAsync(ActionSettings settings, CancellationToken cancellationToken)
    {
        var pressed = new List<int>();

        try
        {
            foreach (var evt in settings.MacroEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (evt.Type)
                {
                    case MacroEventType.KeyDown:
                        _services.Input.KeyDown(evt.Code);
                        pressed.Add(evt.Code);
                        break;
                    case MacroEventType.KeyUp:
                        _services.Input.KeyUp(evt.Code);
                        pressed.Remove(evt.Code);
                        break;
                    case MacroEventType.MouseDown:
                        _services.Input.MouseButton(evt.Code, down: true);
                        break;
                    case MacroEventType.MouseUp:
                        _services.Input.MouseButton(evt.Code, down: false);
                        break;
                    case MacroEventType.Wheel:
                        _services.Input.MouseWheel(evt.Delta);
                        break;
                    case MacroEventType.MouseMove:
                        _services.Input.MouseMove(evt.X, evt.Y);
                        break;
                    case MacroEventType.Text:
                        if (!string.IsNullOrEmpty(evt.Text))
                        {
                            _services.Input.TypeText(evt.Text!);
                        }

                        break;
                    case MacroEventType.Delay:
                        break;
                }

                if (evt.DelayMs > 0)
                {
                    await Task.Delay(evt.DelayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // Never leave a modifier stuck down if playback is cancelled mid-macro.
            for (var i = pressed.Count - 1; i >= 0; i--)
            {
                try
                {
                    _services.Input.KeyUp(pressed[i]);
                }
                catch (Exception)
                {
                    // Ignore - we are already unwinding.
                }
            }
        }
    }

    private static string NormalizeUrl(string url) =>
        url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
}
