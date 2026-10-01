using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// Writing a package back out. Two promises carry everything else: a file
/// nobody touched keeps its bytes, and a file that was touched changes only
/// where it was touched.
/// </summary>
public class PackageWriterTests
{
    private const string BaseXml =
        "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n" +
        "<Root_Element ID=\"DAOCUi\">\n" +
        "\t<WindowTemplate>\n" +
        "\t\t<Name>chat</Name>\n" +
        "\t\t<Position>\n" +
        "\t\t\t<X>10</X>\n" +
        "\t\t\t<Y>20</Y>\n" +
        "\t\t</Position>\n" +
        "\t\t<ImageDef>\n" +
        "\t\t\t<Name>backdrop</Name>\n" +
        "\t\t</ImageDef>\n" +
        "\t\t<ButtonDef>\n" +
        "\t\t\t<Name>send</Name>\n" +
        "\t\t</ButtonDef>\n" +
        "\t</WindowTemplate>\n" +
        "\t<WindowTemplate>\n" +
        "\t\t<Name>combat</Name>\n" +
        "\t</WindowTemplate>\n" +
        "</Root_Element>\n";

    private const string ExtraXml =
        "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n" +
        "<Root_Element ID=\"DAOCUi\">\n" +
        "\t<WindowTemplate>\n" +
        "\t\t<Name>compass</Name>\n" +
        "\t</WindowTemplate>\n" +
        "</Root_Element>\n";

    /// <summary>
    /// A package the way the loader builds one — through
    /// <see cref="PackageLoader.Load"/>, so that Docs, Files and OriginalPaths
    /// are filled the same way they are in the app. The mixed-case paths are
    /// there on purpose: the keys are lower-cased, the export must not be.
    /// </summary>
    private static Package Sample(string? baseXml = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["base.xml"] = Encoding.UTF8.GetBytes(baseXml ?? BaseXml),
            ["extra.xml"] = Encoding.UTF8.GetBytes(ExtraXml),
            ["assets/logo.tga"] = new byte[] { 1, 2, 3, 4 },
        };
        var original = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["base.xml"] = "Base.xml",
            ["extra.xml"] = "Extra.xml",
            ["assets/logo.tga"] = "Assets/Logo.tga",
        };
        return PackageLoader.Load(files, original);
    }

    private static WindowDef Pick(Package pkg, string id) => pkg.Windows.First(w => w.Id == id);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    // -----------------------------------------------------------------
    // Fidelity
    // -----------------------------------------------------------------

    [Fact]
    public void An_untouched_file_keeps_its_bytes()
    {
        var pkg = Sample();

        foreach (string file in new[] { "base.xml", "extra.xml" })
            Assert.Equal(pkg.Files[file], PackageWriter.SerializeFile(pkg, file));
    }

    /// <summary>
    /// The point of preserving the whitespace: a changed value shows up as one
    /// changed line, not as a rewritten file.
    /// </summary>
    [Fact]
    public void A_changed_value_changes_only_its_own_line()
    {
        var pkg = Sample();
        XmlEdit.SetSub(Pick(pkg, "chat").Node, 99.0, "Position", "X");

        var before = Text(pkg.Files["base.xml"]).Split('\n');
        var after = Text(PackageWriter.SerializeFile(pkg, "base.xml")).Split('\n');

        Assert.Equal(before.Length, after.Length);
        var changed = before.Where((line, i) => line != after[i]).ToList();
        Assert.Single(changed);
        Assert.Contains("<X>99</X>", after[5]);
    }

    [Fact]
    public void Tabs_and_indentation_survive()
    {
        var pkg = Sample();
        Assert.Contains("\n\t\t<Name>chat</Name>\n",
            Text(PackageWriter.SerializeFile(pkg, "base.xml")));
    }

    /// <summary>
    /// The line ending of a saved file must not depend on the machine that
    /// saves it.
    ///
    /// <para>Found by CI on Windows, where ten of these tests failed while all
    /// of them passed on Linux. <c>XNode.ToString</c> builds its writer settings
    /// from the save options and leaves <c>NewLineHandling</c> at
    /// <c>Replace</c>, whose <c>NewLineChars</c> default to
    /// <see cref="Environment.NewLine"/> — so every line break the loader
    /// preserved was rewritten to the local one, and an LF package saved on
    /// Windows came back with CRLF in all 105 files. The rule this pins down:
    /// <b>the source bytes decide the line ending, and nothing else gets a
    /// say.</b></para>
    /// </summary>
    [Fact]
    public void The_line_ending_comes_from_the_file_not_from_the_machine()
    {
        var pkg = Sample();   // written with "\n" throughout

        string text = Text(PackageWriter.SerializeFile(pkg, "base.xml"));
        Assert.DoesNotContain("\r", text);

        // And with a change in it, so the writing path is exercised too.
        XmlEdit.SetSub(Pick(pkg, "chat").Node, 99.0, "Position", "X");
        Assert.DoesNotContain("\r", Text(PackageWriter.SerializeFile(pkg, "base.xml")));
    }

    [Fact]
    public void Windows_line_endings_survive()
    {
        var pkg = Sample(BaseXml.Replace("\n", "\r\n"));
        string written = Text(PackageWriter.SerializeFile(pkg, "base.xml"));

        Assert.Equal(pkg.Files["base.xml"], PackageWriter.SerializeFile(pkg, "base.xml"));
        Assert.DoesNotContain(written.Replace("\r\n", ""), "\n");
    }

    // -----------------------------------------------------------------
    // Indentation: what structural edits do to the file
    // -----------------------------------------------------------------
    //
    // With the whitespace preserved, an element moved on its own leaves its
    // indentation behind and two elements end up on one line. Nothing of that
    // is visible in the editor — it shows up in the file, and then in every
    // diff from there on.

    /// <summary>The lines of the file, so a shape can be looked at directly.</summary>
    private static string[] Lines(Package pkg, string file) =>
        Text(PackageWriter.SerializeFile(pkg, file)).Split('\n');

    [Fact]
    public void A_new_element_lands_on_a_line_of_its_own()
    {
        var pkg = Sample();
        XmlEdit.SetSub(Pick(pkg, "combat").Node, 300.0, "Width");

        var lines = Lines(pkg, "base.xml");
        Assert.Contains("\t\t<Width>300</Width>", lines);
        Assert.Contains("\t</WindowTemplate>", lines);      // not glued to the new line
        Assert.Equal(Text(pkg.Files["base.xml"]).Split('\n').Length + 1, lines.Length);
    }

    /// <summary>The element definitions of the chat window, in drawing order.</summary>
    private static XElement Def(Package pkg, string name) =>
        Pick(pkg, "chat").Node.Elements()
            .First(e => e.Name.LocalName.EndsWith("Def", StringComparison.Ordinal)
                        && Xml.NameOf(e) == name);

    [Fact]
    public void Reordering_two_elements_swaps_their_lines_and_nothing_else()
    {
        var pkg = Sample();
        Assert.True(ElementEditor.MoveForward(Def(pkg, "backdrop")));

        var before = Text(pkg.Files["base.xml"]).Split('\n');
        var after = Lines(pkg, "base.xml");

        Assert.Equal(before.Length, after.Length);
        Assert.Contains("\t\t<ImageDef>", after);
        Assert.Contains("\t\t<ButtonDef>", after);

        // The button is now drawn first, so it stands further up in the file.
        Assert.True(Array.IndexOf(after, "\t\t<ButtonDef>")
                    < Array.IndexOf(after, "\t\t<ImageDef>"));
    }

    [Fact]
    public void A_duplicate_lands_on_its_own_line_too()
    {
        var pkg = Sample();
        ElementEditor.Duplicate(Def(pkg, "send"));

        var lines = Lines(pkg, "base.xml");
        Assert.Equal(2, lines.Count(l => l == "\t\t<ButtonDef>"));
        Assert.DoesNotContain(lines, l => l.Contains("</ButtonDef>\t\t<ButtonDef>"));
    }

    [Fact]
    public void A_deleted_element_takes_its_blank_line_with_it()
    {
        var pkg = Sample();
        PackageEditor.DeleteWindow(pkg, Pick(pkg, "combat"));

        var lines = Lines(pkg, "base.xml");
        Assert.DoesNotContain(lines, l => l.Trim().Length == 0 && l.Length > 0);
        Assert.Equal(
            Text(pkg.Files["base.xml"]).Split('\n').Length - 3,   // the three lines of the window
            lines.Length);
    }

    // -----------------------------------------------------------------
    // The declaration and the encoding
    // -----------------------------------------------------------------

    [Fact]
    public void A_file_without_a_declaration_gets_the_one_the_original_writes()
    {
        // serializeFile, line 3047 of the original.
        var doc = XDocument.Parse("<Root_Element><A>1</A></Root_Element>",
            LoadOptions.PreserveWhitespace);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n",
            PackageWriter.SerializeText(doc));
    }

    [Fact]
    public void A_declaration_without_an_encoding_stays_that_way()
    {
        // One file of the reference package is written like this
        // (skill_icons.xml), and a bare declaration means UTF-8.
        string xml = "<?xml version=\"1.0\"?>\n<XML ID=\"DAOCUi\">\n\t<A>ä</A>\n</XML>\n";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["icons.xml"] = Encoding.UTF8.GetBytes(xml),
        };
        var pkg = PackageLoader.Load(files);

        byte[] written = PackageWriter.SerializeFile(pkg, "icons.xml");
        Assert.Equal(pkg.Files["icons.xml"], written);
        Assert.StartsWith("<?xml version=\"1.0\"?>", Text(written));
    }

    /// <summary>
    /// The bytes beat the declaration. Real packages are full of UTF-8 under
    /// <c>encoding="ISO-8859-1"</c> — writing them "as declared" would rewrite
    /// every umlaut in a file the session never opened.
    /// </summary>
    [Fact]
    public void Utf8_under_a_latin1_declaration_stays_utf8()
    {
        var pkg = Sample(BaseXml.Replace("<Name>chat</Name>", "<Name>Kanäle</Name>"));

        byte[] written = PackageWriter.SerializeFile(pkg, "base.xml");
        Assert.Equal(pkg.Files["base.xml"], written);
        Assert.Contains("Kanäle", Encoding.UTF8.GetString(written));
    }

    [Fact]
    public void A_real_latin1_file_stays_latin1()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = Encoding.GetEncoding("iso-8859-1");

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.xml"] = latin1.GetBytes(BaseXml.Replace("<Name>chat</Name>", "<Name>Kanäle</Name>")),
        };
        var pkg = PackageLoader.Load(files);

        byte[] written = PackageWriter.SerializeFile(pkg, "a.xml");
        Assert.Equal(pkg.Files["a.xml"], written);
        Assert.Contains("Kanäle", latin1.GetString(written));
    }

    /// <summary>
    /// A character ISO-8859-1 cannot hold must not become a question mark. The
    /// file becomes UTF-8 — and says so, so that it does not contradict itself.
    /// </summary>
    [Fact]
    public void A_character_the_encoding_cannot_hold_makes_the_file_utf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = Encoding.GetEncoding("iso-8859-1");

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.xml"] = latin1.GetBytes(BaseXml.Replace("<Name>chat</Name>", "<Name>Kanäle</Name>")),
        };
        var pkg = PackageLoader.Load(files);

        // A Cyrillic name — outside Latin-1 in every direction.
        XmlEdit.SetSub(Pick(pkg, "Kanäle").Node, "Чат", "Name");

        string written = Encoding.UTF8.GetString(PackageWriter.SerializeFile(pkg, "a.xml"));
        Assert.Contains("<Name>Чат</Name>", written);   // and not "<Name>???</Name>"
        Assert.Contains("encoding=\"UTF-8\"", written);
    }

    // -----------------------------------------------------------------
    // Which files have changed
    // -----------------------------------------------------------------

    [Fact]
    public void Only_dirty_windows_name_their_file()
    {
        var pkg = Sample();
        Assert.Empty(PackageWriter.ChangedFiles(pkg));

        Pick(pkg, "combat").Dirty = true;
        Assert.Equal(new[] { "base.xml" }, PackageWriter.ChangedFiles(pkg));
    }

    /// <summary>
    /// The deviation from the original: after deleting the ONLY window of a
    /// file there is no dirty window left to name it, and the original's save
    /// list is built from dirty windows alone — the deletion never reaches the
    /// disk there.
    /// </summary>
    [Fact]
    public void Deleting_the_only_window_of_a_file_still_names_that_file()
    {
        var pkg = Sample();
        PackageEditor.DeleteWindow(pkg, Pick(pkg, "compass"));

        Assert.DoesNotContain(pkg.Windows, w => w.File == "extra.xml");
        Assert.Contains("extra.xml", PackageWriter.ChangedFiles(pkg));

        // And the deletion really is in the written file.
        Assert.DoesNotContain("compass", Text(PackageWriter.SerializeFile(pkg, "extra.xml")));
    }

    [Fact]
    public void Clearing_the_dirty_marks_only_touches_what_was_written()
    {
        var pkg = Sample();
        Pick(pkg, "chat").Dirty = true;
        Pick(pkg, "compass").Dirty = true;

        PackageWriter.ClearDirty(pkg, new[] { "base.xml" });

        Assert.False(Pick(pkg, "chat").Dirty);
        Assert.True(Pick(pkg, "compass").Dirty);
    }

    // -----------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------

    [Fact]
    public void Saving_the_changed_files_keeps_the_folder_structure_and_the_spelling()
    {
        var pkg = Sample();
        XmlEdit.SetSub(Pick(pkg, "chat").Node, 99.0, "Position", "X");
        Pick(pkg, "chat").Dirty = true;

        string dir = Path.Combine(Path.GetTempPath(), "forge-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = PackageWriter.SaveChanged(pkg, dir);

            Assert.Equal(new[] { "Base.xml" }, written);          // not "base.xml"
            Assert.True(File.Exists(Path.Combine(dir, "Base.xml")));
            Assert.False(File.Exists(Path.Combine(dir, "Extra.xml")));
            Assert.Contains("<X>99</X>", File.ReadAllText(Path.Combine(dir, "Base.xml")));
            Assert.False(Pick(pkg, "chat").Dirty);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // -----------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------

    [Fact]
    public void The_zip_holds_every_file_under_its_original_path()
    {
        var pkg = Sample();

        using var ms = new MemoryStream();
        PackageWriter.WriteZip(pkg, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal(
            new[] { "Assets/Logo.tga", "Base.xml", "Extra.xml" },
            zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// The proof that an export is usable: load it back and the change is
    /// there — no re-parsing of a file the writer mangled.
    /// </summary>
    [Fact]
    public void An_exported_zip_loads_back_with_the_change_in_it()
    {
        var pkg = Sample();
        XmlEdit.SetSub(Pick(pkg, "chat").Node, 99.0, "Position", "X");

        using var ms = new MemoryStream();
        PackageWriter.WriteZip(pkg, ms);
        ms.Position = 0;

        var again = PackageLoader.LoadFromZip(ms);
        Assert.Equal(3, again.Windows.Count);
        Assert.Equal(4, again.Files["assets/logo.tga"].Length);
        Assert.Equal("99", Xml.Tx(Xml.Sub(Pick(again, "chat").Node, "Position"), "X"));
    }

    [Fact]
    public void A_changed_only_zip_holds_nothing_else()
    {
        var pkg = Sample();
        Pick(pkg, "chat").Dirty = true;

        using var ms = new MemoryStream();
        PackageWriter.WriteZip(pkg, ms, changedOnly: true);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal(new[] { "Base.xml" }, zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void Saving_a_zip_clears_the_dirty_marks()
    {
        var pkg = Sample();
        Pick(pkg, "chat").Dirty = true;

        string path = Path.Combine(Path.GetTempPath(), "forge-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            PackageWriter.SaveZip(pkg, path);

            Assert.True(new FileInfo(path).Length > 0);
            Assert.False(Pick(pkg, "chat").Dirty);
            Assert.Empty(PackageWriter.ChangedFiles(pkg));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
