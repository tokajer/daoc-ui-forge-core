using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Image and icon elements. Ported from <c>renderEl</c>, branches
/// <c>FullResizeImageDef</c> … <c>DockableIconDef</c>.
/// </summary>
public static partial class ElementRenderer
{
    /// <summary>Full-surface background made of nine fields (original 1309).</summary>
    private static void FullResizeImage(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("FullResizeImageTemplate");
        if (s.W == 0) s.W = s.WinW;
        if (s.H == 0) s.H = s.WinH;
        s.Drew |= NineSlice.DrawFullResize(c, s.Tex, tpl, s.W, s.H);
    }

    /// <summary>
    /// Horizontally stretchable image, also used as a button (original 1315).
    /// </summary>
    private static void HorizontalResize(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("HorizontalResizeImageTemplate",
            "HorizontalResizeButtonTemplate", "ButtonTemplate");

        // Read the font name from the ORIGINAL template — it is about to be
        // replaced, and the replacement carries no font (original 1321).
        string fontFromTpl = Xml.NameOf(Xml.Sub(tpl, "Font"));

        // HorizontalResizeButtonTemplate does not draw itself; it points
        // through <Normal> at a HorizontalResizeImageTemplate.
        if (tpl is not null && tpl.Name.LocalName == "HorizontalResizeButtonTemplate")
        {
            double h0 = Num(Xml.Tx(tpl, "Height"));
            string inner = First(tpl, "Normal", "NormalHighlit", "Pressed");
            var res = s.Find(inner, "HorizontalResizeImageTemplate");
            if (res is not null) tpl = res;
            if (s.H == 0 && h0 != 0) s.H = h0;
        }
        if (s.H == 0) s.H = NumNz(Xml.Tx(tpl, "Height"), 16);

        s.Drew |= NineSlice.DrawHResize(c, s.Tex, tpl, s.W != 0 ? s.W : s.WinW, s.H);

        // Composite buttons (emoticons, say) additionally carry an icon from
        // <ImageAreaTemplateName>, offset by <ImageOffset>.
        string iconName = Xml.Tx(s.Def, "ImageAreaTemplateName");
        if (iconName.Length > 0 && iconName != "none")
        {
            var iTpl = s.Find(iconName, "ImageAreaTemplate", "IconTemplate");
            var isz = Pt(iTpl, "Size");
            double iw = isz?.X ?? 16, ih = isz?.Y ?? 16;
            var io = Pt(s.Def, "ImageOffset") ?? new SKPoint(0, 0);
            // Without a vertical offset the icon sits centred (original 1343).
            double iy = io.Y != 0 ? io.Y : JsRound((s.H - ih) / 2);

            c.Save();
            c.ClipRect(new SKRect((float)io.X, (float)iy,
                (float)(io.X + iw), (float)(iy + ih)));
            c.Translate((float)io.X, (float)iy);
            s.Drew |= NineSlice.DrawArea(c, s.Tex, iTpl, iw, ih);
            c.Restore();
        }

        if (s.Text.Length > 0)
        {
            // <LabelAlignment> is a block of flags (not a text value) and
            // occurs in two spellings.
            var la = Xml.Sub(s.Def, "LabelAlignment") ?? Xml.Sub(s.Def, "Labelalignment");
            bool On(string k) => la is not null && Flag(la, k);
            double ind = Num(Xml.Tx(s.Def, "LabelIndent"));

            var hAlign = HAlign.Center;
            var vAlign = VAlign.Center;
            if (On("TopLeft")) { hAlign = HAlign.Start; vAlign = VAlign.Top; }
            if (On("EndAligned") || On("Right")) hAlign = HAlign.End;
            if (On("CenterHorizontally")) hAlign = HAlign.Center;
            if (On("CenterVertically")) vAlign = VAlign.Center;

            float padL = 0, padR = 0;
            if (ind != 0)
            {
                if (hAlign == HAlign.End) padR = (float)ind;
                else padL = (float)ind;
            }

            TextDrawer.Draw(c, s.Fonts,
                s.FontName.Length > 0 ? s.FontName : fontFromTpl,
                s.Text, s.Box,
                ColorOf(s.Def, "Color", TextDefault),
                hAlign, vAlign, padL, padR);
        }
    }

    /// <summary>Vertically stretchable image (original 1372).</summary>
    private static void VerticalResize(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("VerticalResizeImageTemplate");
        if (s.W == 0) s.W = NumNz(Xml.Tx(tpl, "Width"), 16);
        if (s.H == 0) s.H = s.WinH;
        s.Drew |= NineSlice.DrawVResize(c, s.Tex, tpl, s.W, s.H);
    }

    /// <summary>
    /// A plain image area (original 1379). <c>DynamicImageDef</c> brings its
    /// texture and measurements along and needs no template.
    /// </summary>
    private static void ImageArea(SKCanvas c, ElState s)
    {
        string ownTex = Xml.Tx(s.Def, "TextureName");
        if (s.Tag == "DynamicImageDef" && ownTex.Length > 0)
        {
            var dim = Pt(s.Def, "Dimensions");
            var tc = Pt(s.Def, "TextureCoords") ?? new SKPoint(0, 0);
            if (s.W == 0) s.W = dim?.X ?? 0;
            if (s.H == 0) s.H = dim?.Y ?? 0;
            double w = s.W != 0 ? s.W : 16, h = s.H != 0 ? s.H : 16;
            s.Drew |= s.Tex.Draw(c, ownTex, tc.X, tc.Y, (float)w, (float)h,
                new SKRect(0, 0, (float)w, (float)h));
            return;
        }

        var tpl = s.Tpl("ImageAreaTemplate", "IconTemplate");
        var size = Pt(tpl, "Size");
        if (s.W == 0 && size is not null) s.W = size.Value.X;
        if (s.H == 0 && size is not null) s.H = size.Value.Y;
        s.Drew |= NineSlice.DrawArea(c, s.Tex, tpl, s.W, s.H);
    }

    /// <summary>
    /// An image the client loads from a file while it runs (DAoCEd's
    /// <c>StaticfileimagedefNode</c>). The HTML original has no branch for it,
    /// which left it the one control type the drawing layer did not know: it
    /// fell through to <c>default: return null</c> and was not on screen at
    /// all, not even as a box to click.
    ///
    /// <para><b>Nothing about its content is in the package.</b> There is no
    /// template and no texture name — <c>&lt;CanvasName&gt;</c> names a surface
    /// the client fills, and DAoCEd's node carries that one property and
    /// nothing else. So the honest drawing is the run-time stand-in over
    /// whatever size the element declares: the same answer the icons get when
    /// only the engine knows what goes in them.</para>
    ///
    /// <para>Its size is therefore the element's own
    /// <c>&lt;Width&gt;</c>/<c>&lt;Height&gt;</c>, or the window through
    /// <c>GrowWidth</c>/<c>GrowHeight</c> — both settled before the dispatch.
    /// No default is invented: a size guessed here would be a size the game
    /// does not use.</para>
    /// </summary>
    private static void StaticFileImage(SKCanvas c, ElState s)
    {
        Placeholders.ContentAtRuntime(c, s.Box);
        s.ShowedSubstitute = true;
    }

    /// <summary>
    /// Icons of every kind (original 1508). The intricate part is choosing the
    /// sprite level for multi-level <c>StatusIconTemplate</c>.
    /// </summary>
    private static void Icon(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("StatusIconTemplate", "IconTemplate", "ImageAreaTemplate");
        int forceLevel = 0;

        /* Simulating a state.
           Effect icons (mez, diz, poison, nearsight) all live in the same
           texture, one row per effect via TextureStart. Which effect shows is
           decided in the game by the LEVEL the adapter supplies (e.g.
           group_status0) — not by showing and hiding.
           Measured off status_effect_icon.tga:
             level 1 = mez (cyan), 2 = diz (orange),
             level 3 = poison (green), 4 = nearsight (purple), 0 = empty. */
        int effect = EffectOf(s.TplName);
        if (effect != 0)
        {
            // With no state chosen nothing is active (as with "healthy" in the game).
            if (s.Opt.SimulatedState != effect) { s.Hidden = true; return; }
            forceLevel = effect;   // draw exactly the level the icon carries
        }

        bool drewInner;
        if (tpl is not null && tpl.Name.LocalName == "StatusIconTemplate")
        {
            // Sprite sheet: <TextureStart> is the origin, <Width>/<Height>
            // one level. MaxLevels levels sit next to each other, across or
            // down.
            string texName = Xml.Tx(tpl, "TextureName");
            var st = Pt(tpl, "TextureStart") ?? new SKPoint(0, 0);
            double iw = NumNz(Xml.Tx(tpl, "Width"), 16);
            double ih = NumNz(Xml.Tx(tpl, "Height"), 16);
            int levels = (int)Math.Max(1, Num(Xml.Tx(tpl, "MaxLevels"), 1));
            bool horiz = Flag(tpl, "Horizontal");

            int lvl = ChooseLevel(s, forceLevel, levels);

            if (s.W == 0) s.W = iw;
            if (s.H == 0) s.H = ih;

            float sx = st.X + (horiz ? (float)(lvl * iw) : 0);
            float sy = st.Y + (horiz ? 0 : (float)(lvl * ih));
            drewInner = s.Tex.Draw(c, texName, sx, sy, (float)iw, (float)ih,
                new SKRect(0, 0, (float)s.W, (float)s.H));
        }
        else
        {
            var size = Pt(tpl, "Size");
            // Per the documentation: IconSize 0 means 16x16, else 32x32.
            double isz = tpl is not null && Xml.Tx(tpl, "IconSize").Length > 0
                ? (Num(Xml.Tx(tpl, "IconSize")) != 0 ? 32 : 16)
                : 0;
            if (s.W == 0) s.W = size?.X ?? (isz != 0 ? isz : 16);
            if (s.H == 0) s.H = size?.Y ?? (isz != 0 ? isz : 16);
            drewInner = NineSlice.DrawArea(c, s.Tex, tpl, s.W, s.H);
        }

        if (!drewInner)
        {
            // Item and spell icons only come from the engine in the game.
            // Show a placeholder so grid and size stay judgeable.
            Placeholders.ContentAtRuntime(c, s.Box);
            s.ShowedSubstitute = true;
        }
        s.Drew |= drewInner;
    }

    /// <summary>
    /// Recognise an effect icon by the ending of its name. The digit is
    /// optional, because the templates come in two families (16 px and 10 px
    /// rows).
    /// </summary>
    private static int EffectOf(string tplName)
    {
        string tn = tplName.ToLowerInvariant();
        if (EndsWithEffect(tn, "_mez")) return 1;
        if (EndsWithEffect(tn, "_diz")) return 2;
        if (EndsWithEffect(tn, "_psn")) return 3;
        if (EndsWithEffect(tn, "_ns")) return 4;
        return 0;
    }

    /// <summary>Equivalent to <c>/_mez\d?$/</c>: the ending, optionally with a digit.</summary>
    private static bool EndsWithEffect(string s, string suffix)
    {
        if (s.EndsWith(suffix, StringComparison.Ordinal)) return true;
        return s.Length > 0 && char.IsAsciiDigit(s[^1])
            && s[..^1].EndsWith(suffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which sprite level to show? (original 1537–1559)
    /// 1. Effect icons dictate their level.
    /// 2. Otherwise the adapter decides: value/maximum is mapped onto the
    ///    available levels (group_health0 = 20/100 gives level 2 out of 11).
    ///    That is exactly how the engine fills multi-level icons such as the
    ///    group health frame.
    /// 3. Direction indicators (compass) show their neutral position.
    /// </summary>
    private static int ChooseLevel(ElState s, int forceLevel, int levels)
    {
        if (forceLevel > 0) return Math.Min(forceLevel, levels - 1);

        string adName = SampleData.AdapterOf(s.Def);
        string adl = (adName + " " + s.TplName).ToLowerInvariant();
        bool dirLike = adl.Contains("compass") || adl.Contains("heading")
            || adl.Contains("direction") || adl.Contains("arrow") || adl.Contains("rotat");

        if (s.Opt.ShowSampleData && adName.Length > 0 && levels > 1)
        {
            var frac = RefFraction(s.Ctx, adName, out _);
            if (frac is { } f) return (int)JsRound(f * (levels - 1));
            return dirLike ? 0 : levels - 1;
        }
        return levels > 1 ? (dirLike ? 0 : levels - 1) : 0;
    }
}
