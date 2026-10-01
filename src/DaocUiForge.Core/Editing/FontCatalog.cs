using System.Xml.Linq;
using DaocUiForge.Core.Formats;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>One row of the font list.</summary>
/// <param name="Name">The name the XML refers to it by.</param>
/// <param name="File">The TTF it points at, relative to the package root.</param>
/// <param name="Height">&lt;Height&gt; — the font size in pixels.</param>
/// <param name="LineHeightEm">
/// Line height in em out of the TTF metrics, or 0 when the file could not be
/// read. The line advance follows from it.
/// </param>
/// <param name="Uses">How often a &lt;FontName&gt; or &lt;Font&gt; names it.</param>
public sealed record FontEntry(
    string Name, string File, double Height, double LineHeightEm, int Uses)
{
    /// <summary>
    /// The size the drawing layer will use — the same answer
    /// <see cref="FontProvider.SizePx"/> gives, so the number shown and the
    /// number drawn cannot disagree.
    /// </summary>
    public double SizePx => Height >= 6 ? Height : 11;

    /// <summary>
    /// The line advance that follows: <c>SizePx × LineHeightEm</c>. Without
    /// metrics the fallback of <see cref="FontProvider.DefaultLineHeightEm"/>
    /// applies, again as <see cref="FontProvider.LinePx"/> does.
    /// </summary>
    public double LinePx
    {
        get
        {
            double lh = LineHeightEm > 0 ? LineHeightEm : FontProvider.DefaultLineHeightEm;
            return RenderMath.JsRound(SizePx * lh * 10) / 10;
        }
    }

    /// <summary>Whether the TTF is really reachable — package or game folder.</summary>
    public bool Resolved => LineHeightEm > 0;
}

/// <summary>
/// The package's &lt;TTFFont&gt; declarations: list them, create one, change one,
/// remove one. DAoCEd's <c>CreateFontAction</c> from the useful side.
///
/// <para><b>What DAoCEd's own tool does, and why this is not that.</b> Its
/// <c>FontGeneratorDialog</c> picks a system font, a style and a size, and its
/// <c>FontGenerator</c> then renders the characters into a bitmap and writes the
/// result to a file called <c>test.jpg</c> in the working directory — a hard-coded
/// name, a format the client does not read, and no connection to the package at
/// all. It is an unfinished experiment. What the format actually wants is a
/// declaration: a name, a TTF file, and a font size. That is what this
/// does, and <see cref="ResourceImport"/> is what puts the TTF itself into the
/// package.</para>
///
/// <para><b>&lt;Height&gt; is the font size, and the line advance follows from
/// the TTF</b>. It is the single most misread field in the format —
/// this port had it the other way round until it was measured against the
/// running game — so the entry carries both numbers and the editor shows both.</para>
/// </summary>
public static class FontCatalog
{
    /// <summary>The tags an element names a font under, as the renderer reads them.</summary>
    private static readonly string[] NameTags = { "FontName", "Font" };

    // -----------------------------------------------------------------
    // The list
    // -----------------------------------------------------------------

    /// <summary>Every declared font, sorted by name, with its use count.</summary>
    public static List<FontEntry> All(Package pkg)
    {
        var uses = UseCounts(pkg);

        return pkg.Fonts
            .Select(f => new FontEntry(
                f.Key, f.Value.File, f.Value.Height, f.Value.LineHeight,
                uses.GetValueOrDefault(f.Key)))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IEnumerable<FontEntry> Filter(IEnumerable<FontEntry> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || f.File.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// How often each font name is named.
    ///
    /// <para>Both spellings are counted, because both are read: an element
    /// carries &lt;FontName&gt; and a template a nested &lt;Font&gt; block with a
    /// &lt;Name&gt; in it (<c>ElementRenderer</c> asks for both). A
    /// &lt;Font&gt; that holds a block rather than a name is the template form,
    /// and its &lt;Name&gt; is what counts.</para>
    /// </summary>
    public static Dictionary<string, int> UseCounts(Package pkg)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Count(string? name)
        {
            string n = (name ?? "").Trim();
            if (n.Length > 0) counts[n] = counts.GetValueOrDefault(n) + 1;
        }

        foreach (var doc in pkg.Docs.Values)
        {
            if (doc.Root is null) continue;

            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                string tag = e.Name.LocalName;
                if (!NameTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;

                // <TTFFont> declares; <Font>/<FontName> refer. A block with
                // children carries its name inside.
                Count(e.HasElements ? Xml.Tx(e, "Name") : (e.Value ?? "").Trim());
            }
        }

        return counts;
    }

    // -----------------------------------------------------------------
    // Creating and changing
    // -----------------------------------------------------------------

    /// <summary>Whether the package already declares a font of this name.</summary>
    public static bool NameTaken(Package pkg, string? name) =>
        !string.IsNullOrWhiteSpace(name) && pkg.Fonts.ContainsKey(name.Trim());

    /// <summary>
    /// The &lt;TTFFont&gt; block that declares this name, or null. Matched
    /// case-insensitively, the way every font lookup is.
    /// </summary>
    public static XElement? NodeOf(Package pkg, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string wanted = name.Trim();

        foreach (var doc in pkg.Docs.Values)
        {
            if (doc.Root is null) continue;

            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                if (!string.Equals(e.Name.LocalName, "TTFFont", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(Xml.NameOf(e), wanted, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
        }

        return null;
    }

    /// <summary>
    /// Declare a font: <c>&lt;TTFFont&gt;&lt;Name&gt;&lt;File&gt;&lt;Height&gt;</c>
    /// into <paramref name="doc"/>.
    ///
    /// <para>Whether the TTF is really there is checked and <i>said</i>, not
    /// enforced — the same call as <see cref="TextureCatalog.Create"/>: the file
    /// may live in the game folder, or be imported a moment later.</para>
    /// </summary>
    /// <param name="height">The font size in pixels.</param>
    /// <exception cref="ArgumentException">
    /// The name is empty or taken, the path is empty, the height is not a
    /// sensible one, or the document has no root.
    /// </exception>
    public static FontEntry Create(Package pkg, XDocument doc, string name, string file, double height)
    {
        name = (name ?? "").Trim();
        file = (file ?? "").Trim();

        if (name.Length == 0)
            throw new ArgumentException(T("A font needs a name."));
        if (file.Length == 0)
            throw new ArgumentException(T("A font needs a TTF file to point at."));
        if (NameTaken(pkg, name))
            throw new ArgumentException(T("A font called “{0}” is already declared.", name));
        if (height < 6 || height > 200)
            throw new ArgumentException(T("The font size has to be between 6 and 200 pixels."));
        if (doc.Root is null)
            throw new ArgumentException(T("That file has no root element to put it in."));

        var ns = doc.Root.Name.Namespace;
        var node = new XElement(ns + "TTFFont",
            new XElement(ns + "Name", name),
            new XElement(ns + "File", file));
        XmlEdit.SetSub(node, height, "Height");

        XmlEdit.Append(doc.Root, node);
        Register(pkg, name, file, height);
        PackageEditor.MarkFileDirty(pkg, node);

        return Entry(pkg, name, 0);
    }

    /// <summary>
    /// Change a font's name, its file or its font size.
    ///
    /// <para><b>A rename carries its references</b>, exactly as
    /// <see cref="TextureCatalog.Edit"/> does: every &lt;FontName&gt; and every
    /// template's &lt;Font&gt;&lt;Name&gt; naming the old one follows. Renaming
    /// the declaration alone would leave every label that named it falling back
    /// to a guessed system font, which is a difference nobody would trace back
    /// to a rename.</para>
    /// </summary>
    /// <returns>The entry as it now is, and how many references were rewritten.</returns>
    public static (FontEntry Entry, int Renamed) Edit(
        Package pkg, string oldName, string newName, string newFile, double newHeight)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        newFile = (newFile ?? "").Trim();

        if (newName.Length == 0)
            throw new ArgumentException(T("A font needs a name."));
        if (newFile.Length == 0)
            throw new ArgumentException(T("A font needs a TTF file to point at."));
        if (newHeight < 6 || newHeight > 200)
            throw new ArgumentException(T("The font size has to be between 6 and 200 pixels."));

        if (NodeOf(pkg, oldName) is not { } node)
            throw new ArgumentException(T("No font called “{0}” is declared.", oldName));

        bool renaming = !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
        if (renaming && NameTaken(pkg, newName))
            throw new ArgumentException(T("A font called “{0}” is already declared.", newName));

        int renamed = renaming ? Rename(pkg, oldName, newName) : 0;

        XmlEdit.SetSub(node, newName, "Name");
        XmlEdit.SetSub(node, newFile, "File");
        XmlEdit.SetSub(node, newHeight, "Height");
        PackageEditor.MarkFileDirty(pkg, node);

        if (renaming) pkg.Fonts.Remove(oldName);
        Register(pkg, newName, newFile, newHeight);

        return (Entry(pkg, newName, UseCounts(pkg).GetValueOrDefault(newName)), renamed);
    }

    /// <summary>
    /// Remove a font declaration. The references stay, as with a texture: an
    /// element naming an undeclared font falls back to a system face rather than
    /// vanishing, and rewriting a hundred labels is a change nobody asked for.
    /// </summary>
    /// <returns>How many references now name a font that is not declared.</returns>
    public static int Delete(Package pkg, string name)
    {
        name = (name ?? "").Trim();

        if (NodeOf(pkg, name) is not { } node)
            throw new ArgumentException(T("No font called “{0}” is declared.", name));

        int uses = UseCounts(pkg).GetValueOrDefault(name);

        PackageEditor.MarkFileDirty(pkg, node);
        XmlEdit.RemoveWithIndent(node);
        pkg.Fonts.Remove(name);

        return uses;
    }

    /// <summary>Whether the TTF is reachable — in the package or in the game folder.</summary>
    public static bool FileReachable(Package pkg, string? file) =>
        !string.IsNullOrWhiteSpace(file)
        && (FileResolver.Find(pkg.Files, file) is not null || GameFolder.Find(pkg, file) is not null);

    // -----------------------------------------------------------------

    /// <summary>
    /// Put the font into the model and read its metrics back. Without the
    /// metrics the size would be computed from the 1.3 fallback and every label
    /// in that font would come out the wrong size.
    /// </summary>
    private static void Register(Package pkg, string name, string file, double height)
    {
        pkg.Fonts[name] = new FontRef
        {
            File = file,
            Height = height,
            Bold = name.Contains("bold", StringComparison.OrdinalIgnoreCase)
                   || file.Contains("bold", StringComparison.OrdinalIgnoreCase),
        };

        byte[]? bytes = FileResolver.Find(pkg.Files, file) is string key
            ? pkg.Files[key]
            : GameFolder.Find(pkg, file) is string gameKey
                ? GameFolder.Read(pkg, gameKey)
                : null;

        if (bytes is null) return;
        double lh = TtfMetrics.LineHeightEm(bytes);
        if (lh > 0) pkg.Fonts[name].LineHeight = lh;
    }

    private static int Rename(Package pkg, string oldName, string newName)
    {
        int renamed = 0;

        foreach (var doc in pkg.Docs.Values)
        {
            if (doc.Root is null) continue;

            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                string tag = e.Name.LocalName;
                if (!NameTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;

                // The block form (<Font><Name>…) has its name one level down;
                // the leaf form carries it as text.
                var target = e.HasElements ? Xml.Sub(e, "Name") : e;
                if (target is null) continue;
                if (!string.Equals((target.Value ?? "").Trim(), oldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                target.Value = newName;
                renamed++;
                PackageEditor.MarkFileDirty(pkg, target);
            }
        }

        return renamed;
    }

    private static FontEntry Entry(Package pkg, string name, int uses)
    {
        var f = pkg.Fonts[name];
        return new FontEntry(name, f.File, f.Height, f.LineHeight, uses);
    }
}
