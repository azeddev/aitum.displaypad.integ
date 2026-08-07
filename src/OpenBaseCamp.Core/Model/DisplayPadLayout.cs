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

    /// <summary>Default grid orientation: 4 columns x 3 rows (landscape).</summary>
    public const int DefaultColumns = 4;

    public const int DefaultRows = 3;

    public static int RowsFor(int columns) => columns <= 0 ? DefaultRows : KeyCount / columns;
}
