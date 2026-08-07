using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenBaseCamp.Core.Services;

namespace OpenBaseCamp.App.Services.Win32;

/// <summary>
/// Master volume via the Core Audio API, so a key can set an exact percentage and read the
/// real level back for display (media keys can only step in fixed increments).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioController : IAudioController, IDisposable
{
    private readonly object _lock = new();
    private IAudioEndpointVolume? _endpoint;
    private bool _unavailable;

    public float GetVolume()
    {
        var endpoint = Resolve();
        if (endpoint is null)
        {
            return -1f;
        }

        try
        {
            return endpoint.GetMasterVolumeLevelScalar(out var level) == 0 ? level : -1f;
        }
        catch (Exception)
        {
            Invalidate();
            return -1f;
        }
    }

    public void SetVolume(float level)
    {
        var endpoint = Resolve();
        if (endpoint is null)
        {
            return;
        }

        try
        {
            endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), Guid.Empty);
        }
        catch (Exception)
        {
            Invalidate();
        }
    }

    public void StepVolume(int percentPoints)
    {
        var current = GetVolume();
        if (current < 0)
        {
            return;
        }

        SetVolume(current + percentPoints / 100f);
    }

    public bool GetMute()
    {
        var endpoint = Resolve();
        if (endpoint is null)
        {
            return false;
        }

        try
        {
            return endpoint.GetMute(out var muted) == 0 && muted;
        }
        catch (Exception)
        {
            Invalidate();
            return false;
        }
    }

    public void SetMute(bool muted)
    {
        var endpoint = Resolve();
        if (endpoint is null)
        {
            return;
        }

        try
        {
            endpoint.SetMute(muted, Guid.Empty);
        }
        catch (Exception)
        {
            Invalidate();
        }
    }

    public void ToggleMute() => SetMute(!GetMute());

    private IAudioEndpointVolume? Resolve()
    {
        lock (_lock)
        {
            if (_endpoint is not null)
            {
                return _endpoint;
            }

            if (_unavailable)
            {
                return null;
            }

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0 || device is null)
                {
                    _unavailable = true;
                    return null;
                }

                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var activated) != 0 || activated is null)
                {
                    _unavailable = true;
                    return null;
                }

                _endpoint = (IAudioEndpointVolume)activated;
                return _endpoint;
            }
            catch (Exception)
            {
                // No audio endpoint (headless session, disabled device): degrade quietly.
                _unavailable = true;
                return null;
            }
        }
    }

    private void Invalidate()
    {
        lock (_lock)
        {
            _endpoint = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_endpoint is not null)
            {
                Marshal.FinalReleaseComObject(_endpoint);
                _endpoint = null;
            }
        }
    }

    private const uint ClsCtxAll = 0x17;

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2,
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);

        int UnregisterControlChangeNotify(IntPtr notify);

        int GetChannelCount(out uint count);

        int SetMasterVolumeLevel(float levelDb, Guid eventContext);

        int SetMasterVolumeLevelScalar(float level, Guid eventContext);

        int GetMasterVolumeLevel(out float levelDb);

        int GetMasterVolumeLevelScalar(out float level);

        int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);

        int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);

        int GetChannelVolumeLevel(uint channel, out float levelDb);

        int GetChannelVolumeLevelScalar(uint channel, out float level);

        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);

        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

        int GetVolumeStepInfo(out uint step, out uint stepCount);

        int VolumeStepUp(Guid eventContext);

        int VolumeStepDown(Guid eventContext);

        int QueryHardwareSupport(out uint hardwareSupportMask);

        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }
}
