using System.Xml;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The structural operations of the property editor: clipboard, drawing order,
/// duplicating, deleting, visibility. All of them are plain XML surgery, so
/// they run without a UI.
/// </summary>
public class ElementEditorTests
{
    /// <summary>
    /// A window with three labels A, B, C — plus a &lt;Width&gt; in front of
    /// them, because a real window carries plain fields among its element
    /// definitions and the reordering has to step over those.
    /// </summary>
    private static XElement Window() => XElement.Parse("""
        <WindowTemplate>
          <Width>200</Width>
          <LabelDef><Data>A</Data><Position><X>10</X><Y>20</Y></Position></LabelDef>
          <LabelDef><Data>B</Data></LabelDef>
          <LabelDef><Data>C</Data></LabelDef>
        </WindowTemplate>
        """);

    private static XElement Pick(XElement win, string data) =>
        ElementEditor.ElementsOf(win).First(e => Xml.Tx(e, "Data") == data);

    private static string Order(XElement win) =>
        string.Concat(ElementEditor.ElementsOf(win).Select(e => Xml.Tx(e, "Data")));

    // -----------------------------------------------------------------
    // What counts as an element
    // -----------------------------------------------------------------

    [Fact]
    public void Only_tags_ending_in_Def_count_as_elements()
    {
        var win = Window();

        Assert.Equal(3, ElementEditor.ElementsOf(win).Count());
        Assert.False(ElementEditor.IsElement(Xml.Sub(win, "Width")!));
        Assert.True(ElementEditor.IsElement(Pick(win, "A")));
    }

    // -----------------------------------------------------------------
    // Drawing order — document order, later means on top
    // -----------------------------------------------------------------

    [Fact]
    public void MoveForward_swaps_with_the_next_element()
    {
        var win = Window();

        Assert.True(ElementEditor.MoveForward(Pick(win, "A")));

        Assert.Equal("BAC", Order(win));
    }

    [Fact]
    public void MoveForward_stops_at_the_last_element()
    {
        var win = Window();

        Assert.False(ElementEditor.MoveForward(Pick(win, "C")));

        Assert.Equal("ABC", Order(win));
    }

    [Fact]
    public void MoveBackward_swaps_with_the_previous_element()
    {
        var win = Window();

        Assert.True(ElementEditor.MoveBackward(Pick(win, "C")));

        Assert.Equal("ACB", Order(win));
    }

    /// <summary>
    /// The first element has &lt;Width&gt; in front of it, not another
    /// element. Moving it back must do nothing rather than shuffle it past a
    /// field that is none of its business.
    /// </summary>
    [Fact]
    public void MoveBackward_does_not_step_over_a_plain_field()
    {
        var win = Window();

        Assert.False(ElementEditor.MoveBackward(Pick(win, "A")));

        Assert.Equal("ABC", Order(win));
        Assert.Equal("Width", win.Elements().First().Name.LocalName);
    }

    [Fact]
    public void MoveToFront_puts_the_element_last_so_it_is_drawn_on_top()
    {
        var win = Window();

        Assert.True(ElementEditor.MoveToFront(Pick(win, "A")));

        Assert.Equal("BCA", Order(win));
    }

    [Fact]
    public void MoveToBack_puts_the_element_before_the_first_one_but_after_the_fields()
    {
        var win = Window();

        Assert.True(ElementEditor.MoveToBack(Pick(win, "C")));

        Assert.Equal("CAB", Order(win));
        // <Width> stays in front — only the element definitions are reordered.
        Assert.Equal("Width", win.Elements().First().Name.LocalName);
    }

    [Fact]
    public void MoveToBack_on_the_first_element_changes_nothing()
    {
        var win = Window();

        Assert.False(ElementEditor.MoveToBack(Pick(win, "A")));

        Assert.Equal("ABC", Order(win));
    }

    // -----------------------------------------------------------------
    // Clipboard and duplicating
    // -----------------------------------------------------------------

    [Fact]
    public void Copy_detaches_so_the_original_survives_a_later_cut()
    {
        var win = Window();
        var a = Pick(win, "A");

        var clip = ElementEditor.Copy(a);
        ElementEditor.Delete(a);

        Assert.Null(clip.Parent);
        Assert.Equal("A", Xml.Tx(clip, "Data"));
        Assert.Equal("BC", Order(win));
    }

    [Fact]
    public void Cut_removes_the_element_and_hands_back_a_copy()
    {
        var win = Window();

        var clip = ElementEditor.Cut(Pick(win, "B"));

        Assert.Equal("AC", Order(win));
        Assert.Equal("B", Xml.Tx(clip, "Data"));
    }

    [Fact]
    public void Paste_inserts_after_the_target_and_offsets_the_copy()
    {
        var win = Window();
        var clip = ElementEditor.Copy(Pick(win, "A"));

        var pasted = ElementEditor.Paste(Pick(win, "B"), clip);

        Assert.Equal("ABAC", Order(win));
        Assert.Equal("18", Xml.Tx(Xml.Sub(pasted, "Position"), "X"));   // 10 + 8
        Assert.Equal("28", Xml.Tx(Xml.Sub(pasted, "Position"), "Y"));   // 20 + 8
    }

    /// <summary>
    /// Pasting must not consume the clipboard: the original clones on the way
    /// in, so the same entry can be dropped in several times.
    /// </summary>
    [Fact]
    public void Paste_leaves_the_clipboard_usable()
    {
        var win = Window();
        var clip = ElementEditor.Copy(Pick(win, "A"));

        ElementEditor.Paste(Pick(win, "B"), clip);
        ElementEditor.Paste(Pick(win, "C"), clip);

        Assert.Equal("ABACA", Order(win));
        Assert.Null(clip.Parent);
        // Both copies were offset from the source, not from each other.
        Assert.Equal("10", Xml.Tx(Xml.Sub(clip, "Position"), "X"));
    }

    [Fact]
    public void Paste_on_an_element_without_a_position_starts_from_zero()
    {
        var win = Window();
        var clip = ElementEditor.Copy(Pick(win, "B"));   // B has no <Position>

        var pasted = ElementEditor.Paste(Pick(win, "C"), clip);

        Assert.Equal("8", Xml.Tx(Xml.Sub(pasted, "Position"), "X"));
        Assert.Equal("8", Xml.Tx(Xml.Sub(pasted, "Position"), "Y"));
    }

    [Fact]
    public void Duplicate_puts_the_copy_directly_behind_the_original()
    {
        var win = Window();

        var copy = ElementEditor.Duplicate(Pick(win, "A"));

        Assert.Equal("AABC", Order(win));
        Assert.Equal("20", Xml.Tx(Xml.Sub(copy, "Position"), "X"));   // 10 + 10
        Assert.Equal("30", Xml.Tx(Xml.Sub(copy, "Position"), "Y"));
    }

    // -----------------------------------------------------------------
    // Position
    // -----------------------------------------------------------------

    [Fact]
    public void Nudge_moves_by_the_delta_and_creates_a_missing_position()
    {
        var win = Window();
        var b = Pick(win, "B");

        ElementEditor.Nudge(b, -1, 3);

        Assert.Equal("-1", Xml.Tx(Xml.Sub(b, "Position"), "X"));
        Assert.Equal("3", Xml.Tx(Xml.Sub(b, "Position"), "Y"));
    }

    [Fact]
    public void ResetPosition_puts_the_element_at_the_windows_origin()
    {
        var win = Window();
        var a = Pick(win, "A");

        ElementEditor.ResetPosition(a);

        Assert.Equal("0", Xml.Tx(Xml.Sub(a, "Position"), "X"));
        Assert.Equal("0", Xml.Tx(Xml.Sub(a, "Position"), "Y"));
    }

    // -----------------------------------------------------------------
    // Visibility
    // -----------------------------------------------------------------

    /// <summary>
    /// Only the literal "false" hides an element — the same test
    /// <see cref="DaocUiForge.Core.Render.WindowRenderer"/> makes, so the
    /// editor and the drawing cannot disagree about what is on screen.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("<Visible>true</Visible>", true)]
    [InlineData("<Visible>false</Visible>", false)]
    [InlineData("<Visible>0</Visible>", true)]
    public void IsVisible_only_reacts_to_the_literal_false(string inner, bool expected) =>
        Assert.Equal(expected, ElementEditor.IsVisible(XElement.Parse($"<LabelDef>{inner}</LabelDef>")));

    [Fact]
    public void ToggleVisible_flips_and_reports_the_new_state()
    {
        var def = XElement.Parse("<LabelDef />");

        Assert.False(ElementEditor.ToggleVisible(def));
        Assert.Equal("false", Xml.Tx(def, "Visible"));

        Assert.True(ElementEditor.ToggleVisible(def));
        Assert.Equal("true", Xml.Tx(def, "Visible"));
    }

    // -----------------------------------------------------------------
    // Raw XML
    // -----------------------------------------------------------------

    [Fact]
    public void ReplaceFromXml_swaps_the_node_in_place()
    {
        var win = Window();

        var fresh = ElementEditor.ReplaceFromXml(Pick(win, "B"),
            "<ButtonDef><Data>B2</Data></ButtonDef>");

        Assert.Equal("AB2C", Order(win));
        Assert.Same(win, fresh.Parent);
        Assert.Equal("ButtonDef", fresh.Name.LocalName);
    }

    /// <summary>
    /// Broken XML must leave the window alone. Parsing first and replacing
    /// second is what guarantees that — the old element is still there to fall
    /// back on.
    /// </summary>
    [Fact]
    public void ReplaceFromXml_leaves_the_window_untouched_when_the_xml_is_broken()
    {
        var win = Window();

        Assert.ThrowsAny<XmlException>(() =>
            ElementEditor.ReplaceFromXml(Pick(win, "B"), "<ButtonDef><Data>oops"));

        Assert.Equal("ABC", Order(win));
    }

    // -----------------------------------------------------------------
    // Which fields an element gets
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("LabelDef", "", true)]
    [InlineData("ButtonDef", "", true)]
    [InlineData("TextAreaDef", "", true)]
    [InlineData("ImageAreaDef", "", false)]
    [InlineData("CompassControlDef", "", false)]
    public void HasText_follows_the_type_list_of_the_original(string tag, string inner, bool expected) =>
        Assert.Equal(expected, ElementEditor.HasText(XElement.Parse($"<{tag}>{inner}</{tag}>")));

    /// <summary>
    /// The deliberate widening: a type outside the list that nevertheless
    /// carries a font must not have that value silently un-editable.
    /// </summary>
    [Fact]
    public void HasText_also_applies_when_the_element_already_carries_text_or_a_font()
    {
        Assert.True(ElementEditor.HasText(
            XElement.Parse("<EditBoxDef><FontName>MyFont</FontName></EditBoxDef>")));
        Assert.True(ElementEditor.HasText(
            XElement.Parse("<ComboBoxDef><Data>x</Data></ComboBoxDef>")));
    }
}
