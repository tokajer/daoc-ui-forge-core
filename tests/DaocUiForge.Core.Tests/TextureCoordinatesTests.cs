using System.Xml.Linq;
using DaocUiForge.Core.Editing;
using DaocUiForge.Core.Model;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The model behind the texture editor: which pieces of a texture a template
/// names, and how large each of them is. The sizes have to be the ones
/// <c>NineSlice</c> draws — a frame at a size the game never uses is a picture
/// of nothing.
/// </summary>
public class TextureCoordinatesTests
{
    private static XElement T(string xml) => XElement.Parse(xml);

    [Fact]
    public void The_nine_fields_get_the_nine_sizes_the_template_declares()
    {
        var tpl = T("""
            <FullResizeImageTemplate>
              <Name>frame</Name>
              <LeftWidth>5</LeftWidth><MiddleWidth>105</MiddleWidth><RightWidth>7</RightWidth>
              <TopHeight>2</TopHeight><MiddleHeight>4</MiddleHeight><BottomHeight>3</BottomHeight>
              <Texture>
                <TextureName>parts</TextureName>
                <TopLeft><X>0</X><Y>60</Y></TopLeft>
                <TopMiddle><X>3</X><Y>60</Y></TopMiddle>
                <TopRight><X>107</X><Y>60</Y></TopRight>
                <MiddleMiddle><X>3</X><Y>68</Y></MiddleMiddle>
                <BottomRight><X>107</X><Y>79</Y></BottomRight>
              </Texture>
            </FullResizeImageTemplate>
            """);

        var points = TextureCoordinates.Of(tpl).ToDictionary(p => p.Tag);

        Assert.Equal(5, points.Count);

        Assert.Equal((5d, 2d), (points["TopLeft"].Width, points["TopLeft"].Height));
        Assert.Equal((105d, 2d), (points["TopMiddle"].Width, points["TopMiddle"].Height));
        Assert.Equal((7d, 2d), (points["TopRight"].Width, points["TopRight"].Height));
        Assert.Equal((105d, 4d), (points["MiddleMiddle"].Width, points["MiddleMiddle"].Height));
        Assert.Equal((7d, 3d), (points["BottomRight"].Width, points["BottomRight"].Height));

        Assert.Equal(107, points["TopRight"].X);
        Assert.Equal(60, points["TopRight"].Y);
    }

    [Fact]
    public void A_horizontal_template_gets_left_repeat_and_right()
    {
        var tpl = T("""
            <HorizontalResizeImageTemplate>
              <Name>inset</Name>
              <Height>16</Height>
              <LeftWidth>3</LeftWidth><RepeatWidth>4</RepeatWidth><RightWidth>5</RightWidth>
              <Texture>
                <TextureName>ui</TextureName>
                <Left><X>211</X><Y>152</Y></Left>
                <Repeat><X>218</X><Y>152</Y></Repeat>
                <Right><X>224</X><Y>152</Y></Right>
              </Texture>
            </HorizontalResizeImageTemplate>
            """);

        var points = TextureCoordinates.Of(tpl).ToDictionary(p => p.Tag);

        Assert.Equal((3d, 16d), (points["Left"].Width, points["Left"].Height));
        Assert.Equal((4d, 16d), (points["Repeat"].Width, points["Repeat"].Height));
        Assert.Equal((5d, 16d), (points["Right"].Width, points["Right"].Height));
    }

    /// <summary>
    /// The states of one control are the same size — that is what makes them
    /// states of it. The size comes from &lt;Size&gt; or from
    /// &lt;Width&gt;/&lt;Height&gt;, exactly as DrawArea reads it.
    /// </summary>
    [Fact]
    public void Every_state_of_a_button_gets_the_buttons_own_size()
    {
        var tpl = T("""
            <ButtonTemplate>
              <Name>gold</Name>
              <Size><X>15</X><Y>14</Y></Size>
              <Texture>
                <TextureName>ui</TextureName>
                <Normal><X>0</X><Y>0</Y></Normal>
                <Pressed><X>16</X><Y>0</Y></Pressed>
              </Texture>
            </ButtonTemplate>
            """);

        var points = TextureCoordinates.Of(tpl);

        Assert.Equal(2, points.Count);
        Assert.All(points, p => Assert.Equal((15d, 14d), (p.Width, p.Height)));
    }

    /// <summary>
    /// The point list is read off the XML, not off a table of tag names: there
    /// are around forty template types and no list of them anywhere.
    /// Anything with an X and a Y under it is a point into the texture.
    /// </summary>
    [Fact]
    public void A_tag_nobody_has_seen_is_a_point_too_and_a_scalar_pair_is_not()
    {
        var tpl = T("""
            <SomethingNewTemplate>
              <Name>new</Name>
              <Width>8</Width><Height>8</Height>
              <Texture>
                <TextureName>ui</TextureName>
                <WhoKnows><X>3</X><Y>4</Y></WhoKnows>
              </Texture>
            </SomethingNewTemplate>
            """);

        var point = Assert.Single(TextureCoordinates.Of(tpl));

        Assert.Equal("WhoKnows", point.Tag);
        Assert.Equal(3, point.X);
        Assert.Equal(4, point.Y);
    }

    [Fact]
    public void A_template_without_a_texture_has_no_points()
    {
        var tpl = T("<StatusBarTemplate><Name>bar</Name>" +
                    "<ForegroundHResizeTemplate>fg</ForegroundHResizeTemplate></StatusBarTemplate>");

        Assert.Empty(TextureCoordinates.Of(tpl));
        Assert.Equal("", TextureCoordinates.TextureOf(tpl));
    }

    [Fact]
    public void Move_writes_both_values_back_into_the_point_it_came_from()
    {
        var tpl = T("""
            <ButtonTemplate>
              <Name>gold</Name>
              <Size><X>15</X><Y>14</Y></Size>
              <Texture>
                <TextureName>ui</TextureName>
                <Normal><X>0</X><Y>0</Y></Normal>
              </Texture>
            </ButtonTemplate>
            """);

        TextureCoordinates.Move(TextureCoordinates.Of(tpl)[0], 12, 34);

        var moved = TextureCoordinates.Of(tpl)[0];
        Assert.Equal(12, moved.X);
        Assert.Equal(34, moved.Y);

        // And the <Size> beside it is untouched — it is not a point.
        Assert.Equal("15", Xml.Tx(Xml.Sub(tpl, "Size"), "X"));
    }

    /// <summary>The name may sit on the template or inside its block.</summary>
    [Fact]
    public void TextureOf_looks_in_both_places()
    {
        Assert.Equal("a", TextureCoordinates.TextureOf(T(
            "<T><Texture><TextureName>a</TextureName></Texture></T>")));
        Assert.Equal("b", TextureCoordinates.TextureOf(T(
            "<T><TextureName>b</TextureName></T>")));
    }
}
