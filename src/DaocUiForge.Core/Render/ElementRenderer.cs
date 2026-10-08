using System.Xml.Linq;
using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Draws a single element of a window. Ported from <c>renderEl</c> of the
/// HTML original (lines 1272–1986).
///
/// <para><b>Why this records instead of drawing straight away.</b> The
/// original builds a detached <c>&lt;div&gt;</c>, draws into it in local
/// coordinates and sets <c>left</c>/<c>top</c> ONLY AFTERWARDS (line 1956).
/// That is no accident: edge-relative alignment (<c>OffsetRight</c>,
/// <c>OffsetBottom</c>, centring) needs the final width and height, and for
/// most element types those only come out of the template. An
/// <c>SKCanvas</c>, however, wants to know the offset before drawing. So the
/// content is recorded into an <see cref="SKPicture"/> and then replayed
/// shifted and clipped — the exact counterpart of the detached DOM node. That
/// keeps the order of the original instead of separating size measurement
/// from drawing and letting the two drift apart.</para>
///
/// <para><b>Synchronous.</b> This was <c>async</c> in the original and needed
/// a <c>renderSeq</c> cancellation counter. Here the
/// <see cref="TextureCache"/> loads on first access and keeps the result, so
/// the concurrency goes away.</para>
/// </summary>
public static partial class ElementRenderer
{
    /// <summary>
    /// Recording area. Generous, because the final size of the element is not
    /// yet known at this point; the replay clips to the element box anyway.
    /// </summary>
    private static readonly SKRect RecordArea = new(-4096, -4096, 8192, 8192);

    /// <summary>
    /// The colour a text carries when nothing declares one — <b>white</b>.
    ///
    /// <para>Not a stand-in but the format's own default: DAoCEd's
    /// <c>ColorProperty</c> starts at r=g=b=a=255 and only overwrites those
    /// from the XML, so a missing <c>&lt;Color&gt;</c>, a missing
    /// <c>&lt;ColorNormal&gt;</c> and an unparsable one all come out white.
    /// The three warm tones that stood here (<c>#f0e6d0</c>, <c>#e8dcc0</c>,
    /// <c>#c8bfa8</c>) were carried over from the HTML original's stylesheet
    /// and gave every undeclared text a golden tint the game does not
    /// have.</para>
    /// </summary>
    private static readonly SKColor TextDefault = SKColors.White;

    /// <summary>
    /// Draw one element onto the surface.
    /// </summary>
    /// <param name="canvas">Target surface of the window.</param>
    /// <param name="ctx">Package, textures, fonts, switches.</param>
    /// <param name="def">The XML node of the element.</param>
    /// <param name="winW">Window width (for alignment and growing).</param>
    /// <param name="winH">Window height.</param>
    /// <returns>
    /// The drawn element together with its place — the basis for mouse
    /// picking, the element tree and the inspection report. <c>null</c> for an
    /// unknown type (<c>default: return null</c> in the original).
    /// </returns>
    public static RenderedElement? Render(SKCanvas canvas, RenderContext ctx,
        XElement def, double winW, double winH)
    {
        var s = new ElState(ctx, def, winW, winH);

        // --- Preamble (original 1273–1306) ---------------------------
        var pos = Pt(def, "Position") ?? new SKPoint(0, 0);
        s.W = Num(Xml.Tx(def, "Width"));
        s.H = Num(Xml.Tx(def, "Height"));

        // Some Def types name their template differently.
        s.TplName = First(def, "TemplateName", "Templatename",
            "HRButtonTemplateName", "ImageAreaTemplateName", "IconTemplateName");

        // Does the element point at a template the package does not have?
        string? missingTpl = null;
        if (s.TplName.Length > 0 && s.TplName != "none" && FindAny(ctx, s.TplName) is null)
        {
            missingTpl = s.TplName;
            ctx.Package.MissingTemplates.Add(s.TplName);
        }

        s.Align = Xml.Sub(def, "Alignment");
        double left = pos.X, top = pos.Y;

        /* A TabsDef lands at TWICE its <Position> — the client lays the tab
           buttons and the control area out from the control's own position a
           second time, inside the box it has already put the control in. The
           size stays <Width> x <Height>; only the origin moves.

           Measured against the running game (community_ingame.png): the
           declared box is (4,15) 590x368 in a 605x410 window, and all four
           edges of the drawn control sit at (8,30)-(598,398). Confirmed by
           training_window, whose tab content the doubling explains and the
           declared position does not: its vertical slider spans y 156-486
           inside a doubled box of 140-490, where the declared box (70-420)
           would cut 66 px off it.

           DAoCEd does NOT do this (ControlNode.getAlignedBounds knows nothing
           of it), so its preview and the HTML original both put the row 15 px
           too high — which is what made community_window's REFRESH button
           appear to be crossed by the tab frame. */
        if (s.Tag == "TabsDef") { left += pos.X; top += pos.Y; }

        /* GrowWidth/GrowHeight: the element grows WITH the window, by the
           amount the window itself gained. It does not stretch to the window
           edge — at the declared size a growing element keeps exactly its
           declared one. Without a declared size on that axis there
           is nothing to add to, and the size keeps coming from the template,
           just as it does without the flag. */
        if (s.Align is not null)
        {
            if (s.W > 0 && Flag(s.Align, "GrowWidth")) s.W += Math.Max(0, ctx.GrowW - winW);
            if (s.H > 0 && Flag(s.Align, "GrowHeight")) s.H += Math.Max(0, ctx.GrowH - winH);
        }

        /* The "chat" window's type-to-chat row (ControlId 1001-1004) is not
           where its own <Position> says. DAoCEd does not read this from the
           format either; it special-cases a window literally named "chat",
           keyed on these four exact ids:

             1002 (background) / 1003 (label): y = winH - declaredH - 4,
                                                x = 50, width -= 50
             1001 (emote button):              y = winH - 22, x = 28
             1004 (channel button):            y = winH - 22, x = 6

           So this is package-specific chrome the client positions itself,
           the same way a TabsDef lands at twice its position above — not a
           layout rule this renderer can derive from the XML in general, and
           not something to "fix" by rewriting the package (its <Position>
           of <Y>0</Y> is correct, DAoCEd's own editor just does not draw it
           there). Preview only; the XML is untouched. */
        if (ctx.WindowId.Equals("chat", StringComparison.OrdinalIgnoreCase))
        {
            string cid = Xml.Tx(def, "ControlId");
            if (cid == "1002" || cid == "1003") { top = winH - s.H - 4; left = 50; s.W -= 50; }
            else if (cid == "1001") { top = winH - 22; left = 28; }
            else if (cid == "1004") { top = winH - 22; left = 6; }
        }

        // Read display text WITHOUT trimming: in DAoC packages leading
        // spaces are alignment, not an accident (see Xml.TxDisplay).
        s.Text = FirstDisplay(def, "Data", "Label", "Text");
        s.FontName = First(def, "FontName", "Font");

        // --- Record the content (original 1308–1934) -----------------
        var recorder = new SKPictureRecorder();
        var rec = recorder.BeginRecording(RecordArea);
        bool known = Dispatch(rec, s);
        using var picture = recorder.EndRecording();
        recorder.Dispose();

        if (!known) return null;   // original: default: return null

        // --- Edge-relative alignment (original 1936–1959) ------------
        // W/H are settled now.
        if (s.Align is not null)
        {
            /* OffsetRight/OffsetBottom decide which edge an element sticks
               to when the window is enlarged.

               Vertically, <OffsetBottom> ALWAYS measures from the bottom
               edge — together with TopLeft as well. In custom3 the group
               concerned (bar, percentage, realm rank/level/rp, separator)
               forms a coherent footer that way.

               Horizontally <OffsetRight> also holds together with TopLeft.
               Checked against an in-game screenshot 2026-10-08: the
               stats_index tab row (TopLeft+OffsetRight, X 0..140) is drawn in
               reverse order, the X=0 inventory tab rightmost, which leaves the
               T/G buttons at X=8 uncovered; the "%" of float_level_exp_window
               sits at the right edge. Right/EndAligned only without TopLeft. */
            bool anchored = Flag(s.Align, "TopLeft");
            if (Flag(s.Align, "OffsetRight")
                || (!anchored && (Flag(s.Align, "Right") || Flag(s.Align, "EndAligned"))))
                left = winW - s.W - pos.X;
            if (Flag(s.Align, "Bottom") || Flag(s.Align, "OffsetBottom"))
                top = winH - s.H - pos.Y;
            if (Flag(s.Align, "CenterHorizontally"))
            {
                if (s.W == 0) s.W = winW;
                left = JsRound((winW - s.W) / 2);
            }
            if (Flag(s.Align, "CenterVertically"))
            {
                if (s.H == 0) s.H = winH;
                top = JsRound((winH - s.H) / 2);
            }
        }

        float bx = (float)JsRound(left), by = (float)JsRound(top);

        // Without <Width>/<Height> the box was content-sized in the
        // original. So the measured extent counts for the hit area — without
        // it a text field lacking <Width> would be zero pixels wide and not
        // clickable.
        float visW = s.W != 0 ? (float)JsRound(s.W) : s.ContentW;
        float visH = s.H != 0 ? (float)JsRound(s.H) : s.ContentH;
        var bounds = new SKRect(bx, by, bx + visW, by + visH);

        /* --- Report: texture declared, but not in the package ---------
           The template is there, yet no texture area was drawn. Without a
           marker an empty frame would sit there looking like a real control
           (original 1965–1984).

           DELIBERATE DEVIATION FROM THE ORIGINAL: there the condition hangs
           off `el.childElementCount`. But the drawing functions always return
           a host <div> that the caller appends unconditionally — for plain
           image and button elements the count is therefore never 0, and the
           marker fails to fire in exactly the place its own comment intends.
           Only the branches where the author additionally wrote
           `if(...childElementCount)` mark anything at all. What counts here
           instead is whether texture pixels were drawn and whether the branch
           already showed a substitute of its own. */
        var noTex = missingTpl is null && !s.Drew && !s.ShowedSubstitute
            ? TexturesOutsidePackage(ctx, def, s.TplName)
            : Array.Empty<string>();

        var result = new RenderedElement
        {
            Def = def,
            Tag = def.Name.LocalName,
            Visible = !s.Hidden,
            MissingTemplate = missingTpl,
            MissingTextures = noTex,
        };
        result.Bounds = bounds;

        if (s.Hidden) return result;   // display:none — the box counts, the content does not

        // --- Replay ---------------------------------------------------
        canvas.Save();
        // .el { overflow:hidden } — but ONLY on the axes whose size is
        // actually declared. Without <Width> the box was content-wide in the
        // original and clipped nothing; clipping to 0 here would make the
        // content vanish entirely.
        // CheckBoxDef turns clipping off completely (overflow:visible),
        // because its label sits beside the box.
        if (s.ClipContent)
            canvas.ClipRect(new SKRect(bx, by,
                s.W != 0 ? bx + (float)JsRound(s.W) : RecordArea.Right,
                s.H != 0 ? by + (float)JsRound(s.H) : RecordArea.Bottom));
        canvas.Translate(bx, by);
        canvas.DrawPicture(picture);
        canvas.Restore();

        // Markers last, so they end up above the content.
        if (ctx.Options.MarkMissing && missingTpl is not null)
            Placeholders.MissingTemplate(canvas, bounds);
        else if (noTex.Count > 0)
            Placeholders.TextureOutsidePackage(canvas, bounds);

        return result;
    }

    /// <summary>
    /// Type dispatch. Returns false when the type is unknown — the
    /// <c>default: return null</c> branch of the original.
    /// </summary>
    private static bool Dispatch(SKCanvas c, ElState s)
    {
        switch (s.Tag)
        {
            case "FullResizeImageDef": FullResizeImage(c, s); return true;

            case "HorizontalResizeImageDef":
            case "HorizontalResizeImageButtonDef":
            case "HorizontalResizeButtonDef": HorizontalResize(c, s); return true;

            case "VerticalResizeImageDef": VerticalResize(c, s); return true;

            case "ImageAreaDef":
            case "DynamicImageDef": ImageArea(c, s); return true;

            case "ButtonDef":
            case "InvisibleButtonDef":
            case "CheckBoxDef": Button(c, s); return true;

            case "StatusBarDef":
            case "VerticalStatusbarDef": StatusBar(c, s); return true;

            case "StatusIconDef":
            case "IconDef":
            case "DockableIconDef": Icon(c, s); return true;

            case "ScalarLabelDef":
            case "LabelDef": Label(c, s); return true;

            case "ListBoxDef":
            case "TreeControlDef": ListBox(c, s); return true;

            case "TextAreaDef":
            case "ChatControlDef": TextArea(c, s); return true;

            case "ComboBoxDef": ComboBox(c, s); return true;
            case "HorizontalSliderDef": HorizontalSlider(c, s); return true;
            case "VerticalSliderDef": VerticalSlider(c, s); return true;
            case "CompassControlDef": Compass(c, s); return true;
            case "TabsDef": Tabs(c, s); return true;
            case "IconSetDef": IconSet(c, s); return true;

            case "EditBoxDef":
            case "ClickableEditBoxDef": EditBox(c, s); return true;

            case "StaticFileImageDef": StaticFileImage(c, s); return true;

            default: return false;
        }
    }

    // --- shared helpers ----------------------------------------------

    /// <summary>First non-empty text value out of several spellings.</summary>
    private static string First(XElement? node, params string[] tags)
    {
        foreach (var t in tags)
        {
            string v = Xml.Tx(node, t);
            if (v.Length > 0) return v;
        }
        return "";
    }

    /// <summary>
    /// Like <see cref="First"/>, but for display text: keeps leading spaces
    /// that are meant as alignment (see <see cref="Xml.TxDisplay"/>).
    /// </summary>
    private static string FirstDisplay(XElement? node, params string[] tags)
    {
        foreach (var t in tags)
        {
            string v = Xml.TxDisplay(node, t);
            if (v.Length > 0) return v;
        }
        return "";
    }

    /// <summary>A yes/no field in the DAoC sense: exactly the text "true".</summary>
    private static bool Flag(XElement? node, string tag) => Xml.Tx(node, tag) == "true";

    /// <summary>
    /// A number where 0 counts as "not set". Mirrors the JS pattern
    /// <c>num(tx(x,'Height'),16)||16</c>: a missing value AND an explicit 0
    /// both fall back.
    /// </summary>
    private static double NumNz(string? s, double fallback)
    {
        double v = Num(s, fallback);
        return v != 0 ? v : fallback;
    }

    /// <summary>
    /// First value that is not 0. Mirrors the JS chain <c>a||b||c</c> on
    /// numbers, where 0 counts as "not set".
    /// </summary>
    private static double FirstNonZero(params double[] values)
    {
        foreach (double v in values)
            if (v != 0) return v;
        return 0;
    }

    /// <summary>Find a template without a type hint (<c>findTpl(name)</c> in the original).</summary>
    private static XElement? FindAny(RenderContext ctx, string? name) =>
        Ingest.PackageLoader.FindTemplate(ctx.Package, name);

    /// <summary>
    /// Look up the value and maximum tables: exact spelling first, then
    /// lower-cased — exactly like
    /// <c>A.s[ad]!==undefined?A.s[ad]:A.s[adl]</c> in the original.
    /// Deliberately NO wider search: "no value found" is a real branch with
    /// its own handling in the original.
    /// </summary>
    private static string? RefValue(IReadOnlyDictionary<string, string> table, string key)
    {
        if (table.TryGetValue(key, out var exact)) return exact;
        return table.TryGetValue(key.ToLowerInvariant(), out var lower) ? lower : null;
    }

    /// <summary>
    /// Look up the colour table. Here the original explicitly searches
    /// case-insensitively (line 1593).
    /// </summary>
    private static string? RefColor(IReadOnlyDictionary<string, string> table, string key)
    {
        if (table.TryGetValue(key, out var exact)) return exact;
        foreach (var kv in table)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    /// <summary>
    /// Fill level from the reference values: value/maximum, otherwise a value
    /// that works as a percentage. <c>null</c> when both fail.
    /// </summary>
    private static double? RefFraction(RenderContext ctx, string adapter, out double raw)
    {
        raw = double.NaN;
        string? cur = RefValue(ctx.Reference.Current, adapter);
        string? max = RefValue(ctx.Reference.Max, adapter);
        double c = Num(cur, double.NaN), mx = Num(max, double.NaN);
        raw = c;
        if (!double.IsNaN(c) && !double.IsNaN(mx) && mx > 0)
            return Math.Clamp(c / mx, 0, 1);
        if (!double.IsNaN(c) && c >= 0 && c <= 100) return c / 100;
        return null;
    }

    /// <summary>
    /// Textures that an existing template declares but that are not in the
    /// package. The usual case is a reference into the game folder
    /// (<c>atlantis/emoticons.tga</c>) — that is NOT a package error and is
    /// reported separately.
    /// </summary>
    private static IReadOnlyList<string> TexturesOutsidePackage(
        RenderContext ctx, XElement def, string tplName)
    {
        List<string>? used = null;
        foreach (var name in new[]
                 {
                     tplName,
                     Xml.Tx(def, "ImageAreaTemplateName"),
                     Xml.Tx(def, "HRButtonTemplateName"),
                 })
        {
            if (name.Length == 0 || name == "none") continue;
            var tp = FindAny(ctx, name);
            if (tp is null) continue;
            var tb = Xml.Sub(tp, "Texture") ?? tp;
            string tn = Xml.Tx(tb, "TextureName");
            if (tn.Length == 0) tn = Xml.Tx(tp, "TextureName");
            if (tn.Length == 0 || tn == "none") continue;
            if (!ctx.Package.MissingTextures.Contains(tn)) continue;
            used ??= new List<string>();
            if (!used.Contains(tn, StringComparer.OrdinalIgnoreCase)) used.Add(tn);
        }
        return used ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>
    /// Running state of one element. These were local variables of
    /// <c>renderEl</c> in the original; the case branches mutate them, hence
    /// a class rather than a value type.
    /// </summary>
    private sealed class ElState
    {
        public ElState(RenderContext ctx, XElement def, double winW, double winH)
        {
            Ctx = ctx;
            Def = def;
            Tag = def.Name.LocalName;
            WinW = winW;
            WinH = winH;
        }

        public RenderContext Ctx { get; }
        public XElement Def { get; }
        public string Tag { get; }
        public double WinW { get; }
        public double WinH { get; }

        public string TplName { get; set; } = "";
        public XElement? Align { get; set; }
        public string Text { get; set; } = "";
        public string FontName { get; set; } = "";

        /// <summary>Width/height — the case branches refine them from the template.</summary>
        public double W { get; set; }

        public double H { get; set; }

        /// <summary>Was anything actually drawn? Governs the stand-in shapes.</summary>
        public bool Drew { get; set; }

        /// <summary>
        /// Measured content width when no &lt;Width&gt; is set. In the
        /// original the content determined the box width there (CSS
        /// shrink-to-fit, because <c>el.style.width</c> was only set under
        /// <c>if(w)</c>). This does NOT feed into alignment — that keeps
        /// computing with <see cref="W"/> = 0, otherwise right-anchored
        /// elements shift. For clipping and the hit area only.
        /// </summary>
        public float ContentW { get; set; }

        public float ContentH { get; set; }

        /// <summary>
        /// The branch already showed a substitute of its own (stripes, a
        /// stand-in area, a gradient). The general "texture missing" marker
        /// then stays away — otherwise two markers would sit on top of each
        /// other.
        /// </summary>
        public bool ShowedSubstitute { get; set; }

        /// <summary>display:none — only for effect icons without a matching state.</summary>
        public bool Hidden { get; set; }

        /// <summary>overflow:hidden (the rule). CheckBoxDef turns it off.</summary>
        public bool ClipContent { get; set; } = true;

        // --- convenience ---------------------------------------------
        public TextureCache Tex => Ctx.Textures;
        public FontProvider Fonts => Ctx.Fonts;
        public RenderOptions Opt => Ctx.Options;

        /// <summary>Find the element's template, preferring the expected types.</summary>
        public XElement? Tpl(params string[] preferredTags) =>
            Ingest.PackageLoader.FindTemplate(Ctx.Package, TplName, preferredTags);

        /// <summary>Find any template by name.</summary>
        public XElement? Find(string? name, params string[] preferredTags) =>
            Ingest.PackageLoader.FindTemplate(Ctx.Package, name, preferredTags);

        /// <summary>The element box in local coordinates.</summary>
        public SKRect Box => new(0, 0, (float)W, (float)H);
    }
}
