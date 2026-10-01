using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The pick lists behind the "…" buttons of the property editor.
/// </summary>
public class ChoiceListTests
{
    private static Package PackageWithTemplates()
    {
        var pkg = new Package();

        void Add(string type, string name)
        {
            if (!pkg.Templates.TryGetValue(type, out var byName))
                pkg.Templates[type] = byName = new Dictionary<string, XElement>(StringComparer.Ordinal);
            byName[name] = new XElement(type, new XElement("Name", name));
            pkg.ByNameLc[name] = byName[name];
        }

        Add("ButtonTemplate", "zeta_button");
        Add("ButtonTemplate", "alpha_button");
        Add("FullResizeImageTemplate", "mid_frame");
        return pkg;
    }

    // -----------------------------------------------------------------
    // Templates
    // -----------------------------------------------------------------

    /// <summary>
    /// Every type has to be in there. The rule is that templates
    /// are collected generically — a list that knew only about a few types
    /// would leave the rest unpickable.
    /// </summary>
    [Fact]
    public void Templates_lists_every_type()
    {
        var list = ChoiceList.Templates(PackageWithTemplates());

        Assert.Contains(list, c => c.Value == "zeta_button");
        Assert.Contains(list, c => c.Value == "alpha_button");
        Assert.Contains(list, c => c.Value == "mid_frame");
    }

    [Fact]
    public void Templates_shortens_the_type_to_a_readable_hint()
    {
        var list = ChoiceList.Templates(PackageWithTemplates());

        Assert.Equal("Button", list.First(c => c.Value == "alpha_button").Hint);
        Assert.Equal("FullResizeImage", list.First(c => c.Value == "mid_frame").Hint);
    }

    /// <summary>
    /// "none" is a legitimate value of the format, not a fault — the element is
    /// then deliberately without a template. It has to be pickable, otherwise
    /// the only way to set it is by hand.
    /// </summary>
    [Fact]
    public void Templates_start_with_the_empty_entry_and_none()
    {
        var list = ChoiceList.Templates(PackageWithTemplates());

        Assert.Equal("", list[0].Value);
        Assert.Equal("none", list[1].Value);
    }

    [Fact]
    public void Templates_are_sorted_by_name_across_types()
    {
        var names = ChoiceList.Templates(PackageWithTemplates())
            .Skip(2).Select(c => c.Value).ToList();

        Assert.Equal(new[] { "alpha_button", "mid_frame", "zeta_button" }, names);
    }

    /// <summary>
    /// The list behind "+ Template": each type once, with the full tag as the
    /// value, because that is what is written into the file.
    /// </summary>
    [Fact]
    public void TemplateTypes_offers_each_type_of_the_package_once()
    {
        var list = ChoiceList.TemplateTypes(PackageWithTemplates());

        Assert.Equal(new[] { "ButtonTemplate", "FullResizeImageTemplate" },
            list.Select(c => c.Value).ToArray());
        Assert.Equal(new[] { "Button", "FullResizeImage" },
            list.Select(c => c.Label).ToArray());
    }

    /// <summary>
    /// How many of each — the number is the difference between "this is the
    /// type this package uses for its buttons" and a name out of a list.
    /// </summary>
    [Fact]
    public void TemplateTypes_says_how_many_of_each_are_in_the_package()
    {
        var list = ChoiceList.TemplateTypes(PackageWithTemplates());

        Assert.Equal("ButtonTemplate · 2", list.First(c => c.Value == "ButtonTemplate").Hint);
    }

    /// <summary>
    /// No empty entry, unlike the other lists: a template without a type is not
    /// a thing the format has, and picking one would create a node the loader
    /// then drops.
    /// </summary>
    [Fact]
    public void TemplateTypes_offers_no_empty_entry()
    {
        Assert.DoesNotContain(ChoiceList.TemplateTypes(PackageWithTemplates()), c => c.Value.Length == 0);
    }

    // -----------------------------------------------------------------
    // Fonts
    // -----------------------------------------------------------------

    [Fact]
    public void Fonts_are_listed_with_their_line_height()
    {
        var pkg = new Package();
        pkg.Fonts["MyFont"] = new FontRef { File = "my.ttf", Height = 12 };

        var list = ChoiceList.Fonts(pkg);

        Assert.Equal("", list[0].Value);
        Assert.Equal("MyFont", list[1].Value);
        Assert.Contains("12", list[1].Hint);
    }

    // -----------------------------------------------------------------
    // Adapters and events (reference data)
    // -----------------------------------------------------------------

    [Fact]
    public void Adapters_come_from_the_reference_data_and_are_free_of_duplicates()
    {
        var list = ChoiceList.Adapters();

        Assert.True(list.Count > 100, $"only {list.Count} adapters");
        Assert.Equal("", list[0].Value);

        var values = list.Skip(1).Select(c => c.Value).ToList();
        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// A scalar adapter carrying a maximum shows both, because "20/100" is
    /// what says the bar will come out a fifth full.
    /// </summary>
    [Fact]
    public void Adapters_show_value_and_maximum_together()
    {
        var data = DaocUiForge.Core.Reference.ReferenceData.Default;
        string? withMax = data.Current.Keys.FirstOrDefault(k => data.Max.ContainsKey(k));
        Assert.NotNull(withMax);

        var choice = ChoiceList.Adapters().First(c => c.Value == withMax);

        Assert.Contains("/", choice.Hint);
    }

    [Fact]
    public void Events_come_from_the_reference_data_with_a_description()
    {
        var list = ChoiceList.Events();

        Assert.True(list.Count > 20, $"only {list.Count} events");
        Assert.Equal("", list[0].Value);
        Assert.Contains(list, c => c.Hint.Length > 0);
    }

    /// <summary>
    /// The DAoCEd descriptions are HTML. In a desktop label a literal
    /// "&lt;br&gt;" is simply wrong text.
    /// </summary>
    [Fact]
    public void Event_descriptions_carry_no_html_breaks()
    {
        Assert.DoesNotContain(ChoiceList.Events(),
            c => c.Hint.Contains("<br", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("one two", ChoiceList.Plain("one<br>two"));
        Assert.Equal("one two", ChoiceList.Plain("one<BR/>two"));
    }

    // -----------------------------------------------------------------
    // Filtering
    // -----------------------------------------------------------------

    [Fact]
    public void Filter_matches_the_label_case_insensitively()
    {
        var items = new[]
        {
            new Choice("alpha_button", "alpha_button"),
            new Choice("mid_frame", "mid_frame"),
        };

        Assert.Single(ChoiceList.Filter(items, "BUTTON"));
        Assert.Equal(2, ChoiceList.Filter(items, "").Count());
        Assert.Equal(2, ChoiceList.Filter(items, null).Count());
        Assert.Empty(ChoiceList.Filter(items, "nothing"));
    }
}
