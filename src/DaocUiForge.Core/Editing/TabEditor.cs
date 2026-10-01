using System.Globalization;
using System.Xml.Linq;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// The nested blocks of a &lt;TabsDef&gt;: the tabs themselves
/// (&lt;Tab&gt;&lt;Id&gt;&lt;Name&gt;) and the assignments that put an element
/// on one of them (&lt;TabControl&gt;&lt;TabId&gt;&lt;ControlId&gt;).
/// DAoCEd edits both through its TabEditor; <see cref="ControlSchema"/> covers
/// leaf tags only and therefore never reached them.
///
/// <para>The tag names and the exact matching are the renderer's
/// (<see cref="WindowRenderer"/> reads the same blocks), so what the editor
/// lists is what the drawing pass acts on.</para>
/// </summary>
public static class TabEditor
{
    public const string TypeTag = "TabsDef";

    public static bool IsTabs(XElement def) => def.Name.LocalName == TypeTag;

    /// <summary>The &lt;Tab&gt; blocks in document order, which is the order the
    /// tab row is drawn in.</summary>
    public static IReadOnlyList<XElement> Tabs(XElement def) =>
        def.Elements().Where(e => e.Name.LocalName == "Tab").ToList();

    /// <summary>The &lt;TabControl&gt; blocks: which element shows on which tab.</summary>
    public static IReadOnlyList<XElement> Members(XElement def) =>
        def.Elements().Where(e => e.Name.LocalName == "TabControl").ToList();

    public static string IdOf(XElement tab) => Xml.Tx(tab, "Id");

    public static string NameOf(XElement tab) => Xml.Tx(tab, "Name");

    public static string TabIdOf(XElement member) => Xml.Tx(member, "TabId");

    public static string ControlIdOf(XElement member) => Xml.Tx(member, "ControlId");

    /// <summary>
    /// One past the highest id in use, or 1 for the first tab.
    ///
    /// <para>DAoCEd counts instead (<c>getSize() + 1</c>), which hands out an id
    /// that is already taken as soon as a tab in the middle has been deleted.
    /// Two tabs on one id make every &lt;TabControl&gt; between them ambiguous.</para>
    /// </summary>
    public static string NextId(XElement def)
    {
        int highest = 0;
        foreach (var tab in Tabs(def))
            if (int.TryParse(IdOf(tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                highest = Math.Max(highest, id);

        return (highest + 1).ToString(CultureInfo.InvariantCulture);
    }

    public static XElement AddTab(XElement def, string? name = null)
    {
        string id = NextId(def);
        var ns = def.Name.Namespace;

        var fresh = new XElement(ns + "Tab",
            new XElement(ns + "Id", id),
            new XElement(ns + "Name", string.IsNullOrWhiteSpace(name) ? T("New tab") : name.Trim()));

        // Behind the last tab, so the new one is drawn rightmost. Without tabs
        // yet, behind the last leaf tag rather than behind a <TabControl>: the
        // packages keep the tabs in front of the assignments.
        var after = Tabs(def).LastOrDefault()
                    ?? def.Elements().LastOrDefault(e => e.Name.LocalName != "TabControl");

        return Place(def, fresh, after);
    }

    public static XElement AddMember(XElement def, string tabId, string controlId)
    {
        var ns = def.Name.Namespace;

        var fresh = new XElement(ns + "TabControl",
            new XElement(ns + "TabId", tabId),
            new XElement(ns + "ControlId", controlId));

        return Place(def, fresh, Members(def).LastOrDefault() ?? Tabs(def).LastOrDefault());
    }

    public static void SetName(XElement tab, string name) => XmlEdit.SetSub(tab, name, "Name");

    public static void SetMember(XElement member, string tabId, string controlId)
    {
        XmlEdit.SetSub(member, tabId, "TabId");
        XmlEdit.SetSub(member, controlId, "ControlId");
    }

    /// <summary>
    /// Renumber a tab, and move its assignments with it.
    ///
    /// <para><b>Deviation from DAoCEd</b>, which writes the two fields of its
    /// list entry back and leaves &lt;TabControl&gt; alone. An assignment
    /// pointing at an id no longer in the list does not fall back to "always
    /// visible": <see cref="WindowRenderer"/> hides an element whose tab ids
    /// exclude the active tab, and no tab can ever be the missing one. So the
    /// controls of the renamed tab disappear from the window with nothing on
    /// screen saying why.</para>
    /// </summary>
    public static void SetId(XElement def, XElement tab, string id)
    {
        string old = IdOf(tab);
        XmlEdit.SetSub(tab, id, "Id");

        if (old.Length == 0 || old == id) return;

        foreach (var member in Members(def))
            if (TabIdOf(member) == old) XmlEdit.SetSub(member, id, "TabId");
    }

    /// <summary>
    /// Remove a tab and the assignments that named it.
    ///
    /// <para>Same reasoning as <see cref="SetId"/>: an orphaned
    /// &lt;TabControl&gt; hides its element for good. The elements themselves
    /// are untouched, they simply become visible on every tab again.</para>
    /// </summary>
    public static void RemoveTab(XElement def, XElement tab)
    {
        string id = IdOf(tab);
        XmlEdit.RemoveWithIndent(tab);

        if (id.Length == 0) return;

        foreach (var member in Members(def))
            if (TabIdOf(member) == id) XmlEdit.RemoveWithIndent(member);
    }

    public static void RemoveMember(XElement member) => XmlEdit.RemoveWithIndent(member);

    /// <summary>
    /// Put an assignment on a different tab.
    ///
    /// <para>DAoCEd has no such action: moving an element from one tab to
    /// another there is a delete on one list and an add on the other, and the
    /// control id has to be typed again in between. Two steps, and the first one
    /// on its own leaves the element visible everywhere — so an interruption
    /// between them is a silent change to the window.</para>
    ///
    /// <para><b>The element does not move with it.</b> A &lt;TabControl&gt; says
    /// which tab shows which control id; the control itself sits in the window
    /// and is not owned by a tab. Nothing else has to follow.</para>
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public static bool MoveMember(XElement member, string tabId)
    {
        string now = TabIdOf(member);
        if (now == tabId) return false;

        XmlEdit.SetSub(member, tabId, "TabId");
        return true;
    }

    /// <summary>
    /// Whether an assignment for this control id is already on that tab. Two
    /// blocks naming one control on one tab are not an error the game notices,
    /// but they are noise in the file and a row that cannot be told from its
    /// twin in the editor.
    /// </summary>
    public static bool AlreadyOn(XElement def, string tabId, string controlId) =>
        Members(def).Any(m => TabIdOf(m) == tabId && ControlIdOf(m) == controlId);

    private static XElement Place(XElement def, XElement fresh, XElement? after) =>
        after is null ? XmlEdit.Append(def, fresh) : XmlEdit.InsertFreshAfter(after, fresh);
}
