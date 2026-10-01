using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DaocUiForge.Core.Localization;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The second language. English is the key
/// (<see cref="Strings"/>), so what has to be checked is the catalogue: that it
/// loads, that it answers, and above all that its placeholders line up with the
/// code's — a <c>{2}</c> in a sentence the code hands two values to is a crash
/// on a button press, in one language only, on somebody else's machine.
/// </summary>
[Collection("language")]
public class StringsTests : IDisposable
{
    public StringsTests() => Strings.Use(Strings.Default);

    /// <summary>
    /// Back to English afterwards. The catalogue is process-wide, and a test
    /// that left it on German would change what every other test reads.
    /// </summary>
    public void Dispose() => Strings.Use(Strings.Default);

    [Fact]
    public void English_hands_back_the_key_itself()
    {
        Strings.Use("en");

        Assert.Equal("en", Strings.Current);
        Assert.Equal("Open folder…", Strings.T("Open folder…"));
        // Including a sentence no catalogue will ever hold.
        Assert.Equal("Nothing translates this", Strings.T("Nothing translates this"));
    }

    [Fact]
    public void German_answers_and_falls_back_to_English()
    {
        Strings.Use("de");

        Assert.Equal("de", Strings.Current);
        Assert.Equal("Ordner öffnen…", Strings.T("Open folder…"));
        Assert.Equal("Nothing translates this", Strings.T("Nothing translates this"));
    }

    [Fact]
    public void Placeholders_are_filled_in_either_language()
    {
        Strings.Use("en");
        Assert.Equal("Saved: w.xml", Strings.T("Saved: {0}", "w.xml"));

        Strings.Use("de");
        Assert.Equal("Gespeichert: w.xml", Strings.T("Saved: {0}", "w.xml"));
    }

    [Fact]
    public void Numbers_stay_invariant_in_German()
    {
        /* Deliberate: every number in this editor is a number in the XML, and
           the XML is invariant. A field that showed "10,5" and
           wrote 10 would be the decimal separator trap with a translation layer
           over it. */
        Strings.Use("de");

        Assert.Equal("10.5 px", Strings.T("{0:0.#} px", 10.5));
    }

    [Fact]
    public void An_unknown_code_and_a_full_culture_name_both_land_somewhere_sensible()
    {
        Assert.Equal("de", Strings.Normalise("de-AT"));
        Assert.Equal("en", Strings.Normalise("fr"));
        Assert.Equal("en", Strings.Normalise("nonsense"));
    }

    [Fact]
    public void Changed_fires_when_the_language_really_changes()
    {
        int fired = 0;
        void Count(object? s, EventArgs e) => fired++;

        Strings.Changed += Count;
        try
        {
            Strings.Use("de");
            Strings.Use("de");        // no change, no event
            Strings.Use("en");
        }
        finally
        {
            Strings.Changed -= Count;
        }

        Assert.Equal(2, fired);
    }

    // ---------------------------------------------------------------
    // The catalogue itself
    // ---------------------------------------------------------------

    /// <summary>The area files of a language: resource name → its table.</summary>
    private static Dictionary<string, Dictionary<string, string>> Areas(string code)
    {
        var assembly = typeof(Strings).Assembly;
        string prefix = $"DaocUiForge.Core.Localization.{code}.";

        var areas = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                        && n.EndsWith(".json", StringComparison.Ordinal))
            .ToDictionary(
                n => n[prefix.Length..],
                n =>
                {
                    using var stream = assembly.GetManifestResourceStream(n)!;
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
                });

        Assert.NotEmpty(areas);
        return areas;
    }

    /// <summary>All of them merged, the way <see cref="Strings.Use"/> reads them.</summary>
    private static Dictionary<string, string> Catalogue(string code)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in Areas(code).Values)
            foreach (var (key, value) in table)
                all[key] = value;
        return all;
    }

    /// <summary>Which placeholders a format string uses, e.g. {0}, {1:0.#}.</summary>
    private static SortedSet<int> Holes(string text)
    {
        var found = new SortedSet<int>();
        foreach (Match m in Regex.Matches(text, @"(?<!\{)\{(\d+)[^{}]*\}"))
            found.Add(int.Parse(m.Groups[1].Value));
        return found;
    }

    [Fact]
    public void No_German_string_asks_for_a_value_the_code_does_not_hand_over()
    {
        /* The rule is one-sided, and that is the point.
           <see cref="Strings.T"/> passes every value the English sentence has
           holes for, so a translation using <b>fewer</b> is harmless — German
           has no "{0} time{1}" to build, it just says "{0}-mal". Using
           <b>more</b> throws a FormatException on a button press, in one
           language only, on somebody else's machine. */
        var bad = new List<string>();

        foreach (var (english, german) in Catalogue("de"))
            if (!Holes(german).IsSubsetOf(Holes(english)))
                bad.Add($"“{english}” -> “{german}”");

        Assert.Empty(bad);
    }

    [Fact]
    public void A_sentence_the_catalogue_does_not_hold_is_still_formatted()
    {
        /* The guard behind the rule above. A key with no translation goes
           through the English text — placeholders and all — rather than coming
           out raw. */
        Strings.Use("de");

        Assert.Equal("Nothing translates this: 7",
            Strings.T("Nothing translates this: {0}", 7));
    }

    [Fact]
    public void No_German_string_is_left_empty_or_identical_by_accident()
    {
        // Empty would be worse than English — the button would have no label.
        // A dropped entry falls back, so blanks must not be in the file at all.
        Assert.DoesNotContain(Catalogue("de"), e => e.Value.Length == 0);
    }

    [Fact]
    public void The_catalogue_covers_what_the_code_asks_for()
    {
        /* A sample rather than a sweep over the sources: the point is that the
           catalogue is really wired to the code, and there is one string here
           out of every area file, so an area that stopped being embedded, or
           was renamed and left out of the build, is a red test rather than a
           pane that quietly reverts to English. */
        Strings.Use("de");

        foreach (string key in new[]
                 {
                     "Open folder…",            // main: the toolbar
                     "A texture needs a name.", // catalogs: Core, thrown and shown
                     "Line advance",            // catalogs: the font pane
                     "Label indent",            // properties: the property editor
                     "MISSING TEMPLATES",       // report: the inspection report
                     "Game folder",             // settings: the settings dialog
                     "Properties",              // common: more than one pane says it
                 })
            Assert.NotEqual(key, Strings.T(key));
    }

    [Fact]
    public void Every_string_lives_in_exactly_one_area()
    {
        /* The catalogue is split per area so a third language stays workable
           (see Strings.Read). The split is only worth anything while it is a
           partition: a key in two files is two translations of one sentence,
           of which the interface shows whichever file was read last, and the
           other is edited forever with no effect. A string more than one pane
           uses belongs in common.json. */
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var twice = new List<string>();

        foreach (var (area, table) in Areas("de").OrderBy(a => a.Key, StringComparer.Ordinal))
            foreach (string key in table.Keys)
                if (!seen.TryAdd(key, area))
                    twice.Add($"“{key}” in {seen[key]} and {area}");

        Assert.Empty(twice);
    }

    [Fact]
    public void No_key_carries_an_escape_the_code_never_asks_for()
    {
        /* The catalogue was first scraped out of the sources, and fourteen
           entries kept the escaped SOURCE form of their key: "…\\n" as a
           backslash and an n, where the code hands T() a line break. Those keys
           can never be hit, so the fourteen sentences were shown in English
           while the file said they were translated: the one failure the
           English-as-key design cannot make visible. */
        foreach (var (key, value) in Catalogue("de"))
        {
            Assert.DoesNotContain("\\n", key, StringComparison.Ordinal);
            Assert.DoesNotContain("\\\"", key, StringComparison.Ordinal);
            Assert.DoesNotContain("\\n", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Languages_lists_English_first_and_names_each_in_itself()
    {
        // Nobody looks for "German" in a language list.
        Assert.Equal("en", Strings.Languages[0].Code);
        Assert.Contains(Strings.Languages, l => l.Code == "de" && l.Name == "Deutsch");
    }
}
