using DaocUiForge.Core.Diagnostics;
using DaocUiForge.Core.Formats;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using SkiaSharp;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Decodes the package's textures and keeps them around. Ported from
/// <c>loadTex</c> and <c>slice</c> of the HTML original.
///
/// Two quirks of the original are kept:
/// - A failure is remembered too (null in the cache), so a broken file is not
///   decoded again on every redraw.
/// - A declared but unresolvable texture is NOT a package error: typically it
///   points into the game folder (atlantis/…). It goes to
///   <see cref="Package.MissingTextures"/> and is reported separately.
/// </summary>
public sealed class TextureCache : IDisposable
{
    private readonly Package _pkg;
    private readonly Dictionary<string, SKBitmap?> _cache = new(StringComparer.Ordinal);

    /// <summary>Nearest neighbour rather than smoothing — sprites are pixel-exact.</summary>
    private static readonly SKPaint NearestNeighbour = new()
    {
        FilterQuality = SKFilterQuality.None,
        IsAntialias = false,
    };

    public TextureCache(Package pkg) => _pkg = pkg;

    /// <summary>
    /// Decoded texture for a file path (cached).
    ///
    /// <para>The package is asked first and the game folder second, which is the
    /// order the client uses: <c>ui/custom</c> overrides the default interface.
    /// Without a game folder the second half does nothing and the behaviour is
    /// what it always was (<see cref="Ingest.GameFolder"/>).</para>
    /// </summary>
    public SKBitmap? Load(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string key = FileResolver.NormPath(path);
        if (_cache.TryGetValue(key, out var hit)) return hit;

        SKBitmap? res = null;
        byte[]? data = null;
        string? fileKey = FileResolver.Find(_pkg.Files, path);
        bool fromGame = false;

        if (fileKey is not null)
        {
            data = _pkg.Files[fileKey];
        }
        else if (GameFolder.Find(_pkg, path) is string gameKey)
        {
            fileKey = gameKey;
            data = GameFolder.Read(_pkg, gameKey);
            fromGame = true;
        }

        if (data is not null && fileKey is not null)
        {
            try
            {
                if (fileKey.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                    res = TgaDecoder.Decode(data);
                else if (fileKey.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    res = DdsDecoder.Decode(data);
                else
                    res = SKBitmap.Decode(data); // PNG/JPG/BMP through Skia
            }
            catch (Exception ex)
            {
                // The original passes over a damaged file silently. Drawing
                // still carries on without it — one broken sprite must not take
                // the window down — but the reason is said once, which is all
                // the caching makes it worth saying.
                Log.Default.Warn("Texture", $"{fileKey} will not decode: {ex.Message}");
                res = null;
            }

            if (res is null)
                Log.Default.Warn("Texture", $"{fileKey} decoded to nothing.");
        }

        _cache[key] = res;
        _fromGameFolder[key] = res is not null && fromGame;
        return res;
    }

    /// <summary>
    /// Which cached paths were answered by the game folder. Kept per path
    /// rather than per texture name, because that is the level the answer is
    /// decided at — two names may point at one file.
    /// </summary>
    private readonly Dictionary<string, bool> _fromGameFolder = new(StringComparer.Ordinal);

    /// <summary>
    /// Forget one path, so the next request goes back to disk. Needed after a
    /// file is imported into the package: a failure is cached too, so a texture
    /// that was missing a moment ago would otherwise stay missing until the
    /// package is reloaded.
    /// </summary>
    public void Forget(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string key = FileResolver.NormPath(path);
        if (_cache.Remove(key, out var bmp)) bmp?.Dispose();
        _fromGameFolder.Remove(key);
    }

    /// <summary>
    /// Resolve a texture through the &lt;Name&gt; declared in the package.
    /// When the name is not declared it counts as a path itself.
    /// </summary>
    public SKBitmap? Resolve(string? texName)
    {
        if (string.IsNullOrEmpty(texName)) return null;
        string path = _pkg.Textures.TryGetValue(texName, out var p) ? p : texName;
        var tex = Load(path);

        if (tex is null) _pkg.MissingTextures.Add(texName);
        else if (_fromGameFolder.GetValueOrDefault(FileResolver.NormPath(path)))
            _pkg.GameFolderTextures.Add(texName);

        return tex;
    }

    /// <summary>Is the texture name declared in the package via &lt;Texture&gt;?</summary>
    public bool IsDeclared(string? texName) =>
        !string.IsNullOrEmpty(texName) && _pkg.Textures.ContainsKey(texName);

    /// <summary>Note a texture name for the inspection report.</summary>
    public void MarkMissing(string? texName)
    {
        if (!string.IsNullOrEmpty(texName)) _pkg.MissingTextures.Add(texName);
    }

    /// <summary>
    /// Draw a slice of a texture onto the target area. Equivalent to
    /// <c>slice</c> plus the placement that follows it.
    /// </summary>
    /// <returns>true when something was actually drawn.</returns>
    public bool Draw(SKCanvas canvas, string? texName,
        float sx, float sy, float sw, float sh, SKRect dst)
    {
        if (sw <= 0 || sh <= 0 || dst.Width <= 0 || dst.Height <= 0) return false;
        var tex = Resolve(texName);
        if (tex is null) return false;

        canvas.DrawBitmap(tex, new SKRect(sx, sy, sx + sw, sy + sh), dst, NearestNeighbour);
        return true;
    }

    /// <summary>
    /// A slice as a bitmap of its own. Drawing does not need this (it goes
    /// straight onto the surface), but tests and a later texture browser do.
    /// </summary>
    public SKBitmap? Slice(string? texName, float sx, float sy, float sw, float sh,
        float dw = 0, float dh = 0)
    {
        if (sw <= 0 || sh <= 0) { Resolve(texName); return null; }
        int w = Math.Max(1, (int)Math.Round(dw > 0 ? dw : sw));
        int h = Math.Max(1, (int)Math.Round(dh > 0 ? dh : sh));

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        if (!Draw(canvas, texName, sx, sy, sw, sh, new SKRect(0, 0, w, h)))
        {
            bmp.Dispose();
            return null;
        }
        return bmp;
    }

    public void Dispose()
    {
        foreach (var b in _cache.Values) b?.Dispose();
        _cache.Clear();
        _fromGameFolder.Clear();
    }
}
