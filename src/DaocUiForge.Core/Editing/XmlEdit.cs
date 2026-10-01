using System.Globalization;
using System.Xml.Linq;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// Writing counterpart to <see cref="Xml"/>: the helpers the property editor
/// needs to put a value onto the XML tree. Ported from <c>ensure</c> and
/// <c>setSub</c> of the HTML original (lines 2304–2313).
///
/// <para>The principle of the original is kept: every change goes straight onto
/// the tree of <see cref="WindowDef.Doc"/>. Nothing is tracked alongside it, so
/// saving is a plain serialisation.</para>
/// </summary>
public static class XmlEdit
{
    /// <summary>
    /// The child element with this name, created and appended when absent.
    ///
    /// <para>Finding is case-insensitive (through <see cref="Xml.Sub"/>), so an
    /// existing <c>&lt;Adaptername&gt;</c> is found by "AdapterName" and keeps
    /// the spelling the file already uses. Only a genuinely new element gets
    /// the spelling passed in here.</para>
    /// </summary>
    public static XElement Ensure(XElement node, string tag)
    {
        var found = Xml.Sub(node, tag);
        if (found is not null) return found;

        // The namespace of the parent, so a namespaced document stays
        // consistent. DAoC files carry none, and then this is simply "".
        var created = new XElement(node.Name.Namespace + tag);

        return Append(node, created);
    }

    /// <summary>
    /// Put a newly built element at the end of <paramref name="parent"/>, on a
    /// line of its own with the indentation its siblings use — and, when it
    /// brings children of its own, laid out to match the rest of the file.
    ///
    /// <para>Behind the last child element rather than at the end of the node:
    /// appending to the node puts the newcomer after the whitespace that
    /// indents the closing tag, so the tag ends up on the same line and a
    /// one-value change shows as two changed lines in the diff.</para>
    /// </summary>
    public static XElement Append(XElement parent, XElement fresh)
    {
        string unit = IndentUnitOf(parent.Document);

        var last = parent.Elements().LastOrDefault();
        var indent = last is null ? null : IndentOf(last);

        // Without a sibling to copy from, one step in from the parent's own
        // line — which is where the first child of a container belongs.
        string line = indent?.Value
                      ?? (IndentOf(parent) is { } own ? own.Value + unit : "");

        Lay(fresh, line, unit);

        if (indent is not null) last!.AddAfterSelf(indent.Value, fresh);
        else parent.Add(fresh);

        return fresh;
    }

    // -----------------------------------------------------------------
    // Indentation
    // -----------------------------------------------------------------
    //
    // The loader parses with LoadOptions.PreserveWhitespace, so every line
    // break and indent of the file is in the tree as a text node. That is what
    // lets PackageWriter write an untouched file back byte for byte — and it
    // means an element moved on its own leaves its indentation behind. Two
    // elements end up on one line, the diff covers half the file, and none of
    // it is visible in the editor.
    //
    // The rule that holds it together: an element travels with the whitespace
    // in front of it, and lands in front of the whitespace of the element it
    // displaces.

    /// <summary>
    /// The whitespace directly in front of a node — its indentation — or null
    /// when the document carries none (parsed without
    /// <see cref="LoadOptions.PreserveWhitespace"/>, or all on one line).
    /// </summary>
    public static XText? IndentOf(XNode node) =>
        node.PreviousNode is XText t && t.Value.Trim().Length == 0 ? t : null;

    /// <summary>The step to assume when the document shows none of its own.</summary>
    private const string DefaultIndent = "    ";

    /// <summary>
    /// The indentation step this document uses — four spaces, a tab, whatever
    /// it happens to be.
    ///
    /// <para>Read off the file rather than assumed. A created element that
    /// indents differently from its neighbours puts a style change into the
    /// diff on top of the real one, and the packages in the wild are not
    /// consistent: the reference package has files on four spaces and files on
    /// tabs.</para>
    /// </summary>
    public static string IndentUnitOf(XDocument? doc)
    {
        if (doc?.Root is null) return DefaultIndent;

        foreach (var e in doc.Root.Descendants())
        {
            if (e.Parent is null) continue;
            if (IndentOf(e) is not XText mine || IndentOf(e.Parent) is not XText outer) continue;

            // Only what stands behind the last line break counts. Blank lines
            // between elements are common in these files, and comparing the
            // whole text node would let one of them defeat every match.
            string inner = LastLine(mine.Value), around = LastLine(outer.Value);
            if (inner.Length > around.Length && inner.StartsWith(around, StringComparison.Ordinal))
                return inner[around.Length..];
        }

        return DefaultIndent;
    }

    /// <summary>What stands behind the last line break — the indent alone.</summary>
    private static string LastLine(string s)
    {
        int nl = s.LastIndexOf('\n');
        return nl < 0 ? s : s[(nl + 1)..];
    }

    /// <summary>
    /// The last line break of the text together with the indent behind it —
    /// <c>"\r\n\t\t"</c> out of <c>"\r\n\r\n\t\t"</c>.
    ///
    /// <para>The break is taken, not rebuilt: a file stored with CRLF has
    /// <c>"\r\n"</c> in these nodes, and writing <c>"\n"</c> back would put one
    /// LF line into a file where every other line ends CRLF.</para>
    /// </summary>
    private static string LastBreak(string s)
    {
        int nl = s.LastIndexOf('\n');
        if (nl < 0) return s;
        return s[(nl > 0 && s[nl - 1] == '\r' ? nl - 1 : nl)..];
    }

    /// <summary>
    /// Give a freshly built subtree the whitespace of the file it is going
    /// into, so a created element reads like the ones around it instead of
    /// arriving as one long line.
    ///
    /// <para>Only newly built trees: a node that already carries whitespace
    /// (one copied out of the document, say) is left exactly as it is.</para>
    /// </summary>
    /// <param name="el">The element, at the point where its own tag sits.</param>
    /// <param name="line">The line break and indent in front of that tag.</param>
    /// <param name="unit">One step further in.</param>
    private static void Lay(XElement el, string line, string unit)
    {
        if (!el.HasElements) return;

        // No line to hang it off — the document keeps everything on one, and
        // breaking it here would be the only change in the file.
        if (!line.Contains('\n')) return;

        // Already laid out; not ours to reformat.
        if (el.Nodes().OfType<XText>().Any(t => t.Value.Trim().Length == 0)) return;

        // One line break and the indent behind it — not the blank lines a file
        // may keep between its top-level entries. Those separate one entry from
        // the next; they are not a level, and carrying them inwards would put
        // two empty lines in front of every field of the new element. The break
        // itself is taken from the file rather than written as "\n", so a CRLF
        // file does not gain an LF line.
        string at = LastBreak(line);
        string inner = at + unit;

        foreach (var child in el.Elements().ToList())
        {
            child.AddBeforeSelf(new XText(inner));
            Lay(child, inner, unit);
        }
        el.Add(new XText(at));
    }

    /// <summary>
    /// Take the node out of the tree together with its indentation, and return
    /// both so they can be put back somewhere else.
    /// </summary>
    private static (XText? Indent, XElement Element) Detach(XElement el)
    {
        var indent = IndentOf(el);
        indent?.Remove();
        el.Remove();
        return (indent, el);
    }

    /// <summary>Put a detached element back behind <paramref name="anchor"/>.</summary>
    private static void PlaceAfter(XElement anchor, (XText? Indent, XElement Element) it)
    {
        if (it.Indent is not null) anchor.AddAfterSelf(it.Indent, it.Element);
        else anchor.AddAfterSelf(it.Element);
    }

    /// <summary>
    /// Put a detached element back in front of <paramref name="anchor"/> — in
    /// front of the anchor's own indentation, so that the anchor keeps it and
    /// the newcomer starts the line.
    /// </summary>
    private static void PlaceBefore(XElement anchor, (XText? Indent, XElement Element) it)
    {
        XNode at = IndentOf(anchor) ?? (XNode)anchor;
        if (it.Indent is not null) at.AddBeforeSelf(it.Indent, it.Element);
        else at.AddBeforeSelf(it.Element);
    }

    /// <summary>Move an element behind <paramref name="anchor"/>, indentation and all.</summary>
    public static void MoveAfter(XElement el, XElement anchor) => PlaceAfter(anchor, Detach(el));

    /// <summary>Move an element in front of <paramref name="anchor"/>, indentation and all.</summary>
    public static void MoveBefore(XElement el, XElement anchor) => PlaceBefore(anchor, Detach(el));

    /// <summary>
    /// Insert a new node behind <paramref name="anchor"/>, on a line of its own
    /// with the anchor's indentation. Takes nodes rather than elements because a
    /// comment is inserted the same way (see <see cref="ElementEditor.NodesOf"/>).
    /// </summary>
    public static void InsertAfter(XNode anchor, XNode fresh)
    {
        var indent = IndentOf(anchor);
        if (indent is not null) anchor.AddAfterSelf(new XText(indent.Value), fresh);
        else anchor.AddAfterSelf(fresh);
    }

    /// <summary>
    /// Insert a freshly built element behind <paramref name="anchor"/>, laid
    /// out with the indentation of the file it joins. A subtree built in code
    /// carries no whitespace, so <see cref="InsertAfter(XNode, XNode)"/> alone
    /// would put the whole element on one line.
    /// </summary>
    public static XElement InsertFreshAfter(XNode anchor, XElement fresh)
    {
        Lay(fresh, IndentOf(anchor)?.Value ?? "", IndentUnitOf(anchor.Document));
        InsertAfter(anchor, fresh);
        return fresh;
    }

    /// <summary>
    /// Lay a document built in code out over lines, so a created file reads
    /// like the ones beside it instead of arriving as one enormous line.
    ///
    /// <para>The counterpart of <see cref="Append"/> for the case where there
    /// is nothing to copy from: a fresh document has no whitespace anywhere, so
    /// the root has no indentation of its own to hang the children off. The
    /// step is passed in rather than read off the document for the same reason
    /// — take it from a file the package already has
    /// (<see cref="IndentUnitOf"/>), so a package written with tabs stays on
    /// tabs.</para>
    /// </summary>
    public static void LayDocument(XDocument doc, string unit)
    {
        if (doc.Root is null) return;
        Lay(doc.Root, "\n", unit);
    }

    /// <summary>Remove a node and the indentation that belonged to it.</summary>
    public static void RemoveWithIndent(XNode el)
    {
        IndentOf(el)?.Remove();
        el.Remove();
    }

    /// <summary>
    /// Set the text of a nested element, creating what is missing along the
    /// way. <c>SetSub(def, "12", "Position", "X")</c> writes
    /// <c>&lt;Position&gt;&lt;X&gt;12&lt;/X&gt;&lt;/Position&gt;</c>.
    /// </summary>
    public static void SetSub(XElement node, string value, params string[] path)
    {
        if (path.Length == 0) throw new ArgumentException(T("Path is empty."), nameof(path));

        var n = node;
        for (int i = 0; i < path.Length - 1; i++) n = Ensure(n, path[i]);
        Ensure(n, path[^1]).Value = value;
    }

    /// <summary>
    /// Numeric overload. Formats with the invariant culture on purpose: on a
    /// German system <c>5.5.ToString()</c> yields "5,5", which
    /// <see cref="RenderMath.Num"/> then reads back as 5 — a value silently
    /// altered by the machine's locale.
    /// </summary>
    public static void SetSub(XElement node, double value, params string[] path) =>
        SetSub(node, value.ToString("R", CultureInfo.InvariantCulture), path);

    /// <summary>
    /// Remove a direct child element (the first one matching, as with
    /// <see cref="Xml.Sub"/>). Returns whether anything was there.
    /// </summary>
    public static bool RemoveSub(XElement node, string tag)
    {
        var found = Xml.Sub(node, tag);
        if (found is null) return false;
        found.Remove();
        return true;
    }

    /// <summary>
    /// Set a value, or remove the element when the value is empty. Used for
    /// the fields where "nothing" and "empty" mean the same thing to the game
    /// but an empty element is noise in the file — the template name, the
    /// adapter, the event.
    /// </summary>
    public static void SetOrRemove(XElement node, string? value, string tag)
    {
        if (string.IsNullOrEmpty(value)) RemoveSub(node, tag);
        else SetSub(node, value, tag);
    }

    // -----------------------------------------------------------------
    // Which tag a value belongs in
    // -----------------------------------------------------------------
    //
    // Several fields exist under more than one name. The original picks the
    // write target with a chain of `tx(def,'X')!=='' ? 'X' : …` — but `tx`
    // there is case-insensitive too, so its "AdapterName vs Adaptername"
    // branches can never come apart. What it was reaching for is what these
    // do: write into the tag the file already uses, and only fall back to a
    // default when none of them is there.
    //
    // A deliberate deviation on that default. The original defaults adapters
    // to <Adapter> and events to <ActionCode>, while its *reading* side looks
    // at <Adapter> first and at <ActionCode> only for events. Writing to a tag
    // the reader does not consult first means the edit silently does nothing.
    // The defaults here are therefore the names that actually occur in real
    // packages: <AdapterName> (1653 uses against 644 for <Adapter> in the
    // reference package) and <OnClickEvent> (1041 uses; <ActionCode> and
    // <Command> do not appear at all).

    /// <summary>Tags that may carry an adapter, in the order they are read
    /// (see <see cref="SampleData.AdapterOf"/>).</summary>
    private static readonly string[] AdapterTags =
        { "Adapter", "AdapterName", "TextAdapterName", "LabelAdapterName" };

    /// <summary>Tags that may carry a click event, in the order they are read.</summary>
    private static readonly string[] EventTags =
        { "ActionCode", "Command", "OnClickEvent" };

    /// <summary>Tags that may carry display text, in the order they are read.</summary>
    private static readonly string[] TextTags = { "Data", "Label", "Text" };

    /// <summary>
    /// Which of <paramref name="tags"/> the element already has, else
    /// <paramref name="fallback"/>.
    /// </summary>
    private static string KeyOf(XElement? def, string[] tags, string fallback)
    {
        foreach (string tag in tags)
            if (Xml.Sub(def, tag) is not null) return tag;
        return fallback;
    }

    /// <summary>Where the display text of this element goes.</summary>
    public static string TextKey(XElement? def) => KeyOf(def, TextTags, "Data");

    /// <summary>Where the adapter name of this element goes.</summary>
    public static string AdapterKey(XElement? def) => KeyOf(def, AdapterTags, "AdapterName");

    /// <summary>Where the click event of this element goes.</summary>
    public static string EventKey(XElement? def) => KeyOf(def, EventTags, "OnClickEvent");

    /// <summary>
    /// The click event currently set, read in the same order the original
    /// displays it: <c>ActionCode</c>, <c>Command</c>, <c>OnClickEvent</c>.
    /// </summary>
    public static string EventOf(XElement? def)
    {
        foreach (string tag in EventTags)
        {
            string v = Xml.Tx(def, tag);
            if (v.Length > 0) return v;
        }
        return "";
    }

    /// <summary>
    /// Where the id of a window goes: the tag it was read from.
    ///
    /// <para><b>Deviation from the original.</b> Its inspector writes the id
    /// into &lt;WindowId&gt; unconditionally (line 2571) — while its own
    /// loader takes the id from <see cref="Xml.NameOf"/> and falls back to
    /// &lt;WindowId&gt; only when there is no name (line 1094). For every
    /// window that has a &lt;Name&gt;, and that is nearly all of them, the
    /// rename therefore does not survive a reload: the file still carries the
    /// old name, and &lt;WindowId&gt; is not even unique.</para>
    ///
    /// <para>So the id is written back into the tag it came from.</para>
    /// </summary>
    public static string WindowIdKey(XElement? win)
    {
        if (Xml.Sub(win, "Name") is not null) return "Name";
        if (Xml.Sub(win, "n") is not null) return "n";
        if (Xml.Sub(win, "WindowId") is not null) return "WindowId";
        return "Name";
    }
}
