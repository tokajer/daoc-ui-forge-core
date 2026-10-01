using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaocUiForge.Core.Settings;

/// <summary>
/// What the editor remembers between runs. DAoCEd's <c>Settings</c> plus its
/// <c>SettingsDialog</c>; the HTML original has no counterpart, because a page
/// that is opened from a file has nowhere to keep anything.
///
/// <para><b>Only settings that do something.</b> DAoCEd's dialog has four tabs,
/// and half of "Saving" describes how it rewrites a file: indentation, tabs
/// against spaces, blank lines between elements, the encoding. This port does
/// not rewrite files — an untouched file comes back out byte for byte, the
/// indentation step is read off the document it goes into
/// (<see cref="Editing.XmlEdit.IndentUnitOf"/>) and the encoding off the bytes
/// (<see cref="Ingest.PackageLoader.XmlEncodingOf"/>). Offering those four as
/// knobs would promise a reformatting that deliberately does not happen. The
/// tag line survives, because a file this editor <i>creates</i> is a real case
/// since <see cref="Editing.PackageEditor.CreateWindow"/>.</para>
///
/// <para>Its <c>PreviewSize</c> goes the same way: DAoCEd draws into a fixed
/// game screen, while here a window is drawn at its own size and the stage is
/// the viewport.</para>
///
/// <para>Colours are stored as text (<c>#rgb</c> or <c>#rrggbb</c>), which is
/// what they are in DAoCEd's <c>settings.ini</c> too. Core does not parse them
/// into a colour: it has no colour type the interface shares. It does check the
/// shape in <see cref="Validate"/>, so a value the dialog would silently fall
/// back on is repaired in the file instead of sitting there looking valid.</para>
/// </summary>
public sealed class AppSettings
{
    // --- Program ------------------------------------------------------

    /// <summary>
    /// Where the file pickers start. DAoCEd's <c>CustomDir</c> — the
    /// <c>ui/custom</c> folder of the game installation, in practice.
    /// </summary>
    public string? PackageFolder { get; set; }

    /// <summary>DAoCEd's "Auto-load Most Recent Project at Startup".</summary>
    public bool ReopenLastPackage { get; set; } = true;

    /// <summary>The package last opened, folder or ZIP.</summary>
    public string? LastPackage { get; set; }

    /// <summary>
    /// The game installation — DAoCEd's <c>GameDir</c>, which it keeps in order
    /// to start the client.
    ///
    /// <para>Here it is what makes a package's Atlantis textures draw:
    /// <c>atlantis/emoticons.tga</c> is a correct reference to a file the game
    /// has and the package does not, and without this setting those
    /// elements can only ever be hatched. The folder is <b>indexed, not
    /// loaded</b>, and it never becomes part of the package on export — see
    /// <see cref="Ingest.GameFolder"/>.</para>
    ///
    /// <para>It stayed out of this dialog until the loading existed, on the
    /// grounds that a setting which does nothing is worse than no setting.</para>
    /// </summary>
    public string? GameFolder { get; set; }

    /// <summary>
    /// The interface language: a two-letter code
    /// (<see cref="Localization.Strings.Languages"/>), or null for whatever the
    /// system says. English is the project's own language and the fallback for
    /// anything a catalogue does not answer.
    /// </summary>
    public string? Language { get; set; }

    // --- Preview ------------------------------------------------------

    /// <summary>
    /// The stage behind the window. Null keeps the gradient of the original's
    /// stylesheet, which is what the preview has always looked like; a value
    /// replaces it with that flat colour (DAoCEd's <c>ColorBg</c>).
    /// </summary>
    public string? StageColor { get; set; }

    /// <summary>
    /// An image behind the window — DAoCEd's <c>BackgroundImage</c>. Without
    /// one the "game backdrop" is the painted stand-in the original uses, which
    /// says roughly "sky, grass, ground" and no more.
    /// </summary>
    public string? BackdropImage { get; set; }

    /// <summary>The marker around the selected element (<c>ColorSelect</c>).</summary>
    public string SelectionColor { get; set; } = DefaultSelectionColor;

    public const string DefaultSelectionColor = "#ff503c";

    // --- The stage bar, as it was left -------------------------------

    public int Zoom { get; set; } = 100;

    /// <summary>The range the zoom slider offers, and the range the file may hold.</summary>
    public const int MinZoom = 50;

    public const int MaxZoom = 300;

    public bool ShowGrid { get; set; } = true;

    public bool ShowBackdrop { get; set; }

    public bool ShowSampleData { get; set; } = true;

    public bool MarkMissing { get; set; } = true;

    public bool ShowZones { get; set; }

    // --- The panes, as they were left ---------------------------------

    /// <summary>
    /// Width of the list on the left. Both panes are draggable, and a width
    /// that resets on every start is a width nobody drags twice. Not in the
    /// settings dialog: it is set by dragging, not by typing.
    /// </summary>
    public int ListPaneWidth { get; set; } = 270;

    public const int MinListPaneWidth = 170;

    public const int MaxListPaneWidth = 600;

    /// <summary>Width of the properties pane on the right.</summary>
    public int InspectorPaneWidth { get; set; } = 280;

    public const int MinInspectorPaneWidth = 200;

    public const int MaxInspectorPaneWidth = 700;

    // --- Saving and editing -------------------------------------------

    /// <summary>
    /// A comment put at the top of a file this editor creates. DAoCEd writes
    /// one into every file it saves; here only created files can carry it,
    /// since the rest keep their own bytes.
    /// </summary>
    public string? TagLine { get; set; }

    /// <summary>DAoCEd's "Warn before deleting nodes".</summary>
    public bool WarnBeforeDeleting { get; set; } = true;

    /// <summary>
    /// Folder names a package is loaded without
    /// (<see cref="Ingest.PackageLoader.DefaultSkippedFolders"/>). Empty loads
    /// everything; dot-prefixed folders are left out regardless, because that
    /// one is the format's rule and not a preference
    /// (<see cref="Ingest.PackageLoader.IsPackageFile"/>).
    ///
    /// <para>A setting rather than a rule in the loader for the reason given
    /// there: "node_modules" is one ecosystem's name for its dependency store,
    /// and a list of such names is a thing a user should be able to see and
    /// change. It takes effect the next time a package is loaded.</para>
    /// </summary>
    public List<string> SkipFolders { get; set; } =
        new(Ingest.PackageLoader.DefaultSkippedFolders);

    // -----------------------------------------------------------------
    // Where it lives
    // -----------------------------------------------------------------

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The settings file: under the user's application data, which is
    /// <c>~/.config</c> on Linux and <c>%APPDATA%</c> on Windows. Not beside
    /// the executable, the way DAoCEd's <c>settings.ini</c> is — an installed
    /// program has no business writing into its own folder, and an AppImage
    /// cannot.
    /// </summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "daoc-ui-forge", "settings.json");

    /// <summary>
    /// Read the settings, or hand back the defaults.
    ///
    /// <para>A file that is missing, unreadable or damaged gives the defaults
    /// rather than an error: settings are a convenience, and refusing to start
    /// over one is out of proportion. The damaged file is left where it is, so
    /// it can still be looked at.</para>
    ///
    /// <para>What the file does hold goes through <see cref="Validate"/>, and
    /// every repair is logged. Until 2026-09-08 a value was only ever read: a
    /// <c>Zoom</c> of 9000 was clamped when it reached the slider and written
    /// back as 9000, and a colour the dialog could not parse kept sitting in the
    /// file looking valid. Loading is where a hand-edited file is first seen, so
    /// it is where it gets straightened out.</para>
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Format)
                           ?? new AppSettings();

            foreach (string repair in settings.Validate())
                Diagnostics.Log.Default.Warn("settings", repair);

            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Put every value back into the range it is read in, and say what was
    /// changed. Nothing here throws: a settings file is a convenience, so a
    /// value out of range is repaired rather than refused.
    ///
    /// <para><b>Paths are not checked.</b> A folder that does not exist is not
    /// the same as a value that cannot be read: an installation moves, a drive
    /// is not mounted yet, and clearing the setting would lose what the user
    /// typed over a condition that mends itself. They are checked where they are
    /// used, which is also the only place that can say so.</para>
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var repairs = new List<string>();

        Zoom = Clamp(Zoom, MinZoom, MaxZoom, nameof(Zoom), repairs);
        ListPaneWidth = Clamp(ListPaneWidth, MinListPaneWidth, MaxListPaneWidth,
            nameof(ListPaneWidth), repairs);
        InspectorPaneWidth = Clamp(InspectorPaneWidth, MinInspectorPaneWidth,
            MaxInspectorPaneWidth, nameof(InspectorPaneWidth), repairs);

        // StageColor is nullable on purpose: null is "keep the gradient", which
        // is a value and not a hole. Only a non-null one that cannot be read is
        // a fault, and it goes back to null rather than to some other colour.
        if (StageColor is not null && !IsColor(StageColor))
        {
            repairs.Add($"StageColor \"{StageColor}\" is not a colour - back to the gradient");
            StageColor = null;
        }

        if (!IsColor(SelectionColor))
        {
            repairs.Add($"SelectionColor \"{SelectionColor}\" is not a colour - back to {DefaultSelectionColor}");
            SelectionColor = DefaultSelectionColor;
        }

        // A language the build does not carry would fall back to English on
        // every start without ever saying so.
        if (Language is not null && !Localization.Strings.Languages.Any(l => l.Code == Language))
        {
            repairs.Add($"Language \"{Language}\" is not one this build has - back to the system's");
            Language = null;
        }

        // Blank and duplicate folder names do nothing, and a blank one in the
        // settings dialog's list looks like a row that got lost.
        int before = SkipFolders.Count;
        SkipFolders = SkipFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (SkipFolders.Count != before)
            repairs.Add($"SkipFolders: {before - SkipFolders.Count} blank or duplicate entries dropped");

        return repairs;
    }

    private static int Clamp(int value, int min, int max, string name, List<string> repairs)
    {
        int clamped = Math.Clamp(value, min, max);
        if (clamped != value) repairs.Add($"{name} {value} is outside {min}-{max} - taken as {clamped}");
        return clamped;
    }

    /// <summary>
    /// The shape the settings dialog's colour parser reads: <c>#rgb</c> or
    /// <c>#rrggbb</c>, with the hash optional. Core checks the shape without
    /// producing a colour, because it has no colour type to produce.
    /// </summary>
    private static bool IsColor(string? text)
    {
        string v = (text ?? "").Trim().TrimStart('#');
        return (v.Length == 3 || v.Length == 6) && v.All(Uri.IsHexDigit);
    }

    /// <summary>Write the settings. Returns whether it worked.</summary>
    public bool Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            string? folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(path, JsonSerializer.Serialize(this, Format));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// A copy, so a dialog can be cancelled without having changed anything.
    /// The list is copied with it: a shallow clone shares it, and a Cancel that
    /// leaves an edited list behind is the one way this could still leak.
    /// </summary>
    public AppSettings Copy()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.SkipFolders = new List<string>(SkipFolders);
        return copy;
    }
}
