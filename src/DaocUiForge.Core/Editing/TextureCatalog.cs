using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>One row of the texture list.</summary>
/// <param name="Name">The name the XML refers to it by.</param>
/// <param name="Path">The file it points at, relative to the package root.</param>
/// <param name="Uses">How often a &lt;TextureName&gt; in the package names it.</param>
public sealed record TextureEntry(string Name, string Path, int Uses);

/// <summary>
/// A rectangle a template cuts out of a texture — one of the frames the
/// original draws over the texture preview.
/// </summary>
public sealed record TextureSlice(string Template, double X, double Y, double Width, double Height);

/// <summary>
/// The texture pane of the original (<c>drawTexList</c>, line 2666, and
/// <c>renderTexInspector</c>, line 2703): which textures the package declares,
/// how often each is referred to, and which piece of one a template uses.
///
/// <para>Kept in Core because all three are questions about the package, and
/// because the slice geometry is the same arithmetic the drawing layer does —
/// if the two disagree, the frames sit somewhere the sprite is not.</para>
/// </summary>
public static class TextureCatalog
{
    // -----------------------------------------------------------------
    // The list
    // -----------------------------------------------------------------

    /// <summary>Every declared texture, sorted by name, with its use count.</summary>
    public static List<TextureEntry> All(Package pkg)
    {
        var uses = UseCounts(pkg);

        return pkg.Textures
            .Select(t => new TextureEntry(t.Key, t.Value, uses.GetValueOrDefault(t.Key)))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Filter on the name, as the original's list does.</summary>
    public static IEnumerable<TextureEntry> Filter(IEnumerable<TextureEntry> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// How often each texture name is referred to.
    ///
    /// <para><b>Counted over the whole file, not only over the templates.</b>
    /// The original walks <c>templates</c> and reads every
    /// &lt;TextureName&gt; below each one — which misses the elements that name
    /// a texture directly. In the reference package that is 18 of 878
    /// references, and each of them would have shown "—" for unused.</para>
    /// </summary>
    public static Dictionary<string, int> UseCounts(Package pkg)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in pkg.Docs.Values)
        {
            if (doc.Root is null) continue;

            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                if (!string.Equals(e.Name.LocalName, "TextureName", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = (e.Value ?? "").Trim();
                if (name.Length == 0) continue;

                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return counts;
    }

    // -----------------------------------------------------------------
    // What a template cuts out of it
    // -----------------------------------------------------------------

    /// <summary>
    /// The rectangles the templates take out of this texture, in the texture's
    /// own pixel coordinates.
    ///
    /// <para>The rules are the original's: the texture name may sit on the
    /// template itself or inside its &lt;Texture&gt; block, the start is
    /// &lt;TextureStart&gt; or, failing that, &lt;TopLeft&gt;, and the size
    /// comes from &lt;Width&gt;/&lt;Height&gt; or from &lt;Size&gt;. Without a
    /// start or without a size there is nothing to frame.</para>
    /// </summary>
    public static List<TextureSlice> SlicesOf(Package pkg, string name)
    {
        var marks = new List<TextureSlice>();
        if (string.IsNullOrEmpty(name)) return marks;

        foreach (var byType in pkg.Templates.Values)
        {
            foreach (var (tplName, node) in byType)
            {
                string tex = Xml.Tx(node, "TextureName");
                if (tex.Length == 0) tex = Xml.Tx(Xml.Sub(node, "Texture"), "TextureName");
                if (!string.Equals(tex, name, StringComparison.OrdinalIgnoreCase)) continue;

                var start = RenderMath.Pt(node, "TextureStart") ?? RenderMath.Pt(node, "TopLeft");
                if (start is null) continue;

                var size = RenderMath.Pt(node, "Size");
                double w = RenderMath.Num(Xml.Tx(node, "Width"));
                double h = RenderMath.Num(Xml.Tx(node, "Height"));
                if (w == 0 && size is not null) w = size.Value.X;
                if (h == 0 && size is not null) h = size.Value.Y;
                if (w <= 0 || h <= 0) continue;

                marks.Add(new TextureSlice(tplName, start.Value.X, start.Value.Y, w, h));
            }
        }

        marks.Sort(static (a, b) => string.Compare(a.Template, b.Template, StringComparison.OrdinalIgnoreCase));
        return marks;
    }

    // -----------------------------------------------------------------
    // Binding one
    // -----------------------------------------------------------------

    /// <summary>Whether the package already declares a texture of this name.</summary>
    public static bool NameTaken(Package pkg, string? name) =>
        !string.IsNullOrWhiteSpace(name) && pkg.Textures.ContainsKey(name.Trim());

    /// <summary>
    /// Whether the path really points at a file of the package, resolved the
    /// same way the drawing layer resolves it.
    ///
    /// <para>Not a condition of <see cref="Create"/>: a texture may legitimately
    /// point into the Atlantis game folder, and a binding may be
    /// written before the file is copied in. It is worth <i>saying</i>,
    /// though — the original takes any string at all and the element stays
    /// empty in the game.</para>
    /// </summary>
    public static bool FileInPackage(Package pkg, string? file) =>
        !string.IsNullOrWhiteSpace(file) && FileResolver.Find(pkg.Files, file) is not null;

    /// <summary>
    /// Declare a texture: <c>&lt;Texture&gt;&lt;Name&gt;&lt;File&gt;</c> into
    /// <paramref name="doc"/>. Ported from <c>#mAsset</c> (DAoCEd's
    /// <c>AddAssetAction</c>, line 3225).
    ///
    /// <para><b>The name check is case-insensitive, unlike the original's.</b>
    /// It asks <c>textures[nm]</c>, keyed exactly, and then writes
    /// <c>texturesLc[nm.toLowerCase()]</c> as well — and resolution reads
    /// <c>textures[n] || texturesLc[n.toLowerCase()]</c> (line 918). Declaring
    /// "Emoticons" beside an existing "emoticons" therefore passes the check and
    /// then answers for it wherever the spellings differ. Here
    /// <see cref="Package.Textures"/> is one case-insensitive table, so the
    /// question cannot come apart from the answer.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is empty or taken, the path is empty, or the document has no
    /// root. The message is meant to be shown as it stands.
    /// </exception>
    public static TextureEntry Create(Package pkg, XDocument doc, string name, string file)
    {
        name = (name ?? "").Trim();
        file = (file ?? "").Trim();

        if (name.Length == 0)
            throw new ArgumentException(T("A texture needs a name."));
        if (file.Length == 0)
            throw new ArgumentException(T("A texture needs a file to point at."));
        if (NameTaken(pkg, name))
            throw new ArgumentException(T("A texture called “{0}” is already declared.", name));
        if (doc.Root is null)
            throw new ArgumentException(T("That file has no root element to put it in."));

        var ns = doc.Root.Name.Namespace;
        var node = new XElement(ns + "Texture",
            new XElement(ns + "Name", name),
            new XElement(ns + "File", file));

        XmlEdit.Append(doc.Root, node);
        pkg.Textures[name] = file;

        // After it is in the tree — a detached node has no document to find
        // the file through.
        PackageEditor.MarkFileDirty(pkg, node);

        // Nothing refers to it yet; that is what the list's dash means.
        return new TextureEntry(name, file, 0);
    }

    // -----------------------------------------------------------------
    // Changing and removing one (DAoCEd: Ressourcen -> Ändern / Löschen)
    // -----------------------------------------------------------------

    /// <summary>
    /// The &lt;Texture&gt; block that declares this name, or null. Matched
    /// case-insensitively, the way every lookup of a texture name is.
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
                if (!string.Equals(e.Name.LocalName, "Texture", StringComparison.OrdinalIgnoreCase))
                    continue;
                // The <Texture> BLOCK inside a template is a different thing
                // with the same tag: it carries a <TextureName>, not a <Name>.
                if (string.Equals(Xml.Tx(e, "Name"), wanted, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
        }

        return null;
    }

    /// <summary>
    /// Change a texture's name and the file it points at — DAoCEd's
    /// "Ressourcen · Ändern", which the HTML original has no counterpart for
    /// (it can declare a texture and never touch one again).
    ///
    /// <para><b>A rename carries its references.</b> Every
    /// &lt;TextureName&gt; in the package naming the old one is rewritten, and
    /// the count comes back so it can be said. DAoCEd renames the declaration
    /// alone, and every template that named it then draws nothing — the same
    /// shape of fault as its template rename.</para>
    /// </summary>
    /// <returns>The entry as it now is, and how many references were rewritten.</returns>
    /// <exception cref="ArgumentException">
    /// The name is empty, the new name is taken by another texture, the path is
    /// empty, or no such texture is declared.
    /// </exception>
    public static (TextureEntry Entry, int Renamed) Edit(
        Package pkg, string oldName, string newName, string newFile)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        newFile = (newFile ?? "").Trim();

        if (newName.Length == 0)
            throw new ArgumentException(T("A texture needs a name."));
        if (newFile.Length == 0)
            throw new ArgumentException(T("A texture needs a file to point at."));

        if (NodeOf(pkg, oldName) is not { } node)
            throw new ArgumentException(T("No texture called “{0}” is declared.", oldName));

        bool renaming = !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
        if (renaming && NameTaken(pkg, newName))
            throw new ArgumentException(T("A texture called “{0}” is already declared.", newName));

        int renamed = 0;
        if (renaming)
        {
            foreach (var doc in pkg.Docs.Values)
            {
                if (doc.Root is null) continue;

                foreach (var e in doc.Root.DescendantsAndSelf())
                {
                    if (!string.Equals(e.Name.LocalName, "TextureName", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.Equals((e.Value ?? "").Trim(), oldName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    e.Value = newName;
                    renamed++;
                    PackageEditor.MarkFileDirty(pkg, e);
                }
            }
        }

        XmlEdit.SetSub(node, newName, "Name");
        XmlEdit.SetSub(node, newFile, "File");
        PackageEditor.MarkFileDirty(pkg, node);

        if (renaming) pkg.Textures.Remove(oldName);
        pkg.Textures[newName] = newFile;

        return (new TextureEntry(newName, newFile, UseCounts(pkg).GetValueOrDefault(newName)), renamed);
    }

    /// <summary>
    /// Remove a texture declaration — DAoCEd's "Ressourcen · Löschen".
    ///
    /// <para>The references are left alone on purpose: an element naming an
    /// undeclared texture is a finding the inspection report already makes
    /// ("UNDECLARED TEXTURES"), while rewriting a hundred elements to name
    /// nothing would be a change nobody asked for and one no undo can take
    /// back. The count comes back so the caller can say what is now dangling.</para>
    /// </summary>
    /// <returns>How many references now name a texture that is not declared.</returns>
    /// <exception cref="ArgumentException">No such texture is declared.</exception>
    public static int Delete(Package pkg, string name)
    {
        name = (name ?? "").Trim();

        if (NodeOf(pkg, name) is not { } node)
            throw new ArgumentException(T("No texture called “{0}” is declared.", name));

        int uses = UseCounts(pkg).GetValueOrDefault(name);

        PackageEditor.MarkFileDirty(pkg, node);
        XmlEdit.RemoveWithIndent(node);
        pkg.Textures.Remove(name);

        return uses;
    }
}
