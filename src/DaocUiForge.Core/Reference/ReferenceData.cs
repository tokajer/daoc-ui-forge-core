using System.Reflection;
using System.Text.Json;

namespace DaocUiForge.Core.Reference;

/// <summary>
/// Reference data from the original DAoCEd (Brad Townsend): sample values for
/// adapters, colour adapters, event descriptions and chat buffers. Shipped as
/// embedded JSON resources.
///
/// These tables take the place of guessed values: bars fill in their real
/// proportion (20/100, say), and text shows a realistic length. The XML is
/// never modified in the process.
/// </summary>
public sealed class ReferenceData
{
    /// <summary>Current values (A.s in the original): adapter name → value.</summary>
    public IReadOnlyDictionary<string, string> Current { get; }

    /// <summary>Maximum values (A.m): adapter name → value.</summary>
    public IReadOnlyDictionary<string, string> Max { get; }

    /// <summary>Sample text (A.t): adapter name → text.</summary>
    public IReadOnlyDictionary<string, string> Texts { get; }

    /// <summary>Colour adapters: name → hex without #.</summary>
    public IReadOnlyDictionary<string, string> Colors { get; }

    /// <summary>Event name → description (HTML, with &lt;br&gt;).</summary>
    public IReadOnlyDictionary<string, string> Events { get; }

    /// <summary>Chat buffers by BufferName: "chat", "system".</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Buffers { get; }

    private ReferenceData(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> max,
        IReadOnlyDictionary<string, string> texts,
        IReadOnlyDictionary<string, string> colors,
        IReadOnlyDictionary<string, string> events,
        IReadOnlyDictionary<string, IReadOnlyList<string>> buffers)
    {
        Current = current; Max = max; Texts = texts;
        Colors = colors; Events = events; Buffers = buffers;
    }

    /// <summary>
    /// The same tables with some entries replaced — what a test value is
    /// (DAoCEd's <c>AdapterEditDialog</c>). A copy rather than a mutation:
    /// <see cref="Default"/> is shared by every render context there is, and a
    /// value typed for one preview must not leak into the inspection report
    /// running beside it.
    /// </summary>
    /// <param name="current">Adapter → value, for the bars.</param>
    /// <param name="max">Adapter → maximum. Left alone where nothing is given.</param>
    /// <param name="texts">Adapter → sample text.</param>
    public ReferenceData With(
        IReadOnlyDictionary<string, string>? current = null,
        IReadOnlyDictionary<string, string>? max = null,
        IReadOnlyDictionary<string, string>? texts = null)
    {
        if ((current?.Count ?? 0) == 0 && (max?.Count ?? 0) == 0 && (texts?.Count ?? 0) == 0)
            return this;

        return new ReferenceData(
            Merge(Current, current), Merge(Max, max), Merge(Texts, texts),
            Colors, Events, Buffers);
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string>? over)
    {
        if (over is null || over.Count == 0) return baseline;

        // Ordinal, because that is how the tables are read: RefValue asks for
        // the exact spelling first and the lower-cased one after, and a
        // case-insensitive copy here would answer a question nobody asked.
        var merged = new Dictionary<string, string>(baseline, StringComparer.Ordinal);
        foreach (var (k, v) in over) merged[k] = v;
        return merged;
    }

    private static readonly Lazy<ReferenceData> _default = new(Load);

    /// <summary>The shared instance, loaded once.</summary>
    public static ReferenceData Default => _default.Value;

    private static ReferenceData Load()
    {
        var asm = Assembly.GetExecutingAssembly();

        T Read<T>(string name)
        {
            // Resource name: DaocUiForge.Core.Reference.<name>
            string res = $"DaocUiForge.Core.Reference.{name}";
            using var s = asm.GetManifestResourceStream(res)
                ?? throw new InvalidOperationException($"Resource missing: {res}");
            return JsonSerializer.Deserialize<T>(s)
                ?? throw new InvalidOperationException($"Resource empty: {res}");
        }

        // a.json has the shape {"s":{...},"m":{...},"t":{...}}
        var a = Read<Dictionary<string, Dictionary<string, string>>>("a.json");
        var colors = Read<Dictionary<string, string>>("colors.json");
        var events = Read<Dictionary<string, string>>("events.json");
        var buffersRaw = Read<Dictionary<string, List<string>>>("buffers.json");

        var buffers = buffersRaw.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);

        return new ReferenceData(
            a.GetValueOrDefault("s") ?? new(),
            a.GetValueOrDefault("m") ?? new(),
            a.GetValueOrDefault("t") ?? new(),
            colors, events, buffers);
    }
}
