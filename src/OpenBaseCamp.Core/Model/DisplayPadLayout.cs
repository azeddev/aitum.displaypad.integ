namespace OpenBaseCamp.Core.Model;

/// <summary>
/// Physical characteristics of the Mountain DisplayPad, as reported by the SDK.
/// FW_NUM_KEY is 12 and <c>SetIconPic</c> defaults to a 31212 byte payload, which is
/// 102 * 102 * 3 (BGR888) - so every key is a 102x102 image.
/// </summary>
public static class DisplayPadLayout
{
    public const int KeyCount = 12;
    public const int KeyPixels = 102;

    /// <summary>Bytes of a single key image once converted to BGR888.</summary>
    public const int KeyImageBytes = KeyPixels * KeyPixels * 3;

    /// <summary>Profiles the firmware itself can hold (FW_NUM_PROFILE).</summary>
    public const int FirmwareProfileCount = 5;

    /// <summary>Width of the whole key panel, from the SetPanelImage defaults (right = 799).</summary>
    public const int PanelWidth = 800;

    /// <summary>Height of the whole key panel, from the SetPanelImage defaults (bottom = 239).</summary>
    public const int PanelHeight = 240;

    /// <summary>
    /// The pad is a landscape strip: 6 columns by 2 rows. The panel is 800x240, and 6x2 is the
    /// only arrangement of twelve keys whose cells (133x120) can hold a 102x102 key image -
    /// 4x3 would give cells only 80 pixels tall.
    /// </summary>
    public const int DefaultColumns = 6;

    public const int DefaultRows = 2;

    public static int RowsFor(int columns) =>
        columns <= 0 ? DefaultRows : Math.Max(1, KeyCount / columns);
}
