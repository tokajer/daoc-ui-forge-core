using DaocUiForge.Core.Settings;
using Xunit;

namespace DaocUiForge.Tests;

/// <summary>
/// What the editor remembers between runs (DAoCEd's <c>Settings</c>). The
/// point of these is the failure cases: a settings file is a convenience, and
/// nothing about it may keep the program from starting.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "daoc-ui-forge-tests-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Defaults_are_what_the_preview_starts_with()
    {
        var s = new AppSettings();

        Assert.True(s.ReopenLastPackage);
        Assert.True(s.ShowGrid);
        Assert.True(s.ShowSampleData);
        Assert.True(s.MarkMissing);
        Assert.False(s.ShowZones);
        Assert.False(s.ShowBackdrop);
        Assert.Equal(100, s.Zoom);
        Assert.True(s.WarnBeforeDeleting);
        Assert.Null(s.StageColor);       // null keeps the gradient of the original
        Assert.Equal(270, s.ListPaneWidth);
        Assert.Equal(280, s.InspectorPaneWidth);
    }

    [Fact]
    public void A_round_trip_keeps_every_value()
    {
        var s = new AppSettings
        {
            PackageFolder = "/games/daoc/ui/custom",
            ReopenLastPackage = false,
            LastPackage = "/games/daoc/ui/custom",
            StageColor = "#112233",
            BackdropImage = "/pictures/camelot.png",
            SelectionColor = "#00ff00",
            Zoom = 200,
            ShowGrid = false,
            ShowBackdrop = true,
            ShowSampleData = false,
            MarkMissing = false,
            ShowZones = true,
            ListPaneWidth = 340,
            InspectorPaneWidth = 420,
            TagLine = "Edited with DAoC UI Forge",
            WarnBeforeDeleting = false,
        };

        Assert.True(s.Save(File_));
        var back = AppSettings.Load(File_);

        Assert.Equal(s.PackageFolder, back.PackageFolder);
        Assert.Equal(s.ReopenLastPackage, back.ReopenLastPackage);
        Assert.Equal(s.LastPackage, back.LastPackage);
        Assert.Equal(s.StageColor, back.StageColor);
        Assert.Equal(s.BackdropImage, back.BackdropImage);
        Assert.Equal(s.SelectionColor, back.SelectionColor);
        Assert.Equal(s.Zoom, back.Zoom);
        Assert.Equal(s.ShowGrid, back.ShowGrid);
        Assert.Equal(s.ShowBackdrop, back.ShowBackdrop);
        Assert.Equal(s.ShowSampleData, back.ShowSampleData);
        Assert.Equal(s.MarkMissing, back.MarkMissing);
        Assert.Equal(s.ShowZones, back.ShowZones);
        Assert.Equal(s.ListPaneWidth, back.ListPaneWidth);
        Assert.Equal(s.InspectorPaneWidth, back.InspectorPaneWidth);
        Assert.Equal(s.TagLine, back.TagLine);
        Assert.Equal(s.WarnBeforeDeleting, back.WarnBeforeDeleting);
    }

    [Fact]
    public void Saving_creates_the_folder_it_needs()
    {
        Assert.False(Directory.Exists(_dir));
        Assert.True(new AppSettings().Save(File_));
        Assert.True(System.IO.File.Exists(File_));
    }

    [Fact]
    public void A_missing_file_gives_the_defaults()
    {
        Assert.True(AppSettings.Load(File_).ShowGrid);
    }

    /// <summary>
    /// Refusing to start over a damaged settings file would be out of all
    /// proportion — and the file stays where it is, so it can be looked at.
    /// </summary>
    [Fact]
    public void A_damaged_file_gives_the_defaults_and_is_left_alone()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File_, "{ this is not json");

        Assert.Equal(100, AppSettings.Load(File_).Zoom);
        Assert.Equal("{ this is not json", System.IO.File.ReadAllText(File_));
    }

    /// <summary>A file written by a later version must not stop an earlier one.</summary>
    [Fact]
    public void Keys_it_does_not_know_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File_, """{ "Zoom": 150, "SomethingFromLater": 3 }""");

        Assert.Equal(150, AppSettings.Load(File_).Zoom);
    }

    // ---------------------------------------------------------------
    // Validation of a hand-edited file
    // ---------------------------------------------------------------

    [Fact]
    public void A_value_outside_its_range_is_taken_as_the_nearest_one()
    {
        /* Until 2026-09-08 a Zoom of 9000 was clamped when it reached the
           slider and written back as 9000 — the file kept a value nothing
           would ever use. */
        var s = new AppSettings { Zoom = 9000, ListPaneWidth = 5, InspectorPaneWidth = 9999 };

        var repairs = s.Validate();

        Assert.Equal(AppSettings.MaxZoom, s.Zoom);
        Assert.Equal(AppSettings.MinListPaneWidth, s.ListPaneWidth);
        Assert.Equal(AppSettings.MaxInspectorPaneWidth, s.InspectorPaneWidth);
        Assert.Equal(3, repairs.Count);
    }

    [Fact]
    public void A_value_inside_its_range_is_left_alone_and_said_nothing_about()
    {
        var s = new AppSettings { Zoom = 150, ListPaneWidth = 300, SelectionColor = "#abc" };

        Assert.Empty(s.Validate());
        Assert.Equal(150, s.Zoom);
        Assert.Equal("#abc", s.SelectionColor);   // the short form is a colour too
    }

    [Fact]
    public void A_colour_that_cannot_be_read_goes_back_to_its_default()
    {
        var s = new AppSettings { SelectionColor = "reddish", StageColor = "#12345" };

        var repairs = s.Validate();

        Assert.Equal(AppSettings.DefaultSelectionColor, s.SelectionColor);
        Assert.Null(s.StageColor);   // null is the gradient, not another colour
        Assert.Equal(2, repairs.Count);
    }

    [Fact]
    public void A_stage_colour_of_null_is_a_value_and_not_a_fault()
    {
        // null means "keep the gradient of the original", which is what the
        // checkbox in the dialog writes.
        var s = new AppSettings { StageColor = null };

        Assert.Empty(s.Validate());
        Assert.Null(s.StageColor);
    }

    [Fact]
    public void A_language_this_build_does_not_have_goes_back_to_the_systems()
    {
        // Otherwise it falls back to English on every start without saying so.
        var s = new AppSettings { Language = "fr" };

        Assert.Single(s.Validate());
        Assert.Null(s.Language);

        var known = new AppSettings { Language = "de" };
        Assert.Empty(known.Validate());
        Assert.Equal("de", known.Language);
    }

    [Fact]
    public void Blank_and_duplicate_skipped_folders_are_dropped()
    {
        var s = new AppSettings
        {
            SkipFolders = new List<string> { "node_modules", "  ", "NODE_MODULES", " build " },
        };

        Assert.Single(s.Validate());
        Assert.Equal(new[] { "node_modules", "build" }, s.SkipFolders);
    }

    [Fact]
    public void Paths_are_not_checked_here()
    {
        /* A folder that is not there is not a value that cannot be read: a
           drive gets mounted, an installation moves, and clearing the setting
           would lose what the user typed over a condition that mends itself.
           They are checked where they are used. */
        var s = new AppSettings
        {
            PackageFolder = "/nowhere/at/all",
            GameFolder = "/nor/here",
            BackdropImage = "/nor/this.png",
            LastPackage = "/gone.zip",
        };

        Assert.Empty(s.Validate());
        Assert.Equal("/nowhere/at/all", s.PackageFolder);
    }

    [Fact]
    public void Loading_repairs_the_file_it_read()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File_,
            """{ "Zoom": 9000, "SelectionColor": "reddish" }""");

        var back = AppSettings.Load(File_);

        Assert.Equal(AppSettings.MaxZoom, back.Zoom);
        Assert.Equal(AppSettings.DefaultSelectionColor, back.SelectionColor);
    }

    [Fact]
    public void Copy_lets_a_dialog_be_cancelled()
    {
        var s = new AppSettings { Zoom = 100 };
        var edited = s.Copy();
        edited.Zoom = 250;

        Assert.Equal(100, s.Zoom);
        Assert.Equal(250, edited.Zoom);
    }

    [Fact]
    public void The_settings_file_lives_under_the_users_application_data()
    {
        string path = AppSettings.DefaultPath;

        Assert.EndsWith(Path.Combine("daoc-ui-forge", "settings.json"), path);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            path);
    }
}
