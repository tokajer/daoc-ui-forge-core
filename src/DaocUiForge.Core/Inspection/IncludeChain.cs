using System.Xml.Linq;
using DaocUiForge.Core.Model;

namespace DaocUiForge.Core.Inspection;

/// <summary>
/// Which of the package's XML files the client actually reads.
///
/// <para>The client starts at <c>uimain.xml</c> and follows the
/// &lt;Include&gt; entries; a file nobody names is a file the game never
/// opens, however correct its contents. DAoCEd builds its whole window list
/// that way (<c>RootNode</c> plus <c>IncludeNode.loadChildren</c>), which is
/// why a window can be in this editor and not in that one.</para>
///
/// <para><b>Loading deliberately does not work this way</b>. Taking
/// the chain as the loader's rule hid 54 of 107 windows in the reference
/// package, and the reason is in the format itself: the game patch overwrites
/// <c>uimain.xml</c>, so a package's own copy is a snapshot of whichever patch
/// it was made under, and the windows it leaves out are still edited and still
/// shipped. So every root-level XML file is loaded and the chain is reported
/// instead of enforced.</para>
/// </summary>
public static class IncludeChain
{
    /// <summary>Where the client starts reading.</summary>
    public const string Root = "uimain.xml";

    /// <summary>
    /// The files reachable from <see cref="Root"/>, as <see cref="Package"/>
    /// keys, including the root itself. Empty when the package has no
    /// <c>uimain.xml</c> — then nothing is known, which is not the same as
    /// nothing being reachable, and <see cref="Unreachable"/> says so.
    /// </summary>
    public static HashSet<string> Reachable(Package pkg)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (KeyOf(pkg, Root) is not string start) return seen;

        var todo = new Stack<string>();
        todo.Push(start);

        while (todo.Count > 0)
        {
            string key = todo.Pop();
            if (!seen.Add(key)) continue;
            if (!pkg.Docs.TryGetValue(key, out var doc)) continue;

            foreach (var inc in doc.Descendants().Where(e =>
                         string.Equals(e.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase)))
            {
                if (KeyOf(pkg, inc.Value.Trim()) is string next) todo.Push(next);
            }
        }

        return seen;
    }

    /// <summary>
    /// The package's XML files that no include names, sorted by path. These
    /// are in the editor and not in the game.
    ///
    /// <para>Without a <c>uimain.xml</c> the answer is an empty list rather
    /// than "all of them": a package that ships only the files it changed is
    /// normal, and calling every one of them unreachable would be noise.</para>
    /// </summary>
    public static List<string> Unreachable(Package pkg)
    {
        if (KeyOf(pkg, Root) is null) return new List<string>();

        var reachable = Reachable(pkg);
        return pkg.Docs.Keys
            .Where(k => !reachable.Contains(k))
            .OrderBy(k => pkg.PathOf(k), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Is this window's file reachable? The question a window list wants to
    /// ask about one row.
    /// </summary>
    public static bool Reaches(HashSet<string> reachable, WindowDef win) =>
        reachable.Count == 0 || reachable.Contains(win.File);

    /// <summary>
    /// A file name out of an include entry, resolved to a <see cref="Package"/>
    /// key. Only the file name counts: the loader keeps root-level XML only,
    /// and the entries are written with and without a folder in
    /// front of them.
    /// </summary>
    private static string? KeyOf(Package pkg, string entry)
    {
        if (entry.Length == 0) return null;

        string wanted = entry.Replace('\\', '/');
        int slash = wanted.LastIndexOf('/');
        if (slash >= 0) wanted = wanted[(slash + 1)..];

        foreach (string key in pkg.Docs.Keys)
        {
            string name = key.Replace('\\', '/');
            int i = name.LastIndexOf('/');
            if (i >= 0) name = name[(i + 1)..];
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) return key;
        }

        return null;
    }
}
