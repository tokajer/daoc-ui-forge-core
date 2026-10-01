using System.Text;

namespace DaocUiForge.Core.Diagnostics;

/// <summary>How bad it is. DAoCEd's <c>LogEntry</c> has the same four.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// One line of the log.
/// </summary>
/// <param name="Time">When it happened, local time.</param>
/// <param name="Level">How bad it is.</param>
/// <param name="Source">Which part said it, e.g. "Package" or "Texture".</param>
/// <param name="Message">The line itself, meant to be shown as it stands.</param>
public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Source, string Message);

/// <summary>
/// Where loading warnings and failures go — DAoCEd's <c>Log</c> plus its
/// <c>ShowLogAction</c>. The HTML original has neither: a browser page writes to
/// the developer console, which nobody using it ever opens.
///
/// <para><b>Why this is in Core.</b> The things worth logging happen where the
/// work happens: an XML file that will not parse
/// (<see cref="Ingest.PackageLoader"/>), a texture whose bytes are damaged
/// (<see cref="Render.TextureCache"/>), a TTF Skia will not take
/// (<see cref="Render.FontProvider"/>). Every one of those was a silent
/// <c>catch { }</c> before this existed, and the status line only ever carried
/// the last thing that happened.</para>
///
/// <para><b>Bounded.</b> A package with a thousand broken textures must not turn
/// the log into the reason the editor runs out of memory, so the oldest entries
/// fall off at <see cref="Capacity"/>, and how many were dropped is remembered
/// (<see cref="Dropped"/>) rather than quietly lost.</para>
///
/// <para><b>Thread-safe.</b> The inspection report draws the whole package on a
/// thread of its own and fills the same sink the interface is reading.</para>
///
/// <para>Not written to a file, unlike DAoCEd, which keeps <c>log.txt</c> beside
/// its jar. An installed program has no business writing into its own folder
/// (the same reasoning as <see cref="Settings.AppSettings.DefaultPath"/>), and
/// the window has "Save as text…" for the case where a log is worth keeping.</para>
/// </summary>
public sealed class Log
{
    /// <summary>How many entries are kept before the oldest fall off.</summary>
    public const int Capacity = 2000;

    /// <summary>
    /// The sink the whole program writes to. One per process rather than one per
    /// package: the interesting entries are made while a package is being loaded,
    /// and at that point there is no package to hang them off yet.
    /// </summary>
    public static Log Default { get; } = new();

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries = new();
    private int _dropped;

    /// <summary>Raised after every entry, on the thread that wrote it.</summary>
    public event EventHandler? Changed;

    /// <summary>How many entries fell off the front.</summary>
    public int Dropped
    {
        get { lock (_gate) return _dropped; }
    }

    /// <summary>The entries, oldest first. A snapshot — the live queue is not handed out.</summary>
    public IReadOnlyList<LogEntry> Entries()
    {
        lock (_gate) return _entries.ToList();
    }

    /// <summary>How many entries there are of each level.</summary>
    public IReadOnlyDictionary<LogLevel, int> Counts()
    {
        var counts = new Dictionary<LogLevel, int>
        {
            [LogLevel.Debug] = 0,
            [LogLevel.Info] = 0,
            [LogLevel.Warning] = 0,
            [LogLevel.Error] = 0,
        };

        lock (_gate)
            foreach (var e in _entries) counts[e.Level]++;

        return counts;
    }

    public void Add(LogLevel level, string source, string message)
    {
        lock (_gate)
        {
            _entries.Enqueue(new LogEntry(DateTime.Now, level, source, message));
            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
                _dropped++;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Info(string source, string message) => Add(LogLevel.Info, source, message);

    public void Warn(string source, string message) => Add(LogLevel.Warning, source, message);

    public void Error(string source, string message) => Add(LogLevel.Error, source, message);

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _dropped = 0;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The log as text, for saving or for pasting into a bug report.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        int dropped;
        List<LogEntry> entries;

        lock (_gate)
        {
            dropped = _dropped;
            entries = _entries.ToList();
        }

        if (dropped > 0)
            sb.Append($"({dropped} older entries dropped)\n\n");

        foreach (var e in entries)
            sb.Append(Format(e)).Append('\n');

        return sb.ToString();
    }

    /// <summary>One entry as a line: time, level, source, message.</summary>
    public static string Format(LogEntry e) =>
        $"{e.Time:HH:mm:ss}  {Tag(e.Level),-7}  {e.Source,-9}  {e.Message}";

    /// <summary>The level as DAoCEd spells it in its own log table.</summary>
    public static string Tag(LogLevel level) => level switch
    {
        LogLevel.Error => "Error",
        LogLevel.Warning => "Warning",
        LogLevel.Debug => "Debug",
        _ => "Info",
    };
}
