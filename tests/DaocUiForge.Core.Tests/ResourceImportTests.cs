using System.Text;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// Copying a file into the package — DAoCEd's <c>ImportFileAction</c>, and the
/// half of "bind a texture" that was missing.
/// </summary>
public class ResourceImportTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "daoc-import-" + Guid.NewGuid().ToString("N"));

    public ResourceImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Tga(int w, int h)
    {
        var buf = new byte[18 + w * h * 3];
        buf[2] = 2;
        buf[12] = (byte)(w & 0xff); buf[13] = (byte)(w >> 8);
        buf[14] = (byte)(h & 0xff); buf[15] = (byte)(h >> 8);
        buf[16] = 24;
        return buf;
    }

    private static byte[] SolidPng(int w, int h)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) c.Clear(new SKColor(0, 128, 255));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Package Pkg() => PackageLoader.Load(
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes("<Interface></Interface>"),
        });

    // ---------------------------------------------------------------
    // Adding
    // ---------------------------------------------------------------

    [Fact]
    public void Add_PutsTheBytesAndThePathIntoThePackage()
    {
        var pkg = Pkg();
        string source = Write("panel.tga", Tga(64, 64));

        var added = ResourceImport.Add(pkg, source, "custom/Assets/Textures/panel.tga");

        Assert.Equal("custom/assets/textures/panel.tga", added.Key);
        Assert.Equal("custom/Assets/Textures/panel.tga", added.Path);
        Assert.True(pkg.Files.ContainsKey(added.Key));
        // The spelling is kept, or an export would rename Assets/ to assets/.
        Assert.Equal("custom/Assets/Textures/panel.tga", pkg.PathOf(added.Key));
    }

    [Fact]
    public void Add_MarksTheFileAsUnsavedWork()
    {
        /* The one that decides whether the import survives. ChangedFiles
           answers with documents, and a binary has none — so without
           AddedFiles the file would ride along in a ZIP export and be missing
           from "save changes", which is the route people use when the package
           folder is the game's own. */
        var pkg = Pkg();
        string source = Write("panel.tga", Tga(64, 64));

        var added = ResourceImport.Add(pkg, source, "assets/panel.tga");

        Assert.DoesNotContain(added.Key, PackageWriter.ChangedFiles(pkg));
        Assert.Contains(added.Key, PackageWriter.PendingFiles(pkg));
    }

    [Fact]
    public void SaveChanged_WritesAnImportedBinaryOut()
    {
        var pkg = Pkg();
        string source = Write("panel.tga", Tga(32, 32));
        ResourceImport.Add(pkg, source, "assets/panel.tga");

        string target = Path.Combine(_dir, "out");
        var written = PackageWriter.SaveChanged(pkg, target);

        Assert.Contains("assets/panel.tga", written);
        Assert.True(File.Exists(Path.Combine(target, "assets", "panel.tga")));

        // And it stops counting as unsaved once it is on disk.
        Assert.Empty(PackageWriter.PendingFiles(pkg));
    }

    [Fact]
    public void Add_RefusesToOverwriteUnlessTold()
    {
        var pkg = Pkg();
        string source = Write("panel.tga", Tga(16, 16));
        ResourceImport.Add(pkg, source, "assets/panel.tga");

        var again = Assert.Throws<ArgumentException>(
            () => ResourceImport.Add(pkg, source, "assets/panel.tga"));
        Assert.Contains("already has a file", again.Message);

        string bigger = Write("bigger.tga", Tga(64, 64));
        var replaced = ResourceImport.Add(pkg, bigger, "assets/panel.tga", replace: true);

        Assert.True(replaced.Replaced);
        Assert.Equal(18 + 64 * 64 * 3, pkg.Files["assets/panel.tga"].Length);
    }

    [Fact]
    public void Add_RefusesAPathThatClimbsOutOfThePackage()
    {
        /* A "../" in the target would write outside the package on the next
           save — into the game folder, or anywhere else. */
        var pkg = Pkg();
        string source = Write("panel.tga", Tga(16, 16));

        var ex = Assert.Throws<ArgumentException>(
            () => ResourceImport.Add(pkg, source, "../../elsewhere/panel.tga"));
        Assert.Contains("out of the package", ex.Message);
    }

    [Fact]
    public void Add_RefusesAnEmptyPathAndAMissingSource()
    {
        var pkg = Pkg();
        string source = Write("panel.tga", Tga(16, 16));

        Assert.Throws<ArgumentException>(() => ResourceImport.Add(pkg, source, "   "));
        Assert.Throws<ArgumentException>(
            () => ResourceImport.Add(pkg, Path.Combine(_dir, "nothing.tga"), "assets/x.tga"));
    }

    // ---------------------------------------------------------------
    // Looking at it first (DAoCEd's Importer warnings)
    // ---------------------------------------------------------------

    [Fact]
    public void Inspect_SaysNothingAboutAWellFormedTga()
    {
        string source = Write("fine.tga", Tga(64, 64));
        Assert.Empty(ResourceImport.Inspect(source));
    }

    [Fact]
    public void Inspect_WarnsAboutASizeThatIsNotAPowerOfTwo()
    {
        // DAoCEd's ImageSizeWarning, and it is right: the client may not draw
        // such a texture properly.
        string source = Write("odd.tga", Tga(24, 40));

        var said = ResourceImport.Inspect(source);

        Assert.Single(said);
        Assert.False(said[0].Blocking);
        Assert.Contains("power of two", said[0].Message);
    }

    [Fact]
    public void Inspect_WarnsThatTheGameDoesNotReadPng()
    {
        // A warning, not a refusal: PNG is a perfectly good working file, and
        // the preview shows it. It just stays empty in the game.
        string source = Write("work.png", SolidPng(64, 64));

        var said = ResourceImport.Inspect(source);

        Assert.Single(said);
        Assert.False(said[0].Blocking);
        Assert.Contains("BMP, DDS and TGA", said[0].Message);
    }

    [Fact]
    public void Inspect_BlocksAnEmptyOrMissingFile()
    {
        string empty = Write("empty.tga", Array.Empty<byte>());

        Assert.True(ResourceImport.Inspect(empty).Single().Blocking);
        Assert.True(ResourceImport.Inspect(Path.Combine(_dir, "no.tga")).Single().Blocking);
    }

    [Fact]
    public void RelativeTo_KeepsAPathInsideTheRootAndDropsOneOutside()
    {
        Assert.Equal("assets/panel.tga",
            ResourceImport.RelativeTo(_dir, Path.Combine(_dir, "assets", "panel.tga")));

        // Outside the package there is nothing to be relative to, so the bare
        // name is the honest answer — the caller decides the folder.
        Assert.Equal("panel.tga",
            ResourceImport.RelativeTo(Path.Combine(_dir, "package"),
                Path.Combine(_dir, "elsewhere", "panel.tga")));
    }

    // ---------------------------------------------------------------
    // The file list
    // ---------------------------------------------------------------

    [Fact]
    public void FileCatalog_TellsWhichDeclarationPointsAtAFile()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes("""
                <Interface>
                  <Texture><Name>panel</Name><File>assets/panel.tga</File></Texture>
                </Interface>
                """),
            ["assets/panel.tga"] = Tga(16, 16),
            ["assets/orphan.tga"] = Tga(16, 16),
        };
        var pkg = PackageLoader.Load(files);

        var all = FileCatalog.All(pkg);
        var declared = all.Single(f => f.Key == "assets/panel.tga");
        var orphan = all.Single(f => f.Key == "assets/orphan.tga");

        Assert.Equal(new[] { "panel" }, declared.TextureNames);
        Assert.Empty(orphan.TextureNames);

        // What nothing points at is what a package accumulates over years and
        // carries in every export.
        Assert.Equal(new[] { "assets/orphan.tga" }, FileCatalog.Unused(pkg).Select(f => f.Path));
    }
}
