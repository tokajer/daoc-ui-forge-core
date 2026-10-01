using SkiaSharp;

namespace DaocUiForge.Core.Render;

/// <summary>
/// A line of text in a declared font, drawn the way a label is — the same
/// <see cref="FontProvider"/>, the same half-leading, the same shadow. Neither
/// the HTML original nor DAoCEd has anything of the kind; there the only way to
/// see what a font name means is to find a window that uses it.
///
/// <para><b>Why it is in Core.</b> <see cref="TextDrawer"/> is internal on
/// purpose — it is the drawing layer's own arithmetic and not an API. A preview
/// that reimplemented it in the interface would be a second answer to "what does
/// this font look like", and the two would drift. This is the one route.</para>
/// </summary>
public static class FontPreview
{
    /// <summary>
    /// The sample line. DAoCEd's own font dialog uses a pangram in the game's
    /// vocabulary and that is worth keeping: it exercises ascenders, descenders
    /// and capitals, which is what a line height is judged on.
    /// </summary>
    public const string SampleText = "The quick green Lurikeen jumped over the lazy Troll";

    /// <summary>Padding around the text, so the shadow is not clipped.</summary>
    private const int Pad = 4;

    /// <summary>
    /// Draw the sample. The caller owns the bitmap.
    /// </summary>
    /// <param name="width">Width of the image; the text is not wrapped.</param>
    public static SKBitmap Draw(RenderContext ctx, string? fontName, string? text = null,
        int width = 560)
    {
        string shown = string.IsNullOrEmpty(text) ? SampleText : text;

        float line = ctx.Fonts.LinePx(fontName);
        int w = Math.Max(16, width);
        int h = (int)Math.Ceiling(Math.Max(line, 8)) + 2 * Pad;

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        TextDrawer.Draw(canvas, ctx.Fonts, fontName, shown,
            new SKRect(Pad, Pad, w - Pad, h - Pad),
            new SKColor(0xd8, 0xc9, 0xa4));

        return bmp;
    }
}
