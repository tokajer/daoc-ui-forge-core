using System.Xml.Linq;

namespace DaocUiForge.Core.Model;

/// <summary>
/// One window (&lt;WindowTemplate&gt;). Equivalent to an entry in windows[] of
/// the HTML version.
/// </summary>
public sealed class WindowDef
{
    // Id and Name are settable: renaming a window in the editor writes the
    // tag AND has to keep the in-memory model in step, otherwise the list and
    // the file disagree until the next reload (see XmlEdit.WindowIdKey).
    public required string Id { get; set; }         // unique id (<n>/<Name>)
    public required string Name { get; set; }       // display name
    public string? WindowId { get; init; }          // NOT unique, see §3.2
    public required string File { get; init; }       // source file in the package
    public required XDocument Doc { get; init; }      // the tree that gets edited
    public required XElement Node { get; init; }      // the <WindowTemplate> element
    public bool Dirty { get; set; }
}

/// <summary>Font metadata. Equivalent to fonts{} of the HTML version.</summary>
public sealed class FontRef
{
    public required string File { get; init; }
    /// <summary>&lt;TTFFont&gt;&lt;Height&gt;: the font size in pixels. The line advance is Height x LineHeight (see TtfMetrics).</summary>
    public double Height { get; set; }
    public bool Bold { get; init; }
    /// <summary>Line height in em, taken from the TTF metrics (see TtfMetrics).</summary>
    public double LineHeight { get; set; }
}
