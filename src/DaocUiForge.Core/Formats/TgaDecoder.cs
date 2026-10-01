using SkiaSharp;

namespace DaocUiForge.Core.Formats;

/// <summary>
/// Decodes TGA textures: type 2 (uncompressed) and type 10 (RLE), 24 or 32 bit
/// each. Ported from <c>decodeTGA</c> of the HTML original. Browsers cannot
/// read TGA natively, and neither can .NET — hence a decoder of our own (plain
/// byte arithmetic).
///
/// Quirks that have to be preserved:
/// - Pixels arrive as BGR(A) and are reordered to RGBA.
/// - Bit 5 of the descriptor byte (0x20) decides the origin: set means
///   top-left, otherwise bottom-left → flip vertically.
/// </summary>
public static class TgaDecoder
{
    public static SKBitmap? Decode(byte[] d)
    {
        if (d.Length < 18) return null;

        int idlen = d[0];
        int cmType = d[1];
        int type = d[2];
        int w = d[12] | (d[13] << 8);
        int h = d[14] | (d[15] << 8);
        int bpp = d[16];
        int desc = d[17];

        if (w == 0 || h == 0) return null;
        if (type != 2 && type != 10) return null;
        int bytes = bpp >> 3;
        if (bytes < 3) return null;

        // Skip the colour map length, if there is one.
        int cmLen = cmType != 0 ? (d[5] | (d[6] << 8)) : 0;
        int cmEntryBytes = cmType != 0 ? (d[7] >> 3) : 0;
        int p = 18 + idlen + cmLen * cmEntryBytes;

        int px = w * h;
        var outp = new byte[px * 4]; // RGBA
        int i = 0;

        if (type == 2)
        {
            for (; i < px; i++)
            {
                if (p + bytes > d.Length) break;
                int o = i * 4;
                outp[o] = d[p + 2];     // R
                outp[o + 1] = d[p + 1]; // G
                outp[o + 2] = d[p];     // B
                outp[o + 3] = bytes == 4 ? d[p + 3] : (byte)255;
                p += bytes;
            }
        }
        else // type == 10, RLE
        {
            while (i < px && p < d.Length)
            {
                int hb = d[p++];
                int cnt = (hb & 0x7f) + 1;
                if ((hb & 0x80) != 0)
                {
                    if (p + bytes > d.Length) break;
                    byte b = d[p], g = d[p + 1], r = d[p + 2];
                    byte a = bytes == 4 ? d[p + 3] : (byte)255;
                    p += bytes;
                    for (int k = 0; k < cnt && i < px; k++, i++)
                    {
                        int o = i * 4;
                        outp[o] = r; outp[o + 1] = g; outp[o + 2] = b; outp[o + 3] = a;
                    }
                }
                else
                {
                    for (int k = 0; k < cnt && i < px; k++, i++)
                    {
                        if (p + bytes > d.Length) break;
                        int o = i * 4;
                        outp[o] = d[p + 2];
                        outp[o + 1] = d[p + 1];
                        outp[o + 2] = d[p];
                        outp[o + 3] = bytes == 4 ? d[p + 3] : (byte)255;
                        p += bytes;
                    }
                }
            }
        }

        bool topLeft = (desc & 0x20) != 0;
        return BitmapBuilder.FromRgba(outp, w, h, flipVertical: !topLeft);
    }
}
