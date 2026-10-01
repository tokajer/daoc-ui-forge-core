using DaocUiForge.Core.Formats;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

public class TgaDecoderTests
{
    /// <summary>
    /// Builds an uncompressed TGA (type 2) out of RGB triples. Storage is BGR;
    /// the origin is set through the descriptor byte.
    /// </summary>
    private static byte[] BuildTga24(int w, int h, (byte r, byte g, byte b)[] pixels, bool topLeft)
    {
        var buf = new byte[18 + w * h * 3];
        buf[2] = 2;                       // type 2, uncompressed
        buf[12] = (byte)(w & 0xff); buf[13] = (byte)(w >> 8);
        buf[14] = (byte)(h & 0xff); buf[15] = (byte)(h >> 8);
        buf[16] = 24;                     // bpp
        buf[17] = (byte)(topLeft ? 0x20 : 0x00);
        int p = 18;
        foreach (var (r, g, b) in pixels)
        {
            buf[p++] = b; buf[p++] = g; buf[p++] = r; // BGR
        }
        return buf;
    }

    [Fact]
    public void Type2_TopLeft_ReadsColorsBackAsRgba()
    {
        // 2x1: red, then green
        var px = new (byte, byte, byte)[] { (255, 0, 0), (0, 255, 0) };
        var tga = BuildTga24(2, 1, px, topLeft: true);

        using var bmp = TgaDecoder.Decode(tga);
        Assert.NotNull(bmp);
        Assert.Equal(2, bmp!.Width);
        Assert.Equal(1, bmp.Height);

        var p0 = bmp.GetPixel(0, 0);
        Assert.Equal(255, p0.Red); Assert.Equal(0, p0.Green); Assert.Equal(0, p0.Blue);
        var p1 = bmp.GetPixel(1, 0);
        Assert.Equal(0, p1.Red); Assert.Equal(255, p1.Green); Assert.Equal(0, p1.Blue);
    }

    [Fact]
    public void Type2_BottomLeft_IsFlippedVertically()
    {
        // 1x2, Ursprung unten-links: erste gespeicherte Zeile ist die untere.
        // We store red (bottom), green (top) → after the flip:
        // y=0 (top) = green, y=1 (bottom) = red.
        var px = new (byte, byte, byte)[] { (255, 0, 0), (0, 255, 0) };
        var tga = BuildTga24(1, 2, px, topLeft: false);

        using var bmp = TgaDecoder.Decode(tga);
        Assert.NotNull(bmp);
        var top = bmp!.GetPixel(0, 0);
        var bottom = bmp.GetPixel(0, 1);
        Assert.Equal(0, top.Red); Assert.Equal(255, top.Green);      // green on top
        Assert.Equal(255, bottom.Red); Assert.Equal(0, bottom.Green); // Rot unten
    }

    [Fact]
    public void UnsupportedType_ReturnsNull()
    {
        var buf = new byte[18];
        buf[2] = 1; // colour-map type, not supported
        buf[12] = 1; buf[14] = 1; buf[16] = 24;
        Assert.Null(TgaDecoder.Decode(buf));
    }
}
