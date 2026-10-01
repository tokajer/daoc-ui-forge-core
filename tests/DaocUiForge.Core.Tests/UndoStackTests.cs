using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// Undo over a whole package. The design rests on two promises, and both are
/// pinned here: a document reports every change of its own subtree, so no
/// action can slip past the recorder; and the text of a document parses back
/// into the same tree, so restoring one cannot half-work.
/// </summary>
public class UndoStackTests
{
    private const string WindowsXml = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <Interface>
          <WindowTemplate>
            <Name>alpha</Name>
            <Width>200</Width>
            <LabelDef><Data>A</Data><Position><X>10</X><Y>20</Y></Position></LabelDef>
            <LabelDef><Data>B</Data></LabelDef>
          </WindowTemplate>
        </Interface>
        """;

    private const string AssetsXml = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <Interface>
          <Include>windows.xml</Include>
          <ButtonTemplate><Name>btn</Name><Height>10</Height></ButtonTemplate>
        </Interface>
        """;

    private static int Includes(Package pkg) =>
        pkg.Docs["assets.xml"].Descendants().Count(e => e.Name.LocalName == "Include");

    private static Package Load() => PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["windows.xml"] = Encoding.UTF8.GetBytes(WindowsXml),
        ["assets.xml"] = Encoding.UTF8.GetBytes(AssetsXml),
    });

    private static (Package Pkg, UndoStack Undo) Watched()
    {
        var pkg = Load();
        var undo = new UndoStack();
        undo.Watch(pkg);
        return (pkg, undo);
    }

    private static XElement Label(Package pkg, string data) =>
        ElementEditor.ElementsOf(pkg.Windows.Single(w => w.Id == "alpha").Node)
            .First(e => Xml.Tx(e, "Data") == data);

    private static string Text(Package pkg, string key) =>
        Encoding.UTF8.GetString(PackageWriter.SerializeFile(pkg, key));

    // -----------------------------------------------------------------
    // The promise the whole design rests on
    // -----------------------------------------------------------------

    [Fact]
    public void A_change_deep_in_a_document_is_noticed_without_being_declared()
    {
        var (pkg, undo) = Watched();
        Assert.False(undo.CanUndo);

        undo.Begin("Nudge");
        ElementEditor.Nudge(Label(pkg, "A"), 5, 0);

        // Nothing told the stack which file that was.
        Assert.True(undo.CanUndo);
        Assert.Equal("Nudge", undo.UndoLabel);
    }

    [Fact]
    public void A_step_opens_by_itself_and_is_named_once_the_action_is_over()
    {
        // The route the interface uses: its editors report an edit after
        // making it, so there is nothing to call beforehand.
        var (pkg, undo) = Watched();
        string before = Text(pkg, "windows.xml");

        ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        undo.Close("Move");
        ElementEditor.Nudge(Label(pkg, "B"), 1, 0);
        undo.Close("Move again");

        Assert.Equal("Move again", undo.UndoLabel);
        undo.Undo();
        Assert.Equal("Move", undo.UndoLabel);
        undo.Undo();

        Assert.Equal(before, Text(pkg, "windows.xml"));
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Closing_with_nothing_changed_names_nothing()
    {
        var (_, undo) = Watched();

        undo.Close("An action that did nothing");

        Assert.False(undo.CanUndo);
        Assert.Null(undo.UndoLabel);
    }

    [Fact]
    public void An_action_that_changed_nothing_leaves_no_step()
    {
        var (_, undo) = Watched();

        undo.Begin("Cancelled");
        undo.Begin("Also cancelled");

        Assert.False(undo.CanUndo);
        Assert.Null(undo.UndoLabel);
    }

    // -----------------------------------------------------------------
    // Undo and redo
    // -----------------------------------------------------------------

    [Fact]
    public void Undo_puts_the_file_back_exactly_and_redo_takes_it_forward_again()
    {
        var (pkg, undo) = Watched();
        string before = Text(pkg, "windows.xml");

        undo.Begin("Nudge");
        ElementEditor.Nudge(Label(pkg, "A"), 5, 7);
        string after = Text(pkg, "windows.xml");
        Assert.NotEqual(before, after);

        Assert.True(undo.Undo());
        Assert.Equal(before, Text(pkg, "windows.xml"));

        Assert.True(undo.Redo());
        Assert.Equal(after, Text(pkg, "windows.xml"));
    }

    [Fact]
    public void Undo_reaches_a_deletion_and_the_element_is_back_where_it_was()
    {
        var (pkg, undo) = Watched();
        string before = Text(pkg, "windows.xml");

        undo.Begin("Delete");
        ElementEditor.Delete(Label(pkg, "A"));
        Assert.Single(ElementEditor.ElementsOf(pkg.Windows.Single(w => w.Id == "alpha").Node));

        Assert.True(undo.Undo());

        Assert.Equal(2, ElementEditor.ElementsOf(pkg.Windows.Single(w => w.Id == "alpha").Node).Count());
        Assert.Equal(before, Text(pkg, "windows.xml"));   // the indentation too
    }

    [Fact]
    public void The_tables_are_rebuilt_so_the_model_and_the_tree_cannot_disagree()
    {
        var (pkg, undo) = Watched();

        undo.Begin("Rename the template");
        XmlEdit.SetSub(pkg.ByNameLc["btn"], "renamed", "Name");
        PackageLoader.Reindex(pkg);
        Assert.True(pkg.ByNameLc.ContainsKey("renamed"));

        Assert.True(undo.Undo());

        Assert.True(pkg.ByNameLc.ContainsKey("btn"));
        Assert.False(pkg.ByNameLc.ContainsKey("renamed"));

        // And the element the table hands out is one of the live tree, not of
        // the tree that was thrown away.
        Assert.Same(pkg.Docs["assets.xml"], pkg.ByNameLc["btn"].Document);
    }

    [Fact]
    public void A_window_selected_before_the_undo_is_a_different_object_afterwards()
    {
        // The price of the design, pinned so nobody relies on the opposite:
        // whoever holds a WindowDef or an XElement has to fetch it again.
        var (pkg, undo) = Watched();
        var win = pkg.Windows.Single(w => w.Id == "alpha");

        undo.Begin("Nudge");
        ElementEditor.Nudge(Label(pkg, "A"), 1, 1);
        undo.Undo();

        Assert.NotSame(win, pkg.Windows.Single(w => w.Id == "alpha"));
    }

    [Fact]
    public void Steps_are_taken_back_one_at_a_time_in_order()
    {
        var (pkg, undo) = Watched();

        undo.Begin("One");
        ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        undo.Begin("Two");
        ElementEditor.Nudge(Label(pkg, "B"), 2, 0);

        Assert.Equal("Two", undo.UndoLabel);
        undo.Undo();
        Assert.Equal("One", undo.UndoLabel);
        undo.Undo();
        Assert.False(undo.CanUndo);

        Assert.Equal("", Xml.Tx(Label(pkg, "B"), "Position"));
        Assert.Equal("10", Xml.Tx(Xml.Sub(Label(pkg, "A"), "Position")!, "X"));
    }

    [Fact]
    public void A_new_step_throws_the_redo_away()
    {
        var (pkg, undo) = Watched();

        undo.Begin("One");
        ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        undo.Undo();
        Assert.True(undo.CanRedo);

        undo.Begin("Something else");
        ElementEditor.Nudge(Label(pkg, "B"), 1, 0);
        undo.Begin("Close it");

        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void The_step_still_open_is_undoable()
    {
        // The last action of a session is never followed by another Begin.
        var (pkg, undo) = Watched();
        string before = Text(pkg, "windows.xml");

        undo.Begin("Nudge");
        ElementEditor.Nudge(Label(pkg, "A"), 3, 3);

        Assert.True(undo.Undo());
        Assert.Equal(before, Text(pkg, "windows.xml"));
    }

    [Fact]
    public void Only_the_last_steps_are_kept()
    {
        var pkg = Load();
        var undo = new UndoStack { Depth = 2 };
        undo.Watch(pkg);

        for (int i = 1; i <= 4; i++)
        {
            undo.Begin("Step " + i);
            ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        }
        undo.Begin("Close the last one");

        Assert.True(undo.Undo());
        Assert.True(undo.Undo());
        Assert.False(undo.CanUndo);
    }

    // -----------------------------------------------------------------
    // Marks, files and whole documents
    // -----------------------------------------------------------------

    [Fact]
    public void Dirty_marks_come_back_with_the_change_that_set_them()
    {
        var (pkg, undo) = Watched();

        undo.Begin("Nudge");
        ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        pkg.Windows.Single(w => w.Id == "alpha").Dirty = true;
        Assert.NotEmpty(PackageWriter.ChangedFiles(pkg));

        undo.Undo();

        Assert.False(pkg.Windows.Single(w => w.Id == "alpha").Dirty);
        Assert.Empty(PackageWriter.ChangedFiles(pkg));
    }

    [Fact]
    public void A_created_window_is_undone_file_and_include_together()
    {
        var (pkg, undo) = Watched();
        int docs = pkg.Docs.Count;

        undo.Begin("New window");
        var win = PackageEditor.CreateWindow(pkg, "custom0_window", 100, 80);
        string key = win.File;

        Assert.True(pkg.Docs.ContainsKey(key));
        Assert.Equal(2, Includes(pkg));       // the file was announced

        Assert.True(undo.Undo());

        Assert.Equal(docs, pkg.Docs.Count);
        Assert.False(pkg.Docs.ContainsKey(key));
        Assert.False(pkg.OriginalPaths.ContainsKey(key));
        Assert.DoesNotContain(pkg.Windows, w => w.Id == "custom0_window");
        Assert.Equal(1, Includes(pkg));       // and the announcement is gone with it
        Assert.Empty(PackageWriter.PendingFiles(pkg));

        // And back again.
        Assert.True(undo.Redo());
        Assert.True(pkg.Docs.ContainsKey(key));
        Assert.Contains(pkg.Windows, w => w.Id == "custom0_window");
    }

    [Fact]
    public void An_imported_file_is_undone_when_its_key_was_captured()
    {
        var (pkg, undo) = Watched();

        undo.Begin("Import");
        undo.Capture("art/new.tga");
        pkg.Files["art/new.tga"] = new byte[] { 1, 2, 3 };
        pkg.OriginalPaths["art/new.tga"] = "art/new.tga";
        pkg.AddedFiles.Add("art/new.tga");

        Assert.True(undo.Undo());

        Assert.False(pkg.Files.ContainsKey("art/new.tga"));
        Assert.False(pkg.OriginalPaths.ContainsKey("art/new.tga"));
        Assert.Empty(pkg.AddedFiles);

        Assert.True(undo.Redo());
        Assert.True(pkg.Files.ContainsKey("art/new.tga"));
        Assert.Contains("art/new.tga", pkg.AddedFiles);
    }

    [Fact]
    public void A_replaced_file_comes_back_with_its_old_bytes()
    {
        var (pkg, undo) = Watched();
        pkg.Files["art/tex.tga"] = new byte[] { 9 };

        undo.Begin("Replace");
        undo.Capture("art/tex.tga");
        pkg.Files["art/tex.tga"] = new byte[] { 7, 7 };

        undo.Undo();

        Assert.Equal(new byte[] { 9 }, pkg.Files["art/tex.tga"]);
    }

    [Fact]
    public void One_step_covers_every_file_it_touched()
    {
        var (pkg, undo) = Watched();
        string windows = Text(pkg, "windows.xml");
        string assets = Text(pkg, "assets.xml");

        undo.Begin("Two files at once");
        ElementEditor.Nudge(Label(pkg, "A"), 4, 0);
        XmlEdit.SetSub(pkg.ByNameLc["btn"], "99", "Height");

        undo.Undo();

        Assert.Equal(windows, Text(pkg, "windows.xml"));
        Assert.Equal(assets, Text(pkg, "assets.xml"));
    }

    [Fact]
    public void Watching_another_package_forgets_the_stack()
    {
        var (pkg, undo) = Watched();

        undo.Begin("Nudge");
        ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        Assert.True(undo.CanUndo);

        undo.Watch(Load());

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void A_change_outside_any_step_is_still_taken_back()
    {
        // Nothing called Begin — the change belongs to no named action, and
        // losing it silently would be the worst outcome of a missed call site.
        var (pkg, undo) = Watched();
        string before = Text(pkg, "windows.xml");

        undo.Begin("The action before");
        ElementEditor.Nudge(Label(pkg, "A"), 1, 0);
        undo.Begin("An action that names itself");
        ElementEditor.Nudge(Label(pkg, "B"), 1, 0);

        Assert.True(undo.Undo());
        Assert.True(undo.Undo());
        Assert.Equal(before, Text(pkg, "windows.xml"));
    }
}
