using DaocUiForge.Core.Diagnostics;
using DaocUiForge.Core.Model;

namespace DaocUiForge.Core.Ingest;

/// <summary>
/// The game installation beside the package. DAoCEd keeps the folder as
/// <c>GameDir</c> and uses it to start the client; here it is what makes the
/// textures a package points at but does not contain — <c>atlantis/emoticons.tga</c>,
/// <c>atlantis/smallarrows.dds</c> — actually draw.
///
/// <para><b>Why an index and not a load.</b> A DAoC installation is several
/// gigabytes. Reading it into <see cref="Package.Files"/> would cost that much
/// memory, and worse, it would make every one of those files part of the package
/// for <see cref="Editing.PackageWriter"/> — a ZIP export would ship the game's
/// artwork. So the folder is walked once for the file types this editor can use,
/// and only the paths are kept (<see cref="Package.GameFiles"/>). The bytes are
/// read when something asks for them, which for a texture happens once because
/// <see cref="Render.TextureCache"/> keeps the decoded bitmap.</para>
///
/// <para><b>A package's own file always wins.</b> Resolution asks
/// <see cref="Package.Files"/> first and only then here, which is the order the
/// client uses too: <c>ui/custom</c> overrides the default interface, that being
/// the whole point of a custom UI.</para>
/// </summary>
public static class GameFolder
{
    /// <summary>
    /// What is worth indexing: the image formats the decoders read plus fonts.
    /// The rest of an installation is models, sound and the client itself, and
    /// walking a hundred thousand of those to find nothing is time spent for
    /// no answer.
    /// </summary>
    public static readonly string[] Extensions =
        { ".tga", ".dds", ".bmp", ".png", ".jpg", ".jpeg", ".ttf" };

    /// <summary>
    /// Index a game folder into the package. Replaces whatever was indexed
    /// before; a null or missing path just unmounts.
    /// </summary>
    /// <returns>How many files were indexed.</returns>
    public static int Mount(Package pkg, string? dir)
    {
        Unmount(pkg);

        if (string.IsNullOrWhiteSpace(dir)) return 0;
        if (!Directory.Exists(dir))
        {
            Log.Default.Warn("Game", $"The game folder {dir} is not there; nothing was indexed.");
            return 0;
        }

        pkg.GameFolder = dir;

        int count = 0;
        foreach (string path in Walk(dir))
        {
            string rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
            // Last one wins, as everywhere else a name can occur twice.
            pkg.GameFiles[FileResolver.NormPath(rel)] = path;
            count++;
        }

        // A font the package names but does not carry may be in there, and its
        // line height decides the size text is drawn at.
        PackageLoader.ResolveFontMetrics(pkg);

        Log.Default.Info("Game", $"Indexed {count} assets under {dir}.");
        return count;
    }

    /// <summary>Forget the game folder. Textures under it become unloadable again.</summary>
    public static void Unmount(Package pkg)
    {
        pkg.GameFolder = null;
        pkg.GameFiles.Clear();
        pkg.GameFolderTextures.Clear();
    }

    /// <summary>
    /// The bytes of one indexed file, or null when it has gone since the index
    /// was built. A folder can be renamed while the editor is open, and that is
    /// not worth an exception halfway through a drawing pass.
    /// </summary>
    public static byte[]? Read(Package pkg, string key)
    {
        if (!pkg.GameFiles.TryGetValue(key, out string? path)) return null;

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Default.Warn("Game", $"{path} could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve a path against the game folder, the same way
    /// <see cref="FileResolver"/> resolves it against the package.
    /// </summary>
    public static string? Find(Package pkg, string path) =>
        pkg.GameFiles.Count == 0 ? null : FileResolver.Find(pkg.GameFiles, path);

    /// <summary>
    /// Every usable file below <paramref name="dir"/>. Enumerated by hand
    /// rather than through <see cref="SearchOption.AllDirectories"/>, because
    /// one unreadable folder in an installation would otherwise abort the whole
    /// walk and leave the index half built with nothing saying so.
    /// </summary>
    private static IEnumerable<string> Walk(string dir)
    {
        var todo = new Stack<string>();
        todo.Push(dir);

        while (todo.Count > 0)
        {
            string here = todo.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(here);
                foreach (string sub in Directory.GetDirectories(here)) todo.Push(sub);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Default.Warn("Game", $"{here} could not be read: {e.Message}");
                continue;
            }

            foreach (string f in files)
                if (Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    yield return f;
        }
    }
}
