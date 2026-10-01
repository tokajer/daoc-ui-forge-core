using System.Xml.Linq;

namespace DaocUiForge.Core.Model;

/// <summary>
/// Small XML access helpers, ported from the functions <c>tx</c>, <c>sub</c>
/// and <c>nameOf</c> of the HTML original. They deliberately encapsulate the
/// format quirks of DAoC's XML.
/// </summary>
public static class Xml
{
    /// <summary>
    /// Text content of a direct child element, case-insensitive on the element
    /// name. Empty string when absent.
    ///
    /// <paramref name="node"/> may be null — as with <c>tx</c> in the original,
    /// where templates that need not exist are accessed all the time.
    /// </summary>
    public static string Tx(XElement? node, string tag)
    {
        if (node is null) return "";
        foreach (var e in node.Elements())
            if (string.Equals(e.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase))
                return (e.Value ?? "").Trim();
        return "";
    }

    /// <summary>
    /// Like <see cref="Tx"/>, but for DISPLAY TEXT
    /// (&lt;Data&gt;, &lt;Label&gt;, &lt;Text&gt;).
    ///
    /// <para><b>Why it exists separately.</b> DAoC packages align text with
    /// leading spaces. <c>custom3_window.xml</c> literally contains
    /// <c>&lt;Data&gt;         %&lt;/Data&gt;</c> — those nine spaces push the
    /// per-cent sign to the right end of its 50 px box, behind the number in
    /// front of it. Trimmed, the sign slides left and it reads "% 87" instead
    /// of "87 %".</para>
    ///
    /// <para><b>Deviation from the original.</b> There <c>tx</c> trims without
    /// exception (line 488), so the HTML version shows the same fault. Since
    /// goal §1 is "pixel-exact as in the game" and the game honours the
    /// spaces, this deliberately deviates.</para>
    ///
    /// <para><b>Why not simply never trim.</b> When the value sits indented on
    /// a line of its own in the XML, the line break and indentation belong to
    /// the formatting of the file, not to the text. Hence: multi-line values
    /// are trimmed, single-line ones are left alone.</para>
    /// </summary>
    public static string TxDisplay(XElement? node, string tag)
    {
        if (node is null) return "";
        foreach (var e in node.Elements())
        {
            if (!string.Equals(e.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase))
                continue;
            string v = e.Value ?? "";
            return v.Contains('\n') || v.Contains('\r') ? v.Trim() : v;
        }
        return "";
    }

    /// <summary>First direct child element with a matching name (case-insensitive).</summary>
    public static XElement? Sub(XElement? node, string tag)
    {
        if (node is null) return null;
        foreach (var e in node.Elements())
            if (string.Equals(e.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase))
                return e;
        return null;
    }

    /// <summary>
    /// Id of a window or a template. Reads &lt;Name&gt; OR the short form
    /// &lt;n&gt; — some packages use the latter (see §3.2). &lt;WindowId&gt; is
    /// NOT the id: it is not unique.
    /// </summary>
    public static string NameOf(XElement? node, string def = "")
    {
        string n = Tx(node, "Name");
        if (n.Length > 0) return n;
        n = Tx(node, "n");
        if (n.Length > 0) return n;
        return def;
    }
}
