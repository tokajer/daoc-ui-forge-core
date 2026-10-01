namespace DaocUiForge.Core.Reference;

/// <summary>
/// Values set by hand for the preview — DAoCEd's <c>AdapterEditDialog</c>
/// ("Testwerte ändern"). The reference tables say what a health bar is at
/// normally; this says what it should be while a bar is being built, so the
/// empty, the half and the full state can all be looked at.
///
/// <para><b>Not written into the package.</b> A test value is a question about
/// the preview, not a property of the UI — the XML is never touched, exactly
/// as with the sample data it overrides.</para>
///
/// <para>Whether an adapter is a scalar or a text one is a fact about the
/// reference tables, not a choice: the same name cannot be both, and
/// <see cref="KindOf"/> answers it so the dialog does not have to ask.</para>
/// </summary>
public sealed class TestValues
{
    /// <summary>What kind of value an adapter carries.</summary>
    public enum Kind
    {
        /// <summary>The reference data knows nothing about this name.</summary>
        Unknown,

        /// <summary>A number, and possibly a maximum — this is what fills a bar.</summary>
        Scalar,

        /// <summary>A piece of text.</summary>
        Text,
    }

    /// <summary>Adapter → the value the preview should use.</summary>
    public Dictionary<string, string> Scalars { get; } = new(StringComparer.Ordinal);

    /// <summary>Adapter → the maximum the preview should measure against.</summary>
    public Dictionary<string, string> Maximums { get; } = new(StringComparer.Ordinal);

    /// <summary>Adapter → the text the preview should show.</summary>
    public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

    /// <summary>Nothing has been set by hand.</summary>
    public bool IsEmpty => Scalars.Count == 0 && Maximums.Count == 0 && Texts.Count == 0;

    /// <summary>How many adapters carry a value set by hand.</summary>
    public int Count => Scalars.Keys.Concat(Maximums.Keys).Concat(Texts.Keys)
        .Distinct(StringComparer.Ordinal).Count();

    /// <summary>The reference tables with these values in them.</summary>
    public ReferenceData Apply(ReferenceData baseline) =>
        baseline.With(Scalars, Maximums, Texts);

    /// <summary>Which table an adapter lives in.</summary>
    public static Kind KindOf(ReferenceData data, string adapter)
    {
        if (data.Texts.ContainsKey(adapter)) return Kind.Text;
        if (data.Current.ContainsKey(adapter)) return Kind.Scalar;
        return Kind.Unknown;
    }

    /// <summary>
    /// Set a value. An empty string clears it again rather than writing an
    /// empty value — a bar measured against "" is not a state anyone wants to
    /// look at, and "back to normal" needs a way in.
    /// </summary>
    public void Set(ReferenceData data, string adapter, string value, string maximum = "")
    {
        if (adapter.Length == 0) return;

        var table = KindOf(data, adapter) == Kind.Text ? Texts : Scalars;
        if (value.Length == 0) table.Remove(adapter);
        else table[adapter] = value;

        // A maximum only means something beside a value, and only for a
        // scalar. Setting one for a text adapter would be dead weight in the
        // table nothing ever reads.
        if (ReferenceEquals(table, Scalars))
        {
            if (maximum.Length == 0) Maximums.Remove(adapter);
            else Maximums[adapter] = maximum;
        }
    }

    /// <summary>Forget the value for one adapter.</summary>
    public void Clear(string adapter)
    {
        Scalars.Remove(adapter);
        Maximums.Remove(adapter);
        Texts.Remove(adapter);
    }

    /// <summary>Forget every value.</summary>
    public void ClearAll()
    {
        Scalars.Clear();
        Maximums.Clear();
        Texts.Clear();
    }

    /// <summary>Has this adapter been given a value by hand?</summary>
    public bool Has(string adapter) =>
        Scalars.ContainsKey(adapter) || Maximums.ContainsKey(adapter) || Texts.ContainsKey(adapter);
}
