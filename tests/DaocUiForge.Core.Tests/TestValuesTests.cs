using System.Text;
using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Reference;
using DaocUiForge.Core.Render;
using SkiaSharp;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// Test values (DAoCEd's <c>AdapterEditDialog</c>): what an adapter reports
/// while the preview draws, so a bar can be looked at empty, half and full.
/// </summary>
public class TestValuesTests
{
    private static ReferenceData Data => ReferenceData.Default;

    [Fact]
    public void A_scalar_value_reaches_the_tables_the_renderer_reads()
    {
        string adapter = Data.Current.Keys.First();

        var values = new TestValues();
        values.Set(Data, adapter, "7", "9");

        var live = values.Apply(Data);

        Assert.Equal("7", live.Current[adapter]);
        Assert.Equal("9", live.Max[adapter]);
    }

    [Fact]
    public void A_text_value_goes_into_the_text_table_and_takes_no_maximum()
    {
        string adapter = Data.Texts.Keys.First(k => !Data.Current.ContainsKey(k));

        var values = new TestValues();
        values.Set(Data, adapter, "hello", "999");

        Assert.Equal("hello", values.Apply(Data).Texts[adapter]);
        Assert.False(values.Maximums.ContainsKey(adapter));
    }

    /// <summary>
    /// The baseline is shared by every render context there is. A value typed
    /// for one preview must not turn up in the inspection report running beside
    /// it.
    /// </summary>
    [Fact]
    public void Setting_a_value_leaves_the_shared_reference_data_alone()
    {
        string adapter = Data.Current.Keys.First();
        string before = Data.Current[adapter];

        var values = new TestValues();
        values.Set(Data, adapter, before + "0");
        values.Apply(Data);

        Assert.Equal(before, ReferenceData.Default.Current[adapter]);
    }

    [Fact]
    public void An_empty_value_puts_the_adapter_back_to_normal()
    {
        string adapter = Data.Current.Keys.First();

        var values = new TestValues();
        values.Set(Data, adapter, "42", "50");
        Assert.True(values.Has(adapter));

        values.Set(Data, adapter, "");

        Assert.False(values.Has(adapter));
        Assert.True(values.IsEmpty);
        Assert.Equal(Data.Current[adapter], values.Apply(Data).Current[adapter]);
    }

    [Fact]
    public void With_nothing_set_the_very_same_tables_come_back()
    {
        var values = new TestValues();

        Assert.Same(Data, values.Apply(Data));
    }

    [Fact]
    public void KindOf_answers_from_the_tables_not_from_the_caller()
    {
        Assert.Equal(TestValues.Kind.Scalar, TestValues.KindOf(Data, Data.Current.Keys.First()));
        Assert.Equal(TestValues.Kind.Text,
            TestValues.KindOf(Data, Data.Texts.Keys.First(k => !Data.Current.ContainsKey(k))));
        Assert.Equal(TestValues.Kind.Unknown, TestValues.KindOf(Data, "no_such_adapter_at_all"));
    }

    /// <summary>
    /// The point of the whole thing: a bar drawn with a test value fills to
    /// where the value says, not to where the reference data says.
    /// </summary>
    [Fact]
    public void A_bar_fills_to_the_test_value()
    {
        const string xml = """
            <Interface>
              <StatusBarTemplate><Name>bar</Name>
                <ForegroundHResizeTemplate>fg</ForegroundHResizeTemplate>
              </StatusBarTemplate>
              <HorizontalResizeImageTemplate><Name>fg</Name>
                <Height>10</Height>
                <LeftWidth>0</LeftWidth><RepeatWidth>1</RepeatWidth><RightWidth>0</RightWidth>
                <Texture><TextureName>tex</TextureName>
                  <Repeat><X>0</X><Y>0</Y></Repeat></Texture>
              </HorizontalResizeImageTemplate>
              <Texture><Name>tex</Name><File>tex.png</File></Texture>
            </Interface>
            """;

        using var tile = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(tile)) c.Clear(new SKColor(255, 0, 0));
        using var img = SKImage.FromBitmap(tile);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);

        var pkg = PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["ui.xml"] = Encoding.UTF8.GetBytes(xml),
            ["tex.png"] = png.ToArray(),
        });

        var def = XElement.Parse("""
            <StatusBarDef><TemplateName>bar</TemplateName>
              <AdapterName>test_bar</AdapterName>
              <Position><X>0</X><Y>0</Y></Position>
              <Width>100</Width><Height>10</Height></StatusBarDef>
            """);

        int FilledWidth(ReferenceData data)
        {
            using var bmp = new SKBitmap(new SKImageInfo(120, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.Transparent);
            using var ctx = new RenderContext(pkg, new RenderOptions { ShowSampleData = true }, data);
            ElementRenderer.Render(canvas, ctx, def, 120, 20);

            // Row 1, not the middle: the tile is 4 px tall, and Skia clamps a
            // source rectangle to the bitmap rather than repeating it.
            int filled = 0;
            for (int x = 0; x < 120; x++)
                if (bmp.GetPixel(x, 1).Red == 255) filled++;
            return filled;
        }

        var quarter = new TestValues();
        quarter.Set(Data, "test_bar", "25", "100");

        var full = new TestValues();
        full.Set(Data, "test_bar", "100", "100");

        Assert.Equal(25, FilledWidth(quarter.Apply(Data)));
        Assert.Equal(100, FilledWidth(full.Apply(Data)));
    }
}
