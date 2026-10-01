using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Reference;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Everything the drawing layer needs, in one place: package, texture cache,
/// fonts, reference data and the preview switches.
///
/// These were global variables in the original. As an object the state can be
/// tested and kept side by side more than once (preview and image export at
/// the same time, say).
///
/// An instance outlives many drawing passes — the texture cache is not meant
/// to be thrown away on every redraw.
/// </summary>
public sealed class RenderContext : IDisposable
{
    public Package Package { get; }
    public TextureCache Textures { get; }
    public FontProvider Fonts { get; }
    /// <summary>
    /// The sample values the preview draws with. Settable, because test values
    /// (DAoCEd's <c>AdapterEditDialog</c>) replace entries in these tables
    /// between one pass and the next — see <see cref="TestValues"/>. Every
    /// reader asks the context at draw time, so a new table takes effect on the
    /// next redraw and nothing has to be told about it.
    /// </summary>
    public ReferenceData Reference { get; set; }
    public RenderOptions Options { get; }

    /// <summary>
    /// Actual content extent of the window. Elements carrying
    /// &lt;GrowWidth&gt;/&lt;GrowHeight&gt; grow up to here. Set by
    /// <see cref="WindowRenderer"/> once per pass.
    /// </summary>
    internal double GrowW { get; set; }

    internal double GrowH { get; set; }

    /// <summary>
    /// Height of the native &lt;TabName&gt; placeholder band (see
    /// <see cref="Placeholders.NativeTabBar"/>), 0 when the window has none.
    /// Set by <see cref="WindowRenderer"/> once per pass, so the placeholder
    /// can be drawn BEFORE the elements: a window can carry real, declared
    /// content in that same band (see <see cref="WindowId"/>), and it must
    /// win over the placeholder rather than be painted over.
    /// </summary>
    internal double NativeTabBandHeight { get; set; }

    /// <summary>
    /// The window currently being drawn (<c>&lt;Name&gt;</c>/<c>&lt;n&gt;</c>),
    /// empty for a window with none. Set by <see cref="WindowRenderer"/> once
    /// per pass. <see cref="ElementRenderer"/> reads it for the one
    /// window-specific hack DAoCEd itself hardcodes (see the "chat" window's
    /// ControlId 1001-1004 there) — keyed on the id the same way DAoCEd's own
    /// <c>handleChatWindow()</c> is, so it cannot fire for any other window.
    /// </summary>
    internal string WindowId { get; set; } = "";

    /// <summary>
    /// Tabs of the window being drawn; empty when it has none. Filled while
    /// drawing a &lt;TabsDef&gt;. Public because the app builds the tab row
    /// above the preview from it (<c>drawTabBar</c> in the original).
    /// </summary>
    public List<TabInfo> Tabs { get; } = new();

    /// <summary>
    /// Id of the tab currently visible. It is set while drawing — either from
    /// <see cref="RenderOptions.ActiveTab"/> or, when that says nothing, to
    /// the first tab there is.
    /// </summary>
    public string? ActiveTab { get; internal set; }

    public RenderContext(Package package, RenderOptions? options = null, ReferenceData? reference = null)
    {
        Package = package;
        Options = options ?? new RenderOptions();
        Reference = reference ?? ReferenceData.Default;
        Textures = new TextureCache(package);
        Fonts = new FontProvider(package);
    }

    /// <summary>Look up a template (see <see cref="PackageLoader.FindTemplate"/>).</summary>
    public XElement? FindTemplate(string? name, params string[] preferredTags)
    {
        var tpl = PackageLoader.FindTemplate(Package, name, preferredTags);
        if (tpl is null && !string.IsNullOrEmpty(name) && name != "none")
            Package.MissingTemplates.Add(name);
        return tpl;
    }

    public void Dispose()
    {
        Textures.Dispose();
        Fonts.Dispose();
    }
}
