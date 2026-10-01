using System.Text;
using DaocUiForge.Core.Diagnostics;
using DaocUiForge.Core.Ingest;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// The sink behind the log window — DAoCEd's <c>Log</c> plus its
/// <c>ShowLogAction</c>. What it is for: before it existed, every one of these
/// was a silent <c>catch</c> or a status line the next message overwrote.
/// </summary>
public class LogTests
{
    [Fact]
    public void Entries_come_back_oldest_first_with_their_level()
    {
        var log = new Log();

        log.Info("Package", "loaded");
        log.Warn("Texture", "will not decode");
        log.Error("Package", "will not parse");

        var entries = log.Entries();

        Assert.Equal(3, entries.Count);
        Assert.Equal(LogLevel.Info, entries[0].Level);
        Assert.Equal("Texture", entries[1].Source);
        Assert.Equal("will not parse", entries[2].Message);
    }

    [Fact]
    public void Counts_are_kept_per_level()
    {
        var log = new Log();
        log.Warn("a", "1");
        log.Warn("a", "2");
        log.Error("a", "3");

        var counts = log.Counts();

        Assert.Equal(2, counts[LogLevel.Warning]);
        Assert.Equal(1, counts[LogLevel.Error]);
        Assert.Equal(0, counts[LogLevel.Info]);
    }

    [Fact]
    public void The_oldest_entries_fall_off_and_the_number_dropped_is_kept()
    {
        /* A package with a thousand broken textures must not turn the log into
           the reason the editor runs out of memory — but "the beginning is
           missing" has to be visible, or the first entry read looks like the
           first thing that happened. */
        var log = new Log();
        for (int i = 0; i < Log.Capacity + 25; i++) log.Info("test", i.ToString());

        Assert.Equal(Log.Capacity, log.Entries().Count);
        Assert.Equal(25, log.Dropped);
        Assert.Equal("25", log.Entries()[0].Message);
        Assert.Contains("25 older entries dropped", log.ToText());
    }

    [Fact]
    public void Clear_empties_it_and_forgets_what_was_dropped()
    {
        var log = new Log();
        for (int i = 0; i < Log.Capacity + 5; i++) log.Info("test", "x");

        log.Clear();

        Assert.Empty(log.Entries());
        Assert.Equal(0, log.Dropped);
    }

    [Fact]
    public void Changed_fires_on_every_entry()
    {
        // The log window follows the sink, which is the point of it being a
        // window rather than a report.
        var log = new Log();
        int fired = 0;
        log.Changed += (_, _) => fired++;

        log.Info("a", "1");
        log.Warn("a", "2");
        log.Clear();

        Assert.Equal(3, fired);
    }

    [Fact]
    public void A_file_that_will_not_parse_is_skipped_and_said()
    {
        /* The original skips a broken XML without a word, which is how a
           package with one damaged file comes up short by a window and nothing
           says why. Skipped it stays — one file must not stop the other
           hundred — but it reaches the log. */
        int before = Log.Default.Counts()[LogLevel.Error];

        var pkg = PackageLoader.Load(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["good.xml"] = Encoding.UTF8.GetBytes(
                "<Interface><WindowTemplate><Name>w</Name></WindowTemplate></Interface>"),
            ["broken.xml"] = Encoding.UTF8.GetBytes("<Interface><unclosed>"),
        });

        Assert.Single(pkg.Windows);
        Assert.True(Log.Default.Counts()[LogLevel.Error] > before);
        Assert.Contains(Log.Default.Entries(),
            e => e.Level == LogLevel.Error && e.Message.Contains("broken.xml"));
    }

    [Fact]
    public void Format_puts_the_time_level_and_source_in_front()
    {
        var entry = new LogEntry(new DateTime(2026, 8, 4, 9, 30, 15), LogLevel.Warning,
            "Texture", "atlas.dds will not decode");

        string line = Log.Format(entry);

        Assert.StartsWith("09:30:15", line);
        Assert.Contains("Warning", line);
        Assert.Contains("Texture", line);
        Assert.EndsWith("atlas.dds will not decode", line);
    }
}
