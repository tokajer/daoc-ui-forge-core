using System.Xml.Linq;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Reference;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>One entry of a pick list.</summary>
/// <param name="Label">What the user reads and filters on.</param>
/// <param name="Value">What is written into the XML.</param>
/// <param name="Hint">A dim note beside it — the type, a sample value, a description.</param>
public readonly record struct Choice(string Label, string Value, string Hint = "");

/// <summary>
/// The lists behind the "…" buttons of the property editor. Ported from the
/// <c>itemsFn</c> closures of the original (<c>tplItems</c>, <c>adItems</c>,
/// <c>evItems</c> and the font list, lines 2429–2500).
///
/// <para>They live in Core rather than in the app because they are pure
/// derivations of package and reference data, and because "does the template
/// list really hold every type" is worth a test.</para>
///
/// <para>Why lists rather than drop-downs: a real package brings around a
/// thousand templates. The original answers that with a filterable overlay
/// (<c>pickFrom</c>), and so does the app.</para>
/// </summary>
public static class ChoiceList
{
    /// <summary>The empty entry every list starts with.</summary>
    public static readonly Choice None = new(T("— none —"), "");

    /// <summary>
    /// Every template in the package, sorted by name, with its type as the
    /// hint. <c>none</c> is offered explicitly: the format uses that name for
    /// "deliberately without a template", and it is not a fault.
    /// </summary>
    public static List<Choice> Templates(Package pkg)
    {
        var list = new List<Choice>();

        foreach (var (type, byName) in pkg.Templates)
        {
            // "ButtonTemplate" reads better as "Button" beside the name.
            string hint = type.EndsWith("Template", StringComparison.Ordinal)
                ? type[..^"Template".Length]
                : type;

            foreach (string name in byName.Keys)
                list.Add(new Choice(name, name, hint));
        }

        list.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, None);
        list.Insert(1, new Choice("none", "none", T("invisible on purpose")));
        return list;
    }

    /// <summary>
    /// The template types the package uses, with how many of each — the list
    /// behind "+ Template" (<c>#bNewTpl</c>, line 3190).
    ///
    /// <para><b>Only types the package already has.</b> There are around forty
    /// of them and no list of them anywhere, which is why the loader collects
    /// templates generically in the first place. Offering a fixed
    /// list from memory would offer types this package has no use for and still
    /// miss the ones it does; the package itself is the only authority on which
    /// are in play.</para>
    ///
    /// <para>No <see cref="None"/> entry: a template without a type is not a
    /// thing the format has.</para>
    /// </summary>
    public static List<Choice> TemplateTypes(Package pkg)
    {
        return pkg.Templates
            .Where(t => t.Value.Count > 0)
            .Select(t => new Choice(
                t.Key.EndsWith("Template", StringComparison.Ordinal)
                    ? t.Key[..^"Template".Length]
                    : t.Key,
                t.Key,
                T("{0} · {1}", t.Key, t.Value.Count)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The backgrounds a new window can be given: the package's
    /// <c>FullResizeImageTemplate</c>s, which is what fills a window edge to
    /// edge. DAoCEd's wizard offers the same list with "(No Background)" in
    /// front of it — <see cref="None"/> here.
    /// </summary>
    public static List<Choice> Backgrounds(Package pkg)
    {
        var list = new List<Choice>();

        if (pkg.Templates.TryGetValue("FullResizeImageTemplate", out var byName))
            foreach (string name in byName.Keys)
                list.Add(new Choice(name, name, T("full resize image")));

        list.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, None);
        return list;
    }

    /// <summary>Fonts of the package, with their pixel size as the hint.</summary>
    public static List<Choice> Fonts(Package pkg)
    {
        var list = pkg.Fonts
            .Select(f => new Choice(f.Key, f.Key, T("{0:0} px line", f.Value.Height)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        list.Insert(0, None);
        return list;
    }

    /// <summary>
    /// Textures the package declares, with the file behind them as the hint —
    /// for the elements that name one directly (<c>DynamicImageDef</c>) rather
    /// than through a template.
    /// </summary>
    public static List<Choice> Textures(Package pkg)
    {
        var list = pkg.Textures
            .Select(t => new Choice(t.Key, t.Key, t.Value))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        list.Insert(0, None);
        return list;
    }

    /// <summary>
    /// The window's elements by their control id, for the tab assignments
    /// (<see cref="TabEditor"/>). Elements without an id are left out: a
    /// &lt;TabControl&gt; can only name one.
    ///
    /// <para>The same id may sit on more than one element. That is the
    /// package's business, not the picker's, so the duplicates are listed
    /// rather than folded together.</para>
    /// </summary>
    public static List<Choice> Controls(XElement window)
    {
        var list = new List<Choice>();

        foreach (var def in ElementEditor.ElementsOf(window))
        {
            string id = Xml.Tx(def, "ControlId");
            if (id.Length == 0) continue;

            string text = Xml.Tx(def, XmlEdit.TextKey(def));
            string type = ControlSchema.NameOf(def.Name.LocalName);

            list.Add(new Choice(id, id, text.Length > 0 ? T("{0} - {1}", type, Shorten(text, 24)) : type));
        }

        list.Sort(static (a, b) => Compare(a.Value, b.Value));
        list.Insert(0, None);
        return list;
    }

    /// <summary>Control ids are numbers in every package seen; sort them as such,
    /// and fall back to text where they are not.</summary>
    private static int Compare(string a, string b) =>
        int.TryParse(a, out int x) && int.TryParse(b, out int y)
            ? x.CompareTo(y)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adapters from the DAoCEd reference data. Text adapters show their
    /// sample text, scalar ones their value — and their maximum where there is
    /// one, because "20/100" is what says the bar will be a fifth full.
    /// </summary>
    public static List<Choice> Adapters(ReferenceData? data = null)
    {
        data ??= ReferenceData.Default;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Choice>();

        void Add(string key, string hint)
        {
            if (!seen.Add(key)) return;
            list.Add(new Choice(key, key, hint));
        }

        foreach (var (key, text) in data.Texts)
            Add(key, Shorten(text, 26));

        foreach (var (key, value) in data.Current)
            Add(key, data.Max.TryGetValue(key, out var max) ? $"{value}/{max}" : value);

        list.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, None);
        return list;
    }

    /// <summary>Events from the DAoCEd reference data, with their description.</summary>
    public static List<Choice> Events(ReferenceData? data = null)
    {
        data ??= ReferenceData.Default;

        var list = data.Events
            .Select(e => new Choice(e.Key, e.Key, Shorten(Plain(e.Value), 40)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        list.Insert(0, None);
        return list;
    }

    /// <summary>
    /// Event descriptions come from DAoCEd as HTML with &lt;br&gt; in them.
    /// The app shows plain text, so the breaks become spaces.
    /// </summary>
    public static string Plain(string html) =>
        html.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", " ", StringComparison.OrdinalIgnoreCase)
            .Replace('\n', ' ')
            .Trim();

    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// Filter a list the way the original's overlay does: case-insensitive on
    /// the label, everything when the query is empty.
    /// </summary>
    public static IEnumerable<Choice> Filter(IEnumerable<Choice> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(c =>
            c.Label.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
