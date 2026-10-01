using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The &lt;TTFFont&gt; declarations — DAoCEd's <c>CreateFontAction</c> from the
/// useful side. The rule under test throughout: &lt;Height&gt; is
/// the LINE height, and the font size follows from the TTF's own metrics.
/// </summary>
public class FontCatalogTests
{
    private const string TwoFonts = """
        <Interface>
          <TTFFont><Name>toka10</Name><File>fonts/toka.ttf</File><Height>10</Height></TTFFont>
          <TTFFont><Name>toka14</Name><File>fonts/toka.ttf</File><Height>14</Height></TTFFont>
          <LabelTemplate><Name>plain</Name>
            <Font><Name>toka10</Name></Font>
          </LabelTemplate>
          <WindowTemplate>
            <Name>w</Name><Width>100</Width><Height>50</Height>
            <LabelDef><FontName>toka10</FontName><Label>hi</Label></LabelDef>
          </WindowTemplate>
        </Interface>
        """;

    private static Package Pkg(string xml = TwoFonts) => PackageLoader.Load(
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(xml),
        });

    private static XDocument Doc(Package pkg) => pkg.Docs["windows.xml"];

    // ---------------------------------------------------------------
    // The list
    // ---------------------------------------------------------------

    [Fact]
    public void All_ListsTheDeclarationsWithTheirLineHeights()
    {
        var rows = FontCatalog.All(Pkg());

        Assert.Equal(new[] { "toka10", "toka14" }, rows.Select(f => f.Name));
        Assert.Equal(10, rows[0].Height);
        Assert.Equal(14, rows[1].Height);
    }

    [Fact]
    public void UseCounts_CountsBothSpellings()
    {
        /* An element carries <FontName> and a template a nested <Font> block
           with a <Name> in it, and the renderer reads both. Counting only one
           would call a font used by half the package "unused". */
        var uses = FontCatalog.UseCounts(Pkg());

        Assert.Equal(2, uses["toka10"]);          // the LabelDef and the template
        Assert.False(uses.ContainsKey("toka14"));
    }

    [Fact]
    public void SizePx_IsTheHeight_AndTheLineAdvanceFollowsFromTheTtf()
    {
        /* THE rule. <Height> is the font size; the line advance is
           what the TTF says a line is worth at that size. Without a readable
           TTF the fallback of 1.3 em applies. The panel shows both numbers,
           and both have to be the ones FontProvider draws with. */
        var entry = new FontEntry("x", "x.ttf", 13, 0, 0);

        Assert.Equal(13, entry.SizePx);
        Assert.Equal(16.9, entry.LinePx);
        Assert.False(entry.Resolved);

        // With metrics the TTF decides the advance.
        var known = new FontEntry("x", "x.ttf", 13, 1.0, 0);
        Assert.Equal(13, known.SizePx);
        Assert.Equal(13, known.LinePx);
        Assert.True(known.Resolved);
    }

    // ---------------------------------------------------------------
    // Creating
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WritesTheDeclarationAndRegistersIt()
    {
        var pkg = Pkg();

        var made = FontCatalog.Create(pkg, Doc(pkg), "toka16", "fonts/toka.ttf", 16);

        Assert.Equal("toka16", made.Name);
        Assert.Equal(16, made.Height);
        Assert.True(pkg.Fonts.ContainsKey("toka16"));

        // In the tree, and the file marked for saving.
        var node = FontCatalog.NodeOf(pkg, "toka16");
        Assert.NotNull(node);
        Assert.Equal("16", Xml.Tx(node, "Height"));
        Assert.Contains("windows.xml", pkg.DirtyFiles);
    }

    [Fact]
    public void Create_RefusesATakenName_CaseInsensitively()
    {
        /* The same fix as for templates and textures: the check has to ask the
           table the answer will come out of. pkg.Fonts is case-insensitive, so
           "TOKA10" beside "toka10" would take its place. */
        var pkg = Pkg();

        var ex = Assert.Throws<ArgumentException>(
            () => FontCatalog.Create(pkg, Doc(pkg), "TOKA10", "fonts/x.ttf", 10));
        Assert.Contains("already declared", ex.Message);
    }

    [Fact]
    public void Create_RefusesAnImpossibleLineHeight()
    {
        var pkg = Pkg();

        Assert.Throws<ArgumentException>(
            () => FontCatalog.Create(pkg, Doc(pkg), "tiny", "fonts/x.ttf", 2));
        Assert.Throws<ArgumentException>(
            () => FontCatalog.Create(pkg, Doc(pkg), "huge", "fonts/x.ttf", 900));
    }

    [Fact]
    public void Create_WritesTheHeightWithTheInvariantCulture()
    {
        /* The port's own trap: on a German system 10.5.ToString()
           gives "10,5", which RenderMath.Num reads back as 10. Going through
           XmlEdit.SetSub is what keeps the number a number. */
        var pkg = Pkg();

        FontCatalog.Create(pkg, Doc(pkg), "half", "fonts/x.ttf", 10.5);

        Assert.Equal("10.5", Xml.Tx(FontCatalog.NodeOf(pkg, "half"), "Height"));
    }

    // ---------------------------------------------------------------
    // Changing
    // ---------------------------------------------------------------

    [Fact]
    public void Edit_CarriesEveryReferenceWithARename()
    {
        /* DAoCEd renames the declaration alone, and every label that named it
           then falls back to a system face at a guessed size — a difference
           nobody would trace back to a rename. Both spellings have to follow. */
        var pkg = Pkg();

        var (now, renamed) = FontCatalog.Edit(pkg, "toka10", "body", "fonts/toka.ttf", 10);

        Assert.Equal("body", now.Name);
        Assert.Equal(2, renamed);
        Assert.False(pkg.Fonts.ContainsKey("toka10"));

        var uses = FontCatalog.UseCounts(pkg);
        Assert.Equal(2, uses["body"]);
        Assert.False(uses.ContainsKey("toka10"));
    }

    [Fact]
    public void Edit_ChangesTheLineHeightWithoutTouchingTheReferences()
    {
        var pkg = Pkg();

        var (now, renamed) = FontCatalog.Edit(pkg, "toka10", "toka10", "fonts/toka.ttf", 12);

        Assert.Equal(0, renamed);
        Assert.Equal(12, now.Height);
        Assert.Equal(12, pkg.Fonts["toka10"].Height);
    }

    [Fact]
    public void Edit_RefusesToTakeAnotherFontsName()
    {
        var pkg = Pkg();

        Assert.Throws<ArgumentException>(
            () => FontCatalog.Edit(pkg, "toka10", "toka14", "fonts/toka.ttf", 10));

        // Its own name is not "taken", though — changing only the height must work.
        FontCatalog.Edit(pkg, "toka10", "toka10", "fonts/toka.ttf", 11);
    }

    // ---------------------------------------------------------------
    // Removing
    // ---------------------------------------------------------------

    [Fact]
    public void Delete_TakesTheDeclarationAndLeavesTheReferences()
    {
        /* Same call as a texture: an element naming an undeclared font falls
           back rather than vanishing, and rewriting a hundred labels is a
           change nobody asked for and no undo can take back. The count comes
           back so it can be said. */
        var pkg = Pkg();

        int dangling = FontCatalog.Delete(pkg, "toka10");

        Assert.Equal(2, dangling);
        Assert.False(pkg.Fonts.ContainsKey("toka10"));
        Assert.Null(FontCatalog.NodeOf(pkg, "toka10"));
        Assert.Equal(2, FontCatalog.UseCounts(pkg)["toka10"]);   // still named
    }

    [Fact]
    public void Delete_RefusesAFontThatIsNotThere()
    {
        Assert.Throws<ArgumentException>(() => FontCatalog.Delete(Pkg(), "nothing_like_it"));
    }

    [Fact]
    public void NodeOf_DoesNotConfuseADeclarationWithAReference()
    {
        // <Font><Name>toka10</Name></Font> inside a template is a reference,
        // not a declaration. Only <TTFFont> declares.
        var pkg = Pkg();

        var node = FontCatalog.NodeOf(pkg, "toka10");

        Assert.NotNull(node);
        Assert.Equal("TTFFont", node!.Name.LocalName);
    }
}
