namespace DaocUiForge.Core.Ingest;

/// <summary>
/// Resolves texture and font paths against the files of the package. Ported
/// from <c>normPath</c> and <c>findFile</c> of the HTML original.
///
/// DAoC references assets in several ways (with and without a leading
/// "custom/", with partial paths). Resolution works through a fixed sequence
/// of strategies.
/// </summary>
public static class FileResolver
{
    /// <summary>Backslashes to slashes, lower case, leading "./" or "/" removed.</summary>
    public static string NormPath(string p)
    {
        string n = p.Replace('\\', '/').ToLowerInvariant();
        if (n.StartsWith("./")) n = n.Substring(2);
        else if (n.StartsWith("/")) n = n.Substring(1);
        return n;
    }

    /// <returns>Normalised key in <paramref name="files"/>, or null.</returns>
    public static string? Find(IReadOnlyDictionary<string, byte[]> files, string path) =>
        Find(files.Keys, files.ContainsKey, path);

    /// <summary>
    /// The same search over a table that holds paths rather than bytes — the
    /// game folder, which is indexed by where its files are on disk rather than
    /// read into memory (<see cref="GameFolder"/>).
    /// </summary>
    public static string? Find(IReadOnlyDictionary<string, string> files, string path) =>
        Find(files.Keys, files.ContainsKey, path);

    /// <param name="keys">Normalised keys, in no particular order.</param>
    /// <param name="has">Exact lookup — a dictionary probe rather than a scan.</param>
    private static string? Find(IEnumerable<string> keys, Func<string, bool> has, string path)
    {
        string n = NormPath(path);
        if (has(n)) return n;

        // 1) the full path as a suffix
        foreach (var k in keys)
            if (k.EndsWith("/" + n, StringComparison.Ordinal)) return k;

        // 2) path without a leading "custom/" (how DAoC references its assets)
        string stripped = n.StartsWith("custom/") ? n.Substring("custom/".Length) : n;
        if (has(stripped)) return stripped;
        foreach (var k in keys)
            if (k.EndsWith("/" + stripped, StringComparison.Ordinal)) return k;

        // 3) the last two path segments (e.g. ghost/ghost_uielements.tga)
        var seg = stripped.Split('/');
        if (seg.Length >= 2)
        {
            string tail = string.Join("/", seg[^2], seg[^1]);
            foreach (var k in keys)
                if (k.EndsWith("/" + tail, StringComparison.Ordinal)) return k;
        }

        // 4) last resort: the bare file name
        string base_ = seg[^1];
        foreach (var k in keys)
        {
            int slash = k.LastIndexOf('/');
            string fn = slash >= 0 ? k.Substring(slash + 1) : k;
            if (fn == base_) return k;
        }

        return null;
    }
}
