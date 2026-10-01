using System.Text;
using System.Xml;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The nested blocks of a TabsDef: what DAoCEd edits in its TabEditor and
/// ControlSchema, covering leaf tags only, never reached.
/// </summary>
public class TabEditorTests
{
    /// <summary>
    /// A window shaped like the reference package: the tabs in front of the
    /// assignments, both inside the TabsDef, and two elements whose visibility
    /// depends on them.
    /// </summary>
    private static XElement Window(string xml = Sample) =>
        XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root!;

    private const string Sample = """
        <WindowTemplate>
            <Name>w</Name>
            <Width>200</Width>
            <Height>100</Height>
            <TabsDef>
                <TemplateName>generic_tabs</TemplateName>
                <ControlId>1002</ControlId>
                <Width>190</Width>
                <Height>90</Height>
                <Tab>
                    <Id>1</Id>
                    <Name>News</Name>
                </Tab>
                <Tab>
                    <Id>2</Id>
                    <Name>Guild</Name>
                </Tab>
                <TabControl>
                    <TabId>1</TabId>
                    <ControlId>1100</ControlId>
                </TabControl>
                <TabControl>
                    <TabId>2</TabId>
                    <ControlId>1101</ControlId>
                </TabControl>
            </TabsDef>
            <LabelDef>
                <ControlId>1100</ControlId>
                <Data>on news</Data>
            </LabelDef>
            <LabelDef>
                <ControlId>1101</ControlId>
                <Data>on guild</Data>
            </LabelDef>
        </WindowTemplate>
        """;

    private static XElement Tabs(XElement window) => window.Element("TabsDef")!;

    /// <summary>
    /// Serialised without letting the writer touch the line breaks. The default
    /// rewrites every one of them to Environment.NewLine, which passes on Linux
    /// and fails on Windows.
    /// </summary>
    private static string Flat(XElement el)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings)) el.WriteTo(writer);
        return sb.ToString();
    }

    // -----------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------

    [Fact]
    public void Tabs_and_assignments_are_read_apart()
    {
        var def = Tabs(Window());

        Assert.Equal(new[] { "1", "2" }, TabEditor.Tabs(def).Select(TabEditor.IdOf));
        Assert.Equal(new[] { "News", "Guild" }, TabEditor.Tabs(def).Select(TabEditor.NameOf));
        Assert.Equal(new[] { "1100", "1101" }, TabEditor.Members(def).Select(TabEditor.ControlIdOf));
    }

    [Fact]
    public void Only_a_TabsDef_carries_tabs()
    {
        var window = Window();
        Assert.True(TabEditor.IsTabs(Tabs(window)));
        Assert.False(TabEditor.IsTabs(window.Elements("LabelDef").First()));
    }

    // -----------------------------------------------------------------
    // Adding
    // -----------------------------------------------------------------

    [Fact]
    public void A_new_tab_lands_behind_the_last_one_and_before_the_assignments()
    {
        var def = Tabs(Window());
        TabEditor.AddTab(def, "Alliance");

        var names = def.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(
            new[] { "TemplateName", "ControlId", "Width", "Height", "Tab", "Tab", "Tab", "TabControl", "TabControl" },
            names);
        Assert.Equal("Alliance", TabEditor.NameOf(TabEditor.Tabs(def)[2]));
    }

    [Fact]
    public void A_new_tab_is_laid_out_like_the_file_it_joins()
    {
        var def = Tabs(Window());
        TabEditor.AddTab(def, "Alliance");

        Assert.Contains(
            "\n        <Tab>\n            <Id>3</Id>\n            <Name>Alliance</Name>\n        </Tab>",
            Flat(def));
    }

    /// <summary>
    /// DAoCEd numbers a new tab getSize()+1, which hands out an id that is
    /// already taken as soon as a tab in the middle has been deleted.
    /// </summary>
    [Fact]
    public void A_new_tab_gets_an_id_no_other_tab_has()
    {
        var def = Tabs(Window());
        TabEditor.RemoveTab(def, TabEditor.Tabs(def)[0]);

        Assert.Equal("3", TabEditor.IdOf(TabEditor.AddTab(def)));
    }

    [Fact]
    public void The_first_tab_of_a_TabsDef_without_any_gets_id_1()
    {
        var def = Tabs(Window());
        foreach (var tab in TabEditor.Tabs(def)) TabEditor.RemoveTab(def, tab);

        var fresh = TabEditor.AddTab(def);
        Assert.Equal("1", TabEditor.IdOf(fresh));

        // Behind the last leaf tag, not behind a TabControl: the packages keep
        // the tabs in front of the assignments.
        Assert.Equal("Height", fresh.ElementsBeforeSelf().Last().Name.LocalName);
    }

    [Fact]
    public void A_new_assignment_lands_behind_the_last_one()
    {
        var def = Tabs(Window());
        var fresh = TabEditor.AddMember(def, "2", "1102");

        Assert.Same(def.Elements().Last(), fresh);
        Assert.Equal("2", TabEditor.TabIdOf(fresh));
        Assert.Equal("1102", TabEditor.ControlIdOf(fresh));
        Assert.Contains(
            "\n        <TabControl>\n            <TabId>2</TabId>\n            <ControlId>1102</ControlId>\n        </TabControl>",
            Flat(def));
    }

    // -----------------------------------------------------------------
    // The assignments follow the tab
    // -----------------------------------------------------------------

    /// <summary>
    /// Deviation from DAoCEd, which leaves TabControl alone. An assignment
    /// naming an id no longer in the list does not mean "always visible": the
    /// renderer hides an element whose tab ids exclude the active tab, and no
    /// tab can ever be the missing one.
    /// </summary>
    [Fact]
    public void Renumbering_a_tab_carries_its_assignments_along()
    {
        var def = Tabs(Window());
        TabEditor.SetId(def, TabEditor.Tabs(def)[0], "7");

        Assert.Equal(new[] { "7", "2" }, TabEditor.Members(def).Select(TabEditor.TabIdOf));
    }

    [Fact]
    public void Renumbering_leaves_the_assignments_of_the_other_tabs_alone()
    {
        var def = Tabs(Window());
        TabEditor.SetId(def, TabEditor.Tabs(def)[1], "9");

        Assert.Equal(new[] { "1", "9" }, TabEditor.Members(def).Select(TabEditor.TabIdOf));
    }

    [Fact]
    public void Removing_a_tab_removes_the_assignments_that_named_it()
    {
        var def = Tabs(Window());
        TabEditor.RemoveTab(def, TabEditor.Tabs(def)[0]);

        Assert.Equal(new[] { "2" }, TabEditor.Tabs(def).Select(TabEditor.IdOf));
        Assert.Equal(new[] { "1101" }, TabEditor.Members(def).Select(TabEditor.ControlIdOf));
    }

    /// <summary>
    /// The point of removing them: the element goes back to being drawn on
    /// every tab instead of disappearing from the window.
    /// </summary>
    [Fact]
    public void An_element_of_a_removed_tab_is_drawn_again()
    {
        var (pkg, win) = Loaded();
        var def = win.Node.Element("TabsDef")!;

        Assert.Equal(new[] { "1002", "1100" }, Drawn(pkg, win));

        TabEditor.RemoveTab(def, TabEditor.Tabs(def)[0]);

        Assert.Equal(new[] { "1002", "1100", "1101" }, Drawn(pkg, win));
    }

    /// <summary>
    /// The counter-check, and the reason for the deviation: taking the tab
    /// alone leaves an assignment naming an id no tab has, and the element it
    /// names is gone from every tab of the window.
    /// </summary>
    [Fact]
    public void A_tab_removed_without_its_assignments_hides_its_elements_for_good()
    {
        var (pkg, win) = Loaded();
        var def = win.Node.Element("TabsDef")!;

        TabEditor.Tabs(def)[0].Remove();

        Assert.DoesNotContain("1100", Drawn(pkg, win));
    }

    private static (Package Pkg, WindowDef Win) Loaded()
    {
        var pkg = PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["w.xml"] = Encoding.UTF8.GetBytes("<Interface>" + Sample + "</Interface>"),
        });
        return (pkg, pkg.Windows.Single());
    }

    /// <summary>The control ids the drawing pass let through.</summary>
    private static string[] Drawn(Package pkg, WindowDef win)
    {
        using var ctx = new RenderContext(pkg, new RenderOptions());
        var rendered = WindowRenderer.Render(ctx, win);
        return rendered.Elements.Select(e => Xml.Tx(e.Def, "ControlId")).ToArray();
    }

    [Fact]
    public void Removing_an_assignment_takes_its_indentation_with_it()
    {
        var def = Tabs(Window());
        TabEditor.RemoveMember(TabEditor.Members(def)[0]);

        Assert.DoesNotContain("1100", Flat(def));
        Assert.Contains("</Tab>\n        <TabControl>", Flat(def));
    }

    // -----------------------------------------------------------------
    // The pick list
    // -----------------------------------------------------------------

    [Fact]
    public void The_control_list_offers_the_elements_that_have_an_id()
    {
        var window = Window();
        window.Add(new XElement("LabelDef", new XElement("Data", "no id")));

        var values = ChoiceList.Controls(window).Select(c => c.Value).ToList();

        Assert.Equal(new[] { "", "1002", "1100", "1101" }, values);
        Assert.Contains("Label", ChoiceList.Controls(window).First(c => c.Value == "1100").Hint);
    }

    /// <summary>Sorted as numbers: 20 belongs behind 3, not in front of it.</summary>
    [Fact]
    public void The_control_list_sorts_the_ids_as_numbers()
    {
        var window = Window();
        window.Add(new XElement("LabelDef", new XElement("ControlId", "20")));
        window.Add(new XElement("LabelDef", new XElement("ControlId", "3")));

        var values = ChoiceList.Controls(window).Select(c => c.Value).ToList();
        Assert.Equal(new[] { "", "3", "20", "1002", "1100", "1101" }, values);
    }

    // -----------------------------------------------------------------
    // Moving an assignment to another tab
    // -----------------------------------------------------------------

    [Fact]
    public void An_assignment_moves_to_another_tab_in_one_step()
    {
        /* DAoCEd has no such action: moving an element from one tab to another
           there is a delete on one list and an add on the other, with the
           control id typed a second time in between. And the first step on its
           own leaves the element visible everywhere, so an interruption between
           the two silently changes the window. */
        var def = Tabs(Window());
        var member = TabEditor.Members(def)[0];

        Assert.True(TabEditor.MoveMember(member, "2"));

        Assert.Equal("2", TabEditor.TabIdOf(member));
        Assert.Equal("1100", TabEditor.ControlIdOf(member));
        // One block still, not a delete plus an add.
        Assert.Equal(2, TabEditor.Members(def).Count);
    }

    [Fact]
    public void Moving_an_assignment_to_the_tab_it_is_on_changes_nothing()
    {
        var def = Tabs(Window());
        var member = TabEditor.Members(def)[0];
        string before = Flat(def);

        Assert.False(TabEditor.MoveMember(member, "1"));
        Assert.Equal(before, Flat(def));
    }

    [Fact]
    public void The_moved_element_is_drawn_on_its_new_tab_and_not_on_the_old_one()
    {
        // The check that matters: the file says one thing and the drawing pass
        // has to agree with it, since an assignment is only ever visible
        // through what it hides.
        var (pkg, win) = Loaded();
        var def = win.Node.Element("TabsDef")!;

        TabEditor.MoveMember(TabEditor.Members(def)[0], "2");

        Assert.DoesNotContain("1100", Drawn(pkg, win));            // tab 1 is the active one
        Assert.Contains("1100", DrawnOn(pkg, win, "2"));
    }

    [Fact]
    public void AlreadyOn_finds_a_twin_before_one_is_made()
    {
        /* Moving an assignment onto a tab that already carries the same control
           would leave two blocks saying one thing — noise in the file, and two
           rows in the editor that cannot be told apart. */
        var def = Tabs(Window());

        Assert.False(TabEditor.AlreadyOn(def, "2", "1100"));

        TabEditor.MoveMember(TabEditor.Members(def)[0], "2");
        Assert.True(TabEditor.AlreadyOn(def, "2", "1100"));
    }

    /// <summary>The control ids drawn with a given tab active.</summary>
    private static string[] DrawnOn(Package pkg, WindowDef win, string tab)
    {
        using var ctx = new RenderContext(pkg, new RenderOptions { ActiveTab = tab });
        var rendered = WindowRenderer.Render(ctx, win);
        return rendered.Elements.Select(e => Xml.Tx(e.Def, "ControlId")).ToArray();
    }
}
