using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OpenBaseCamp.App.Views;

/// <summary>Green when a pad is connected, grey when it is not.</summary>
public sealed class BoolToConnectionBrushConverter : IValueConverter
{
    private static readonly IBrush Connected = new SolidColorBrush(Color.FromRgb(0x3D, 0xD5, 0x8C));
    private static readonly IBrush Disconnected = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x68));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Connected : Disconnected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a hex colour string into a brush for the small colour swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = Core.Rendering.KeyImageRenderer.ParseColor(value as string, SkiaSharp.SKColors.Transparent);
        return new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
