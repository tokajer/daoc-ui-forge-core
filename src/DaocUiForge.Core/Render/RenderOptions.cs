using System.Xml.Linq;
using SkiaSharp;

namespace DaocUiForge.Core.Render;

/// <summary>
/// The knobs of the preview. Equivalent to the global switches of the HTML
/// original (<c>showDummy</c>, <c>activeTab</c>, <c>simState</c>, and the
/// "missing templates" and "zones" check boxes).
/// </summary>
public sealed class RenderOptions
{
    /// <summary>Put sample data into empty fields (<c>showDummy</c>).</summary>
    public bool ShowSampleData { get; set; } = true;

    /// <summary>Id of the visible tab; null means the first one there is.</summary>
    public string? ActiveTab { get; set; }

    /// <summary>
    /// Simulated status effect: 0 none, 1 mez, 2 diz, 3 poison,
    /// 4 nearsight.
    /// </summary>
    public int SimulatedState { get; set; }

    /// <summary>Mark elements whose template cannot be found.</summary>
    public bool MarkMissing { get; set; } = true;

    /// <summary>Hint at the drag zone of the title area (not a game element!).</summary>
    public bool ShowZones { get; set; }

    /// <summary>
    /// Editor-only hints that the game never draws: the tinted click area of an
    /// InvisibleButtonDef, the hatched icon cell in sample list rows, and the
    /// dark panel stand-in for a list box without a background. On by default
    /// for the editor; a preview that should look like the game turns it off.
    /// </summary>
    public bool ShowEditorHints { get; set; } = true;

    /// <summary>
    /// Draw a placeholder for the client-native tab row of chat-style windows
    /// (&lt;TabName&gt; on the WindowTemplate). Unlike &lt;TabsDef&gt; this is
    /// chrome the client itself paints from a template no package carries —
    /// neither DAoCEd nor the original HTML port ever drew it either. On by
    /// default because, unlike the drag zone, it IS visible in the game.
    /// </summary>
    public bool ShowNativeTabs { get; set; } = true;
}

/// <summary>One tab of a window (&lt;TabsDef&gt;&lt;Tab&gt;).</summary>
/// <param name="Id">Id from &lt;Id&gt;.</param>
/// <param name="Name">Display name.</param>
public readonly record struct TabInfo(string Id, string Name);

/// <summary>
/// A drawn element together with its place in the resulting image. Replaces
/// the DOM nodes of the original: mouse picking, the element tree and the
/// inspection report all run off this.
/// </summary>
public sealed class RenderedElement
{
    /// <summary>The XML node — the basis for the property editor.</summary>
    public required XElement Def { get; init; }

    /// <summary>Element type, e.g. "ButtonDef".</summary>
    public required string Tag { get; init; }

    /// <summary>Place in image pixels.</summary>
    public SKRect Bounds { get; internal set; }

    /// <summary>Drawn? Hidden effect icons report false.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Name of the template that could not be found, else null.</summary>
    public string? MissingTemplate { get; init; }

    /// <summary>
    /// Textures that are declared but not in the package — the usual case
    /// being a reference into the game folder (atlantis/…).
    /// </summary>
    public IReadOnlyList<string> MissingTextures { get; init; } = Array.Empty<string>();
}
