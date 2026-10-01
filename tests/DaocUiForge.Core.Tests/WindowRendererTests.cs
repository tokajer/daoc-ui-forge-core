using System.Text;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The regression net for <see cref="WindowRenderer"/>. The focus is on the
/// rules that only arise at window level: tabs, content extent
/// (<c>GrowWidth</c>/<c>GrowHeight</c>), the title zone, and overflow past the
/// window edge.
/// </summary>
public class WindowRendererTests
{
    /// <summary>A package from one XML snippet, without textures.</summary>
    private static Package Pkg(string interfaceXml, params (string path, byte[] data)[] extra)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["windows.xml"] = Encoding.UTF8.GetBytes(interfaceXml),
        };
        foreach (var (path, data) in extra) files[path] = data;
        return PackageLoader.Load(files);
    }

    /// <summary>Draw one window. The caller disposes the image.</summary>
    private static RenderedWindow Render(Package pkg, string winId, RenderOptions? opt = null)
    {
        using var ctx = new RenderContext(pkg, opt ?? new RenderOptions());
        var win = pkg.Windows.Single(w => w.Id == winId);
        return WindowRenderer.Render(ctx, win);
    }

    /// <summary>Names of the drawn elements — for checking who was left in.</summary>
    private static string[] NamesOf(RenderedWindow w) =>
        w.Elements.Select(e => Xml.NameOf(e.Def, e.Tag)).ToArray();

    /// <summary>A single-colour PNG as a texture (the TextureCache leaves PNG to Skia).</summary>
    private static byte[] SolidPng(int w, int h, SKColor color)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) c.Clear(color);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Templates for the pixel tests: a 10×10 area and a stretchable background.</summary>
    private const string TextureXml = """
          <Texture><Name>tex</Name><File>tex.png</File></Texture>
          <ImageAreaTemplate><Name>red</Name>
            <Texture><TextureName>tex</TextureName>
              <TopLeft><X>0</X><Y>0</Y></TopLeft></Texture>
            <Size><X>10</X><Y>10</Y></Size>
          </ImageAreaTemplate>
          <FullResizeImageTemplate><Name>area</Name>
            <Texture><TextureName>tex</TextureName>
              <MiddleMiddle><X>0</X><Y>0</Y></MiddleMiddle></Texture>
            <MiddleWidth>4</MiddleWidth><MiddleHeight>4</MiddleHeight>
          </FullResizeImageTemplate>
        """;

    private static Package PkgWithTexture(string interfaceXml) =>
        Pkg(interfaceXml, ("tex.png", SolidPng(32, 32, new SKColor(255, 0, 0))));

    // ---------------------------------------------------------------
    // Title area
    // ---------------------------------------------------------------

    [Fact]
    public void TitleHeight_IsNotPainted()
    {
        /* The rule that saved the flat windows: <TitleHeight> is the drag zone
           for the mouse, not a bar. A 15 px high window (Clock) would
           otherwise be covered completely. */
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>60</Width><Height>40</Height>
              <TitleWidth>60</TitleWidth><TitleHeight>20</TitleHeight>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(20, res.Chrome.TitleHeight);   // read …
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 60; x++)
            Assert.Equal(0, res.Bitmap.GetPixel(x, y).Alpha);   // … but not painted
    }

    [Fact]
    public void TitleHeight_IsShownAsAZoneOnlyOnRequest()
    {
        // In the original the outline is transparent without #canvasHost.showzones.
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>60</Width><Height>40</Height>
              <TitleWidth>60</TitleWidth><TitleHeight>20</TitleHeight>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w", new RenderOptions { ShowZones = true });

        Assert.True(res.Bitmap.GetPixel(30, 10).Alpha > 0, "the zone should be hinted at");
        Assert.Equal(0, res.Bitmap.GetPixel(30, 30).Alpha);   // not below the zone
    }

    // ---------------------------------------------------------------
    // Tabs
    // ---------------------------------------------------------------

    /// <summary>
    /// A window with two tabs; one text field each hangs off one of them
    /// through &lt;ControlId&gt;, a third off none.
    /// </summary>
    private const string TabWindow = """
        <Interface><WindowTemplate><Name>w</Name>
          <Width>200</Width><Height>60</Height>
          <TabsDef><Name>strip</Name><Width>200</Width><Height>18</Height>
            <Tab><Id>1</Id><Name>First</Name></Tab>
            <Tab><Id>2</Id><Name>Second</Name></Tab>
            <TabControl><TabId>1</TabId><ControlId>10</ControlId></TabControl>
            <TabControl><TabId>2</TabId><ControlId>20</ControlId></TabControl>
          </TabsDef>
          <LabelDef><Name>only_one</Name><ControlId>10</ControlId>
            <Position><X>0</X><Y>20</Y></Position>
            <Width>40</Width><Height>12</Height><Label>a</Label></LabelDef>
          <LabelDef><Name>only_two</Name><ControlId>20</ControlId>
            <Position><X>0</X><Y>40</Y></Position>
            <Width>40</Width><Height>12</Height><Label>b</Label></LabelDef>
          <LabelDef><Name>always</Name>
            <Position><X>100</X><Y>20</Y></Position>
            <Width>40</Width><Height>12</Height><Label>c</Label></LabelDef>
        </WindowTemplate></Interface>
        """;

    [Fact]
    public void WithoutARequest_TheFirstTabIsActive()
    {
        var pkg = Pkg(TabWindow);
        using var res = Render(pkg, "w");

        Assert.Equal(new[] { "1", "2" }, res.Tabs.Select(t => t.Id));
        Assert.Equal("First", res.Tabs[0].Name);
        Assert.Equal("1", res.ActiveTab);
    }

    /// <summary>
    /// The table the element tree labels its rows from. It has to be the very
    /// one the drawing decides visibility with, otherwise the tree claims a tab
    /// the picture does not agree with.
    /// </summary>
    [Fact]
    public void TabMembers_ReportWhichTabsAnElementBelongsTo()
    {
        var pkg = Pkg(TabWindow);
        using var res = Render(pkg, "w");

        Assert.Equal(new[] { "1" }, res.TabMembers["10"]);
        Assert.Equal(new[] { "2" }, res.TabMembers["20"]);
        // The element without a ControlId belongs to no tab and is always visible.
        Assert.Equal(2, res.TabMembers.Count);
    }

    [Fact]
    public void WithoutTabs_TabMembersIsEmpty()
    {
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>50</Width><Height>50</Height>
              <LabelDef><Name>a</Name><ControlId>10</ControlId><Label>a</Label></LabelDef>
            </WindowTemplate></Interface>
            """);
        using var res = Render(pkg, "w");

        Assert.Empty(res.TabMembers);
    }

    [Fact]
    public void ElementsOfOtherTabs_AreNotDrawn()
    {
        var pkg = Pkg(TabWindow);

        using var firstTab = Render(pkg, "w");
        Assert.Contains("only_one", NamesOf(firstTab));
        Assert.DoesNotContain("only_two", NamesOf(firstTab));
        Assert.Contains("always", NamesOf(firstTab));   // without a ControlId: always visible

        using var secondTab = Render(pkg, "w", new RenderOptions { ActiveTab = "2" });
        Assert.Contains("only_two", NamesOf(secondTab));
        Assert.DoesNotContain("only_one", NamesOf(secondTab));
    }

    [Fact]
    public void AnUnknownTab_FallsBackToTheFirst()
    {
        // In the original a global activeTab that still held the id of the
        // previous window after a window switch.
        var pkg = Pkg(TabWindow);
        using var res = Render(pkg, "w", new RenderOptions { ActiveTab = "99" });

        Assert.Equal("1", res.ActiveTab);
        Assert.Contains("only_one", NamesOf(res));
    }

    [Fact]
    public void DefCount_CountsSkippedElementsToo()
    {
        // The original shows "N elements" — meaning the nodes in the window,
        // not the ones actually drawn.
        var pkg = Pkg(TabWindow);
        using var res = Render(pkg, "w");

        Assert.Equal(4, res.DefCount);       // TabsDef plus three text fields
        Assert.Equal(3, res.Elements.Count); // one belongs to the other tab
    }

    // ---------------------------------------------------------------
    // Content extent
    // ---------------------------------------------------------------

    [Fact]
    public void HiddenTabs_DoNotStretchTheWindow()
    {
        /* The case that made the community window 690 wide instead of 605: if
           the elements of ALL tabs count, the window grows to the sum of all
           tab contents. */
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>200</Width><Height>60</Height>
              <TabsDef><Name>strip</Name><Width>200</Width><Height>18</Height>
                <Tab><Id>1</Id><Name>One</Name></Tab>
                <Tab><Id>2</Id><Name>Two</Name></Tab>
                <TabControl><TabId>2</TabId><ControlId>20</ControlId></TabControl>
              </TabsDef>
              <LabelDef><Name>wide_on_the_second</Name><ControlId>20</ControlId>
                <Position><X>200</X><Y>20</Y></Position>
                <Width>85</Width><Height>12</Height><Label>x</Label></LabelDef>
            </WindowTemplate></Interface>
            """);

        using var firstTab = Render(pkg, "w");
        Assert.Equal(200, firstTab.ContentWidth);   // not 285

        using var secondTab = Render(pkg, "w", new RenderOptions { ActiveTab = "2" });
        Assert.Equal(285, secondTab.ContentWidth);  // there it counts
    }

    [Fact]
    public void AGrowingElement_CountsWithItsStartingPointOnly()
    {
        /* Otherwise the window size ratchets up: the growing element grows to
           the content width, which grows again as a result … */
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>100</Width><Height>50</Height>
              <FullResizeImageDef><Name>bg</Name>
                <Position><X>120</X><Y>0</Y></Position>
                <Width>90</Width><Height>20</Height>
                <Alignment><GrowWidth>true</GrowWidth></Alignment>
              </FullResizeImageDef>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(120, res.ContentWidth);   // the starting point, not 120 + 90
    }

    [Fact]
    public void CentredAndEdgeAnchoredElements_DoNotCount()
    {
        // Their position only comes out of the window size that is being
        // established here, so they must not influence it.
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>100</Width><Height>50</Height>
              <LabelDef><Name>centred</Name>
                <Position><X>0</X><Y>200</Y></Position>
                <Width>300</Width><Height>12</Height><Label>a</Label>
                <Alignment><CenterHorizontally>true</CenterHorizontally></Alignment></LabelDef>
              <LabelDef><Name>right</Name>
                <Position><X>400</X><Y>0</Y></Position>
                <Width>50</Width><Height>12</Height><Label>b</Label>
                <Alignment><OffsetRight>true</OffsetRight></Alignment></LabelDef>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(100, res.ContentWidth);
        Assert.Equal(50, res.ContentHeight);
        Assert.False(res.Grows);
    }

    [Fact]
    public void AnInvisibleElement_IsSkipped()
    {
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>100</Width><Height>50</Height>
              <LabelDef><Name>gone</Name><Visible>false</Visible>
                <Position><X>0</X><Y>0</Y></Position>
                <Width>200</Width><Height>12</Height><Label>a</Label></LabelDef>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Empty(res.Elements);
        Assert.Equal(100, res.ContentWidth);   // and it does not stretch the window either
    }

    [Fact]
    public void GrowWidth_FillsUpToTheMeasuredContentWidth()
    {
        /* The handover between the two levels: the WindowRenderer measures the
           content, the ElementRenderer reads it from the RenderContext. Without
           the measurement the background ends at the declared width and the
           grown window has a bare strip. */
        var pkg = PkgWithTexture($"""
            <Interface>
            {TextureXml}
              <WindowTemplate><Name>w</Name>
                <Width>100</Width><Height>40</Height>
                <FullResizeImageDef><Name>bg</Name>
                  <TemplateName>area</TemplateName>
                  <Position><X>0</X><Y>0</Y></Position>
                  <Width>100</Width><Height>20</Height>
                  <Alignment><GrowWidth>true</GrowWidth></Alignment></FullResizeImageDef>
                <LabelDef><Name>far_right</Name>
                  <Position><X>150</X><Y>25</Y></Position>
                  <Width>10</Width><Height>10</Height><Label>x</Label></LabelDef>
              </WindowTemplate>
            </Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(160, res.ContentWidth);          // 150 + 10
        Assert.Equal(255, res.Bitmap.GetPixel(155, 10).Red);   // the background reaches there
    }

    // ---------------------------------------------------------------
    // Overflow past the window edge
    // ---------------------------------------------------------------

    [Fact]
    public void ANegativePosition_MakesRoomInTheImage_AndShiftsThePlaces()
    {
        /* The compass sits at negative positions in a 14×14 window. The engine
           does not clip that — an image would have to, so it grows to the left
           and top. The places of the elements have to move along, otherwise
           mouse picking points at the wrong spot. */
        var pkg = PkgWithTexture($"""
            <Interface>
            {TextureXml}
              <WindowTemplate><Name>w</Name>
                <Width>60</Width><Height>60</Height>
                <ImageAreaDef><Name>past_the_edge</Name>
                  <TemplateName>red</TemplateName>
                  <Position><X>-10</X><Y>-5</Y></Position></ImageAreaDef>
              </WindowTemplate>
            </Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(new SKPointI(10, 5), res.Origin);
        Assert.Equal(70, res.Bitmap.Width);
        Assert.Equal(65, res.Bitmap.Height);

        // Place in image coordinates: (-10,-5) + Origin = (0,0)
        Assert.Equal(new SKRect(0, 0, 10, 10), res.Elements[0].Bounds);
        Assert.Equal(255, res.Bitmap.GetPixel(5, 5).Red);
    }

    [Fact]
    public void ContentBeyondTheDeclaredSize_EnlargesTheImage()
    {
        var pkg = PkgWithTexture($"""
            <Interface>
            {TextureXml}
              <WindowTemplate><Name>w</Name>
                <Width>100</Width><Height>50</Height>
                <ImageAreaDef><Name>sticks_out</Name>
                  <TemplateName>red</TemplateName>
                  <Position><X>95</X><Y>40</Y></Position></ImageAreaDef>
              </WindowTemplate>
            </Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(105, res.Bitmap.Width);
        Assert.Equal(50, res.Bitmap.Height);
        Assert.Equal(new SKPointI(0, 0), res.Origin);
        Assert.Equal(255, res.Bitmap.GetPixel(100, 45).Red);   // beyond the declaration

        // Guide around the declared size: somewhere on the right edge
        // (column 99) there has to be a dash of the dashed line.
        bool line = Enumerable.Range(0, 50)
            .Any(y => res.Bitmap.GetPixel(99, y).Alpha > 0);
        Assert.True(line, "the guide for the declared window size is missing");
    }

    [Fact]
    public void WithoutOverflow_TheImageStaysAsLargeAsDeclared()
    {
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>80</Width><Height>30</Height>
              <LabelDef><Name>inside</Name>
                <Position><X>5</X><Y>5</Y></Position>
                <Width>20</Width><Height>10</Height><Label>a</Label></LabelDef>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(80, res.Bitmap.Width);
        Assert.Equal(30, res.Bitmap.Height);
        Assert.False(res.Grows);
        Assert.False(res.Clipped);
        // No guide: the right edge stays empty.
        Assert.All(Enumerable.Range(0, 30),
            y => Assert.Equal(0, res.Bitmap.GetPixel(79, y).Alpha));
    }

    [Fact]
    public void MissingSizes_GiveThreeHundredByTwoHundred()
    {
        // The fallback of the original (line 2007).
        var pkg = Pkg("<Interface><WindowTemplate><Name>w</Name></WindowTemplate></Interface>");
        using var res = Render(pkg, "w");

        Assert.Equal(300, res.DeclaredWidth);
        Assert.Equal(200, res.DeclaredHeight);
        Assert.Equal(300, res.Bitmap.Width);
        Assert.Equal(200, res.Bitmap.Height);
    }

    [Fact]
    public void EndAligned_DoesNotMoveTheTextInsideItsWidth()
    {
        /* Measured against the running game on summary.xml (2026-08-07): both
           of its end-aligned labels draw their text starting at <Position>,
           4 px and 3.4 px to the left of where aligning inside <Width> would
           put them. The HTML original read the tag as a right alignment.
           <Width> is no more a layout box for a label than <Height> is,
           so the flag must not shift anything. */
        const string Label = """
              <LabelDef><Position><X>4</X><Y>4</Y></Position>
                <Width>90</Width><Height>14</Height>
                <FontName>f11</FontName><Data>7</Data>{0}
              </LabelDef>
            """;

        string Window(string flag) =>
            "<Interface><WindowTemplate><Name>w</Name><Width>100</Width><Height>24</Height>"
            + Label.Replace("{0}", flag) + "</WindowTemplate></Interface>";

        static int FirstInkColumn(SKBitmap bmp)
        {
            for (int x = 0; x < bmp.Width; x++)
                for (int y = 0; y < bmp.Height; y++)
                    if (bmp.GetPixel(x, y).Alpha > 0) return x;
            return -1;
        }

        using var plain = Render(Pkg(Window("")), "w");
        using var aligned = Render(Pkg(Window("<EndAligned>true</EndAligned>")), "w");

        int at = FirstInkColumn(plain.Bitmap);
        Assert.True(at > 0, "the label has to draw something");
        Assert.Equal(at, FirstInkColumn(aligned.Bitmap));
    }

    // ---------------------------------------------------------------
    // Native tab row (<TabName>, distinct from <TabsDef>)
    // ---------------------------------------------------------------

    [Fact]
    public void TabName_DrawsAPlaceholderRowByDefault()
    {
        /* <TabName> is chrome the game client paints itself — neither DAoCEd
           nor the original HTML port ever draws it (DAoCEd keeps the names
           only for the property editor and XML round-trip). ShowNativeTabs defaults to true because,
           unlike the title drag zone, this chrome IS visible in the game. */
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>60</Width><Height>40</Height>
              <TabName>Main</TabName><TabName>Guild</TabName>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.True(res.Bitmap.GetPixel(5, 5).Alpha > 0, "the placeholder row should be hinted at");
        Assert.Equal(0, res.Bitmap.GetPixel(5, 30).Alpha);   // not below the row
    }

    [Fact]
    public void TabName_PlaceholderCanBeTurnedOff()
    {
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>60</Width><Height>40</Height>
              <TabName>Main</TabName>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w", new RenderOptions { ShowNativeTabs = false });

        Assert.Equal(0, res.Bitmap.GetPixel(5, 5).Alpha);
    }

    [Fact]
    public void WithoutTabName_NothingIsPainted()
    {
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>60</Width><Height>40</Height>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(0, res.Bitmap.GetPixel(5, 5).Alpha);
    }

    // ---------------------------------------------------------------
    // Frame data and states
    // ---------------------------------------------------------------

    [Fact]
    public void CornerButtons_AreReported_ButNotPainted()
    {
        /* Deliberate precision: in the original those become <div> elements
           without any CSS rule — so without size and without colour. Inventing
           a size would mean painting pixels the format does not name. */
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>40</Width><Height>40</Height>
              <CloseButton>true</CloseButton><MoveButton>true</MoveButton>
              <BottomRightResizeButton>true</BottomRightResizeButton>
              <ResizeButtonOffsetX>3</ResizeButtonOffsetX>
              <ResizeButtonOffsetY>4</ResizeButtonOffsetY>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.True(res.Chrome.CloseButton);
        Assert.True(res.Chrome.MoveButton);
        Assert.True(res.Chrome.BottomRightResizeButton);
        Assert.False(res.Chrome.TopRightResizeButton);
        Assert.Equal(new SKPoint(3, 4), res.Chrome.ResizeButtonOffset);

        for (int y = 0; y < 40; y++)
        for (int x = 0; x < 40; x++)
            Assert.Equal(0, res.Bitmap.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void EffectIcons_AndGroupColours_AreRecognised()
    {
        // Only with them is the state selector of the preview worth showing.
        var without = Pkg("""
            <Interface><WindowTemplate><Name>w</Name><Width>20</Width><Height>20</Height>
              <IconDef><TemplateName>plain_icon</TemplateName></IconDef>
            </WindowTemplate></Interface>
            """);
        using (var res = Render(without, "w")) Assert.False(res.HasEffects);

        var withEffect = Pkg("""
            <Interface><WindowTemplate><Name>w</Name><Width>20</Width><Height>20</Height>
              <IconDef><TemplateName>healthbar_mez</TemplateName></IconDef>
            </WindowTemplate></Interface>
            """);
        using (var res = Render(withEffect, "w")) Assert.True(res.HasEffects);

        var withColour = Pkg("""
            <Interface><WindowTemplate><Name>w</Name><Width>20</Width><Height>20</Height>
              <LabelDef><ColorAdapter>group_color3</ColorAdapter><Label>a</Label></LabelDef>
            </WindowTemplate></Interface>
            """);
        using (var res = Render(withColour, "w")) Assert.True(res.HasEffects);
    }

    [Fact]
    public void AnUnknownElementType_DoesNotEndUpInTheResult()
    {
        // The default: return null branch of the original.
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>50</Width><Height>50</Height>
              <CompletelyUnknownDef><Name>alien</Name>
                <Width>10</Width><Height>10</Height></CompletelyUnknownDef>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Empty(res.Elements);
        Assert.Equal(1, res.DefCount);   // it is counted regardless
        Assert.Empty(res.Failures);
    }

    [Fact]
    public void AMissingTemplate_IsCounted()
    {
        var pkg = Pkg("""
            <Interface><WindowTemplate><Name>w</Name>
              <Width>50</Width><Height>50</Height>
              <ImageAreaDef><Name>without</Name>
                <TemplateName>does_not_exist</TemplateName>
                <Width>10</Width><Height>10</Height></ImageAreaDef>
            </WindowTemplate></Interface>
            """);

        using var res = Render(pkg, "w");

        Assert.Equal(1, res.MissingTemplateCount);
        Assert.Equal("does_not_exist", res.Elements[0].MissingTemplate);
    }
}
