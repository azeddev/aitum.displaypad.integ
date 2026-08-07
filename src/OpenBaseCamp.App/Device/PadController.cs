using System.Security.Cryptography;
using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Config;
using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Integrations.Aitum;
using OpenBaseCamp.Core.Integrations.Obs;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Monitoring;
using OpenBaseCamp.Core.Rendering;
using OpenBaseCamp.Core.Services;

namespace OpenBaseCamp.App.Device;

/// <summary>
/// Drives the pad: decides what each key looks like, pushes the images, and turns presses
/// into actions. Also implements the navigation and device services the action executor needs.
/// </summary>
public sealed class PadController : IPadNavigation, IPadDeviceControl, IDisposable
{
    private readonly IPadHardware _device;
    private readonly ConfigStore _store;
    private readonly ISystemMetricsProvider _metrics;
    private readonly IAudioController _audio;
    private readonly ObsWebSocketClient _obs;
    private readonly AitumClient _aitum;

    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly byte[]?[] _uploadedHashes = new byte[DisplayPadLayout.KeyCount][];
    private readonly List<string> _pageStack = new();
    private readonly System.Timers.Timer _liveTimer;

    private readonly Dictionary<string, string?> _aitumStateCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _aitumStateFetched = DateTime.MinValue;

    private AppConfig _config;
    private ActionExecutor? _executor;
    private SystemMetrics _lastMetrics = SystemMetrics.Empty;
    private int _brightness = 100;
    private bool _suspended;

    public PadController(
        IPadHardware device,
        ConfigStore store,
        AppConfig config,
        ISystemMetricsProvider metrics,
        IAudioController audio,
        ObsWebSocketClient obs,
        AitumClient aitum)
    {
        _device = device;
        _store = store;
        _config = config;
        _metrics = metrics;
        _audio = audio;
        _obs = obs;
        _aitum = aitum;

        _brightness = config.Settings.Device.Brightness;

        _liveTimer = new System.Timers.Timer(Math.Max(250, config.Settings.MonitorRefreshMs)) { AutoReset = true };
        _liveTimer.Elapsed += async (_, _) => await TickAsync().ConfigureAwait(false);

        // Sample once up front so monitoring keys do not show 0% for the first tick.
        _lastMetrics = _metrics.Sample();

        _device.KeyEvent += OnKeyEvent;
        _device.DeviceStatusChanged += OnDeviceStatusChanged;
        _obs.StateChanged += OnObsStateChanged;
    }

    /// <summary>Raised when the on-screen preview should be rebuilt.</summary>
    public event Action? SurfaceChanged;

    /// <summary>Raised when the active profile or page changes.</summary>
    public event Action? NavigationChanged;

    public event Action<string>? Error;

    public AppConfig Config => _config;

    public Profile ActiveProfile => _config.ActiveProfile;

    /// <summary>
    /// The page currently on the pad. Key events arrive on the SDK's thread and rendering
    /// runs on the thread pool, so the navigation stack is guarded.
    /// </summary>
    public Page CurrentPage
    {
        get
        {
            var profile = ActiveProfile;
            lock (_pageStack)
            {
                for (var i = _pageStack.Count - 1; i >= 0; i--)
                {
                    if (profile.FindPage(_pageStack[i]) is { } page)
                    {
                        return page;
                    }

                    _pageStack.RemoveAt(i);
                }
            }

            return profile.Root;
        }
    }

    public IReadOnlyList<string> PageStack
    {
        get
        {
            lock (_pageStack)
            {
                return _pageStack.ToList();
            }
        }
    }

    public bool CanGoBack
    {
        get
        {
            lock (_pageStack)
            {
                return _pageStack.Count > 0;
            }
        }
    }

    public int Brightness => _brightness;

    public void AttachExecutor(ActionExecutor executor)
    {
        _executor = executor;
        _executor.RunningChanged += (_, _) => RequestRender();
    }

    public void Start() => _liveTimer.Start();

    /// <summary>Stops pushing images, e.g. while the pad is being reconfigured.</summary>
    public void Suspend() => _suspended = true;

    public void Resume()
    {
        _suspended = false;
        InvalidateAll();
    }

    public async Task ApplyConfigAsync(AppConfig config)
    {
        _config = config;
        _brightness = config.Settings.Device.Brightness;
        _liveTimer.Interval = Math.Max(250, config.Settings.MonitorRefreshMs);

        if (_device.PrimaryDeviceId is { } id)
        {
            await _device.EnableSoftwareModeAsync(id, config.Settings.Device.FirmwareKeyMode).ConfigureAwait(false);
            await _device.SetBrightnessAsync(id, config.Settings.Device.Brightness).ConfigureAwait(false);
            var sleep = config.Settings.Device;
            await _device.SetSleepTimerAsync(id, sleep.SleepEnabled, sleep.SleepHours, sleep.SleepMinutes, sleep.SleepSeconds)
                .ConfigureAwait(false);
        }

        InvalidateAll();
        NavigationChanged?.Invoke();
    }

    /// <summary>Forces every key to be re-uploaded on the next render.</summary>
    public void InvalidateAll()
    {
        Array.Clear(_uploadedHashes);
        RequestRender();
    }

    public void RequestRender() => _ = RenderAsync();

    // ---- Navigation ---------------------------------------------------------

    public Task OpenPageAsync(string pageId)
    {
        if (ActiveProfile.FindPage(pageId) is null)
        {
            return Task.CompletedTask;
        }

        lock (_pageStack)
        {
            _pageStack.Add(pageId);
        }

        return AfterNavigationAsync();
    }

    public Task GoBackAsync()
    {
        lock (_pageStack)
        {
            if (_pageStack.Count > 0)
            {
                _pageStack.RemoveAt(_pageStack.Count - 1);
            }
        }

        return AfterNavigationAsync();
    }

    public Task GoHomeAsync()
    {
        lock (_pageStack)
        {
            _pageStack.Clear();
        }

        return AfterNavigationAsync();
    }

    public Task SwitchProfileAsync(string profileId)
    {
        if (_config.Profiles.All(p => p.Id != profileId))
        {
            return Task.CompletedTask;
        }

        _config.ActiveProfileId = profileId;
        lock (_pageStack)
        {
            _pageStack.Clear();
        }

        _store.Save(_config);
        return AfterNavigationAsync();
    }

    public Task NextProfileAsync() => ShiftProfileAsync(1);

    public Task PreviousProfileAsync() => ShiftProfileAsync(-1);

    private Task ShiftProfileAsync(int direction)
    {
        var profiles = _config.Profiles;
        if (profiles.Count <= 1)
        {
            return Task.CompletedTask;
        }

        var index = profiles.FindIndex(p => p.Id == _config.ActiveProfileId);
        if (index < 0)
        {
            index = 0;
        }

        var next = ((index + direction) % profiles.Count + profiles.Count) % profiles.Count;
        return SwitchProfileAsync(profiles[next].Id);
    }

    private Task AfterNavigationAsync()
    {
        InvalidateAll();
        NavigationChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Switches profile when the foreground application matches a profile's trigger list.</summary>
    public void OnForegroundApplicationChanged(string executable)
    {
        if (!_config.Settings.AutoSwitchProfileByApp)
        {
            return;
        }

        var match = _config.Profiles.FirstOrDefault(p =>
            p.AutoSwitchProcesses.Any(name => string.Equals(name, executable, StringComparison.OrdinalIgnoreCase)));

        if (match is not null && match.Id != _config.ActiveProfileId)
        {
            _ = SwitchProfileAsync(match.Id);
        }
    }

    // ---- Device -------------------------------------------------------------

    public async Task SetBrightnessAsync(int percent)
    {
        _brightness = BrightnessLevels.Snap(percent);
        _config.Settings.Device.Brightness = _brightness;

        if (_device.PrimaryDeviceId is { } id)
        {
            await _device.SetBrightnessAsync(id, _brightness).ConfigureAwait(false);
        }

        _store.Save(_config);
        SurfaceChanged?.Invoke();
    }

    public async Task SetSleepTimerAsync(bool enabled, int hours, int minutes, int seconds)
    {
        if (_device.PrimaryDeviceId is { } id)
        {
            await _device.SetSleepTimerAsync(id, enabled, hours, minutes, seconds).ConfigureAwait(false);
        }
    }

    // ---- Key events ---------------------------------------------------------

    private void OnKeyEvent(int matrix, bool pressed, int deviceId)
    {
        var index = KeyMatrixMap.Resolve(_config.Settings.Device, matrix, _config.Settings.Device.Columns);
        KeyMatrixObserved?.Invoke(matrix, pressed, index);

        if (index == KeyMatrixMap.Unknown)
        {
            return;
        }

        var page = CurrentPage;
        var slot = page.Keys.ElementAtOrDefault(index);
        if (slot is null)
        {
            return;
        }

        if (_executor is not { } executor)
        {
            return;
        }

        var identity = new KeyIdentity(ActiveProfile.Id, page.Id, index);
        _ = Task.Run(async () =>
        {
            if (pressed)
            {
                await executor.OnKeyDownAsync(identity, slot.Action).ConfigureAwait(false);
            }
            else
            {
                await executor.OnKeyUpAsync(identity, slot.Action).ConfigureAwait(false);
            }
        });
    }

    /// <summary>Raw feed used by the key-mapping wizard: matrix code, pressed, resolved index.</summary>
    public event Action<int, bool, int>? KeyMatrixObserved;

    /// <summary>Simulates a press from the UI preview.</summary>
    public Task PressAsync(int index)
    {
        var page = CurrentPage;
        var slot = page.Keys.ElementAtOrDefault(index);
        if (slot is null)
        {
            return Task.CompletedTask;
        }

        if (_executor is not { } executor)
        {
            return Task.CompletedTask;
        }

        var identity = new KeyIdentity(ActiveProfile.Id, page.Id, index);
        return executor.OnKeyDownAsync(identity, slot.Action);
    }

    private async void OnDeviceStatusChanged(int deviceId, bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var settings = _config.Settings.Device;
            await _device.EnableSoftwareModeAsync(deviceId, settings.FirmwareKeyMode).ConfigureAwait(false);
            await _device.SetBrightnessAsync(deviceId, settings.Brightness).ConfigureAwait(false);
            await _device.SetSleepTimerAsync(deviceId, settings.SleepEnabled, settings.SleepHours, settings.SleepMinutes, settings.SleepSeconds)
                .ConfigureAwait(false);
            InvalidateAll();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Device setup failed: {ex.Message}");
        }
    }

    private void OnObsStateChanged() => RequestRender();

    // ---- Rendering ----------------------------------------------------------

    private async Task TickAsync()
    {
        _lastMetrics = _metrics.Sample();
        await RefreshAitumStateAsync().ConfigureAwait(false);
        await RenderAsync().ConfigureAwait(false);
    }

    private async Task RefreshAitumStateAsync()
    {
        if (!_aitum.Enabled || DateTime.UtcNow - _aitumStateFetched < TimeSpan.FromSeconds(2))
        {
            return;
        }

        var page = CurrentPage;
        var needed = page.Keys.Any(k =>
            k.Action.Kind == ActionKind.AitumState ||
            (k.Action.Kind == ActionKind.Monitor && k.Action.Settings.Metric == MonitorMetric.AitumState));

        if (!needed)
        {
            return;
        }

        _aitumStateFetched = DateTime.UtcNow;
        var state = await _aitum.GetStateAsync().ConfigureAwait(false);
        lock (_aitumStateCache)
        {
            _aitumStateCache.Clear();
            foreach (var variable in state)
            {
                _aitumStateCache[variable.Name] = variable.Value;
            }
        }
    }

    /// <summary>Builds the render request for one key, including any live value.</summary>
    public KeyRenderRequest BuildRequest(KeySlot slot, int size)
    {
        var action = slot.Action;
        string? valueText = null;
        double? gauge = null;
        var active = false;

        switch (action.Kind)
        {
            case ActionKind.Monitor:
            {
                var display = MetricFormatter.Format(
                    action.Settings.Metric,
                    _lastMetrics,
                    _audio.GetVolume(),
                    _audio.GetMute(),
                    DateTime.Now,
                    LookupAitumState(action.Settings.MetricArgument));
                valueText = display.Value;
                gauge = display.Gauge;
                break;
            }

            case ActionKind.AitumState:
                valueText = LookupAitumState(action.Settings.AitumStateName) ?? "-";
                break;

            case ActionKind.Obs:
                active = ObsCommandRunner.IsActive(_obs, action.Settings);
                break;

            case ActionKind.SwitchProfile:
                active = action.Settings.TargetProfileId == _config.ActiveProfileId;
                break;

            case ActionKind.Macro:
                active = _executor?.IsRunning(new KeyIdentity(ActiveProfile.Id, CurrentPage.Id, slot.Index)) == true;
                break;
        }

        return new KeyRenderRequest
        {
            Appearance = slot.Appearance,
            Size = size,
            ValueText = valueText,
            Gauge = gauge,
            IsActive = active,
            ResolveImagePath = _store.ResolveImage,
        };
    }

    private string? LookupAitumState(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_aitumStateCache)
        {
            return _aitumStateCache.TryGetValue(name!, out var value) ? value : null;
        }
    }

    public async Task RenderAsync()
    {
        if (_suspended)
        {
            return;
        }

        if (!await _renderLock.WaitAsync(0).ConfigureAwait(false))
        {
            // A render is already in flight; its result will include the latest state.
            return;
        }

        try
        {
            SurfaceChanged?.Invoke();

            var deviceId = _device.PrimaryDeviceId;
            if (deviceId is null)
            {
                return;
            }

            var page = CurrentPage;
            for (var index = 0; index < DisplayPadLayout.KeyCount; index++)
            {
                var slot = page.Keys[index];
                var png = KeyImageRenderer.RenderPng(BuildRequest(slot, DisplayPadLayout.KeyPixels));
                var hash = SHA256.HashData(png);

                if (_uploadedHashes[index] is { } previous && previous.AsSpan().SequenceEqual(hash))
                {
                    continue;
                }

                if (await _device.UploadKeyImageAsync(deviceId.Value, index, png).ConfigureAwait(false))
                {
                    _uploadedHashes[index] = hash;
                }
                else
                {
                    _uploadedHashes[index] = null;
                }
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Rendering failed: {ex.Message}");
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>Writes the current page into the device's flash so it shows without the app.</summary>
    public async Task<bool> PersistCurrentPageAsync(int firmwareProfileIndex)
    {
        if (_device.PrimaryDeviceId is not { } deviceId)
        {
            return false;
        }

        var page = CurrentPage;
        var ok = true;
        for (var index = 0; index < DisplayPadLayout.KeyCount; index++)
        {
            var png = KeyImageRenderer.RenderPng(BuildRequest(page.Keys[index], DisplayPadLayout.KeyPixels));
            ok &= await _device.PersistKeyImageAsync(deviceId, index, firmwareProfileIndex, png).ConfigureAwait(false);
        }

        InvalidateAll();
        return ok;
    }

    public void Dispose()
    {
        _liveTimer.Stop();
        _liveTimer.Dispose();
        _device.KeyEvent -= OnKeyEvent;
        _device.DeviceStatusChanged -= OnDeviceStatusChanged;
        _obs.StateChanged -= OnObsStateChanged;
        _renderLock.Dispose();
    }
}
