using System.Text;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The picture of a single template: which drawing function applies, how large
/// it comes out, and what happens to a template that only names others.
///
/// <para>The shape is deliberately read off the XML rather than off the type
/// name (there are around forty types and no list of them), so the
/// cases here are shapes, not types.</para>
/// </summary>
public class TemplatePreviewTests
{
    /// <summary>
    /// A 64 x 64 texture: red everywhere, green at (32,0) 16 x 16, blue at
    /// (0,24) 8 x 8. That makes a slice identifiable by its colour.
    /// </summary>
    private static byte[] Texture()
    {
        using var bmp = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(255, 0, 0));
            using var green = new SKPaint { Color = new SKColor(0, 255, 0) };
            c.DrawRect(32, 0, 16, 16, green);
            using var blue = new SKPaint { Color = new SKColor(0, 0, 255) };
            c.DrawRect(0, 24, 8, 8, blue);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private const string Templates = """
        <Interface>
          <Texture><Name>tex</Name><File>tex.png</File></Texture>

          <HorizontalResizeImageTemplate><Name>bar</Name>
            <Height>6</Height>
            <LeftWidth>4</LeftWidth><RepeatWidth>10</RepeatWidth><RightWidth>4</RightWidth>
            <Texture><TextureName>tex</TextureName>
              <Left><X>0</X><Y>0</Y></Left>
              <Repeat><X>0</X><Y>0</Y></Repeat>
              <Right><X>0</X><Y>0</Y></Right>
            </Texture>
          </HorizontalResizeImageTemplate>

          <VerticalResizeImageTemplate><Name>thumb</Name>
            <Width>7</Width>
            <TopHeight>5</TopHeight><RepeatHeight>3</RepeatHeight><BottomHeight>5</BottomHeight>
            <Texture><TextureName>tex</TextureName>
              <Top><X>0</X><Y>0</Y></Top>
              <Repeat><X>0</X><Y>0</Y></Repeat>
              <Bottom><X>0</X><Y>0</Y></Bottom>
            </Texture>
          </VerticalResizeImageTemplate>

          <FullResizeImageTemplate><Name>frame</Name>
            <LeftWidth>10</LeftWidth><MiddleWidth>40</MiddleWidth><RightWidth>10</RightWidth>
            <TopHeight>2</TopHeight><MiddleHeight>4</MiddleHeight><BottomHeight>2</BottomHeight>
            <Texture><TextureName>tex</TextureName>
              <TopLeft><X>0</X><Y>0</Y></TopLeft>
              <TopMiddle><X>0</X><Y>0</Y></TopMiddle>
              <TopRight><X>0</X><Y>0</Y></TopRight>
              <MiddleLeft><X>0</X><Y>0</Y></MiddleLeft>
              <MiddleMiddle><X>0</X><Y>0</Y></MiddleMiddle>
              <MiddleRight><X>0</X><Y>0</Y></MiddleRight>
              <BottomLeft><X>0</X><Y>0</Y></BottomLeft>
              <BottomMiddle><X>0</X><Y>0</Y></BottomMiddle>
              <BottomRight><X>0</X><Y>0</Y></BottomRight>
            </Texture>
          </FullResizeImageTemplate>

          <ImageAreaTemplate><Name>badge</Name>
            <TextureName>tex</TextureName>
            <TopLeft><X>32</X><Y>0</Y></TopLeft>
            <Size><X>16</X><Y>16</Y></Size>
          </ImageAreaTemplate>

          <StatusIconTemplate><Name>levels</Name>
            <TextureName>tex</TextureName>
            <TextureStart><X>0</X><Y>0</Y></TextureStart>
            <Width>8</Width><Height>8</Height>
            <MaxLevels>4</MaxLevels>
            <Horizontal>false</Horizontal>
          </StatusIconTemplate>

          <StatusBarTemplate><Name>health</Name>
            <ForegroundHResizeTemplate>bar</ForegroundHResizeTemplate>
            <BackgroundHResizeTemplate>bar</BackgroundHResizeTemplate>
            <Height>6</Height>
          </StatusBarTemplate>

          <ButtonTemplate><Name>invisible</Name>
            <Size><X>10</X><Y>10</Y></Size>
            <Texture><TextureName>none</TextureName>
              <Normal><X>0</X><Y>0</Y></Normal>
            </Texture>
          </ButtonTemplate>

          <ListBoxTemplate><Name>plain</Name>
            <LinePadding>4</LinePadding>
            <IconTemplate>nothing_of_that_name</IconTemplate>
          </ListBoxTemplate>

          <ImageAreaTemplate><Name>lost</Name>
            <TextureName>undeclared</TextureName>
            <TopLeft><X>0</X><Y>0</Y></TopLeft>
            <Size><X>4</X><Y>4</Y></Size>
          </ImageAreaTemplate>

          <ImageAreaTemplate><Name>enormous</Name>
            <TextureName>tex</TextureName>
            <TopLeft><X>0</X><Y>0</Y></TopLeft>
            <Size><X>9000</X><Y>10</Y></Size>
          </ImageAreaTemplate>
        </Interface>
        """;

    private static Package Pkg()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["templates.xml"] = Encoding.UTF8.GetBytes(Templates),
            ["tex.png"] = Texture(),
        };
        return PackageLoader.Load(files);
    }

    private static TemplatePreviewPlan Plan(Package pkg, string name) =>
        TemplatePreview.Plan(pkg, pkg.ByNameLc[name]);

    // -----------------------------------------------------------------
    // Which shape, and how large
    // -----------------------------------------------------------------

    [Fact]
    public void A_horizontal_template_is_as_wide_as_its_three_parts()
    {
        var plan = Plan(Pkg(), "bar");

        Assert.Equal(TemplateShape.HResize, plan.Shape);
        Assert.Equal(18, plan.Width);      // 4 + 10 + 4, the repeat once
        Assert.Equal(6, plan.Height);      // <Height>
        Assert.Equal("", plan.Through);
    }

    [Fact]
    public void A_vertical_template_is_as_tall_as_its_three_parts()
    {
        var plan = Plan(Pkg(), "thumb");

        Assert.Equal(TemplateShape.VResize, plan.Shape);
        Assert.Equal(7, plan.Width);       // <Width>
        Assert.Equal(13, plan.Height);     // 5 + 3 + 5
    }

    [Fact]
    public void A_nine_field_template_shows_one_middle_tile()
    {
        var plan = Plan(Pkg(), "frame");

        Assert.Equal(TemplateShape.FullResize, plan.Shape);
        Assert.Equal(60, plan.Width);      // 10 + 40 + 10
        Assert.Equal(8, plan.Height);      // 2 + 4 + 2
    }

    [Fact]
    public void A_plain_slice_takes_its_size_from_the_template()
    {
        var plan = Plan(Pkg(), "badge");

        Assert.Equal(TemplateShape.Area, plan.Shape);
        Assert.Equal(16, plan.Width);
        Assert.Equal(16, plan.Height);
    }

    [Fact]
    public void A_sprite_sheet_is_one_level_large()
    {
        var plan = Plan(Pkg(), "levels");

        Assert.Equal(TemplateShape.IconSheet, plan.Shape);
        Assert.Equal(8, plan.Width);
        Assert.Equal(8, plan.Height);
    }

    /// <summary>
    /// "none" is the format's way of saying "no texture here" (a pure click
    /// area, say). There is nothing to show, and a red cross saying "missing"
    /// would be wrong.
    /// </summary>
    [Fact]
    public void A_texture_called_none_is_not_a_missing_texture()
    {
        Assert.Equal(TemplateShape.None, Plan(Pkg(), "invisible").Shape);
    }

    // -----------------------------------------------------------------
    // One level of indirection
    // -----------------------------------------------------------------

    /// <summary>
    /// 146 of the 1086 templates in the reference package are status bars, and
    /// none of them carries a texture: they name a pair of horizontal bars.
    /// </summary>
    [Fact]
    public void A_template_without_a_texture_is_shown_through_the_first_it_names()
    {
        var pkg = Pkg();
        var plan = Plan(pkg, "health");

        Assert.Equal(TemplateShape.HResize, plan.Shape);
        Assert.Equal("ForegroundHResizeTemplate", plan.Through);
        Assert.Same(pkg.ByNameLc["bar"], plan.Node);
    }

    [Fact]
    public void A_name_that_leads_nowhere_leaves_nothing_to_show()
    {
        var plan = Plan(Pkg(), "plain");

        Assert.Equal(TemplateShape.None, plan.Shape);
        Assert.Equal("", plan.Through);
    }

    // -----------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------

    [Fact]
    public void The_bitmap_is_the_planned_size_and_holds_the_named_slice()
    {
        var pkg = Pkg();
        using var ctx = new RenderContext(pkg);

        using var bmp = TemplatePreview.Render(ctx, pkg.ByNameLc["badge"]);

        Assert.NotNull(bmp);
        Assert.Equal(16, bmp.Width);
        Assert.Equal(16, bmp.Height);
        // <TopLeft> 32,0 is the green square, not the red field around it.
        Assert.Equal(new SKColor(0, 255, 0), bmp.GetPixel(8, 8));
    }

    /// <summary>
    /// The full level, not level 0: on most of these level 0 is the empty one,
    /// and it is what the drawing layer shows without sample data.
    /// </summary>
    [Fact]
    public void A_sprite_sheet_shows_its_full_level()
    {
        var pkg = Pkg();
        using var ctx = new RenderContext(pkg);

        using var bmp = TemplatePreview.Render(ctx, pkg.ByNameLc["levels"]);

        Assert.NotNull(bmp);
        // Level 3 of 4 sits at y = 3 * 8 = 24, which is the blue square.
        Assert.Equal(new SKColor(0, 0, 255), bmp.GetPixel(4, 4));
    }

    [Fact]
    public void A_texture_that_cannot_be_loaded_gives_no_picture()
    {
        var pkg = Pkg();
        using var ctx = new RenderContext(pkg);

        Assert.Null(TemplatePreview.Render(ctx, pkg.ByNameLc["lost"]));
    }

    /// <summary>
    /// The emergency bound. A mistyped &lt;Size&gt; must not allocate hundreds
    /// of megabytes for a thumbnail.
    /// </summary>
    [Fact]
    public void A_size_past_the_bound_gives_no_picture()
    {
        var pkg = Pkg();
        using var ctx = new RenderContext(pkg);

        Assert.Equal(TemplateShape.Area, Plan(pkg, "enormous").Shape);
        Assert.Null(TemplatePreview.Render(ctx, pkg.ByNameLc["enormous"]));
    }
}
