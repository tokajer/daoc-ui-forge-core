using SkiaSharp;

namespace DaocUiForge.Core.Render;

internal enum HAlign { Start, Center, End }

internal enum VAlign { Top, Center }

/// <summary>
/// Draws labels the way the CLIENT lays them out: the first baseline sits one
/// whole ascender below the top of the element, and every further line one
/// line advance below that. No CSS line box, no half-leading.
///
/// The HTML original had a flex box with <c>line-height</c> = the font's line
/// height and centred the text inside it, because a browser has no way to be
/// told a baseline. Measured against the running game that puts the text one
/// to three pixels too high on top of drawing it too small.
///
/// The ascender is rounded UP to whole pixels, which is what a rasteriser
/// hands back for an integer pixel size (FreeType's <c>FT_PIX_CEIL</c>, GDI's
/// <c>tmAscent</c>). Without the rounding the four labels of summary.xml land
/// one pixel high.
/// </summary>
internal static class TextDrawer
{
    /// <summary>Label shadow: 1 px offset, black at about 85 % opacity.</summary>
    public const byte DefaultShadowAlpha = 217;

    /// <returns>
    /// Width of the widest line. Callers need it for elements without
    /// &lt;Width&gt;: in the original the text content determined the width of
    /// the box there (CSS shrink-to-fit).
    /// </returns>
    public static float Draw(SKCanvas canvas, FontProvider fonts, string? fontName,
        string text, SKRect box, SKColor color,
        HAlign hAlign = HAlign.Start, VAlign vAlign = VAlign.Center,
        float padLeft = 0, float padRight = 0, byte shadowAlpha = DefaultShadowAlpha,
        float lineHeight = 0)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        using var paint = new SKPaint
        {
            Typeface = fonts.Typeface(fontName),
            TextSize = fonts.SizePx(fontName),
            Color = color,
            IsAntialias = true,
            SubpixelText = true,
        };

        // A different line height: the chat sets line-height:1.25 instead of
        // the font's own line height.
        float line = lineHeight > 0 ? lineHeight : fonts.LinePx(fontName);
        float ascent = MathF.Ceiling(-paint.FontMetrics.Ascent);

        // white-space:pre — line breaks count, but nothing is wrapped.
        var lines = text.Split('\n');
        float blockTop = vAlign == VAlign.Center
            ? box.Top + (box.Height - lines.Length * line) / 2f
            : box.Top;

        float widest = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string s = lines[i];
            if (s.Length == 0) continue;

            float width = paint.MeasureText(s);
            if (width > widest) widest = width;
            float x = hAlign switch
            {
                HAlign.Center => box.Left + (box.Width - width) / 2f,
                HAlign.End => box.Right - width - padRight,
                _ => box.Left + padLeft,
            };
            float baseline = blockTop + i * line + ascent;

            if (shadowAlpha > 0)
            {
                var keep = paint.Color;
                paint.Color = new SKColor(0, 0, 0, shadowAlpha);
                canvas.DrawText(s, x + 1, baseline + 1, paint);
                paint.Color = keep;
            }
            canvas.DrawText(s, x, baseline, paint);
        }
        return widest;
    }

    /// <summary>A run of text with its own colour inside one line.</summary>
    /// <param name="Text">The content.</param>
    /// <param name="Color">Colour of this run.</param>
    /// <param name="Underline">Underlined (hotspots in the original).</param>
    public readonly record struct Run(string Text, SKColor Color, bool Underline = false);

    /// <summary>
    /// Lay several runs out one after another on a single line. The text area
    /// needs this for &lt;HasHotspots&gt;: the colour changes mid-line there
    /// without the text flow changing.
    /// </summary>
    public static void DrawRuns(SKCanvas canvas, FontProvider fonts, string? fontName,
        IReadOnlyList<Run> runs, SKRect box, VAlign vAlign = VAlign.Top,
        byte shadowAlpha = 0)
    {
        if (runs.Count == 0) return;

        using var paint = new SKPaint
        {
            Typeface = fonts.Typeface(fontName),
            TextSize = fonts.SizePx(fontName),
            IsAntialias = true,
            SubpixelText = true,
        };

        float line = fonts.LinePx(fontName);
        float baseline = box.Top + MathF.Ceiling(-paint.FontMetrics.Ascent);
        if (vAlign == VAlign.Center) baseline += (box.Height - line) / 2f;

        float x = box.Left;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            float w = paint.MeasureText(run.Text);

            if (shadowAlpha > 0)
            {
                paint.Color = new SKColor(0, 0, 0, shadowAlpha);
                canvas.DrawText(run.Text, x + 1, baseline + 1, paint);
            }
            paint.Color = run.Color;
            canvas.DrawText(run.Text, x, baseline, paint);

            if (run.Underline)
            {
                using var u = new SKPaint
                {
                    Color = run.Color, IsStroke = true, StrokeWidth = 1, IsAntialias = false,
                };
                float uy = MathF.Round(baseline + 1.5f) + 0.5f;
                canvas.DrawLine(x, uy, x + w, uy, u);
            }
            x += w;
            if (x > box.Right) break;
        }
    }
}
