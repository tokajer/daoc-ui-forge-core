using System.Xml.Linq;
using DaocUiForge.Core.Ingest;
using DaocUiForge.Core.Model;
using static DaocUiForge.Core.Localization.Strings;

namespace DaocUiForge.Core.Editing;

/// <summary>
/// Undo and redo over a whole package. Neither the HTML original nor DAoCEd has
/// anything of the kind: both write straight onto the tree, and the only way
/// back is to load the package again and lose the session with it.
///
/// <para><b>Snapshots, not inverse operations.</b> The obvious design gives every
/// editing action an inverse. That is some forty inverses over
/// <see cref="ElementEditor"/>, <see cref="PackageEditor"/>, the catalogs and
/// <see cref="TabEditor"/> — and an inverse that is subtly not one is worse than
/// no undo at all, because it restores something that looks right, and the next
/// save writes it to disk. What is kept instead is the text of every document an
/// action touched. That cannot be subtly wrong: either the text parses back to
/// the same tree, or the round trip is broken for saving too, and saving is
/// already tested byte-exact against all 105 files of the reference package.
/// </para>
///
/// <para><b>What an action touched is observed, not declared.</b> A document
/// raises <see cref="XObject.Changing"/> for its whole subtree, so the first
/// change within a step snapshots that document by itself. A caller cannot
/// forget to name a file, which is the failure mode of the alternative: one
/// missed call site and undo silently skips that action. Only what LINQ to XML
/// has no event for has to be declared — the raw bytes of a file
/// (<see cref="Capture"/>). A document that is <b>created</b> needs nothing: the
/// step notes which documents existed when it began. A document that is
/// <b>removed</b> from <see cref="Package.Docs"/> would need
/// <see cref="Capture"/>; nothing does that today.</para>
///
/// <para><b>The price</b> is that restoring a document replaces the tree, so
/// every <see cref="XElement"/> held anywhere else goes stale.
/// <see cref="PackageLoader.Reindex"/> rebuilds the package's own tables; the
/// interface has to rebuild its selection the way it does after a load. That is
/// the trade: a coarser refresh in exchange for an undo that cannot leave a
/// half-restored package behind.</para>
///
/// <para><b>Use.</b> <see cref="Watch"/> once per package, then mark the
/// boundaries of what the user would call one action. A step opens by itself on
/// the first change, so the two ways of marking one are:</para>
/// <list type="bullet">
///   <item><see cref="Close(string)"/> <b>after</b> the action, which is what
///   an interface that reports an edit once it is made can do.</item>
///   <item><see cref="Begin"/> <b>before</b> it, which an action that creates a
///   document must use: the step has to note which documents there were, and by
///   the time the new one raises its first event it is already among
///   them.</item>
/// </list>
/// <para>A step nobody closes stays open and merges into the next one. That is
/// the price of a missed call site here, and it is a coarse undo rather than a
/// lost one.</para>
/// </summary>
public sealed class UndoStack
{
    /// <summary>
    /// How many steps are kept. A step holds the text of the documents it
    /// touched, and an edit touches one file, so the reference package's largest
    /// file fifty times over is still a few megabytes.
    /// </summary>
    public int Depth { get; init; } = 50;

    private readonly List<Step> _undo = new();
    private readonly List<Step> _redo = new();
    private readonly Dictionary<string, (XDocument Doc, EventHandler<XObjectChangeEventArgs> Handler)> _hooks =
        new(StringComparer.Ordinal);

    private Package? _pkg;
    private Step? _open;
    private bool _suspended;

    /// <summary>Follow this package, forgetting whatever was on the stack.</summary>
    public void Watch(Package? pkg)
    {
        Unhook();
        _undo.Clear();
        _redo.Clear();
        _open = null;
        _pkg = pkg;
        Hook();
    }

    public bool CanUndo => _undo.Count > 0 || (_open is not null && Changed(_open));
    public bool CanRedo => _redo.Count > 0;

    /// <summary>What undo would take back, for the button's tooltip.</summary>
    public string? UndoLabel =>
        _open is not null && Changed(_open) ? _open.Label
        : _undo.Count > 0 ? _undo[^1].Label
        : null;

    /// <summary>What redo would put back.</summary>
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>
    /// Start a step. Needed before an action that creates a document, because a
    /// step notes which documents there were when it began.
    /// </summary>
    public void Begin(string label)
    {
        Close();
        if (_pkg is null) return;
        _open = NewStep(label);
    }

    /// <summary>
    /// Close the step that is open and give it a name — what an interface that
    /// reports an edit after making it can call.
    /// </summary>
    public void Close(string label)
    {
        if (_open is not null) _open.Label = label;
        Close();
    }

    /// <summary>
    /// Note the state of one file before something LINQ to XML cannot report
    /// changes it: the raw bytes of a resource
    /// (<see cref="ResourceImport"/>), or a document about to be dropped from
    /// <see cref="Package.Docs"/>.
    /// </summary>
    public void Capture(string key)
    {
        if (_pkg is null || _suspended) return;
        Take(_open ??= NewStep(Unnamed), key);
    }

    /// <summary>An action that never named itself is still an action.</summary>
    private static string Unnamed => T("Change");

    /// <summary>Take back the last step. False when there was nothing to take back.</summary>
    public bool Undo()
    {
        if (_pkg is null) return false;

        Close();                 // the action in progress is an action too
        if (_undo.Count == 0) return false;

        _redo.Add(Apply(Pop(_undo)));
        return true;
    }

    /// <summary>Put back what <see cref="Undo"/> took.</summary>
    public bool Redo()
    {
        if (_pkg is null || _redo.Count == 0) return false;
        _undo.Add(Apply(Pop(_redo)));
        return true;
    }

    // -----------------------------------------------------------------
    // Recording
    // -----------------------------------------------------------------

    /// <summary>
    /// Close the open step. A step that changed nothing is dropped rather than
    /// pushed, so an action the user cancelled does not become an undo that
    /// undoes nothing.
    /// </summary>
    private void Close()
    {
        // A document created by the action is one nobody is watching yet.
        Hook();

        if (_open is null) return;
        if (!Changed(_open)) { _open = null; return; }

        // Documents that were not there when the step began. Their "before" has
        // to be written as "not there" outright: Take reads the state as it
        // stands, and by now that is the created file, so a step built from it
        // would restore the new window instead of removing it.
        foreach (string key in Created(_open).ToList())
        {
            if (!_open.Keys.Add(key)) continue;
            _open.Docs[key] = null;
            _open.Files[key] = null;
            _open.Paths[key] = null;
        }

        _undo.Add(_open);
        if (_undo.Count > Depth) _undo.RemoveAt(0);
        _redo.Clear();
        _open = null;
    }

    private bool Changed(Step step) => step.Keys.Count > 0 || Created(step).Any();

    private IEnumerable<string> Created(Step step) =>
        _pkg is null ? Enumerable.Empty<string>()
        : _pkg.Docs.Keys.Where(k => !step.DocKeys.Contains(k));

    /// <summary>The state of one file as it stands, unless the step has it already.</summary>
    private void Take(Step step, string key)
    {
        var pkg = _pkg!;
        if (!step.Keys.Add(key)) return;

        step.Docs[key] = pkg.Docs.TryGetValue(key, out var doc) ? PackageWriter.SerializeText(doc) : null;
        step.Files[key] = pkg.Files.TryGetValue(key, out var bytes) ? bytes : null;
        step.Paths[key] = pkg.OriginalPaths.TryGetValue(key, out var path) ? path : null;
    }

    private static Step Pop(List<Step> from)
    {
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        return step;
    }

    /// <summary>
    /// A step as things stand: the marks, and which documents there are. Both
    /// are whole-package and small enough to copy outright — 105 keys and three
    /// short sets against the alternative of asking every action what it means
    /// to touch.
    /// </summary>
    private Step NewStep(string label)
    {
        var pkg = _pkg!;
        return new Step
        {
            Label = label,
            DocKeys = new HashSet<string>(pkg.Docs.Keys, StringComparer.Ordinal),
            DirtyFiles = new HashSet<string>(pkg.DirtyFiles, StringComparer.Ordinal),
            AddedFiles = new HashSet<string>(pkg.AddedFiles, StringComparer.Ordinal),
            DirtyWindows = pkg.Windows.Where(w => w.Dirty).Select(w => w.Id)
                .ToHashSet(StringComparer.Ordinal),
        };
    }

    // -----------------------------------------------------------------
    // Restoring
    // -----------------------------------------------------------------

    /// <summary>
    /// Put a step's state back, and hand out the step that would put the
    /// current state back again — which is what makes undo and redo one piece
    /// of code.
    /// </summary>
    private Step Apply(Step step)
    {
        var pkg = _pkg!;
        var inverse = NewStep(step.Label);
        foreach (string key in step.Keys) Take(inverse, key);

        _suspended = true;
        try
        {
            foreach (string key in step.Keys)
            {
                if (step.Docs[key] is string text)
                    pkg.Docs[key] = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
                else
                    pkg.Docs.Remove(key);

                if (step.Files[key] is byte[] bytes) pkg.Files[key] = bytes;
                else pkg.Files.Remove(key);

                if (step.Paths[key] is string path) pkg.OriginalPaths[key] = path;
                else pkg.OriginalPaths.Remove(key);
            }

            Restore(pkg.DirtyFiles, step.DirtyFiles);
            Restore(pkg.AddedFiles, step.AddedFiles);

            // The tables point into trees that have just been replaced.
            PackageLoader.Reindex(pkg);

            // After the rebuild: Reindex creates the windows afresh.
            foreach (var w in pkg.Windows) w.Dirty = step.DirtyWindows.Contains(w.Id);
        }
        finally
        {
            _suspended = false;
        }

        // The watched documents are the old objects now.
        Unhook();
        Hook();
        return inverse;
    }

    private static void Restore(HashSet<string> target, HashSet<string> from)
    {
        target.Clear();
        foreach (string s in from) target.Add(s);
    }

    // -----------------------------------------------------------------
    // Watching the documents
    // -----------------------------------------------------------------

    private void Hook()
    {
        if (_pkg is null) return;

        foreach (var kv in _pkg.Docs)
        {
            if (_hooks.TryGetValue(kv.Key, out var had) && ReferenceEquals(had.Doc, kv.Value)) continue;

            string key = kv.Key;
            EventHandler<XObjectChangeEventArgs> handler = (_, _) => Capture(key);
            kv.Value.Changing += handler;
            _hooks[key] = (kv.Value, handler);
        }
    }

    private void Unhook()
    {
        foreach (var kv in _hooks) kv.Value.Doc.Changing -= kv.Value.Handler;
        _hooks.Clear();
    }

    /// <summary>What one action changed, as it was before the action.</summary>
    private sealed class Step
    {
        public string Label = "";

        /// <summary>The files this step has a "before" for.</summary>
        public readonly HashSet<string> Keys = new(StringComparer.Ordinal);

        /// <summary>Document text, or null when the file had no document.</summary>
        public readonly Dictionary<string, string?> Docs = new(StringComparer.Ordinal);

        /// <summary>Raw bytes, or null when the file was not there.</summary>
        public readonly Dictionary<string, byte[]?> Files = new(StringComparer.Ordinal);

        /// <summary>The spelling the file arrived under, or null.</summary>
        public readonly Dictionary<string, string?> Paths = new(StringComparer.Ordinal);

        /// <summary>Which documents the package had when the step began.</summary>
        public required HashSet<string> DocKeys { get; init; }

        public required HashSet<string> DirtyFiles { get; init; }
        public required HashSet<string> AddedFiles { get; init; }
        public required HashSet<string> DirtyWindows { get; init; }
    }
}
