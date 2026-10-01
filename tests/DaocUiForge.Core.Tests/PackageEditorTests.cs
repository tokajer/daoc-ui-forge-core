using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>Renaming and removing whole windows.</summary>
public class PackageEditorTests
{
    /// <summary>
    /// Two windows in one file plus one in another — the shape that makes the
    /// dirty marking on delete visible at all.
    /// </summary>
    private static Package TwoFiles()
    {
        var pkg = new Package();

        var docA = XDocument.Parse("""
            <Interface>
              <WindowTemplate><Name>chat</Name><WindowId>17</WindowId></WindowTemplate>
              <WindowTemplate><Name>combat</Name></WindowTemplate>
            </Interface>
            """);
        var docB = XDocument.Parse("""
            <Interface>
              <WindowTemplate><Name>compass</Name></WindowTemplate>
            </Interface>
            """);

        void Add(XDocument doc, string file, string id) =>
            pkg.Windows.Add(new WindowDef
            {
                Id = id,
                Name = id,
                File = file,
                Doc = doc,
                Node = doc.Root!.Elements("WindowTemplate")
                    .First(w => Xml.NameOf(w) == id),
            });

        Add(docA, "a.xml", "chat");
        Add(docA, "a.xml", "combat");
        Add(docB, "b.xml", "compass");
        return pkg;
    }

    private static WindowDef Pick(Package pkg, string id) => pkg.Windows.First(w => w.Id == id);

    // -----------------------------------------------------------------
    // Renaming
    // -----------------------------------------------------------------

    [Fact]
    public void Rename_writes_the_tag_and_keeps_the_model_in_step()
    {
        var pkg = TwoFiles();
        var win = Pick(pkg, "chat");

        Assert.True(PackageEditor.Rename(win, "chat_wide"));

        Assert.Equal("chat_wide", win.Id);
        Assert.Equal("chat_wide", Xml.NameOf(win.Node));
        Assert.True(win.Dirty);
    }

    /// <summary>
    /// The loader drops windows without an id. Letting an empty
    /// name through would make the window vanish on the next load — silently,
    /// and only then.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rename_refuses_an_empty_name(string? id)
    {
        var pkg = TwoFiles();
        var win = Pick(pkg, "chat");

        Assert.False(PackageEditor.Rename(win, id));

        Assert.Equal("chat", win.Id);
        Assert.Equal("chat", Xml.NameOf(win.Node));
        Assert.False(win.Dirty);
    }

    [Fact]
    public void Rename_trims_the_name()
    {
        var pkg = TwoFiles();

        PackageEditor.Rename(Pick(pkg, "chat"), "  spaced  ");

        Assert.Equal("spaced", Xml.NameOf(Pick(pkg, "spaced").Node));
    }

    // -----------------------------------------------------------------
    // Deleting
    // -----------------------------------------------------------------

    [Fact]
    public void DeleteWindow_removes_it_from_the_list_and_from_the_tree()
    {
        var pkg = TwoFiles();
        var win = Pick(pkg, "chat");
        var doc = win.Doc;

        PackageEditor.DeleteWindow(pkg, win);

        Assert.DoesNotContain(pkg.Windows, w => w.Id == "chat");
        Assert.Single(doc.Root!.Elements("WindowTemplate"));
    }

    /// <summary>
    /// The windows of the same file share one document. That file has to be
    /// written out again even though nothing inside those windows changed —
    /// otherwise the deletion is simply never saved.
    /// </summary>
    [Fact]
    public void DeleteWindow_marks_the_other_windows_of_the_same_file_dirty()
    {
        var pkg = TwoFiles();

        PackageEditor.DeleteWindow(pkg, Pick(pkg, "chat"));

        Assert.True(Pick(pkg, "combat").Dirty);      // same file
        Assert.False(Pick(pkg, "compass").Dirty);    // a different one
    }

    // -----------------------------------------------------------------
    // Creating a window
    // -----------------------------------------------------------------

    /// <summary>
    /// The shape of a real package as far as this needs it: an include list in
    /// assets.xml, one window in a file of its own, one background template.
    /// </summary>
    private static Package Loaded(string includeHost = "assets.xml")
    {
        var pkg = new Package();

        void File(string key, string text)
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            pkg.Docs[key] = doc;
            pkg.Files[key] = System.Text.Encoding.UTF8.GetBytes(text);
            pkg.OriginalPaths[key] = key;

            foreach (var w in doc.Root!.Elements("WindowTemplate"))
                pkg.Windows.Add(new WindowDef
                {
                    Id = Xml.NameOf(w), Name = Xml.NameOf(w), File = key, Doc = doc, Node = w,
                });

            foreach (var t in doc.Descendants("FullResizeImageTemplate"))
            {
                if (!pkg.Templates.TryGetValue("FullResizeImageTemplate", out var byName))
                    pkg.Templates["FullResizeImageTemplate"] = byName = new();
                byName[Xml.NameOf(t)] = t;
                pkg.ByNameLc[Xml.NameOf(t)] = t;
            }
        }

        File("uimain.xml", """
            <XML ID="DAOCUi">
                <Include>chat_window.xml</Include>
                <Include>assets.xml</Include>
            </XML>
            """);

        File(includeHost, $"""
            <Root_Element ID="DAOCUi">
                <FullResizeImageTemplate>
                    <Name>g_window_background</Name>
                </FullResizeImageTemplate>
                <Include>custom0_window.xml</Include>
            </Root_Element>
            """);

        File("chat_window.xml", """
            <Root_Element ID="DAOCUi">
                <WindowTemplate>
                    <Name>chat_window</Name>
                </WindowTemplate>
            </Root_Element>
            """);

        File("custom0_window.xml", """
            <Root_Element ID="DAOCUi">
                <WindowTemplate>
                    <Name>custom0_window</Name>
                </WindowTemplate>
            </Root_Element>
            """);

        return pkg;
    }

    [Fact]
    public void FreeCustomNames_leaves_out_the_ones_in_use()
    {
        var free = PackageEditor.FreeCustomNames(Loaded());

        Assert.Equal(PackageEditor.MaxCustomWindows - 1, free.Count);
        Assert.DoesNotContain("custom0_window", free);
        Assert.Equal("custom1_window", free[0]);
        Assert.Equal("custom19_window", free[^1]);
    }

    /// <summary>
    /// A file with no window in it still occupies the name: the client loads
    /// by file name, so creating a second one there would write over it.
    /// </summary>
    [Fact]
    public void FreeCustomNames_counts_a_file_without_a_window()
    {
        var pkg = Loaded();
        pkg.Files["custom4_window.xml"] = Array.Empty<byte>();

        Assert.DoesNotContain("custom4_window", PackageEditor.FreeCustomNames(pkg));
    }

    [Fact]
    public void CreateWindow_writes_the_fields_DAoCEd_writes()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200);

        Assert.Equal("custom5_window", Xml.NameOf(win.Node));
        Assert.Equal("custom5_window", Xml.Tx(win.Node, "WindowId"));
        Assert.Equal("300", Xml.Tx(win.Node, "Width"));
        Assert.Equal("200", Xml.Tx(win.Node, "Height"));
        Assert.Equal("300", Xml.Tx(win.Node, "TitleWidth"));
        Assert.Equal("16", Xml.Tx(win.Node, "TitleHeight"));
        Assert.Equal("false", Xml.Tx(win.Node, "CloseButton"));
        Assert.Equal("0", Xml.Tx(win.Node, "MinWidth"));
        Assert.Null(Xml.Sub(win.Node, "FullResizeImageDef"));
    }

    [Fact]
    public void CreateWindow_puts_the_background_in_at_the_full_size()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200, "g_window_background");
        var bg = Xml.Sub(win.Node, "FullResizeImageDef")!;

        Assert.Equal("g_window_background", Xml.Tx(bg, "TemplateName"));
        Assert.Equal("300", Xml.Tx(bg, "Width"));
        Assert.Equal("200", Xml.Tx(bg, "Height"));
        Assert.Equal("0", Xml.Tx(Xml.Sub(bg, "Position"), "X"));
    }

    [Fact]
    public void CreateWindow_registers_the_file_in_the_model()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200);

        Assert.Equal("custom5_window.xml", win.File);
        Assert.Same(win.Doc, pkg.Docs["custom5_window.xml"]);
        Assert.Equal("custom5_window.xml", pkg.OriginalPaths["custom5_window.xml"]);
        Assert.Contains(win, pkg.Windows);
        Assert.Contains("custom5_window.xml", PackageWriter.ChangedFiles(pkg));
    }

    /// <summary>
    /// A window the editor shows and the game never loads is worse than no
    /// window at all — nothing on screen says why it is missing.
    /// </summary>
    [Fact]
    public void CreateWindow_announces_the_file_in_the_include_list()
    {
        var pkg = Loaded();

        PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200);

        var includes = pkg.Docs["assets.xml"].Descendants("Include").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "custom0_window.xml", "custom5_window.xml" }, includes);
        Assert.Contains("assets.xml", PackageWriter.ChangedFiles(pkg));
    }

    /// <summary>
    /// The patch overwrites uimain.xml, so an include written there
    /// lasts until the next patch and no longer. DAoCEd names assets.xml for
    /// the same reason.
    /// </summary>
    [Fact]
    public void IncludeHost_never_answers_uimain()
    {
        var pkg = Loaded();
        pkg.Docs.Remove("assets.xml");

        Assert.Null(PackageEditor.IncludeHost(pkg));
    }

    [Fact]
    public void IncludeHost_falls_back_to_the_fullest_list()
    {
        var pkg = Loaded(includeHost: "custom_includes.xml");

        Assert.Equal("custom_includes.xml", PackageEditor.IncludeHost(pkg));
    }

    /// <summary>
    /// Without a list to write into the window is still created. DAoCEd throws
    /// the whole thing away instead, which loses work over a package layout it
    /// did not expect.
    /// </summary>
    [Fact]
    public void CreateWindow_works_without_an_include_list()
    {
        var pkg = Loaded();
        foreach (var e in pkg.Docs["assets.xml"].Descendants("Include").ToList()) e.Remove();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200);

        Assert.Contains(win, pkg.Windows);
        Assert.False(PackageEditor.RegisterInclude(pkg, "custom5_window"));
    }

    [Theory]
    [InlineData("custom0_window")]   // a window of the package
    [InlineData("CUSTOM0_WINDOW")]   // the same name in another case
    [InlineData("chat_window")]
    public void CreateWindow_refuses_a_name_that_is_taken(string name)
    {
        var pkg = Loaded();

        Assert.Throws<InvalidOperationException>(() => PackageEditor.CreateWindow(pkg, name, 10, 10));
        Assert.Equal(2, pkg.Windows.Count);
    }

    [Fact]
    public void CreateWindow_refuses_an_empty_name()
    {
        var pkg = Loaded();

        Assert.Throws<ArgumentException>(() => PackageEditor.CreateWindow(pkg, "  ", 10, 10));
    }

    /// <summary>
    /// Built in code, so the tree carries no whitespace at all until it is laid
    /// out — without that the whole window arrives as one line.
    /// </summary>
    [Fact]
    public void CreateWindow_lays_the_file_out_like_the_package()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200, "g_window_background");
        string text = PackageWriter.SerializeText(win.Doc);

        Assert.Contains("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<Root_Element ID=\"DAOCUi\">", text);
        Assert.Contains("\n    <WindowTemplate>\n        <Name>custom5_window</Name>", text);
        Assert.Contains("\n        <FullResizeImageDef>\n            <Position>\n                <X>0</X>", text);
        Assert.EndsWith("\n    </WindowTemplate>\n</Root_Element>", text);
    }

    /// <summary>
    /// DAoCEd writes its tag line into every file it saves. Here only a
    /// created file can carry one — the rest keep their own bytes — and it goes
    /// on a line of its own above the window, not onto the root's opening tag.
    /// </summary>
    [Fact]
    public void CreateWindow_puts_the_tag_line_above_the_window()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(
            pkg, "custom5_window", 10, 10, null, "Edited with DAoC UI Forge");

        Assert.Contains(
            "<Root_Element ID=\"DAOCUi\">\n    <!-- Edited with DAoC UI Forge -->\n    <WindowTemplate>",
            PackageWriter.SerializeText(win.Doc));
    }

    /// <summary>XML forbids "--" inside a comment, and XComment throws over it.</summary>
    [Fact]
    public void CreateWindow_survives_a_tag_line_with_two_hyphens()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 10, 10, null, "a -- b");

        Assert.Contains("<!-- a - b -->", PackageWriter.SerializeText(win.Doc));
    }

    [Fact]
    public void CreateWindow_writes_no_comment_without_a_tag_line()
    {
        var pkg = Loaded();

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 10, 10, null, "   ");

        Assert.DoesNotContain("<!--", PackageWriter.SerializeText(win.Doc));
    }

    /// <summary>
    /// The step is read off the package, not assumed: a file indented with
    /// tabs beside one indented with spaces is a style change in every diff
    /// from then on.
    /// </summary>
    [Fact]
    public void CreateWindow_takes_the_indentation_step_from_the_package()
    {
        var pkg = new Package();
        var doc = XDocument.Parse("<Root_Element>\n\t<WindowTemplate>\n\t\t<Name>a</Name>\n\t</WindowTemplate>\n</Root_Element>",
            LoadOptions.PreserveWhitespace);
        pkg.Docs["a.xml"] = doc;
        pkg.Windows.Add(new WindowDef
        {
            Id = "a", Name = "a", File = "a.xml", Doc = doc,
            Node = doc.Root!.Element("WindowTemplate")!,
        });

        var win = PackageEditor.CreateWindow(pkg, "custom0_window", 10, 10);

        Assert.Contains("\n\t<WindowTemplate>\n\t\t<Name>custom0_window</Name>", PackageWriter.SerializeText(win.Doc));
    }

    /// <summary>
    /// The new file has no bytes behind it, so an export that walked
    /// <see cref="Package.Files"/> alone would leave the window out of the
    /// package it just went into.
    /// </summary>
    [Fact]
    public void CreateWindow_reaches_a_full_export()
    {
        var pkg = Loaded();

        PackageEditor.CreateWindow(pkg, "custom5_window", 300, 200);

        var files = PackageWriter.CurrentFiles(pkg);
        Assert.Contains("custom5_window.xml", files.Keys);
        Assert.Contains("<Name>custom5_window</Name>",
            System.Text.Encoding.UTF8.GetString(files["custom5_window.xml"]));
    }

    /// <summary>
    /// The file goes beside the package's other XML, not one level down: the
    /// loader takes only the smallest path depth.
    /// </summary>
    [Fact]
    public void CreateWindow_puts_the_file_where_the_loader_will_find_it()
    {
        var pkg = Loaded();
        foreach (string key in pkg.Docs.Keys.ToList())
        {
            pkg.Docs["ui/" + key] = pkg.Docs[key];
            pkg.OriginalPaths["ui/" + key] = "UI/" + key;
            pkg.Docs.Remove(key);
        }
        foreach (var w in pkg.Windows.ToList()) pkg.Windows.Remove(w);

        var win = PackageEditor.CreateWindow(pkg, "custom5_window", 10, 10);

        Assert.Equal("ui/custom5_window.xml", win.File);
        Assert.Equal("UI/custom5_window.xml", pkg.OriginalPaths[win.File]);
    }
}
