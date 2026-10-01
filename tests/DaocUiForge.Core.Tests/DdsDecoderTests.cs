using DaocUiForge.Core.Formats;
using Xunit;

namespace DaocUiForge.Tests;

public class DdsDecoderTests
{
    private static void W32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v & 0xff); b[o + 1] = (byte)((v >> 8) & 0xff);
        b[o + 2] = (byte)((v >> 16) & 0xff); b[o + 3] = (byte)((v >> 24) & 0xff);
    }
    private static void W16(byte[] b, int o, int v)
    {
        b[o] = (byte)(v & 0xff); b[o + 1] = (byte)((v >> 8) & 0xff);
    }
    private static uint FourCC(string s) =>
        (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

    /// <summary>
    /// Builds a 4x4 DXT1 DDS with a single block. c0 is pure red
    /// (RGB565 0xF800), c1 black, every pixel index 0 → every pixel red,
    /// alpha 255.
    /// </summary>
    private static byte[] BuildDxt1SolidRed()
    {
        var b = new byte[128 + 8]; // header + 1 block (8 bytes for DXT1)
        // Magic "DDS "
        b[0] = (byte)'D'; b[1] = (byte)'D'; b[2] = (byte)'S'; b[3] = (byte)' ';
        W32(b, 12, 4);   // height
        W32(b, 16, 4);   // width
        W32(b, 80, 0x4); // pfFlags: komprimiert (FOURCC)
        W32(b, 84, FourCC("DXT1"));

        int off = 128;
        W16(b, off, 0xF800);     // c0 = rot
        W16(b, off + 2, 0x0000); // c1 = schwarz
        W32(b, off + 4, 0x00000000); // alle 16 Indizes = 0

        return b;
    }

    [Fact]
    public void Dxt1_SolidRed_DecodesAllPixelsRed()
    {
        using var bmp = DdsDecoder.Decode(BuildDxt1SolidRed());
        Assert.NotNull(bmp);
        Assert.Equal(4, bmp!.Width);
        Assert.Equal(4, bmp.Height);

        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            var p = bmp.GetPixel(x, y);
            Assert.Equal(255, p.Red);
            Assert.Equal(0, p.Green);
            Assert.Equal(0, p.Blue);
            Assert.Equal(255, p.Alpha);
        }
    }

    [Fact]
    public void NonDds_ReturnsNull()
    {
        var b = new byte[128];
        Assert.Null(DdsDecoder.Decode(b)); // kein Magic
    }

    [Fact]
    public void Uncompressed_Bgra_ReadsBackAsRgba()
    {
        // 1x1 unkomprimiert, 32 Bit BGRA
        var b = new byte[128 + 4];
        b[0] = (byte)'D'; b[1] = (byte)'D'; b[2] = (byte)'S'; b[3] = (byte)' ';
        W32(b, 12, 1); // height
        W32(b, 16, 1); // width
        W32(b, 80, 0x41); // pfFlags: RGB (0x40) | AlphaPixels (0x1), nicht komprimiert
        W32(b, 88, 32);   // rgbBits
        // BGRA for red with alpha 128: B=0,G=0,R=255,A=128
        b[128] = 0; b[129] = 0; b[130] = 255; b[131] = 128;

        using var bmp = DdsDecoder.Decode(b);
        Assert.NotNull(bmp);
        var p = bmp!.GetPixel(0, 0);
        Assert.Equal(255, p.Red);
        Assert.Equal(0, p.Green);
        Assert.Equal(0, p.Blue);
        Assert.Equal(128, p.Alpha);
    }
}
