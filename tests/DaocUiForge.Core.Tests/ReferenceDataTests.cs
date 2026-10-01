using DaocUiForge.Core.Reference;
using Xunit;

namespace DaocUiForge.Tests;

public class ReferenceDataTests
{
    [Fact]
    public void EmbeddedResources_LoadWithoutError()
    {
        var rd = ReferenceData.Default;
        Assert.NotEmpty(rd.Current);
        Assert.NotEmpty(rd.Max);
        Assert.NotEmpty(rd.Texts);
        Assert.NotEmpty(rd.Colors);
        Assert.NotEmpty(rd.Events);
        Assert.NotEmpty(rd.Buffers);
    }

    [Fact]
    public void KnownSampleValues_ArePresent()
    {
        var rd = ReferenceData.Default;
        // spot checks from the DAoCEd data set
        Assert.True(rd.Max.ContainsKey("group_health0"));
        Assert.Equal("100", rd.Max["group_health0"]);
        Assert.True(rd.Colors.ContainsKey("index_color_1"));
    }

    [Fact]
    public void Buffers_HaveChatAndSystem()
    {
        var rd = ReferenceData.Default;
        Assert.True(rd.Buffers.ContainsKey("chat"));
        Assert.True(rd.Buffers.ContainsKey("system"));
        Assert.NotEmpty(rd.Buffers["chat"]);
    }
}
