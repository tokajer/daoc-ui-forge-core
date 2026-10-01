using System.Text;
using System.Xml;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The template pane: the list, the classification of a template's fields, and
/// the two edits behind it (delete, replace from XML).
/// </summary>
public class TemplateCatalogTests
{
    /// <summary>
    /// One file with the shapes a template field can take: a plain value, a
    /// point, a colour, a nested block — and a colour spelled in lower case,
    /// which the original would not recognise.
    /// </summary>
    private const string BaseXml =
        "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n" +
        "<Root_Element ID=\"DAOCUi\">\n" +
        "\t<Texture>\n" +
        "\t\t<Name>ui_main</Name>\n" +
        "\t\t<File>Assets/Textures/ui_main.tga</File>\n" +
        "\t</Texture>\n" +
        "\t<Texture>\n" +
        "\t\t<Name>icons</Name>\n" +
        "\t\t<File>Assets/Textures/icons.tga</File>\n" +
        "\t</Texture>\n" +
        "\t<ButtonTemplate>\n" +
        "\t\t<Name>gold_button</Name>\n" +
        "\t\t<TextureName>ui_main</TextureName>\n" +
        "\t\t<TextureStart>\n" +
        "\t\t\t<X>10</X>\n" +
        "\t\t\t<Y>20</Y>\n" +
        "\t\t</TextureStart>\n" +
        "\t\t<Size>\n" +
        "\t\t\t<X>64</X>\n" +
        "\t\t\t<Y>16</Y>\n" +
        "\t\t</Size>\n" +
        "\t\t<Color>\n" +
        "\t\t\t<R>200</R>\n" +
        "\t\t\t<G>160</G>\n" +
        "\t\t\t<B>74</B>\n" +
        "\t\t\t<A>255</A>\n" +
        "\t\t</Color>\n" +
        "\t\t<GlowColor>\n" +
        "\t\t\t<r>1</r>\n" +
        "\t\t\t<g>2</g>\n" +
        "\t\t\t<b>3</b>\n" +
        "\t\t</GlowColor>\n" +
        "\t\t<Border>\n" +
        "\t\t\t<TopLeft>\n" +
        "\t\t\t\t<X>1</X>\n" +
        "\t\t\t\t<Y>2</Y>\n" +
        "\t\t\t</TopLeft>\n" +
        "\t\t\t<Edge>\n" +
        "\t\t\t\t<TextureName>ui_main</TextureName>\n" +
        "\t\t\t</Edge>\n" +
        "\t\t\t<Tint>\n" +
        "\t\t\t\t<R>1</R>\n" +
        "\t\t\t\t<G>2</G>\n" +
        "\t\t\t\t<B>3</B>\n" +
        "\t\t\t</Tint>\n" +
        "\t\t\t<Shadow>\n" +
        "\t\t\t\t<Color>\n" +
        "\t\t\t\t\t<R>9</R>\n" +
        "\t\t\t\t\t<G>9</G>\n" +
        "\t\t\t\t\t<B>9</B>\n" +
        "\t\t\t\t</Color>\n" +
        "\t\t\t</Shadow>\n" +
        "\t\t</Border>\n" +
        "\t</ButtonTemplate>\n" +
        "\t<ImageTemplate>\n" +
        "\t\t<Name>icon_frame</Name>\n" +
        "\t\t<Texture>\n" +
        "\t\t\t<TextureName>icons</TextureName>\n" +
        "\t\t</Texture>\n" +
        "\t\t<TopLeft>\n" +
        "\t\t\t<X>0</X>\n" +
        "\t\t\t<Y>0</Y>\n" +
        "\t\t</TopLeft>\n" +
        "\t\t<Width>32</Width>\n" +
        "\t\t<Height>32</Height>\n" +
        "\t</ImageTemplate>\n" +
        "\t<ImageTemplate>\n" +
        "\t\t<Name>no_slice</Name>\n" +
        "\t\t<TextureName>icons</TextureName>\n" +
        "\t</ImageTemplate>\n" +
        "\t<WindowTemplate>\n" +
        "\t\t<Name>chat</Name>\n" +
        "\t\t<ButtonDef>\n" +
        "\t\t\t<Name>send</Name>\n" +
        "\t\t\t<TemplateName>gold_button</TemplateName>\n" +
        "\t\t</ButtonDef>\n" +
        "\t\t<ButtonDef>\n" +
        "\t\t\t<Name>clear</Name>\n" +
        "\t\t\t<TemplateName>gold_button</TemplateName>\n" +
        "\t\t</ButtonDef>\n" +
        "\t\t<ImageDef>\n" +
        "\t\t\t<Name>badge</Name>\n" +
        "\t\t\t<IconTemplateName>icon_frame</IconTemplateName>\n" +
        "\t\t\t<TextureName>icons</TextureName>\n" +
        "\t\t</ImageDef>\n" +
        "\t</WindowTemplate>\n" +
        "</Root_Element>\n";

    /// <summary>
    /// A second file, carrying a template of the <i>same name</i> under another
    /// type. That is what makes the duplicate-name case testable: both survive
    /// in <see cref="Package.Templates"/>, while
    /// <see cref="Package.ByNameLc"/> can only point at one of them.
    /// </summary>
    private const string ExtraXml =
        "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n" +
        "<Root_Element ID=\"DAOCUi\">\n" +
        "\t<ScalarLabelTemplate>\n" +
        "\t\t<Name>gold_button</Name>\n" +
        "\t\t<Width>10</Width>\n" +
        "\t</ScalarLabelTemplate>\n" +
        "\t<WindowTemplate>\n" +
        "\t\t<Name>combat</Name>\n" +
        "\t</WindowTemplate>\n" +
        "</Root_Element>\n";

    private static Package Sample()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["base.xml"] = Encoding.UTF8.GetBytes(BaseXml),
            ["extra.xml"] = Encoding.UTF8.GetBytes(ExtraXml),
        };
        return PackageLoader.Load(files, null);
    }

    private static TemplateEntry Pick(Package pkg, string type, string name) =>
        TemplateCatalog.All(pkg).First(t => t.Type == type && t.Name == name);

    private static string Text(Package pkg, string file) =>
        Encoding.UTF8.GetString(PackageWriter.SerializeFile(pkg, file));

    // -----------------------------------------------------------------
    // The list
    // -----------------------------------------------------------------

    [Fact]
    public void All_lists_every_template_sorted_by_type_and_then_by_name()
    {
        var all = TemplateCatalog.All(Sample());

        Assert.Equal(
            new[]
            {
                ("ButtonTemplate", "gold_button"),
                ("ImageTemplate", "icon_frame"),
                ("ImageTemplate", "no_slice"),
                ("ScalarLabelTemplate", "gold_button"),
            },
            all.Select(t => (t.Type, t.Name)));
    }

    [Fact]
    public void The_short_type_drops_the_suffix()
    {
        Assert.Equal("Button", Pick(Sample(), "ButtonTemplate", "gold_button").ShortType);
    }

    [Fact]
    public void Filter_matches_the_name_or_the_type()
    {
        var all = TemplateCatalog.All(Sample());

        Assert.Equal(new[] { "icon_frame" },
            TemplateCatalog.Filter(all, "frame").Select(t => t.Name));

        // "image" occurs in no name — only in the type.
        Assert.Equal(new[] { "icon_frame", "no_slice" },
            TemplateCatalog.Filter(all, "image").Select(t => t.Name));

        Assert.Equal(all.Count, TemplateCatalog.Filter(all, "").Count());
    }

    // -----------------------------------------------------------------
    // Which shape a field has
    // -----------------------------------------------------------------

    [Fact]
    public void Fields_leave_the_name_out_and_classify_by_shape()
    {
        var fields = TemplateCatalog.Fields(Pick(Sample(), "ButtonTemplate", "gold_button").Node);

        Assert.DoesNotContain(fields, f => f.Label == "Name");

        TemplateField Get(string label) => fields.First(f => f.Label == label);

        Assert.Equal(TemplateFieldKind.Value, Get("TextureName").Kind);
        Assert.Equal(TemplateFieldKind.Pair, Get("TextureStart").Kind);
        Assert.Equal(TemplateFieldKind.Pair, Get("Size").Kind);
        Assert.Equal(TemplateFieldKind.Color, Get("Color").Kind);
        Assert.Equal(TemplateFieldKind.Block, Get("Border").Kind);

        // The parts are handed over, so the app does not have to find them again.
        Assert.Equal(new[] { "R", "G", "B", "A" },
            Get("Color").Parts.Select(p => p.Name.LocalName));
        Assert.Equal(new[] { "X", "Y" }, Get("TextureStart").Parts.Select(p => p.Name.LocalName));
    }

    /// <summary>
    /// A deliberate deviation: the original tests the channel names with
    /// <c>/^[RGBA]$/</c> and would show this one as a nested block, although
    /// everything on its own reading side is case-insensitive.
    /// </summary>
    [Fact]
    public void A_colour_spelled_in_lower_case_is_still_a_colour()
    {
        var fields = TemplateCatalog.Fields(Pick(Sample(), "ButtonTemplate", "gold_button").Node);

        Assert.Equal(TemplateFieldKind.Color, fields.First(f => f.Label == "GlowColor").Kind);
    }

    [Fact]
    public void Flatten_walks_a_block_and_labels_its_rows_with_the_path()
    {
        var border = TemplateCatalog
            .Fields(Pick(Sample(), "ButtonTemplate", "gold_button").Node)
            .First(f => f.Label == "Border");

        var rows = TemplateCatalog.Flatten(border.Node);

        Assert.Equal(
            new[] { "TopLeft.X", "TopLeft.Y", "Edge.TextureName", "Tint", "Shadow.Color" },
            rows.Select(r => r.Label));

        // A colour stays one row wherever it sits — directly in the block, or
        // one level further down.
        Assert.Equal(TemplateFieldKind.Color, rows.First(r => r.Label == "Tint").Kind);
        Assert.Equal(TemplateFieldKind.Color, rows.First(r => r.Label == "Shadow.Color").Kind);
        Assert.Equal(TemplateFieldKind.Value, rows.First(r => r.Label == "TopLeft.X").Kind);

        // A value row points at the element whose text is edited.
        Assert.Equal("1", rows.First(r => r.Label == "TopLeft.X").Node.Value.Trim());
    }

    // -----------------------------------------------------------------
    // Where a template is used
    // -----------------------------------------------------------------

    [Fact]
    public void UsedBy_names_a_window_once_however_many_elements_use_the_template()
    {
        var pkg = Sample();

        var used = TemplateCatalog.UsedBy(pkg, "gold_button");

        Assert.Single(used);
        Assert.Equal("chat", used[0].Id);
    }

    /// <summary>
    /// Wider than the original, which asks four tags — two of which are the
    /// same question — while its renderer honours six.
    /// </summary>
    [Fact]
    public void UsedBy_sees_the_other_template_tags_too()
    {
        var used = TemplateCatalog.UsedBy(Sample(), "icon_frame");

        Assert.Equal(new[] { "chat" }, used.Select(w => w.Id));
    }

    [Fact]
    public void UsedBy_says_nothing_for_a_template_nobody_refers_to()
    {
        Assert.Empty(TemplateCatalog.UsedBy(Sample(), "no_slice"));
    }

    // -----------------------------------------------------------------
    // Deleting
    // -----------------------------------------------------------------

    [Fact]
    public void Delete_removes_the_template_from_the_file_and_from_the_lookups()
    {
        var pkg = Sample();

        TemplateCatalog.Delete(pkg, Pick(pkg, "ImageTemplate", "icon_frame"));

        Assert.False(pkg.Templates["ImageTemplate"].ContainsKey("icon_frame"));
        Assert.False(pkg.ByNameLc.ContainsKey("icon_frame"));
        // The definition is gone; the element still referring to it is not the
        // catalog's business — the editor reports that as a missing template.
        Assert.DoesNotContain("<Name>icon_frame</Name>", Text(pkg, "base.xml"));
    }

    [Fact]
    public void Delete_marks_the_file_so_the_change_is_saved()
    {
        var pkg = Sample();

        TemplateCatalog.Delete(pkg, Pick(pkg, "ImageTemplate", "icon_frame"));

        // No window of that file is dirty — nothing in one changed — so the
        // file has to name itself.
        Assert.Contains("base.xml", pkg.DirtyFiles);
        Assert.Contains("base.xml", PackageWriter.ChangedFiles(pkg));
    }

    [Fact]
    public void Delete_takes_the_indentation_of_the_template_with_it()
    {
        var pkg = Sample();

        TemplateCatalog.Delete(pkg, Pick(pkg, "ImageTemplate", "icon_frame"));

        // The neighbour still starts its own line with its own indent — the
        // symptom of a lost indent is two elements sharing a line.
        Assert.Contains("\t<ImageTemplate>\n\t\t<Name>no_slice</Name>", Text(pkg, "base.xml"));
        Assert.DoesNotContain("\n\n", Text(pkg, "base.xml"));
    }

    /// <summary>
    /// The original deletes <c>byName[nm]</c> outright. With duplicate names
    /// that throws away the entry of a template still in the package, and every
    /// element referring to the name draws nothing from then on.
    /// </summary>
    [Fact]
    public void Delete_hands_the_lookup_to_a_surviving_namesake()
    {
        var pkg = Sample();

        // Whichever of the two won on load — take that one away.
        var winner = pkg.ByNameLc["gold_button"];
        var entry = TemplateCatalog.All(pkg).First(t => ReferenceEquals(t.Node, winner));

        TemplateCatalog.Delete(pkg, entry);

        Assert.True(pkg.ByNameLc.ContainsKey("gold_button"));
        Assert.NotSame(winner, pkg.ByNameLc["gold_button"]);
        Assert.Equal(1, TemplateCatalog.All(pkg).Count(t => t.Name == "gold_button"));
    }

    // -----------------------------------------------------------------
    // Replacing from raw XML
    // -----------------------------------------------------------------

    [Fact]
    public void ReplaceFromXml_reads_the_name_and_the_type_out_of_the_new_xml()
    {
        var pkg = Sample();

        var fresh = TemplateCatalog.ReplaceFromXml(pkg, Pick(pkg, "ImageTemplate", "icon_frame"),
            "<ScalarLabelTemplate><Name>silver_frame</Name><Width>4</Width></ScalarLabelTemplate>");

        Assert.Equal("ScalarLabelTemplate", fresh.Type);
        Assert.Equal("silver_frame", fresh.Name);

        // Reachable under the new name, gone under the old one — the original
        // writes the new node back under the old key and loses both.
        Assert.Same(fresh.Node, pkg.ByNameLc["silver_frame"]);
        Assert.Same(fresh.Node, pkg.Templates["ScalarLabelTemplate"]["silver_frame"]);
        Assert.False(pkg.ByNameLc.ContainsKey("icon_frame"));
        Assert.False(pkg.Templates["ImageTemplate"].ContainsKey("icon_frame"));

        Assert.Contains("silver_frame", Text(pkg, "base.xml"));
        Assert.Contains("base.xml", pkg.DirtyFiles);
    }

    [Fact]
    public void ReplaceFromXml_leaves_the_template_alone_when_the_xml_is_broken()
    {
        var pkg = Sample();
        var entry = Pick(pkg, "ImageTemplate", "icon_frame");

        Assert.Throws<XmlException>(() =>
            TemplateCatalog.ReplaceFromXml(pkg, entry, "<ImageTemplate><Name>oops</ImageTemplate>"));

        Assert.Same(entry.Node, pkg.ByNameLc["icon_frame"]);
        Assert.Contains("icon_frame", Text(pkg, "base.xml"));
    }

    [Fact]
    public void An_edited_field_reaches_the_file()
    {
        var pkg = Sample();
        var fields = TemplateCatalog.Fields(Pick(pkg, "ButtonTemplate", "gold_button").Node);

        // What the app does when a value box is typed into.
        fields.First(f => f.Label == "TextureName").Node.Value = "icons";
        var start = fields.First(f => f.Label == "TextureStart");
        start.Parts[0].Value = "11";

        string text = Text(pkg, "base.xml");
        Assert.Contains("<TextureName>icons</TextureName>", text);
        Assert.Contains("<X>11</X>", text);
    }

    [Fact]
    public void A_template_that_is_not_in_a_document_of_the_package_marks_nothing()
    {
        var pkg = Sample();
        var loose = XElement.Parse("<ButtonTemplate><Name>loose</Name></ButtonTemplate>");

        Assert.False(PackageEditor.MarkFileDirty(pkg, loose));
        Assert.Empty(pkg.DirtyFiles);
    }

    // -----------------------------------------------------------------
    // Creating one (#bNewTpl)
    // -----------------------------------------------------------------

    [Fact]
    public void Create_puts_the_template_into_the_file_and_into_both_lookups()
    {
        var pkg = Sample();

        var made = TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", "silver_button");

        Assert.Equal("ButtonTemplate", made.Type);
        Assert.Equal("silver_button", made.Name);

        Assert.Same(made.Node, pkg.Templates["ButtonTemplate"]["silver_button"]);
        Assert.Same(made.Node, pkg.ByNameLc["silver_button"]);
        Assert.Contains("silver_button", Text(pkg, "base.xml"));

        // The 16 × 16 of the original: without a size most types draw nothing,
        // and an empty template nobody can see is hard to go on editing.
        var size = Xml.Sub(made.Node, "Size");
        Assert.Equal("16", Xml.Tx(size, "X"));
        Assert.Equal("16", Xml.Tx(size, "Y"));
    }

    /// <summary>
    /// The fault of the original. It asks <c>byName[nm]</c>, keyed exactly, and
    /// then writes <c>byNameLc[nm.toLowerCase()]</c> — the table every lookup
    /// falls back to. Creating a namesake in another spelling therefore passes
    /// its check and quietly takes the old template's place.
    /// </summary>
    [Fact]
    public void Create_refuses_a_name_that_differs_from_an_existing_one_only_in_case()
    {
        var pkg = Sample();
        var before = pkg.ByNameLc["gold_button"];

        Assert.Throws<ArgumentException>(() =>
            TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", "Gold_Button"));

        Assert.Same(before, pkg.ByNameLc["gold_button"]);
        Assert.DoesNotContain("Gold_Button", Text(pkg, "base.xml"));
    }

    /// <summary>
    /// The loader collects everything ending in "Template" except
    /// &lt;WindowTemplate&gt;. A type outside that rule is written
    /// into the file happily and is simply not there after the next load;
    /// &lt;WindowTemplate&gt; would come back as a phantom window.
    /// </summary>
    [Theory]
    [InlineData("WindowTemplate")]
    [InlineData("ButtonDef")]
    [InlineData("")]
    public void Create_refuses_a_type_the_loader_would_not_collect_as_a_template(string type)
    {
        var pkg = Sample();

        Assert.Throws<ArgumentException>(() =>
            TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], type, "silver_button"));

        Assert.False(pkg.ByNameLc.ContainsKey("silver_button"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_refuses_a_template_without_a_name(string name)
    {
        var pkg = Sample();

        Assert.Throws<ArgumentException>(() =>
            TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", name));
    }

    /// <summary>
    /// No window changed, so nothing else would notice — the file has to name
    /// itself, or the new template never reaches the disk.
    /// </summary>
    [Fact]
    public void Create_marks_the_file_so_the_new_template_can_be_saved()
    {
        var pkg = Sample();

        TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", "silver_button");

        Assert.Contains("base.xml", pkg.DirtyFiles);
        Assert.Contains("base.xml", PackageWriter.ChangedFiles(pkg));
        Assert.DoesNotContain(pkg.Windows, w => w.Dirty);
    }

    /// <summary>
    /// The promise that matters: a template created here is a template the
    /// loader finds again. Everything else — the type rule, the name, the
    /// element being in the tree at all — is only a means to this.
    /// </summary>
    [Fact]
    public void A_created_template_survives_a_save_and_a_reload()
    {
        var pkg = Sample();
        TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", "silver_button");

        var again = PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["base.xml"] = PackageWriter.SerializeFile(pkg, "base.xml"),
        });

        var back = PackageLoader.FindTemplate(again, "silver_button");
        Assert.NotNull(back);
        Assert.Equal("ButtonTemplate", back!.Name.LocalName);
        Assert.Equal("16", Xml.Tx(Xml.Sub(back, "Size"), "X"));
    }

    /// <summary>
    /// The new template reads like the rest of its file. The sample is written
    /// with tabs, as half the reference package is.
    /// </summary>
    [Fact]
    public void A_created_template_is_indented_like_its_neighbours()
    {
        var pkg = Sample();
        TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", "silver_button");

        Assert.Contains(
            "\n\t<ButtonTemplate>\n\t\t<Name>silver_button</Name>\n"
            + "\t\t<Size>\n\t\t\t<X>16</X>\n\t\t\t<Y>16</Y>\n\t\t</Size>\n\t</ButtonTemplate>",
            Text(pkg, "base.xml"));
    }

    /// <summary>
    /// Only the file that was written to changes. A create that reached into a
    /// second document would be invisible until the next load.
    /// </summary>
    [Fact]
    public void Create_touches_only_the_file_it_was_given()
    {
        var pkg = Sample();
        string before = Text(pkg, "extra.xml");

        TemplateCatalog.Create(pkg, pkg.Docs["base.xml"], "ButtonTemplate", "silver_button");

        Assert.Equal(before, Text(pkg, "extra.xml"));
        Assert.DoesNotContain("extra.xml", pkg.DirtyFiles);
    }
}
