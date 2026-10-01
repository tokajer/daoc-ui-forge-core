using System.Xml.Linq;
using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>
/// The stretchable image templates of DAoC. Ported from
/// <c>drawHResize</c>, <c>drawVResize</c>, <c>drawFullResize</c> and
/// <c>drawArea</c> of the HTML original.
///
/// Core rule: the stretchable areas are TILED, not
/// stretched. A stretched frame looks wrong immediately, because its
/// ornamental seams smear.
///
/// Every method draws in local coordinates starting at (0,0) and reports back
/// whether anything was drawn at all. That corresponds to the
/// <c>childElementCount</c> check in the original, which is what decides
/// there whether a placeholder is needed.
/// </summary>
public static class NineSlice
{
    /// <summary>
    /// The texture a template draws from. Normally <c>&lt;TextureName&gt;</c>,
    /// either inside the <c>&lt;Texture&gt;</c> block or beside it — but
    /// <c>ListBoxHeaderTemplate</c> spells it <c>&lt;Texture&gt;&lt;Name&gt;</c>
    /// instead, and it is the only template type that does:
    /// DAoCEd's <c>ListboxheadertemplateNode.loadTexture</c> reads
    /// <c>new StringProperty(e, "Name")</c> where all nine other node classes
    /// read "TextureName". Missing that spelling left every column header of
    /// <c>community_window</c> as a hatched "texture not found" cell.
    ///
    /// <para>The fallback is only taken INSIDE a <c>&lt;Texture&gt;</c> block,
    /// so it cannot collide with the <c>&lt;Name&gt;</c> that carries the
    /// template's own name one level up.</para>
    /// </summary>
    public static string TextureNameOf(XElement? tpl)
    {
        var t = Xml.Sub(tpl, "Texture");
        string n = Xml.Tx(t, "TextureName");
        if (n.Length == 0 && t is not null) n = Xml.Tx(t, "Name");
        if (n.Length == 0) n = Xml.Tx(tpl, "TextureName");
        return n;
    }

    /// <summary>
    /// Which of the four functions a template wants, read off its
    /// &lt;Texture&gt; block rather than off its type name. There are around
    /// forty template types and no list of them anywhere, and the
    /// point names below are exactly the ones the functions read — so this
    /// cannot disagree with what they then do.
    /// </summary>
    public static TemplateShape ShapeOf(XElement? tpl)
    {
        if (tpl is null) return TemplateShape.None;

        var t = Xml.Sub(tpl, "Texture");
        if (t is not null)
        {
            if (Pt(t, "MiddleMiddle") is not null) return TemplateShape.FullResize;
            if (Pt(t, "Left") is not null && Pt(t, "Right") is not null) return TemplateShape.HResize;
            if (Pt(t, "Top") is not null && Pt(t, "Bottom") is not null) return TemplateShape.VResize;
        }
        else if (Xml.Tx(tpl, "TextureName").Length > 0 && Pt(tpl, "TextureStart") is not null)
        {
            return TemplateShape.IconSheet;
        }

        string tex = TextureNameOf(tpl);
        return tex.Length == 0 || tex == "none" ? TemplateShape.None : TemplateShape.Area;
    }

    /// <summary>
    /// Draw a template the way its own shape says. For a template named by
    /// another one — a list box's &lt;BackgroundTemplate&gt;, a slider's
    /// &lt;BackgroundVResizeTemplate&gt; — the caller cannot know which of the
    /// four it is, and guessing <see cref="DrawArea"/> is what made a
    /// nine-field background come out as a stamp of its top-left corner.
    /// </summary>
    public static bool DrawTemplate(SKCanvas canvas, TextureCache tex, XElement? tpl,
        double W, double H, string? stateKey = null) =>
        ShapeOf(tpl) switch
        {
            TemplateShape.FullResize => DrawFullResize(canvas, tex, tpl, W, H),
            TemplateShape.HResize => DrawHResize(canvas, tex, tpl, W, H),
            TemplateShape.VResize => DrawVResize(canvas, tex, tpl, W, H),
            _ => DrawArea(canvas, tex, tpl, W, H, stateKey),
        };

    /// <summary>Three-part image, stretchable across (left/repeat/right).</summary>
    public static bool DrawHResize(SKCanvas canvas, TextureCache tex, XElement? tpl, double W, double H)
    {
        var t = Xml.Sub(tpl, "Texture");
        if (t is null) return false;

        string texName = TextureNameOf(tpl);
        double h = Num(Xml.Tx(tpl, "Height"), H);
        if (h == 0) h = H;

        double lw = Num(Xml.Tx(tpl, "LeftWidth"));
        double rw = Num(Xml.Tx(tpl, "RightWidth"));
        double rp = Num(Xml.Tx(tpl, "RepeatWidth"), 1);
        if (rp == 0) rp = 1;

        var left = Pt(t, "Left");
        var repeat = Pt(t, "Repeat");
        var right = Pt(t, "Right");

        double total = Math.Max(0, W);
        double mid = Math.Max(0, total - lw - rw);
        bool drew = false;

        if (lw > 0 && left is { } l)
            drew |= tex.Draw(canvas, texName, l.X, l.Y, (float)lw, (float)h,
                new SKRect(0, 0, (float)lw, (float)h));

        if (mid > 0 && repeat is { } r)
        {
            // draw tiled (the engine repeats)
            double x = lw, remaining = mid;
            while (remaining > 0)
            {
                double w = Math.Min(rp, remaining);
                drew |= tex.Draw(canvas, texName, r.X, r.Y, (float)w, (float)h,
                    new SKRect((float)x, 0, (float)(x + w), (float)h));
                x += w;
                remaining -= w;
            }
        }

        if (rw > 0 && right is { } rt)
            drew |= tex.Draw(canvas, texName, rt.X, rt.Y, (float)rw, (float)h,
                new SKRect((float)(lw + mid), 0, (float)(lw + mid + rw), (float)h));

        return drew;
    }

    /// <summary>Three-part image, stretchable down (top/repeat/bottom).</summary>
    public static bool DrawVResize(SKCanvas canvas, TextureCache tex, XElement? tpl, double W, double H)
    {
        var t = Xml.Sub(tpl, "Texture");
        if (t is null) return false;

        string texName = TextureNameOf(tpl);
        double w = Num(Xml.Tx(tpl, "Width"), W);
        if (w == 0) w = W;

        double th = Num(Xml.Tx(tpl, "TopHeight"));
        double bh = Num(Xml.Tx(tpl, "BottomHeight"));
        double rp = Num(Xml.Tx(tpl, "RepeatHeight"), 1);
        if (rp == 0) rp = 1;

        var top = Pt(t, "Top");
        var repeat = Pt(t, "Repeat");
        var bottom = Pt(t, "Bottom");

        double mid = Math.Max(0, H - th - bh);
        bool drew = false;

        if (th > 0 && top is { } tp)
            drew |= tex.Draw(canvas, texName, tp.X, tp.Y, (float)w, (float)th,
                new SKRect(0, 0, (float)w, (float)th));

        if (mid > 0 && repeat is { } r)
        {
            double y = th, remaining = mid;
            while (remaining > 0)
            {
                double hh = Math.Min(rp, remaining);
                drew |= tex.Draw(canvas, texName, r.X, r.Y, (float)w, (float)hh,
                    new SKRect(0, (float)y, (float)w, (float)(y + hh)));
                y += hh;
                remaining -= hh;
            }
        }

        if (bh > 0 && bottom is { } b)
            drew |= tex.Draw(canvas, texName, b.X, b.Y, (float)w, (float)bh,
                new SKRect(0, (float)(th + mid), (float)w, (float)(th + mid + bh)));

        return drew;
    }

    /// <summary>
    /// Fully stretchable image made of nine fields. The corners stay fixed;
    /// edges and centre are tiled.
    /// </summary>
    public static bool DrawFullResize(SKCanvas canvas, TextureCache tex, XElement? tpl, double W, double H)
    {
        var t = Xml.Sub(tpl, "Texture");
        if (t is null) return false;

        string texName = TextureNameOf(tpl);
        double lw = Num(Xml.Tx(tpl, "LeftWidth"));
        double rw = Num(Xml.Tx(tpl, "RightWidth"));
        double th = Num(Xml.Tx(tpl, "TopHeight"));
        double bh = Num(Xml.Tx(tpl, "BottomHeight"));

        // Tile size of the centre: <MiddleWidth>, else <CenterWidth>, else 1.
        double cw = Num(Xml.Tx(tpl, "MiddleWidth"));
        if (cw == 0) cw = Num(Xml.Tx(tpl, "CenterWidth"), 1);
        if (cw == 0) cw = 1;
        double ch = Num(Xml.Tx(tpl, "MiddleHeight"));
        if (ch == 0) ch = Num(Xml.Tx(tpl, "CenterHeight"), 1);
        if (ch == 0) ch = 1;

        double midW = Math.Max(0, W - lw - rw);
        double midH = Math.Max(0, H - th - bh);

        // field, target position, target size, tile size in the source
        (string Key, double X, double Y, double W, double H, double Sw, double Sh)[] parts =
        {
            ("TopLeft",      0,       0,       lw,   th,   lw, th),
            ("TopMiddle",    lw,      0,       midW, th,   cw, th),
            ("TopRight",     lw+midW, 0,       rw,   th,   rw, th),
            ("MiddleLeft",   0,       th,      lw,   midH, lw, ch),
            ("MiddleMiddle", lw,      th,      midW, midH, cw, ch),
            ("MiddleRight",  lw+midW, th,      rw,   midH, rw, ch),
            ("BottomLeft",   0,       th+midH, lw,   bh,   lw, bh),
            ("BottomMiddle", lw,      th+midH, midW, bh,   cw, bh),
            ("BottomRight",  lw+midW, th+midH, rw,   bh,   rw, bh),
        };

        bool drew = false;
        foreach (var part in parts)
        {
            if (part.W <= 0 || part.H <= 0) continue;
            if (part.Sw <= 0 || part.Sh <= 0) continue;
            var p = Pt(t, part.Key);
            if (p is not { } src) continue;

            // Tiling: the last tile is clipped, not squeezed.
            for (double yy = 0; yy < part.H; yy += part.Sh)
            for (double xx = 0; xx < part.W; xx += part.Sw)
            {
                double dw = Math.Min(part.Sw, part.W - xx);
                double dh = Math.Min(part.Sh, part.H - yy);
                float dx = (float)(part.X + xx), dy = (float)(part.Y + yy);
                drew |= tex.Draw(canvas, texName, src.X, src.Y, (float)dw, (float)dh,
                    new SKRect(dx, dy, dx + (float)dw, dy + (float)dh));
            }
        }
        return drew;
    }

    /// <summary>
    /// A plain texture slice (ImageArea / Button / Icon).
    /// <paramref name="stateKey"/> picks the state (e.g. "Normal",
    /// "Pressed"); without it the order of the original applies.
    /// </summary>
    public static bool DrawArea(SKCanvas canvas, TextureCache tex, XElement? tpl,
        double W, double H, string? stateKey = null)
    {
        if (tpl is null) return false;

        // Some templates carry a <Texture> block, others the fields directly.
        var t = Xml.Sub(tpl, "Texture") ?? tpl;
        string texName = TextureNameOf(tpl);

        // "none" means deliberately invisible (pure click areas, say).
        if (texName.Length == 0 || texName == "none") return false;

        // Is the texture declared at all? If not, into the report with it.
        if (!tex.IsDeclared(texName)) tex.MarkMissing(texName);

        var size = Pt(tpl, "Size");
        double w = W != 0 ? W : (size?.X ?? 0);
        double h = H != 0 ? H : (size?.Y ?? 0);

        SKPoint? p = null;
        foreach (var key in new[] { stateKey, "Normal", "TopLeft", "Position", "Offset" })
        {
            if (string.IsNullOrEmpty(key)) continue;
            p = Pt(t, key) ?? Pt(tpl, key);
            if (p is not null) break;
        }
        p ??= new SKPoint((float)Num(Xml.Tx(t, "X")), (float)Num(Xml.Tx(t, "Y")));

        double sw = size?.X ?? w;
        double sh = size?.Y ?? h;
        if (sw == 0) sw = w;
        if (sh == 0) sh = h;

        return tex.Draw(canvas, texName, p.Value.X, p.Value.Y, (float)sw, (float)sh,
            new SKRect(0, 0, (float)w, (float)h));
    }
}
