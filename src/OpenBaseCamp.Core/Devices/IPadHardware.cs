using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Devices;

public sealed record PadDeviceInfo(
    int DeviceId,
    ushort VendorId,
    ushort ProductId,
    ushort FirmwareVersion,
    ushort BootloaderVersion)
{
    public string FirmwareText => $"{FirmwareVersion >> 8}.{FirmwareVersion & 0xFF}";

    public string Identity => $"VID {VendorId:X4} / PID {ProductId:X4}";
}

/// <summary>
/// Everything the app does to a DisplayPad. Implemented for real by the Windows SDK wrapper;
/// a no-op implementation lets the UI be built and previewed without hardware.
/// </summary>
public interface IPadHardware
{
    /// <summary>Device id and whether it is now connected.</summary>
    event Action<int, bool>? DeviceStatusChanged;

    /// <summary>Raw key event: matrix code, pressed, device id.</summary>
    event Action<int, bool, int>? KeyEvent;

    /// <summary>Firmware update progress: 0..100, or -1 on failure.</summary>
    event Action<int>? FirmwareProgress;

    event Action<string>? Log;

    IReadOnlyCollection<int> ConnectedDevices { get; }

    int? PrimaryDeviceId { get; }

    bool IsConnected { get; }

    Task StartAsync();

    Task ScanForDevicesAsync();

    Task<PadDeviceInfo?> GetDeviceInfoAsync(int deviceId);

    Task<string> GetFirmwareVersionAsync(int deviceId);

    Task<bool> EnableSoftwareModeAsync(int deviceId, FirmwareKeyMode keyMode);

    Task<bool> ReleaseSoftwareModeAsync(int deviceId);

    Task<bool> SetBrightnessAsync(int deviceId, int percent);

    Task<int> GetBrightnessAsync(int deviceId);

    Task<bool> SetSleepTimerAsync(int deviceId, bool enabled, int hours, int minutes, int seconds);

    Task<(bool Enabled, int Hours, int Minutes, int Seconds)> GetSleepTimerAsync(int deviceId);

    Task<bool> SwitchFirmwareProfileAsync(int deviceId, int profileNumber);

    Task<bool> ResetPicturesAsync(int deviceId);

    Task<bool> ResetKeysAsync(int deviceId);

    Task<bool> ResetFlashAsync(int deviceId, bool allProfiles);

    Task<byte> GetRemainingImageSlotsAsync(int deviceId);

    Task<bool> UploadKeyImageAsync(int deviceId, int keyIndex, byte[] pngBytes);

    Task<bool> PersistKeyImageAsync(int deviceId, int keyIndex, int firmwareProfileIndex, byte[] pngBytes);

    Task<bool> StartFirmwareUpdateAsync(int deviceId, byte[] binary, byte targetDevice, ushort firmwareVersion);

    Task<bool> IsUpdatingAsync(int deviceId);

    /// <summary>Closes the driver and terminates the process.</summary>
    void Shutdown(int exitCode = 0);
}
