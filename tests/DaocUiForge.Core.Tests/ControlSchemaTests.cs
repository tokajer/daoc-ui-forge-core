using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The per-type property table taken from DAoCEd, plus creating controls and
/// comments with it.
/// </summary>
public class ControlSchemaTests
{
    private static XElement Window() => XDocument.Parse("""
        <Root>
            <WindowTemplate>
                <Name>w</Name>
                <LabelDef>
                    <Data>A</Data>
                </LabelDef>
            </WindowTemplate>
        </Root>
        """, LoadOptions.PreserveWhitespace).Root!.Element("WindowTemplate")!;

    // -----------------------------------------------------------------
    // The table
    // -----------------------------------------------------------------

    [Fact]
    public void Every_type_the_renderer_draws_has_a_schema()
    {
        // Every type ElementRenderer dispatches on. StaticFileImageDef joined
        // them with the run-time stand-in; before that it was the one type the
        // schema carried and the drawing layer did not know.
        foreach (string type in new[]
                 {
                     "StaticFileImageDef",
                     "FullResizeImageDef", "HorizontalResizeImageDef",
                     "HorizontalResizeImageButtonDef", "HorizontalResizeButtonDef",
                     "VerticalResizeImageDef", "ImageAreaDef", "DynamicImageDef",
                     "ButtonDef", "InvisibleButtonDef", "CheckBoxDef", "StatusBarDef",
                     "VerticalStatusbarDef", "StatusIconDef", "IconDef", "DockableIconDef",
                     "ScalarLabelDef", "LabelDef", "ListBoxDef", "TreeControlDef",
                     "TextAreaDef", "ChatControlDef", "ComboBoxDef", "HorizontalSliderDef",
                     "VerticalSliderDef", "CompassControlDef", "TabsDef", "IconSetDef",
                     "EditBoxDef", "ClickableEditBoxDef",
                 })
        {
            Assert.True(ControlSchema.Knows(type), type);
            Assert.NotEmpty(ControlSchema.Fields(type));
        }
    }

    [Fact]
    public void Lookup_is_case_insensitive_like_the_rest_of_the_format()
    {
        Assert.True(ControlSchema.Knows("labeldef"));
        Assert.Equal("Label", ControlSchema.NameOf("LABELDEF"));
    }

    [Fact]
    public void An_unknown_type_has_no_fields_rather_than_throwing()
    {
        Assert.False(ControlSchema.Knows("SomethingDef"));
        Assert.Empty(ControlSchema.Fields("SomethingDef"));
        Assert.Equal("Something", ControlSchema.NameOf("SomethingDef"));
    }

    [Fact]
    public void Listbox_carries_the_fields_the_generic_editor_had_no_place_for()
    {
        var tags = ControlSchema.Fields("ListBoxDef").Select(f => f.Tag).ToList();

        Assert.Contains("Sorting", tags);
        Assert.Contains("Columns", tags);
        Assert.Contains("OnHeaderClickEvent", tags);
        Assert.Contains("ShowIndicator", tags);
    }

    [Fact]
    public void Field_kinds_match_the_tag_they_stand_for()
    {
        var label = ControlSchema.Fields("LabelDef").ToDictionary(f => f.Tag, f => f.Kind);

        Assert.Equal(FieldKind.Color, label["Color"]);
        Assert.Equal(FieldKind.Font, label["FontName"]);
        Assert.Equal(FieldKind.Int, label["Width"]);
        Assert.Equal(FieldKind.Bool, label["TextCentered"]);
        Assert.Equal(FieldKind.Text, label["Data"]);
        Assert.Equal(FieldKind.Adapter, label["Adapter"]);
    }

    // -----------------------------------------------------------------
    // Alignment flags
    // -----------------------------------------------------------------

    [Fact]
    public void Only_the_literal_true_counts_as_a_set_flag()
    {
        var def = XElement.Parse("""
            <LabelDef><Alignment><GrowWidth>true</GrowWidth><TopLeft>1</TopLeft></Alignment></LabelDef>
            """);

        Assert.True(ControlSchema.Flag(def, "Alignment", "GrowWidth"));
        Assert.False(ControlSchema.Flag(def, "Alignment", "TopLeft"));
        Assert.False(ControlSchema.Flag(def, "Alignment", "GrowHeight"));
    }

    [Fact]
    public void Clearing_the_last_flag_takes_the_block_with_it()
    {
        var def = XElement.Parse("<LabelDef />");

        ControlSchema.SetFlag(def, "Alignment", "GrowWidth", true);
        Assert.True(ControlSchema.Flag(def, "Alignment", "GrowWidth"));

        ControlSchema.SetFlag(def, "Alignment", "TopLeft", true);
        ControlSchema.SetFlag(def, "Alignment", "GrowWidth", false);
        Assert.NotNull(Xml.Sub(def, "Alignment"));

        ControlSchema.SetFlag(def, "Alignment", "TopLeft", false);
        Assert.Null(Xml.Sub(def, "Alignment"));
    }

    [Fact]
    public void Clearing_a_flag_writes_no_false_into_the_file()
    {
        var def = XElement.Parse("<LabelDef><Alignment><TopLeft>true</TopLeft></Alignment></LabelDef>");

        ControlSchema.SetFlag(def, "Alignment", "TopLeft", false);
        Assert.DoesNotContain("false", def.ToString());
    }

    // -----------------------------------------------------------------
    // Creating a control
    // -----------------------------------------------------------------

    [Fact]
    public void A_created_control_carries_the_tags_of_its_type()
    {
        var win = Window();
        var def = ElementEditor.Create(win, "StatusBarDef");

        Assert.Equal("StatusBarDef", def.Name.LocalName);
        Assert.NotNull(Xml.Sub(def, "ControlId"));
        Assert.Equal("0", Xml.Tx(Xml.Sub(def, "Position"), "X"));

        foreach (var field in ControlSchema.Fields("StatusBarDef"))
            Assert.NotNull(Xml.Sub(def, field.Tag));
    }

    [Fact]
    public void Numbers_start_at_zero_and_flags_at_false()
    {
        var def = ElementEditor.Create(Window(), "LabelDef");

        Assert.Equal("0", Xml.Tx(def, "Width"));
        Assert.Equal("false", Xml.Tx(def, "TextCentered"));
        Assert.Equal("255", Xml.Tx(Xml.Sub(def, "Color"), "A"));
        Assert.Equal("", Xml.Tx(def, "Data"));
    }

    [Fact]
    public void No_empty_alignment_block_is_created()
    {
        var def = ElementEditor.Create(Window(), "ButtonDef");

        Assert.Null(Xml.Sub(def, "Alignment"));
        Assert.Null(Xml.Sub(def, "LabelAlignment"));
    }

    [Fact]
    public void A_created_control_is_indented_like_the_file_it_joins()
    {
        var win = Window();
        var def = ElementEditor.Create(win, "LabelDef");

        // Not one long line. The new element sits where
        // its neighbour does, its fields one step further in.
        Assert.Equal("\n        ", XmlEdit.IndentOf(def)!.Value);
        Assert.Contains("\n            <Width>", def.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void A_control_lands_behind_the_one_it_was_added_from()
    {
        var win = Window();
        var first = ElementEditor.ElementsOf(win).First();

        var def = ElementEditor.Create(win, "ButtonDef", first);

        Assert.Equal(new[] { "LabelDef", "ButtonDef" },
            ElementEditor.ElementsOf(win).Select(e => e.Name.LocalName));
        Assert.Same(def, ElementEditor.ElementsOf(win).Last());
    }

    [Fact]
    public void An_anchor_from_another_window_is_ignored_rather_than_written_to()
    {
        var win = Window();
        var stranger = ElementEditor.ElementsOf(Window()).First();

        ElementEditor.Create(win, "ButtonDef", stranger);

        Assert.Equal(2, ElementEditor.ElementsOf(win).Count());
        Assert.Single(ElementEditor.ElementsOf(stranger.Parent!));
    }

    // -----------------------------------------------------------------
    // Comments
    // -----------------------------------------------------------------

    [Fact]
    public void Comments_are_listed_between_the_elements()
    {
        var win = Window();
        var first = ElementEditor.ElementsOf(win).First();

        ElementEditor.CreateComment(win, "Group", first);

        var nodes = ElementEditor.NodesOf(win).ToList();
        Assert.Equal(2, nodes.Count);
        Assert.IsType<XElement>(nodes[0]);
        Assert.IsType<XComment>(nodes[1]);

        // The element list stays what it was.
        Assert.Single(ElementEditor.ElementsOf(win));
    }

    [Fact]
    public void A_comment_is_padded_the_way_the_packages_write_them()
    {
        var comment = ElementEditor.CreateComment(Window(), "Group");

        Assert.Equal(" Group ", comment.Value);
        Assert.Equal("Group", ElementEditor.CommentText(comment));
    }

    [Fact]
    public void A_double_hyphen_would_be_invalid_xml_and_is_dropped()
    {
        var comment = ElementEditor.CreateComment(Window(), "a -- b");

        Assert.Equal("a - b", ElementEditor.CommentText(comment));
        Assert.Contains("<!-- a - b -->", comment.Parent!.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void Editing_a_comment_keeps_the_padding()
    {
        var comment = ElementEditor.CreateComment(Window(), "Group");

        ElementEditor.SetComment(comment, "  Other  ");

        Assert.Equal(" Other ", comment.Value);
    }
}
