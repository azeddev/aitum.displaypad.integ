using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Rendering;

/// <summary>Everything the renderer needs to paint one key face.</summary>
public sealed class KeyRenderRequest
{
    public required KeyAppearance Appearance { get; init; }

    /// <summary>Pixel size of the square output. 102 for the device.</summary>
    public int Size { get; init; } = DisplayPadLayout.KeyPixels;

    /// <summary>Replaces <see cref="KeyAppearance.Title"/> - used by live monitoring keys.</summary>
    public string? TitleOverride { get; init; }

    /// <summary>Drawn large in the middle, above the icon. Used for metric values.</summary>
    public string? ValueText { get; init; }

    /// <summary>0..1 arc drawn around the key edge, e.g. CPU load. Null hides it.</summary>
    public double? Gauge { get; init; }

    /// <summary>Highlights the key, e.g. OBS is live or a toggle is on.</summary>
    public bool IsActive { get; init; }

    /// <summary>Resolves <see cref="KeyAppearance.ImageFile"/> to an absolute path.</summary>
    public Func<string, string?>? ResolveImagePath { get; init; }

    /// <summary>
    /// Encoded image drawn in place of the icon and any configured image - used for
    /// album and thumbnail art on now-playing keys.
    /// </summary>
    public byte[]? ImageOverride { get; init; }
}
