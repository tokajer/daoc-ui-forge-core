using DaocUiForge.Core.Diagnostics;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using SkiaSharp;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Supplies the package's fonts for drawing. Ported from <c>fontInfo</c>,
/// <c>fontPx</c>, <c>fontLine</c> and <c>loadFonts</c>.
///
/// THE rule of this class:
/// &lt;TTFFont&gt;&lt;Height&gt; is the FONT SIZE in pixels - the pixel size
/// the client hands the rasteriser. The line advance follows from it and the
/// TTF metrics, which is the opposite way round from the HTML original: a
/// browser cannot be told a baseline, so it set line-height to Height and
/// shrank the font to fit. Measured against the running game on
/// summary.xml, 2026-08-07.
/// </summary>
public sealed class FontProvider : IDisposable
{
    /// <summary>Line height in em when the TTF supplies no metrics.</summary>
    public const double DefaultLineHeightEm = 1.3;

    private readonly Package _pkg;
    private readonly Dictionary<string, SKTypeface?> _byFile = new(StringComparer.Ordinal);
    private readonly List<SKTypeface> _owned = new();

    public FontProvider(Package pkg) => _pkg = pkg;

    /// <summary>
    /// Font data for a name. When the package does not know the name it is
    /// guessed from the name itself, as in the original (e.g. "arial9").
    /// </summary>
    public FontRef Info(string? name)
    {
        string key = (name ?? "").Trim();
        if (key.Length > 0 && _pkg.Fonts.TryGetValue(key, out var f)) return f;

        // Fallback: read trailing digits as the height.
        int end = key.Length;
        int start = end;
        while (start > 0 && char.IsAsciiDigit(key[start - 1])) start--;
        double height = start < end && int.TryParse(key.AsSpan(start, end - start), out var n) ? n : 11;

        return new FontRef
        {
            File = "",
            Height = height,
            Bold = key.Contains("bold", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Line advance in pixels: the font size times the TTF's own line height
    /// in em. Rounded to one decimal, as the original rounded its own figure.
    /// </summary>
    public float LinePx(string? name)
    {
        var f = Info(name);
        double h = f.Height >= 6 ? f.Height : 11;
        double lh = f.LineHeight > 0 ? f.LineHeight : DefaultLineHeightEm;
        return (float)(RenderMath.JsRound(h * lh * 10) / 10);
    }

    /// <summary>Font size in pixels — the value from &lt;Height&gt;.</summary>
    public float SizePx(string? name)
    {
        var f = Info(name);
        return (float)(f.Height >= 6 ? f.Height : 11);
    }

    /// <summary>
    /// Typeface for a name: the TTF from the package if there is one,
    /// otherwise a system font of matching weight.
    /// </summary>
    public SKTypeface Typeface(string? name)
    {
        var f = Info(name);
        if (!string.IsNullOrEmpty(f.File))
        {
            string cacheKey = FileResolver.NormPath(f.File);
            if (!_byFile.TryGetValue(cacheKey, out var tf))
            {
                tf = LoadTypeface(f.File);
                _byFile[cacheKey] = tf;
                if (tf is not null) _owned.Add(tf);
            }
            if (tf is not null) return tf;
        }

        return SKTypeface.FromFamilyName(null,
            f.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
    }

    /// <summary>
    /// The TTF from the package, or — when the package does not carry it — from
    /// the game folder, whose <c>DAoC.ttf</c> and relatives every default window
    /// names (<see cref="Ingest.GameFolder"/>). Without a game folder the second
    /// half does nothing and a system font stands in, as it always did.
    /// </summary>
    private SKTypeface? LoadTypeface(string file)
    {
        string? key = FileResolver.Find(_pkg.Files, file);
        byte[]? bytes = key is not null ? _pkg.Files[key] : null;

        if (bytes is null && Ingest.GameFolder.Find(_pkg, file) is string gameKey)
        {
            key = gameKey;
            bytes = Ingest.GameFolder.Read(_pkg, gameKey);
        }

        if (bytes is null || key is null)
        {
            Log.Default.Warn("Font", $"{file} is not in the package; a system font stands in.");
            return null;
        }

        try
        {
            using var data = SKData.CreateCopy(bytes);
            var tf = SKTypeface.FromData(data);
            if (tf is null)
                Log.Default.Warn("Font", $"{key} is not a font Skia can read; a system font stands in.");
            return tf;
        }
        catch (Exception ex)
        {
            // Not loadable — the substitute font takes over. Said rather than
            // swallowed: text drawn in the wrong face is the kind of difference
            // that gets blamed on the renderer.
            Log.Default.Warn("Font", $"{key} will not load: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var tf in _owned) tf.Dispose();
        _owned.Clear();
        _byFile.Clear();
    }
}
