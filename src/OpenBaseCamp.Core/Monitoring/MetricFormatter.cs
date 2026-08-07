using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;

namespace OpenBaseCamp.Core.Monitoring;

public readonly record struct MetricDisplay(string Value, double? Gauge)
{
    public static MetricDisplay None => new(string.Empty, null);
}

/// <summary>Turns a sampled metric into the text and gauge painted on a key face.</summary>
public static class MetricFormatter
{
    public static MetricDisplay Format(
        MonitorMetric metric,
        SystemMetrics metrics,
        float masterVolume,
        bool muted,
        DateTime now,
        string? aitumStateValue = null)
    {
        switch (metric)
        {
            case MonitorMetric.CpuUsage:
                return Percent(metrics.CpuPercent);

            case MonitorMetric.RamUsage:
                return Percent(metrics.RamPercent);

            case MonitorMetric.RamUsedGb:
                return new MetricDisplay($"{metrics.RamUsedGb:0.0}G", Ratio(metrics.RamPercent));

            case MonitorMetric.GpuUsage:
                return metrics.GpuPercent < 0
                    ? new MetricDisplay("n/a", null)
                    : Percent(metrics.GpuPercent);

            case MonitorMetric.DiskUsage:
                return Percent(metrics.DiskUsedPercent);

            case MonitorMetric.DiskFreeGb:
                return new MetricDisplay(FormatGb(metrics.DiskFreeGb), null);

            case MonitorMetric.NetworkDown:
                return new MetricDisplay(FormatRate(metrics.NetworkDownKbps), null);

            case MonitorMetric.NetworkUp:
                return new MetricDisplay(FormatRate(metrics.NetworkUpKbps), null);

            case MonitorMetric.Time:
                return new MetricDisplay(now.ToString("HH:mm"), null);

            case MonitorMetric.Date:
                return new MetricDisplay(now.ToString("dd MMM"), null);

            case MonitorMetric.MasterVolume:
                if (muted)
                {
                    return new MetricDisplay("Mute", 0);
                }

                return masterVolume < 0
                    ? new MetricDisplay("n/a", null)
                    : Percent(masterVolume * 100.0);

            case MonitorMetric.AitumState:
                return new MetricDisplay(Shorten(aitumStateValue) ?? "-", null);

            default:
                return MetricDisplay.None;
        }
    }

    private static MetricDisplay Percent(double value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        return new MetricDisplay($"{clamped:0}%", clamped / 100.0);
    }

    private static double Ratio(double percent) => Math.Clamp(percent, 0, 100) / 100.0;

    private static string FormatGb(double gb) => gb >= 1000
        ? $"{gb / 1024.0:0.0}T"
        : $"{gb:0}G";

    /// <summary>Network rates arrive in kbit/s.</summary>
    private static string FormatRate(double kbps)
    {
        if (kbps >= 1000)
        {
            return $"{kbps / 1000.0:0.0}M";
        }

        return kbps >= 100 ? $"{kbps:0}k" : $"{kbps:0.0}k";
    }

    private static string? Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().Trim('"');
        return trimmed.Length <= 7 ? trimmed : trimmed[..7];
    }
}
