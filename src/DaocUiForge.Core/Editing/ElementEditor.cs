using System.Xml.Linq;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// The structural operations of the property editor: clipboard, drawing order,
/// duplicating, deleting, visibility. Ported from the toolbar of
/// <c>renderInspector</c> in the HTML original (lines 2329–2392), which in turn
/// follows DAoCEd.
///
/// <para>All of it is plain XML surgery on <see cref="WindowDef.Doc"/> and
/// carries no UI — which is what lets these rules be tested headless. The app
/// only redraws afterwards.</para>
///
/// <para><b>Drawing order is document order.</b> An element drawn later covers
/// the ones before it, so "forward" means "further towards the end of the
/// parent". That is also the order <see cref="WindowRenderer"/> walks in and
/// the order the picking searches in reverse.</para>
/// </summary>
public static class ElementEditor
{
    /// <summary>
    /// Is this node an element definition? The original tests
    /// <c>/Def$/.test(tagName)</c> — everything ending in "Def" counts, which
    /// is what keeps &lt;Width&gt; and its like out of the reordering.
    /// </summary>
    public static bool IsElement(XElement node) =>
        node.Name.LocalName.EndsWith("Def", StringComparison.Ordinal);

    /// <summary>The element definitions among a window's children, in drawing order.</summary>
    public static IEnumerable<XElement> ElementsOf(XElement parent) =>
        parent.Elements().Where(IsElement);

    /// <summary>
    /// Element definitions and the comments between them, in document order:
    /// what DAoCEd's Controls list shows. Comments are nothing to the game but
    /// structure to whoever edits the file.
    /// </summary>
    public static IEnumerable<XNode> NodesOf(XElement parent) =>
        parent.Nodes().Where(n => n is XComment || (n is XElement e && IsElement(e)));

    /// <summary>
    /// Element types that carry text and a font, and therefore get the
    /// "Text &amp; font" group in the editor. The list is the original's
    /// (line 2421).
    /// </summary>
    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal)
    {
        "LabelDef", "ScalarLabelDef", "ButtonDef", "HorizontalResizeButtonDef",
        "HorizontalResizeImageButtonDef", "CheckBoxDef", "TextAreaDef",
    };

    /// <summary>
    /// Does this element get the text and font fields?
    ///
    /// <para>Widened against the original by the second condition: an element
    /// that already carries a text or font tag gets the group whatever its
    /// type. The original's fixed list would otherwise make an existing value
    /// un-editable and therefore invisible — and the format is looser than
    /// that list (<c>EditBoxDef</c> and <c>ComboBoxDef</c> take a font too).
    /// The clause can only ever add fields, never remove them.</para>
    /// </summary>
    public static bool HasText(XElement def) =>
        TextTypes.Contains(def.Name.LocalName)
        || Xml.Sub(def, "Data") is not null
        || Xml.Sub(def, "Label") is not null
        || Xml.Sub(def, "Text") is not null
        || Xml.Sub(def, "FontName") is not null;

    // -----------------------------------------------------------------
    // Clipboard
    // -----------------------------------------------------------------

    /// <summary>
    /// A detached deep copy, for the clipboard. Detached on purpose: the
    /// original clones as well (<c>cloneNode(true)</c>) rather than holding on
    /// to the live node, so cutting and then pasting still works.
    /// </summary>
    public static XElement Copy(XElement def) => new(def);

    /// <summary>Copy to the clipboard and remove. Returns the copy.</summary>
    public static XElement Cut(XElement def)
    {
        var clone = Copy(def);
        XmlEdit.RemoveWithIndent(def);
        return clone;
    }

    /// <summary>
    /// Insert a clipboard element after <paramref name="target"/>, shifted so
    /// it does not hide under the element it came from. Returns the inserted
    /// node — a copy, so the same clipboard entry can be pasted again.
    /// </summary>
    public static XElement Paste(XElement target, XElement clipboard, double offset = 8)
    {
        var n = new XElement(clipboard);
        Nudge(n, offset, offset);
        XmlEdit.InsertAfter(target, n);
        return n;
    }

    /// <summary>A copy right next to the original. Returns the new node.</summary>
    public static XElement Duplicate(XElement def, double offset = 10)
    {
        var n = new XElement(def);
        Nudge(n, offset, offset);
        XmlEdit.InsertAfter(def, n);
        return n;
    }

    // -----------------------------------------------------------------
    // Position
    // -----------------------------------------------------------------

    /// <summary>Move by a delta. A missing &lt;Position&gt; counts as (0,0).</summary>
    public static void Nudge(XElement def, double dx, double dy)
    {
        var p = Xml.Sub(def, "Position");
        double x = RenderMath.Num(Xml.Tx(p, "X"));
        double y = RenderMath.Num(Xml.Tx(p, "Y"));
        XmlEdit.SetSub(def, x + dx, "Position", "X");
        XmlEdit.SetSub(def, y + dy, "Position", "Y");
    }

    /// <summary>Put the element back at the window's origin.</summary>
    public static void ResetPosition(XElement def)
    {
        XmlEdit.SetSub(def, 0d, "Position", "X");
        XmlEdit.SetSub(def, 0d, "Position", "Y");
    }

    // -----------------------------------------------------------------
    // Drawing order
    // -----------------------------------------------------------------

    /// <summary>
    /// One place later, so it is drawn over its neighbour. False when it is
    /// already last, or when the next sibling is not an element definition.
    /// </summary>
    public static bool MoveForward(XElement def)
    {
        var next = def.ElementsAfterSelf().FirstOrDefault();
        if (next is null || !IsElement(next)) return false;
        XmlEdit.MoveAfter(def, next);
        return true;
    }

    /// <summary>One place earlier, so its neighbour is drawn over it.</summary>
    public static bool MoveBackward(XElement def)
    {
        var prev = def.ElementsBeforeSelf().LastOrDefault();
        if (prev is null || !IsElement(prev)) return false;
        XmlEdit.MoveBefore(def, prev);
        return true;
    }

    /// <summary>Drawn last of all — on top of everything.</summary>
    public static bool MoveToFront(XElement def)
    {
        var parent = def.Parent;
        if (parent is null) return false;

        var last = parent.Elements().LastOrDefault();
        if (last is null || ReferenceEquals(last, def)) return false;

        XmlEdit.MoveAfter(def, last);
        return true;
    }

    /// <summary>Drawn first of all — underneath everything.</summary>
    public static bool MoveToBack(XElement def)
    {
        var parent = def.Parent;
        if (parent is null) return false;

        var first = ElementsOf(parent).FirstOrDefault();
        if (first is null || ReferenceEquals(first, def)) return false;

        XmlEdit.MoveBefore(def, first);
        return true;
    }

    // -----------------------------------------------------------------
    // Visibility and removal
    // -----------------------------------------------------------------

    /// <summary>
    /// Is the element drawn? Only the literal <c>false</c> hides it — the same
    /// test <see cref="WindowRenderer"/> makes, so the editor and the drawing
    /// cannot disagree.
    /// </summary>
    public static bool IsVisible(XElement def) => Xml.Tx(def, "Visible") != "false";

    /// <summary>Flip &lt;Visible&gt; and return the new state.</summary>
    public static bool ToggleVisible(XElement def)
    {
        bool now = !IsVisible(def);
        XmlEdit.SetSub(def, now ? "true" : "false", "Visible");
        return now;
    }

    /// <summary>Remove the element from its window, its indentation with it.</summary>
    public static void Delete(XElement def) => XmlEdit.RemoveWithIndent(def);

    // -----------------------------------------------------------------
    // Creating
    // -----------------------------------------------------------------

    /// <summary>
    /// A new control of this type, with the tags of <see cref="ControlSchema"/>
    /// written out empty, as the packages and DAoCEd carry them. Sizes start at
    /// 0, so it is invisible in the preview until given one, and reachable in
    /// the element list from the moment it exists.
    /// </summary>
    /// <param name="after">Insert behind this node; null appends.</param>
    public static XElement Create(XElement parent, string type, XNode? after = null)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException(T("A control needs a type."), nameof(type));

        var def = new XElement(parent.Name.Namespace + type.Trim());

        // ControlId and Position first, as every window file has them.
        def.Add(new XElement(def.Name.Namespace + "ControlId"));
        def.Add(new XElement(def.Name.Namespace + "Position",
            new XElement(def.Name.Namespace + "X", "0"),
            new XElement(def.Name.Namespace + "Y", "0")));

        foreach (var field in ControlSchema.Fields(type))
        {
            switch (field.Kind)
            {
                // Alignment blocks appear once a flag is set; the packages
                // carry no empty ones.
                case FieldKind.Alignment:
                    break;

                case FieldKind.Int:
                    def.Add(new XElement(def.Name.Namespace + field.Tag, "0"));
                    break;

                case FieldKind.Bool:
                    def.Add(new XElement(def.Name.Namespace + field.Tag, "false"));
                    break;

                case FieldKind.Color:
                    def.Add(new XElement(def.Name.Namespace + field.Tag,
                        new XElement(def.Name.Namespace + "R", "255"),
                        new XElement(def.Name.Namespace + "G", "255"),
                        new XElement(def.Name.Namespace + "B", "255"),
                        new XElement(def.Name.Namespace + "A", "255")));
                    break;

                case FieldKind.Point:
                    def.Add(new XElement(def.Name.Namespace + field.Tag,
                        new XElement(def.Name.Namespace + "X", "0"),
                        new XElement(def.Name.Namespace + "Y", "0")));
                    break;

                default:
                    def.Add(new XElement(def.Name.Namespace + field.Tag));
                    break;
            }
        }

        return Place(parent, def, after);
    }

    /// <summary>DAoCEd's AddCommentAction.</summary>
    public static XComment CreateComment(XElement parent, string text, XNode? after = null)
    {
        var comment = new XComment(CommentText(text));
        Place(parent, comment, after);
        return comment;
    }

    public static void SetComment(XComment comment, string text) =>
        comment.Value = CommentText(text);

    public static string CommentText(XComment comment) => comment.Value.Trim();

    /// <summary>
    /// Padded with one space either side, as the files write them. "--" is
    /// dropped: XML forbids it inside a comment and <see cref="XComment"/>
    /// throws over it.
    /// </summary>
    private static string CommentText(string text)
    {
        string body = text.Replace("--", "-").Trim();
        return body.Length == 0 ? " " : T(" {0} ", body);
    }

    /// <summary>
    /// Behind an anchor, or at the end of the window. A created element is laid
    /// out to match the file (see <see cref="XmlEdit.InsertFreshAfter"/>).
    /// </summary>
    private static T Place<T>(XElement parent, T fresh, XNode? after) where T : XNode
    {
        // The anchor has to be in this very window: a stale selection would
        // otherwise write into the wrong file.
        if (after is null || !ReferenceEquals(after.Parent, parent))
            after = NodesOf(parent).LastOrDefault();

        if (after is null)
        {
            // Empty window: nothing to take the indentation from.
            if (fresh is XElement first) XmlEdit.Append(parent, first);
            else parent.Add(fresh);
            return fresh;
        }

        if (fresh is XElement el) XmlEdit.InsertFreshAfter(after, el);
        else XmlEdit.InsertAfter(after, fresh);

        return fresh;
    }

    // -----------------------------------------------------------------
    // Raw XML
    // -----------------------------------------------------------------

    /// <summary>
    /// Replace an element with XML typed by hand. Returns the new node.
    /// Throws <see cref="System.Xml.XmlException"/> when the text does not
    /// parse — the caller reports that rather than losing the old element.
    /// </summary>
    public static XElement ReplaceFromXml(XElement def, string xml)
    {
        // Parsed first, replaced second: a failure must leave the tree alone.
        var parsed = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        def.ReplaceWith(parsed);
        return parsed;
    }

    /// <summary>
    /// One element as readable XML, for the raw-XML box. The original inserts
    /// the breaks itself (<c>replace(/&gt;&lt;/g,'&gt;\n&lt;')</c>);
    /// <see cref="XElement.ToString()"/> indents properly, which is the same
    /// idea done better.
    /// </summary>
    public static string ToXml(XElement def) => def.ToString();
}
