using DaocUiForge.Core.Diagnostics;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>What is wrong with a file somebody is about to import, if anything.</summary>
/// <param name="Blocking">True when importing it makes no sense at all.</param>
/// <param name="Message">Meant to be shown as it stands.</param>
public readonly record struct ImportWarning(bool Blocking, string Message);

/// <summary>One file that was copied into the package.</summary>
/// <param name="Key">Its normalised key in <see cref="Package.Files"/>.</param>
/// <param name="Path">The package-relative path it went in under.</param>
/// <param name="Bytes">How large it is.</param>
/// <param name="Replaced">Whether it took the place of a file that was there.</param>
public readonly record struct ImportedFile(string Key, string Path, int Bytes, bool Replaced);

/// <summary>
/// Copy a file into the package — DAoCEd's <c>ImportFileAction</c> and its
/// <c>Importer</c>, and the half of "bind a texture" that was missing.
/// <see cref="TextureCatalog.Create"/> writes the name and the path; until now
/// the file itself still had to be put there with a file manager, and a package
/// opened from a ZIP had nowhere to put it at all.
///
/// <para><b>The package carries it from here on.</b> The bytes go into
/// <see cref="Package.Files"/>, the spelling into
/// <see cref="Package.OriginalPaths"/>, and the key into
/// <see cref="Package.AddedFiles"/> so that "save changes" writes it out and not
/// only a ZIP export does — the difference matters, because saving into the
/// package's own folder is what people do when that folder is the game's.</para>
///
/// <para><b>Warnings, not refusals.</b> DAoCEd checks the same two things and
/// then asks "Continue Importing?" — the format (the client reads BMP, DDS and
/// TGA) and whether the edges are powers of two. Both are worth saying and
/// neither is worth enforcing: PNG is fine as a working file, and the client
/// does draw a 24 x 40 sprite, it simply may not do it well. What <i>is</i>
/// refused is a file with no bytes and a path that would leave the package.</para>
/// </summary>
public static class ResourceImport
{
    /// <summary>What the DAoC client itself reads (DAoCEd's Importer, same list).</summary>
    public static readonly string[] ClientFormats = { ".bmp", ".dds", ".tga" };

    /// <summary>What this editor can decode and therefore show in the preview.</summary>
    public static readonly string[] KnownFormats =
        { ".bmp", ".dds", ".tga", ".png", ".jpg", ".jpeg", ".ttf" };

    /// <summary>
    /// Look at a file before importing it. An empty list means nothing to say.
    /// </summary>
    /// <param name="source">Where the file is now.</param>
    public static IReadOnlyList<ImportWarning> Inspect(string source)
    {
        var found = new List<ImportWarning>();

        if (!File.Exists(source))
            return new[] { new ImportWarning(true, T("There is no file at {0}.", source)) };

        var info = new FileInfo(source);
        if (info.Length == 0)
            return new[] { new ImportWarning(true, T("{0} is empty.", info.Name)) };

        string ext = Path.GetExtension(source).ToLowerInvariant();

        if (!KnownFormats.Contains(ext))
        {
            found.Add(new ImportWarning(false,
                T("{0} is not a format this editor decodes, so the preview will stay empty. The file is copied in all the same.", ext)));
        }
        else if (ext != ".ttf" && !ClientFormats.Contains(ext))
        {
            found.Add(new ImportWarning(false,
                T("The game reads BMP, DDS and TGA. A {0} shows here and stays empty in the game.", ext.TrimStart('.').ToUpperInvariant())));
        }

        // The size check DAoCEd makes. It reads the header itself rather than
        // decoding, because a 2048 x 2048 DDS is expensive to decode for a
        // question about two numbers.
        if (SizeOf(source) is (int w, int h))
        {
            if (!IsPowerOfTwo(w) || !IsPowerOfTwo(h))
                found.Add(new ImportWarning(false,
                    T("{0} x {1} is not a power of two on both edges (16, 32, 64, 128, 256…). The client may not draw it properly.", w, h)));
        }

        return found;
    }

    /// <summary>
    /// Copy a file into the package under <paramref name="target"/>, a path
    /// relative to the package root.
    /// </summary>
    /// <param name="replace">
    /// Whether a file already there may be overwritten. False and one is there
    /// throws — the caller asks first, as DAoCEd's importer does.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The source is missing, the target is empty or leaves the package, or a
    /// file is there and <paramref name="replace"/> is false. The message is
    /// meant to be shown as it stands.
    /// </exception>
    /// <summary>
    /// The key <see cref="Add"/> will file this target under. Public because a
    /// caller has to know it beforehand: <see cref="UndoStack"/> notes the
    /// state of a file before it is written, and raw bytes raise no event to
    /// notice it by.
    /// </summary>
    public static string KeyFor(string target) => FileResolver.NormPath(CleanTarget(target));

    public static ImportedFile Add(Package pkg, string source, string target, bool replace = false)
    {
        if (!File.Exists(source))
            throw new ArgumentException(T("There is no file at {0}.", source));

        string rel = CleanTarget(target);
        string key = KeyFor(target);

        bool there = pkg.Files.ContainsKey(key);
        if (there && !replace)
            throw new ArgumentException(T("The package already has a file at {0}.", rel));

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException(T("{0} could not be read: {1}", source, e.Message));
        }

        pkg.Files[key] = bytes;
        // Only for a file that is new. Overwriting one keeps the spelling the
        // package already uses — renaming Assets/ to assets/ on the way past is
        // the fault Package.OriginalPaths exists to prevent.
        if (!there) pkg.OriginalPaths[key] = rel;
        pkg.AddedFiles.Add(key);

        Log.Default.Info("Import", T("{0} -> {1} ({2} bytes)", Path.GetFileName(source), pkg.PathOf(key), bytes.Length));
        return new ImportedFile(key, pkg.PathOf(key), bytes.Length, there);
    }

    /// <summary>
    /// Turn a chosen file's path into one relative to the package root, when it
    /// is below it. A file from anywhere else keeps its bare name — the caller
    /// decides the folder.
    /// </summary>
    public static string RelativeTo(string? root, string file)
    {
        if (string.IsNullOrWhiteSpace(root)) return Path.GetFileName(file);

        try
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            return rel.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(rel)
                ? Path.GetFileName(file)
                : rel;
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(file);
        }
    }

    /// <summary>
    /// The files this session added, in the spelling they went in under.
    /// </summary>
    public static IReadOnlyList<string> Added(Package pkg) =>
        pkg.AddedFiles.Select(pkg.PathOf).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// A target path the package can hold: slashes, no leading one, and nothing
    /// that climbs out of the root. A "../" in there would write outside the
    /// package on the next save.
    /// </summary>
    private static string CleanTarget(string target)
    {
        string rel = (target ?? "").Trim().Replace('\\', '/').TrimStart('/');

        if (rel.Length == 0)
            throw new ArgumentException(T("A file needs a path inside the package."));
        if (rel.Split('/').Any(s => s == ".."))
            throw new ArgumentException(T("{0} leads out of the package.", rel));

        return rel;
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

    /// <summary>
    /// Width and height out of the header, or null when the format does not say
    /// so here. Only TGA and DDS are read: those are the two the package uses
    /// and the two this project has decoders for.
    /// </summary>
    private static (int W, int H)? SizeOf(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".tga" && ext != ".dds") return null;

            using var fs = File.OpenRead(path);
            var head = new byte[128];
            if (fs.Read(head, 0, head.Length) < 32) return null;

            return ext == ".tga"
                ? (head[12] | (head[13] << 8), head[14] | (head[15] << 8))
                : (Read32(head, 16), Read32(head, 12));   // DDS: height at 12, width at 16
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int Read32(byte[] b, int at) =>
        b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);
}
