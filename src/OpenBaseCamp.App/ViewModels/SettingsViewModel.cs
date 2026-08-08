using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenBaseCamp.App.Platform;
using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly AppHost _host;

    [ObservableProperty]
    private string _obsStatus = string.Empty;

    [ObservableProperty]
    private string _aitumStatus = string.Empty;

    [ObservableProperty]
    private string _deviceStatus = string.Empty;

    [ObservableProperty]
    private string _mappingStatus = "Not started.";

    [ObservableProperty]
    private bool _isMapping;

    [ObservableProperty]
    private int _mappingIndex;

    [ObservableProperty]
    private string _firmwareFile = string.Empty;

    [ObservableProperty]
    private int _firmwareProgress;

    [ObservableProperty]
    private string _firmwareStatus = string.Empty;

    [ObservableProperty]
    private string _spotifyStatus = string.Empty;

    [ObservableProperty]
    private string _twitchStatus = string.Empty;

    [ObservableProperty]
    private bool _isAuthorizing;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _host = main.Host;

        _startWithWindows = PlatformFactory.AutoStartEnabled;
        _host.Hardware.FirmwareProgress += OnFirmwareProgress;
        _host.Controller.KeyMatrixObserved += OnKeyMatrixObserved;

        _ = RefreshDeviceStatusAsync();
    }

    private AppSettings Settings => _host.Config.Settings;

    private DeviceSettings Device => Settings.Device;

    public IReadOnlyList<int> BrightnessSteps { get; } = BrightnessLevels.All;

    public IReadOnlyList<FirmwareKeyMode> FirmwareKeyModes { get; } = Enum.GetValues<FirmwareKeyMode>();

    public IReadOnlyList<int> ColumnChoices { get; } = new[] { 6, 4, 3, 2 };

    public string ConfigLocation => _host.Store.RootDirectory;

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // ---- General ------------------------------------------------------------

    private bool _startWithWindows;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value))
            {
                return;
            }

            Settings.StartWithWindows = value;
            if (!PlatformFactory.TrySetAutoStart(value, out var error) && error is not null)
            {
                _main.ReportError(error);
                SetProperty(ref _startWithWindows, PlatformFactory.AutoStartEnabled);
            }
        }
    }

    public bool StartMinimized
    {
        get => Settings.StartMinimized;
        set
        {
            Settings.StartMinimized = value;
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTray
    {
        get => Settings.MinimizeToTray;
        set
        {
            Settings.MinimizeToTray = value;
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => Settings.CloseToTray;
        set
        {
            Settings.CloseToTray = value;
            OnPropertyChanged();
        }
    }

    public bool AutoSwitchProfileByApp
    {
        get => Settings.AutoSwitchProfileByApp;
        set
        {
            Settings.AutoSwitchProfileByApp = value;
            OnPropertyChanged();
        }
    }

    public int MonitorRefreshMs
    {
        get => Settings.MonitorRefreshMs;
        set
        {
            Settings.MonitorRefreshMs = Math.Clamp(value, 250, 60_000);
            OnPropertyChanged();
        }
    }

    // ---- Device -------------------------------------------------------------

    public int Brightness
    {
        get => Device.Brightness;
        set
        {
            Device.Brightness = BrightnessLevels.Snap(value);
            OnPropertyChanged();
            _ = _host.Controller.SetBrightnessAsync(Device.Brightness);
        }
    }

    public bool SleepEnabled
    {
        get => Device.SleepEnabled;
        set
        {
            Device.SleepEnabled = value;
            OnPropertyChanged();
            ApplySleep();
        }
    }

    public int SleepHours
    {
        get => Device.SleepHours;
        set
        {
            Device.SleepHours = Math.Clamp(value, 0, 24);
            OnPropertyChanged();
            ApplySleep();
        }
    }

    public int SleepMinutes
    {
        get => Device.SleepMinutes;
        set
        {
            Device.SleepMinutes = Math.Clamp(value, 0, 59);
            OnPropertyChanged();
            ApplySleep();
        }
    }

    public int SleepSeconds
    {
        get => Device.SleepSeconds;
        set
        {
            Device.SleepSeconds = Math.Clamp(value, 0, 59);
            OnPropertyChanged();
            ApplySleep();
        }
    }

    public int Columns
    {
        get => Device.Columns;
        set
        {
            Device.Columns = value;
            OnPropertyChanged();
            _main.RefreshGridShape();
        }
    }

    public FirmwareKeyMode FirmwareKeyMode
    {
        get => Device.FirmwareKeyMode;
        set
        {
            Device.FirmwareKeyMode = value;
            OnPropertyChanged();

            if (_host.Hardware.PrimaryDeviceId is { } id)
            {
                _ = _host.Hardware.EnableSoftwareModeAsync(id, value);
            }
        }
    }

    private void ApplySleep() =>
        _ = _host.Controller.SetSleepTimerAsync(Device.SleepEnabled, Device.SleepHours, Device.SleepMinutes, Device.SleepSeconds);

    private async Task RefreshDeviceStatusAsync()
    {
        if (_host.Hardware.PrimaryDeviceId is not { } id)
        {
            DeviceStatus = "No DisplayPad is connected.";
            return;
        }

        var info = await _host.Hardware.GetDeviceInfoAsync(id);
        var slots = await _host.Hardware.GetRemainingImageSlotsAsync(id);
        DeviceStatus = info is null
            ? $"Device {id} connected."
            : $"{info.Identity}, firmware {info.FirmwareText}, bootloader {info.BootloaderVersion}, {slots} image slots free.";
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        await _host.Hardware.ScanForDevicesAsync();
        await RefreshDeviceStatusAsync();
        await _main.RefreshDeviceAsync();
    }

    [RelayCommand]
    private async Task ResetPicturesAsync()
    {
        if (_host.Hardware.PrimaryDeviceId is { } id)
        {
            await _host.Hardware.ResetPicturesAsync(id);
            _host.Controller.InvalidateAll();
        }
    }

    [RelayCommand]
    private async Task FactoryResetAsync()
    {
        if (_host.Hardware.PrimaryDeviceId is not { } id)
        {
            return;
        }

        await _host.Hardware.ResetKeysAsync(id);
        await _host.Hardware.ResetFlashAsync(id, allProfiles: true);
        await _host.Hardware.EnableSoftwareModeAsync(id, Device.FirmwareKeyMode);
        _host.Controller.InvalidateAll();
        DeviceStatus = "The device was reset to factory defaults.";
    }

    [RelayCommand]
    private async Task PersistCurrentPageAsync()
    {
        var ok = await _host.Controller.PersistCurrentPageAsync(1);
        DeviceStatus = ok
            ? "The current page was written to the device's first firmware profile."
            : "The page could not be written to the device.";
    }

    // ---- Key mapping wizard -------------------------------------------------

    public string MappingPrompt => IsMapping
        ? $"Press key {MappingIndex + 1} of {DisplayPadLayout.KeyCount} on the pad."
        : "Run this once so presses land on the right key.";

    [RelayCommand]
    private void StartMapping()
    {
        if (!_host.Hardware.IsConnected)
        {
            MappingStatus = "Connect a DisplayPad first.";
            return;
        }

        Device.KeyMatrixMap.Clear();
        MappingIndex = 0;
        IsMapping = true;
        MappingStatus = "Waiting for the first key…";
        OnPropertyChanged(nameof(MappingPrompt));
    }

    [RelayCommand]
    private void CancelMapping()
    {
        IsMapping = false;
        MappingStatus = "Cancelled.";
        OnPropertyChanged(nameof(MappingPrompt));
    }

    [RelayCommand]
    private void ClearMapping()
    {
        Device.KeyMatrixMap.Clear();
        IsMapping = false;
        MappingStatus = "Cleared - the built-in guess is used again.";
        OnPropertyChanged(nameof(MappingPrompt));
    }

    private void OnKeyMatrixObserved(int matrix, bool pressed, int resolvedIndex)
    {
        if (!pressed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsMapping)
            {
                MappingStatus = $"Last key seen: matrix 0x{matrix:X} → index {(resolvedIndex < 0 ? "unmapped" : resolvedIndex.ToString())}.";
                return;
            }

            KeyMatrixMap.Learn(Device, matrix, MappingIndex);
            MappingIndex++;

            if (MappingIndex >= DisplayPadLayout.KeyCount)
            {
                IsMapping = false;
                MappingStatus = "All twelve keys were mapped.";
                _host.SaveConfig();
            }
            else
            {
                MappingStatus = $"Recorded matrix 0x{matrix:X} for key {MappingIndex}.";
            }

            OnPropertyChanged(nameof(MappingPrompt));
        });
    }

    // ---- Firmware -----------------------------------------------------------

    [RelayCommand]
    private async Task ChooseFirmwareAsync()
    {
        var file = await _main.PickAnyFileAsync();
        if (file is not null)
        {
            FirmwareFile = file;
        }
    }

    [RelayCommand]
    private async Task FlashFirmwareAsync()
    {
        if (_host.Hardware.PrimaryDeviceId is not { } id)
        {
            FirmwareStatus = "No DisplayPad is connected.";
            return;
        }

        if (!File.Exists(FirmwareFile))
        {
            FirmwareStatus = "Choose a firmware .bin file first.";
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(FirmwareFile);
            FirmwareProgress = 0;
            FirmwareStatus = "Updating - do not unplug the device.";
            _host.Controller.Suspend();

            var started = await _host.Hardware.StartFirmwareUpdateAsync(id, bytes, 0, 0);
            if (!started)
            {
                FirmwareStatus = "The update could not be started (wrong file?).";
                _host.Controller.Resume();
            }
        }
        catch (Exception ex)
        {
            FirmwareStatus = $"Update failed: {ex.Message}";
            _host.Controller.Resume();
        }
    }

    private void OnFirmwareProgress(int percent) => Dispatcher.UIThread.Post(() =>
    {
        if (percent < 0)
        {
            FirmwareStatus = "The firmware update failed.";
            FirmwareProgress = 0;
            _host.Controller.Resume();
            return;
        }

        FirmwareProgress = percent;
        FirmwareStatus = percent >= 100 ? "The firmware update finished." : $"Updating… {percent}%";

        if (percent >= 100)
        {
            _host.Controller.Resume();
        }
    });

    // ---- OBS ----------------------------------------------------------------

    public bool ObsEnabled
    {
        get => Settings.Obs.Enabled;
        set
        {
            Settings.Obs.Enabled = value;
            OnPropertyChanged();
            _ = ApplyObsAsync();
        }
    }

    public string ObsHost
    {
        get => Settings.Obs.Host;
        set
        {
            Settings.Obs.Host = value;
            OnPropertyChanged();
        }
    }

    public int ObsPort
    {
        get => Settings.Obs.Port;
        set
        {
            Settings.Obs.Port = Math.Clamp(value, 1, 65535);
            OnPropertyChanged();
        }
    }

    public string ObsPassword
    {
        get => Settings.Obs.Password;
        set
        {
            Settings.Obs.Password = value;
            OnPropertyChanged();
        }
    }

    public bool ObsAutoReconnect
    {
        get => Settings.Obs.AutoReconnect;
        set
        {
            Settings.Obs.AutoReconnect = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task ApplyObsAsync()
    {
        ObsStatus = Settings.Obs.Enabled ? "Connecting…" : "Disabled.";
        await _host.Obs.ApplySettingsAsync(Settings.Obs);

        if (!Settings.Obs.Enabled)
        {
            return;
        }

        // Give the handshake a moment before reporting.
        await Task.Delay(1200);
        ObsStatus = _host.Obs.IsConnected
            ? $"Connected to OBS at {Settings.Obs.Host}:{Settings.Obs.Port}."
            : $"Not connected: {_host.Obs.LastError ?? "no response"}";
    }

    // ---- Aitum --------------------------------------------------------------

    public bool AitumEnabled
    {
        get => Settings.Aitum.Enabled;
        set
        {
            Settings.Aitum.Enabled = value;
            OnPropertyChanged();
            _host.Aitum.ApplySettings(Settings.Aitum);
        }
    }

    public string AitumHost
    {
        get => Settings.Aitum.Host;
        set
        {
            Settings.Aitum.Host = value;
            OnPropertyChanged();
            _host.Aitum.ApplySettings(Settings.Aitum);
        }
    }

    public int AitumPort
    {
        get => Settings.Aitum.Port;
        set
        {
            Settings.Aitum.Port = Math.Clamp(value, 1, 65535);
            OnPropertyChanged();
            _host.Aitum.ApplySettings(Settings.Aitum);
        }
    }

    [RelayCommand]
    private async Task TestAitumAsync()
    {
        _host.Aitum.ApplySettings(Settings.Aitum);
        AitumStatus = "Checking…";

        if (await _host.Aitum.TestConnectionAsync())
        {
            var rules = await _host.Aitum.GetRulesAsync();
            AitumStatus = $"Connected to Aitum. {rules.Count} rule(s) available.";
        }
        else
        {
            AitumStatus = $"No response from {_host.Aitum.BaseUrl}: {_host.Aitum.LastError}";
        }
    }

    // ---- Spotify ------------------------------------------------------------

    public bool SpotifyEnabled
    {
        get => Settings.Spotify.Enabled;
        set
        {
            Settings.Spotify.Enabled = value;
            OnPropertyChanged();
            _host.Spotify.ApplySettings(Settings.Spotify);
        }
    }

    public string SpotifyClientId
    {
        get => Settings.Spotify.ClientId;
        set
        {
            Settings.Spotify.ClientId = value.Trim();
            OnPropertyChanged();
            _host.Spotify.ApplySettings(Settings.Spotify);
        }
    }

    public string SpotifyRedirectUri
    {
        get => Settings.Spotify.RedirectUri;
        set
        {
            Settings.Spotify.RedirectUri = value.Trim();
            OnPropertyChanged();
            _host.Spotify.ApplySettings(Settings.Spotify);
        }
    }

    public string SpotifyScopes => string.Join(" ", _host.Spotify.Scopes);

    [RelayCommand]
    private async Task ConnectSpotifyAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.Spotify.ClientId))
        {
            SpotifyStatus = "Enter the client id from your Spotify application first.";
            return;
        }

        Settings.Spotify.Enabled = true;
        OnPropertyChanged(nameof(SpotifyEnabled));
        _host.Spotify.ApplySettings(Settings.Spotify);

        IsAuthorizing = true;
        SpotifyStatus = "A browser window has opened. Approve the request there.";

        var error = await _host.Spotify.AuthorizeAsync(OpenBrowser);
        IsAuthorizing = false;

        if (error is not null)
        {
            SpotifyStatus = error;
            return;
        }

        _host.SaveConfig();
        await _host.Spotify.RefreshNowPlayingAsync(force: true);
        SpotifyStatus = $"Connected. {_host.Spotify.NowPlaying.Display}";
    }

    [RelayCommand]
    private void DisconnectSpotify()
    {
        _host.Spotify.SignOut();
        _host.SaveConfig();
        SpotifyStatus = "Disconnected.";
    }

    // ---- Twitch -------------------------------------------------------------

    public bool TwitchEnabled
    {
        get => Settings.Twitch.Enabled;
        set
        {
            Settings.Twitch.Enabled = value;
            OnPropertyChanged();
            _host.Twitch.ApplySettings(Settings.Twitch);
        }
    }

    public string TwitchClientId
    {
        get => Settings.Twitch.ClientId;
        set
        {
            Settings.Twitch.ClientId = value.Trim();
            OnPropertyChanged();
            _host.Twitch.ApplySettings(Settings.Twitch);
        }
    }

    public string TwitchClientSecret
    {
        get => Settings.Twitch.ClientSecret;
        set
        {
            Settings.Twitch.ClientSecret = value.Trim();
            OnPropertyChanged();
            _host.Twitch.ApplySettings(Settings.Twitch);
        }
    }

    public string TwitchRedirectUri
    {
        get => Settings.Twitch.RedirectUri;
        set
        {
            Settings.Twitch.RedirectUri = value.Trim();
            OnPropertyChanged();
            _host.Twitch.ApplySettings(Settings.Twitch);
        }
    }

    [RelayCommand]
    private async Task ConnectTwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.Twitch.ClientId) || string.IsNullOrWhiteSpace(Settings.Twitch.ClientSecret))
        {
            TwitchStatus = "Enter both the client id and the client secret first.";
            return;
        }

        Settings.Twitch.Enabled = true;
        OnPropertyChanged(nameof(TwitchEnabled));
        _host.Twitch.ApplySettings(Settings.Twitch);

        IsAuthorizing = true;
        TwitchStatus = "A browser window has opened. Approve the request there.";

        var error = await _host.Twitch.AuthorizeAsync(OpenBrowser);
        IsAuthorizing = false;

        if (error is not null)
        {
            TwitchStatus = error;
            return;
        }

        _host.SaveConfig();
        await _host.Twitch.RefreshStateAsync(force: true);
        var state = _host.Twitch.State;
        TwitchStatus = $"Connected as {state.DisplayName ?? Settings.Twitch.Login}. " +
                       (state.IsLive ? $"Live with {state.Viewers} viewers." : "Currently offline.");
    }

    [RelayCommand]
    private void DisconnectTwitch()
    {
        _host.Twitch.SignOut();
        _host.SaveConfig();
        TwitchStatus = "Disconnected.";
    }

    private void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _main.ReportError($"Could not open the browser: {ex.Message}. Open this URL manually: {url}");
        }
    }

    public void Dispose()
    {
        _host.Hardware.FirmwareProgress -= OnFirmwareProgress;
        _host.Controller.KeyMatrixObserved -= OnKeyMatrixObserved;
    }
}
