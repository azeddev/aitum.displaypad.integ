using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using OpenBaseCamp.Core.Services;
using static OpenBaseCamp.App.Services.Win32.NativeMethods;

namespace OpenBaseCamp.App.Services.Win32;

/// <summary>
/// CPU, memory, disk and network sampling for the monitoring keys. Everything except GPU
/// uses plain Win32/BCL calls; GPU utilisation reads the "GPU Engine" performance counters
/// and is reported as -1 when they are not available.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMetricsProvider : ISystemMetricsProvider, IDisposable
{
    private readonly object _lock = new();
    private readonly string _systemDrive;

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private double _cpuPercent;

    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastNetworkSample = DateTime.MinValue;
    private double _downKbps;
    private double _upKbps;

    private PerformanceCounterSet? _gpuCounters;
    private bool _gpuUnavailable;
    private DateTime _lastGpuRefresh = DateTime.MinValue;

    public WindowsMetricsProvider()
    {
        _systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
    }

    public SystemMetrics Sample()
    {
        lock (_lock)
        {
            return new SystemMetrics(
                SampleCpu(),
                SampleRamPercent(out var usedGb, out var totalGb),
                usedGb,
                totalGb,
                SampleGpu(),
                SampleDisk(out var freeGb),
                freeGb,
                SampleNetwork(out var up),
                up);
        }
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return _cpuPercent;
        }

        var idleValue = idle.Value;
        var kernelValue = kernel.Value;
        var userValue = user.Value;

        if (_lastKernel != 0 || _lastUser != 0)
        {
            var idleDelta = idleValue - _lastIdle;
            var kernelDelta = kernelValue - _lastKernel;
            var userDelta = userValue - _lastUser;

            // Kernel time already includes idle time.
            var total = kernelDelta + userDelta;
            if (total > 0)
            {
                _cpuPercent = Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
            }
        }

        _lastIdle = idleValue;
        _lastKernel = kernelValue;
        _lastUser = userValue;
        return _cpuPercent;
    }

    private static double SampleRamPercent(out double usedGb, out double totalGb)
    {
        var status = new MemoryStatusEx { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            usedGb = 0;
            totalGb = 0;
            return 0;
        }

        const double gb = 1024.0 * 1024.0 * 1024.0;
        totalGb = status.ullTotalPhys / gb;
        usedGb = (status.ullTotalPhys - status.ullAvailPhys) / gb;
        return status.dwMemoryLoad;
    }

    private double SampleDisk(out double freeGb)
    {
        try
        {
            var drive = new DriveInfo(_systemDrive);
            if (!drive.IsReady || drive.TotalSize == 0)
            {
                freeGb = 0;
                return 0;
            }

            const double gb = 1024.0 * 1024.0 * 1024.0;
            freeGb = drive.TotalFreeSpace / gb;
            return (drive.TotalSize - drive.TotalFreeSpace) * 100.0 / drive.TotalSize;
        }
        catch (Exception)
        {
            freeGb = 0;
            return 0;
        }
    }

    private double SampleNetwork(out double upKbps)
    {
        try
        {
            long received = 0;
            long sent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            var now = DateTime.UtcNow;
            if (_lastNetworkSample != DateTime.MinValue)
            {
                var seconds = (now - _lastNetworkSample).TotalSeconds;
                if (seconds > 0.05)
                {
                    _downKbps = Math.Max(0, (received - _lastBytesReceived) * 8 / 1000.0 / seconds);
                    _upKbps = Math.Max(0, (sent - _lastBytesSent) * 8 / 1000.0 / seconds);
                }
            }

            _lastBytesReceived = received;
            _lastBytesSent = sent;
            _lastNetworkSample = now;
        }
        catch (Exception)
        {
            // Adapter enumeration can throw while the machine is resuming.
        }

        upKbps = _upKbps;
        return _downKbps;
    }

    private double SampleGpu()
    {
        if (_gpuUnavailable)
        {
            return -1;
        }

        try
        {
            // Instances come and go as processes start; rebuild the set periodically.
            if (_gpuCounters is null || DateTime.UtcNow - _lastGpuRefresh > TimeSpan.FromSeconds(20))
            {
                _gpuCounters?.Dispose();
                _gpuCounters = PerformanceCounterSet.CreateGpuUtilisation();
                _lastGpuRefresh = DateTime.UtcNow;
            }

            if (_gpuCounters is null)
            {
                _gpuUnavailable = true;
                return -1;
            }

            return _gpuCounters.SumPercent();
        }
        catch (Exception)
        {
            _gpuUnavailable = true;
            return -1;
        }
    }

    public void Dispose() => _gpuCounters?.Dispose();

    /// <summary>Wraps the "GPU Engine \ Utilization Percentage" counters.</summary>
    private sealed class PerformanceCounterSet : IDisposable
    {
        private readonly List<PerformanceCounter> _counters;

        private PerformanceCounterSet(List<PerformanceCounter> counters) => _counters = counters;

        public static PerformanceCounterSet? CreateGpuUtilisation()
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return null;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var counters = new List<PerformanceCounter>();

            foreach (var instance in category.GetInstanceNames())
            {
                // "engtype_3D" carries the number people recognise as "GPU usage".
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                    counter.NextValue();
                    counters.Add(counter);
                }
                catch (Exception)
                {
                    // Instance disappeared between enumeration and construction.
                }
            }

            return counters.Count == 0 ? null : new PerformanceCounterSet(counters);
        }

        public double SumPercent()
        {
            double total = 0;
            foreach (var counter in _counters)
            {
                try
                {
                    total += counter.NextValue();
                }
                catch (Exception)
                {
                    // A dead instance contributes nothing.
                }
            }

            return Math.Clamp(total, 0, 100);
        }

        public void Dispose()
        {
            foreach (var counter in _counters)
            {
                counter.Dispose();
            }

            _counters.Clear();
        }
    }
}
