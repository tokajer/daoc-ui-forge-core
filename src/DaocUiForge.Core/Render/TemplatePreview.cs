using System.Xml.Linq;
using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>How a template puts pixels on a surface.</summary>
public enum TemplateShape
{
    /// <summary>It draws nothing by itself and names nothing that does.</summary>
    None,

    /// <summary>One rectangle out of a texture (<see cref="NineSlice.DrawArea"/>).</summary>
    Area,

    /// <summary>Left / repeat / right (<see cref="NineSlice.DrawHResize"/>).</summary>
    HResize,

    /// <summary>Top / repeat / bottom (<see cref="NineSlice.DrawVResize"/>).</summary>
    VResize,

    /// <summary>Nine fields (<see cref="NineSlice.DrawFullResize"/>).</summary>
    FullResize,

    /// <summary>A sprite sheet with <c>MaxLevels</c> levels, as a StatusIconTemplate has.</summary>
    IconSheet,
}

/// <summary>What to draw for a template, and how large.</summary>
/// <param name="Shape">Which of the drawing functions applies.</param>
/// <param name="Node">
/// The template that actually carries the texture. Not necessarily the one that
/// was asked about, see <paramref name="Through"/>.
/// </param>
/// <param name="Width">The size the template comes out at, unstretched.</param>
/// <param name="Height">See <paramref name="Width"/>.</param>
/// <param name="Through">
/// Empty when the template draws itself. Otherwise the tag it was reached
/// through: a StatusBarTemplate carries no texture and names one under
/// &lt;ForegroundHResizeTemplate&gt;.
/// </param>
public sealed record TemplatePreviewPlan(
    TemplateShape Shape, XElement Node, double Width, double Height, string Through = "");

/// <summary>
/// A picture of a single template, outside any window. The HTML original has no
/// counterpart: its template pane lists fields and nothing else, so the only way
/// to see what a name means is to find a window that uses it.
///
/// <para><b>The shape is read off the XML, not off the type name.</b> There are
/// around forty template types and no list of them anywhere, so a
/// table keyed by type would be wrong for the next package. Which point names
/// the &lt;Texture&gt; block carries says the same thing and says it for types
/// nobody has seen yet, and they are exactly the names
/// <see cref="NineSlice"/> reads, so the preview cannot disagree with the
/// window.</para>
///
/// <para><b>One level of indirection.</b> A good third of the templates in a
/// real package carry no texture at all and only name others: 146 of the 1086
/// in the reference package are StatusBarTemplates pointing at a pair of
/// horizontal bars. Following the first named template that does draw turns
/// "no preview" from 261 templates into 40. It goes exactly one level: a chain
/// would need the composition rules of the drawing layer, and those belong to
/// the element that assembles them.</para>
/// </summary>
public static class TemplatePreview
{
    /// <summary>
    /// The largest preview drawn. An emergency bound like
    /// <see cref="WindowRenderer.MaxSurface"/>: a mistyped &lt;Size&gt; would
    /// otherwise allocate hundreds of megabytes for a thumbnail. The widest
    /// template of the reference package is 659 px.
    /// </summary>
    public const int MaxSide = 2048;

    /// <summary>What would be drawn for this template.</summary>
    public static TemplatePreviewPlan Plan(Package pkg, XElement tpl)
    {
        var own = Direct(tpl);
        if (own.Shape != TemplateShape.None) return own;

        foreach (var child in tpl.Elements())
        {
            if (child.HasElements) continue;

            string name = (child.Value ?? "").Trim();
            if (name.Length == 0 || name == "none") continue;
            if (!pkg.ByNameLc.TryGetValue(name, out var other)) continue;
            if (ReferenceEquals(other, tpl)) continue;

            var plan = Direct(other);
            if (plan.Shape != TemplateShape.None)
                return plan with { Through = child.Name.LocalName };
        }

        return own;
    }

    /// <summary>
    /// Draw the plan. Null when nothing came of it: the texture is missing from
    /// the package, unreadable, or points into the game folder.
    /// The caller owns the bitmap.
    /// </summary>
    public static SKBitmap? Render(TextureCache tex, TemplatePreviewPlan plan)
    {
        if (plan.Shape == TemplateShape.None) return null;

        int w = (int)Math.Ceiling(plan.Width);
        int h = (int)Math.Ceiling(plan.Height);
        if (w <= 0 || h <= 0 || w > MaxSide || h > MaxSide) return null;

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        bool drew = plan.Shape switch
        {
            TemplateShape.FullResize => NineSlice.DrawFullResize(canvas, tex, plan.Node, w, h),
            TemplateShape.HResize => NineSlice.DrawHResize(canvas, tex, plan.Node, w, h),
            TemplateShape.VResize => NineSlice.DrawVResize(canvas, tex, plan.Node, w, h),
            TemplateShape.IconSheet => IconSheet(canvas, tex, plan.Node, w, h),
            // "Normal" is the resting state of a button; a template without one
            // falls through to <TopLeft> inside DrawArea.
            _ => NineSlice.DrawArea(canvas, tex, plan.Node, w, h, "Normal"),
        };

        if (drew) return bmp;

        bmp.Dispose();
        return null;
    }

    /// <summary>Plan and draw in one go.</summary>
    public static SKBitmap? Render(RenderContext ctx, XElement tpl) =>
        Render(ctx.Textures, Plan(ctx.Package, tpl));

    // -----------------------------------------------------------------
    // Which shape, and how large
    // -----------------------------------------------------------------

    /// <summary>What the template draws by itself, ignoring what it names.</summary>
    private static TemplatePreviewPlan Direct(XElement tpl)
    {
        // The shape decision is NineSlice's, so a preview and a window cannot
        // disagree about what a template is. Only the natural size is worked
        // out here, and that is a question the drawing layer never asks.
        var shape = NineSlice.ShapeOf(tpl);

        if (shape == TemplateShape.FullResize)
        {
            double lw = Num(Xml.Tx(tpl, "LeftWidth"));
            double rw = Num(Xml.Tx(tpl, "RightWidth"));
            double th = Num(Xml.Tx(tpl, "TopHeight"));
            double bh = Num(Xml.Tx(tpl, "BottomHeight"));

            // The tile of the centre, read the way DrawFullResize reads it. One
            // tile is the natural size: more would be a stretch nobody asked for.
            double cw = Num(Xml.Tx(tpl, "MiddleWidth"));
            if (cw == 0) cw = Num(Xml.Tx(tpl, "CenterWidth"), 1);
            if (cw == 0) cw = 1;
            double ch = Num(Xml.Tx(tpl, "MiddleHeight"));
            if (ch == 0) ch = Num(Xml.Tx(tpl, "CenterHeight"), 1);
            if (ch == 0) ch = 1;

            return new TemplatePreviewPlan(TemplateShape.FullResize, tpl, lw + cw + rw, th + ch + bh);
        }

        if (shape == TemplateShape.HResize)
        {
            double rp = Num(Xml.Tx(tpl, "RepeatWidth"), 1);
            if (rp == 0) rp = 1;
            double w = Num(Xml.Tx(tpl, "LeftWidth")) + rp + Num(Xml.Tx(tpl, "RightWidth"));
            return new TemplatePreviewPlan(TemplateShape.HResize, tpl, w,
                Nz(Xml.Tx(tpl, "Height"), 16));
        }

        if (shape == TemplateShape.VResize)
        {
            double rp = Num(Xml.Tx(tpl, "RepeatHeight"), 1);
            if (rp == 0) rp = 1;
            double h = Num(Xml.Tx(tpl, "TopHeight")) + rp + Num(Xml.Tx(tpl, "BottomHeight"));
            return new TemplatePreviewPlan(TemplateShape.VResize, tpl,
                Nz(Xml.Tx(tpl, "Width"), 16), h);
        }

        // A sprite sheet: <TextureStart> is the origin and <Width>/<Height> one
        // level. DrawArea would not find that start point - it looks for
        // <Normal>, <TopLeft>, <Position> and <Offset> and none of them is here.
        if (shape == TemplateShape.IconSheet)
            return new TemplatePreviewPlan(TemplateShape.IconSheet, tpl,
                Nz(Xml.Tx(tpl, "Width"), 16), Nz(Xml.Tx(tpl, "Height"), 16));

        if (shape == TemplateShape.None)
            return new TemplatePreviewPlan(TemplateShape.None, tpl, 0, 0);

        var size = Pt(tpl, "Size");
        double aw = size?.X ?? Num(Xml.Tx(tpl, "Width"));
        double ah = size?.Y ?? Num(Xml.Tx(tpl, "Height"));
        if (aw <= 0 || ah <= 0)
            return new TemplatePreviewPlan(TemplateShape.None, tpl, 0, 0);

        return new TemplatePreviewPlan(TemplateShape.Area, tpl, aw, ah);
    }

    /// <summary>
    /// The full level of a sprite sheet, which is what the drawing layer shows
    /// without sample data (<c>ChooseLevel</c>). Level 0 is the empty one on
    /// most of these, and an empty preview says nothing about the template.
    /// </summary>
    private static bool IconSheet(SKCanvas c, TextureCache tex, XElement tpl, double w, double h)
    {
        string name = Xml.Tx(tpl, "TextureName");
        var start = Pt(tpl, "TextureStart") ?? new SKPoint(0, 0);
        int levels = (int)Math.Max(1, Num(Xml.Tx(tpl, "MaxLevels"), 1));
        bool horizontal = Xml.Tx(tpl, "Horizontal") == "true";

        int level = levels - 1;
        float sx = start.X + (horizontal ? (float)(level * w) : 0);
        float sy = start.Y + (horizontal ? 0 : (float)(level * h));

        return tex.Draw(c, name, sx, sy, (float)w, (float)h,
            new SKRect(0, 0, (float)w, (float)h));
    }

    private static double Nz(string? s, double fallback)
    {
        double v = Num(s);
        return v != 0 ? v : fallback;
    }
}
