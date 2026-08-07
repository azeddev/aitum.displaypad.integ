namespace OpenBaseCamp.Core.Model;

public enum IconFit
{
    /// <summary>Scale until the whole icon fits, keeping aspect ratio.</summary>
    Contain,

    /// <summary>Scale until the icon covers the key, keeping aspect ratio (crops).</summary>
    Cover,

    /// <summary>Ignore aspect ratio.</summary>
    Stretch,

    /// <summary>Draw at native size, centred.</summary>
    Center,
}

public enum TitlePosition
{
    Top,
    Middle,
    Bottom,
}

/// <summary>How a key is painted on the 102x102 LCD.</summary>
public sealed class KeyAppearance
{
    /// <summary>Identifier of a built-in vector icon (see <c>BuiltInIcons</c>).</summary>
    public string? IconId { get; set; }

    /// <summary>File name inside the image library folder, if a custom image was chosen.</summary>
    public string? ImageFile { get; set; }

    public string BackgroundColor { get; set; } = "#FF101014";

    public string? IconTint { get; set; }

    public IconFit IconFit { get; set; } = IconFit.Contain;

    /// <summary>1.0 fills the key; smaller values inset the icon.</summary>
    public double IconScale { get; set; } = 0.62;

    public string? Title { get; set; }

    public bool ShowTitle { get; set; } = true;

    public string TitleColor { get; set; } = "#FFFFFFFF";

    public double TitleSize { get; set; } = 17;

    public string TitleFont { get; set; } = "Segoe UI";

    public bool TitleBold { get; set; } = true;

    public bool TitleOutline { get; set; } = true;

    public TitlePosition TitlePosition { get; set; } = TitlePosition.Bottom;

    public KeyAppearance Clone() => (KeyAppearance)MemberwiseClone();
}
