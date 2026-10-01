using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Reference;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Inspection;

/// <summary>
/// What is wrong with a package. Ported from <c>#bReport</c> of the HTML
/// original (line 3078), which looks for elements pointing at templates the
/// package does not define — those stay invisible in the game as well.
///
/// <para><b>The report renders the package first.</b> The original reads
/// <c>missingTex</c>, a set that <c>slice</c> fills while drawing — so its
/// report only ever knows about the windows somebody happened to look at, and
/// says "no missing textures" for a package whose every texture is missing as
/// long as nothing was opened. <see cref="Build"/> draws all of them first.
/// That costs a few seconds on a real package and is the reason this returns a
/// value instead of writing into the interface.</para>
///
/// <para>Three sections have no counterpart in the original: the elements that
/// <i>threw</i> while drawing (it has <c>catch(e){el=null}</c> and loses them),
/// the windows that hit the surface bound, and the files whose declared
/// encoding contradicts their bytes.</para>
/// </summary>
public sealed class InspectionReport
{
    /// <summary>A template that is referenced but not defined, and by whom.</summary>
    public readonly record struct MissingTemplate(
        string Name, IReadOnlyList<string> Users);

    /// <summary>A texture declared with &lt;Texture&gt; whose file is not in the package.</summary>
    public readonly record struct ExternalTexture(string Name, string Path);

    /// <summary>An element that threw while being drawn.</summary>
    public readonly record struct Failure(string Window, string Tag, string Name, string Message);

    /// <summary>A file whose declaration names an encoding it is not written in.</summary>
    public readonly record struct EncodingMismatch(string File, string Declared, string Actual);

    public required int WindowCount { get; init; }
    public required int ElementCount { get; init; }

    /// <summary>Referenced but not defined — these elements stay invisible in the game.</summary>
    public required IReadOnlyList<MissingTemplate> MissingTemplates { get; init; }

    /// <summary>
    /// Texture names used but never declared with &lt;Texture&gt;&lt;Name&gt;.
    /// These elements stay empty in the game too.
    /// </summary>
    public required IReadOnlyList<string> UndeclaredTextures { get; init; }

    /// <summary>
    /// Correctly declared, but the file lives in the game folder rather than in
    /// the package. The game shows them; only the preview cannot load them.
    /// </summary>
    public required IReadOnlyList<ExternalTexture> ExternalTextures { get; init; }

    /// <summary>
    /// Textures that were drawn out of the game folder rather than out of the
    /// package (<see cref="Ingest.GameFolder"/>). Not a fault — the game has
    /// those files — but the package does not carry them, so the same preview on
    /// a machine without the installation shows the hatching instead.
    /// </summary>
    public required IReadOnlyList<ExternalTexture> GameFolderTextures { get; init; }

    /// <summary>Elements that threw. The original swallows these.</summary>
    public required IReadOnlyList<Failure> Failures { get; init; }

    /// <summary>Windows whose image hit <see cref="WindowRenderer"/>'s surface bound.</summary>
    public required IReadOnlyList<string> ClippedWindows { get; init; }

    /// <summary>Files that declare one encoding and are written in another.</summary>
    public required IReadOnlyList<EncodingMismatch> EncodingMismatches { get; init; }

    /// <summary>
    /// XML files no &lt;Include&gt; names, with the windows in them. The client
    /// reads its files through the chain that starts at <c>uimain.xml</c>, so
    /// these are in the editor and never in the game — and they are the reason
    /// this editor lists windows DAoCEd does not (<see cref="IncludeChain"/>).
    /// </summary>
    public required IReadOnlyList<UnreachableFile> UnreachableFiles { get; init; }

    /// <summary>A file the include chain does not reach, and what is in it.</summary>
    public readonly record struct UnreachableFile(string Path, IReadOnlyList<string> Windows);

    /// <summary>
    /// Nothing to report. <see cref="GameFolderTextures"/> is deliberately not
    /// part of it: those drew correctly, and calling a package faulty for using
    /// the game's own artwork would be wrong.
    /// </summary>
    public bool IsClean =>
        MissingTemplates.Count == 0 && UndeclaredTextures.Count == 0
        && ExternalTextures.Count == 0 && Failures.Count == 0
        && ClippedWindows.Count == 0 && EncodingMismatches.Count == 0
        && UnreachableFiles.Count == 0;

    /// <summary>The one-line summary the original puts in its toast.</summary>
    public string Summary =>
        IsClean
            ? T("No faults found.")
            : string.Join(" · ", new[]
            {
                MissingTemplates.Count > 0 ? T("{0} missing templates", MissingTemplates.Count) : null,
                UndeclaredTextures.Count > 0 ? T("{0} undeclared textures", UndeclaredTextures.Count) : null,
                ExternalTextures.Count > 0 ? T("{0} from the game folder", ExternalTextures.Count) : null,
                Failures.Count > 0 ? T("{0} failed elements", Failures.Count) : null,
                ClippedWindows.Count > 0 ? T("{0} clipped windows", ClippedWindows.Count) : null,
                EncodingMismatches.Count > 0 ? T("{0} encoding mismatches", EncodingMismatches.Count) : null,
                UnreachableFiles.Count > 0 ? T("{0} files no include names", UnreachableFiles.Count) : null,
            }.Where(s => s is not null));

    // -----------------------------------------------------------------
    // Building
    // -----------------------------------------------------------------

    /// <summary>
    /// The tags a template name can live under, in the order the original reads
    /// them (line 3084).
    /// </summary>
    private static readonly string[] TemplateTags =
        { "TemplateName", "Templatename", "HRButtonTemplateName", "ImageAreaTemplateName" };

    /// <summary>
    /// Draw the whole package and collect what went wrong.
    /// </summary>
    /// <param name="pkg">The package. Its MissingTemplates/MissingTextures are
    /// filled by the render pass — that is where those sets come from.</param>
    /// <param name="reference">Reference data, or the default set.</param>
    public static InspectionReport Build(Package pkg, ReferenceData? reference = null)
    {
        // A context of its own, with the default options: the report must not
        // depend on which switches the preview happens to have on, and it must
        // not disturb the caches the interface is using.
        using var ctx = new RenderContext(pkg, new RenderOptions(), reference);

        var failures = new List<Failure>();
        var clipped = new List<string>();
        int elements = 0;

        foreach (var win in pkg.Windows)
        {
            using var res = WindowRenderer.Render(ctx, win);

            elements += res.Elements.Count;
            if (res.Clipped) clipped.Add(win.Id);
            foreach (var f in res.Failures)
                failures.Add(new Failure(win.Id, f.Tag, f.Name, f.Message));
        }

        return new InspectionReport
        {
            WindowCount = pkg.Windows.Count,
            ElementCount = elements,
            MissingTemplates = FindMissingTemplates(pkg),
            UndeclaredTextures = pkg.MissingTextures
                .Where(t => !IsNone(t) && !pkg.Textures.ContainsKey(t))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ExternalTextures = pkg.MissingTextures
                .Where(t => !IsNone(t) && pkg.Textures.ContainsKey(t))
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Select(t => new ExternalTexture(t, pkg.Textures[t]))
                .ToList(),
            GameFolderTextures = pkg.GameFolderTextures
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Select(t => new ExternalTexture(t, pkg.Textures.GetValueOrDefault(t, t)))
                .ToList(),
            Failures = failures,
            ClippedWindows = clipped,
            EncodingMismatches = FindEncodingMismatches(pkg),
            UnreachableFiles = FindUnreachableFiles(pkg),
        };
    }

    /// <summary>
    /// The XML files the include chain does not reach, with the windows they
    /// hold. Not a fault of the editor and not always a fault of the package —
    /// but it is the difference between "the game loads this" and "the game
    /// does not", and nothing else says which.
    /// </summary>
    private static List<UnreachableFile> FindUnreachableFiles(Package pkg) =>
        IncludeChain.Unreachable(pkg)
            .Select(key => new UnreachableFile(
                pkg.PathOf(key),
                pkg.Windows.Where(w => w.File == key)
                    .Select(w => w.Id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();

    /// <summary>
    /// "none" is the format's way of saying "no texture here", not a fault. The
    /// renderer still records it, because <c>slice</c> in the original does not
    /// filter it either (line 924) — but in a report it is pure noise.
    /// </summary>
    private static bool IsNone(string name) =>
        string.Equals(name, "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every element whose template is not in the package, grouped by template
    /// and sorted by how often it is missed — the order of the original.
    /// </summary>
    private static List<MissingTemplate> FindMissingTemplates(Package pkg)
    {
        var users = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var win in pkg.Windows)
        {
            foreach (var def in win.Node.Elements()
                         .Where(e => e.Name.LocalName.EndsWith("Def", StringComparison.Ordinal)))
            {
                string name = TemplateNameOf(def);
                if (name.Length == 0 || IsNone(name)) continue;
                if (PackageLoader.FindTemplate(pkg, name) is not null) continue;

                if (!users.TryGetValue(name, out var list))
                    users[name] = list = new SortedSet<string>(StringComparer.Ordinal);
                list.Add(T("{0}  [{1}]", win.Id, win.File));
            }
        }

        return users
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new MissingTemplate(kv.Key, kv.Value.ToList()))
            .ToList();
    }

    private static string TemplateNameOf(XElement def)
    {
        foreach (string tag in TemplateTags)
        {
            string v = Xml.Tx(def, tag);
            if (v.Length > 0) return v;
        }
        return "";
    }

    /// <summary>
    /// Files that declare one encoding and are written in another — what the
    /// original leaves behind on every save. Pure ASCII does not
    /// count: those bytes are the same under either encoding, so there is
    /// nothing to go wrong.
    /// </summary>
    private static List<EncodingMismatch> FindEncodingMismatches(Package pkg)
    {
        var found = new List<EncodingMismatch>();

        foreach (var (file, doc) in pkg.Docs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!pkg.Files.TryGetValue(file, out var bytes)) continue;
            if (bytes.All(b => b < 0x80)) continue;

            // No declaration, or one without an encoding, means UTF-8 — and
            // then the bytes cannot contradict it, because a file that is not
            // valid UTF-8 has no declaration to disagree with either.
            string declared = doc.Declaration?.Encoding ?? "";
            if (declared.Length == 0) continue;

            string actual = PackageLoader.XmlEncodingOf(bytes).WebName;
            if (!string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase))
                found.Add(new EncodingMismatch(pkg.PathOf(file), declared, actual));
        }

        return found;
    }

    // -----------------------------------------------------------------
    // The text
    // -----------------------------------------------------------------

    /// <summary>
    /// The report as text, in the layout of the original (line 3092 onwards) —
    /// a heading, an explanation of what the section means, then the list.
    /// </summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(T("Inspection report\n\n"));
        sb.Append(T("{0} windows, {1} elements drawn.\n\n", WindowCount, ElementCount));

        if (IsClean)
        {
            sb.Append(T("No missing templates, no unloadable textures, nothing that failed.\n"));
            AppendGameFolder(sb);
            return sb.ToString();
        }

        if (MissingTemplates.Count > 0)
        {
            Section(sb, T("MISSING TEMPLATES"),
                T("These elements point at templates the package does not define. They stay invisible in the game too."));
            foreach (var t in MissingTemplates)
            {
                sb.Append(T("{0}  ({1}x)\n", t.Name, t.Users.Count));
                foreach (string u in t.Users) sb.Append("    ").Append(u).Append('\n');
                sb.Append('\n');
            }
        }

        if (UndeclaredTextures.Count > 0)
        {
            Section(sb, T("UNDECLARED TEXTURES"),
                T("These texture names are used but nowhere declared with <Texture><Name>. The elements stay empty in the game too."));
            foreach (string t in UndeclaredTextures) sb.Append("    ").Append(t).Append('\n');
            sb.Append('\n');
        }

        if (ExternalTextures.Count > 0)
        {
            Section(sb, T("TEXTURES FROM THE GAME FOLDER"),
                T("These textures are declared correctly, but the file is not in the package — it is in the game folder (Atlantis). The game shows them normally; only this preview cannot load them. The elements are hatched with a \"?\" in the preview. Point the editor at the game installation under Settings and they will draw."));
            foreach (var t in ExternalTextures)
                sb.Append("    ").Append(t.Name).Append("   ->  ").Append(t.Path).Append('\n');
            sb.Append('\n');
        }

        if (Failures.Count > 0)
        {
            Section(sb, T("ELEMENTS THAT FAILED"),
                T("These elements threw while being drawn. The window is still shown, the element is not. This has no counterpart in the HTML original, which discards the error."));
            foreach (var f in Failures)
            {
                string what = f.Name.Length > 0 ? T("{0} \"{1}\"", f.Tag, f.Name) : f.Tag;
                sb.Append(T("    {0} / {1}: {2}\n", f.Window, what, f.Message));
            }
            sb.Append('\n');
        }

        if (ClippedWindows.Count > 0)
        {
            Section(sb, T("CLIPPED WINDOWS"),
                T("The image of these windows hit the upper size bound. Usually a mistyped <Position> or <Width> — a five-digit value."));
            foreach (string w in ClippedWindows) sb.Append("    ").Append(w).Append('\n');
            sb.Append('\n');
        }

        if (EncodingMismatches.Count > 0)
        {
            Section(sb, T("ENCODING DOES NOT MATCH THE DECLARATION"),
                T("These files declare one encoding and are written in another, so their umlauts arrive mangled in the game. It is what the HTML editor leaves behind on every save. Saving here preserves the bytes rather than repairing them, because a repair would rewrite files you never opened — fix them deliberately, in one pass."));
            foreach (var m in EncodingMismatches)
                sb.Append(T("    {0}   declares {1}, is {2}\n", m.File, m.Declared, m.Actual));
            sb.Append('\n');
        }

        if (UnreachableFiles.Count > 0)
        {
            Section(sb, T("NO INCLUDE NAMES THESE FILES"),
                T("The client starts at uimain.xml and follows the <Include> list. These files are not in that chain, so the game never opens them, however correct they are — and DAoCEd does not list them either, because it builds its window list from the same chain. This editor loads every root-level XML file on purpose (the chain hides 54 of 107 windows in a real package) and reports the difference instead."));
            foreach (var f in UnreachableFiles)
            {
                sb.Append("    ").Append(f.Path);
                if (f.Windows.Count > 0)
                    sb.Append("   ->  ").Append(string.Join(", ", f.Windows));
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        AppendGameFolder(sb);
        return sb.ToString();
    }

    /// <summary>
    /// The textures that came out of the game folder. Its own method because it
    /// belongs in the text whether or not there are faults — a clean package
    /// that draws only because this machine has the game installed is exactly
    /// the case worth knowing about.
    /// </summary>
    private void AppendGameFolder(StringBuilder sb)
    {
        if (GameFolderTextures.Count == 0) return;

        sb.Append('\n');
        Section(sb, T("DRAWN FROM THE GAME FOLDER"),
                T("These drew out of the game installation, not out of the package. That is correct — every client has those files — but the package does not carry them, so the same window on a machine without the game shows the hatching instead. Import a copy if the package is meant to stand on its own."));
        foreach (var t in GameFolderTextures)
            sb.Append("    ").Append(t.Name).Append("   ->  ").Append(t.Path).Append('\n');
        sb.Append('\n');
    }

    /// <summary>
    /// A heading and the paragraph that says what the section means.
    ///
    /// <para>One string rather than a line per argument: the lines are one
    /// sentence broken for the source's margin, and a translation has to be
    /// free to break it somewhere else (<see cref="Localization.Strings"/>).
    /// The wrapping happens here instead.</para>
    /// </summary>
    private static void Section(StringBuilder sb, string title, string what)
    {
        sb.Append(title).Append('\n');
        foreach (string line in Wrap(what)) sb.Append(line).Append('\n');
        sb.Append('\n');
    }

    /// <summary>Break a paragraph at the last space before the margin.</summary>
    private static IEnumerable<string> Wrap(string text, int width = 72)
    {
        foreach (string paragraph in text.Split('\n'))
        {
            string rest = paragraph;
            while (rest.Length > width)
            {
                int cut = rest.LastIndexOf(' ', width);
                if (cut <= 0) cut = width;
                yield return rest[..cut];
                rest = rest[(cut + 1)..];
            }
            yield return rest;
        }
    }
}
