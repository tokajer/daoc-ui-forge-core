namespace DaocUiForge.Core.Formats;

/// <summary>
/// Reads the line height (in em) straight out of the <c>head</c> and
/// <c>hhea</c> tables of a TTF. Ported from <c>ttfLineHeight</c>.
///
/// Background: in DAoC <c>&lt;TTFFont&gt;&lt;Height&gt;</c> is the font size in
/// pixels, and the line ADVANCE works out as <c>Height x line height</c>, where
/// the line height comes from the TTF metrics:
/// <c>(ascender - descender + lineGap) / unitsPerEm</c>. The HTML original had
/// the two the other way round (a browser can only be given a font size and a
/// line-height, never a baseline), which drew everything about 25 % too small
/// (measured against the game on 2026-08-07).
/// </summary>
public static class TtfMetrics
{
    /// <returns>
    /// Line height in em (typically about 1.3), or 0 when the file cannot be
    /// read or the result falls outside the plausible band (0.5..4).
    /// </returns>
    public static double LineHeightEm(byte[] buf)
    {
        try
        {
            if (buf.Length < 12) return 0;
            int num = U16(buf, 4);
            int head = 0, hhea = 0;
            for (int i = 0; i < num; i++)
            {
                int o = 12 + i * 16;
                if (o + 12 > buf.Length) break;
                string tag = "" + (char)buf[o] + (char)buf[o + 1] + (char)buf[o + 2] + (char)buf[o + 3];
                if (tag == "head") head = (int)U32(buf, o + 8);
                else if (tag == "hhea") hhea = (int)U32(buf, o + 8);
            }
            if (head == 0 || hhea == 0) return 0;

            int upem = U16(buf, head + 18);
            int asc = I16(buf, hhea + 4);
            int desc = I16(buf, hhea + 6);
            int gap = I16(buf, hhea + 8);
            if (upem == 0) return 0;

            double lh = (asc - desc + gap) / (double)upem;
            return (lh > 0.5 && lh < 4) ? lh : 0;
        }
        catch
        {
            return 0;
        }
    }

    // TTF tables are big-endian.
    private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static short I16(byte[] b, int o) => (short)((b[o] << 8) | b[o + 1]);

    private static uint U32(byte[] b, int o) =>
        (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
}
