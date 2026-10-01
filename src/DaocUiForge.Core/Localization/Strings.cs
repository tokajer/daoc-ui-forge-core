using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DaocUiForge.Core.Diagnostics;

namespace DaocUiForge.Core.Localization;

/// <summary>A language the interface can be shown in.</summary>
/// <param name="Code">Two-letter ISO code.</param>
/// <param name="Name">What it is called in itself — nobody looks for "German".</param>
public readonly record struct Language(string Code, string Name);

/// <summary>
/// The interface's words. English is the project's language (decided
/// 2026-07-30) and German is the selectable second one.
///
/// <para><b>The English string is the key.</b> <c>T("Open folder…")</c> looks
/// that sentence up and hands back the German one when the catalogue has it, and
/// the sentence itself when it does not. Three things follow, and all three
/// matter more than the tidiness of symbolic keys:</para>
/// <list type="bullet">
/// <item>There is no English catalogue to keep in step with the code, so English
/// cannot go stale or come up missing.</item>
/// <item>A missing translation degrades to English rather than to
/// <c>MainWindow.OpenFolder.Label</c> on a button.</item>
/// <item>The code still reads as the sentence it shows, which is what makes a
/// wrong string findable at all.</item>
/// </list>
///
/// <para><b>Numbers stay invariant.</b> Formatting goes through
/// <see cref="CultureInfo.InvariantCulture"/> on purpose, even in German. Every
/// number in this editor is a number in the XML, and the XML is invariant:
/// a field that showed "10,5" and wrote 10 would be the decimal
/// separator trap with a translation layer on top. The words change, the
/// numerals do not.</para>
///
/// <para><b>In Core</b> because Core produces user-visible text too — the
/// messages of <see cref="Editing.TextureCatalog"/> and its relatives are meant
/// to be shown as they stand, and the inspection report is a page of prose.</para>
/// </summary>
public static class Strings
{
    /// <summary>English needs no catalogue: it is the key.</summary>
    public const string Default = "en";

    /// <summary>
    /// What can be picked. Kept as a list rather than derived from the embedded
    /// files, because the order and the spelling of the names are decisions.
    /// </summary>
    public static readonly IReadOnlyList<Language> Languages = new[]
    {
        new Language("en", "English"),
        new Language("de", "Deutsch"),
    };

    private static readonly object Gate = new();
    private static Dictionary<string, string> _table = new(StringComparer.Ordinal);
    private static string _current = Default;

    /// <summary>The language in use, as a two-letter code.</summary>
    public static string Current
    {
        get { lock (Gate) return _current; }
    }

    /// <summary>Raised after <see cref="Use"/> changed the language.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Switch language. <c>null</c> means "whatever the system says", falling
    /// back to English for anything there is no catalogue for.
    /// </summary>
    /// <returns>The code actually in use.</returns>
    public static string Use(string? code)
    {
        string want = Normalise(code);

        lock (Gate)
        {
            // English is the starting state and needs no catalogue, so an
            // unchanged code is always a no-op.
            if (want == _current) return _current;

            _table = want == Default
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : Read(want);
            _current = want;
        }

        Changed?.Invoke(null, EventArgs.Empty);
        return want;
    }

    /// <summary>
    /// The sentence in the current language, or the English one it was asked
    /// for.
    /// </summary>
    /// <param name="english">The English text — and the key.</param>
    /// <param name="args">
    /// Values for <c>{0}</c>, <c>{1}</c> … Formatted with the invariant
    /// culture; see the class comment on why.
    /// </param>
    public static string T(string english, params object?[] args)
    {
        string text;
        lock (Gate) text = _table.GetValueOrDefault(english, english);

        return args.Length == 0 ? text : Format(text, english, args);
    }

    /// <summary>
    /// A translation whose placeholders do not match the code is a crash on a
    /// button press, in one language only, on somebody else's machine. It falls
    /// back to English instead and says so once.
    /// </summary>
    private static string Format(string text, string english, object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, text, args);
        }
        catch (FormatException)
        {
            Log.Default.Warn("Language", $"The {Current} text for “{english}” has bad placeholders.");
            try
            {
                return string.Format(CultureInfo.InvariantCulture, english, args);
            }
            catch (FormatException)
            {
                return english;
            }
        }
    }

    /// <summary>
    /// Which language a code means. Anything unknown, empty, or a full culture
    /// name ("de-AT") is reduced to a two-letter code and then to English if
    /// there is no catalogue for it.
    /// </summary>
    public static string Normalise(string? code)
    {
        string want = (code ?? "").Trim();

        if (want.Length == 0)
        {
            // The system's language. Under InvariantGlobalization this is
            // always the invariant culture, which is why that switch had to
            // come off for this feature.
            want = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }

        if (want.Length > 2) want = want[..2];
        want = want.ToLowerInvariant();

        return Languages.Any(l => l.Code == want) ? want : Default;
    }

    /// <summary>
    /// A catalogue out of the assembly's embedded resources: every
    /// <c>Localization/{code}/*.json</c> there is, merged.
    ///
    /// <para><b>One file per area of the interface</b>, not one file per
    /// language. Six hundred strings in a single 43 KB JSON were workable for
    /// one translation and would not have been for the fifth: a translator
    /// cannot see where they are, a review cannot be split, and two people
    /// cannot work at once. The areas are the panes they belong to
    /// (<c>main</c>, <c>properties</c>, <c>catalogs</c>, <c>report</c>,
    /// <c>settings</c>) plus <c>common</c> for the words more than one of them
    /// uses. Nothing here knows those names: adding an area is adding a file,
    /// and adding a language is adding a folder.</para>
    ///
    /// <para>A key belongs to exactly one file, which is what
    /// <c>StringsTests</c> holds to; were two files to carry it anyway, the
    /// later name wins and neither the interface nor the fallback breaks.</para>
    ///
    /// <para>A missing or damaged catalogue leaves the interface in English
    /// rather than failing to start, and a damaged area leaves the other five
    /// translated. A translation is a convenience, and the fallback is the
    /// whole point of keying on the English text.</para>
    /// </summary>
    private static Dictionary<string, string> Read(string code)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        var assembly = Assembly.GetExecutingAssembly();
        string prefix = $"DaocUiForge.Core.Localization.{code}.";
        var files = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                        && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            Log.Default.Warn("Language", $"No catalogue for {code}; staying on English.");
            return table;
        }

        foreach (string name in files)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                var read = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (read is null) continue;

                foreach (var (key, value) in read)
                    if (value.Length > 0) table[key] = value;
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                Log.Default.Warn("Language", $"{name} will not read: {e.Message}");
            }
        }

        Log.Default.Info("Language", $"{code}: {table.Count} strings from {files.Count} files.");
        return table;
    }
}
