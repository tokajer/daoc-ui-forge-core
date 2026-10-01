using System.Xml.Linq;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// Edits at package level: renaming and removing whole windows. Ported from
/// <c>buildWindowGroup</c> of the HTML original (line 2560).
///
/// <para>Kept apart from <see cref="ElementEditor"/> because these touch the
/// model as well as the XML — the window list and the dirty marks have to stay
/// in step with the tree, and getting that wrong is only noticed on the next
/// load.</para>
/// </summary>
public static class PackageEditor
{
    /// <summary>
    /// Rename a window: the tag the id was read from is written (see
    /// <see cref="XmlEdit.WindowIdKey"/>), and the model follows.
    ///
    /// <para>An empty name is refused rather than applied. The loader drops
    /// windows without an id, so an empty one would make the window
    /// vanish on the next load — silently, and only then.</para>
    /// </summary>
    /// <returns>Whether the name was taken.</returns>
    public static bool Rename(WindowDef win, string? id)
    {
        id = id?.Trim();
        if (string.IsNullOrEmpty(id)) return false;

        XmlEdit.SetSub(win.Node, id, XmlEdit.WindowIdKey(win.Node));
        win.Id = id;
        win.Name = id;
        win.Dirty = true;
        return true;
    }

    /// <summary>
    /// The file a node came from, or null when it belongs to no document the
    /// package knows — which happens to a node that has already been removed
    /// (<see cref="System.Xml.Linq.XObject.Document"/> is null then).
    /// </summary>
    public static string? FileOf(Package pkg, XObject node)
    {
        var doc = node.Document;
        if (doc is null) return null;

        foreach (var (key, candidate) in pkg.Docs)
            if (ReferenceEquals(candidate, doc))
                return key;

        return null;
    }

    /// <summary>
    /// Note that the file this node lives in has to be written out.
    ///
    /// <para>For a change that belongs to no window — a template edit, say.
    /// <see cref="PackageWriter.ChangedFiles"/> reads
    /// <see cref="Package.DirtyFiles"/> alongside the dirty windows, so this is
    /// all it takes. The windows of that file are deliberately <i>not</i>
    /// marked: nothing in them changed, and a dot beside them would claim
    /// otherwise. The original has to mark one, because its save list knows
    /// only windows (line 2826).</para>
    /// </summary>
    /// <returns>Whether a file was found to mark.</returns>
    public static bool MarkFileDirty(Package pkg, XObject node)
    {
        if (FileOf(pkg, node) is not string file) return false;
        pkg.DirtyFiles.Add(file);
        return true;
    }

    /// <summary>
    /// Remove a window from the package and from its file.
    ///
    /// <para>The other windows of the same file are marked dirty as well: they
    /// share one <see cref="WindowDef.Doc"/>, so that file has to be written
    /// out again even though nothing in them changed. The original does the
    /// same (line 2589) — miss it and the deletion is simply not saved.</para>
    ///
    /// <para>The file itself is noted too, which the original does not do. Its
    /// list of what to save is <c>windows.filter(w=&gt;w.dirty)</c>, and after
    /// deleting the <i>only</i> window of a file there is no dirty window left
    /// to name it — the deletion then never reaches the disk. See
    /// <see cref="PackageWriter.ChangedFiles"/>.</para>
    /// </summary>
    public static void DeleteWindow(Package pkg, WindowDef win)
    {
        XmlEdit.RemoveWithIndent(win.Node);
        pkg.Windows.Remove(win);
        pkg.DirtyFiles.Add(win.File);

        foreach (var other in pkg.Windows)
            if (string.Equals(other.File, win.File, StringComparison.OrdinalIgnoreCase))
                other.Dirty = true;
    }

    // -----------------------------------------------------------------
    // Creating a window (DAoCEd's AddWindowWizard)
    // -----------------------------------------------------------------
    //
    // The HTML original cannot do this at all — a browser has no file to
    // create. DAoCEd builds a file of its own per window, announces it in an
    // include list, and offers only the names the game knows about.

    /// <summary>
    /// How many custom windows the DAoC client accepts: <c>custom0_window</c>
    /// through <c>custom19_window</c>. DAoCEd's
    /// <c>SelectName.MAX_CUSTOM_WINDOWS</c>, and the number its wizard puts on
    /// screen ("The DAoC client allows a maximum of 20 custom windows").
    /// </summary>
    public const int MaxCustomWindows = 20;

    /// <summary>The name of custom window <paramref name="n"/>.</summary>
    public static string CustomWindowName(int n) => $"custom{n}_window";

    /// <summary>
    /// The custom window names this package has not used yet — what the wizard
    /// offers.
    ///
    /// <para>A name counts as taken when a window carries it <b>or</b> when the
    /// file exists. Those are two different things: a file may hold no window
    /// the loader accepted (no <c>&lt;Name&gt;</c>, or it does not parse), and
    /// creating a second window under that name would write over it. DAoCEd
    /// asks only the include list, which is the same question from the other
    /// side.</para>
    /// </summary>
    public static List<string> FreeCustomNames(Package pkg)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in pkg.Windows) used.Add(w.Id);

        var free = new List<string>();
        for (int i = 0; i < MaxCustomWindows; i++)
        {
            string name = CustomWindowName(i);
            if (used.Contains(name)) continue;
            if (pkg.Files.ContainsKey(FileKeyFor(pkg, name))) continue;
            free.Add(name);
        }
        return free;
    }

    /// <summary>Is this name already a window of the package, or a file of it?</summary>
    public static bool WindowNameTaken(Package pkg, string name) =>
        pkg.Windows.Any(w => string.Equals(w.Id, name, StringComparison.OrdinalIgnoreCase))
        || pkg.Files.ContainsKey(FileKeyFor(pkg, name));

    /// <summary>
    /// Create a window in a file of its own, announce it in the package's
    /// include list, and put it into the model as the loader would.
    ///
    /// <para>The window's fields are DAoCEd's
    /// (<c>WindowtemplateNode(name, w, h)</c>): the buttons off, the offsets
    /// and the resize limits at 0, the title zone as wide as the window and 16
    /// high. A background is optional and arrives as a
    /// <c>&lt;FullResizeImageDef&gt;</c> filling the window, which is what the
    /// wizard's second page offers.</para>
    /// </summary>
    /// <param name="background">
    /// A <c>FullResizeImageTemplate</c> name, or null for an empty window.
    /// </param>
    /// <param name="tagLine">
    /// A comment for the top of the file (<see cref="Settings.AppSettings.TagLine"/>).
    /// DAoCEd writes one into every file it saves; here only a created file can
    /// carry one, because the rest keep their own bytes.
    /// </param>
    public static WindowDef CreateWindow(
        Package pkg, string name, int width, int height, string? background = null,
        string? tagLine = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            throw new ArgumentException(T("A window needs a name."), nameof(name));
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width), T("A window cannot be smaller than nothing."));
        if (WindowNameTaken(pkg, name))
            throw new InvalidOperationException(T("'{0}' is already in the package.", name));

        string key = FileKeyFor(pkg, name);
        var doc = NewWindowDocument(pkg, name, width, height, background?.Trim(), tagLine);
        var node = doc.Root!.Elements().First(e => e.Name.LocalName == "WindowTemplate");

        pkg.Docs[key] = doc;
        pkg.OriginalPaths[key] = PathFor(pkg, name);
        pkg.DirtyFiles.Add(key);

        var win = new WindowDef
        {
            Id = name,
            Name = name,
            WindowId = name,
            File = key,
            Doc = doc,
            Node = node,
            Dirty = true,
        };
        pkg.Windows.Add(win);

        // The order the loader leaves behind, so the list does not rearrange
        // itself the next time the package is opened.
        pkg.Windows.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));

        RegisterInclude(pkg, name);
        return win;
    }

    /// <summary>
    /// The file a new window file has to be announced in, or null when the
    /// package has no include list at all.
    ///
    /// <para><b>Not <c>uimain.xml</c>, even though that is where the chain
    /// starts.</b> The game patch overwrites it, so an include
    /// written there survives until the next patch and no longer. DAoCEd names
    /// <c>assets.xml</c> outright and refuses to create the window when it is
    /// missing; the preference here is the same file, with the fullest include
    /// list as the fallback so a package that organises itself differently
    /// still works.</para>
    /// </summary>
    public static string? IncludeHost(Package pkg)
    {
        string? best = null;
        int most = 0;

        foreach (var (key, doc) in pkg.Docs)
        {
            if (string.Equals(FileName(key), "uimain.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            int count = Includes(doc).Count();
            if (count == 0) continue;

            if (string.Equals(FileName(key), "assets.xml", StringComparison.OrdinalIgnoreCase))
                return key;

            // Ordinal on a tie, so the answer does not depend on dictionary order.
            if (count > most || (count == most && string.CompareOrdinal(key, best) < 0))
            {
                best = key;
                most = count;
            }
        }

        return best;
    }

    /// <summary>
    /// Add <c>&lt;Include&gt;name.xml&lt;/Include&gt;</c> behind the last one
    /// there, and mark that file. Without it the editor would show a window the
    /// game never loads.
    /// </summary>
    /// <returns>Whether an include list was found to write into.</returns>
    public static bool RegisterInclude(Package pkg, string name)
    {
        if (IncludeHost(pkg) is not string host) return false;

        var doc = pkg.Docs[host];
        string entry = name + ".xml";

        var existing = Includes(doc).ToList();
        if (existing.Any(e => string.Equals(e.Value.Trim(), entry, StringComparison.OrdinalIgnoreCase)))
            return true;

        var fresh = new XElement(doc.Root!.Name.Namespace + "Include", entry);
        if (existing.Count > 0) XmlEdit.InsertAfter(existing[^1], fresh);
        else XmlEdit.Append(doc.Root!, fresh);

        pkg.DirtyFiles.Add(host);
        return true;
    }

    private static IEnumerable<XElement> Includes(XDocument doc) =>
        doc.Descendants().Where(e =>
            string.Equals(e.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The document of a new window: the declaration and the root element the
    /// package's own files carry, then DAoCEd's window fields.
    /// </summary>
    private static XDocument NewWindowDocument(
        Package pkg, string name, int width, int height, string? background, string? tagLine)
    {
        // The root element is copied from a file that already holds a window,
        // because that is exactly the kind of file being written. Without one,
        // what DAoCEd writes (IncludeNode.writeXMLRoot).
        var model = pkg.Windows.FirstOrDefault()?.Doc.Root;
        var root = model is null
            ? new XElement("Root_Element", new XAttribute("ID", "DAOCUi"))
            : new XElement(model.Name, model.Attributes());

        var ns = root.Name.Namespace;
        var win = new XElement(ns + "WindowTemplate");

        // DAoCEd's WindowtemplateNode(name, w, h), in the order its files show.
        void Set(string tag, string value) => win.Add(new XElement(ns + tag, value));
        Set("Name", name);
        Set("WindowId", name);
        Set("CloseButton", "false");
        Set("MoveButton", "false");
        Set("TopRightResizeButton", "false");
        Set("BottomRightResizeButton", "false");
        Set("BottomLeftResizeButton", "false");
        Set("ResizeButtonOffsetX", "0");
        Set("ResizeButtonOffsetY", "0");
        Set("TitleWidth", Int(width));
        Set("TitleHeight", "16");
        Set("Width", Int(width));
        Set("Height", Int(height));
        Set("ResizeableWidth", "0");
        Set("ResizeableHeight", "0");
        Set("ResizeableTwoWayWidth", "0");
        Set("ResizeableTwoWayHeight", "0");
        Set("MinWidth", "0");
        Set("MinHeight", "0");

        if (!string.IsNullOrEmpty(background))
            win.Add(new XElement(ns + "FullResizeImageDef",
                new XElement(ns + "Position",
                    new XElement(ns + "X", "0"),
                    new XElement(ns + "Y", "0")),
                new XElement(ns + "TemplateName", background),
                new XElement(ns + "Width", Int(width)),
                new XElement(ns + "Height", Int(height))));

        root.Add(win);

        // ISO-8859-1 is what every file of a real package declares and what
        // DAoCEd writes. PackageWriter encodes to it and repairs the
        // declaration should a character turn up that it cannot hold.
        var doc = new XDocument(new XDeclaration("1.0", "ISO-8859-1", null), root);

        // Built in code, so there is not a single line break in it yet. The
        // step comes from a file the package already has.
        XmlEdit.LayDocument(doc, XmlEdit.IndentUnitOf(model?.Document));

        // The tag line goes in after the layout, on the window's own line —
        // Lay walks elements and would leave a comment hanging off the root's
        // opening tag. "--" is dropped, since XML forbids it inside a comment
        // (see ElementEditor.CommentText).
        if (!string.IsNullOrWhiteSpace(tagLine))
        {
            var comment = new XComment(T(" {0} ", tagLine.Replace("--", "-").Trim()));
            if (XmlEdit.IndentOf(win) is XText indent) win.AddBeforeSelf(comment, new XText(indent.Value));
            else win.AddBeforeSelf(comment);
        }

        return doc;
    }

    private static string Int(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The normalised key a window file of this name would have.</summary>
    private static string FileKeyFor(Package pkg, string name) =>
        Ingest.FileResolver.NormPath(FolderOf(pkg) + name + ".xml");

    /// <summary>The path it is written under — the folder in its own spelling.</summary>
    private static string PathFor(Package pkg, string name)
    {
        string key = FolderOf(pkg);
        if (key.Length == 0) return name + ".xml";

        // The folder as some sibling spells it, not the lower-cased key: on a
        // case-sensitive file system "Custom/" and "custom/" are two folders.
        string sibling = pkg.Docs.Keys.FirstOrDefault(k => k.StartsWith(key, StringComparison.Ordinal)) ?? "";
        string spelt = pkg.PathOf(sibling);
        int slash = spelt.LastIndexOf('/');
        return (slash < 0 ? "" : spelt[..(slash + 1)]) + name + ".xml";
    }

    /// <summary>
    /// The folder the package's active XML files live in, with a trailing
    /// slash. The new file belongs beside them: the loader takes only the
    /// smallest path depth, so a window one level down would not be
    /// there after a reload.
    /// </summary>
    private static string FolderOf(Package pkg)
    {
        string any = pkg.Docs.Keys.FirstOrDefault() ?? "";
        int slash = any.LastIndexOf('/');
        return slash < 0 ? "" : any[..(slash + 1)];
    }

    private static string FileName(string key)
    {
        int slash = key.LastIndexOf('/');
        return slash < 0 ? key : key[(slash + 1)..];
    }
}
