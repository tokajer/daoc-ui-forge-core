using System.Text;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Inspection;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The inspection report. What matters is which of the four texture and
/// template cases end up in which section — a package where everything is
/// reported as broken is as useless as one where nothing is.
/// </summary>
public class InspectionReportTests
{
    /// <summary>
    /// One window with four elements, one per case: a template that exists, one
    /// that does not, the literal "none", and two textures — one undeclared,
    /// one declared but pointing into the game folder.
    /// </summary>
    private static Package Sample()
    {
        string xml = """
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <Root_Element ID="DAOCUi">
              <Texture>
                <Name>from_game</Name>
                <File>atlantis/emoticons.tga</File>
              </Texture>
              <ImageAreaTemplate>
                <Name>good_template</Name>
                <TextureName>in_package</TextureName>
                <Size><X>16</X><Y>16</Y></Size>
                <TopLeft><X>0</X><Y>0</Y></TopLeft>
              </ImageAreaTemplate>
              <WindowTemplate>
                <Name>demo</Name>
                <Width>100</Width>
                <Height>60</Height>
                <ImageAreaDef>
                  <ControlId>ok</ControlId>
                  <TemplateName>good_template</TemplateName>
                  <Position><X>0</X><Y>0</Y></Position>
                </ImageAreaDef>
                <ImageAreaDef>
                  <ControlId>broken</ControlId>
                  <TemplateName>no_such_template</TemplateName>
                  <Position><X>0</X><Y>10</Y></Position>
                </ImageAreaDef>
                <ImageAreaDef>
                  <ControlId>empty</ControlId>
                  <TemplateName>none</TemplateName>
                  <Position><X>0</X><Y>20</Y></Position>
                </ImageAreaDef>
                <ImageAreaDef>
                  <ControlId>external</ControlId>
                  <TemplateName>game_template</TemplateName>
                  <Position><X>0</X><Y>30</Y></Position>
                </ImageAreaDef>
              </WindowTemplate>
              <ImageAreaTemplate>
                <Name>game_template</Name>
                <TextureName>from_game</TextureName>
                <Size><X>16</X><Y>16</Y></Size>
                <TopLeft><X>0</X><Y>0</Y></TopLeft>
              </ImageAreaTemplate>
            </Root_Element>
            """;

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["ui.xml"] = Encoding.UTF8.GetBytes(xml),
        };
        return PackageLoader.Load(files);
    }

    [Fact]
    public void A_template_that_is_missing_is_named_with_the_window_that_wants_it()
    {
        var report = InspectionReport.Build(Sample());

        var missing = Assert.Single(report.MissingTemplates,
            t => t.Name == "no_such_template");
        Assert.Contains("demo", Assert.Single(missing.Users));
    }

    [Fact]
    public void A_template_that_exists_and_the_literal_none_are_not_faults()
    {
        var report = InspectionReport.Build(Sample());

        Assert.DoesNotContain(report.MissingTemplates, t => t.Name == "good_template");
        Assert.DoesNotContain(report.MissingTemplates,
            t => string.Equals(t.Name, "none", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// "none" means "no texture here". The renderer records it because the
    /// original's <c>slice</c> does not filter it either — the report must.
    /// </summary>
    [Fact]
    public void The_literal_none_never_reaches_the_texture_sections()
    {
        var pkg = Sample();
        var report = InspectionReport.Build(pkg);

        Assert.DoesNotContain("none", report.UndeclaredTextures,
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(report.ExternalTextures,
            t => string.Equals(t.Name, "none", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The distinction that matters to a user: an undeclared name is a fault in
    /// the package, a texture from the game folder is not — it merely cannot be
    /// previewed.
    /// </summary>
    [Fact]
    public void Undeclared_textures_and_game_folder_textures_are_kept_apart()
    {
        var report = InspectionReport.Build(Sample());

        Assert.Contains("in_package", report.UndeclaredTextures);
        Assert.DoesNotContain("from_game", report.UndeclaredTextures);

        var external = Assert.Single(report.ExternalTextures);
        Assert.Equal("from_game", external.Name);
        Assert.Equal("atlantis/emoticons.tga", external.Path);
    }

    [Fact]
    public void A_file_whose_bytes_contradict_its_declaration_is_reported()
    {
        // UTF-8 umlauts under encoding="ISO-8859-1" — what the original leaves
        // behind on every save.
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.xml"] = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n" +
                "<Root_Element><WindowTemplate><Name>Kanäle</Name></WindowTemplate></Root_Element>\n"),
        };
        var report = InspectionReport.Build(PackageLoader.Load(files));

        var m = Assert.Single(report.EncodingMismatches);
        Assert.Equal("a.xml", m.File);
        Assert.Equal("ISO-8859-1", m.Declared);
        Assert.Equal("utf-8", m.Actual);
    }

    /// <summary>
    /// Pure ASCII is the same under either encoding, so nothing can go wrong —
    /// reporting it would bury the real cases under a hundred false ones.
    /// </summary>
    [Fact]
    public void An_ascii_file_is_never_an_encoding_mismatch()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.xml"] = Encoding.ASCII.GetBytes(
                "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n" +
                "<Root_Element><WindowTemplate><Name>chat</Name></WindowTemplate></Root_Element>\n"),
        };
        var report = InspectionReport.Build(PackageLoader.Load(files));

        Assert.Empty(report.EncodingMismatches);
    }

    [Fact]
    public void A_package_without_faults_says_so()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.xml"] = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<Root_Element><WindowTemplate><Name>chat</Name>" +
                "<Width>10</Width><Height>10</Height></WindowTemplate></Root_Element>\n"),
        };
        var report = InspectionReport.Build(PackageLoader.Load(files));

        Assert.True(report.IsClean);
        Assert.Equal("No faults found.", report.Summary);
        Assert.Contains("No missing templates", report.ToText());
    }

    [Fact]
    public void The_text_names_every_section_that_has_something_in_it()
    {
        string text = InspectionReport.Build(Sample()).ToText();

        Assert.Contains("MISSING TEMPLATES", text);
        Assert.Contains("no_such_template  (1x)", text);
        Assert.Contains("UNDECLARED TEXTURES", text);
        Assert.Contains("TEXTURES FROM THE GAME FOLDER", text);
        Assert.Contains("from_game   ->  atlantis/emoticons.tga", text);

        // Sections without content stay out — an empty heading reads like a
        // fault that was not explained.
        Assert.DoesNotContain("CLIPPED WINDOWS", text);
        Assert.DoesNotContain("ELEMENTS THAT FAILED", text);
    }

    /// <summary>
    /// The deviation from the original: its report reads a set that drawing
    /// fills, so it only knows the windows somebody opened. Building the report
    /// on a package nothing has drawn yet has to find the textures anyway.
    /// </summary>
    [Fact]
    public void The_report_finds_textures_without_anything_being_drawn_first()
    {
        var pkg = Sample();
        Assert.Empty(pkg.MissingTextures);          // nothing rendered yet

        var report = InspectionReport.Build(pkg);
        Assert.NotEmpty(report.UndeclaredTextures);
    }

    // ---------------------------------------------------------------
    // The include chain (why this editor lists windows DAoCEd does not)
    // ---------------------------------------------------------------

    /// <summary>A package with a chain: uimain -> a.xml, and b.xml on its own.</summary>
    private static Package WithChain()
    {
        byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["uimain.xml"] = B("<XML ID=\"DAOCUi\"><Include>a.xml</Include></XML>"),
            ["a.xml"] = B("<Root_Element ID=\"DAOCUi\"><WindowTemplate><Name>reached</Name>" +
                          "<Width>10</Width><Height>10</Height></WindowTemplate></Root_Element>"),
            ["b.xml"] = B("<Root_Element ID=\"DAOCUi\"><WindowTemplate><Name>orphan</Name>" +
                          "<Width>10</Width><Height>10</Height></WindowTemplate></Root_Element>"),
        };
        return PackageLoader.Load(files);
    }

    [Fact]
    public void A_file_no_include_names_is_reported_with_the_windows_in_it()
    {
        var pkg = WithChain();

        var orphan = Assert.Single(InspectionReport.Build(pkg).UnreachableFiles);
        Assert.Equal("b.xml", orphan.Path);
        Assert.Equal(new[] { "orphan" }, orphan.Windows);
    }

    [Fact]
    public void The_root_and_what_it_includes_count_as_reached()
    {
        var reachable = IncludeChain.Reachable(WithChain());

        Assert.Contains("uimain.xml", reachable);
        Assert.Contains("a.xml", reachable);
        Assert.DoesNotContain("b.xml", reachable);
    }

    /// <summary>
    /// A package that ships only the files it changed has no uimain.xml, and
    /// calling every one of its files unreachable would be noise, not a
    /// finding.
    /// </summary>
    [Fact]
    public void Without_a_uimain_nothing_is_called_unreachable()
    {
        Assert.Empty(InspectionReport.Build(Sample()).UnreachableFiles);
    }

    /// <summary>An include chain may go deeper than one level, and may loop.</summary>
    [Fact]
    public void The_chain_is_followed_through_and_survives_a_cycle()
    {
        byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        var pkg = PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["uimain.xml"] = B("<XML ID=\"DAOCUi\"><Include>assets.xml</Include></XML>"),
            ["assets.xml"] = B("<XML ID=\"DAOCUi\"><Include>deep.xml</Include>" +
                               "<Include>uimain.xml</Include></XML>"),
            ["deep.xml"] = B("<Root_Element ID=\"DAOCUi\"><WindowTemplate><Name>deep</Name>" +
                             "<Width>10</Width><Height>10</Height></WindowTemplate></Root_Element>"),
            ["lost.xml"] = B("<Root_Element ID=\"DAOCUi\"><WindowTemplate><Name>lost</Name>" +
                             "<Width>10</Width><Height>10</Height></WindowTemplate></Root_Element>"),
        });

        var reachable = IncludeChain.Reachable(pkg);

        Assert.Contains("deep.xml", reachable);
        Assert.DoesNotContain("lost.xml", reachable);
    }
}
