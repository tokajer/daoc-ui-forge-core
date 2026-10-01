using System.Xml.Linq;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

public enum FieldKind
{
    String,

    /// <summary>Display text: keeps leading spaces (see <see cref="Xml.TxDisplay"/>).</summary>
    Text,

    Int,
    Bool,

    /// <summary>R/G/B/A under one tag.</summary>
    Color,

    /// <summary>X/Y under one tag.</summary>
    Point,

    Template,
    Adapter,
    Event,
    Font,
    Texture,

    /// <summary>The seven flags of <see cref="ControlSchema.AlignmentFlags"/>.</summary>
    Alignment,
}

public readonly record struct ControlField(string Tag, string Label, FieldKind Kind);

/// <summary>
/// Which properties each control type has, taken from DAoCEd 1.80's
/// daoc.editor.model.nodes.*defNode classes: loadChildren names the tags,
/// getEditors names the field kind. Order is DAoCEd's, not alphabetical.
///
/// <para>Not a validator: a file may carry tags this does not list, and nothing
/// here removes one.</para>
/// </summary>
public static class ControlSchema
{
    private static ControlField F(string tag, string label, FieldKind kind) => new(tag, label, kind);

    private static ControlField Str(string tag, string label) => F(tag, label, FieldKind.String);

    private static ControlField Txt(string tag, string label) => F(tag, label, FieldKind.Text);

    private static ControlField Int(string tag, string label) => F(tag, label, FieldKind.Int);

    private static ControlField Yes(string tag, string label) => F(tag, label, FieldKind.Bool);

    private static ControlField Tpl(string tag, string label) => F(tag, label, FieldKind.Template);

    private static ControlField Ada(string tag, string label) => F(tag, label, FieldKind.Adapter);

    private static ControlField Evt(string tag, string label) => F(tag, label, FieldKind.Event);

    private static ControlField Pt(string tag, string label) => F(tag, label, FieldKind.Point);

    private static ControlField Aln(string tag, string label) => F(tag, label, FieldKind.Alignment);

    /// <summary>DAoCEd's ControlNode: what every control type carries.</summary>
    public static readonly ControlField[] Common =
    {
        Str("ControlId", T("Control id")),
        Pt("Position", "Position"),
        Aln("Alignment", "Alignment"),
        Str("ToolTipID", T("Tooltip id")),
    };

    /// <summary>The flags inside an alignment block, in DAoCEd's AlignEditor order.</summary>
    public static readonly (string Tag, string Label)[] AlignmentFlags =
    {
        ("TopLeft", T("Top left")),
        ("OffsetRight", T("Offset right")),
        ("OffsetBottom", T("Offset bottom")),
        ("GrowWidth", T("Grow width")),
        ("GrowHeight", T("Grow height")),
        ("CenterHorizontally", T("Center horizontally")),
        ("CenterVertically", T("Center vertically")),
    };

    private static readonly Dictionary<string, ControlField[]> ByType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ButtonDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Txt("Label", "Label"),
                Evt("OnClickEvent", T("On click")),
                Evt("OnShiftClickEvent", T("On shift-click")),
                Ada("ColorAdapter", T("Color adapter")),
                Ada("Adapter", "Adapter"),
                Aln("LabelAlignment", T("Text alignment")),
            },

            ["ChatControlDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("LinePadding", T("Line padding")),
                Str("BufferName", T("Buffer name")),
                Pt("TextOffset", T("Text offset")),
                Int("Width", "Width"),
                Int("Height", "Height"),
            },

            ["CheckBoxDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Ada("TextAdapterName", T("Text adapter")),
                Txt("Data", "Text"),
                Int("LabelWidth", T("Label width")),
                Evt("OnClickEvent", T("On click")),
            },

            ["ClickableEditBoxDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Int("MaxCharacters", T("Max characters")),
            },

            ["ComboBoxDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Evt("OnChangeEvent", T("On change")),
                Ada("AdapterName", "Adapter"),
            },

            ["CompassControlDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Int("Width", "Width"),
                Int("Height", "Height"),
            },

            ["DockableIconDef"] = new[]
            {
                Tpl("IconTemplateName", T("Icon template")),
                Tpl("TemplateName", "Template"),
            },

            ["DynamicImageDef"] = new[]
            {
                F("TextureName", "Texture", FieldKind.Texture),
                Pt("Dimensions", "Size"),
                Pt("TextureCoords", T("Texture coords")),
            },

            ["EditBoxDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Int("MaxCharacters", T("Max characters")),
            },

            ["FullResizeImageDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Width", "Width"),
                Int("Height", "Height"),
            },

            ["HorizontalResizeButtonDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Width", "Width"),
                Evt("OnClickEvent", T("On click")),
                Txt("Label", "Label"),
                Int("LabelIndent", T("Label indent")),
                Aln("LabelAlignment", T("Text alignment")),
            },

            ["HorizontalResizeImageButtonDef"] = new[]
            {
                Tpl("ImageAreaTemplateName", T("Image template")),
                Tpl("HRButtonTemplateName", T("Button template")),
                Int("Width", "Width"),
                Pt("ImageOffset", T("Image offset")),
                Txt("Label", "Label"),
                Int("LabelIndent", T("Label indent")),
                Evt("OnClickEvent", T("On click")),
                Aln("LabelAlignment", T("Text alignment")),
            },

            ["HorizontalResizeImageDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Width", "Width"),
            },

            ["HorizontalSliderDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Width", "Width"),
                Int("NumTicks", "Ticks"),
                Evt("OnClickEvent", T("On click")),
                Ada("AdapterName", "Adapter"),
            },

            ["IconDef"] = new[]
            {
                Int("IconId", T("Icon id")),
                Txt("Data", "Text"),
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Evt("OnClickEvent", T("On click")),
                Ada("StateAdapterName", T("State adapter")),
                Ada("LabelAdapterName", T("Label adapter")),
                Yes("HideLabel", T("Hide label")),
            },

            ["IconSetDef"] = new[]
            {
                Int("Rows", "Rows"),
                Int("Columns", "Columns"),
                Ada("BaseAdapterName", T("Base adapter")),
                Tpl("TemplateName", "Template"),
            },

            ["ImageAreaDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
            },

            ["InvisibleButtonDef"] = new[]
            {
                Txt("Label", "Label"),
                Evt("OnClickEvent", T("On click")),
                Int("Width", "Width"),
                Int("Height", "Height"),
            },

            ["LabelDef"] = new[]
            {
                F("Color", "Color", FieldKind.Color),
                F("FontName", "Font", FieldKind.Font),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Int("MaxCharacters", T("Max characters")),
                Txt("Data", "Text"),
                Ada("Adapter", "Adapter"),
                Ada("ColorAdapter", T("Color adapter")),
                Yes("TextCentered", T("Text centered")),
                Yes("EndAligned", T("End aligned")),
            },

            ["ListBoxDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Yes("Sorting", "Sorting"),
                Int("Columns", "Columns"),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Yes("ShowDefaultToolTips", T("Default tooltips")),
                Yes("ShowIndicator", T("Show indicator")),
                Tpl("IconTemplate", T("Icon template")),
                Evt("OnClickEvent", T("On click")),
                Evt("OnHeaderClickEvent", T("On header click")),
            },

            ["ScalarLabelDef"] = new[]
            {
                F("FontName", "Font", FieldKind.Font),
                Ada("Adapter", "Adapter"),
                Ada("ColorAdapter", T("Color adapter")),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Int("MaxCharacters", T("Max characters")),
                F("Color", "Color", FieldKind.Color),
                Txt("Data", "Text"),
                Yes("TextCentered", T("Text centered")),
                Yes("DontDrawWhenZero", T("Hide when zero")),
                Yes("DontDrawWhenNegOne", T("Hide when -1")),
                Yes("EndAligned", T("End aligned")),
            },

            ["StaticFileImageDef"] = new[]
            {
                Str("CanvasName", T("Canvas name")),
            },

            ["StatusBarDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Int("Width", "Width"),
            },

            ["StatusIconDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
            },

            ["TabsDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Evt("OnClickEvent", T("On click")),
                Int("Width", "Width"),
                Int("Height", "Height"),
            },

            ["TextAreaDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Yes("HasHotSpots", T("Has hot spots")),
                Int("MaxCharacters", T("Max characters")),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Str("HotspotDelineator", T("Hotspot delineator")),
            },

            ["TreeControlDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Int("Width", "Width"),
                Int("Height", "Height"),
                Int("Column1Width", T("Column 1 width")),
                Int("Column2Width", T("Column 2 width")),
                Int("Column1Offset", T("Column 1 offset")),
                Int("Column2Offset", T("Column 2 offset")),
            },

            ["VerticalResizeImageDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Height", "Height"),
            },

            ["VerticalSliderDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Int("Height", "Height"),
                Int("NumTicks", "Ticks"),
                Evt("OnClickEvent", T("On click")),
                Ada("AdapterName", "Adapter"),
            },

            ["VerticalStatusbarDef"] = new[]
            {
                Tpl("TemplateName", "Template"),
                Ada("AdapterName", "Adapter"),
                Int("Height", "Height"),
                Yes("DontDrawWhenZero", T("Hide when zero")),
            },
        };

    /// <summary>Readable names, from DAoCEd's ControlTypes table.</summary>
    private static readonly Dictionary<string, string> Names =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ButtonDef"] = "Button",
            ["ChatControlDef"] = T("Chat area"),
            ["CheckBoxDef"] = "Checkbox",
            ["ClickableEditBoxDef"] = T("Clickable edit box"),
            ["ComboBoxDef"] = "Combobox",
            ["CompassControlDef"] = "Compass",
            ["DockableIconDef"] = T("Dockable icon"),
            ["DynamicImageDef"] = T("Dynamic image"),
            ["EditBoxDef"] = "Editbox",
            ["FullResizeImageDef"] = T("Full resize image"),
            ["HorizontalResizeButtonDef"] = T("Horizontal resize button"),
            ["HorizontalResizeImageButtonDef"] = T("Horizontal resize image button"),
            ["HorizontalResizeImageDef"] = T("Horizontal resize image"),
            ["HorizontalSliderDef"] = T("Horizontal slider"),
            ["IconDef"] = "Icon",
            ["IconSetDef"] = T("Icon set"),
            ["ImageAreaDef"] = "Image",
            ["InvisibleButtonDef"] = T("Invisible button"),
            ["LabelDef"] = "Label",
            ["ListBoxDef"] = "Listbox",
            ["ScalarLabelDef"] = T("Scalar label"),
            ["StaticFileImageDef"] = T("Static file image"),
            ["StatusBarDef"] = "Statusbar",
            ["StatusIconDef"] = T("Status icon"),
            ["TabsDef"] = "Tabs",
            ["TextAreaDef"] = "Textarea",
            ["TreeControlDef"] = "Tree",
            ["VerticalResizeImageDef"] = T("Vertical resize image"),
            ["VerticalSliderDef"] = T("Vertical slider"),
            ["VerticalStatusbarDef"] = T("Vertical statusbar"),
        };

    public static IReadOnlyList<(string Tag, string Name)> Types =>
        Names.Select(p => (p.Key, p.Value)).OrderBy(p => p.Value, StringComparer.Ordinal).ToList();

    public static string NameOf(string tag) =>
        Names.TryGetValue(tag, out string? name) ? name : Short(tag);

    public static string Short(string tag) =>
        tag.EndsWith("Def", StringComparison.OrdinalIgnoreCase) ? tag[..^3] : tag;

    /// <summary>Empty for a type the table does not know; it stays editable through the raw XML.</summary>
    public static IReadOnlyList<ControlField> Fields(string tag) =>
        ByType.TryGetValue(tag, out var fields) ? fields : Array.Empty<ControlField>();

    public static IReadOnlyList<ControlField> Fields(XElement def) => Fields(def.Name.LocalName);

    public static bool Knows(string tag) => ByType.ContainsKey(tag);

    public static bool Knows(XElement def) => Knows(def.Name.LocalName);

    /// <summary>Only the literal "true" counts, as in ElementRenderer.Flag.</summary>
    public static bool Flag(XElement? def, string tag, string flag) =>
        Xml.Tx(Xml.Sub(def, tag), flag) == "true";

    /// <summary>
    /// Clearing removes the flag instead of writing false, and drops the block
    /// with the last one: that is how the packages carry it.
    /// </summary>
    public static void SetFlag(XElement def, string tag, string flag, bool on)
    {
        if (on)
        {
            XmlEdit.SetSub(def, "true", tag, flag);
            return;
        }

        var block = Xml.Sub(def, tag);
        if (block is null) return;

        XmlEdit.RemoveSub(block, flag);
        if (!block.HasElements) XmlEdit.RemoveWithIndent(block);
    }
}
