using System.Globalization;
using System.Xml.Linq;
using DaocUiForge.Core.Model;
using SkiaSharp;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Small arithmetic and reading helpers of the drawing layer. Ported from
/// <c>num</c>, <c>pt</c>, <c>rgba</c> and <c>hexOf</c> of the HTML original.
/// </summary>
public static class RenderMath
{
    /// <summary>
    /// A number out of an XML text value. Mirrors <c>parseFloat</c>: the
    /// leading numeric part counts and everything after it is ignored ("16px"
    /// yields 16). Without a usable start <paramref name="def"/> applies.
    /// </summary>
    public static double Num(string? s, double def = 0)
    {
        if (string.IsNullOrEmpty(s)) return def;

        int i = 0, n = s.Length;
        while (i < n && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < n && (s[i] == '+' || s[i] == '-')) i++;
        int digits = 0;
        while (i < n && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
        if (i < n && s[i] == '.')
        {
            i++;
            while (i < n && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
        }
        if (digits == 0) return def;

        // Take the exponent only if it is complete, just like parseFloat.
        int afterMantissa = i;
        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < n && (s[j] == '+' || s[j] == '-')) j++;
            int expDigits = 0;
            while (j < n && s[j] >= '0' && s[j] <= '9') { j++; expDigits++; }
            if (expDigits > 0) afterMantissa = j;
        }

        return double.TryParse(s.AsSpan(start, afterMantissa - start),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    }

    /// <summary>
    /// Rounding the way JavaScript <c>Math.round</c> does it: a half ALWAYS
    /// goes up, negative values included (−1.5 becomes −1).
    ///
    /// Do not replace this with <see cref="Math.Round(double)"/>: that rounds
    /// to even (0.5 becomes 0 instead of 1) and differs again for negative
    /// values. Either difference shifts elements by one pixel against the
    /// original — visible on centred elements and on sprite levels.
    /// </summary>
    public static double JsRound(double v) => Math.Floor(v + 0.5);

    /// <summary>
    /// A point from a child element carrying &lt;X&gt;/&lt;Y&gt;
    /// (e.g. &lt;Position&gt;). Null when the child is absent — the
    /// difference from (0,0) matters in several places.
    /// </summary>
    public static SKPoint? Pt(XElement? node, string tag)
    {
        var s = Xml.Sub(node, tag);
        if (s is null) return null;
        return new SKPoint((float)Num(Xml.Tx(s, "X")), (float)Num(Xml.Tx(s, "Y")));
    }

    /// <summary>
    /// A colour from a child element carrying &lt;R&gt;&lt;G&gt;&lt;B&gt;&lt;A&gt;.
    /// Missing channels count as 255, as in the original.
    /// </summary>
    public static SKColor ColorOf(XElement? node, string tag, SKColor def)
    {
        var c = Xml.Sub(node, tag);
        if (c is null) return def;
        byte Ch(string k) => (byte)Math.Clamp((int)Num(Xml.Tx(c, k), 255), 0, 255);
        return new SKColor(Ch("R"), Ch("G"), Ch("B"), Ch("A"));
    }
}
