using SkiaSharp;

namespace DaocUiForge.Core.Formats;

/// <summary>
/// Builds an <see cref="SKBitmap"/> out of an RGBA byte buffer. Equivalent to
/// <c>putImageData</c> of the HTML original, including the vertical flip for
/// TGA files whose origin is bottom-left.
/// </summary>
internal static class BitmapBuilder
{
    /// <param name="rgba">Pixels as R,G,B,A, 4 bytes each, row by row top→bottom.</param>
    /// <param name="flipVertical">Reverse the row order (bottom-left origin).</param>
    public static SKBitmap FromRgba(byte[] rgba, int w, int h, bool flipVertical)
    {
        // Unpremultiplied, because the source data is straight alpha.
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bmp = new SKBitmap(info);

        int stride = w * 4;
        unsafe
        {
            byte* dst = (byte*)bmp.GetPixels().ToPointer();
            for (int y = 0; y < h; y++)
            {
                int srcY = flipVertical ? (h - 1 - y) : y;
                int srcOff = srcY * stride;
                int dstOff = y * stride;
                for (int x = 0; x < stride; x++)
                    dst[dstOff + x] = rgba[srcOff + x];
            }
        }
        return bmp;
    }
}
