using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Devices;

/// <summary>
/// Translates the raw <c>wMatrix</c> value the SDK reports for a key press into a
/// 0..11 key index.
///
/// Mountain never published the DisplayPad matrix table, so the mapping is learned:
/// the settings screen has a wizard that asks the user to press each key in order and
/// stores the result in <see cref="DeviceSettings.KeyMatrixMap"/>. Until that has been
/// run a heuristic covers the layouts the firmware plausibly uses (0-based, 1-based, or
/// row/column packed into the high and low bytes).
/// </summary>
public static class KeyMatrixMap
{
    public const int Unknown = -1;

    public static int Resolve(DeviceSettings settings, int matrix, int columns = DisplayPadLayout.DefaultColumns)
    {
        if (settings.KeyMatrixMap.Count > 0)
        {
            if (settings.KeyMatrixMap.TryGetValue(matrix.ToString(), out var learned)
                && learned >= 0
                && learned < DisplayPadLayout.KeyCount)
            {
                return learned;
            }

            // A learned map is authoritative: an unknown code is not one of the 12 keys.
            return Unknown;
        }

        return Guess(matrix, columns);
    }

    /// <summary>Best-effort mapping used before the wizard has been run.</summary>
    public static int Guess(int matrix, int columns = DisplayPadLayout.DefaultColumns)
    {
        if (matrix is >= 0 and < DisplayPadLayout.KeyCount)
        {
            return matrix;
        }

        if (matrix is >= 1 and <= DisplayPadLayout.KeyCount)
        {
            return matrix - 1;
        }

        // Row in the high byte, column in the low byte (either 0- or 1-based).
        var rows = DisplayPadLayout.RowsFor(columns);
        var high = (matrix >> 8) & 0xFF;
        var low = matrix & 0xFF;

        if (high > 0 && high <= rows && low > 0 && low <= columns)
        {
            var index = (high - 1) * columns + (low - 1);
            if (index < DisplayPadLayout.KeyCount)
            {
                return index;
            }
        }

        if (high < rows && low < columns)
        {
            var index = high * columns + low;
            if (index < DisplayPadLayout.KeyCount)
            {
                return index;
            }
        }

        return Unknown;
    }

    public static void Learn(DeviceSettings settings, int matrix, int keyIndex)
    {
        if (keyIndex is < 0 or >= DisplayPadLayout.KeyCount)
        {
            return;
        }

        // One physical key per matrix code: drop any earlier claim on this index.
        foreach (var stale in settings.KeyMatrixMap
                     .Where(pair => pair.Value == keyIndex)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            settings.KeyMatrixMap.Remove(stale);
        }

        settings.KeyMatrixMap[matrix.ToString()] = keyIndex;
    }

    public static bool IsComplete(DeviceSettings settings) =>
        settings.KeyMatrixMap.Values.Distinct().Count() >= DisplayPadLayout.KeyCount;
}
