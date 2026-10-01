using DaocUiForge.Core.Formats;
using Xunit;

namespace DaocUiForge.Tests;

public class TtfMetricsTests
{
    /// <summary>
    /// Builds a minimal TTF table structure with head and hhea only, just
    /// enough for LineHeightEm to find the metrics. Big-endian.
    /// </summary>
    private static byte[] BuildFont(int unitsPerEm, short ascender, short descender, short lineGap)
    {
        // sfnt header: 12 bytes, then a 16-byte directory entry per table.
        // We lay out two tables: head and hhea.
        int numTables = 2;
        int dirSize = 12 + numTables * 16;

        // head table: unitsPerEm sits at offset 18 (2 bytes).
        int headLen = 54;
        // hhea-Tabelle: ascender@4, descender@6, lineGap@8 (je 2 Byte).
        int hheaLen = 36;

        int headOff = dirSize;
        int hheaOff = headOff + headLen;
        var buf = new byte[hheaOff + hheaLen];

        void W16(int off, int v) { buf[off] = (byte)(v >> 8); buf[off + 1] = (byte)(v & 0xff); }
        void W32(int off, uint v)
        {
            buf[off] = (byte)(v >> 24); buf[off + 1] = (byte)(v >> 16);
            buf[off + 2] = (byte)(v >> 8); buf[off + 3] = (byte)(v & 0xff);
        }
        void WTag(int off, string tag)
        {
            for (int i = 0; i < 4; i++) buf[off + i] = (byte)tag[i];
        }

        // sfnt-Header
        W32(0, 0x00010000);     // version
        W16(4, numTables);      // numTables (LineHeightEm liest genau das)

        // Directory-Eintrag head
        WTag(12, "head"); W32(12 + 8, (uint)headOff); W32(12 + 12, (uint)headLen);
        // Directory-Eintrag hhea
        WTag(28, "hhea"); W32(28 + 8, (uint)hheaOff); W32(28 + 12, (uint)hheaLen);

        // head-Inhalt
        W16(headOff + 18, unitsPerEm);
        // hhea-Inhalt
        W16(hheaOff + 4, (ushort)ascender);
        W16(hheaOff + 6, (ushort)descender);
        W16(hheaOff + 8, (ushort)lineGap);

        return buf;
    }

    [Fact]
    public void TypicalMetrics_YieldAroundOnePointTwoEm()
    {
        // Values in the range of ordinary TrueType fonts (DejaVu-like):
        // (1901 - (-483) + 0) / 2048 ≈ 1.164
        var font = BuildFont(2048, 1901, -483, 0);
        double lh = TtfMetrics.LineHeightEm(font);
        Assert.InRange(lh, 1.16, 1.17);
    }

    [Fact]
    public void ExactRatio_IsPreserved()
    {
        // (1000 - (-200) + 0) / 1000 = 1.2 exactly
        var font = BuildFont(1000, 1000, -200, 0);
        double lh = TtfMetrics.LineHeightEm(font);
        Assert.Equal(1.2, lh, 6);
    }

    [Fact]
    public void OutOfPlausibleBand_ReturnsZero()
    {
        // line height > 4 → discarded
        var font = BuildFont(100, 450, 0, 0); // 4.5 em
        Assert.Equal(0, TtfMetrics.LineHeightEm(font));
    }

    [Fact]
    public void Garbage_ReturnsZeroNotThrow()
    {
        Assert.Equal(0, TtfMetrics.LineHeightEm(new byte[] { 1, 2, 3 }));
        Assert.Equal(0, TtfMetrics.LineHeightEm(Array.Empty<byte>()));
    }
}
