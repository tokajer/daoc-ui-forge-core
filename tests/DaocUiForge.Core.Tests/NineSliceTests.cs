using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// Cases of their own for the stretchable image templates. Until now they were
/// covered only through the pixel tests of <see cref="ElementRendererTests"/>,
/// which go through the element types and therefore never state what
/// <c>drawHResize</c> and <c>drawVResize</c> themselves promise.
///
/// <para>The promise being pinned down is the core format rule: the stretchable
/// middle is <b>tiled</b>, not stretched. A stretched frame smears its
/// ornamental seams, which is visible at a glance and impossible to describe in
/// a bug report — so it belongs in a test that counts pixels.</para>
///
/// <para>The textures are built as strips of distinct colours, so where a pixel
/// came from can be read off the colour it has.</para>
/// </summary>
public class NineSliceTests
{
    private static readonly SKColor Left = new(255, 0, 0);
    private static readonly SKColor Repeat = new(0, 255, 0);
    private static readonly SKColor Right = new(0, 0, 255);
    private static readonly SKColor Elsewhere = new(255, 255, 0);

    /// <summary>
    /// A PNG with one colour per column — column x is <paramref name="columns"/>[x].
    /// PNG because <see cref="TextureCache"/> hands anything that is not TGA or
    /// DDS to Skia, which is the shortest route to a real texture here.
    /// </summary>
    private static byte[] StripsPng(SKColor[] columns, int height = 8)
    {
        using var bmp = new SKBitmap(new SKImageInfo(
            columns.Length, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            for (int x = 0; x < columns.Length; x++)
            {
                using var paint = new SKPaint { Color = columns[x] };
                c.DrawRect(x, 0, 1, height, paint);
            }
        }

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The same, one colour per row.</summary>
    private static byte[] RowsPng(SKColor[] rows, int width = 8)
    {
        using var bmp = new SKBitmap(new SKImageInfo(
            width, rows.Length, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            for (int y = 0; y < rows.Length; y++)
            {
                using var paint = new SKPaint { Color = rows[y] };
                c.DrawRect(0, y, width, 1, paint);
            }
        }

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Package Pkg(string interfaceXml, byte[] texture)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(interfaceXml),
            ["tex.png"] = texture,
        };
        return PackageLoader.Load(files);
    }

    /// <summary>Draw into a fresh surface and hand back the pixels.</summary>
    private static SKBitmap Draw(Package pkg, Func<SKCanvas, TextureCache, XElement?, bool> what,
        string template, int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var ctx = new RenderContext(pkg);
        what(canvas, ctx.Textures, pkg.ByNameLc[template]);
        return bmp;
    }

    private static void AssertColumn(SKBitmap bmp, int x, SKColor want)
    {
        var got = bmp.GetPixel(x, bmp.Height / 2);
        Assert.True(got.Red == want.Red && got.Green == want.Green && got.Blue == want.Blue,
            $"column {x}: expected {want}, got {got}");
    }

    private static void AssertRow(SKBitmap bmp, int y, SKColor want)
    {
        var got = bmp.GetPixel(bmp.Width / 2, y);
        Assert.True(got.Red == want.Red && got.Green == want.Green && got.Blue == want.Blue,
            $"row {y}: expected {want}, got {got}");
    }

    // ---------------------------------------------------------------
    // drawHResize
    // ---------------------------------------------------------------

    [Fact]
    public void HResize_PutsLeftRepeatAndRightWhereTheyBelong()
    {
        /* Texture: column 0 = left cap, column 1 = repeat, column 2 = right cap,
           column 3 onwards deliberately a colour nothing may reach for. */
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <HResizeImageTemplate><Name>bar</Name>
                <LeftWidth>1</LeftWidth><RightWidth>1</RightWidth>
                <RepeatWidth>1</RepeatWidth><Height>4</Height>
                <Texture><TextureName>tex</TextureName>
                  <Left><X>0</X><Y>0</Y></Left>
                  <Repeat><X>1</X><Y>0</Y></Repeat>
                  <Right><X>2</X><Y>0</Y></Right>
                </Texture>
              </HResizeImageTemplate>
            </Interface>
            """, StripsPng(new[] { Left, Repeat, Right, Elsewhere }));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawHResize(c, t, tpl, 10, 4), "bar", 10, 4);

        AssertColumn(bmp, 0, Left);
        AssertColumn(bmp, 9, Right);
        // Everything between the caps is the repeat tile, over and over.
        for (int x = 1; x <= 8; x++) AssertColumn(bmp, x, Repeat);
    }

    [Fact]
    public void HResize_TilesTheMiddle_ItDoesNotStretchIt()
    {
        /* THE rule. With a 2 px repeat tile of two colours, tiling
           gives an alternating pattern and stretching gives a gradient — so the
           two readings are told apart by one pixel.

           Texture: 0 = left cap, 1..2 = the two-pixel repeat tile, 3 = right cap. */
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <HResizeImageTemplate><Name>bar</Name>
                <LeftWidth>1</LeftWidth><RightWidth>1</RightWidth>
                <RepeatWidth>2</RepeatWidth><Height>4</Height>
                <Texture><TextureName>tex</TextureName>
                  <Left><X>0</X><Y>0</Y></Left>
                  <Repeat><X>1</X><Y>0</Y></Repeat>
                  <Right><X>3</X><Y>0</Y></Right>
                </Texture>
              </HResizeImageTemplate>
            </Interface>
            """, StripsPng(new[] { Left, Repeat, Elsewhere, Right }));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawHResize(c, t, tpl, 9, 4), "bar", 9, 4);

        AssertColumn(bmp, 0, Left);
        AssertColumn(bmp, 8, Right);

        // 7 px of middle from a 2 px tile: green, yellow, green, yellow, …
        for (int x = 1; x <= 7; x++)
            AssertColumn(bmp, x, (x - 1) % 2 == 0 ? Repeat : Elsewhere);
    }

    [Fact]
    public void HResize_ClipsTheLastTile_ItDoesNotSqueezeIt()
    {
        /* The middle is 5 px and the tile 2 px, so the last one is half a tile.
           Clipped it shows the tile's first colour; squeezed it would show both
           colours compressed into one pixel — which is the smear the tiling rule
           exists to avoid. */
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <HResizeImageTemplate><Name>bar</Name>
                <LeftWidth>1</LeftWidth><RightWidth>1</RightWidth>
                <RepeatWidth>2</RepeatWidth><Height>4</Height>
                <Texture><TextureName>tex</TextureName>
                  <Left><X>0</X><Y>0</Y></Left>
                  <Repeat><X>1</X><Y>0</Y></Repeat>
                  <Right><X>3</X><Y>0</Y></Right>
                </Texture>
              </HResizeImageTemplate>
            </Interface>
            """, StripsPng(new[] { Left, Repeat, Elsewhere, Right }));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawHResize(c, t, tpl, 7, 4), "bar", 7, 4);

        AssertColumn(bmp, 5, Repeat);   // the clipped half tile: its first column
        AssertColumn(bmp, 6, Right);
    }

    [Fact]
    public void HResize_WithoutATextureBlock_DrawsNothing()
    {
        // A template with no <Texture> is the "nothing to draw" case the caller
        // decides a placeholder on.
        var pkg = Pkg("""
            <Interface>
              <HResizeImageTemplate><Name>bar</Name><LeftWidth>1</LeftWidth></HResizeImageTemplate>
            </Interface>
            """, StripsPng(new[] { Left }));

        using var bmp = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var ctx = new RenderContext(pkg);

        Assert.False(NineSlice.DrawHResize(canvas, ctx.Textures, pkg.ByNameLc["bar"], 4, 4));
    }

    // ---------------------------------------------------------------
    // drawVResize
    // ---------------------------------------------------------------

    [Fact]
    public void VResize_PutsTopRepeatAndBottomWhereTheyBelong()
    {
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <VResizeImageTemplate><Name>bar</Name>
                <TopHeight>1</TopHeight><BottomHeight>1</BottomHeight>
                <RepeatHeight>1</RepeatHeight><Width>4</Width>
                <Texture><TextureName>tex</TextureName>
                  <Top><X>0</X><Y>0</Y></Top>
                  <Repeat><X>0</X><Y>1</Y></Repeat>
                  <Bottom><X>0</X><Y>2</Y></Bottom>
                </Texture>
              </VResizeImageTemplate>
            </Interface>
            """, RowsPng(new[] { Left, Repeat, Right, Elsewhere }));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawVResize(c, t, tpl, 4, 10), "bar", 4, 10);

        AssertRow(bmp, 0, Left);
        AssertRow(bmp, 9, Right);
        for (int y = 1; y <= 8; y++) AssertRow(bmp, y, Repeat);
    }

    [Fact]
    public void VResize_TilesTheMiddle_ItDoesNotStretchIt()
    {
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <VResizeImageTemplate><Name>bar</Name>
                <TopHeight>1</TopHeight><BottomHeight>1</BottomHeight>
                <RepeatHeight>2</RepeatHeight><Width>4</Width>
                <Texture><TextureName>tex</TextureName>
                  <Top><X>0</X><Y>0</Y></Top>
                  <Repeat><X>0</X><Y>1</Y></Repeat>
                  <Bottom><X>0</X><Y>3</Y></Bottom>
                </Texture>
              </VResizeImageTemplate>
            </Interface>
            """, RowsPng(new[] { Left, Repeat, Elsewhere, Right }));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawVResize(c, t, tpl, 4, 9), "bar", 4, 9);

        AssertRow(bmp, 0, Left);
        AssertRow(bmp, 8, Right);
        for (int y = 1; y <= 7; y++)
            AssertRow(bmp, y, (y - 1) % 2 == 0 ? Repeat : Elsewhere);
    }

    [Fact]
    public void VResize_WidthFallsBackToTheControl_WhenTheTemplateDeclaresNone()
    {
        /* <Width> on a VResize template is the width of the strip in the
           texture; without one the control's own width applies. The scroll bars
           of the reference package rely on it. */
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <VResizeImageTemplate><Name>bar</Name>
                <TopHeight>1</TopHeight><BottomHeight>1</BottomHeight>
                <RepeatHeight>1</RepeatHeight>
                <Texture><TextureName>tex</TextureName>
                  <Top><X>0</X><Y>0</Y></Top>
                  <Repeat><X>0</X><Y>1</Y></Repeat>
                  <Bottom><X>0</X><Y>2</Y></Bottom>
                </Texture>
              </VResizeImageTemplate>
            </Interface>
            """, RowsPng(new[] { Left, Repeat, Right }, width: 8));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawVResize(c, t, tpl, 6, 5), "bar", 8, 5);

        // Six pixels wide, because that is what the control asked for.
        AssertRow(bmp, 0, Left);
        var inside = bmp.GetPixel(5, 0);
        var outside = bmp.GetPixel(7, 0);
        Assert.Equal(Left.Red, inside.Red);
        Assert.Equal(0, outside.Alpha);
    }

    // ---------------------------------------------------------------
    // ShapeOf and DrawTemplate
    // ---------------------------------------------------------------

    [Fact]
    public void ShapeOf_ReadsTheXml_NotTheTypeName()
    {
        /* There are around forty template types and no list of them anywhere,
           so the shape is decided by which point names the <Texture> block
           carries. Here a template with a made-up type name still comes out as
           an HResize, because that is what its points say. */
        var pkg = Pkg("""
            <Interface>
              <SomethingNobodyHasSeenTemplate><Name>odd</Name>
                <Texture><TextureName>tex</TextureName>
                  <Left><X>0</X><Y>0</Y></Left>
                  <Right><X>2</X><Y>0</Y></Right>
                </Texture>
              </SomethingNobodyHasSeenTemplate>
            </Interface>
            """, StripsPng(new[] { Left }));

        Assert.Equal(TemplateShape.HResize, NineSlice.ShapeOf(pkg.ByNameLc["odd"]));
    }

    [Fact]
    public void DrawTemplate_DispatchesByShape_NotByAssumingAnArea()
    {
        /* The fault that made community_window come out wrong: a template named
           by another one was always drawn with DrawArea, which asks for one
           slice as large as the whole control — and Skia clamps a source
           rectangle to the bitmap and shrinks the destination with it, so a
           593 px panel came out as a 112 px stamp.

           Here the same call goes through DrawTemplate: the right cap has to
           land at the right edge, which only the HResize branch does. */
        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <HResizeImageTemplate><Name>bar</Name>
                <LeftWidth>1</LeftWidth><RightWidth>1</RightWidth>
                <RepeatWidth>1</RepeatWidth><Height>4</Height>
                <Texture><TextureName>tex</TextureName>
                  <Left><X>0</X><Y>0</Y></Left>
                  <Repeat><X>1</X><Y>0</Y></Repeat>
                  <Right><X>2</X><Y>0</Y></Right>
                </Texture>
              </HResizeImageTemplate>
            </Interface>
            """, StripsPng(new[] { Left, Repeat, Right }));

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawTemplate(c, t, tpl, 12, 4), "bar", 12, 4);

        AssertColumn(bmp, 0, Left);
        AssertColumn(bmp, 11, Right);
        AssertColumn(bmp, 6, Repeat);
    }

    // ---------------------------------------------------------------
    // drawFullResize
    // ---------------------------------------------------------------

    [Fact]
    public void FullResize_KeepsTheCornersAndTilesTheRest()
    {
        /* Nine fields out of a 3x3 texture, one colour per field, drawn at
           7 x 7. The corners stay 1 px; the edges and the centre are tiled. */
        var colours = new[]
        {
            new SKColor(10, 0, 0), new SKColor(20, 0, 0), new SKColor(30, 0, 0),
            new SKColor(0, 10, 0), new SKColor(0, 20, 0), new SKColor(0, 30, 0),
            new SKColor(0, 0, 10), new SKColor(0, 0, 20), new SKColor(0, 0, 30),
        };

        using var src = new SKBitmap(new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(src))
        {
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                using var paint = new SKPaint { Color = colours[y * 3 + x] };
                c.DrawRect(x, y, 1, 1, paint);
            }
        }
        using var img = SKImage.FromBitmap(src);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);

        var pkg = Pkg("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <FullResizeImageTemplate><Name>panel</Name>
                <LeftWidth>1</LeftWidth><RightWidth>1</RightWidth>
                <TopHeight>1</TopHeight><BottomHeight>1</BottomHeight>
                <MiddleWidth>1</MiddleWidth><MiddleHeight>1</MiddleHeight>
                <Texture><TextureName>tex</TextureName>
                  <TopLeft><X>0</X><Y>0</Y></TopLeft>
                  <TopMiddle><X>1</X><Y>0</Y></TopMiddle>
                  <TopRight><X>2</X><Y>0</Y></TopRight>
                  <MiddleLeft><X>0</X><Y>1</Y></MiddleLeft>
                  <MiddleMiddle><X>1</X><Y>1</Y></MiddleMiddle>
                  <MiddleRight><X>2</X><Y>1</Y></MiddleRight>
                  <BottomLeft><X>0</X><Y>2</Y></BottomLeft>
                  <BottomMiddle><X>1</X><Y>2</Y></BottomMiddle>
                  <BottomRight><X>2</X><Y>2</Y></BottomRight>
                </Texture>
              </FullResizeImageTemplate>
            </Interface>
            """, data.ToArray());

        using var bmp = Draw(pkg,
            (c, t, tpl) => NineSlice.DrawFullResize(c, t, tpl, 7, 7), "panel", 7, 7);

        void At(int x, int y, SKColor want)
        {
            var got = bmp.GetPixel(x, y);
            Assert.True(got.Red == want.Red && got.Green == want.Green && got.Blue == want.Blue,
                $"({x},{y}): expected {want}, got {got}");
        }

        At(0, 0, colours[0]);   // top left corner, untiled
        At(6, 0, colours[2]);   // top right corner
        At(0, 6, colours[6]);   // bottom left corner
        At(6, 6, colours[8]);   // bottom right corner
        At(3, 0, colours[1]);   // top edge, tiled
        At(0, 3, colours[3]);   // left edge, tiled
        At(3, 3, colours[4]);   // centre, tiled
        At(6, 3, colours[5]);   // right edge
        At(3, 6, colours[7]);   // bottom edge
    }
}
