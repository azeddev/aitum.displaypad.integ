using System.Collections.Concurrent;
using DisplayPad.SDK;
using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.Device;

/// <summary>
/// Owns the one and only <see cref="DisplayPadHelper"/> instance and serialises every call
/// into the native SDK.
///
/// Two SDK behaviours drive the design here:
///   * Constructing <see cref="DisplayPadHelper"/> already starts the SDK's own message pump
///     thread and calls OpenUSBDriver, so the static events must be subscribed *before* the
///     first instance is created - the SDK raises them without a null check.
///   * That pump thread is a foreground thread and the SDK cannot stop it on .NET 8
///     (Thread.Abort throws), so the process has to exit explicitly. See <see cref="Shutdown"/>.
/// </summary>
public sealed class DisplayPadService : IPadHardware, IDisposable
{
    private static readonly object InitLock = new();
    private static DisplayPadService? _instance;

    private readonly SemaphoreSlim _sdkLock = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _connected = new();
    private readonly string _scratchDirectory;
    private DisplayPadHelper? _helper;
    private bool _disposed;

    private DisplayPadService(string scratchDirectory)
    {
        _scratchDirectory = scratchDirectory;
        Directory.CreateDirectory(_scratchDirectory);
    }

    /// <summary>Fired when a pad is plugged in (true) or removed (false).</summary>
    public event Action<int, bool>? DeviceStatusChanged;

    /// <summary>Raw key event: matrix code, pressed, device id.</summary>
    public event Action<int, bool, int>? KeyEvent;

    /// <summary>Firmware update progress, 0..100 or -1 on failure.</summary>
    public event Action<int>? FirmwareProgress;

    public event Action<string>? Log;

    public IReadOnlyCollection<int> ConnectedDevices => _connected.Keys.ToList();

    public int? PrimaryDeviceId => _connected.Keys.OrderBy(id => id).Select(id => (int?)id).FirstOrDefault();

    public bool IsConnected => !_connected.IsEmpty;

    public static DisplayPadService Create(string scratchDirectory)
    {
        lock (InitLock)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            var service = new DisplayPadService(scratchDirectory);

            // Subscribe first: the SDK invokes these delegates without checking for null.
            DisplayPadHelper.DisplayPadPlugCallBack += service.OnPlugCallback;
            DisplayPadHelper.DisplayPadKeyCallBack += service.OnKeyCallback;
            DisplayPadHelper.DisplayPadProgressCallBack += service.OnProgressCallback;

            _instance = service;
            return service;
        }
    }

    /// <summary>Starts the SDK and picks up any pad that was already plugged in.</summary>
    public async Task StartAsync()
    {
        await RunAsync(() =>
        {
            _helper = new DisplayPadHelper();
            Log?.Invoke($"DisplayPad SDK DLL version {_helper.DisplayPadDllVersion()}");
            return true;
        }).ConfigureAwait(false);

        // The plug callback only fires on change, so probe the ids the SDK can hand out
        // (MAX_DEV_COUNT is 10 and ids are documented as non-zero).
        await ScanForDevicesAsync().ConfigureAwait(false);
    }

    public async Task ScanForDevicesAsync()
    {
        for (var id = 1; id <= 10; id++)
        {
            var deviceId = id;
            var plugged = await RunAsync(() => _helper?.DisplayPadIsDevicePlug(deviceId) ?? false).ConfigureAwait(false);
            if (plugged)
            {
                MarkConnected(deviceId);
            }
        }
    }

    public Task<PadDeviceInfo?> GetDeviceInfoAsync(int deviceId) => RunAsync(() =>
    {
        if (_helper is null)
        {
            return (PadDeviceInfo?)null;
        }

        var info = _helper.DisplayPadGetDeviceInfo(deviceId);
        return new PadDeviceInfo(deviceId, info.vid, info.pid, info.fwVer, info.bootloadVer);
    });

    public Task<string> GetFirmwareVersionAsync(int deviceId) =>
        RunAsync(() => _helper?.DisplayPadGetDevAppVer(deviceId) ?? "0");

    /// <summary>Hands control of the key displays to this application.</summary>
    public Task<bool> EnableSoftwareModeAsync(int deviceId, FirmwareKeyMode keyMode) => RunAsync(() =>
    {
        if (_helper is null)
        {
            return false;
        }

        var ok = _helper.DisplayPadAPEnable("true", deviceId);

        if (keyMode != FirmwareKeyMode.LeaveUnchanged)
        {
            _helper.DisplayPadEnableKeyFunc(keyMode == FirmwareKeyMode.Enable ? "true" : "false", deviceId);
        }

        return ok;
    });

    public Task<bool> ReleaseSoftwareModeAsync(int deviceId) => RunAsync(() =>
    {
        if (_helper is null)
        {
            return false;
        }

        _helper.DisplayPadEnableKeyFunc("true", deviceId);
        return _helper.DisplayPadAPEnable("false", deviceId);
    });

    public Task<bool> SetBrightnessAsync(int deviceId, int percent) =>
        RunAsync(() => _helper?.DisplayPadSetMainBrightness(BrightnessLevels.Snap(percent), deviceId) ?? false);

    public Task<int> GetBrightnessAsync(int deviceId) =>
        RunAsync(() => _helper?.DisplayPadGetMainBrightness(deviceId) ?? 0);

    public Task<bool> SetSleepTimerAsync(int deviceId, bool enabled, int hours, int minutes, int seconds) =>
        RunAsync(() => _helper?.DisplayPadSetTFTSleepTime(
            enabled ? "1" : "0",
            Math.Clamp(hours, 0, 24).ToString(),
            Math.Clamp(minutes, 0, 59).ToString(),
            Math.Clamp(seconds, 0, 59).ToString(),
            deviceId) ?? false);

    public Task<(bool Enabled, int Hours, int Minutes, int Seconds)> GetSleepTimerAsync(int deviceId) => RunAsync(() =>
    {
        if (_helper is null)
        {
            return (false, 0, 0, 0);
        }

        var sleep = _helper.DisplayPadGetTFTSleepTime(deviceId);
        return (sleep.byStatus != 0, sleep.byHH, sleep.byMM, sleep.bySS);
    });

    public Task<bool> SwitchFirmwareProfileAsync(int deviceId, int profileNumber) =>
        RunAsync(() => _helper?.DisplayPadSwitchProfile(
            Math.Clamp(profileNumber, 1, DisplayPadLayout.FirmwareProfileCount).ToString(), deviceId) ?? false);

    public Task<bool> ResetPicturesAsync(int deviceId) =>
        RunAsync(() => _helper?.DisplayPadResetPicture(deviceId) ?? false);

    public Task<bool> ResetKeysAsync(int deviceId) =>
        RunAsync(() => _helper?.DisplayPadResetKeys(deviceId) ?? false);

    /// <summary>Factory reset. <paramref name="allProfiles"/> false resets only the current one.</summary>
    public Task<bool> ResetFlashAsync(int deviceId, bool allProfiles) =>
        RunAsync(() => _helper?.DisplayPadResetFlash(allProfiles ? "true" : "false", deviceId) ?? false);

    public Task<byte> GetRemainingImageSlotsAsync(int deviceId) =>
        RunAsync(() => _helper?.DisplayPadCheckBMPStorage(deviceId) ?? (byte)0);

    /// <summary>
    /// Pushes a rendered key face to the pad. The SDK only accepts a file path, so the PNG
    /// is written to a per-key scratch file first. It resizes to 102x102 and applies the
    /// rounded-corner mask itself.
    /// </summary>
    public Task<bool> UploadKeyImageAsync(int deviceId, int keyIndex, byte[] pngBytes)
    {
        if (keyIndex is < 0 or >= DisplayPadLayout.KeyCount)
        {
            return Task.FromResult(false);
        }

        return RunAsync(() =>
        {
            if (_helper is null)
            {
                return false;
            }

            var path = Path.Combine(_scratchDirectory, $"key{keyIndex:00}.png");
            File.WriteAllBytes(path, pngBytes);
            return _helper.UploadImage(deviceId, path, keyIndex);
        });
    }

    /// <summary>Writes a key face into the device's flash so it survives without the app running.</summary>
    public Task<bool> PersistKeyImageAsync(int deviceId, int keyIndex, int firmwareProfileIndex, byte[] pngBytes) =>
        RunAsync(() =>
        {
            if (_helper is null)
            {
                return false;
            }

            var path = Path.Combine(_scratchDirectory, $"persist{keyIndex:00}.png");
            File.WriteAllBytes(path, pngBytes);
            return _helper.UploadImageBySetIconPic(deviceId, path, keyIndex, false, firmwareProfileIndex);
        });

    public Task<bool> StartFirmwareUpdateAsync(int deviceId, byte[] binary, byte targetDevice, ushort firmwareVersion) =>
        RunAsync(() =>
        {
            if (_helper is null || binary.Length == 0)
            {
                return false;
            }

            var info = new DisplayPadSDK.FWBinUpdateInfo_byte
            {
                pBinImage1 = binary,
                dwBinLength1 = (uint)binary.Length,
                byTargetDev = targetDevice,
                uFWVersion = firmwareVersion,
            };

            return _helper.DisplayPadStartFWUpdate(info, deviceId);
        });

    public Task<bool> IsUpdatingAsync(int deviceId) =>
        RunAsync(() => _helper?.DisplayPadIsUpdating(deviceId) ?? false);

    private void OnPlugCallback(int status, int deviceId)
    {
        // status: 0 = removed, 1 = plugged, 2 = suspend
        switch (status)
        {
            case 1:
                MarkConnected(deviceId);
                break;
            case 0:
            case 2:
                if (_connected.TryRemove(deviceId, out _))
                {
                    Log?.Invoke($"DisplayPad {deviceId} disconnected (status {status}).");
                    DeviceStatusChanged?.Invoke(deviceId, false);
                }

                break;
        }
    }

    private void MarkConnected(int deviceId)
    {
        if (_connected.TryAdd(deviceId, 0))
        {
            Log?.Invoke($"DisplayPad {deviceId} connected.");
            DeviceStatusChanged?.Invoke(deviceId, true);
        }
    }

    private void OnKeyCallback(int keyMatrix, int pressed, int deviceId) =>
        KeyEvent?.Invoke(keyMatrix, pressed != 0, deviceId);

    private void OnProgressCallback(int percentage) => FirmwareProgress?.Invoke(percentage);

    private async Task<T> RunAsync<T>(Func<T> work)
    {
        if (_disposed)
        {
            return default!;
        }

        await _sdkLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"SDK call failed: {ex.Message}");
            return default!;
        }
        finally
        {
            _sdkLock.Release();
        }
    }

    /// <summary>
    /// Closes the driver and terminates the process. The SDK keeps a foreground message-pump
    /// thread alive that it cannot stop on .NET 8, so a normal return from Main would hang.
    /// </summary>
    public void Shutdown(int exitCode = 0)
    {
        try
        {
            Dispose();
        }
        finally
        {
            Environment.Exit(exitCode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _helper?.DisplayPadCloseUSBDriver();
        }
        catch (Exception)
        {
            // The driver may already be gone; nothing useful to do here.
        }

        DisplayPadHelper.DisplayPadPlugCallBack -= OnPlugCallback;
        DisplayPadHelper.DisplayPadKeyCallBack -= OnKeyCallback;
        DisplayPadHelper.DisplayPadProgressCallBack -= OnProgressCallback;

        _sdkLock.Dispose();
    }
}
