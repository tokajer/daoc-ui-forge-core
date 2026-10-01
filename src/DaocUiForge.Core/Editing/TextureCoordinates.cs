using System.Xml.Linq;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Render;
using static DaocUiForge.Core.Render.RenderMath;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// One point a template takes out of a texture, and the rectangle it stands
/// for.
/// </summary>
/// <param name="Tag">The tag the point sits under: TopLeft, Left, Normal, …</param>
/// <param name="Owner">
/// The element the pair of &lt;X&gt;/&lt;Y&gt; lives in — what the editor
/// writes back into.
/// </param>
/// <param name="X">Position in the texture's own pixels.</param>
/// <param name="Y">See <paramref name="X"/>.</param>
/// <param name="Width">
/// How much of the texture this point takes. 0 when the template says nothing
/// about it, which is not the same as nothing being taken.
/// </param>
/// <param name="Height">See <paramref name="Width"/>.</param>
public sealed record TexturePoint(
    string Tag, XElement Owner, double X, double Y, double Width, double Height);

/// <summary>
/// Which pieces of a texture a template names, and how large each is — the
/// model behind DAoCEd's Textureeditor, where those rectangles are dragged
/// around on the texture instead of typed as numbers.
///
/// <para><b>The points are read off the XML, not off a list of type names.</b>
/// The same rule as <see cref="NineSlice.ShapeOf"/> and for the same reason:
/// there are around forty template types and no list of them anywhere.
/// Any child carrying an &lt;X&gt; and a &lt;Y&gt; is a point into
/// the texture, whatever it is called, so a type nobody has seen yet is
/// editable too.</para>
///
/// <para><b>The size of each rectangle comes from the shape</b>, because that
/// is where the drawing layer gets it: a nine-field template cuts its corners
/// at LeftWidth x TopHeight and its middle at MiddleWidth x MiddleHeight, and
/// showing one rectangle of a single size over all nine would be a picture of
/// something the game never draws.</para>
/// </summary>
public static class TextureCoordinates
{
    /// <summary>
    /// The name of the texture the template cuts out of, or empty. It may sit
    /// inside the &lt;Texture&gt; block or directly on the template.
    /// </summary>
    public static string TextureOf(XElement? tpl)
    {
        var t = Xml.Sub(tpl, "Texture");
        string name = Xml.Tx(t, "TextureName");
        return name.Length > 0 ? name : Xml.Tx(tpl, "TextureName");
    }

    /// <summary>The block the points live in: the &lt;Texture&gt; block, else the template.</summary>
    public static XElement? BlockOf(XElement? tpl) => Xml.Sub(tpl, "Texture") ?? tpl;

    /// <summary>
    /// Every point the template takes out of its texture, in document order.
    /// Empty when it names no texture — there is then nothing to point into.
    /// </summary>
    public static List<TexturePoint> Of(XElement? tpl)
    {
        var found = new List<TexturePoint>();
        if (tpl is null || BlockOf(tpl) is not { } block) return found;

        var shape = NineSlice.ShapeOf(tpl);

        foreach (var child in block.Elements())
        {
            if (Xml.Sub(child, "X") is null || Xml.Sub(child, "Y") is null) continue;

            string tag = child.Name.LocalName;
            var (w, h) = SizeOf(tpl, shape, tag);
            found.Add(new TexturePoint(tag, child,
                Num(Xml.Tx(child, "X")), Num(Xml.Tx(child, "Y")), w, h));
        }

        return found;
    }

    /// <summary>Move one point. The values go in with the invariant culture.</summary>
    public static void Move(TexturePoint point, double x, double y)
    {
        XmlEdit.SetSub(point.Owner, x, "X");
        XmlEdit.SetSub(point.Owner, y, "Y");
    }

    /// <summary>
    /// How much of the texture the field under <paramref name="tag"/> takes.
    /// The arithmetic is <see cref="NineSlice"/>'s, field for field — if the
    /// two disagree, the frames sit somewhere the sprite is not.
    /// </summary>
    private static (double W, double H) SizeOf(XElement tpl, TemplateShape shape, string tag)
    {
        double lw = Num(Xml.Tx(tpl, "LeftWidth"));
        double rw = Num(Xml.Tx(tpl, "RightWidth"));
        double th = Num(Xml.Tx(tpl, "TopHeight"));
        double bh = Num(Xml.Tx(tpl, "BottomHeight"));

        switch (shape)
        {
            case TemplateShape.FullResize:
            {
                double cw = Num(Xml.Tx(tpl, "MiddleWidth"));
                if (cw == 0) cw = Num(Xml.Tx(tpl, "CenterWidth"));
                double ch = Num(Xml.Tx(tpl, "MiddleHeight"));
                if (ch == 0) ch = Num(Xml.Tx(tpl, "CenterHeight"));

                double w = tag.Contains("Left", StringComparison.Ordinal) ? lw
                    : tag.Contains("Right", StringComparison.Ordinal) ? rw
                    : cw;
                double h = tag.StartsWith("Top", StringComparison.Ordinal) ? th
                    : tag.StartsWith("Bottom", StringComparison.Ordinal) ? bh
                    : ch;
                return (w, h);
            }

            case TemplateShape.HResize:
            {
                double h = Num(Xml.Tx(tpl, "Height"));
                double w = tag switch
                {
                    "Left" => lw,
                    "Right" => rw,
                    _ => Num(Xml.Tx(tpl, "RepeatWidth")),
                };
                return (w, h);
            }

            case TemplateShape.VResize:
            {
                double w = Num(Xml.Tx(tpl, "Width"));
                double h = tag switch
                {
                    "Top" => th,
                    "Bottom" => bh,
                    _ => Num(Xml.Tx(tpl, "RepeatHeight")),
                };
                return (w, h);
            }

            case TemplateShape.IconSheet:
                return (Num(Xml.Tx(tpl, "Width")), Num(Xml.Tx(tpl, "Height")));

            default:
            {
                // One slice, and every state (Normal, Pressed, …) is the same
                // size — that is what makes them states of one control.
                var size = Pt(tpl, "Size");
                return (size?.X ?? Num(Xml.Tx(tpl, "Width")),
                        size?.Y ?? Num(Xml.Tx(tpl, "Height")));
            }
        }
    }
}
