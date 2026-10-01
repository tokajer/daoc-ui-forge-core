using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The writing side of the XML access (<c>ensure</c>/<c>setSub</c> in the
/// original) plus the question of which tag a value belongs in.
/// </summary>
public class XmlEditTests
{
    private static XElement Def(string inner = "") =>
        XElement.Parse($"<LabelDef>{inner}</LabelDef>");

    /// <summary>
    /// A document parsed the way the loader parses one — every indent in the
    /// tree as a text node of its own. Without that nothing below is testable:
    /// the indentation simply would not be there.
    /// </summary>
    private static XDocument Doc(string xml) =>
        XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

    /// <summary>
    /// The tree as text, with the line breaks it really holds.
    ///
    /// <para>Deliberately <b>not</b> <c>ToString(SaveOptions.DisableFormatting)</c>:
    /// that leaves <c>NewLineHandling</c> at <c>Replace</c>, whose
    /// <c>NewLineChars</c> default to <see cref="Environment.NewLine"/>, so it
    /// rewrites every line break to the local one — which is exactly what these
    /// tests are about, and what would make every one of them pass on Linux and
    /// fail on Windows. <c>PackageWriter.Body</c> does the same thing for the
    /// same reason.</para>
    /// </summary>
    private static string Flat(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings)) doc.Root!.WriteTo(writer);
        return sb.ToString();
    }

    // -----------------------------------------------------------------
    // Ensure / SetSub
    // -----------------------------------------------------------------

    [Fact]
    public void Ensure_returns_the_existing_child_rather_than_a_second_one()
    {
        var def = Def("<Width>40</Width>");

        var got = XmlEdit.Ensure(def, "Width");

        Assert.Equal("40", got.Value);
        Assert.Single(def.Elements("Width"));
    }

    [Fact]
    public void Ensure_finds_case_insensitively_and_keeps_the_files_own_spelling()
    {
        // Real packages contain <Adaptername> beside <AdapterName>. Writing
        // must land in the element that is there, not create a twin.
        var def = Def("<Adaptername>group_health1</Adaptername>");

        XmlEdit.SetSub(def, "group_health2", "AdapterName");

        Assert.Single(def.Elements());
        Assert.Equal("Adaptername", def.Elements().First().Name.LocalName);
        Assert.Equal("group_health2", Xml.Tx(def, "AdapterName"));
    }

    [Fact]
    public void Ensure_appends_what_is_missing()
    {
        var def = Def();

        XmlEdit.Ensure(def, "Height");

        Assert.NotNull(Xml.Sub(def, "Height"));
    }

    [Fact]
    public void SetSub_creates_the_whole_path()
    {
        var def = Def();

        XmlEdit.SetSub(def, 12d, "Position", "X");
        XmlEdit.SetSub(def, 34d, "Position", "Y");

        var pos = Xml.Sub(def, "Position");
        Assert.NotNull(pos);
        Assert.Equal("12", Xml.Tx(pos, "X"));
        Assert.Equal("34", Xml.Tx(pos, "Y"));
        Assert.Single(def.Elements("Position"));
    }

    /// <summary>
    /// The reason the numeric overload exists. With a comma as the decimal
    /// separator the default ToString() writes "5,5", which
    /// <see cref="RenderMath.Num"/> reads back as 5 — a value altered by the
    /// machine's locale alone.
    ///
    /// <para>The culture is built by hand rather than fetched as "de-DE":
    /// the solution runs with <c>InvariantGlobalization</c>, so named cultures
    /// do not exist here. That switch has to come off for the planned German
    /// interface, and then the trap is live — which is exactly why the
    /// formatting must not depend on it.</para>
    /// </summary>
    [Fact]
    public void SetSub_writes_numbers_locale_independently()
    {
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";

        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;
            Assert.Equal("5,5", 5.5.ToString());     // the trap really is armed

            var def = Def();
            XmlEdit.SetSub(def, 5.5, "Position", "X");

            Assert.Equal("5.5", Xml.Tx(Xml.Sub(def, "Position"), "X"));
            Assert.Equal(5.5, RenderMath.Num(Xml.Tx(Xml.Sub(def, "Position"), "X")));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void SetSub_writes_whole_numbers_without_a_decimal_point()
    {
        var def = Def();

        XmlEdit.SetSub(def, 40d, "Width");

        Assert.Equal("40", Xml.Tx(def, "Width"));
    }

    [Fact]
    public void SetOrRemove_deletes_on_an_empty_value()
    {
        var def = Def("<TemplateName>foo</TemplateName>");

        XmlEdit.SetOrRemove(def, "", "TemplateName");

        Assert.Null(Xml.Sub(def, "TemplateName"));
    }

    [Fact]
    public void SetOrRemove_sets_a_real_value()
    {
        var def = Def();

        XmlEdit.SetOrRemove(def, "bar", "TemplateName");

        Assert.Equal("bar", Xml.Tx(def, "TemplateName"));
    }

    // -----------------------------------------------------------------
    // Which tag a value belongs in
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("<Data>x</Data>", "Data")]
    [InlineData("<Label>x</Label>", "Label")]
    [InlineData("<Text>x</Text>", "Text")]
    [InlineData("", "Data")]                                    // fallback
    [InlineData("<Label>x</Label><Data>y</Data>", "Data")]       // Data wins
    public void TextKey_writes_into_the_tag_that_is_there(string inner, string expected) =>
        Assert.Equal(expected, XmlEdit.TextKey(Def(inner)));

    [Theory]
    [InlineData("<Adapter>a</Adapter>", "Adapter")]
    [InlineData("<AdapterName>a</AdapterName>", "AdapterName")]
    [InlineData("<TextAdapterName>a</TextAdapterName>", "TextAdapterName")]
    [InlineData("", "AdapterName")]                              // fallback
    public void AdapterKey_writes_into_the_tag_that_is_there(string inner, string expected) =>
        Assert.Equal(expected, XmlEdit.AdapterKey(Def(inner)));

    /// <summary>
    /// The whole point of picking the key this way: what
    /// <see cref="SampleData.AdapterOf"/> reads has to be what the editor
    /// writes, otherwise typing into the field changes nothing on screen.
    /// </summary>
    [Theory]
    [InlineData("<Adapter>old</Adapter>")]
    [InlineData("<AdapterName>old</AdapterName>")]
    [InlineData("<TextAdapterName>old</TextAdapterName>")]
    [InlineData("<LabelAdapterName>old</LabelAdapterName>")]
    [InlineData("")]
    public void The_adapter_written_is_the_adapter_read_back(string inner)
    {
        var def = Def(inner);

        XmlEdit.SetSub(def, "player_health", XmlEdit.AdapterKey(def));

        Assert.Equal("player_health", SampleData.AdapterOf(def));
    }

    [Theory]
    [InlineData("<OnClickEvent>e</OnClickEvent>", "OnClickEvent")]
    [InlineData("<Onclickevent>e</Onclickevent>", "OnClickEvent")]  // same tag, other spelling
    [InlineData("<Command>e</Command>", "Command")]
    [InlineData("<ActionCode>e</ActionCode>", "ActionCode")]
    [InlineData("", "OnClickEvent")]                                // fallback
    public void EventKey_writes_into_the_tag_that_is_there(string inner, string expected) =>
        Assert.Equal(expected, XmlEdit.EventKey(Def(inner)));

    [Fact]
    public void EventOf_reads_in_the_order_of_the_original()
    {
        Assert.Equal("a", XmlEdit.EventOf(Def("<ActionCode>a</ActionCode><Command>b</Command>")));
        Assert.Equal("b", XmlEdit.EventOf(Def("<Command>b</Command><OnClickEvent>c</OnClickEvent>")));
        Assert.Equal("c", XmlEdit.EventOf(Def("<OnClickEvent>c</OnClickEvent>")));
        Assert.Equal("", XmlEdit.EventOf(Def()));
    }

    [Fact]
    public void The_event_written_is_the_event_read_back()
    {
        var def = Def("<Command>old</Command>");

        XmlEdit.SetSub(def, "quit", XmlEdit.EventKey(def));

        Assert.Equal("quit", XmlEdit.EventOf(def));
    }

    // -----------------------------------------------------------------
    // The id of a window
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("<Name>chat</Name>", "Name")]
    [InlineData("<n>chat</n>", "n")]
    [InlineData("<WindowId>17</WindowId>", "WindowId")]
    [InlineData("<Name>chat</Name><WindowId>17</WindowId>", "Name")]
    [InlineData("", "Name")]
    public void WindowIdKey_writes_into_the_tag_the_id_came_from(string inner, string expected) =>
        Assert.Equal(expected, XmlEdit.WindowIdKey(XElement.Parse($"<WindowTemplate>{inner}</WindowTemplate>")));

    /// <summary>
    /// The point of the deviation from the original. It always writes
    /// &lt;WindowId&gt;, so on a window that carries a &lt;Name&gt; the rename
    /// is lost the moment the package is loaded again — and &lt;WindowId&gt;
    /// is not unique either.
    /// </summary>
    [Fact]
    public void The_window_id_written_is_the_window_id_read_back()
    {
        var win = XElement.Parse("<WindowTemplate><Name>chat</Name><WindowId>17</WindowId></WindowTemplate>");

        XmlEdit.SetSub(win, "chat_wide", XmlEdit.WindowIdKey(win));

        Assert.Equal("chat_wide", Xml.NameOf(win));
        Assert.Equal("17", Xml.Tx(win, "WindowId"));   // untouched — it is not the id
    }

    // -----------------------------------------------------------------
    // Append and the indentation of a created element
    // -----------------------------------------------------------------

    /// <summary>
    /// A created element with children of its own arrives as one long line
    /// unless somebody lays it out. Everything else in these files is indented,
    /// so a template created in the editor would be the one unreadable block in
    /// its file — and unreadable in the diff too.
    /// </summary>
    [Fact]
    public void Append_lays_a_nested_element_out_the_way_the_file_is_laid_out()
    {
        var doc = Doc("<Root>\n    <A>\n        <X>1</X>\n    </A>\n</Root>");

        XmlEdit.Append(doc.Root!, new XElement("B",
            new XElement("Name", "silver"),
            new XElement("Size", new XElement("X", "16"))));

        Assert.Equal(
            "<Root>\n    <A>\n        <X>1</X>\n    </A>\n"
            + "    <B>\n        <Name>silver</Name>\n"
            + "        <Size>\n            <X>16</X>\n        </Size>\n    </B>\n</Root>",
            Flat(doc));
    }

    /// <summary>
    /// The step comes from the file, not from a default. The reference package
    /// has files on four spaces and files on tabs; guessing would put a style
    /// change into the diff on top of the real one.
    /// </summary>
    [Fact]
    public void Append_takes_the_indentation_step_from_the_file()
    {
        var doc = Doc("<Root>\n\t<A>\n\t\t<X>1</X>\n\t</A>\n</Root>");

        XmlEdit.Append(doc.Root!, new XElement("B", new XElement("Name", "silver")));

        Assert.Contains("\n\t<B>\n\t\t<Name>silver</Name>\n\t</B>", Flat(doc));
    }

    /// <summary>
    /// Blank lines between elements are ordinary in these files (assets.xml of
    /// the reference package has two before every entry). Comparing the whole
    /// text node would let one of them defeat every match and fall back to the
    /// default step.
    /// </summary>
    [Fact]
    public void IndentUnitOf_looks_past_blank_lines_between_elements()
    {
        var doc = Doc("<Root>\n\n\n  <A>\n\n    <X>1</X>\n  </A>\n</Root>");

        Assert.Equal("  ", XmlEdit.IndentUnitOf(doc));
    }

    /// <summary>
    /// The blank lines a file keeps between its top-level entries separate one
    /// entry from the next — they are not a level, and must not be carried
    /// inwards. Found against the real package: <c>assets.xml</c> puts two
    /// empty lines in front of every entry, and the first version of this put
    /// two in front of every <i>field</i> of the created template as well.
    /// </summary>
    [Fact]
    public void Append_does_not_carry_the_blank_lines_between_entries_inwards()
    {
        var doc = Doc("<Root>\n\n\n    <A>\n        <X>1</X>\n    </A>\n</Root>");

        XmlEdit.Append(doc.Root!, new XElement("B", new XElement("Name", "silver")));

        // The new entry keeps the file's spacing in front of it, and its own
        // fields sit on plain lines one step further in.
        Assert.Equal(
            "<Root>\n\n\n    <A>\n        <X>1</X>\n    </A>\n\n\n"
            + "    <B>\n        <Name>silver</Name>\n    </B>\n</Root>",
            Flat(doc));
    }

    /// <summary>
    /// The line break is taken from the document, not written as <c>"\n"</c>.
    ///
    /// <para><b>This cannot bite through the loader today</b> — the parser folds
    /// CRLF to LF (XML §2.11), so a parsed tree only ever holds LF, and
    /// <see cref="Core.Editing.PackageWriter"/> puts the file's own ending back
    /// from the source bytes. The tree here is therefore built by hand. It is
    /// pinned anyway because assuming a line break is exactly the shape of
    /// mistake that just cost the writer ten tests on Windows, and taking the
    /// one that is there costs nothing.</para>
    /// </summary>
    [Fact]
    public void Append_takes_the_line_break_from_the_document_rather_than_assuming_one()
    {
        var doc = new XDocument(new XElement("Root",
            new XText("\r\n    "), new XElement("A", new XText("\r\n        "),
                new XElement("X", "1"), new XText("\r\n    ")),
            new XText("\r\n")));

        XmlEdit.Append(doc.Root!, new XElement("B", new XElement("Name", "silver")));

        string flat = Flat(doc);
        Assert.Contains("\r\n    <B>\r\n        <Name>silver</Name>\r\n    </B>", flat);

        // Nothing left over: every break in the result is a CRLF.
        Assert.DoesNotContain("\n", flat.Replace("\r\n", ""));
    }

    /// <summary>
    /// A document that keeps everything on one line stays on one line: the
    /// break would be the only change in the file.
    /// </summary>
    [Fact]
    public void Append_does_not_break_a_document_that_has_no_line_breaks()
    {
        var doc = Doc("<Root><A><X>1</X></A></Root>");

        XmlEdit.Append(doc.Root!, new XElement("B", new XElement("Name", "silver")));

        Assert.Equal("<Root><A><X>1</X></A><B><Name>silver</Name></B></Root>", Flat(doc));
    }

    /// <summary>
    /// A node that already carries whitespace — one copied out of the document,
    /// say — is not ours to reformat.
    /// </summary>
    [Fact]
    public void Append_leaves_a_node_that_is_already_laid_out_alone()
    {
        var doc = Doc("<Root>\n    <A>\n        <X>1</X>\n    </A>\n</Root>");

        XmlEdit.Append(doc.Root!, new XElement(doc.Root!.Element("A")!));

        Assert.Equal(
            "<Root>\n    <A>\n        <X>1</X>\n    </A>\n    <A>\n        <X>1</X>\n    </A>\n</Root>",
            Flat(doc));
    }

    /// <summary>
    /// The promise <see cref="XmlEdit.Ensure"/> was built on has to survive the
    /// move into <see cref="XmlEdit.Append"/>: the new element goes behind the
    /// last child, not behind the whitespace that indents the closing tag —
    /// otherwise the tag joins it on one line and one value costs two lines of
    /// diff.
    /// </summary>
    [Fact]
    public void Append_keeps_the_closing_tag_on_its_own_line()
    {
        var doc = Doc("<Root>\n    <A>1</A>\n</Root>");

        XmlEdit.Append(doc.Root!, new XElement("B", "2"));

        Assert.Equal("<Root>\n    <A>1</A>\n    <B>2</B>\n</Root>", Flat(doc));
    }
}
