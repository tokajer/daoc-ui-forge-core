using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>One row of the file list.</summary>
/// <param name="Key">Its normalised key in <see cref="Package.Files"/>.</param>
/// <param name="Path">The spelling it arrived under, relative to the package root.</param>
/// <param name="Bytes">How large it is.</param>
/// <param name="Kind">Extension without the dot, upper case, or "—".</param>
/// <param name="Added">Imported this session and not yet written out.</param>
/// <param name="TextureNames">The texture declarations pointing at it.</param>
public sealed record PackageFile(
    string Key, string Path, int Bytes, string Kind, bool Added, IReadOnlyList<string> TextureNames);

/// <summary>
/// Every file of the package, not only the declared textures — the resource
/// browser half of DAoCEd's <c>ImportFileAction</c>.
///
/// <para><b>Why it is worth its own list.</b> <see cref="TextureCatalog"/>
/// answers "what does the XML declare"; this answers "what is actually in the
/// package". The two differ in both directions, and each difference is a real
/// finding: a TGA nobody declares is dead weight in the ZIP, and a declaration
/// pointing at a file that is not there is an element that stays empty in the
/// game. Neither the HTML original nor DAoCEd lists the files at all — a browser
/// cannot, and DAoCEd works straight on the game folder, where the file manager
/// is the list.</para>
/// </summary>
public static class FileCatalog
{
    /// <summary>Every file, sorted by path.</summary>
    public static List<PackageFile> All(Package pkg)
    {
        // Which declarations point at which file. Resolved the way the drawing
        // layer resolves it, so "used by" here and "loads" there agree.
        var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, path) in pkg.Textures)
        {
            if (FileResolver.Find(pkg.Files, path) is not string key) continue;
            if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<string>();
            list.Add(name);
        }

        foreach (var list in byKey.Values)
            list.Sort(StringComparer.OrdinalIgnoreCase);

        return pkg.Files
            .Select(f => new PackageFile(
                f.Key,
                pkg.PathOf(f.Key),
                f.Value.Length,
                KindOf(f.Key),
                pkg.AddedFiles.Contains(f.Key),
                byKey.GetValueOrDefault(f.Key) ?? (IReadOnlyList<string>)Array.Empty<string>()))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Filter on the path, which is what the eye is scanning.</summary>
    public static IEnumerable<PackageFile> Filter(IEnumerable<PackageFile> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(f => f.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Files nothing points at: no texture declaration, no font, and not an XML
    /// the loader read. They are what a package accumulates over years of
    /// editing, and they are carried in every export.
    /// </summary>
    public static List<PackageFile> Unused(Package pkg)
    {
        var fonts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in pkg.Fonts.Values)
            if (FileResolver.Find(pkg.Files, f.File) is string key) fonts.Add(key);

        return All(pkg)
            .Where(f => f.TextureNames.Count == 0
                        && !fonts.Contains(f.Key)
                        && !pkg.Docs.ContainsKey(f.Key)
                        && !f.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>A size a person can read at a glance.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => T("{0} B", bytes),
        < 1024 * 1024 => T("{0:0.#} KB", bytes / 1024.0),
        _ => T("{0:0.#} MB", bytes / (1024.0 * 1024.0)),
    };

    private static string KindOf(string key)
    {
        string ext = Path.GetExtension(key);
        return ext.Length > 1 ? ext[1..].ToUpperInvariant() : "—";
    }
}
