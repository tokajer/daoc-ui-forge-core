using System.Text;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The texture pane: which textures the package declares, how often they are
/// referred to, and which piece of one a template cuts out.
/// </summary>
public class TextureCatalogTests
{
    /// <summary>
    /// Four templates, each written the way a real package writes one: the
    /// texture name on the template or inside its &lt;Texture&gt; block, the
    /// start as &lt;TextureStart&gt; or &lt;TopLeft&gt;, the size as
    /// &lt;Width&gt;/&lt;Height&gt; or as &lt;Size&gt;.
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
        "\t<Texture>\n" +
        "\t\t<Name>unused_tex</Name>\n" +
        "\t\t<File>Assets/Textures/unused.tga</File>\n" +
        "\t</Texture>\n" +
        "\t<ButtonTemplate>\n" +
        "\t\t<Name>gold_button</Name>\n" +
        "\t\t<TextureName>ui_main</TextureName>\n" +
        "\t\t<TextureStart>\n" +
        "\t\t\t<X>10</X>\n" +
        "\t\t\t<Y>20</Y>\n" +
        "\t\t</TextureStart>\n" +
        "\t\t<Width>64</Width>\n" +
        "\t\t<Height>16</Height>\n" +
        "\t</ButtonTemplate>\n" +
        "\t<ImageTemplate>\n" +
        "\t\t<Name>corner</Name>\n" +
        "\t\t<Texture>\n" +
        "\t\t\t<TextureName>ui_main</TextureName>\n" +
        "\t\t</Texture>\n" +
        "\t\t<TopLeft>\n" +
        "\t\t\t<X>0</X>\n" +
        "\t\t\t<Y>96</Y>\n" +
        "\t\t</TopLeft>\n" +
        "\t\t<Size>\n" +
        "\t\t\t<X>8</X>\n" +
        "\t\t\t<Y>8</Y>\n" +
        "\t\t</Size>\n" +
        "\t</ImageTemplate>\n" +
        "\t<ImageTemplate>\n" +
        "\t\t<Name>no_size</Name>\n" +
        "\t\t<TextureName>ui_main</TextureName>\n" +
        "\t\t<TextureStart>\n" +
        "\t\t\t<X>4</X>\n" +
        "\t\t\t<Y>4</Y>\n" +
        "\t\t</TextureStart>\n" +
        "\t</ImageTemplate>\n" +
        "\t<ImageTemplate>\n" +
        "\t\t<Name>no_start</Name>\n" +
        "\t\t<TextureName>ui_main</TextureName>\n" +
        "\t\t<Width>12</Width>\n" +
        "\t\t<Height>12</Height>\n" +
        "\t</ImageTemplate>\n" +
        "\t<WindowTemplate>\n" +
        "\t\t<Name>chat</Name>\n" +
        "\t\t<ImageDef>\n" +
        "\t\t\t<Name>badge</Name>\n" +
        "\t\t\t<TextureName>icons</TextureName>\n" +
        "\t\t</ImageDef>\n" +
        "\t</WindowTemplate>\n" +
        "</Root_Element>\n";

    private static Package Sample()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["base.xml"] = Encoding.UTF8.GetBytes(BaseXml),
        };
        return PackageLoader.Load(files, null);
    }

    private static string Text(Package pkg, string file) =>
        Encoding.UTF8.GetString(PackageWriter.SerializeFile(pkg, file));

    // -----------------------------------------------------------------
    // The list
    // -----------------------------------------------------------------

    [Fact]
    public void All_lists_the_declared_textures_by_name_with_their_file()
    {
        var all = TextureCatalog.All(Sample());

        Assert.Equal(new[] { "icons", "ui_main", "unused_tex" }, all.Select(t => t.Name));
        Assert.Equal("Assets/Textures/icons.tga", all[0].Path);
    }

    [Fact]
    public void The_use_count_says_how_often_a_texture_is_named()
    {
        var all = TextureCatalog.All(Sample()).ToDictionary(t => t.Name);

        // Four templates name ui_main — one of them inside its <Texture> block.
        Assert.Equal(4, all["ui_main"].Uses);
        Assert.Equal(0, all["unused_tex"].Uses);
    }

    /// <summary>
    /// The original counts only what sits below a template. An element naming
    /// its texture directly — 18 of the 878 references in the reference package
    /// — would show as unused.
    /// </summary>
    [Fact]
    public void A_texture_named_by_a_window_element_counts_too()
    {
        Assert.Equal(1, TextureCatalog.All(Sample()).First(t => t.Name == "icons").Uses);
    }

    [Fact]
    public void Filter_matches_the_name()
    {
        var all = TextureCatalog.All(Sample());

        Assert.Equal(new[] { "ui_main" }, TextureCatalog.Filter(all, "MAIN").Select(t => t.Name));
        Assert.Equal(all.Count, TextureCatalog.Filter(all, null).Count());
    }

    // -----------------------------------------------------------------
    // What a template cuts out
    // -----------------------------------------------------------------

    [Fact]
    public void SlicesOf_reads_the_start_and_the_size_the_way_the_drawing_does()
    {
        var marks = TextureCatalog.SlicesOf(Sample(), "ui_main");

        // no_size and no_start have nothing to frame and stay out.
        Assert.Equal(new[] { "corner", "gold_button" }, marks.Select(m => m.Template));

        var button = marks.First(m => m.Template == "gold_button");
        Assert.Equal((10, 20, 64, 16), (button.X, button.Y, button.Width, button.Height));
    }

    [Fact]
    public void SlicesOf_falls_back_from_TextureStart_to_TopLeft_and_from_Width_to_Size()
    {
        var corner = TextureCatalog.SlicesOf(Sample(), "ui_main").First(m => m.Template == "corner");

        Assert.Equal((0, 96, 8, 8), (corner.X, corner.Y, corner.Width, corner.Height));
    }

    [Fact]
    public void SlicesOf_finds_the_texture_name_inside_the_Texture_block()
    {
        // "corner" declares its texture only in <Texture><TextureName>.
        Assert.Contains(TextureCatalog.SlicesOf(Sample(), "ui_main"), m => m.Template == "corner");
    }

    [Fact]
    public void SlicesOf_matches_the_name_without_regard_to_case()
    {
        Assert.NotEmpty(TextureCatalog.SlicesOf(Sample(), "UI_Main"));
    }

    [Fact]
    public void SlicesOf_says_nothing_for_a_texture_no_template_uses()
    {
        Assert.Empty(TextureCatalog.SlicesOf(Sample(), "unused_tex"));
        Assert.Empty(TextureCatalog.SlicesOf(Sample(), ""));
    }

    // -----------------------------------------------------------------
    // Binding one (#mAsset)
    // -----------------------------------------------------------------

    [Fact]
    public void Create_declares_the_texture_in_the_file_and_in_the_package()
    {
        var pkg = Sample();

        var made = TextureCatalog.Create(pkg, pkg.Docs["base.xml"], "glow", "Assets/Textures/glow.tga");

        Assert.Equal("glow", made.Name);
        Assert.Equal("Assets/Textures/glow.tga", made.Path);
        Assert.Equal(0, made.Uses);                       // nothing refers to it yet

        Assert.Equal("Assets/Textures/glow.tga", pkg.Textures["glow"]);
        Assert.Contains(
            "<Texture>\n\t\t<Name>glow</Name>\n\t\t<File>Assets/Textures/glow.tga</File>\n\t</Texture>",
            Text(pkg, "base.xml"));
    }

    /// <summary>
    /// The fault of the original. It asks <c>textures[nm]</c>, keyed exactly,
    /// then writes <c>texturesLc[nm.toLowerCase()]</c> as well — while
    /// resolution reads <c>textures[n] || texturesLc[...]</c>. A namesake in
    /// another spelling passes its check and then answers for the old one
    /// wherever the spellings differ.
    /// </summary>
    [Fact]
    public void Create_refuses_a_name_that_differs_from_a_declared_one_only_in_case()
    {
        var pkg = Sample();

        Assert.Throws<ArgumentException>(() =>
            TextureCatalog.Create(pkg, pkg.Docs["base.xml"], "UI_Main", "Assets/Textures/other.tga"));

        Assert.Equal("Assets/Textures/ui_main.tga", pkg.Textures["ui_main"]);
    }

    [Theory]
    [InlineData("", "Assets/Textures/glow.tga")]
    [InlineData("   ", "Assets/Textures/glow.tga")]
    [InlineData("glow", "")]
    public void Create_refuses_a_binding_that_is_missing_half_of_itself(string name, string file)
    {
        var pkg = Sample();

        Assert.Throws<ArgumentException>(() =>
            TextureCatalog.Create(pkg, pkg.Docs["base.xml"], name, file));

        Assert.False(pkg.Textures.ContainsKey("glow"));
    }

    [Fact]
    public void Create_marks_the_file_so_the_binding_can_be_saved()
    {
        var pkg = Sample();

        TextureCatalog.Create(pkg, pkg.Docs["base.xml"], "glow", "Assets/Textures/glow.tga");

        Assert.Contains("base.xml", pkg.DirtyFiles);
        Assert.Contains("base.xml", PackageWriter.ChangedFiles(pkg));
    }

    /// <summary>
    /// The promise that matters: the loader reads the binding back.
    /// </summary>
    [Fact]
    public void A_bound_texture_survives_a_save_and_a_reload()
    {
        var pkg = Sample();
        TextureCatalog.Create(pkg, pkg.Docs["base.xml"], "glow", "Assets/Textures/glow.tga");

        var again = PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["base.xml"] = PackageWriter.SerializeFile(pkg, "base.xml"),
        });

        Assert.Equal("Assets/Textures/glow.tga", again.Textures["glow"]);
    }

    /// <summary>
    /// The binding is a name and a path; whether the path leads anywhere is a
    /// separate question. It has to stay separate — a texture may legitimately
    /// point into the game folder, and a binding may be written
    /// before the file is copied in. But it is worth being able to ask: the
    /// original takes any string at all and the element then stays empty in the
    /// game.
    /// </summary>
    [Fact]
    public void FileInPackage_answers_from_the_files_not_from_the_declaration()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["base.xml"] = Encoding.UTF8.GetBytes(BaseXml),
            ["assets/textures/glow.tga"] = new byte[] { 1, 2, 3 },
        };
        var pkg = PackageLoader.Load(files, null);

        // Resolved as the drawing layer resolves it — case and separators alike.
        Assert.True(TextureCatalog.FileInPackage(pkg, "Assets/Textures/glow.tga"));

        // Declared in the file, but the texture itself is not in the package.
        Assert.False(TextureCatalog.FileInPackage(pkg, "Assets/Textures/ui_main.tga"));
        Assert.False(TextureCatalog.FileInPackage(pkg, ""));

        // And the binding is created regardless — it is a hint, not a rule.
        TextureCatalog.Create(pkg, pkg.Docs["base.xml"], "nowhere", "Assets/Textures/nowhere.tga");
        Assert.True(pkg.Textures.ContainsKey("nowhere"));
    }

    // -----------------------------------------------------------------
    // Changing and removing one (DAoCEd: Ressourcen -> Ändern / Löschen)
    // -----------------------------------------------------------------

    /// <summary>
    /// The fault this avoids: DAoCEd renames the declaration and leaves the
    /// references, so every template that named the texture draws nothing —
    /// the same shape as its template rename.
    /// </summary>
    [Fact]
    public void A_rename_carries_every_reference_with_it()
    {
        var pkg = Sample();

        var (entry, renamed) = TextureCatalog.Edit(pkg, "ui_main", "ui_main2", "Assets/Textures/x.tga");

        Assert.Equal("ui_main2", entry.Name);
        Assert.Equal(4, renamed);
        Assert.Equal(4, entry.Uses);

        Assert.False(pkg.Textures.ContainsKey("ui_main"));
        Assert.Equal("Assets/Textures/x.tga", pkg.Textures["ui_main2"]);
        Assert.DoesNotContain("<TextureName>ui_main</TextureName>", Text(pkg, "base.xml"));
    }

    [Fact]
    public void Changing_only_the_file_touches_no_reference()
    {
        var pkg = Sample();

        var (entry, renamed) = TextureCatalog.Edit(pkg, "ui_main", "ui_main", "Assets/other.tga");

        Assert.Equal(0, renamed);
        Assert.Equal("Assets/other.tga", entry.Path);
        Assert.Contains("<TextureName>ui_main</TextureName>", Text(pkg, "base.xml"));
    }

    [Fact]
    public void A_rename_onto_a_name_that_is_taken_is_refused()
    {
        var pkg = Sample();

        // Case-insensitive, because that is how every lookup of the name asks.
        Assert.Throws<ArgumentException>(() =>
            TextureCatalog.Edit(pkg, "ui_main", "ICONS", "Assets/Textures/ui_main.tga"));

        Assert.True(pkg.Textures.ContainsKey("ui_main"));
    }

    /// <summary>
    /// The declaration goes, the references stay — and how many now dangle is
    /// the answer, because that is what the inspection report will list.
    /// </summary>
    [Fact]
    public void Delete_removes_the_declaration_and_says_what_now_dangles()
    {
        var pkg = Sample();

        int dangling = TextureCatalog.Delete(pkg, "ui_main");

        Assert.Equal(4, dangling);
        Assert.False(pkg.Textures.ContainsKey("ui_main"));

        string text = Text(pkg, "base.xml");
        Assert.DoesNotContain("<Name>ui_main</Name>", text);
        Assert.Contains("<TextureName>ui_main</TextureName>", text);   // left behind on purpose
    }

    /// <summary>
    /// The &lt;Texture&gt; BLOCK inside a template has the same tag as a
    /// declaration and is a different thing: it carries a &lt;TextureName&gt;,
    /// not a &lt;Name&gt;. Confusing the two would delete a template's insides.
    /// </summary>
    [Fact]
    public void NodeOf_finds_the_declaration_not_a_templates_texture_block()
    {
        var pkg = Sample();

        var node = TextureCatalog.NodeOf(pkg, "ui_main");

        Assert.NotNull(node);
        Assert.Equal("Assets/Textures/ui_main.tga", Xml.Tx(node, "File"));
        Assert.Null(TextureCatalog.NodeOf(pkg, "no_such_texture"));
    }
}
