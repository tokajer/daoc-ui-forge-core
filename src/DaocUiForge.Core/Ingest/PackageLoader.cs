using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Diagnostics;
using DaocUiForge.Core.Formats;
using DaocUiForge.Core.Model;

namespace DaocUiForge.Core.Ingest;

/// <summary>
/// Reads a DAoC user-interface package out of a folder or a ZIP file. Ported
/// from <c>ingest</c> of the HTML original, including every format rule.
///
/// The essential rules, which have to be preserved:
/// - Only XML files in the package's ROOT folder are evaluated. Subfolders
///   (NF, OF, Options) are variants and stay out of it. The root depth is the
///   smallest path depth among all XML files.
/// - Templates: generically everything ending in "Template" (except
///   WindowTemplate). A fixed list of types would miss some.
/// - With duplicate names the one loaded LAST wins (ByNameLc is overwritten).
/// - The window id is &lt;Name&gt;/&lt;n&gt;, NOT &lt;WindowId&gt;.
/// - Files below a dot-prefixed folder are not part of the package
///   (see <see cref="IsPackageFile"/>), and files below a folder the user named
///   are left out on top of that (see <see cref="DefaultSkippedFolders"/>).
/// </summary>
public static class PackageLoader
{
    static PackageLoader()
    {
        // For the ISO-8859-1 fallback when decoding broken umlauts.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Whether a file belongs to the package at all. A path segment beginning
    /// with a dot is metadata of something else (version control, an editor,
    /// the operating system) and is skipped.
    ///
    /// <para><b>Why the loader has a rule about this.</b> A package folder is
    /// very often a working copy: the reference package carries a
    /// <c>.git</c> directory, and its 10,439 files were read into
    /// <see cref="Package.Files"/>, which put them in a ZIP export
    /// (<see cref="Editing.PackageWriter"/> writes what the package holds) and
    /// into the last resort of <see cref="FileResolver.Find"/>, where a texture
    /// nobody can resolve could come back as a blob out of
    /// <c>.git/objects</c>. Nothing the client reads is dot-prefixed: the format
    /// names its assets as plain relative paths.</para>
    ///
    /// <para>A bare <c>.</c> segment passes (it is only a redundant spelling of
    /// the same folder); <c>..</c> does not, which also keeps a ZIP from
    /// reaching outside the package, the stance
    /// <see cref="Editing.ResourceImport"/> already takes.</para>
    /// </summary>
    /// <param name="path">A package-relative path, slashes either way.</param>
    public static bool IsPackageFile(string path) =>
        !path.Replace('\\', '/').Split('/').Any(seg => seg.Length > 1 && seg[0] == '.');

    /// <summary>
    /// The folders a package is loaded without unless the caller says
    /// otherwise. <see cref="Settings.AppSettings.SkipFolders"/> starts here and
    /// the settings dialog edits it.
    ///
    /// <para><b>Why this is a preference and not a rule.</b>
    /// <see cref="IsPackageFile"/> can be a rule because it asks a question
    /// about the format: nothing the client reads is dot-prefixed, so
    /// dot-prefixed is somebody else's metadata whatever the folder is called.
    /// "node_modules" is not that. It is the name one ecosystem happens to give
    /// its dependency store, and a loader that knows such names has stopped
    /// describing the format and started keeping a list. So it is kept where a
    /// list belongs: in the settings, visible, editable, and reported in the log
    /// whenever it leaves something out.</para>
    ///
    /// <para>It earns its place all the same. The reference package carries a
    /// checkout under <c>custom/picker</c> whose 7,310 files under
    /// <c>node_modules</c> outweigh the package's own 274 MB more than three
    /// times over, and every one of them was read into
    /// <see cref="Package.Files"/>, shown in the file pane, and written into a
    /// ZIP export. The alternative considered was to load only the file types
    /// the client reads, which would have solved this one case by making the
    /// file pane blind to everything else a package legitimately carries.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSkippedFolders = new[] { "node_modules" };

    /// <summary>
    /// Which of <paramref name="skip"/> stands on the path, or null. Only
    /// folders count: the last segment is the file itself.
    /// </summary>
    private static string? SkippedFolderOn(string path, HashSet<string> skip)
    {
        if (skip.Count == 0) return null;

        var seg = path.Split('/');
        for (int i = 0; i < seg.Length - 1; i++)
            if (skip.Contains(seg[i])) return seg[i];
        return null;
    }

    private static HashSet<string> SkipSet(IEnumerable<string>? folders) =>
        new(folders ?? DefaultSkippedFolders, StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads from an unpacked folder (recursively).</summary>
    /// <param name="skipFolders">
    /// Folder names to leave out; null for <see cref="DefaultSkippedFolders"/>,
    /// an empty list for none at all.
    /// </param>
    public static Package LoadFromDirectory(string dir, IEnumerable<string>? skipFolders = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var original = new Dictionary<string, string>(StringComparer.Ordinal);
        var skip = SkipSet(skipFolders);
        var hit = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int metadata = 0, left = 0;

        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
            if (!IsPackageFile(rel)) { metadata++; continue; }
            if (SkippedFolderOn(rel, skip) is string folder) { hit.Add(folder); left++; continue; }
            files[FileResolver.NormPath(rel)] = File.ReadAllBytes(path);
            original[FileResolver.NormPath(rel)] = rel;
        }

        ReportSkipped(metadata, left, hit);
        return Load(files, original);
    }

    /// <summary>Loads from a ZIP file.</summary>
    public static Package LoadFromZip(string zipPath, IEnumerable<string>? skipFolders = null)
    {
        using var fs = File.OpenRead(zipPath);
        return LoadFromZip(fs, skipFolders);
    }

    /// <summary>Loads from a ZIP stream (an upload, say).</summary>
    public static Package LoadFromZip(Stream zipStream, IEnumerable<string>? skipFolders = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var original = new Dictionary<string, string>(StringComparer.Ordinal);
        var skip = SkipSet(skipFolders);
        var hit = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int metadata = 0, left = 0;

        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/")) continue; // folder
            string rel = entry.FullName.Replace('\\', '/');
            if (!IsPackageFile(rel)) { metadata++; continue; }
            if (SkippedFolderOn(rel, skip) is string folder) { hit.Add(folder); left++; continue; }
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            files[FileResolver.NormPath(rel)] = ms.ToArray();
            original[FileResolver.NormPath(rel)] = rel;
        }

        ReportSkipped(metadata, left, hit);
        return Load(files, original);
    }

    /// <summary>
    /// What was left outside the package, said separately because the two
    /// reasons are different in kind: the first is the format's answer, the
    /// second is the user's. A folder nobody meant to exclude is found by
    /// reading this line, so it names the folders it really struck rather than
    /// the ones it was watching for.
    /// </summary>
    private static void ReportSkipped(int metadata, int left, IReadOnlyCollection<string> folders)
    {
        if (metadata > 0)
            Log.Default.Info("Package",
                $"{metadata} files below dot-prefixed folders were skipped; they are not part of the package.");

        if (left > 0)
            Log.Default.Info("Package",
                $"{left} files below {string.Join(", ", folders)} were left out; " +
                "the list is in the settings under \"Folders to leave out\".");
    }

    /// <summary>The core: builds the package model out of raw files.</summary>
    /// <param name="rawFiles">Normalised path (see FileResolver.NormPath) → bytes.</param>
    /// <param name="originalPaths">
    /// The same keys → the spelling the file arrived under. Optional; without
    /// it an export writes the lower-case keys (see Package.OriginalPaths).
    /// </param>
    public static Package Load(
        Dictionary<string, byte[]> rawFiles,
        IReadOnlyDictionary<string, string>? originalPaths = null)
    {
        var pkg = new Package();
        foreach (var kv in rawFiles) pkg.Files[kv.Key] = kv.Value;
        if (originalPaths is not null)
            foreach (var kv in originalPaths) pkg.OriginalPaths[kv.Key] = kv.Value;

        var xmlFiles = pkg.Files.Keys
            .Where(k => k.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (xmlFiles.Count == 0) return pkg;

        // Root depth = smallest path depth. Only those files are active.
        int rootDepth = xmlFiles.Min(k => k.Split('/').Length);
        var activeXml = xmlFiles.Where(k => k.Split('/').Length == rootDepth).ToList();

        foreach (var key in activeXml)
        {
            string text = DecodeXmlText(pkg.Files[key]);
            XDocument doc;
            try
            {
                doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException ex)
            {
                // The original skips the file without a word, which is how a
                // package with one broken file comes up short by a window and
                // nothing says why. Skipped it stays — one damaged file must not
                // stop the other 104 — but it is said (Log, DAoCEd's ShowLogAction).
                Log.Default.Error("Package", $"{key} will not parse and was skipped: {ex.Message}");
                continue;
            }
            if (doc.Root == null) continue;

            // Kept so that saving reaches the tree even when the file holds no
            // window at all (templates only), or when its last one is deleted.
            pkg.Docs[key] = doc;
        }

        Reindex(pkg);
        return pkg;
    }

    /// <summary>
    /// Derive everything the documents imply: windows, templates, textures and
    /// fonts. <see cref="Package.Docs"/> is the authority; the rest of the model
    /// is a reading of it.
    ///
    /// <para><b>Why this is a step of its own.</b> Undo restores documents
    /// (<see cref="Editing.UndoStack"/>), and a restored document is a new tree:
    /// every <see cref="XElement"/> the tables hold points into the tree that
    /// was replaced. Deriving the tables again is the whole repair, and it is
    /// the same code a fresh load runs, so an undone package and a reloaded one
    /// cannot come out differently.</para>
    ///
    /// <para>The order is the insertion order of <see cref="Package.Docs"/>,
    /// which is the order the load put them in. That matters: with duplicate
    /// template names the one read LAST wins (§4), so re-deriving in another
    /// order would quietly change which template a name resolves to.</para>
    ///
    /// <para>Dirty marks live on <see cref="Model.WindowDef"/>, and the windows
    /// are built afresh here — so they are carried over by id. A window that
    /// was renamed while dirty keeps its mark under the new name, because the
    /// rename wrote the tag and the id together.</para>
    /// </summary>
    public static void Reindex(Package pkg)
    {
        var dirty = pkg.Windows.Where(w => w.Dirty).Select(w => w.Id).ToHashSet(StringComparer.Ordinal);

        pkg.Windows.Clear();
        pkg.Templates.Clear();
        pkg.ByNameLc.Clear();
        pkg.Textures.Clear();
        pkg.Fonts.Clear();

        foreach (var kv in pkg.Docs)
        {
            IngestTextures(pkg, kv.Value);
            IngestFonts(pkg, kv.Value);
            IngestTemplates(pkg, kv.Value);
            IngestWindows(pkg, kv.Value, kv.Key);
        }

        pkg.Windows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        foreach (var w in pkg.Windows)
            if (dirty.Contains(w.Id)) w.Dirty = true;

        // Work out the TTF line heights (see TtfMetrics).
        ResolveFontMetrics(pkg);
    }

    private static void IngestTextures(Package pkg, XDocument doc)
    {
        foreach (var tex in doc.Descendants().Where(e =>
                     string.Equals(e.Name.LocalName, "Texture", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Xml.NameOf(tex);
            string file = Xml.Tx(tex, "File");
            if (name.Length > 0 && file.Length > 0)
                pkg.Textures[name] = file; // case-insensitive through the comparer
        }
    }

    private static void IngestFonts(Package pkg, XDocument doc)
    {
        foreach (var f in doc.Descendants().Where(e =>
                     string.Equals(e.Name.LocalName, "TTFFont", StringComparison.OrdinalIgnoreCase)))
        {
            string nm = Xml.NameOf(f);
            if (nm.Length == 0) continue;
            string file = Xml.Tx(f, "File");
            double height = ParseNum(Xml.Tx(f, "Height"), 11);
            bool bold = nm.Contains("bold", StringComparison.OrdinalIgnoreCase)
                        || file.Contains("bold", StringComparison.OrdinalIgnoreCase);
            pkg.Fonts[nm] = new FontRef { File = file, Height = height, Bold = bold };
        }
    }

    private static void IngestTemplates(Package pkg, XDocument doc)
    {
        foreach (var t in doc.Descendants())
        {
            string tag = t.Name.LocalName;
            if (!tag.EndsWith("Template", StringComparison.Ordinal)) continue;
            if (tag == "WindowTemplate") continue;
            string nm = Xml.NameOf(t);
            if (nm.Length == 0) continue;

            if (!pkg.Templates.TryGetValue(tag, out var byType))
            {
                byType = new Dictionary<string, XElement>(StringComparer.Ordinal);
                pkg.Templates[tag] = byType;
            }
            byType[nm] = t;
            pkg.ByNameLc[nm] = t; // last one wins
        }
    }

    private static void IngestWindows(Package pkg, XDocument doc, string file)
    {
        foreach (var w in doc.Descendants().Where(e => e.Name.LocalName == "WindowTemplate"))
        {
            string nm = Xml.NameOf(w);
            string wid = Xml.Tx(w, "WindowId");
            string id = nm.Length > 0 ? nm : wid;
            if (id.Length == 0) continue;

            pkg.Windows.Add(new WindowDef
            {
                Id = id,
                Name = nm.Length > 0 ? nm : wid,
                WindowId = wid.Length > 0 ? wid : null,
                File = file,
                Doc = doc,
                Node = w,
            });
        }
    }

    /// <summary>
    /// Read every declared font's line height out of its TTF (see
    /// <see cref="TtfMetrics"/>).
    ///
    /// <para>Public and repeatable, because mounting a game folder can put a
    /// font within reach that was not there at load time — and a font whose
    /// metrics are unknown draws about a quarter too large.</para>
    /// </summary>
    public static void ResolveFontMetrics(Package pkg)
    {
        foreach (var font in pkg.Fonts.Values)
        {
            if (string.IsNullOrEmpty(font.File)) continue;

            byte[]? bytes = FileResolver.Find(pkg.Files, font.File) is string key
                ? pkg.Files[key]
                : GameFolder.Find(pkg, font.File) is string gameKey
                    ? GameFolder.Read(pkg, gameKey)
                    : null;

            if (bytes is null) continue;
            double lh = TtfMetrics.LineHeightEm(bytes);
            if (lh > 0) font.LineHeight = lh;
        }
    }

    /// <summary>
    /// Finds a template through the preferred types, then case-insensitively.
    /// Ported from <c>findTpl</c>.
    /// </summary>
    public static XElement? FindTemplate(Package pkg, string? name, params string[] preferredTags)
    {
        if (string.IsNullOrEmpty(name) || name == "none") return null;
        foreach (var tag in preferredTags)
            if (pkg.Templates.TryGetValue(tag, out var g) && g.TryGetValue(name, out var el))
                return el;
        return pkg.ByNameLc.TryGetValue(name, out var any) ? any : null;
    }

    /// <summary>
    /// How the bytes of an XML file are really encoded: UTF-8 when they decode
    /// as UTF-8 without a fault, ISO-8859-1 otherwise.
    ///
    /// <para>The declaration is deliberately not consulted, because in real
    /// packages it lies: 97 of the 105 files of the reference package hold UTF-8
    /// bytes under <c>encoding="ISO-8859-1"</c>. That is what the original
    /// leaves behind \u2014 a browser <c>Blob</c> writes UTF-8 whatever the header
    /// says. <see cref="Editing.PackageWriter"/> asks the same question when it
    /// writes, so a file that was not touched keeps its bytes.</para>
    /// </summary>
    public static Encoding XmlEncodingOf(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding("iso-8859-1"); }
            catch { return new UTF8Encoding(false); }
        }
    }

    private static string DecodeXmlText(byte[] bytes) => XmlEncodingOf(bytes).GetString(bytes);

    private static double ParseNum(string s, double def)
    {
        if (string.IsNullOrWhiteSpace(s)) return def;
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
    }
}
