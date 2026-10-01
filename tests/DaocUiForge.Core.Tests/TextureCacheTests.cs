using System.Text;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// Cases of their own for <see cref="TextureCache"/>. It was covered only
/// indirectly through the pixel tests, which say nothing about the three things
/// it actually promises: that a name is resolved through the package's
/// declarations, that a failure is remembered rather than retried on every
/// redraw, and that a name it cannot answer lands in the inspection report.
/// </summary>
public class TextureCacheTests
{
    private static byte[] SolidPng(int w, int h, SKColor color)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) c.Clear(color);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Package Pkg(string interfaceXml, params (string Path, byte[] Bytes)[] extra)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(interfaceXml),
        };
        foreach (var (path, bytes) in extra) files[path] = bytes;
        return PackageLoader.Load(files);
    }

    private const string OneTexture = """
        <Interface>
          <Texture><Name>emoticons</Name><File>assets/emo.png</File></Texture>
        </Interface>
        """;

    // ---------------------------------------------------------------
    // Resolving
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_GoesThroughTheDeclaration_NotThroughTheNameAsAPath()
    {
        var pkg = Pkg(OneTexture, ("assets/emo.png", SolidPng(4, 4, new SKColor(255, 0, 0))));
        using var cache = new TextureCache(pkg);

        var tex = cache.Resolve("emoticons");

        Assert.NotNull(tex);
        Assert.Equal(4, tex!.Width);
        Assert.Empty(pkg.MissingTextures);
    }

    [Fact]
    public void Resolve_TreatsAnUndeclaredNameAsAPathOfItsOwn()
    {
        // The original does the same: elements naming a file directly are real
        // and there are 18 of them in the reference package.
        var pkg = Pkg("<Interface></Interface>",
            ("assets/loose.png", SolidPng(2, 2, new SKColor(0, 255, 0))));
        using var cache = new TextureCache(pkg);

        Assert.NotNull(cache.Resolve("assets/loose.png"));
    }

    [Fact]
    public void Resolve_RecordsANameItCannotAnswer()
    {
        // This set is what the inspection report is built from. Without the
        // entry a missing texture is invisible everywhere.
        var pkg = Pkg(OneTexture);   // the file itself is not in the package
        using var cache = new TextureCache(pkg);

        Assert.Null(cache.Resolve("emoticons"));
        Assert.Contains("emoticons", pkg.MissingTextures);
    }

    [Fact]
    public void IsDeclared_AsksTheDeclarations_CaseInsensitively()
    {
        var pkg = Pkg(OneTexture);
        using var cache = new TextureCache(pkg);

        Assert.True(cache.IsDeclared("Emoticons"));
        Assert.False(cache.IsDeclared("nothing_like_it"));
        Assert.False(cache.IsDeclared(null));
    }

    // ---------------------------------------------------------------
    // Caching
    // ---------------------------------------------------------------

    [Fact]
    public void Load_HandsBackTheSameBitmap_RatherThanDecodingAgain()
    {
        /* The point of the cache. A window redraw asks for the same atlas
           dozens of times, and a 1024 px DDS decoded per element would make the
           preview crawl. Reference equality is the check, because a second
           decode would be a different object. */
        var pkg = Pkg(OneTexture, ("assets/emo.png", SolidPng(4, 4, new SKColor(255, 0, 0))));
        using var cache = new TextureCache(pkg);

        var first = cache.Load("assets/emo.png");
        var second = cache.Load("assets/emo.png");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Load_RemembersAFailureToo()
    {
        /* A quirk taken from the original and worth keeping: without it a
           package with a hundred missing textures walks the whole file table
           once per element per redraw, looking for something that is not
           there. Checked through the resolver, which is the expensive part. */
        var pkg = Pkg(OneTexture);
        using var cache = new TextureCache(pkg);

        Assert.Null(cache.Load("assets/emo.png"));

        // Put the file there afterwards. A cache that forgot its failure would
        // find it now — and that is exactly what Forget is for (below).
        pkg.Files["assets/emo.png"] = SolidPng(4, 4, new SKColor(255, 0, 0));
        Assert.Null(cache.Load("assets/emo.png"));
    }

    [Fact]
    public void Forget_LetsAnImportedFileBeFound()
    {
        /* The other side of the same coin. Importing a file into an open
           package has to reach the drawing layer, and the remembered failure is
           what would otherwise keep the element empty until a reload. */
        var pkg = Pkg(OneTexture);
        using var cache = new TextureCache(pkg);

        Assert.Null(cache.Load("assets/emo.png"));

        pkg.Files["assets/emo.png"] = SolidPng(4, 4, new SKColor(255, 0, 0));
        cache.Forget("assets/emo.png");

        Assert.NotNull(cache.Load("assets/emo.png"));
    }

    // ---------------------------------------------------------------
    // Slicing
    // ---------------------------------------------------------------

    [Fact]
    public void Slice_CutsThePieceAsked_ForAndScalesItWhenTold()
    {
        var pkg = Pkg("<Interface></Interface>",
            ("atlas.png", SolidPng(16, 16, new SKColor(0, 0, 255))));
        using var cache = new TextureCache(pkg);

        using var natural = cache.Slice("atlas.png", 0, 0, 8, 4);
        Assert.NotNull(natural);
        Assert.Equal(8, natural!.Width);
        Assert.Equal(4, natural.Height);

        using var scaled = cache.Slice("atlas.png", 0, 0, 8, 4, dw: 16, dh: 8);
        Assert.NotNull(scaled);
        Assert.Equal(16, scaled!.Width);
        Assert.Equal(8, scaled.Height);
    }

    [Fact]
    public void Slice_OfAMissingTextureIsNull_AndStillRecordsTheName()
    {
        var pkg = Pkg(OneTexture);
        using var cache = new TextureCache(pkg);

        Assert.Null(cache.Slice("emoticons", 0, 0, 4, 4));
        Assert.Contains("emoticons", pkg.MissingTextures);
    }

    [Fact]
    public void Draw_RefusesAnEmptySourceOrTarget()
    {
        // Not a fault, a normal case: a template whose size fields are 0 asks
        // for nothing, and the caller decides a placeholder on the answer.
        var pkg = Pkg("<Interface></Interface>",
            ("atlas.png", SolidPng(8, 8, new SKColor(0, 0, 255))));
        using var cache = new TextureCache(pkg);

        using var bmp = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);

        Assert.False(cache.Draw(canvas, "atlas.png", 0, 0, 0, 4, new SKRect(0, 0, 4, 4)));
        Assert.False(cache.Draw(canvas, "atlas.png", 0, 0, 4, 4, new SKRect(0, 0, 0, 4)));
        Assert.True(cache.Draw(canvas, "atlas.png", 0, 0, 4, 4, new SKRect(0, 0, 4, 4)));
    }

    // ---------------------------------------------------------------
    // The game folder
    // ---------------------------------------------------------------

    [Fact]
    public void Load_FallsBackToTheGameFolder_AndSaysWhereItCameFrom()
    {
        /* A texture under atlantis/ is a correct reference to a file the game
           has and the package does not. With the installation
           indexed it draws — and the package remembers that it did, because a
           package which only looks complete on a machine with the game
           installed is worth reporting. */
        string dir = Path.Combine(Path.GetTempPath(), "daoc-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "atlantis"));
        File.WriteAllBytes(Path.Combine(dir, "atlantis", "emo.png"),
            SolidPng(8, 8, new SKColor(255, 0, 255)));

        try
        {
            var pkg = Pkg("""
                <Interface>
                  <Texture><Name>emoticons</Name><File>atlantis/emo.png</File></Texture>
                </Interface>
                """);

            using (var before = new TextureCache(pkg))
                Assert.Null(before.Resolve("emoticons"));

            Assert.Equal(1, GameFolder.Mount(pkg, dir));

            using var cache = new TextureCache(pkg);
            var tex = cache.Resolve("emoticons");

            Assert.NotNull(tex);
            Assert.Equal(8, tex!.Width);
            Assert.Contains("emoticons", pkg.GameFolderTextures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ThePackagesOwnFileWins_OverTheGameFolder()
    {
        /* The order the client uses: ui/custom overrides the default interface,
           which is the whole point of a custom UI. Told apart by size. */
        string dir = Path.Combine(Path.GetTempPath(), "daoc-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        File.WriteAllBytes(Path.Combine(dir, "assets", "emo.png"),
            SolidPng(32, 32, new SKColor(255, 0, 255)));

        try
        {
            var pkg = Pkg(OneTexture, ("assets/emo.png", SolidPng(4, 4, new SKColor(255, 0, 0))));
            GameFolder.Mount(pkg, dir);

            using var cache = new TextureCache(pkg);
            var tex = cache.Resolve("emoticons");

            Assert.NotNull(tex);
            Assert.Equal(4, tex!.Width);                 // the package's own
            Assert.Empty(pkg.GameFolderTextures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MountingAGameFolder_LeavesThePackagesFileTableAlone()
    {
        /* The condition the whole feature rests on: the game's artwork must not
           become part of the package, or an export would ship gigabytes of
           somebody else's files. */
        string dir = Path.Combine(Path.GetTempPath(), "daoc-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "loose.tga"), new byte[64]);

        try
        {
            var pkg = Pkg(OneTexture);
            int before = pkg.Files.Count;

            GameFolder.Mount(pkg, dir);

            Assert.Equal(before, pkg.Files.Count);
            Assert.Single(pkg.GameFiles);

            GameFolder.Unmount(pkg);
            Assert.Empty(pkg.GameFiles);
            Assert.Null(pkg.GameFolder);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
