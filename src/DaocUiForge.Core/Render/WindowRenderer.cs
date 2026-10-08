using System.Text.RegularExpressions;
using System.Xml.Linq;
using DaocUiForge.Core.Model;
using SkiaSharp;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Draws a whole window into an image. Ported from <c>renderWindow</c> of the
/// HTML original (lines 1991–2186).
///
/// <para><b>Order of work</b> (it comes from the original and is mandatory):
/// collect tabs → measure content → draw elements → make room for overflow.
/// The measurement has to happen before the elements, because
/// <c>&lt;GrowWidth&gt;</c>/<c>&lt;GrowHeight&gt;</c> read it through
/// <see cref="RenderContext.GrowW"/>.</para>
///
/// <para><b>Why this records too.</b> Elements may sit outside the window
/// frame — as they do in the game — including at negative positions (the
/// compass in a 14×14 window). The original fixes that up afterwards by
/// shifting the drawing surface in the DOM; an <see cref="SKBitmap"/> needs
/// its size up front. So the same move as one level down in
/// <see cref="ElementRenderer"/>: record into an <see cref="SKPicture"/>
/// first, then replay it shifted.</para>
///
/// <para><b>Synchronous</b> instead of <c>async</c>: the
/// <see cref="TextureCache"/> loads on first access, which removes the need
/// for the original's <c>renderSeq</c> counter.</para>
///
/// <para><b>Not included:</b> the selection marker of the original
/// (<c>.dw.sel</c>, dashed) and the faint outline around the drawing surface
/// (<c>.dw .surface::before</c>). Both are preview furniture rather than part
/// of the window — they belong in the app's preview control so that the image
/// here holds window pixels only.</para>
/// </summary>
public static class WindowRenderer
{
    /// <summary>
    /// Upper bound for an image edge. A typo in a <c>&lt;Position&gt;</c>
    /// (five-digit values do occur in packages that grew over the years)
    /// would otherwise allocate hundreds of megabytes. The original had no
    /// such limit — there it was merely a very large <c>&lt;div&gt;</c>. When
    /// the bound bites, the overflow is cut off and
    /// <see cref="RenderedWindow.Clipped"/> says so.
    /// </summary>
    public const int MaxSurface = 8192;

    /// <summary>
    /// Row height for the &lt;TabName&gt; placeholder (see
    /// <see cref="Placeholders.NativeTabBar"/>). Not a format value — no
    /// package specifies one, since the client draws this chrome itself. 16
    /// matches the other single-line UI rows in the packages looked at
    /// (e.g. the chat window's own input bar).
    /// </summary>
    private const float NativeTabBarHeight = 16f;

    /// <summary>Effect templates: <c>*_mez</c>, <c>*_diz</c>, <c>*_psn</c>, <c>*_ns</c>.</summary>
    private static readonly Regex EffectTemplate =
        new(@"(_mez|_diz|_psn|_ns)\d?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Group colours: <c>group_color1</c> … (they override a fixed &lt;Color&gt;).</summary>
    private static readonly Regex GroupColor =
        new(@"^group_color\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Draw one window of the package.</summary>
    public static RenderedWindow Render(RenderContext ctx, WindowDef win) =>
        Render(ctx, win.Node);

    /// <summary>
    /// Draw a &lt;WindowTemplate&gt; node. An overload of its own because
    /// tests and the template browser only have the node, not a
    /// <see cref="WindowDef"/>.
    /// </summary>
    public static RenderedWindow Render(RenderContext ctx, XElement node)
    {
        // Missing size: 300 × 200, as in the original (line 2007).
        double declW = Num(Xml.Tx(node, "Width"), 300);
        double declH = Num(Xml.Tx(node, "Height"), 200);
        ctx.WindowId = Xml.NameOf(node);

        // Only children whose name ends in "Def" are elements. Anything else
        // (TabsDef siblings such as TabControl, metadata) is not.
        var kids = node.Elements()
            .Where(e => e.Name.LocalName.EndsWith("Def", StringComparison.Ordinal))
            .ToList();

        var tabMap = CollectTabs(ctx, node, kids);

        /* Content extent — has to come before drawing (see above). Some
           windows (MiniInfo) grow with their content in the game: the declared
           size is only the starting value. */
        var (contentW, contentH) = MeasureContent(ctx, kids, tabMap, declW, declH);
        ctx.GrowW = contentW;
        ctx.GrowH = contentH;

        // Native <TabName> tags (no real TabsDef): see NativeTabBandHeight.
        // Has to be known before the elements are drawn, because it changes
        // where some of them land.
        var nativeTabs = node.Elements()
            .Where(e => e.Name.LocalName.Equals("TabName", StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Value ?? "").Trim())
            .Where(s => s.Length > 0)
            .ToList();
        ctx.NativeTabBandHeight = ctx.Options.ShowNativeTabs && nativeTabs.Count > 0
            ? NativeTabBarHeight : 0;

        // --- Record the elements (original 2124–2137) -------------------
        var elements = new List<RenderedElement>();
        var failures = new List<ElementFailure>();

        var recorder = new SKPictureRecorder();
        var rec = recorder.BeginRecording(
            new SKRect(-MaxSurface, -MaxSurface, MaxSurface, MaxSurface));

        foreach (var def in kids)
        {
            if (HiddenByTab(ctx, tabMap, def)) continue;
            // <Visible>false</Visible> hides an element (SetVisibleAction in the game).
            if (Xml.Tx(def, "Visible") == "false") continue;

            try
            {
                var el = ElementRenderer.Render(rec, ctx, def, declW, declH);
                if (el is not null) elements.Add(el);
            }
            catch (Exception ex)
            {
                // The original has `catch(e){ el=null; }`: one broken element
                // must not take the window down with it. Unlike there, the
                // failure stays visible — otherwise it can never be found in
                // the inspection report.
                failures.Add(new ElementFailure(def.Name.LocalName,
                    Xml.NameOf(def, def.Name.LocalName), ex.Message));
            }
        }

        using var picture = recorder.EndRecording();
        recorder.Dispose();

        /* --- Make room for overflow (original 2140–2158) ---------------
           Measured against the element boxes, as the original measures
           `surf.querySelectorAll('.el')` — which counts effect icons set to
           display:none as well, because the box is in the DOM regardless of
           visibility. */
        float minX = 0, minY = 0, maxX = (float)declW, maxY = (float)declH;
        foreach (var el in elements)
        {
            var b = el.Bounds;
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
        }

        int padL = (int)Math.Max(0, Math.Ceiling(-minX));
        int padT = (int)Math.Max(0, Math.Ceiling(-minY));
        bool overflow = padL != 0 || padT != 0 || maxX > declW || maxY > declH;

        int wantW = (int)Math.Ceiling(Math.Max(declW, maxX)) + padL;
        int wantH = (int)Math.Ceiling(Math.Max(declH, maxY)) + padT;
        int bmpW = Math.Clamp(wantW, 1, MaxSurface);
        int bmpH = Math.Clamp(wantH, 1, MaxSurface);

        // --- Replay ----------------------------------------------------
        var bmp = new SKBitmap(new SKImageInfo(bmpW, bmpH,
            SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);

            var frame = new SKRect(padL, padT, padL + (float)declW, padT + (float)declH);

            /* The title area: per the documentation only the zone for moving
               the window with the mouse, NOT a visible element.
               The original used to draw a grey bar here, which covered the
               content of flat windows (Clock, 15 px). It sits below the
               elements — through DOM order in the original, through drawing
               order here. */
            double titleW = Num(Xml.Tx(node, "TitleWidth"));
            double titleH = Num(Xml.Tx(node, "TitleHeight"));
            if (ctx.Options.ShowZones && titleW > 0 && titleH > 0)
                Placeholders.TitleZone(canvas, new SKRect(padL, padT,
                    padL + (float)titleW, padT + (float)titleH));

            /* The native tab row sits behind the elements too, like the title
               zone above: chat_window.xml puts its real type-to-chat line
               (a LabelDef + its background, ControlId 1002/1003) in exactly
               the band this placeholder covers, at <Y>0</Y> — the client
               shows one or the other there at runtime, and a static preview
               cannot choose between them. Drawing the placeholder first lets
               that real, declared content paint over it instead of being
               hidden under it; only the strip a window carries no such
               content for stays the placeholder's dashed boxes. */
            if (ctx.NativeTabBandHeight > 0)
            {
                int active = ctx.Options.ActiveTab is { } want
                    ? Math.Max(0, nativeTabs.FindIndex(n => n.Equals(want, StringComparison.OrdinalIgnoreCase)))
                    : 0;
                Placeholders.NativeTabBar(canvas, new SKRect(padL, padT,
                    padL + (float)declW, padT + NativeTabBarHeight), nativeTabs, active);
            }

            canvas.Save();
            canvas.Translate(padL, padT);
            canvas.DrawPicture(picture);
            canvas.Restore();

            // Move the element boxes into image coordinates — mouse picking
            // and the element tree run off these later.
            if (padL != 0 || padT != 0)
                foreach (var el in elements)
                {
                    var b = el.Bounds;
                    el.Bounds = new SKRect(b.Left + padL, b.Top + padT,
                        b.Right + padL, b.Bottom + padT);
                }

            // The guide goes above the elements (z-index:1 in the original).
            if (overflow) Placeholders.DeclaredFrame(canvas, frame);
        }

        return new RenderedWindow
        {
            Bitmap = bmp,
            DeclaredWidth = declW,
            DeclaredHeight = declH,
            ContentWidth = contentW,
            ContentHeight = contentH,
            Origin = new SKPointI(padL, padT),
            Clipped = wantW > bmpW || wantH > bmpH,
            Elements = elements,
            Failures = failures,
            DefCount = kids.Count,
            Tabs = ctx.Tabs.ToList(),
            ActiveTab = ctx.ActiveTab,
            TabMembers = tabMap.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal),
            Chrome = ReadChrome(node),
            HasEffects = HasEffects(kids),
        };
    }

    /// <summary>
    /// Collect the window's tabs (original 2062–2084).
    ///
    /// <para>&lt;TabsDef&gt; lists the tabs (&lt;Tab&gt;&lt;Id&gt;&lt;Name&gt;),
    /// and &lt;TabControl&gt; assigns, through &lt;ControlId&gt;, the elements
    /// that are only visible on a given tab. Elements without an assignment
    /// are always visible.</para>
    /// </summary>
    /// <returns>ControlId → ids of the tabs it appears on.</returns>
    private static Dictionary<string, List<string>> CollectTabs(
        RenderContext ctx, XElement node, List<XElement> kids)
    {
        ctx.Tabs.Clear();
        ctx.ActiveTab = ctx.Options.ActiveTab;

        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void AddTabControl(XElement tc)
        {
            string tid = Xml.Tx(tc, "TabId"), cid = Xml.Tx(tc, "ControlId");
            if (tid.Length == 0 || cid.Length == 0) return;
            if (!map.TryGetValue(cid, out var list)) map[cid] = list = new List<string>();
            list.Add(tid);
        }

        foreach (var def in kids)
        {
            if (def.Name.LocalName != "TabsDef") continue;
            foreach (var t in def.Elements().Where(c => c.Name.LocalName == "Tab"))
            {
                string id = Xml.Tx(t, "Id");
                if (id.Length == 0) continue;
                ctx.Tabs.Add(new TabInfo(id, Xml.NameOf(t, "Tab " + id)));
            }

            foreach (var tc in def.Elements().Where(c => c.Name.LocalName == "TabControl"))
                AddTabControl(tc);
        }

        // TabControl blocks that sit directly in the window count too.
        foreach (var tc in node.Elements().Where(c => c.Name.LocalName == "TabControl"))
            AddTabControl(tc);

        // Requested tab no longer there → the first one applies.
        if (ctx.Tabs.Count > 0 && !ctx.Tabs.Any(t => t.Id == ctx.ActiveTab))
            ctx.ActiveTab = ctx.Tabs[0].Id;

        return map;
    }

    /// <summary>
    /// Does the element belong to a different tab? Without an assignment it is
    /// always visible.
    /// </summary>
    private static bool HiddenByTab(RenderContext ctx,
        Dictionary<string, List<string>> tabMap, XElement def)
    {
        if (ctx.Tabs.Count == 0) return false;
        string cid = Xml.Tx(def, "ControlId");
        if (cid.Length == 0) return false;
        if (!tabMap.TryGetValue(cid, out var ids)) return false;
        return !ids.Contains(ctx.ActiveTab, StringComparer.Ordinal);
    }

    /// <summary>
    /// The actual content extent (original 2093–2122). The basis for
    /// <c>&lt;GrowWidth&gt;</c>/<c>&lt;GrowHeight&gt;</c>.
    ///
    /// <para>Three format rules all come together here:</para>
    /// <list type="bullet">
    /// <item>Elements of hidden tabs do NOT count — otherwise the window
    /// stretches to the extent of all tabs at once (which made the community
    /// window 690 wide instead of 605).</item>
    /// <item>Centred and edge-anchored elements do not count: their position
    /// is derived from the window size, which is precisely what is being
    /// established here.</item>
    /// <item>For growing elements only the starting point counts, not the
    /// size — otherwise the window ratchets itself up on them.</item>
    /// </list>
    ///
    /// <para>Measured against the XML nodes, not against
    /// <see cref="RenderedElement.Visible"/>: whether an effect icon is
    /// visible is only known after drawing, and drawing needs the result from
    /// here. The original solves it the same way.</para>
    /// </summary>
    private static (double W, double H) MeasureContent(RenderContext ctx,
        List<XElement> kids, Dictionary<string, List<string>> tabMap,
        double declW, double declH)
    {
        double contentW = declW, contentH = declH;

        foreach (var def in kids)
        {
            if (HiddenByTab(ctx, tabMap, def)) continue;
            if (Xml.Tx(def, "Visible") == "false") continue;

            var al = Xml.Sub(def, "Alignment");
            bool grow = false;
            if (al is not null)
            {
                if (Flag(al, "CenterHorizontally") || Flag(al, "CenterVertically")) continue;
                if (Flag(al, "OffsetRight") || (!Flag(al, "TopLeft") && Flag(al, "OffsetBottom")))
                    continue;
                grow = Flag(al, "GrowWidth") || Flag(al, "GrowHeight");
            }

            var p = Pt(def, "Position") ?? new SKPoint(0, 0);
            double dw = Num(Xml.Tx(def, "Width")), dh = Num(Xml.Tx(def, "Height"));

            if (grow)
            {
                if (p.X > contentW) contentW = p.X;
                if (p.Y > contentH) contentH = p.Y;
            }
            else
            {
                if (dw > 0 && p.X + dw > contentW) contentW = p.X + dw;
                if (dh > 0 && p.Y + dh > contentH) contentH = p.Y + dh;
            }
        }

        return (contentW, contentH);
    }

    /// <summary>
    /// Corner buttons and drag zones of the window frame (docs 3.5, original
    /// 2036–2060). They are not elements in the window; the engine creates
    /// them from the window template.
    ///
    /// <para><b>Why nothing is drawn here.</b> The original creates a
    /// <c>&lt;div class="wbtn …"&gt;</c> per button — and there is not one CSS
    /// rule for it. Those buttons have neither size nor colour there, so
    /// nothing becomes visible. Rather than invent a size (the format names
    /// none; only the offset from <c>ResizeButtonOffsetX/Y</c>), the values are
    /// passed on as data. The preview control can draw handles from them, and
    /// the later inspection report gets to see the fields.</para>
    /// </summary>
    private static WindowChrome ReadChrome(XElement node)
    {
        bool Yes(string tag) => Xml.Tx(node, tag) == "true";
        return new WindowChrome
        {
            MoveButton = Yes("MoveButton"),
            CloseButton = Yes("CloseButton"),
            TopRightResizeButton = Yes("TopRightResizeButton"),
            BottomRightResizeButton = Yes("BottomRightResizeButton"),
            BottomLeftResizeButton = Yes("BottomLeftResizeButton"),
            ResizeButtonOffset = new SKPoint(
                (float)Num(Xml.Tx(node, "ResizeButtonOffsetX")),
                (float)Num(Xml.Tx(node, "ResizeButtonOffsetY"))),
            TitleWidth = Num(Xml.Tx(node, "TitleWidth")),
            TitleHeight = Num(Xml.Tx(node, "TitleHeight")),
        };
    }

    /// <summary>
    /// Does the window carry effect icons or group colours? Only then is the
    /// state selector of the preview worth offering (original 2160–2163).
    /// </summary>
    private static bool HasEffects(List<XElement> kids)
    {
        foreach (var d in kids)
        {
            if (EffectTemplate.IsMatch(Xml.Tx(d, "TemplateName"))) return true;
            // Two lookups in the original (ColorAdapter/Coloradapter) — Xml.Tx
            // already compares the element name case-insensitively.
            if (GroupColor.IsMatch(Xml.Tx(d, "ColorAdapter"))) return true;
        }
        return false;
    }

    /// <summary>A yes/no field in the DAoC sense: exactly the text "true".</summary>
    private static bool Flag(XElement? node, string tag) => Xml.Tx(node, tag) == "true";
}

/// <summary>
/// Result of one window pass: the image and everything the app and the
/// inspection report need to know about it. Replaces the drawing surface plus
/// status lines of the original.
///
/// <para>The caller takes over the <see cref="Bitmap"/> — hence
/// <see cref="IDisposable"/>.</para>
/// </summary>
public sealed class RenderedWindow : IDisposable
{
    /// <summary>The drawn window. Transparent wherever nothing was painted.</summary>
    public required SKBitmap Bitmap { get; init; }

    /// <summary>Size according to the XML (&lt;Width&gt;/&lt;Height&gt;).</summary>
    public required double DeclaredWidth { get; init; }

    public required double DeclaredHeight { get; init; }

    /// <summary>
    /// Extent of the content. Larger than the declared size when the window
    /// grows in the game; growing elements fill up to here.
    /// </summary>
    public required double ContentWidth { get; init; }

    public required double ContentHeight { get; init; }

    /// <summary>
    /// Where the window's point (0,0) sits in the image. Other than (0,0) when
    /// elements are at negative positions — the image then gets room on the
    /// left and top so they stay visible.
    /// </summary>
    public required SKPointI Origin { get; init; }

    /// <summary>The overflow exceeded <see cref="WindowRenderer.MaxSurface"/>.</summary>
    public bool Clipped { get; init; }

    /// <summary>
    /// The elements drawn, with their place in the image — the basis for mouse
    /// picking, the element tree and the inspection report.
    /// </summary>
    public required IReadOnlyList<RenderedElement> Elements { get; init; }

    /// <summary>Elements that threw an exception (normally empty).</summary>
    public required IReadOnlyList<ElementFailure> Failures { get; init; }

    /// <summary>
    /// Number of element nodes in the window — including the skipped ones
    /// (hidden tabs, <c>&lt;Visible&gt;false&lt;/Visible&gt;</c>). The original
    /// shows this as "N elements".
    /// </summary>
    public required int DefCount { get; init; }

    /// <summary>Tabs of the window; empty when it has none.</summary>
    public required IReadOnlyList<TabInfo> Tabs { get; init; }

    /// <summary>The tab whose elements were drawn.</summary>
    public string? ActiveTab { get; init; }

    /// <summary>
    /// Which tabs an element appears on: &lt;ControlId&gt; → tab ids. Built
    /// from the &lt;TabControl&gt; blocks, and the same table the drawing pass
    /// decides visibility with — the original keeps it in the global
    /// <c>tabMap</c> and its element tree reads it from there.
    ///
    /// <para>An element whose ControlId is absent from this belongs to no tab
    /// and is therefore always visible.</para>
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TabMembers { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>Corner buttons and drag zones of the frame.</summary>
    public required WindowChrome Chrome { get; init; }

    /// <summary>
    /// Windows with effect icons or group colours. Only there does the state
    /// selector make sense (<see cref="RenderOptions.SimulatedState"/>).
    /// </summary>
    public bool HasEffects { get; init; }

    /// <summary>Elements whose template is missing from the package.</summary>
    public int MissingTemplateCount =>
        Elements.Count(e => e.MissingTemplate is not null);

    /// <summary>Does the content reach beyond the declared size?</summary>
    public bool Grows => ContentWidth > DeclaredWidth || ContentHeight > DeclaredHeight;

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// Corner buttons and drag zones of a window. The engine creates them from the
/// window template; they are not elements in the XML.
/// </summary>
public sealed class WindowChrome
{
    public bool MoveButton { get; init; }
    public bool CloseButton { get; init; }

    /// <summary>Only in effect when no <see cref="CloseButton"/> is set.</summary>
    public bool TopRightResizeButton { get; init; }

    public bool BottomRightResizeButton { get; init; }
    public bool BottomLeftResizeButton { get; init; }

    /// <summary>Offset of the lower resize buttons (ResizeButtonOffsetX/Y).</summary>
    public SKPoint ResizeButtonOffset { get; init; }

    /// <summary>Drag zone for moving — not a visible bar.</summary>
    public double TitleWidth { get; init; }

    public double TitleHeight { get; init; }
}

/// <summary>An element that threw while being drawn.</summary>
/// <param name="Tag">Element type, e.g. "ButtonDef".</param>
/// <param name="Name">Name of the element, as far as there is one.</param>
/// <param name="Message">Message of the exception.</param>
public readonly record struct ElementFailure(string Tag, string Name, string Message);
