using SkiaSharp;

namespace DaocUiForge.Core.Formats;

/// <summary>
/// Decodes DDS textures: DXT1/3/5 as well as uncompressed BGRA. Ported from
/// <c>decodeDDS</c> of the HTML original.
///
/// What matters for faithful reproduction:
/// - Colours are unpacked from RGB565 and scaled with <c>*255/31</c> and
///   <c>*255/63</c> respectively, as in the original.
/// - The JS version writes into a <c>Uint8ClampedArray</c>; assigning to one
///   rounds to the nearest integer and clamps to 0..255.
///   <see cref="Clamp8"/> mirrors that exactly — otherwise the interpolation
///   of the intermediate colours is off by ±1.
/// - DXT1 with c0&lt;=c1 has a 3-colour variant whose fourth index is
///   transparent.
/// </summary>
public static class DdsDecoder
{
    private struct Rgb { public double R, G, B; }

    public static SKBitmap? Decode(byte[] buf)
    {
        if (buf.Length < 128) return null;
        // "DDS " magic, little-endian 0x20534444
        if (U32(buf, 0) != 0x20534444) return null;

        int h = (int)U32(buf, 12);
        int w = (int)U32(buf, 16);
        uint pfFlags = U32(buf, 80);
        uint fourCC = U32(buf, 84);
        uint rgbBits = U32(buf, 88);

        if (w <= 0 || h <= 0) return null;

        var outp = new byte[w * h * 4]; // RGBA
        int off = 128;

        uint FC(string s) =>
            (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

        if ((pfFlags & 0x4) != 0) // compressed
        {
            int dxt = fourCC == FC("DXT1") ? 1
                    : fourCC == FC("DXT3") ? 3
                    : fourCC == FC("DXT5") ? 5
                    : 0;
            if (dxt == 0) return null;

            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            var c = new Rgb[4];

            for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                byte[]? a = null;      // DXT3: 16 explicit alpha values
                int a0 = 0, a1 = 0;    // DXT5: two alpha endpoints
                byte[]? abits = null;  // DXT5: 6 bytes of alpha indices

                if (dxt == 3)
                {
                    a = new byte[16];
                    for (int k = 0; k < 8; k++)
                    {
                        int v = buf[off + k];
                        a[k * 2] = (byte)((v & 15) * 17);
                        a[k * 2 + 1] = (byte)((v >> 4) * 17);
                    }
                    off += 8;
                }
                else if (dxt == 5)
                {
                    a0 = buf[off];
                    a1 = buf[off + 1];
                    abits = new byte[6];
                    for (int k = 2; k < 8; k++) abits[k - 2] = buf[off + k];
                    off += 8;
                }

                int c0 = buf[off] | (buf[off + 1] << 8);
                int c1 = buf[off + 2] | (buf[off + 3] << 8);
                uint bits = U32(buf, off + 4);
                off += 8;

                Unpack565(c0, ref c[0]);
                Unpack565(c1, ref c[1]);

                if (dxt != 1 || c0 > c1)
                {
                    c[2].R = (2 * c[0].R + c[1].R) / 3;
                    c[2].G = (2 * c[0].G + c[1].G) / 3;
                    c[2].B = (2 * c[0].B + c[1].B) / 3;
                    c[3].R = (c[0].R + 2 * c[1].R) / 3;
                    c[3].G = (c[0].G + 2 * c[1].G) / 3;
                    c[3].B = (c[0].B + 2 * c[1].B) / 3;
                }
                else
                {
                    c[2].R = (c[0].R + c[1].R) / 2;
                    c[2].G = (c[0].G + c[1].G) / 2;
                    c[2].B = (c[0].B + c[1].B) / 2;
                    c[3].R = 0; c[3].G = 0; c[3].B = 0;
                }

                for (int py = 0; py < 4; py++)
                for (int px2 = 0; px2 < 4; px2++)
                {
                    int X = bx * 4 + px2, Y = by * 4 + py;
                    if (X >= w || Y >= h) continue;

                    int idx = (int)((bits >> (2 * (py * 4 + px2))) & 3);
                    Rgb col = c[idx];

                    double al = 255;
                    if (dxt == 3)
                    {
                        al = a![py * 4 + px2];
                    }
                    else if (dxt == 5)
                    {
                        int bi = py * 4 + px2, sh = bi * 3;
                        int byI = sh >> 3, bo = sh & 7;
                        int lo = abits![byI];
                        int hi = byI + 1 < abits.Length ? abits[byI + 1] : 0;
                        int v = ((lo | (hi << 8)) >> bo) & 7;
                        if (v == 0) al = a0;
                        else if (v == 1) al = a1;
                        else if (a0 > a1) al = ((8 - v) * a0 + (v - 1) * a1) / 7.0;
                        else if (v == 6) al = 0;
                        else if (v == 7) al = 255;
                        else al = ((6 - v) * a0 + (v - 1) * a1) / 5.0;
                    }
                    else if (dxt == 1 && c0 <= c1 && idx == 3)
                    {
                        al = 0;
                    }

                    int o = (Y * w + X) * 4;
                    outp[o] = Clamp8(col.R);
                    outp[o + 1] = Clamp8(col.G);
                    outp[o + 2] = Clamp8(col.B);
                    outp[o + 3] = Clamp8(al);
                }
            }
        }
        else // uncompressed, BGRA
        {
            int bytes = (int)(rgbBits >> 3);
            if (bytes < 3) return null;
            for (int i = 0; i < w * h; i++)
            {
                int s = off + i * bytes, o = i * 4;
                outp[o] = buf[s + 2];
                outp[o + 1] = buf[s + 1];
                outp[o + 2] = buf[s];
                outp[o + 3] = bytes == 4 ? buf[s + 3] : (byte)255;
            }
        }

        // DDS: origin top-left, no flip needed.
        return BitmapBuilder.FromRgba(outp, w, h, flipVertical: false);
    }

    private static void Unpack565(int v, ref Rgb o)
    {
        o.R = ((v >> 11) & 31) * 255.0 / 31.0;
        o.G = ((v >> 5) & 63) * 255.0 / 63.0;
        o.B = (v & 31) * 255.0 / 31.0;
    }

    /// <summary>
    /// Mirrors assignment to a Uint8ClampedArray: round to the nearest integer
    /// (0.5 goes up), then clamp to 0..255.
    /// </summary>
    private static byte Clamp8(double x)
    {
        double r = Math.Round(x, MidpointRounding.AwayFromZero);
        if (r < 0) return 0;
        if (r > 255) return 255;
        return (byte)r;
    }

    private static uint U32(byte[] b, int o) =>
        (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
}
