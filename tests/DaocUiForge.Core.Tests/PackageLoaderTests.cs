using System.Text;
using DaocUiForge.Core.Ingest;
using Xunit;

namespace DaocUiForge.Tests;

public class PackageLoaderTests
{
    private static Dictionary<string, byte[]> Files(params (string path, string xml)[] entries)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, xml) in entries)
            d[path] = Encoding.UTF8.GetBytes(xml);
        return d;
    }

    [Fact]
    public void RootXml_IsLoaded_SubfolderVariants_AreSkipped()
    {
        // Root depth = 1. The NF variant (depth 2) has to be ignored.
        var pkg = PackageLoader.Load(Files(
            ("windows.xml",
             "<Interface><WindowTemplate><Name>root_win</Name></WindowTemplate></Interface>"),
            ("nf/windows.xml",
             "<Interface><WindowTemplate><Name>nf_win</Name></WindowTemplate></Interface>")
        ));

        Assert.Contains(pkg.Windows, w => w.Id == "root_win");
        Assert.DoesNotContain(pkg.Windows, w => w.Id == "nf_win");
    }

    [Fact]
    public void WindowId_IsNotUsedAsIdentity_NameWins()
    {
        var pkg = PackageLoader.Load(Files(
            ("ctx.xml",
             "<Interface>" +
             "<WindowTemplate><Name>alpha</Name><WindowId>7</WindowId></WindowTemplate>" +
             "<WindowTemplate><Name>beta</Name><WindowId>7</WindowId></WindowTemplate>" +
             "</Interface>")
        ));

        // Four identical WindowIds, different names → two windows kept here.
        Assert.Contains(pkg.Windows, w => w.Id == "alpha");
        Assert.Contains(pkg.Windows, w => w.Id == "beta");
    }

    [Fact]
    public void ShortNameTag_n_IsReadAsName()
    {
        // Some packages use <n> as the short form of <Name>.
        var pkg = PackageLoader.Load(Files(
            ("w.xml", "<Interface><WindowTemplate><n>kurz</n></WindowTemplate></Interface>")
        ));
        Assert.Contains(pkg.Windows, w => w.Id == "kurz");
    }

    [Fact]
    public void DuplicateTemplateName_LastLoadedWins()
    {
        // Both in the same file; the second definition has to win.
        var pkg = PackageLoader.Load(Files(
            ("t.xml",
             "<Interface>" +
             "<ButtonTemplate><Name>dup</Name><Height>10</Height></ButtonTemplate>" +
             "<ButtonTemplate><Name>dup</Name><Height>20</Height></ButtonTemplate>" +
             "</Interface>")
        ));

        var tpl = PackageLoader.FindTemplate(pkg, "dup");
        Assert.NotNull(tpl);
        Assert.Equal("20", DaocUiForge.Core.Model.Xml.Tx(tpl!, "Height"));
    }

    [Fact]
    public void Templates_AreCollectedGenerically_ByTemplateSuffix()
    {
        var pkg = PackageLoader.Load(Files(
            ("t.xml",
             "<Interface>" +
             "<ExoticWidgetTemplate><Name>weird</Name></ExoticWidgetTemplate>" +
             "</Interface>")
        ));
        Assert.NotNull(PackageLoader.FindTemplate(pkg, "weird"));
    }

    [Fact]
    public void Textures_AreCaseInsensitive()
    {
        var pkg = PackageLoader.Load(Files(
            ("t.xml",
             "<Interface><Texture><Name>MyTex</Name><File>art/mytex.tga</File></Texture></Interface>")
        ));
        Assert.True(pkg.Textures.ContainsKey("mytex"));
        Assert.Equal("art/mytex.tga", pkg.Textures["MYTEX"]);
    }

    [Theory]
    [InlineData("windows.xml", true)]
    [InlineData("art/button.tga", true)]
    [InlineData("./windows.xml", true)]      // a redundant segment, not metadata
    [InlineData(".git/config", false)]
    [InlineData(".git/objects/ab/cdef", false)]
    [InlineData(".gitignore", false)]
    [InlineData("art/.DS_Store", false)]
    [InlineData(".svn/entries", false)]
    [InlineData("..\\outside.tga", false)]   // a ZIP must not reach out of itself
    public void IsPackageFile_SkipsDotPrefixedSegments(string path, bool expected) =>
        Assert.Equal(expected, PackageLoader.IsPackageFile(path));

    [Fact]
    public void LoadFromDirectory_LeavesVersionControlMetadataOut()
    {
        // A package folder is very often a working copy. Everything under .git
        // used to land in Package.Files, and from there in a ZIP export.
        string dir = Path.Combine(Path.GetTempPath(), "daoc-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, ".git", "objects"));
        Directory.CreateDirectory(Path.Combine(dir, "art"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "windows.xml"),
                "<Interface><WindowTemplate><Name>real_win</Name></WindowTemplate></Interface>");
            File.WriteAllText(Path.Combine(dir, ".git", "config"), "[core]");
            File.WriteAllBytes(Path.Combine(dir, ".git", "objects", "blob"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(dir, "art", "button.tga"), new byte[] { 0 });

            var pkg = PackageLoader.LoadFromDirectory(dir);

            Assert.Contains(pkg.Windows, w => w.Id == "real_win");
            Assert.True(pkg.Files.ContainsKey("art/button.tga"));
            Assert.DoesNotContain(pkg.Files.Keys, k => k.StartsWith(".git", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadFromDirectory_LeavesTheNamedFoldersOut()
    {
        // A dependency tree in a package folder: 7,310 files in the reference
        // package, read in, listed in the file pane and written into a ZIP
        // export. Unlike the dot rule this one is a setting, so the test also
        // pins down that it can be turned off and pointed elsewhere.
        string dir = Path.Combine(Path.GetTempPath(), "daoc-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "picker", "node_modules", "lodash"));
        Directory.CreateDirectory(Path.Combine(dir, "art"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "windows.xml"),
                "<Interface><WindowTemplate><Name>real_win</Name></WindowTemplate></Interface>");
            File.WriteAllText(Path.Combine(dir, "picker", "node_modules", "lodash", "fp.js"), "x");
            File.WriteAllBytes(Path.Combine(dir, "art", "button.tga"), new byte[] { 0 });

            var pkg = PackageLoader.LoadFromDirectory(dir);
            Assert.Contains(pkg.Windows, w => w.Id == "real_win");
            Assert.True(pkg.Files.ContainsKey("art/button.tga"));
            Assert.DoesNotContain(pkg.Files.Keys, k => k.Contains("node_modules", StringComparison.Ordinal));

            // Empty means "load everything": the rule is the user's, not the format's.
            var all = PackageLoader.LoadFromDirectory(dir, Array.Empty<string>());
            Assert.Contains(all.Files.Keys, k => k.Contains("node_modules", StringComparison.Ordinal));

            // And it names folders, not paths: "picker" takes the tree with it.
            var named = PackageLoader.LoadFromDirectory(dir, new[] { "picker" });
            Assert.DoesNotContain(named.Files.Keys, k => k.StartsWith("picker/", StringComparison.Ordinal));
            Assert.True(named.Files.ContainsKey("art/button.tga"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_file_named_like_a_skipped_folder_is_still_loaded()
    {
        // Only folders count. A file called node_modules is a file, and losing
        // it would be the loader reading the list one segment too far.
        string dir = Path.Combine(Path.GetTempPath(), "daoc-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "windows.xml"), "<Interface/>");
            File.WriteAllText(Path.Combine(dir, "node_modules"), "not a folder");

            Assert.True(PackageLoader.LoadFromDirectory(dir).Files.ContainsKey("node_modules"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadFromZip_LeavesTheNamedFoldersOut()
    {
        // The same rule on the other way in, so a ZIP made of such a folder
        // cannot smuggle the tree back in.
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
                   ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "windows.xml",
                "<Interface><WindowTemplate><Name>real_win</Name></WindowTemplate></Interface>");
            Write(zip, "picker/node_modules/lodash/fp.js", "x");
        }
        ms.Position = 0;

        var pkg = PackageLoader.LoadFromZip(ms);

        Assert.Contains(pkg.Windows, w => w.Id == "real_win");
        Assert.DoesNotContain(pkg.Files.Keys, k => k.Contains("node_modules", StringComparison.Ordinal));

        static void Write(System.IO.Compression.ZipArchive zip, string name, string body)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(body));
        }
    }

    [Fact]
    public void LoadFromZip_LeavesVersionControlMetadataOut()
    {
        // The same rule on the other way in: a ZIP made of such a folder.
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
                   ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "windows.xml",
                "<Interface><WindowTemplate><Name>real_win</Name></WindowTemplate></Interface>");
            Write(zip, ".git/config", "[core]");
        }
        ms.Position = 0;

        var pkg = PackageLoader.LoadFromZip(ms);

        Assert.Contains(pkg.Windows, w => w.Id == "real_win");
        Assert.DoesNotContain(pkg.Files.Keys, k => k.StartsWith(".git", StringComparison.Ordinal));

        static void Write(System.IO.Compression.ZipArchive zip, string name, string body)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(body));
        }
    }
}
