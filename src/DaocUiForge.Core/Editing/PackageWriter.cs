using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// Writing a package back out: one XML file, every changed file, or the whole
/// package as a ZIP. Ported from <c>serializeFile</c>, <c>#bSave</c> and
/// <c>#bZip</c> of the HTML original (lines 3045–3076).
///
/// <para>The rule of the original holds: everything that was edited sits on the
/// tree of <see cref="WindowDef.Doc"/>, so writing is a plain serialisation.
/// Nothing is tracked alongside it.</para>
///
/// <para><b>Clean diffs.</b> The loader
/// parses with <see cref="LoadOptions.PreserveWhitespace"/>, so every line break
/// and every indent of the source file is in the tree as a text node.
/// Serialising with <see cref="SaveOptions.DisableFormatting"/> writes those back
/// out untouched, which means an untouched file comes out byte for byte as it
/// went in and a changed file differs only where it was changed. Neither of the
/// two obvious alternatives is needed — re-indenting the tree would
/// have rewritten every file, and textual node replacement is unnecessary work
/// once the whitespace is preserved anyway.</para>
///
/// <para><b>Encoding is a deviation from the original.</b> DAoC files declare
/// <c>encoding="ISO-8859-1"</c> and carry German umlauts as single bytes. The
/// original builds a <c>Blob</c> from a JavaScript string, so the browser writes
/// UTF-8 bytes under a header saying ISO-8859-1 — the file contradicts itself and
/// its umlauts arrive broken. Here the text is encoded the way the declaration
/// says. When a character has no place in that encoding the file is written as
/// UTF-8 and the declaration is corrected to match, rather than losing the
/// character to a question mark.</para>
/// </summary>
public static class PackageWriter
{
    /// <summary>
    /// What the original prepends to a file without a declaration
    /// (<c>serializeFile</c>, line 3047).
    /// </summary>
    private const string FallbackEncoding = "ISO-8859-1";

    // -----------------------------------------------------------------
    // One document
    // -----------------------------------------------------------------

    /// <summary>
    /// The complete text of a document: its declaration, then the tree exactly
    /// as it was loaded apart from the changes.
    /// </summary>
    public static string SerializeText(XDocument doc)
    {
        string body = Body(doc);
        var decl = doc.Declaration ?? new XDeclaration("1.0", FallbackEncoding, null);

        // The line break between the declaration and the root element is a
        // whitespace node of the document, so a parsed file already carries it
        // in `body`. Only a tree built in code needs one added — putting it
        // there unconditionally is a byte of drift in every file.
        string gap = body.StartsWith('\n') || body.StartsWith('\r') ? "" : "\n";
        return decl + gap + body;
    }

    /// <summary>
    /// The tree as text, with the line breaks it actually holds.
    ///
    /// <para><b>Why not <c>doc.ToString(SaveOptions.DisableFormatting)</c>.</b>
    /// LINQ-to-XML builds its writer settings from the save options and never
    /// touches <see cref="XmlWriterSettings.NewLineHandling"/>, which therefore
    /// stays at <see cref="NewLineHandling.Replace"/> with
    /// <see cref="XmlWriterSettings.NewLineChars"/> defaulted to
    /// <see cref="Environment.NewLine"/>. Every line break the loader preserved
    /// is then rewritten to the line break of the machine doing the writing —
    /// so on Windows a file stored with LF comes back out with CRLF in every
    /// single line, which is precisely the thing this class promises not to do.
    /// It passes on Linux for the same reason it fails on Windows, so only CI
    /// ever saw it.</para>
    ///
    /// <para>The file's line ending is decided in one place, from the source
    /// bytes (see <see cref="SerializeFile(XDocument, byte[])"/>). The writer's
    /// job is to leave it alone.</para>
    /// </summary>
    private static string Body(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,   // SerializeText writes it itself
            Indent = false,              // what SaveOptions.DisableFormatting means
            NewLineHandling = NewLineHandling.None,
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings)) doc.WriteTo(writer);
        return sb.ToString();
    }

    /// <summary>
    /// The bytes of one file, encoded as its declaration says.
    /// </summary>
    /// <param name="doc">The tree to write.</param>
    /// <param name="source">
    /// The bytes the file was loaded from, when they are known. They decide the
    /// line ending and whether the file ends in a line break — XML parsing
    /// normalises CRLF to LF (§2.11 of the specification), so a file written
    /// from the tree alone would differ in every single line.
    /// </param>
    public static byte[] SerializeFile(XDocument doc, byte[]? source = null)
    {
        string text = SerializeText(doc);
        bool crlf = source is not null && HasCrLf(source);

        // Normalise first: the tree only ever holds "\n" (the parser folds CRLF
        // per §2.11), but a caller may hand over a tree built by other means.
        if (crlf) text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

        // A trailing line break is a whitespace node like any other and comes
        // out of the tree by itself. It is added only where there was one to
        // begin with and the tree does not carry it — a file built in code.
        if ((source is null || EndsWithNewline(source)) && !text.EndsWith('\n'))
            text += crlf ? "\r\n" : "\n";

        // The name the written declaration carries — the fallback one when the
        // document had no declaration at all, so a repair below finds it.
        string declared = doc.Declaration is null ? FallbackEncoding
            : doc.Declaration.Encoding ?? "";

        return Encode(text, declared, source);
    }

    /// <summary>The bytes of one file of the package, changes included.</summary>
    public static byte[] SerializeFile(Package pkg, string file)
    {
        if (!pkg.Docs.TryGetValue(file, out var doc))
            throw new ArgumentException(T("No XML document loaded for '{0}'.", file), nameof(file));

        return SerializeFile(doc, pkg.Files.TryGetValue(file, out var raw) ? raw : null);
    }

    /// <summary>
    /// Encode the text the way the file itself is encoded.
    ///
    /// <para><b>The bytes beat the declaration.</b> 97 of the 105 XML files of
    /// the reference package hold UTF-8 under <c>encoding="ISO-8859-1"</c>,
    /// because that is what the original leaves behind. Writing them out "as
    /// declared" would rewrite every umlaut in files this session never opened,
    /// and produce a file in two encodings at once as soon as one label is
    /// edited. <see cref="Ingest.PackageLoader.XmlEncodingOf"/> decides, so
    /// reading and writing cannot come apart.</para>
    ///
    /// <para>Without source bytes — a document built in code — the declaration
    /// is all there is. No encoding name at all then means ISO-8859-1 when the
    /// file had no declaration either (what the original prepends), and UTF-8
    /// when it had one without an encoding: that is what a bare
    /// <c>&lt;?xml version="1.0"?&gt;</c> means per the specification, and one
    /// file of the reference package really is written that way.</para>
    /// </summary>
    private static byte[] Encode(string text, string? declared, byte[]? source)
    {
        Encoding enc;
        if (source is not null)
        {
            enc = Ingest.PackageLoader.XmlEncodingOf(source);
        }
        else
        {
            try
            {
                enc = string.IsNullOrWhiteSpace(declared)
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(declared, EncoderFallback.ExceptionFallback,
                        DecoderFallback.ReplacementFallback);
            }
            catch (ArgumentException)
            {
                enc = Encoding.UTF8;      // a name no encoding answers to
            }
        }

        // No BOM: the package files have none, and the game reads bytes.
        if (enc.CodePage == Encoding.UTF8.CodePage)
            return new UTF8Encoding(false).GetBytes(text);

        // An unencodable character has to throw rather than turn into "?" —
        // the point of the repair below is not to lose it.
        enc = Encoding.GetEncoding(enc.CodePage, EncoderFallback.ExceptionFallback,
            DecoderFallback.ReplacementFallback);

        try
        {
            return enc.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            // A character the declared encoding cannot hold — an em dash pasted
            // into a label, say. Writing "?" would lose it silently, so the file
            // becomes UTF-8 and says so.
            string fixedText = text.Replace(
                $"encoding=\"{declared}\"", "encoding=\"UTF-8\"", StringComparison.Ordinal);
            return new UTF8Encoding(false).GetBytes(fixedText);
        }
    }

    private static bool HasCrLf(byte[] bytes)
    {
        for (int i = 0; i + 1 < bytes.Length; i++)
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n') return true;
        return false;
    }

    private static bool EndsWithNewline(byte[] bytes) =>
        bytes.Length > 0 && bytes[^1] == (byte)'\n';

    // -----------------------------------------------------------------
    // Which files have changed
    // -----------------------------------------------------------------

    /// <summary>
    /// The files that have to be written out: those of every dirty window, plus
    /// <see cref="Package.DirtyFiles"/>.
    ///
    /// <para><b>A deviation from the original</b>, which builds this list from
    /// <c>windows.filter(w=&gt;w.dirty)</c> alone. Delete the only window of a
    /// file and there is no dirty window left to name it — the deletion is then
    /// simply never saved. <see cref="PackageEditor.DeleteWindow"/> therefore
    /// notes the file itself.</para>
    /// </summary>
    public static IReadOnlyList<string> ChangedFiles(Package pkg)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var w in pkg.Windows)
            if (w.Dirty) files.Add(w.File);
        foreach (var f in pkg.DirtyFiles) files.Add(f);

        // Only what there is a tree for. A dirty mark on a file that never
        // parsed would otherwise throw at the end of a save.
        return files.Where(pkg.Docs.ContainsKey).ToList();
    }

    /// <summary>
    /// Everything that would be lost by closing the package now: the changed
    /// documents plus the files this session imported
    /// (<see cref="Package.AddedFiles"/>).
    ///
    /// <para><see cref="ChangedFiles"/> deliberately answers with documents
    /// only — it feeds the serialiser. This is the question the interface asks:
    /// "is there unsaved work", and an imported texture that never reaches the
    /// disk is exactly that.</para>
    /// </summary>
    public static IReadOnlyList<string> PendingFiles(Package pkg)
    {
        var files = new SortedSet<string>(ChangedFiles(pkg), StringComparer.Ordinal);
        foreach (string f in pkg.AddedFiles)
            if (pkg.Files.ContainsKey(f)) files.Add(f);
        return files.ToList();
    }

    /// <summary>
    /// Clear the dirty marks of the files named — after they were written, and
    /// only for those, so a partial save does not claim the rest is safe.
    /// </summary>
    public static void ClearDirty(Package pkg, IEnumerable<string> files)
    {
        var done = new HashSet<string>(files, StringComparer.Ordinal);
        foreach (var w in pkg.Windows)
            if (done.Contains(w.File)) w.Dirty = false;
        pkg.DirtyFiles.RemoveWhere(done.Contains);
        pkg.AddedFiles.RemoveWhere(done.Contains);
    }

    // -----------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------

    /// <summary>
    /// Write one file of the package to <paramref name="path"/> — the "save
    /// window" of the original, which hands the file to the browser's download
    /// folder under its bare name.
    /// </summary>
    public static void SaveFileAs(Package pkg, string file, string path)
    {
        File.WriteAllBytes(path, SerializeFile(pkg, file));
        ClearDirty(pkg, new[] { file });
    }

    /// <summary>
    /// Write every changed file into <paramref name="dir"/>, keeping the folder
    /// structure of the package, and clear their dirty marks.
    ///
    /// <para>An imported binary goes out as it came in (see
    /// <see cref="Package.AddedFiles"/>): it has no document to serialise, and
    /// leaving it behind would write an XML that names a texture the folder does
    /// not have.</para>
    /// </summary>
    /// <returns>The files written, in the spelling they were written under.</returns>
    public static IReadOnlyList<string> SaveChanged(Package pkg, string dir)
    {
        var pending = PendingFiles(pkg);
        var written = new List<string>();

        foreach (string file in pending)
        {
            string rel = pkg.PathOf(file);
            string target = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            string? folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllBytes(target, pkg.Docs.ContainsKey(file)
                ? SerializeFile(pkg, file)
                : pkg.Files[file]);
            written.Add(rel);
        }

        ClearDirty(pkg, pending);
        return written;
    }

    // -----------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------

    /// <summary>
    /// Every file of the package as it stands: the raw bytes, with the XML of
    /// each loaded document replaced by the current state of its tree.
    /// </summary>
    /// <param name="changedOnly">
    /// Only the files from <see cref="ChangedFiles"/> — a patch rather than a
    /// package.
    /// </param>
    public static Dictionary<string, byte[]> CurrentFiles(Package pkg, bool changedOnly = false)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (changedOnly)
        {
            foreach (string file in ChangedFiles(pkg))
                result[file] = SerializeFile(pkg, file);
            return result;
        }

        foreach (var kv in pkg.Files)
            result[kv.Key] = pkg.Docs.ContainsKey(kv.Key)
                ? SerializeFile(pkg, kv.Key)
                : kv.Value;

        // A document with no bytes behind it is a file this session created
        // (see PackageEditor.CreateWindow). Walking Files alone would leave it
        // out of the export — the window would be in the editor and not in the
        // package.
        foreach (string key in pkg.Docs.Keys)
            if (!result.ContainsKey(key)) result[key] = SerializeFile(pkg, key);

        return result;
    }

    /// <summary>
    /// Write the package as a ZIP — what a player installs. Paths keep the
    /// spelling they came in with (see <see cref="Package.OriginalPaths"/>).
    ///
    /// <para>The original cannot do this: a browser without a ZIP library
    /// downloads the changed files one at a time, and it does exactly that
    /// (<c>#bZip</c>, a <c>setTimeout</c> per file).</para>
    /// </summary>
    public static void WriteZip(Package pkg, Stream target, bool changedOnly = false)
    {
        var files = CurrentFiles(pkg, changedOnly);

        // leaveOpen: the caller owns the stream — it may be a FileStream in a
        // using of its own, or a MemoryStream that still has to be read.
        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var key in files.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var entry = zip.CreateEntry(pkg.PathOf(key), CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(files[key], 0, files[key].Length);
        }
    }

    /// <summary>Write the package as a ZIP file, and clear the dirty marks.</summary>
    public static void SaveZip(Package pkg, string path, bool changedOnly = false)
    {
        var changed = ChangedFiles(pkg);

        using (var fs = File.Create(path))
            WriteZip(pkg, fs, changedOnly);

        ClearDirty(pkg, changed);
    }
}
