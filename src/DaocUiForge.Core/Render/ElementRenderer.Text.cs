using System.Xml.Linq;
using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Text elements: labels, lists, trees and text areas. Ported from
/// <c>renderEl</c>, branches <c>LabelDef</c> … <c>ChatControlDef</c>.
/// </summary>
public static partial class ElementRenderer
{
    /// <summary>
    /// Colours of the status effects, measured off
    /// <c>status_effect_icon.tga</c>: mez, diz, poison, nearsight. With a
    /// state simulated the name takes on the matching colour — as in the game,
    /// where <c>group_colorN</c> reflects the effect.
    /// </summary>
    private static readonly SKColor[] StateColors =
    {
        default,                        // 0 = no state
        new(0x00, 0xFF, 0xFF),          // 1 mez
        new(0xFD, 0x8F, 0x4D),          // 2 diz
        new(0x9B, 0xFF, 0x85),          // 3 poison
        new(0xD1, 0x9D, 0xFB),          // 4 nearsight
    };

    /// <summary>
    /// A free-standing text field (original 1585).
    ///
    /// Two quirks: &lt;Height&gt; is a CLIPPING FRAME here, not a
    /// layout size — overlaps with neighbouring elements are normal and not an
    /// error. And DAoC puts the text at the TOP of the box, not centred.
    /// </summary>
    private static void Label(SKCanvas c, ElState s)
    {
        var col = ColorOf(s.Def, "Color", TextDefault);

        /* <ColorAdapter> colours the text at run time — red in the game, say,
           when a group member is dead or carries a status. The fixed <Color>
           is only the starting value. */
        string cad = First(s.Def, "ColorAdapter", "Coloradapter");
        if (s.Opt.ShowSampleData && cad.Length > 0)
        {
            string? hex = RefColor(s.Ctx.Reference.Colors, cad);
            if (hex is not null && TryHex(hex, out var fromAdapter)) col = fromAdapter;

            int st = s.Opt.SimulatedState;
            if (st > 0 && st < StateColors.Length && IsGroupColor(cad))
                col = StateColors[st];
        }

        /* The text has to be resolved before the height is: without <Height>
           the box is content-sized, and content-sized means every line. */
        string shown = s.Text;
        bool fromSample = false;

        /* With an <Adapter> the engine fills the text at run time, even when
           <Data> is set. Checked against an in-game screenshot 2026-10-08:
           a ScalarLabelDef draws <Data> as a prefix of the value ("R" + 5 =
           "R5", "%" + 15 = "%15" in float_realm_exp_window); a LabelDef draws
           the value instead of <Data> (custom7 shows the member name, not its
           <Data>group0</Data>). */
        string adapter = SampleData.AdapterOf(s.Def);
        if (shown.Length > 0 && adapter.Length > 0 && s.Opt.ShowSampleData)
        {
            // A scalar shows the number: realm_rank is "10" among the current
            // values but "R10L9" among the texts, which SampleData prefers.
            string? value = s.Tag == "ScalarLabelDef"
                            && s.Ctx.Reference.Current.TryGetValue(adapter, out var scalar)
                ? scalar
                : SampleData.For(adapter, 0, s.Ctx.Reference);
            if (!string.IsNullOrEmpty(value))
            {
                shown = s.Tag == "ScalarLabelDef" ? shown + value : value;
                int max = (int)Num(Xml.Tx(s.Def, "MaxCharacters"));
                if (max > 0 && shown.Length > max) shown = shown[..max];
                fromSample = true;
            }
        }

        if (shown.Length == 0)
        {
            string ad = SampleData.AdapterOf(s.Def);
            if (s.Opt.ShowSampleData && ad.Length > 0)
            {
                shown = SampleData.For(ad, (int)Num(Xml.Tx(s.Def, "MaxCharacters")),
                    s.Ctx.Reference) ?? "";
                fromSample = shown.Length > 0;
            }
            else if (s.Tag == "ScalarLabelDef") shown = "0";
        }

        float px = s.Fonts.SizePx(s.FontName);

        /* Without <Height> the box is as tall as the text. The first line keeps
           the original's px + 3; every further one adds a line advance, which
           was missing until 2026-09-08 — a two-line label got a box one line
           tall, so half of it could not be clicked and the overflow
           measurement counted it short (white-space:pre, nothing is wrapped,
           so the line count is the number of breaks plus one). */
        if (s.H == 0)
        {
            int lines = shown.Length == 0 ? 1 : shown.Count(ch => ch == '\n') + 1;
            s.H = px + 3 + (lines - 1) * s.Fonts.LinePx(s.FontName);
        }

        // An empty label has no text to draw, but it keeps the box above: the
        // element is there and can be selected.
        if (shown.Length == 0) return;

        /* Alignment: <TextCentered> sits in the Def itself,
           CenterHorizontally/Right in the <Alignment> block.

           <EndAligned> in the Def does NOT align — measured against the game on
           summary.xml (2026-08-07): both of its end-aligned labels draw their
           text starting at <Position>, where aligning inside <Width> would put
           them 4 px and 3.4 px further right. The HTML original read the tag as
           a right alignment; nothing in the format says <Width> is a box to
           align inside, and <Height> next to it is already only a clipping
           frame. The flag inside <Alignment> is a different thing
           and stays: that block is DAoCEd's own alignment editor. */
        var hAlign = HAlign.Start;
        if (Flag(s.Def, "TextCentered")) hAlign = HAlign.Center;
        else if (s.Align is not null && Flag(s.Align, "CenterHorizontally")) hAlign = HAlign.Center;
        else if (s.Align is not null && (Flag(s.Align, "Right") || Flag(s.Align, "EndAligned")))
            hAlign = HAlign.End;

        // Without <Width> the box is content-wide (CSS shrink-to-fit), and
        // then any horizontal alignment has no effect.
        if (s.W == 0) hAlign = HAlign.Start;


        // Sample values a little paler (opacity:.92 in the original).
        if (fromSample) col = col.WithAlpha((byte)(col.Alpha * 235 / 255));

        float boxW = s.W != 0 ? (float)s.W : RecordArea.Right;
        float widest = TextDrawer.Draw(c, s.Fonts, s.FontName, shown,
            new SKRect(0, 0, boxW, (float)s.H), col,
            hAlign, VAlign.Top, shadowAlpha: 204);   // rgba(0,0,0,.8)

        if (s.W == 0) s.ContentW = widest;
        s.Drew = true;
    }

    /// <summary>Equivalent to <c>/^group_color\d+$/i</c>.</summary>
    private static bool IsGroupColor(string name)
    {
        const string prefix = "group_color";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string rest = name[prefix.Length..];
        return rest.Length > 0 && rest.All(char.IsAsciiDigit);
    }

    /// <summary>Six-digit hex value without <c>#</c>, as in colors.json.</summary>
    private static bool TryHex(string hex, out SKColor color)
    {
        color = default;
        if (hex.Length != 6) return false;
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int v)) return false;
        color = new SKColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>
    /// List box and tree view (original 1627). Structure per the DAoCEd model:
    /// a background template, rows made of IconTemplate + TextFont, optionally
    /// a scroll bar on the right.
    /// </summary>
    private static void ListBox(SKCanvas c, ElState s)
    {
        var tpl = s.Tpl("ListBoxTemplate", "TreeControlTemplate");
        string bgN = Xml.Tx(tpl, "BackgroundTemplate");
        var bgT = bgN.Length > 0 && bgN != "none" ? s.Find(bgN) : null;

        /* A COLUMN HEADER splits the box in two, and the switch for it is
           <HeaderHorizResizeTemplate>: DAoCEd's ListboxtemplateNode reads
           HeaderHeight, HeaderBackgroundTemplate, the HeaderControls and the
           two row templates ONLY inside "if (getChild(e,
           "HeaderHorizResizeTemplate") != null)", and its hasSpecialStuff()
           asks nothing else. The body background is then not the box: its
           ControlListboxdef hands drawFullAtHeight the height
           "H - HeaderHeight" and the start "HeaderHeight", and paints the
           header band over the top with a template of its own. Drawing one
           frame across the whole height put the body's upper edge behind the
           header row instead of below it. */
        bool hasHeader = Xml.Tx(tpl, "HeaderHorizResizeTemplate").Length > 0;
        double headH = hasHeader ? Num(Xml.Tx(tpl, "HeaderHeight"), 39) : 0;
        if (headH <= 0 || headH >= s.H) headH = 0;

        bool drewBg;
        if (headH > 0)
        {
            c.Save();
            c.Translate(0, (float)headH);
            drewBg = bgT is not null && NineSlice.DrawTemplate(c, s.Tex, bgT, s.W, s.H - headH);
            c.Restore();

            // No header background of its own -> the body's, as in DAoCEd.
            string hbN = Xml.Tx(tpl, "HeaderBackgroundTemplate");
            var hbT = hbN.Length > 0 && hbN != "none"
                ? s.Find(hbN, "FullResizeImageTemplate") : null;
            hbT ??= bgT;
            drewBg |= hbT is not null && NineSlice.DrawTemplate(c, s.Tex, hbT, s.W, headH);
        }
        else
        {
            drewBg = bgT is not null && NineSlice.DrawTemplate(c, s.Tex, bgT, s.W, s.H);
        }

        if (!drewBg && s.Opt.ShowEditorHints)
        {
            Placeholders.Fill(c, s.Box, Placeholders.PanelDark);
            Placeholders.Border(c, s.Box, Placeholders.Brass(56));
            s.ShowedSubstitute = true;
        }
        s.Drew |= drewBg;

        // The header columns are declared structure, not a run-time value —
        // they are drawn whether or not sample data is switched on.
        if (headH > 0 && s.W > 20) ListBoxHeader(c, s, tpl, headH);

        if (!s.Opt.ShowSampleData || s.W <= 20 || s.H - headH <= 12) return;

        // the def's IconTemplate, empty included, overrides the template's, as in the game
        string icoN = Xml.Sub(s.Def, "IconTemplate") is not null
            ? Xml.Tx(s.Def, "IconTemplate") : Xml.Tx(tpl, "IconTemplate");
        var icoT = s.Find(icoN, "IconTemplate", "ImageAreaTemplate");
        var isz = Pt(icoT, "Size");
        double iw = isz?.X ?? 0, ih = isz?.Y ?? 0;
        double lp = Num(Xml.Tx(tpl, "LinePadding"), 1);
        double cp = Num(Xml.Tx(tpl, "CellPadding"));
        var tf = Xml.Sub(tpl, "TextFont");
        string fn = s.FontName.Length > 0 ? s.FontName : Xml.NameOf(tf, "TokaFontRegular10");
        var col = ColorOf(tf, "ColorNormal", TextDefault);
        double tlo = Num(Xml.Tx(tpl, "TextLeftOffset"));
        double tto = Num(Xml.Tx(tpl, "TextTopOffset"));
        double rowH = Math.Max(ih, s.Fonts.SizePx(fn) + 2) + lp;
        if (rowH <= 0) return;

        // keep the scroll bar space on the right free
        double sb = Flag(tpl, "DrawScroll") ? 8 : 0;
        double top = headH + tto;
        int rows = (int)Math.Max(0, Math.Floor((s.H - top) / rowH));

        /* <AlternateRowName>/<SelectedRowName> are HorizontalResizeImage
           templates laid under every other row and under the selected one.
           DAoCEd puts them at "HeaderHeight + LinePadding + AlternateRowOffset"
           and spaces them by the TEMPLATE's own height while its text rows
           advance by the font height — the two therefore drift apart in its
           preview. Here one row pitch serves both. */
        var altT = RowStripe(s, Xml.Tx(tpl, "AlternateRowName"));
        var selT = RowStripe(s, Xml.Tx(tpl, "SelectedRowName")) ?? altT;
        double altOff = Num(Xml.Tx(tpl, "AlternateRowOffset"), -2);
        double selOff = Num(Xml.Tx(tpl, "SelectedRowOffset"), -2);
        bool showAlt = altT is not null && Flag(tpl, "DisplayAlternateRow");

        /* Columns (TreeControlDef only): <Column1Offset>/<Column1Width> and
           <Column2Offset>/<Column2Width> say where the two text columns start
           and how wide they are. Without those values a single column is
           drawn that fills the rest of the row. */
        double c1o = Num(Xml.Tx(s.Def, "Column1Offset"), double.NaN);
        double c1w = Num(Xml.Tx(s.Def, "Column1Width"), double.NaN);
        double c2o = Num(Xml.Tx(s.Def, "Column2Offset"), double.NaN);
        double c2w = Num(Xml.Tx(s.Def, "Column2Width"), double.NaN);
        bool hasCols = s.Tag == "TreeControlDef" && !double.IsNaN(c1o) && !double.IsNaN(c1w);

        for (int r = 0; r < Math.Min(rows, 6); r++)
        {
            double y = top + r * rowH;

            // Row 0 stands for the selected one, every other row after it for
            // the alternating background.
            var stripe = r == 0 ? selT : (showAlt && r % 2 == 1 ? altT : null);
            if (stripe is not null)
            {
                double sh = Num(Xml.Tx(stripe, "Height"), rowH - lp);
                double sw = Math.Max(0, s.W - 2 * cp);
                if (sh > 0 && sw > 0)
                {
                    c.Save();
                    c.Translate((float)cp, (float)(y + (r == 0 ? selOff : altOff)));
                    NineSlice.DrawHResize(c, s.Tex, stripe, sw, sh);
                    c.Restore();
                }
            }

            if (iw > 0 && ih > 0)
            {
                var cell = new SKRect((float)cp, (float)y, (float)(cp + iw), (float)(y + ih));
                c.Save();
                c.ClipRect(cell);
                c.Translate(cell.Left, cell.Top);
                bool drewIcon = NineSlice.DrawArea(c, s.Tex, icoT, iw, ih);
                c.Restore();
                if (!drewIcon && s.Opt.ShowEditorHints) Placeholders.ContentAtRuntimeCell(c, cell, 71);
            }

            void Cell(double left, double width, string text)
            {
                if (width <= 0) return;
                var box = new SKRect((float)left, (float)y,
                    (float)(left + width), (float)(y + rowH - lp));
                c.Save();
                c.ClipRect(box);
                TextDrawer.Draw(c, s.Fonts, fn, text, box, col,
                    HAlign.Start, VAlign.Center, shadowAlpha: 0);
                c.Restore();
            }

            if (hasCols)
            {
                // column 2 ends at the list edge at the latest (minus the scroll bar)
                Cell(c1o, Math.Min(c1w, Math.Max(0, s.W - c1o - sb)), "Entry " + (r + 1));
                if (!double.IsNaN(c2o) && !double.IsNaN(c2w) && c2o < s.W - sb)
                    Cell(c2o, Math.Min(c2w, Math.Max(0, s.W - c2o - sb)), "Value " + (r + 1));
            }
            else
            {
                double indent = cp + (iw > 0 ? iw + 2 : 0) + tlo;
                Cell(indent, Math.Max(0, s.W - indent - sb), "Entry " + (r + 1));
            }
        }

        if (sb > 0)
        {
            var bar = new SKRect((float)(s.W - sb), 0, (float)s.W, (float)s.H);
            Placeholders.Fill(c, bar, Placeholders.Brass(26));
            using var p = new SKPaint
            {
                Color = Placeholders.Brass(56), IsStroke = true, StrokeWidth = 1,
            };
            c.DrawLine(bar.Left + 0.5f, 0, bar.Left + 0.5f, (float)s.H, p);
        }
    }

    /// <summary>
    /// The row background named by <c>AlternateRowName</c>/
    /// <c>SelectedRowName</c> — a HorizontalResizeImage template, or nothing
    /// when the name is empty or "none".
    /// </summary>
    private static XElement? RowStripe(ElState s, string name) =>
        name.Length > 0 && name != "none"
            ? s.Find(name, "HorizontalResizeImageTemplate")
            : null;

    /// <summary>
    /// The column header of a list box (DAoCEd's <c>Listboxheader</c>). Every
    /// &lt;HeaderControl&gt; names a &lt;ListBoxHeaderTemplate&gt;, whose
    /// &lt;Texture&gt; holds the column icon at &lt;Size&gt; and whose
    /// &lt;TextFont&gt; carries the caption colour; the caption sits directly
    /// BELOW the icon (<c>labelPosY = current.getHeight()</c>).
    ///
    /// <para>Where the columns sit horizontally is the one thing the format
    /// does not say. It declares &lt;HeaderLeftOffset&gt; and
    /// &lt;HeaderRightOffset&gt; — and DAoCEd reads neither: its
    /// ControlListboxdef puts header <c>i</c> at the hard-coded
    /// <c>10 + 50 * i</c>, which is why seven columns end halfway across the
    /// list in its preview. The client sizes them from the run-time table (the
    /// HeaderHorizResizeTemplate is the handle to drag them by), so any
    /// preview has to choose. Here they are spread evenly between the two
    /// offsets the format does declare, which at least fills the box the way
    /// the game does.</para>
    /// </summary>
    private static void ListBoxHeader(SKCanvas c, ElState s, XElement? tpl, double headH)
    {
        var heads = tpl?.Elements()
            .Where(e => e.Name.LocalName.Equals("HeaderControl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => Num(Xml.Tx(e, "Column")))
            .ToList();
        if (heads is null || heads.Count == 0) return;

        double lo = Num(Xml.Tx(tpl, "HeaderLeftOffset"), 3);
        double ro = Num(Xml.Tx(tpl, "HeaderRightOffset"), 3);
        double to = Num(Xml.Tx(tpl, "HeaderTopOffset"), 2);
        double avail = s.W - lo - ro;
        if (avail <= 0 || headH - to <= 0) return;
        double colW = avail / heads.Count;

        for (int i = 0; i < heads.Count; i++)
        {
            string name = Xml.Tx(heads[i], "TemplateName");
            var ht = s.Find(name, "ListBoxHeaderTemplate");
            if (ht is null)
            {
                // Into the inspection report with it, as for any other
                // template a package names and does not have.
                if (name.Length > 0 && name != "none")
                    s.Ctx.Package.MissingTemplates.Add(name);
                continue;
            }

            var isz = Pt(Xml.Sub(ht, "Texture"), "Size");
            double iw = isz?.X ?? 0, ih = isz?.Y ?? 0;

            var cell = new SKRect((float)(lo + i * colW), (float)to,
                (float)(lo + (i + 1) * colW), (float)headH);
            c.Save();
            c.ClipRect(cell);
            c.Translate(cell.Left, cell.Top);

            if (iw > 0 && ih > 0 && !NineSlice.DrawArea(c, s.Tex, ht, iw, ih, "Normal"))
                Placeholders.ContentAtRuntimeCell(c,
                    new SKRect(0, 0, (float)iw, (float)ih), 71);

            /* The caption comes from the run-time table, so it is a sample
               like any other; DAoCEd's own answer is the literal word "Test"
               for every column. The template name says more than that and
               costs nothing — "listboxheader_level" reads as "Level". */
            if (s.Opt.ShowSampleData)
            {
                var tf = Xml.Sub(ht, "TextFont");
                var box = new SKRect(0, (float)ih, cell.Width, (float)(headH - to));
                if (box.Height > 0)
                    TextDrawer.Draw(c, s.Fonts, Xml.NameOf(tf, "TokaFontBold10"),
                        HeaderCaption(name), box,
                        ColorOf(tf, "ColorNormal", TextDefault),
                        HAlign.Start, VAlign.Top, shadowAlpha: 0);
            }

            c.Restore();
            s.Drew = true;
        }
    }

    /// <summary>
    /// Readable caption for a header template name: the tail after the last
    /// underscore, capitalised. A naming convention, not the format — see
    /// <see cref="ListBoxHeader"/> for why a preview has to invent one at all.
    /// </summary>
    private static string HeaderCaption(string templateName)
    {
        int cut = templateName.LastIndexOf('_');
        string tail = cut >= 0 && cut < templateName.Length - 1
            ? templateName[(cut + 1)..] : templateName;
        return tail.Length == 0 ? "—" : char.ToUpperInvariant(tail[0]) + tail[1..];
    }

    /// <summary>
    /// Text area and chat window (original 1706).
    ///
    /// TextArea has a template of its own (<c>TextAreaTemplate</c>) with
    /// BackgroundTemplate, TextOffset and Font — it is NOT a list box with
    /// rows and icons.
    /// </summary>
    private static void TextArea(SKCanvas c, ElState s)
    {
        var taT = s.Tpl("TextAreaTemplate", "ChatControlTemplate");
        string bgN = Xml.Tx(taT, "BackgroundTemplate");
        var taBg = bgN.Length > 0 && bgN != "none" ? s.Find(bgN) : null;

        bool drewBg = taBg is not null && s.W != 0 && s.H != 0
            && NineSlice.DrawTemplate(c, s.Tex, taBg, s.W, s.H);
        if (!drewBg)
        {
            Placeholders.Fill(c, s.Box, new SKColor(8, 10, 14, 128));
            Placeholders.Border(c, s.Box, Placeholders.Brass(56));
            s.ShowedSubstitute = true;
        }
        s.Drew |= drewBg;

        if (s.Tag == "ChatControlDef" && s.Opt.ShowSampleData)
        {
            DrawChat(c, s);
            return;
        }

        var off = Pt(taT, "TextOffset") ?? new SKPoint(3, 3);
        var taF = Xml.Sub(taT, "Font");
        string fn = s.FontName.Length > 0 ? s.FontName : Xml.NameOf(taF, "TokaFontRegular10");
        var col = ColorOf(taF, "ColorNormal", TextDefault);

        var box = new SKRect(off.X, off.Y, (float)s.W - 2, (float)s.H - 2);
        if (box.Width <= 0 || box.Height <= 0) return;

        if (s.Opt.ShowSampleData)
        {
            string raw = s.Text.Length > 0
                ? s.Text
                : SampleData.For(SampleData.AdapterOf(s.Def), 0, s.Ctx.Reference) ?? "";
            if (raw.Length == 0) return;

            /* <HasHotspots>/<HotspotDelineator>: inside the text a delimiter
               (usually "{") marks clickable spots — quest goals in the game,
               say. They are highlighted in colour so it is visible that the
               area is interactive. */
            string del = Xml.Tx(s.Def, "HotspotDelineator");
            if (Flag(s.Def, "HasHotspots") && del.Length > 0 && raw.Contains(del))
            {
                var parts = raw.Split(del);
                var runs = new List<TextDrawer.Run> { new(parts[0], col) };
                for (int i = 1; i < parts.Length; i++)
                    runs.Add(new TextDrawer.Run(parts[i], new SKColor(0x7F, 0xC4, 0xE8), true));
                c.Save();
                c.ClipRect(box);
                TextDrawer.DrawRuns(c, s.Fonts, fn, runs, box);
                c.Restore();
            }
            else
            {
                c.Save();
                c.ClipRect(box);
                TextDrawer.Draw(c, s.Fonts, fn, raw, box, col,
                    HAlign.Start, VAlign.Top, shadowAlpha: 0);
                c.Restore();
            }
        }
        else if (s.Text.Length > 0)
        {
            c.Save();
            c.ClipRect(box);
            TextDrawer.Draw(c, s.Fonts, s.FontName.Length > 0 ? s.FontName : "TokaFontRegular10",
                s.Text, box, TextDefault, HAlign.Start, VAlign.Top, shadowAlpha: 0);
            c.Restore();
        }
    }

    /// <summary>
    /// Sample lines of the chat window. Lines in the reference data start with
    /// <c>~RRGGBB</c> — that is the colour of the line, not text.
    /// </summary>
    private static void DrawChat(SKCanvas c, ElState s)
    {
        // <BufferName> picks the text buffer (chat, system, …)
        string bufN = Xml.Tx(s.Def, "BufferName");
        if (bufN.Length == 0) bufN = "chat";
        if (!s.Ctx.Reference.Buffers.TryGetValue(bufN, out var lines))
            s.Ctx.Reference.Buffers.TryGetValue("chat", out lines);
        if (lines is null || lines.Count == 0) return;

        string fn = s.FontName.Length > 0 ? s.FontName : "TokaFontRegular11";
        // The chat sets line-height:1.25 — not the font's own line height.
        float rowH = s.Fonts.SizePx(fn) * 1.25f;
        var box = new SKRect(4, 2, (float)s.W - 4, (float)s.H - 2);  // padding:2px 4px
        if (box.Width <= 0 || box.Height <= 0) return;

        c.Save();
        c.ClipRect(box);
        float y = box.Top;
        foreach (string line in lines)
        {
            if (y >= box.Bottom) break;
            var (text, col) = SplitChatColor(line);
            TextDrawer.Draw(c, s.Fonts, fn, text,
                new SKRect(box.Left, y, box.Right, y + rowH), col,
                HAlign.Start, VAlign.Center, shadowAlpha: 0, lineHeight: rowH);
            y += rowH;
        }
        c.Restore();
    }

    /// <summary>Split off a leading <c>~RRGGBB</c> (original line 1731).</summary>
    private static (string Text, SKColor Color) SplitChatColor(string line)
    {
        if (line.Length >= 7 && line[0] == '~'
            && TryHex(line[1..7], out var col))
            return (line[7..], col);
        return (line, TextDefault);
    }
}
