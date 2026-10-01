using System.Xml.Linq;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>One row of the template list: the type it was declared under, its
/// name, and the node itself.</summary>
public sealed record TemplateEntry(string Type, string Name, XElement Node)
{
    /// <summary>"ButtonTemplate" reads better as "Button" over a list of names —
    /// the original strips the suffix in the same place.</summary>
    public string ShortType => Type.EndsWith("Template", StringComparison.Ordinal)
        ? Type[..^"Template".Length]
        : Type;
}

/// <summary>How a field of a template is best shown.</summary>
public enum TemplateFieldKind
{
    /// <summary>A leaf: one value in one box.</summary>
    Value,

    /// <summary>&lt;R&gt;&lt;G&gt;&lt;B&gt;[&lt;A&gt;] — a swatch, not four numbers.</summary>
    Color,

    /// <summary>One or two coordinate values (&lt;X&gt;&lt;Y&gt;) that fit on one line.</summary>
    Pair,

    /// <summary>Anything deeper. Its contents come from <see cref="TemplateCatalog.Flatten"/>.</summary>
    Block,
}

/// <summary>
/// One editable field of a template.
/// </summary>
/// <param name="Label">What to write in front of it, dotted for nested fields.</param>
/// <param name="Node">
/// The element the value lives in. For <see cref="TemplateFieldKind.Value"/>
/// that is the element whose text is edited; for the other kinds it is the
/// element holding <paramref name="Parts"/>.
/// </param>
/// <param name="Kind">Which shape the field has.</param>
/// <param name="Parts">The channels of a colour, or the members of a pair.</param>
public sealed record TemplateField(
    string Label,
    XElement Node,
    TemplateFieldKind Kind,
    IReadOnlyList<XElement> Parts);

/// <summary>
/// The template pane of the original: the list of every template in the package
/// (<c>drawTplList</c>, line 2787) and the rules behind its editor
/// (<c>renderTplInspector</c>, line 2817).
///
/// <para>The classification of a field lives here rather than in the app,
/// because "three children called R, G and B are a colour, two called X and Y
/// are a point" is a rule about the format — and one worth a test. The app only
/// turns the answer into controls.</para>
/// </summary>
public static class TemplateCatalog
{
    private static readonly IReadOnlyList<XElement> NoParts = Array.Empty<XElement>();

    // -----------------------------------------------------------------
    // The list
    // -----------------------------------------------------------------

    /// <summary>
    /// Every template of the package, sorted by type and then by name — the
    /// order the original's list uses, and the reason it can group by type
    /// while walking it once.
    /// </summary>
    public static List<TemplateEntry> All(Package pkg)
    {
        var list = new List<TemplateEntry>();

        foreach (var (type, byName) in pkg.Templates)
            foreach (var (name, node) in byName)
                list.Add(new TemplateEntry(type, name, node));

        list.Sort(static (a, b) =>
        {
            int byType = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);
            return byType != 0 ? byType : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>
    /// Filter as the original does: on the name <i>or</i> the type, so "button"
    /// finds every button template as well as the ones named after one.
    /// </summary>
    public static IEnumerable<TemplateEntry> Filter(IEnumerable<TemplateEntry> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(t =>
            t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || t.Type.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tags that carry a template name, in the order the drawing layer reads
    /// them.
    ///
    /// <para><b>Wider than the original.</b> Its "used in" list asks for
    /// <c>TemplateName</c>, <c>Templatename</c>, <c>HRButtonTemplateName</c> and
    /// <c>ImageAreaTemplateName</c> — and the first two are the same question,
    /// because its <c>tx</c> is case-insensitive. Meanwhile its own renderer
    /// also honours <c>BackgroundTemplateName</c>, <c>ForegroundTemplateName</c>
    /// and <c>IconTemplateName</c>, so a template used through one of those
    /// reports "not used at all" while it is plainly on screen.</para>
    /// </summary>
    private static readonly string[] TemplateTags =
    {
        "TemplateName", "HRButtonTemplateName", "ImageAreaTemplateName",
        "BackgroundTemplateName", "ForegroundTemplateName", "IconTemplateName",
    };

    /// <summary>
    /// The windows whose elements refer to this template, each named once.
    /// </summary>
    public static List<WindowDef> UsedBy(Package pkg, string name)
    {
        var hits = new List<WindowDef>();
        if (string.IsNullOrEmpty(name)) return hits;

        foreach (var win in pkg.Windows)
        {
            foreach (var def in ElementEditor.ElementsOf(win.Node))
            {
                if (!TemplateTags.Any(tag =>
                        string.Equals(Xml.Tx(def, tag), name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                hits.Add(win);
                break;      // the window is named once, however many elements use it
            }
        }
        return hits;
    }

    // -----------------------------------------------------------------
    // The fields of one template
    // -----------------------------------------------------------------

    /// <summary>
    /// The direct fields of a template, classified. The name is left out: it is
    /// the template's identity, not one of its values, and the original skips
    /// it in the same place.
    /// </summary>
    public static IReadOnlyList<TemplateField> Fields(XElement template)
    {
        var list = new List<TemplateField>();

        foreach (var child in template.Elements())
        {
            string tag = child.Name.LocalName;
            if (string.Equals(tag, "Name", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(tag, "n", StringComparison.OrdinalIgnoreCase)) continue;

            list.Add(Classify(tag, child));
        }

        return list;
    }

    /// <summary>
    /// The contents of a <see cref="TemplateFieldKind.Block"/>, flattened into
    /// rows with dotted labels — <c>Texture.Start.X</c>. Ported from
    /// <c>addFields</c> of the original.
    ///
    /// <para>Only <see cref="TemplateFieldKind.Value"/> and
    /// <see cref="TemplateFieldKind.Color"/> come out of here: everything deeper
    /// is walked into rather than folded up, so no field of a template can hide
    /// from the editor.</para>
    /// </summary>
    public static IReadOnlyList<TemplateField> Flatten(XElement block)
    {
        var list = new List<TemplateField>();
        Walk(block, "", list);
        return list;
    }

    private static void Walk(XElement node, string prefix, List<TemplateField> into)
    {
        foreach (var child in node.Elements())
        {
            var kids = child.Elements().ToList();
            string label = prefix + child.Name.LocalName;

            // A block with a block of its own inside: go one level deeper
            // rather than trying to show a tree in a two-column grid.
            if (kids.Count > 0 && kids.Any(k => k.HasElements))
            {
                Walk(child, label + ".", into);
                continue;
            }

            if (kids.Count == 0)
            {
                into.Add(new TemplateField(label, child, TemplateFieldKind.Value, NoParts));
                continue;
            }

            if (IsColor(kids))
            {
                into.Add(new TemplateField(label, child, TemplateFieldKind.Color, kids));
                continue;
            }

            foreach (var leaf in kids)
                into.Add(new TemplateField(
                    label + "." + leaf.Name.LocalName, leaf, TemplateFieldKind.Value, NoParts));
        }
    }

    private static TemplateField Classify(string label, XElement node)
    {
        var kids = node.Elements().ToList();

        if (kids.Count == 0)
            return new TemplateField(label, node, TemplateFieldKind.Value, NoParts);

        if (IsColor(kids))
            return new TemplateField(label, node, TemplateFieldKind.Color, kids);

        if (kids.Count <= 2 && kids.All(k => !k.HasElements && IsChannel(k, "XYRGBA")))
            return new TemplateField(label, node, TemplateFieldKind.Pair, kids);

        return new TemplateField(label, node, TemplateFieldKind.Block, NoParts);
    }

    /// <summary>Three or four leaves called R, G, B and A — a colour.</summary>
    private static bool IsColor(List<XElement> kids) =>
        kids.Count is >= 3 and <= 4 && kids.All(k => !k.HasElements && IsChannel(k, "RGBA"));

    /// <summary>
    /// A single-letter tag out of the set given.
    ///
    /// <para>Case-insensitive, unlike the original's <c>/^[RGBA]$/</c>.
    /// Everything else on the reading side of this port is
    /// (<see cref="Xml.Sub"/>, <see cref="Render.RenderMath.ColorOf"/>), so a
    /// file spelling its channels in lower case would be read as a colour and
    /// then shown as a nested block. The looser test can only classify more
    /// fields correctly, never fewer.</para>
    /// </summary>
    private static bool IsChannel(XElement e, string allowed)
    {
        string n = e.Name.LocalName;
        return n.Length == 1 && allowed.Contains(char.ToUpperInvariant(n[0]));
    }

    // -----------------------------------------------------------------
    // Editing
    // -----------------------------------------------------------------

    /// <summary>
    /// Whether the package already holds a template of this name.
    ///
    /// <para>Case-insensitive, because the lookup that matters is
    /// (<see cref="Package.ByNameLc"/>) — see <see cref="Create"/>.</para>
    /// </summary>
    public static bool NameTaken(Package pkg, string? name) =>
        !string.IsNullOrWhiteSpace(name) && pkg.ByNameLc.ContainsKey(name.Trim());

    /// <summary>
    /// Create a template and put it into <paramref name="doc"/>. Ported from
    /// <c>#bNewTpl</c> (DAoCEd's <c>AddTemplateAction</c>, line 3188).
    ///
    /// <para>The new template gets a name and a 16 × 16 &lt;Size&gt;, as in the
    /// original: with no size at all most types draw nothing, and an empty
    /// template that cannot be seen is hard to go on editing.</para>
    ///
    /// <para><b>The name check is case-insensitive, unlike the original's.</b>
    /// It asks <c>byName[nm]</c>, which is keyed exactly, and then writes
    /// <c>byNameLc[nm.toLowerCase()]</c> — the table every lookup falls back to
    /// (<c>findTpl</c>, line 1115). Creating "Button_Bag" beside an existing
    /// "button_bag" therefore passes the check and takes the old template's
    /// place: every element referring to it silently draws the new empty one
    /// instead, and nothing says so.</para>
    ///
    /// <para><b>The type is checked as well.</b> The loader collects templates
    /// generically — everything ending in "Template", except
    /// &lt;WindowTemplate&gt;. A type outside that rule is written
    /// into the file happily and is simply not there after the next load, and
    /// &lt;WindowTemplate&gt; would come back as a phantom window. The original
    /// cannot hit either, because it only offers the types the package already
    /// has — this is reachable, so it is refused.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is empty or taken, the type is not a template type, or the
    /// document has no root. The message is meant to be shown as it stands.
    /// </exception>
    public static TemplateEntry Create(Package pkg, XDocument doc, string type, string name)
    {
        type = (type ?? "").Trim();
        name = (name ?? "").Trim();

        if (name.Length == 0)
            throw new ArgumentException(T("A template needs a name."));
        if (!type.EndsWith("Template", StringComparison.Ordinal) || type == "WindowTemplate")
            throw new ArgumentException(T("“{0}” is not a template type.", type));
        if (NameTaken(pkg, name))
            throw new ArgumentException(T("A template called “{0}” is already in the package.", name));
        if (doc.Root is null)
            throw new ArgumentException(T("That file has no root element to put it in."));

        var ns = doc.Root.Name.Namespace;
        var node = new XElement(ns + type,
            new XElement(ns + "Name", name),
            new XElement(ns + "Size",
                new XElement(ns + "X", "16"),
                new XElement(ns + "Y", "16")));

        XmlEdit.Append(doc.Root, node);

        var entry = new TemplateEntry(type, name, node);
        Register(pkg, entry);

        // After it is in the tree: MarkFileDirty finds the file through the
        // node's document, and a detached node has none.
        PackageEditor.MarkFileDirty(pkg, node);

        return entry;
    }

    /// <summary>
    /// Remove a template from its file and from the package's lookup tables.
    ///
    /// <para><b>More careful than the original with duplicate names.</b> It does
    /// <c>delete byName[t.nm]</c> unconditionally — but with duplicate names the
    /// last one loaded wins, so deleting the <i>other</i> one throws
    /// away the entry of a template that is still there, and every element
    /// referring to the name suddenly draws nothing. The lookup is handed to a
    /// surviving namesake instead.</para>
    /// </summary>
    public static void Delete(Package pkg, TemplateEntry t)
    {
        // Before the node leaves the tree: afterwards its Document is null and
        // there is no way back to the file it came from.
        PackageEditor.MarkFileDirty(pkg, t.Node);

        XmlEdit.RemoveWithIndent(t.Node);
        Forget(pkg, t);
    }

    /// <summary>
    /// Replace a template with XML typed by hand. Returns the entry as it now
    /// stands. Throws <see cref="System.Xml.XmlException"/> when the text does
    /// not parse — nothing is touched then.
    ///
    /// <para><b>The name and the type are re-read from the new XML.</b> The
    /// original writes the node back under the old key
    /// (<c>templates[t.type][t.nm]=nn</c>), so renaming a template in that box
    /// leaves the package pointing at the new node under the old name, and
    /// nothing finds it under the new one.</para>
    /// </summary>
    public static TemplateEntry ReplaceFromXml(Package pkg, TemplateEntry t, string xml)
    {
        // Parsed before anything is replaced, so a typo cannot cost the template.
        var parsed = XElement.Parse(xml, LoadOptions.PreserveWhitespace);

        PackageEditor.MarkFileDirty(pkg, t.Node);
        t.Node.ReplaceWith(parsed);
        Forget(pkg, t);

        var fresh = new TemplateEntry(
            parsed.Name.LocalName,
            Xml.NameOf(parsed, t.Name),
            parsed);

        Register(pkg, fresh);
        return fresh;
    }

    /// <summary>Take a template out of the lookup tables.</summary>
    private static void Forget(Package pkg, TemplateEntry t)
    {
        if (pkg.Templates.TryGetValue(t.Type, out var byType)
            && byType.TryGetValue(t.Name, out var mapped)
            && ReferenceEquals(mapped, t.Node))
        {
            byType.Remove(t.Name);
        }

        if (!pkg.ByNameLc.TryGetValue(t.Name, out var lc) || !ReferenceEquals(lc, t.Node)) return;

        var survivor = AnyNamed(pkg, t.Name);
        if (survivor is null) pkg.ByNameLc.Remove(t.Name);
        else pkg.ByNameLc[t.Name] = survivor;
    }

    /// <summary>Put a template into the lookup tables (last one wins, as on load).</summary>
    private static void Register(Package pkg, TemplateEntry t)
    {
        if (t.Name.Length == 0) return;

        if (!pkg.Templates.TryGetValue(t.Type, out var byType))
        {
            byType = new Dictionary<string, XElement>(StringComparer.Ordinal);
            pkg.Templates[t.Type] = byType;
        }
        byType[t.Name] = t.Node;
        pkg.ByNameLc[t.Name] = t.Node;
    }

    /// <summary>Any remaining template of that name, whatever its type.</summary>
    private static XElement? AnyNamed(Package pkg, string name)
    {
        foreach (var byType in pkg.Templates.Values)
            foreach (var (other, node) in byType)
                if (string.Equals(other, name, StringComparison.OrdinalIgnoreCase))
                    return node;
        return null;
    }
}
