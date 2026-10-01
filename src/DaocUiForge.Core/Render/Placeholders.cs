using SkiaSharp;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Stand-in shapes of the preview: everything the original solved with CSS
/// (<c>background</c>, <c>border</c>, <c>outline:dashed</c>,
/// <c>repeating-linear-gradient</c>).
///
/// This is deliberately NOT how the game looks. These shapes only appear
/// where something is missing from the package or where the engine only
/// supplies the content at run time. Without them an empty frame would sit
/// there looking like a real control — exactly the confusion the original
/// resolved with <c>.notex</c> and <c>.miss</c>.
///
/// The colour values are taken from the stylesheet of the HTML version (as
/// <c>rgba(…)</c> there) so the preview keeps its familiar look.
/// </summary>
internal static class Placeholders
{
    // --- Colours from the original stylesheet --------------------------

    /// <summary>Brass — the base tone of the preview.</summary>
    public static SKColor Brass(byte alpha) => new(200, 160, 74, alpha);

    /// <summary>Bluish tint for "texture lives in the game folder".</summary>
    public static SKColor Steel(byte alpha) => new(120, 150, 190, alpha);

    public static readonly SKColor PanelDark = new(8, 10, 14, 115);   // rgba(8,10,14,.45)
    public static readonly SKColor MissingTpl = new(255, 160, 60, 179); // rgba(255,160,60,.7)

    // --- Basic shapes -------------------------------------------------

    public static void Fill(SKCanvas canvas, SKRect r, SKColor color)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var p = new SKPaint { Color = color, IsAntialias = false };
        canvas.DrawRect(r, p);
    }

    /// <summary>
    /// A 1 px frame. Like a CSS border it sits INSIDE, hence the half pixel
    /// inset — otherwise the line would land on the edge and be halved by
    /// clipping.
    /// </summary>
    public static void Border(SKCanvas canvas, SKRect r, SKColor color,
        float[]? dash = null)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var p = new SKPaint
        {
            Color = color,
            IsStroke = true,
            StrokeWidth = 1,
            IsAntialias = false,
            PathEffect = dash is null ? null : SKPathEffect.CreateDash(dash, 0),
        };
        canvas.DrawRect(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), p);
        p.PathEffect?.Dispose();
    }

    public static void Dashed(SKCanvas canvas, SKRect r, SKColor color) =>
        Border(canvas, r, color, new[] { 3f, 2f });

    public static void Dotted(SKCanvas canvas, SKRect r, SKColor color) =>
        Border(canvas, r, color, new[] { 1f, 1f });

    /// <summary>
    /// Diagonal stripes — <c>repeating-linear-gradient(45deg, a 0 Np, b Np 2Np)</c>.
    /// Marks areas whose content only comes into being in the game (item and
    /// spell icons).
    /// </summary>
    public static void Hatch(SKCanvas canvas, SKRect r, SKColor a, SKColor b, float stripe)
    {
        if (r.Width <= 0 || r.Height <= 0 || stripe <= 0) return;

        // Gradient axis 45° towards the upper right, period = 2 × stripe width.
        float d = stripe * 2f / 1.41421356f;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(r.Left, r.Bottom),
            new SKPoint(r.Left + d, r.Bottom - d),
            new[] { a, a, b, b },
            new[] { 0f, 0.5f, 0.5f, 1f },
            SKShaderTileMode.Repeat);
        using var p = new SKPaint { Shader = shader, IsAntialias = false };
        canvas.DrawRect(r, p);
    }

    /// <summary>Vertical gradient — <c>linear-gradient(180deg, …)</c>.</summary>
    public static void VGradient(SKCanvas canvas, SKRect r, SKColor top, SKColor bottom)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(r.Left, r.Top), new SKPoint(r.Left, r.Bottom),
            new[] { top, bottom }, null, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = false };
        canvas.DrawRect(r, p);
    }

    /// <summary>A round face with a gradient — the stand-in for the compass.</summary>
    public static void RadialDisc(SKCanvas canvas, SKRect r, SKColor inner, SKColor outer,
        SKColor border)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var c = new SKPoint(r.MidX, r.MidY);
        float rad = Math.Min(r.Width, r.Height) / 2f;
        using var shader = SKShader.CreateRadialGradient(
            c, rad, new[] { inner, outer }, null, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawOval(r, p);
        using var b = new SKPaint
        {
            Color = border, IsStroke = true, StrokeWidth = 1, IsAntialias = true,
        };
        canvas.DrawOval(new SKRect(r.Left + 0.5f, r.Top + 0.5f, r.Right - 0.5f, r.Bottom - 0.5f), b);
    }

    /// <summary>
    /// A two-tone bar with a hard edge — in the original a
    /// <c>linear-gradient</c> with two identical stops, so not a gradient at
    /// all but two areas. The stand-in for a missing bar template.
    /// </summary>
    public static void SplitBar(SKCanvas canvas, SKRect r, double fraction,
        SKColor filled, SKColor rest, bool vertical)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        double f = Math.Clamp(fraction, 0, 1);
        if (vertical)
        {
            float y = r.Bottom - (float)Math.Round(r.Height * f);
            Fill(canvas, new SKRect(r.Left, r.Top, r.Right, y), rest);
            Fill(canvas, new SKRect(r.Left, y, r.Right, r.Bottom), filled);
        }
        else
        {
            float x = r.Left + (float)Math.Round(r.Width * f);
            Fill(canvas, new SKRect(r.Left, r.Top, x, r.Bottom), filled);
            Fill(canvas, new SKRect(x, r.Top, r.Right, r.Bottom), rest);
        }
    }

    // --- Composed markers ---------------------------------------------

    /// <summary>
    /// An area whose icon only the engine supplies (items, spells).
    /// Matches the stripe pattern in <c>renderEl</c> line 1578.
    /// </summary>
    public static void ContentAtRuntime(SKCanvas canvas, SKRect r)
    {
        Hatch(canvas, r, Brass(33), Brass(13), 4);
        Border(canvas, r, Brass(87));
    }

    /// <summary>Like <see cref="ContentAtRuntime"/>, but for cells in lists and grids.</summary>
    public static void ContentAtRuntimeCell(SKCanvas canvas, SKRect r, byte borderAlpha)
    {
        Hatch(canvas, r, Brass(33), Brass(13), 4);
        Border(canvas, r, Brass(borderAlpha));
    }

    /// <summary>
    /// Template present, its texture not in the package — the usual case being
    /// a reference into the game folder (<c>atlantis/…</c>). Matches
    /// <c>.el.notex</c> including the "?" in the middle.
    /// </summary>
    public static void TextureOutsidePackage(SKCanvas canvas, SKRect r)
    {
        Hatch(canvas, r, Steel(26), Steel(5), 5);
        Dashed(canvas, r, new SKColor(130, 170, 210, 140));

        if (r.Width < 8 || r.Height < 8) return;
        using var p = new SKPaint
        {
            Color = new SKColor(150, 190, 230, 191),
            TextSize = 10,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };
        var m = p.FontMetrics;
        canvas.DrawText("?", r.MidX, r.MidY + (-m.Ascent - m.Descent) / 2f, p);
    }

    /// <summary>
    /// The element points at a template the package does not contain.
    /// Matches <c>#canvasHost.showmiss .el.miss</c>.
    /// </summary>
    public static void MissingTemplate(SKCanvas canvas, SKRect r)
    {
        Fill(canvas, r, new SKColor(255, 160, 60, 23));
        Dashed(canvas, r, MissingTpl);
    }

    /// <summary>
    /// Drag zone of the title area (<c>&lt;TitleWidth&gt;/&lt;TitleHeight&gt;</c>).
    /// This is NOT a game element — the zone only moves the window with the
    /// mouse. It therefore appears only when
    /// <see cref="RenderOptions.ShowZones"/> is set; in the original the
    /// outline is fully transparent without <c>#canvasHost.showzones</c>.
    /// </summary>
    public static void TitleZone(SKCanvas canvas, SKRect r)
    {
        Fill(canvas, r, new SKColor(120, 180, 255, 13));   // rgba(120,180,255,.05)
        Dotted(canvas, r, new SKColor(120, 180, 255, 140)); // rgba(120,180,255,.55)
    }

    /// <summary>
    /// Guide around the declared window size. It only appears when elements
    /// reach beyond it — otherwise there is no way to tell where the window
    /// ends according to the XML (<c>.declframe</c>).
    ///
    /// <para>Small deviation: in the original this is a CSS <c>outline</c>, a
    /// line OUTSIDE the box. On an image that line would be cut off on the
    /// left and top, because overflow there does not create any margin.
    /// Hence it sits inside here.</para>
    /// </summary>
    public static void DeclaredFrame(SKCanvas canvas, SKRect r) =>
        Dashed(canvas, r, Brass(115));   // rgba(200,160,74,.45)

    /// <summary>
    /// The client-native tab row of a chat-style window (&lt;TabName&gt; on the
    /// WindowTemplate, distinct from &lt;TabsDef&gt;). The game paints this
    /// from a template baked into the client — no package, stock or custom,
    /// carries it, and DAoCEd keeps the names only for the property editor
    /// and XML round-trip, never for painting; the original HTML port has no
    /// code touching the tag at all. There is therefore no pixel spec to copy
    /// from anywhere in the format — this is a labelled approximation (dashed
    /// border, like the other reconstructed zones), not a reconstruction.
    /// </summary>
    public static void NativeTabBar(SKCanvas canvas, SKRect r, IReadOnlyList<string> names, int active = 0)
    {
        if (names.Count == 0 || r.Width <= 0 || r.Height <= 0) return;

        using var pInactive = new SKPaint
        {
            Color = new SKColor(230, 220, 190, 230),
            TextSize = 10,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };
        using var pActive = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 255),
            TextSize = 10,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };
        var m = pInactive.FontMetrics;
        float baseline = r.MidY + (-m.Ascent - m.Descent) / 2f;

        float x = r.Left;
        for (int i = 0; i < names.Count; i++)
        {
            if (x >= r.Right) break;
            string name = names[i];
            bool on = i == active;
            var p = on ? pActive : pInactive;
            float w = p.MeasureText(name) + 12;
            var box = new SKRect(x, r.Top, Math.Min(x + w, r.Right), r.Bottom);
            // The active tab reads as raised/pressed (solid fill, no dash);
            // the rest stay the dim outline they always were.
            Fill(canvas, box, on ? Brass(90) : Brass(46));
            if (on) Border(canvas, box, Brass(200));
            else Dashed(canvas, box, Brass(140));
            if (box.Width >= 10) canvas.DrawText(name, box.MidX, baseline, p);
            x = box.Right;
        }
    }
}
