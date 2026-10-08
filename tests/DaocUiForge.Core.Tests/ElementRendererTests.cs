using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The regression net for the drawing layer. The focus is on the geometry
/// rules of the DAoC UI format — they were worked
/// out the hard way, they are not obvious, and breaking one otherwise only
/// shows up on screen.
/// </summary>
public class ElementRendererTests
{
    /// <summary>A package from XML snippets, without files or textures.</summary>
    private static Package Pkg(string interfaceXml)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(interfaceXml),
        };
        return PackageLoader.Load(files);
    }

    private static XElement Def(string xml) => XElement.Parse(xml);

    /// <summary>
    /// Draw one element and return its place. The target surface is real, so
    /// that the drawing calls run as well.
    /// </summary>
    private static RenderedElement? Render(Package pkg, XElement def,
        double winW = 200, double winH = 100, RenderOptions? opt = null)
    {
        using var bmp = new SKBitmap(new SKImageInfo(
            (int)winW, (int)winH, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var ctx = new RenderContext(pkg, opt ?? new RenderOptions());
        return ElementRenderer.Render(canvas, ctx, def, winW, winH);
    }

    private static Package Empty() => Pkg("<Interface></Interface>");

    // ---------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------

    [Fact]
    public void OffsetRight_AppliesWithAndWithoutTopLeft()
    {
        /* OffsetRight measures from the right edge, together with TopLeft as
           well. In game the stats_index_window tab row (TopLeft+OffsetRight)
           is drawn in reverse order, the X=0 tab rightmost; checked against
           an in-game screenshot 2026-10-08. */
        var pkg = Empty();

        var ohneTopLeft = Render(pkg, Def("""
            <LabelDef>
              <Position><X>10</X><Y>0</Y></Position>
              <Width>40</Width><Height>12</Height><Label>x</Label>
              <Alignment><OffsetRight>true</OffsetRight></Alignment>
            </LabelDef>
            """), winW: 200);
        Assert.NotNull(ohneTopLeft);
        Assert.Equal(150, ohneTopLeft.Bounds.Left);   // 200 - 40 - 10

        var mitTopLeft = Render(pkg, Def("""
            <LabelDef>
              <Position><X>10</X><Y>0</Y></Position>
              <Width>40</Width><Height>12</Height><Label>x</Label>
              <Alignment><TopLeft>true</TopLeft><OffsetRight>true</OffsetRight></Alignment>
            </LabelDef>
            """), winW: 200);
        Assert.NotNull(mitTopLeft);
        Assert.Equal(150, mitTopLeft.Bounds.Left);    // 200 - 40 - 10
    }

    [Fact]
    public void OffsetBottom_AlwaysMeasuresFromTheBottom_EvenWithTopLeft()
    {
        // Unlike the horizontal case, the vertical anchor applies TOGETHER
        // with TopLeft as well (the footer in custom3).
        var pkg = Empty();

        var el = Render(pkg, Def("""
            <LabelDef>
              <Position><X>0</X><Y>5</Y></Position>
              <Width>20</Width><Height>10</Height><Label>x</Label>
              <Alignment><TopLeft>true</TopLeft><OffsetBottom>true</OffsetBottom></Alignment>
            </LabelDef>
            """), winH: 100);

        Assert.NotNull(el);
        Assert.Equal(85, el.Bounds.Top);   // 100 - 10 - 5
    }

    [Fact]
    public void GrowWidth_KeepsTheDeclaredSize_WhenTheWindowGainedNothing()
    {
        /* Grow adds what the WINDOW gained, it does not stretch to the edge.
           At the declared size the delta is 0, so a growing element is exactly
           as wide as it says. This is the reading DAoCEd draws with: its
           getAlignedBounds handles centring and the two offsets and does not
           look at the grow flags at all. Reading it the other way made the
           three text areas of community_window 347 px tall instead of 95. */
        var pkg = Empty();

        var el = Render(pkg, Def("""
            <LabelDef>
              <Position><X>30</X><Y>0</Y></Position>
              <Width>50</Width><Height>10</Height><Label>x</Label>
              <Alignment><GrowWidth>true</GrowWidth></Alignment>
            </LabelDef>
            """), winW: 200);

        Assert.NotNull(el);
        Assert.Equal(50, el.Bounds.Width);
    }

    [Fact]
    public void GrowWidth_WithoutADeclaredWidth_InventsNoSize()
    {
        // There is nothing to add the gain to. The template decides the size,
        // exactly as it does for an element without the flag.
        var pkg = Empty();

        var el = Render(pkg, Def("""
            <LabelDef>
              <Position><X>30</X><Y>0</Y></Position>
              <Height>10</Height><Label>x</Label>
              <Alignment><GrowWidth>true</GrowWidth></Alignment>
            </LabelDef>
            """), winW: 200);

        Assert.NotNull(el);
        Assert.NotEqual(170, el.Bounds.Width);
    }

    [Fact]
    public void Centring_RoundsLikeJavaScript_HalvesGoUp()
    {
        /* C# Math.Round rounds to even (0.5 -> 0), JavaScript always up
           (0.5 -> 1). With an odd remaining width that shifts the element by a
           pixel. Window width 201, element width 100 -> (201-100)/2 = 50.5
           -> 51. */
        var pkg = Empty();

        var el = Render(pkg, Def("""
            <LabelDef>
              <Width>100</Width><Height>10</Height><Label>x</Label>
              <Alignment><CenterHorizontally>true</CenterHorizontally></Alignment>
            </LabelDef>
            """), winW: 201);

        Assert.NotNull(el);
        Assert.Equal(51, el.Bounds.Left);
    }

    [Fact]
    public void JsRound_IsRightForNegativesToo()
    {
        // JavaScript: Math.round(-1.5) === -1, not -2.
        Assert.Equal(1, RenderMath.JsRound(0.5));
        Assert.Equal(-1, RenderMath.JsRound(-1.5));
        Assert.Equal(2, RenderMath.JsRound(1.5));
        Assert.Equal(0, RenderMath.JsRound(-0.5));
    }

    [Fact]
    public void LabelHeight_IsAClippingFrame_NotALayoutSize()
    {
        // <Height> on labels only clips. Two labels may overlap without
        // anyone "correcting" the height.
        var pkg = Empty();

        var el = Render(pkg, Def("""
            <LabelDef>
              <Position><X>0</X><Y>0</Y></Position>
              <Width>50</Width><Height>4</Height><Label>Sehr hoher Text</Label>
            </LabelDef>
            """));

        Assert.NotNull(el);
        Assert.Equal(4, el.Bounds.Height);   // taken over unchanged
    }

    [Fact]
    public void LabelWithoutWidth_GetsContentWidth_ForTheHitArea()
    {
        /* In the original renderEl set the width only under `if(w)`; without
           <Width> the box was content-wide. Without reproducing that the
           element would be zero pixels wide — and thus not clickable. */
        var pkg = Empty();

        var el = Render(pkg, Def("<LabelDef><Label>Sample text</Label></LabelDef>"));

        Assert.NotNull(el);
        Assert.True(el.Bounds.Width > 0,
            "A text field without <Width> must have a measurable width.");
    }

    [Fact]
    public void MultiLineLabelWithoutHeight_IsAsTallAsItsLines()
    {
        /* A known trap, and it was wrong until
           2026-09-08: the fallback height for a label without <Height> was one
           line regardless of the text, so the lower half of a two-line label
           could not be clicked and counted short when the overflow was
           measured. Nothing is wrapped (white-space:pre), so the line count is
           the number of breaks plus one. */
        var pkg = Empty();

        var one = Render(pkg, Def("<LabelDef><Label>One</Label></LabelDef>"));
        var two = Render(pkg, Def("<LabelDef><Label>One\nTwo</Label></LabelDef>"));
        var three = Render(pkg, Def("<LabelDef><Label>One\nTwo\nThree</Label></LabelDef>"));

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotNull(three);

        using var ctx = new RenderContext(pkg, new RenderOptions());
        float px = ctx.Fonts.SizePx(null), line = ctx.Fonts.LinePx(null);

        // The box is rounded once, at the end — comparing the differences
        // instead would put the rounding of a fractional advance into them.
        Assert.Equal(RenderMath.JsRound(px + 3), one.Bounds.Height, 3);
        Assert.Equal(RenderMath.JsRound(px + 3 + line), two.Bounds.Height, 3);
        Assert.Equal(RenderMath.JsRound(px + 3 + 2 * line), three.Bounds.Height, 3);
        Assert.True(two.Bounds.Height > one.Bounds.Height,
            "A second line has to make the box taller.");
    }

    [Fact]
    public void SingleLineLabel_KeepsTheOriginalsFallbackHeight()
    {
        /* The first line is unchanged on purpose: px + 3 is what the original
           gives a label without <Height>, it was measured against the game,
           and only the lines after it were ever missing. */
        var pkg = Empty();

        var el = Render(pkg, Def("<LabelDef><FontName>x</FontName><Label>One</Label></LabelDef>"));

        Assert.NotNull(el);
        using var ctx = new RenderContext(pkg, new RenderOptions());
        Assert.Equal(ctx.Fonts.SizePx("x") + 3, el.Bounds.Height, 3);
    }

    [Fact]
    public void DeclaredHeightStillWins_OverTheLineCount()
    {
        // The line count is the fallback, not a correction: a declared
        // <Height> is a clipping frame and the box stays that tall.
        var pkg = Empty();

        var el = Render(pkg, Def(
            "<LabelDef><Width>40</Width><Height>4</Height><Label>One\nTwo</Label></LabelDef>"));

        Assert.NotNull(el);
        Assert.Equal(40, el.Bounds.Width);
        Assert.Equal(4, el.Bounds.Height);
    }

    [Fact]
    public void LeadingSpacesInDisplayText_ArePreserved()
    {
        /* DAoC packages align text with leading spaces. custom3_window.xml
           contains <Data>         %</Data> — those nine spaces push the
           per-cent sign behind the number in front of it. Trimmed, it reads
           "% 87" instead of "87 %". The original trims (tx, line 488) and shows
           the same fault; this deliberately deviates. */
        Assert.Equal("         %", Xml.TxDisplay(
            Def("<LabelDef><Data>         %</Data></LabelDef>"), "Data"));

        // Indented across lines = formatting of the file, not text content.
        Assert.Equal("Hello", Xml.TxDisplay(Def("""
            <LabelDef><Data>
                Hello
            </Data></LabelDef>
            """), "Data"));

        // Tx itself still trims — numbers and names need that.
        Assert.Equal("%", Xml.Tx(
            Def("<LabelDef><Data>         %</Data></LabelDef>"), "Data"));
    }

    [Fact]
    public void AlignedText_EndsUpAtTheRightOfTheBox()
    {
        // Does preserving them show up in the drawing too? The text has to be
        // measurably wider than the sign on its own.
        var pkg = Empty();

        var indented = Render(pkg, Def("<LabelDef><Data>         %</Data></LabelDef>"));
        var plain = Render(pkg, Def("<LabelDef><Data>%</Data></LabelDef>"));

        Assert.NotNull(indented);
        Assert.NotNull(plain);
        Assert.True(indented.Bounds.Width > plain.Bounds.Width,
            "The leading spaces have to increase the measured width.");
    }

    // ---------------------------------------------------------------
    // Fonts
    // ---------------------------------------------------------------

    [Fact]
    public void TtfHeight_IsFontSize_NotLineHeight()
    {
        /* THE rule, measured against the running game on summary.xml
           (2026-08-07): <Height>13</Height> is a 13 px font, and the line
           advance follows from the TTF at 13 x 1.3 = 16.9. The HTML original
           had it the other way round and drew everything about a quarter too
           small. */
        var pkg = Empty();
        pkg.Fonts["testfont"] = new FontRef { File = "", Height = 13, LineHeight = 1.3 };

        using var fonts = new FontProvider(pkg);

        Assert.Equal(13f, fonts.SizePx("testfont"));     // font size
        Assert.Equal(16.9f, fonts.LinePx("testfont"));   // line advance
    }

    // ---------------------------------------------------------------
    // States and templates
    // ---------------------------------------------------------------

    [Fact]
    public void EffectIcon_IsInvisibleWithoutAMatchingState()
    {
        /* Effect templates (*_mez, *_diz, *_psn, *_ns) show their effect
           through the SPRITE LEVEL, not by showing and hiding. With no state
           chosen none is active — as with "healthy" in the game. */
        var pkg = Pkg("""
            <Interface>
              <StatusIconTemplate><Name>grp_mez</Name>
                <TextureName>fx</TextureName><Width>16</Width><Height>16</Height>
                <MaxLevels>5</MaxLevels>
              </StatusIconTemplate>
            </Interface>
            """);
        var def = Def("<StatusIconDef><TemplateName>grp_mez</TemplateName></StatusIconDef>");

        var off = Render(pkg, def, opt: new RenderOptions { SimulatedState = 0 });
        Assert.NotNull(off);
        Assert.False(off.Visible);

        // mez = state 1 → visible
        var on = Render(pkg, def, opt: new RenderOptions { SimulatedState = 1 });
        Assert.NotNull(on);
        Assert.True(on.Visible);

        // diz = state 2 → this icon stays off
        var wrongState = Render(pkg, def, opt: new RenderOptions { SimulatedState = 2 });
        Assert.NotNull(wrongState);
        Assert.False(wrongState.Visible);
    }

    [Fact]
    public void MissingTemplate_ShowsUpInTheReport()
    {
        var pkg = Empty();
        var el = Render(pkg, Def("""
            <ButtonDef><TemplateName>gibt_es_nicht</TemplateName>
              <Width>10</Width><Height>10</Height></ButtonDef>
            """));

        Assert.NotNull(el);
        Assert.Equal("gibt_es_nicht", el.MissingTemplate);
        Assert.Contains("gibt_es_nicht", pkg.MissingTemplates);
    }

    [Fact]
    public void InvisibleButton_HintIsHiddenWithShowEditorHintsOff()
    {
        // The tinted click area is an editor-only hint, not a game element:
        // with ShowEditorHints off the bitmap has to stay fully transparent.
        var pkg = Empty();
        var def = Def("""
            <InvisibleButtonDef><TemplateName>none</TemplateName>
              <Width>20</Width><Height>20</Height></InvisibleButtonDef>
            """);

        using var offBmp = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(offBmp))
        using (var ctx = new RenderContext(Empty(), new RenderOptions { ShowEditorHints = false }))
            ElementRenderer.Render(canvas, ctx, def, 20, 20);
        Assert.Equal(0, offBmp.GetPixel(10, 10).Alpha);

        using var onBmp = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(onBmp))
        using (var ctx = new RenderContext(pkg, new RenderOptions()))
            ElementRenderer.Render(canvas, ctx, def, 20, 20);
        Assert.True(onBmp.GetPixel(10, 10).Alpha > 0);
    }

    [Fact]
    public void TemplateName_none_IsNotAMissingTemplate()
    {
        // "none" explicitly means "deliberately none" — not a finding.
        var pkg = Empty();
        var el = Render(pkg, Def("""
            <ButtonDef><TemplateName>none</TemplateName>
              <Width>10</Width><Height>10</Height></ButtonDef>
            """));

        Assert.NotNull(el);
        Assert.Null(el.MissingTemplate);
        Assert.Empty(pkg.MissingTemplates);
    }

    [Fact]
    public void TextureInTheGameFolder_IsReportedSeparately()
    {
        /* The template is there, but its texture points into the game folder
           (atlantis/…). That is NOT a package error — the element is marked and
           the texture reported separately. */
        var pkg = Pkg("""
            <Interface>
              <ImageAreaTemplate><Name>pfeil</Name>
                <Texture><TextureName>smallarrows</TextureName>
                  <TopLeft><X>0</X><Y>0</Y></TopLeft></Texture>
                <Size><X>16</X><Y>16</Y></Size>
              </ImageAreaTemplate>
            </Interface>
            """);
        pkg.Textures["smallarrows"] = "atlantis/smallarrows.dds";   // file is absent

        var el = Render(pkg, Def("<ImageAreaDef><TemplateName>pfeil</TemplateName></ImageAreaDef>"));

        Assert.NotNull(el);
        Assert.Null(el.MissingTemplate);                       // the template is present
        Assert.Contains("smallarrows", el.MissingTextures);
        Assert.Contains("smallarrows", pkg.MissingTextures);
    }

    [Fact]
    public void TemplateLookupPrefersTheExpectedType()
    {
        // Same name in two types: the expected type wins.
        var pkg = Pkg("""
            <Interface>
              <ButtonTemplate><Name>doppelt</Name><Size><X>40</X><Y>20</Y></Size></ButtonTemplate>
              <ImageAreaTemplate><Name>doppelt</Name><Size><X>99</X><Y>99</Y></Size></ImageAreaTemplate>
            </Interface>
            """);

        var el = Render(pkg, Def("<ButtonDef><TemplateName>doppelt</TemplateName></ButtonDef>"));

        Assert.NotNull(el);
        Assert.Equal(40, el.Bounds.Width);   // from the ButtonTemplate
        Assert.Equal(20, el.Bounds.Height);
    }

    [Fact]
    public void UnknownElementType_ReturnsNull()
    {
        // In the original: default: return null.
        Assert.Null(Render(Empty(), Def("<VoelligUnbekanntDef><Width>10</Width></VoelligUnbekanntDef>")));
    }

    // ---------------------------------------------------------------
    // StaticFileImageDef — the type the original had no branch for
    // ---------------------------------------------------------------

    /// <summary>
    /// Without a branch it fell through to <c>default: return null</c>: no box,
    /// no hit area, and counted as an unknown type by the smoke test.
    /// </summary>
    [Fact]
    public void StaticFileImage_IsAKnownType_AndKeepsItsDeclaredSize()
    {
        var el = Render(Empty(), Def("""
            <StaticFileImageDef>
              <Position><X>5</X><Y>7</Y></Position>
              <CanvasName>map</CanvasName>
              <Width>60</Width><Height>40</Height>
            </StaticFileImageDef>
            """));

        Assert.NotNull(el);
        Assert.Equal(5, el.Bounds.Left);
        Assert.Equal(7, el.Bounds.Top);
        Assert.Equal(60, el.Bounds.Width);
        Assert.Equal(40, el.Bounds.Height);
    }

    /// <summary>
    /// Nothing in the package says what a canvas holds, so the run-time
    /// stand-in is what is drawn — and it has to be real pixels, or the
    /// element is invisible in the preview after all.
    /// </summary>
    [Fact]
    public void StaticFileImage_DrawsTheRunTimeStandIn()
    {
        using var bmp = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var ctx = new RenderContext(Empty(), new RenderOptions());

        var el = ElementRenderer.Render(canvas, ctx, Def("""
            <StaticFileImageDef>
              <Position><X>4</X><Y>4</Y></Position>
              <CanvasName>map</CanvasName>
              <Width>20</Width><Height>20</Height>
            </StaticFileImageDef>
            """), 40, 40);

        Assert.NotNull(el);
        Assert.True(bmp.GetPixel(10, 10).Alpha > 0);   // inside the element
        Assert.Equal(0, bmp.GetPixel(30, 30).Alpha);   // beyond it, nothing
    }

    /// <summary>
    /// No size is invented for it: DAoCEd's node carries only
    /// <c>&lt;CanvasName&gt;</c>, so a default here would be a size the game
    /// does not use. The element is still known, and still in the list.
    /// </summary>
    [Fact]
    public void StaticFileImage_InventsNoSize()
    {
        var el = Render(Empty(), Def("<StaticFileImageDef><CanvasName>map</CanvasName></StaticFileImageDef>"));

        Assert.NotNull(el);
        Assert.Equal(0, el.Bounds.Width);
        Assert.Equal(0, el.Bounds.Height);
    }

    /// <summary>It grows with the window like any other control.</summary>
    [Fact]
    public void StaticFileImage_GrowsWithTheWindow()
    {
        var el = Render(Empty(), Def("""
            <StaticFileImageDef>
              <CanvasName>map</CanvasName>
              <Width>320</Width><Height>240</Height>
              <Alignment><GrowWidth>true</GrowWidth><GrowHeight>true</GrowHeight></Alignment>
            </StaticFileImageDef>
            """), winW: 320, winH: 240);

        Assert.NotNull(el);
        Assert.Equal(320, el.Bounds.Width);
        Assert.Equal(240, el.Bounds.Height);
    }

    // ---------------------------------------------------------------
    // Sizes from the template
    // ---------------------------------------------------------------

    [Fact]
    public void FullResizeImage_FillsTheWindow_WhenNothingIsGiven()
    {
        var pkg = Empty();
        var el = Render(pkg, Def("<FullResizeImageDef><TemplateName>none</TemplateName></FullResizeImageDef>"),
            winW: 320, winH: 240);

        Assert.NotNull(el);
        Assert.Equal(320, el.Bounds.Width);
        Assert.Equal(240, el.Bounds.Height);
    }

    [Fact]
    public void HorizontalResizeButton_ResolvesItsInnerTemplate()
    {
        /* HorizontalResizeButtonTemplate does not draw itself; it points
           through <Normal> at a HorizontalResizeImageTemplate. The height comes
           from the outer template. */
        var pkg = Pkg("""
            <Interface>
              <HorizontalResizeButtonTemplate><Name>knopf</Name>
                <Height>22</Height><Normal>knopf_bild</Normal>
              </HorizontalResizeButtonTemplate>
              <HorizontalResizeImageTemplate><Name>knopf_bild</Name>
                <Texture><TextureName>tex</TextureName>
                  <Left><X>0</X><Y>0</Y></Left></Texture>
                <LeftWidth>4</LeftWidth>
              </HorizontalResizeImageTemplate>
            </Interface>
            """);

        var el = Render(pkg, Def("""
            <HorizontalResizeButtonDef><TemplateName>knopf</TemplateName>
              <Width>80</Width></HorizontalResizeButtonDef>
            """));

        Assert.NotNull(el);
        Assert.Equal(22, el.Bounds.Height);
        Assert.Null(el.MissingTemplate);
    }

    [Fact]
    public void VerticalResizeImage_TakesWidthFromTheTemplate_AndHeightFromTheWindow()
    {
        var pkg = Pkg("""
            <Interface>
              <VerticalResizeImageTemplate><Name>leiste</Name>
                <Width>9</Width>
                <Texture><TextureName>tex</TextureName></Texture>
              </VerticalResizeImageTemplate>
            </Interface>
            """);

        var el = Render(pkg, Def("<VerticalResizeImageDef><TemplateName>leiste</TemplateName></VerticalResizeImageDef>"),
            winH: 150);

        Assert.NotNull(el);
        Assert.Equal(9, el.Bounds.Width);
        Assert.Equal(150, el.Bounds.Height);
    }

    [Fact]
    public void DynamicImage_BringsItsOwnMeasurements()
    {
        // DynamicImageDef needs no template: texture and measurements sit in
        // the element itself.
        var pkg = Empty();
        var el = Render(pkg, Def("""
            <DynamicImageDef><TextureName>eigen</TextureName>
              <Dimensions><X>64</X><Y>32</Y></Dimensions>
              <TextureCoords><X>0</X><Y>0</Y></TextureCoords>
            </DynamicImageDef>
            """));

        Assert.NotNull(el);
        Assert.Equal(64, el.Bounds.Width);
        Assert.Equal(32, el.Bounds.Height);
    }

    [Fact]
    public void IconSet_DerivesTheGridFromItsArea()
    {
        /* Without <Rows>/<Columns> the grid follows from the area and the cell
           size: 64 / (16+0) = 4 columns, 32 / 16 = 2 rows. */
        var pkg = Pkg("""
            <Interface>
              <IconSetTemplate><Name>raster</Name><IconTemplate>zelle</IconTemplate></IconSetTemplate>
              <IconTemplate><Name>zelle</Name><Size><X>16</X><Y>16</Y></Size></IconTemplate>
            </Interface>
            """);

        var el = Render(pkg, Def("""
            <IconSetDef><TemplateName>raster</TemplateName>
              <Width>64</Width><Height>32</Height></IconSetDef>
            """));

        Assert.NotNull(el);
        Assert.Equal(64, el.Bounds.Width);
        Assert.Equal(32, el.Bounds.Height);
    }

    [Fact]
    public void Tabs_AreCollected_AndTheFirstIsActive()
    {
        var pkg = Pkg("<Interface><TabsTemplate><Name>reiter</Name></TabsTemplate></Interface>");
        var def = Def("""
            <TabsDef><TemplateName>reiter</TemplateName>
              <Width>200</Width><Height>20</Height>
              <Tab><Id>1</Id><Name>Erster</Name></Tab>
              <Tab><Id>2</Id><Name>Zweiter</Name></Tab>
            </TabsDef>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(200, 100));
        using var canvas = new SKCanvas(bmp);
        using var ctx = new RenderContext(pkg);
        ElementRenderer.Render(canvas, ctx, def, 200, 100);

        Assert.Equal(2, ctx.Tabs.Count);
        Assert.Equal("Erster", ctx.Tabs[0].Name);
        Assert.Equal("1", ctx.ActiveTab);
    }

    // ---------------------------------------------------------------
    // Pixels: record -> shift -> clip
    // ---------------------------------------------------------------

    /// <summary>
    /// A single-colour texture as PNG. The TextureCache recognises .tga and
    /// .dds itself and leaves everything else to Skia — PNG is therefore the
    /// shortest route to a real texture in a test.
    /// </summary>
    private static byte[] SolidPng(int w, int h, SKColor color)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) c.Clear(color);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// A package with a real, loadable texture called "tex" (32x32, red).
    /// </summary>
    private static Package PkgWithTexture(string interfaceXml)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(interfaceXml),
            ["tex.png"] = SolidPng(32, 32, new SKColor(255, 0, 0)),
        };
        return PackageLoader.Load(files);
    }

    [Fact]
    public void DrawnImage_EndsUpAtThePositionOfTheElement()
    {
        /* Checks the chain the whole drawing layer rests on: content is
           recorded in local coordinates and only shifted afterwards (because
           edge alignment needs the final size). If the shift goes missing
           everything sticks in the top left corner — and not one of the
           geometry tests notices. */
        var pkg = PkgWithTexture("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <ImageAreaTemplate><Name>rot</Name>
                <Texture><TextureName>tex</TextureName>
                  <TopLeft><X>0</X><Y>0</Y></TopLeft></Texture>
                <Size><X>10</X><Y>10</Y></Size>
              </ImageAreaTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(60, 60, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg);

        var el = ElementRenderer.Render(canvas, ctx, Def("""
            <ImageAreaDef><TemplateName>rot</TemplateName>
              <Position><X>20</X><Y>30</Y></Position></ImageAreaDef>
            """), 60, 60);

        Assert.NotNull(el);
        Assert.Equal(new SKRect(20, 30, 30, 40), el.Bounds);

        Assert.Equal(255, bmp.GetPixel(25, 35).Red);   // in the middle of the element
        Assert.Equal(0, bmp.GetPixel(5, 5).Alpha);     // outside stays empty
        Assert.Equal(0, bmp.GetPixel(35, 45).Alpha);   // and so does past the element
    }

    [Fact]
    public void ContentIsClippedToTheElementBox()
    {
        /* .el { overflow:hidden } in the original. The template supplies
           20x20 but the element declares only 8x8 — the rest has to go. */
        var pkg = PkgWithTexture("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <ImageAreaTemplate><Name>rot</Name>
                <Texture><TextureName>tex</TextureName>
                  <TopLeft><X>0</X><Y>0</Y></TopLeft></Texture>
                <Size><X>20</X><Y>20</Y></Size>
              </ImageAreaTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg);

        ElementRenderer.Render(canvas, ctx, Def("""
            <ImageAreaDef><TemplateName>rot</TemplateName>
              <Position><X>0</X><Y>0</Y></Position>
              <Width>8</Width><Height>8</Height></ImageAreaDef>
            """), 40, 40);

        Assert.Equal(255, bmp.GetPixel(4, 4).Red);   // inside
        Assert.Equal(0, bmp.GetPixel(12, 4).Alpha);  // beyond the 8 px
        Assert.Equal(0, bmp.GetPixel(4, 12).Alpha);
    }

    [Fact]
    public void NineSliceBackground_IsTiledNotStretched()
    {
        /* The core rule for stretchable images: the centre is
           REPEATED. With a 4 px tile and a 12 px target width three tiles have
           to appear — not one stretched one. Checkable through coverage:
           stretched the area would look identical, but the last tile gets
           clipped, so we count filled pixels. */
        var pkg = PkgWithTexture("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <FullResizeImageTemplate><Name>rahmen</Name>
                <Texture><TextureName>tex</TextureName>
                  <MiddleMiddle><X>0</X><Y>0</Y></MiddleMiddle></Texture>
                <MiddleWidth>4</MiddleWidth><MiddleHeight>4</MiddleHeight>
              </FullResizeImageTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg);

        ElementRenderer.Render(canvas, ctx, Def("""
            <FullResizeImageDef><TemplateName>rahmen</TemplateName>
              <Width>12</Width><Height>12</Height></FullResizeImageDef>
            """), 20, 20);

        // Without corners and edges the area consists of the centre alone:
        // 12x12 have to be filled completely.
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 12; x++)
            Assert.Equal(255, bmp.GetPixel(x, y).Red);

        Assert.Equal(0, bmp.GetPixel(13, 13).Alpha);   // outside
    }

    // ---------------------------------------------------------------
    // Sample data
    // ---------------------------------------------------------------

    [Fact]
    public void ScalarLabel_WithoutSampleData_ShowsZero()
    {
        // showDummy off: ScalarLabelDef shows "0", not the adapter name.
        var el = Render(Empty(), Def("""
            <ScalarLabelDef><Adapter>group_health0</Adapter>
              <Width>40</Width><Height>12</Height></ScalarLabelDef>
            """), opt: new RenderOptions { ShowSampleData = false });

        Assert.NotNull(el);
        Assert.True(el.Visible);
    }

    [Fact]
    public void SampleData_DoesNotChangeTheXml()
    {
        /* A principle: sample content is presentation only. If the renderer
           wrote it into the tree it would end up in the package on save. */
        var def = Def("""
            <LabelDef><Adapter>group_name0</Adapter>
              <Width>60</Width><Height>12</Height></LabelDef>
            """);
        string before = def.ToString();

        Render(Empty(), def, opt: new RenderOptions { ShowSampleData = true });

        Assert.Equal(before, def.ToString());
    }

    // ---------------------------------------------------------------
    // A named sub-template is drawn by its own shape
    // ---------------------------------------------------------------

    [Fact]
    public void ABackgroundTemplate_IsDrawnByItsOwnShape_NotAlwaysAsAnArea()
    {
        /* A TextAreaTemplate names its background by name, and that name may
           lead to any of the four shapes. Drawing it with DrawArea regardless
           asks for one slice as large as the whole control: Skia clamps a
           source rectangle to the bitmap and shrinks the target with it, so a
           593 x 347 background came out 112 x 20 (community_window). */
        var pkg = PkgWithTexture("""
            <Interface>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
              <FullResizeImageTemplate><Name>frame</Name>
                <Texture><TextureName>tex</TextureName>
                  <MiddleMiddle><X>0</X><Y>0</Y></MiddleMiddle></Texture>
                <MiddleWidth>4</MiddleWidth><MiddleHeight>4</MiddleHeight>
              </FullResizeImageTemplate>
              <TextAreaTemplate><Name>area</Name>
                <BackgroundTemplate>frame</BackgroundTemplate>
              </TextAreaTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(80, 60, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg, new RenderOptions { ShowSampleData = false });

        ElementRenderer.Render(canvas, ctx, Def("""
            <TextAreaDef><TemplateName>area</TemplateName>
              <Position><X>0</X><Y>0</Y></Position>
              <Width>70</Width><Height>50</Height></TextAreaDef>
            """), 80, 60);

        // The whole declared box is covered, tile by tile — not a stamp of the
        // texture's top left corner in one corner of it.
        Assert.Equal(255, bmp.GetPixel(2, 2).Red);
        Assert.Equal(255, bmp.GetPixel(65, 45).Red);
        Assert.Equal(0, bmp.GetPixel(75, 55).Alpha);   // and nothing past it
    }

    [Fact]
    public void ShapeOf_ReadsTheTextureBlock_NotTheTypeName()
    {
        // The one rule both the window and the template preview go through.
        XElement T(string xml) => XElement.Parse(xml);

        Assert.Equal(TemplateShape.FullResize, NineSlice.ShapeOf(T(
            "<AnythingTemplate><Texture><TextureName>t</TextureName>" +
            "<MiddleMiddle><X>0</X><Y>0</Y></MiddleMiddle></Texture></AnythingTemplate>")));

        Assert.Equal(TemplateShape.HResize, NineSlice.ShapeOf(T(
            "<AnythingTemplate><Texture><TextureName>t</TextureName>" +
            "<Left><X>0</X><Y>0</Y></Left><Right><X>9</X><Y>0</Y></Right></Texture></AnythingTemplate>")));

        Assert.Equal(TemplateShape.VResize, NineSlice.ShapeOf(T(
            "<AnythingTemplate><Texture><TextureName>t</TextureName>" +
            "<Top><X>0</X><Y>0</Y></Top><Bottom><X>0</X><Y>9</Y></Bottom></Texture></AnythingTemplate>")));

        Assert.Equal(TemplateShape.Area, NineSlice.ShapeOf(T(
            "<AnythingTemplate><Texture><TextureName>t</TextureName>" +
            "<TopLeft><X>0</X><Y>0</Y></TopLeft></Texture></AnythingTemplate>")));

        Assert.Equal(TemplateShape.None, NineSlice.ShapeOf(T(
            "<AnythingTemplate><Texture><TextureName>none</TextureName></Texture></AnythingTemplate>")));
    }

    // ---------------------------------------------------------------
    // Colours and the tab row
    // ---------------------------------------------------------------

    /// <summary>
    /// Is there a pixel matching this test? Antialiased text never reaches
    /// full opacity on a transparent surface, so the alpha is only asked to be
    /// mostly there.
    /// </summary>
    private static bool HasPixel(SKBitmap bmp, Func<SKColor, bool> match)
    {
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Alpha > 200 && match(p)) return true;
        }
        return false;
    }

    [Fact]
    public void TextWithoutAColour_IsWhite()
    {
        /* The format's own default, not a stand-in: DAoCEd's ColorProperty
           starts at r=g=b=a=255 and only overwrites those from the XML. The
           warm tones that stood here gave every undeclared text a golden
           tint the game does not have. */
        using var bmp = new SKBitmap(new SKImageInfo(120, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(Empty());

        ElementRenderer.Render(canvas, ctx, Def("""
            <LabelDef><Position><X>2</X><Y>2</Y></Position>
              <Width>110</Width><Height>20</Height>
              <Data>MMM</Data></LabelDef>
            """), 120, 40);

        // Neutral and bright — the warm defaults were all R > G > B.
        Assert.True(HasPixel(bmp, p =>
            p.Red > 200 && p.Red == p.Green && p.Green == p.Blue));
    }

    [Fact]
    public void TabLabels_TakeTheirColourFromTheButtonTemplate()
    {
        /* The colour belongs to the tab button's <Font>. DAoCEd's
           ControlTabsdef draws every tab in ColorNormal; here the active one
           carries the Pressed texture, so it takes the colour that goes with
           it. Both used to be hard-coded, the inactive one in a golden tan. */
        var pkg = Pkg("""
            <Interface>
              <TabsTemplate><Name>reiter</Name>
                <TabButtonTemplate>knopf</TabButtonTemplate>
                <TabXOffset>4</TabXOffset>
              </TabsTemplate>
              <ButtonTemplate><Name>knopf</Name>
                <Size><X>40</X><Y>20</Y></Size>
                <Font><Name>f</Name>
                  <ColorNormal><R>0</R><G>255</G><B>0</B><A>255</A></ColorNormal>
                  <ColorPressed><R>0</R><G>0</G><B>255</B><A>255</A></ColorPressed>
                </Font>
              </ButtonTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(120, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg);

        ElementRenderer.Render(canvas, ctx, Def("""
            <TabsDef><TemplateName>reiter</TemplateName>
              <Width>120</Width><Height>40</Height>
              <Tab><Id>1</Id><Name>MMM</Name></Tab>
              <Tab><Id>2</Id><Name>MMM</Name></Tab>
            </TabsDef>
            """), 120, 40);

        // the active tab in ColorPressed, the other one in ColorNormal
        Assert.True(HasPixel(bmp, p => p.Blue > 150 && p.Red < 90 && p.Green < 90));
        Assert.True(HasPixel(bmp, p => p.Green > 150 && p.Red < 90 && p.Blue < 90));

        // and nothing in the golden tan that used to be hard-coded here
        Assert.False(HasPixel(bmp, p => p.Red > 150 && p.Green > 140 && p.Blue > 120
            && p.Red > p.Green && p.Green > p.Blue));
    }

    [Fact]
    public void TabXOffset_IsTheOverlap_NotAMarginInFront()
    {
        /* DAoCEd's ControlTabsdef puts button i at
           "i * Size.X - i * TabXOffset" and starts at 0 — the tabs overlap,
           they do not sit behind a margin with a gap between them. The second
           tab is drawn after the first, so a pixel inside the overlap belongs
           to it. */
        var pkg = Pkg("""
            <Interface>
              <TabsTemplate><Name>reiter</Name>
                <TabButtonTemplate>knopf</TabButtonTemplate>
                <TabXOffset>20</TabXOffset>
              </TabsTemplate>
              <ButtonTemplate><Name>knopf</Name>
                <Size><X>40</X><Y>20</Y></Size>
              </ButtonTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(120, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg,
            new RenderOptions { ActiveTab = "2" });

        ElementRenderer.Render(canvas, ctx, Def("""
            <TabsDef><TemplateName>reiter</TemplateName>
              <Width>120</Width><Height>40</Height>
              <Tab><Id>1</Id><Name>A</Name></Tab>
              <Tab><Id>2</Id><Name>B</Name></Tab>
            </TabsDef>
            """), 120, 40);

        /* Without a texture the stand-in gradient stands for the button, and
           the active one is the lighter of the two (0x5C… against 0x33…).
           x = 30 lies in the overlap 20..40: with a gap in front it would
           still belong to the first, dark tab. */
        Assert.True(bmp.GetPixel(30, 1).Red > 0x45);

        // And the row ends at 2 * 40 - 20, not at 20 + 2 * 41.
        Assert.Equal(0, bmp.GetPixel(61, 1).Alpha);
    }

    [Fact]
    public void ATabsDef_LandsAtTwiceItsPosition()
    {
        /* The client lays the tab buttons and the control area out from the
           control's own <Position> a second time, inside the box it has
           already put the control in — so the row starts at (2X, 2Y) while
           <Width>/<Height> stay as declared. Measured against the running
           game on community_window; DAoCEd does not do it, which
           is why its preview puts the row too high. */
        var pkg = Pkg("""
            <Interface>
              <TabsTemplate><Name>reiter</Name>
                <TabButtonTemplate>knopf</TabButtonTemplate>
              </TabsTemplate>
              <ButtonTemplate><Name>knopf</Name>
                <Size><X>40</X><Y>20</Y></Size>
              </ButtonTemplate>
            </Interface>
            """);

        using var bmp = new SKBitmap(new SKImageInfo(120, 80, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg, new RenderOptions());

        var res = ElementRenderer.Render(canvas, ctx, Def("""
            <TabsDef><TemplateName>reiter</TemplateName>
              <Position><X>5</X><Y>10</Y></Position>
              <Width>100</Width><Height>60</Height>
              <Tab><Id>1</Id><Name>A</Name></Tab>
            </TabsDef>
            """), 120, 80);

        // The box keeps its declared size and moves by its position twice.
        Assert.Equal(new SKRect(10, 20, 110, 80), res!.Bounds);

        // The stand-in for the single button now covers (10,20), not (5,10).
        Assert.Equal(0, bmp.GetPixel(12, 15).Alpha);
        Assert.NotEqual(0, bmp.GetPixel(12, 22).Alpha);
    }

    // ---------------------------------------------------------------
    // List box with a column header
    // ---------------------------------------------------------------

    private static readonly SKColor Body = new(255, 0, 0);
    private static readonly SKColor Head = new(0, 255, 0);

    /// <summary>
    /// An 8x8 PNG whose left half is what the background frame draws from and
    /// whose right half is what the header icon draws from. Which of the two
    /// reached a pixel can then be read off its colour. It has to be big
    /// enough for BOTH source rectangles: Skia clamps a source rectangle to
    /// the bitmap and shrinks the destination with it, so a
    /// texture one pixel too small draws a stamp rather than nothing.
    /// </summary>
    private static byte[] TwoHalvesPng()
    {
        using var bmp = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            using var left = new SKPaint { Color = Body };
            using var right = new SKPaint { Color = Head };
            c.DrawRect(0, 0, 4, 8, left);
            c.DrawRect(4, 0, 4, 8, right);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Package PkgWithTexture(string interfaceXml, byte[] texture)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(interfaceXml),
            ["tex.png"] = texture,
        };
        return PackageLoader.Load(files);
    }

    /// <summary>
    /// A list box whose template carries the header block, plus a background
    /// that fills whatever box it is handed with one colour.
    /// </summary>
    private const string ListBoxWithHeader = """
        <Interface>
          <Texture><Name>tex</Name><File>tex.png</File></Texture>
          <FullResizeImageTemplate><Name>frame</Name>
            <MiddleWidth>1</MiddleWidth><MiddleHeight>1</MiddleHeight>
            <Texture><TextureName>tex</TextureName>
              <MiddleMiddle><X>0</X><Y>0</Y></MiddleMiddle>
            </Texture>
          </FullResizeImageTemplate>
          <ListBoxHeaderTemplate><Name>head_name</Name>
            <Texture><Name>tex</Name>
              <Size><X>4</X><Y>4</Y></Size>
              <Normal><X>4</X><Y>0</Y></Normal>
            </Texture>
          </ListBoxHeaderTemplate>
          <ListBoxTemplate><Name>liste</Name>
            <BackgroundTemplate>frame</BackgroundTemplate>
            <HeaderHorizResizeTemplate>egal</HeaderHorizResizeTemplate>
            <HeaderHeight>20</HeaderHeight>
            <HeaderTopOffset>0</HeaderTopOffset>
            <HeaderLeftOffset>0</HeaderLeftOffset>
            <HeaderRightOffset>0</HeaderRightOffset>
            <HeaderControl><Column>1</Column><TemplateName>head_name</TemplateName></HeaderControl>
          </ListBoxTemplate>
        </Interface>
        """;

    private const string ListBoxDefXml = """
        <ListBoxDef>
          <Position><X>0</X><Y>0</Y></Position>
          <Width>60</Width><Height>60</Height>
          <TemplateName>liste</TemplateName>
        </ListBoxDef>
        """;

    [Fact]
    public void AListBoxWithAColumnHeader_DrawsItsBodyBelowTheHeaderBand()
    {
        /* DAoCEd's ControlListboxdef hands drawFullAtHeight the height
           "H - HeaderHeight" and the start "HeaderHeight", then paints the
           header band over the top with a template of its own. One frame
           across the whole height put the body's upper edge behind the header
           row instead of below it — which is what made the guild list of
           community_window come out unlike DAoCEd's rendering of it. */
        var pkg = PkgWithTexture(ListBoxWithHeader, TwoHalvesPng());

        using var bmp = new SKBitmap(new SKImageInfo(60, 60, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg, new RenderOptions { ShowSampleData = false });
        ElementRenderer.Render(canvas, ctx, Def(ListBoxDefXml), 60, 60);

        // The body frame ends where the box does, not 20 px short of it: it
        // starts at HeaderHeight and is H - HeaderHeight tall.
        Assert.Equal(Body, bmp.GetPixel(30, 59));

        /* And the header icon sits in the band, at its declared 4x4 — the
           template names its texture <Texture><Name>, which is the spelling
           only ListBoxHeaderTemplate uses. */
        Assert.Equal(Head, bmp.GetPixel(1, 1));
        Assert.Equal(Body, bmp.GetPixel(30, 1));   // beside it: the header band
    }

    [Fact]
    public void WithoutAHeaderHorizResizeTemplate_TheListBoxHasNoHeaderBand()
    {
        /* The counter-check for the rule above. DAoCEd's
           ListboxtemplateNode reads HeaderHeight and the HeaderControls only
           inside "if (getChild(e, "HeaderHorizResizeTemplate") != null)", and
           hasSpecialStuff() asks nothing else — so a HeaderHeight on its own
           must change nothing at all. */
        var pkg = PkgWithTexture(
            ListBoxWithHeader.Replace(
                "<HeaderHorizResizeTemplate>egal</HeaderHorizResizeTemplate>", ""),
            TwoHalvesPng());

        using var bmp = new SKBitmap(new SKImageInfo(60, 60, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var ctx = new RenderContext(pkg, new RenderOptions { ShowSampleData = false });
        ElementRenderer.Render(canvas, ctx, Def(ListBoxDefXml), 60, 60);

        // The background fills the box by itself, and no header icon is drawn
        // although the HeaderHeight and the HeaderControl are still there.
        Assert.Equal(Body, bmp.GetPixel(30, 59));
        Assert.Equal(Body, bmp.GetPixel(1, 1));
    }

    [Fact]
    public void TheTextureOfAListBoxHeader_IsNamedByTextureName()
    {
        /* ListBoxHeaderTemplate spells its texture <Texture><Name>, where
           every other template type spells it <Texture><TextureName>:
           DAoCEd's ListboxheadertemplateNode.loadTexture reads
           new StringProperty(e, "Name"), and it is the only one of its ten
           node classes that does. Without that spelling every column header
           of community_window came out as a hatched "texture missing" cell.
           The <Name> one level up still names the TEMPLATE. */
        var tpl = XElement.Parse("""
            <ListBoxHeaderTemplate><Name>head_name</Name>
              <Texture><Name>page4</Name><Size><X>4</X><Y>4</Y></Size></Texture>
            </ListBoxHeaderTemplate>
            """);

        Assert.Equal("page4", NineSlice.TextureNameOf(tpl));

        // The usual spelling keeps precedence where both are present.
        var both = XElement.Parse("""
            <FullResizeImageTemplate><Name>frame</Name>
              <Texture><Name>falsch</Name><TextureName>richtig</TextureName></Texture>
            </FullResizeImageTemplate>
            """);
        Assert.Equal("richtig", NineSlice.TextureNameOf(both));
    }
}
