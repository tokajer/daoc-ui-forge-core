using System.Xml.Linq;

namespace DaocUiForge.Core.Model;

/// <summary>
/// A loaded DAoC user-interface package. Equivalent to the global state of the
/// HTML version (windows[], templates{}, textures{}, fonts{}).
///
/// A principle taken from the original: every change goes straight onto the
/// XML tree (<see cref="WindowDef.Doc"/>). Saving only serialises the tree —
/// nothing is tracked alongside it.
/// </summary>
public sealed class Package
{
    /// <summary>One entry per &lt;WindowTemplate&gt;.</summary>
    public List<WindowDef> Windows { get; } = new();

    /// <summary>
    /// Templates by type and name: Templates["ButtonTemplate"]["my_button"].
    /// </summary>
    public Dictionary<string, Dictionary<string, XElement>> Templates { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Type-independent fast access to templates, case-insensitive. With
    /// duplicate names the one loaded last wins (per the documentation).
    /// </summary>
    public Dictionary<string, XElement> ByNameLc { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Texture name → relative path from &lt;Texture&gt;&lt;File&gt;.</summary>
    public Dictionary<string, string> Textures { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Font name → metadata.</summary>
    public Dictionary<string, FontRef> Fonts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every file of the package: normalised lower-case path → raw bytes.
    /// Textures and fonts are resolved through this.
    /// </summary>
    public Dictionary<string, byte[]> Files { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The path a file arrived under, in its original spelling: normalised key
    /// → relative path as it was on disk or in the ZIP.
    ///
    /// <para><see cref="Files"/> is keyed lower-case so that references resolve
    /// case-insensitively (<see cref="Ingest.FileResolver.NormPath"/>). Writing
    /// a package back out from those keys alone would rename every file —
    /// <c>Assets/</c> to <c>assets/</c>, <c>CBT_500.bmp</c> to
    /// <c>cbt_500.bmp</c> — which is a change nobody asked for and one that
    /// bites on a case-sensitive file system. Export goes through
    /// <see cref="PathOf"/> instead.</para>
    /// </summary>
    public Dictionary<string, string> OriginalPaths { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The parsed document of every active XML file: normalised key →
    /// <see cref="XDocument"/>.
    ///
    /// <para>Kept for saving. A document is not reachable through
    /// <see cref="Windows"/> alone: a file may hold nothing but templates, and
    /// the last window of a file can be deleted — in both cases the tree still
    /// has to be written out.</para>
    /// </summary>
    public Dictionary<string, XDocument> Docs { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Files that have to be written out although no window of theirs is
    /// marked dirty — a window was deleted, or a template was changed.
    /// <see cref="Editing.PackageWriter.ChangedFiles"/> takes both sources.
    /// </summary>
    public HashSet<string> DirtyFiles { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Files this session copied into the package that are not XML — an
    /// imported texture or TTF (<see cref="Editing.ResourceImport"/>).
    ///
    /// <para><see cref="Editing.PackageWriter.ChangedFiles"/> answers with the
    /// documents that have to be serialised again; a binary has no document, so
    /// without this list an imported file would ride along in a ZIP export and
    /// be missing from "save changes" — the one route people use when the
    /// package folder is the game's own.</para>
    /// </summary>
    public HashSet<string> AddedFiles { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The game installation's folder, when one is set — DAoCEd's
    /// <c>GameDir</c>. Null means textures under <c>atlantis/</c> stay
    /// unloadable, which is what they were before this existed.
    /// </summary>
    public string? GameFolder { get; set; }

    /// <summary>
    /// The game folder's assets: normalised relative path → the file's place on
    /// disk. Indexed rather than read in, and deliberately <b>not</b> part of
    /// <see cref="Files"/>: those files belong to the game, not to the package,
    /// and an export that carried them would ship gigabytes of somebody else's
    /// artwork (see <see cref="Ingest.GameFolder"/>).
    /// </summary>
    public Dictionary<string, string> GameFiles { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Texture names the drawing layer resolved out of the game folder rather
    /// than out of the package. They are correct — the game has those files —
    /// but they are the reason a package can look complete here and empty on
    /// somebody else's installation, so the report names them.
    /// </summary>
    public HashSet<string> GameFolderTextures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The original spelling of a file path, or the key itself when unknown.</summary>
    public string PathOf(string key) =>
        OriginalPaths.TryGetValue(key, out var p) ? p : key;

    /// <summary>Templates that were referenced but not found (inspection report).</summary>
    public HashSet<string> MissingTemplates { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Textures that were referenced but not found (inspection report).</summary>
    public HashSet<string> MissingTextures { get; } = new(StringComparer.OrdinalIgnoreCase);
}
