using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Controls: buttons, check boxes, bars, sliders, tabs, grids. Ported from
/// <c>renderEl</c>, branches <c>ButtonDef</c> … <c>ClickableEditBoxDef</c>.
/// </summary>
public static partial class ElementRenderer
{
    /// <summary>Button, invisible button and check box (original 1402).</summary>
    private static void Button(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("ButtonTemplate", "CheckBoxTemplate");
        var size = Pt(tpl, "Size");
        if (s.W == 0 && size is not null) s.W = size.Value.X;
        if (s.H == 0 && size is not null) s.H = size.Value.Y;

        if (s.Tag != "InvisibleButtonDef")
        {
            s.Drew |= NineSlice.DrawArea(c, s.Tex, tpl, s.W, s.H, "Normal");
        }
        else
        {
            // A pure click area — invisible in the game, hinted at in the
            // preview so that it can be found at all.
            if (s.Opt.ShowEditorHints)
            {
                Placeholders.Fill(c, s.Box, new SKColor(120, 180, 255, 18));
                Placeholders.Dotted(c, s.Box, new SKColor(140, 190, 255, 89));
            }
            // Also tells ElementRenderer.cs:217 not to look for textures outside the package; a click area has none to look for whether or not the hint is drawn.
            s.ShowedSubstitute = true;

            /* The game never draws the <Label> of a click area: in
               new_group_window it names the member ("grouptarget0") and,
               drawn, sits on top of the class column. Shown only as an
               editor hint. Checked against an in-game screenshot 2026-10-08. */
            if (!s.Opt.ShowEditorHints) return;
        }

        if (s.Text.Length == 0) return;

        var fdef = Xml.Sub(tpl, "Font");
        string fn = s.FontName.Length > 0 ? s.FontName : Xml.NameOf(fdef);
        var col = fdef is not null
            ? ColorOf(fdef, "ColorNormal", TextDefault)
            : TextDefault;

        if (s.Tag == "CheckBoxDef")
        {
            /* Per the documentation the text offset lives in the TEMPLATE
               (<TextOffset>), not in the element. <LabelWidth> is a CHARACTER
               COUNT, not a pixel width — hence the estimate below. */
            var to = Pt(tpl, "TextOffset")
                ?? new SKPoint((float)((s.W != 0 ? s.W : 14) + 4), 0);
            var ta = Xml.Sub(tpl, "TextAlignment");
            var hAlign = HAlign.Start;
            var vAlign = VAlign.Center;
            if (ta is not null)
            {
                if (Flag(ta, "CenterHorizontally")) hAlign = HAlign.Center;
                if (Flag(ta, "EndAligned") || Flag(ta, "OffsetRight")) hAlign = HAlign.End;
                if (Flag(ta, "CenterVertically")) vAlign = VAlign.Center;
                if (Flag(ta, "TopLeft")) vAlign = VAlign.Top;
            }

            double lw = Num(Xml.Tx(s.Def, "LabelWidth"));
            // estimate the width roughly from the character count
            double approx = lw > 0 ? JsRound(lw * s.Fonts.SizePx(fn) * 0.55) : 120;
            double bh = s.H != 0 ? s.H : 14;

            // The label sits BESIDE the box and may reach beyond the element
            // box (overflow:visible in the original).
            s.ClipContent = false;
            TextDrawer.Draw(c, s.Fonts, fn, s.Text,
                new SKRect(to.X, to.Y, (float)(to.X + approx), (float)(to.Y + bh)),
                col, hAlign, vAlign);
        }
        else
        {
            TextDrawer.Draw(c, s.Fonts, fn, s.Text, s.Box, col,
                HAlign.Center, VAlign.Center);
        }
    }

    /// <summary>
    /// Progress and status bars, across and down (original 1444).
    /// </summary>
    private static void StatusBar(SKCanvas c, ElState s)
    {
        bool vertical = s.Tag == "VerticalStatusbarDef";
        var tpl = s.Tpl("StatusBarTemplate", "VerticalStatusBarTemplate");

        // The fields are named differently depending on the template:
        // across: Background/ForegroundHResizeTemplate
        // down:   Background/ForegroundImage
        string bgN = First(tpl, "BackgroundHResizeTemplate", "BackgroundImage",
            "BackgroundVResizeTemplate", "BackgroundTemplateName", "Background");
        string fgN = First(tpl, "ForegroundHResizeTemplate", "ForegroundImage",
            "ForegroundVResizeTemplate", "ForegroundTemplateName", "Foreground");
        var bg = s.Find(bgN);
        var fg = s.Find(fgN);

        static bool IsV(System.Xml.Linq.XElement? t) =>
            t is not null && (t.Name.LocalName.Contains("Vertical")
                              || t.Name.LocalName.Contains("VResize"));

        // Measurements: its own value, else the template's, else the
        // background template's, else a workable value from experience.
        if (s.H == 0)
            s.H = FirstNonZero(Num(Xml.Tx(tpl, "Height")), Num(Xml.Tx(bg, "Height")),
                vertical ? 40 : 8);
        if (s.W == 0)
            s.W = FirstNonZero(Num(Xml.Tx(tpl, "Width")), Num(Xml.Tx(bg, "Width")),
                vertical ? 7 : 60);

        double pct = FillFraction(s);
        var off = Pt(tpl, "ForegroundOffset") ?? new SKPoint(0, 0);

        if (bg is not null)
            s.Drew |= IsV(bg) || vertical
                ? NineSlice.DrawVResize(c, s.Tex, bg, s.W, s.H)
                : NineSlice.DrawHResize(c, s.Tex, bg, s.W, s.H);

        if (fg is not null)
        {
            if (vertical || IsV(fg))
            {
                // vertical: fill from the bottom up
                double fh = JsRound((s.H - off.Y) * pct);
                float top = (float)(off.Y + (s.H - off.Y - fh));
                c.Save();
                c.ClipRect(new SKRect(off.X, top, (float)(off.X + s.W), (float)(top + fh)));
                c.Translate(off.X, top);
                s.Drew |= NineSlice.DrawVResize(c, s.Tex, fg, s.W, fh);
                c.Restore();
            }
            else
            {
                double fw = JsRound(s.W * pct);
                double fh = NumNz(Xml.Tx(fg, "Height"), s.H);
                c.Save();
                c.ClipRect(new SKRect(off.X, off.Y, (float)(off.X + fw), (float)(off.Y + fh)));
                c.Translate(off.X, off.Y);
                s.Drew |= NineSlice.DrawHResize(c, s.Tex, fg, fw, fh);
                c.Restore();
            }
        }

        if (bg is null && fg is null && s.TplName.Length > 0 && s.TplName != "none")
        {
            // Only when a template was meant but cannot be found. With
            // <TemplateName>none</TemplateName> the bar stays deliberately
            // invisible.
            Placeholders.SplitBar(c, s.Box, pct,
                new SKColor(0x7A, 0x1C, 0x14), new SKColor(0x24, 0x18, 0x12), vertical);
            s.ShowedSubstitute = true;
        }
    }

    /// <summary>
    /// Fill level of a bar. Preferably from the real value and maximum of the
    /// original editor, so that bars fill in the right proportion
    /// (original 1460–1477).
    /// </summary>
    private static double FillFraction(ElState s)
    {
        const double fallback = 0.72;
        string ad = SampleData.AdapterOf(s.Def);
        if (!s.Opt.ShowSampleData || ad.Length == 0) return fallback;

        if (RefFraction(s.Ctx, ad, out _) is { } f) return f;

        // No pair of numbers on file: derive it from the sample text.
        string raw = SampleData.For(ad.ToLowerInvariant(), 0, s.Ctx.Reference) ?? "";
        if (IsClockLike(raw)) return 0.55;
        double v = Num(raw, double.NaN);
        if (!double.IsNaN(v) && v >= 0 && v <= 100) return v / 100;
        return fallback;
    }

    /// <summary>Equivalent to <c>/^\d+:\d+/</c> — a time such as "0:24".</summary>
    private static bool IsClockLike(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i == 0 || i >= s.Length || s[i] != ':') return false;
        return i + 1 < s.Length && char.IsAsciiDigit(s[i + 1]);
    }

    /// <summary>Combo box (original 1777).</summary>
    private static void ComboBox(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("ComboBoxTemplate");
        // ComboBox does not draw itself; it points at a LabelButtonTemplate
        // (the field) and a DownButtonTemplate (the arrow).
        var labT = s.Find(Xml.Tx(tpl, "LabelButtonTemplate"));
        var dwnT = s.Find(Xml.Tx(tpl, "DownButtonTemplate"));
        var lsz = Pt(labT, "Size");
        if (s.H == 0) s.H = FirstNonZero(lsz?.Y ?? 0, 18);
        if (s.W == 0) s.W = FirstNonZero(lsz?.X ?? 0, 120);

        bool drewBody = NineSlice.DrawArea(c, s.Tex, labT, s.W, s.H, "Normal");
        if (!drewBody)
        {
            Placeholders.VGradient(c, s.Box,
                new SKColor(0x3A, 0x33, 0x2A), new SKColor(0x24, 0x1F, 0x19));
            Placeholders.Border(c, s.Box, new SKColor(0x14, 0x10, 0x0C));
            s.ShowedSubstitute = true;
        }
        s.Drew |= drewBody;

        if (dwnT is null) return;
        var dsz = Pt(dwnT, "Size");
        double dw = dsz?.X ?? 12, dh = dsz is { } d && d.Y != 0 ? d.Y : s.H;
        float dx = (float)(s.W - dw), dy = (float)JsRound((s.H - dh) / 2);
        c.Save();
        c.ClipRect(new SKRect(dx, dy, (float)(dx + dw), (float)(dy + dh)));
        c.Translate(dx, dy);
        s.Drew |= NineSlice.DrawArea(c, s.Tex, dwnT, dw, dh, "Normal");
        c.Restore();
    }

    /// <summary>Horizontal slider (original 1805).</summary>
    private static void HorizontalSlider(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("HorizontalSliderTemplate");
        var bg = s.Find(Xml.Tx(tpl, "BackgroundHResizeTemplate"),
            "HorizontalResizeImageTemplate");
        if (s.H == 0) s.H = NumNz(Xml.Tx(tpl, "Height"), 16);

        bool drew = false;
        if (bg is not null)
            drew |= NineSlice.DrawHResize(c, s.Tex, bg, s.W != 0 ? s.W : 100, s.H);

        var ind = s.Find(Xml.Tx(tpl, "ImageAreaTemplate"), "ImageAreaTemplate");
        if (ind is not null)
        {
            var sz = Pt(ind, "Size");
            double kw = sz?.X ?? 16, kh = sz?.Y ?? 16;
            // In the preview the knob sits in the middle of the track.
            float kx = (float)JsRound((s.W != 0 ? s.W : 100) * 0.5);
            c.Save();
            c.ClipRect(new SKRect(kx, 0, (float)(kx + kw), (float)kh));
            c.Translate(kx, 0);
            drew |= NineSlice.DrawArea(c, s.Tex, ind, kw, kh);
            c.Restore();
        }

        if (!drew)
        {
            Placeholders.Fill(c, s.Box, Placeholders.Brass(31));
            Placeholders.Border(c, s.Box, Placeholders.Brass(77));
            s.ShowedSubstitute = true;
        }
        s.Drew |= drew;
    }

    /// <summary>Vertical slider (original 1825).</summary>
    private static void VerticalSlider(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("VerticalSliderTemplate");
        var bg = s.Find(Xml.Tx(tpl, "BackgroundVResizeTemplate"),
            "VerticalResizeImageTemplate");
        bool drew = bg is not null && NineSlice.DrawTemplate(c, s.Tex, bg, s.W, s.H);
        if (!drew)
        {
            Placeholders.Fill(c, s.Box, Placeholders.Brass(31));
            Placeholders.Border(c, s.Box, Placeholders.Brass(77));
            s.ShowedSubstitute = true;
        }
        s.Drew |= drew;
    }

    /// <summary>Compass (original 1835).</summary>
    private static void Compass(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("CompassControlTemplate");
        // Here the field is called <Texture> and holds a texture name, not a block.
        string texName = Xml.Tx(tpl, "Texture");
        bool drew = false;
        if (texName.Length > 0 && s.W != 0 && s.H != 0)
            drew = s.Tex.Draw(c, texName, 0, 0, (float)s.W, (float)s.H, s.Box);

        if (!drew)
        {
            Placeholders.RadialDisc(c, s.Box,
                new SKColor(0x2A, 0x3A, 0x2A), new SKColor(0x14, 0x1A, 0x14),
                Placeholders.Brass(102));
            s.ShowedSubstitute = true;
        }
        s.Drew |= drew;
    }

    /// <summary>
    /// Tab row (original 1849). Count and names come from &lt;Tab&gt; child
    /// elements; which tab is visible comes from the preview switches.
    /// </summary>
    private static void Tabs(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("TabsTemplate");

        var btn = s.Find(Xml.Tx(tpl, "TabButtonTemplate"),
            "ButtonTemplate", "HorizontalResizeButtonTemplate");
        var size = Pt(btn, "Size");

        /* The control area does NOT fill the box: it begins one pixel above
           the lower edge of the tab buttons, so the buttons sit on its frame
           instead of on top of it. DAoCEd's ControlTabsdef hands
           drawFullAtHeight the height "H − Size.Y + 1" and the start
           "Size.Y − 1". Filling the whole box drew the area's top edge behind
           the button row. */
        var bg = s.Find(Xml.Tx(tpl, "ControlAreaFullResizeImageTemplate"),
            "FullResizeImageTemplate");
        bool drew = false;
        if (bg is not null && s.W != 0 && s.H != 0)
        {
            double top = size is not null ? size.Value.Y - 1 : 0;
            c.Save();
            c.Translate(0, (float)top);
            drew |= NineSlice.DrawFullResize(c, s.Tex, bg, s.W, s.H - top);
            c.Restore();
        }

        var myTabs = s.Def.Elements()
            .Where(e => e.Name.LocalName == "Tab")
            .Select(t => new TabInfo(Xml.Tx(t, "Id"), Xml.NameOf(t, Xml.Tx(t, "Id"))))
            .ToList();

        if (myTabs.Count > 0)
        {
            // report the tabs for the app's tab row
            foreach (var t in myTabs)
                if (!s.Ctx.Tabs.Contains(t)) s.Ctx.Tabs.Add(t);
            s.Ctx.ActiveTab ??= s.Opt.ActiveTab ?? myTabs[0].Id;

            /* <TabXOffset> is the OVERLAP of two neighbouring tabs, not a
               margin in front of the first one: DAoCEd's ControlTabsdef puts
               button i at "i * Size.X - i * TabXOffset" and starts at 0.
               <TabYOffset> exists in the template (default 2) and
               ControlTabsdef never reads it — the buttons sit at the top of
               the TabsDef box and the control area begins below them. */
            double ox = Num(Xml.Tx(tpl, "TabXOffset"), 5);
            double bw = size?.X ?? 70, bh = size?.Y ?? 18;
            var fdef = Xml.Sub(btn, "Font");
            string fn = Xml.NameOf(fdef, "TokaFontRegular10");

            /* The label colour belongs to the button template, and its default
               is white. DAoCEd draws every tab in ColorNormal —
               it has no notion of a current tab; here the active one is drawn
               with the Pressed texture, so it takes the colour that goes with
               it. */
            var colOn = ColorOf(fdef, "ColorPressed", TextDefault);
            var colOff = ColorOf(fdef, "ColorNormal", TextDefault);

            for (int i = 0; i < myTabs.Count; i++)
            {
                var t = myTabs[i];
                bool on = t.Id == s.Ctx.ActiveTab;
                float cx = (float)(i * (bw - ox)), cy = 0;
                var cell = new SKRect(cx, cy, (float)(cx + bw), (float)(cy + bh));

                c.Save();
                c.ClipRect(cell);
                c.Translate(cx, cy);
                var local = new SKRect(0, 0, (float)bw, (float)bh);
                bool drewCell = NineSlice.DrawArea(c, s.Tex, btn, bw, bh,
                    on ? "Pressed" : "Normal");
                if (!drewCell)
                {
                    Placeholders.VGradient(c, local,
                        on ? new SKColor(0x5C, 0x4D, 0x33) : new SKColor(0x33, 0x2C, 0x23),
                        on ? new SKColor(0x3D, 0x33, 0x25) : new SKColor(0x24, 0x1F, 0x19));
                    Placeholders.Border(c, local, new SKColor(0x17, 0x13, 0x0E));
                }
                TextDrawer.Draw(c, s.Fonts, fn, t.Name, local,
                    on ? colOn : colOff, HAlign.Center, VAlign.Center);
                c.Restore();
                drew = true;
            }
        }

        if (!drew)
        {
            Placeholders.Fill(c, s.Box, Placeholders.Brass(20));
            s.ShowedSubstitute = true;
            using var p = new SKPaint
            {
                Color = Placeholders.Brass(89), IsStroke = true, StrokeWidth = 1,
            };
            c.DrawLine(0, (float)s.H - 0.5f, (float)s.W, (float)s.H - 0.5f, p);
        }
        s.Drew |= drew;
    }

    /// <summary>Icon grid, e.g. spell and item bars (original 1897).</summary>
    private static void IconSet(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("IconSetTemplate");
        var ico = s.Find(Xml.Tx(tpl, "IconTemplate"), "IconTemplate", "ImageAreaTemplate");
        var size = Pt(ico, "Size");
        double cw = size?.X ?? 16, ch = size?.Y ?? 16;
        double cp = Num(Xml.Tx(tpl, "CellPadding"));
        double lp = Num(Xml.Tx(tpl, "LinePadding"));

        // count preferably from <Rows>/<Columns>, else derived from the area
        double declCols = Num(Xml.Tx(s.Def, "Columns"));
        double declRows = Num(Xml.Tx(s.Def, "Rows"));
        int cols = (int)Math.Max(1, declCols != 0 ? declCols
            : (s.W != 0 ? Math.Floor(s.W / (cw + cp)) : 1));
        int rows = (int)Math.Max(1, declRows != 0 ? declRows
            : (s.H != 0 ? Math.Floor(s.H / (ch + lp)) : 1));

        if (s.W == 0) s.W = cols * (cw + cp) - cp;
        if (s.H == 0) s.H = rows * (ch + lp) - lp;

        bool drewAny = false;
        for (int r = 0; r < rows; r++)
        for (int col = 0; col < cols; col++)
        {
            float x = (float)(col * (cw + cp)), y = (float)(r * (ch + lp));
            var cell = new SKRect(x, y, (float)(x + cw), (float)(y + ch));
            c.Save();
            c.ClipRect(cell);
            c.Translate(x, y);
            bool drewCell = NineSlice.DrawArea(c, s.Tex, ico, cw, ch);
            c.Restore();
            if (!drewCell)
            {
                Placeholders.ContentAtRuntimeCell(c, cell, 77);
                s.ShowedSubstitute = true;
            }
            drewAny |= drewCell;
        }

        if (rows <= 0 || cols <= 0)
            Placeholders.Dashed(c, s.Box, Placeholders.Brass(77));
        s.Drew |= drewAny;
    }

    /// <summary>
    /// Edit box (original 1924). The original deliberately draws only an area
    /// here: in the game the engine fills the content.
    /// </summary>
    private static void EditBox(SKCanvas c, ElState s)
    {
        Placeholders.Fill(c, s.Box, new SKColor(8, 10, 14, 153));
        Placeholders.Border(c, s.Box, Placeholders.Brass(77));
        s.ShowedSubstitute = true;
    }
}
