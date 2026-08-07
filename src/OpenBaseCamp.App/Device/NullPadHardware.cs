using OpenBaseCamp.Core.Devices;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.Device;

/// <summary>
/// Stands in for a DisplayPad when the SDK is not available (design-time preview, or a
/// non-Windows build). Everything succeeds and nothing is sent anywhere.
/// </summary>
public sealed class NullPadHardware : IPadHardware
{
    public event Action<int, bool>? DeviceStatusChanged;

    public event Action<int, bool, int>? KeyEvent;

    public event Action<int>? FirmwareProgress;

    public event Action<string>? Log;

    public IReadOnlyCollection<int> ConnectedDevices => Array.Empty<int>();

    public int? PrimaryDeviceId => null;

    public bool IsConnected => false;

    public Task StartAsync()
    {
        Log?.Invoke("Running without the DisplayPad SDK: the pad preview is simulated.");
        return Task.CompletedTask;
    }

    public Task ScanForDevicesAsync() => Task.CompletedTask;

    public Task<PadDeviceInfo?> GetDeviceInfoAsync(int deviceId) => Task.FromResult<PadDeviceInfo?>(null);

    public Task<string> GetFirmwareVersionAsync(int deviceId) => Task.FromResult("0");

    public Task<bool> EnableSoftwareModeAsync(int deviceId, FirmwareKeyMode keyMode) => Task.FromResult(false);

    public Task<bool> ReleaseSoftwareModeAsync(int deviceId) => Task.FromResult(false);

    public Task<bool> SetBrightnessAsync(int deviceId, int percent) => Task.FromResult(false);

    public Task<int> GetBrightnessAsync(int deviceId) => Task.FromResult(100);

    public Task<bool> SetSleepTimerAsync(int deviceId, bool enabled, int hours, int minutes, int seconds) => Task.FromResult(false);

    public Task<(bool Enabled, int Hours, int Minutes, int Seconds)> GetSleepTimerAsync(int deviceId) =>
        Task.FromResult((false, 0, 0, 0));

    public Task<bool> SwitchFirmwareProfileAsync(int deviceId, int profileNumber) => Task.FromResult(false);

    public Task<bool> ResetPicturesAsync(int deviceId) => Task.FromResult(false);

    public Task<bool> ResetKeysAsync(int deviceId) => Task.FromResult(false);

    public Task<bool> ResetFlashAsync(int deviceId, bool allProfiles) => Task.FromResult(false);

    public Task<byte> GetRemainingImageSlotsAsync(int deviceId) => Task.FromResult((byte)0);

    public Task<bool> UploadKeyImageAsync(int deviceId, int keyIndex, byte[] pngBytes) => Task.FromResult(false);

    public Task<bool> PersistKeyImageAsync(int deviceId, int keyIndex, int firmwareProfileIndex, byte[] pngBytes) => Task.FromResult(false);

    public Task<bool> StartFirmwareUpdateAsync(int deviceId, byte[] binary, byte targetDevice, ushort firmwareVersion) =>
        Task.FromResult(false);

    public Task<bool> IsUpdatingAsync(int deviceId) => Task.FromResult(false);

    public void Shutdown(int exitCode = 0) => Environment.Exit(exitCode);

    /// <summary>Lets the preview harness pretend a key was pressed.</summary>
    public void SimulateKey(int matrix, bool pressed, int deviceId = 1) => KeyEvent?.Invoke(matrix, pressed, deviceId);

    public void SimulateConnection(int deviceId, bool connected) => DeviceStatusChanged?.Invoke(deviceId, connected);

    public void SimulateFirmwareProgress(int percent) => FirmwareProgress?.Invoke(percent);
}
