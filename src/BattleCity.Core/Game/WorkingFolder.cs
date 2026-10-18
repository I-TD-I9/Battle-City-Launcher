namespace BattleCity.Game;

/// <summary>Why the client starts in the game's folder, without the card pictures.</summary>
public enum NoPictures
{
    /// <summary>The game's folder, or an archive its patch.txt names, has no path the client can read.</summary>
    GameFolder,

    /// <summary>The launcher's working folder has none.</summary>
    LauncherFolder,
}

/// <summary>
/// The folder the client starts in: one of the launcher's own, LauncherSettings.Folder\Game,
/// rather than the game's.
/// </summary>
/// <remarks>
/// At start-up the client reads patch.txt from its working folder (sub_416f60, called at
/// 0x4162a4), and it then looks for every file it opens under each line of it in turn, and last
/// under the bare path; at each place a loose file comes before an archive (sub_578140, which
/// opens through sub_577870). The launcher's patch.txt names its folder of card pictures, bc,
/// first, then the archives the game's own patch.txt names and the game's folder, both by
/// absolute path. So the client still finds everything it found before, the sound archives in
/// VOICE and Region.dat among them, through the game's folder line, while a file in bc comes
/// first; and the game's folder is never written to.
/// </remarks>
public static class WorkingFolder
{
    public static string Path { get; } = System.IO.Path.Combine(LauncherSettings.Folder, "Game");

    /// <summary>The folder of card pictures in it, kept the same as the server's (Pictures.CardPictures).</summary>
    public const string Overrides = "bc";

    // The client reads each line with fgets into 0x100 bytes (0x416fc0): the line, its CR LF and
    // the zero after them must fit.
    internal const int LongestLine = 0x100 - 3;

    /// <summary>
    /// Writes patch.txt into <paramref name="folder"/> for <paramref name="game"/>, and returns
    /// the folder to start the client in, in the form it can be given. When that folder, or some
    /// line of the file, has no form the client can read, nothing is written, and the client
    /// starts in the game's folder instead, as it always did, without the card pictures: the
    /// reason comes with it.
    /// </summary>
    public static (string Start, NoPictures? Reason) Prepare(GameFolder game, string folder) =>
        Prepare(game.Path, folder, AnsiPath.Fits, AnsiPath.ShortName);

    /// <summary>
    /// The game's folder to start the client in when the launcher's can't be: in the form the
    /// client can be given it, or as it is when it has none.
    /// </summary>
    public static string Fallback(GameFolder game) => Fallback(game.Path, AnsiPath.Fits, AnsiPath.ShortName);

    internal static (string Start, NoPictures? Reason) Prepare(string gameFolder, string folder, Func<string, bool> fits,
                                                             Func<string, string?> shortName)
    {
        // The launcher's own folder first: when the client can't start there, a plainer game
        // folder would not help.
        if (AnsiPath.Folder(folder, fits, shortName) is not { } start)
            return (Fallback(gameFolder, fits, shortName), NoPictures.LauncherFolder);
        var lines = Lines(gameFolder, File.ReadAllBytes(System.IO.Path.Combine(gameFolder, GameFolder.PatchList)), fits, shortName);
        if (lines is null)
            return (Fallback(gameFolder, fits, shortName), NoPictures.GameFolder);
        LauncherFiles.Replace(System.IO.Path.Combine(folder, GameFolder.PatchList),
                              AnsiPath.CodePage().GetBytes(string.Concat(lines.Select(line => line + "\r\n"))));
        return (start, null);
    }

    // The client keeps the folder it starts in as GetCurrentDirectoryA spells it (0x575d8c), so
    // in the form the code page can spell when there is one.
    internal static string Fallback(string gameFolder, Func<string, bool> fits, Func<string, string?> shortName) =>
        AnsiPath.Form(gameFolder, fits, shortName) ?? gameFolder;

    /// <summary>
    /// The launcher's patch.txt: bc, then each archive the game's own <paramref name="patchList"/>
    /// names, in its order, by absolute path, then the game's folder; or null when one of them has
    /// no form the client can read.
    /// </summary>
    /// <remarks>
    /// A path goes in as it is when the system's code page can spell it and it has no spaces, and
    /// otherwise by its 8.3 short name. The client cuts every line at its first ".dat" (0x417037),
    /// so a folder with ".dat" in its name cannot be named at all, unless by a short name, whose
    /// capitals the cut passes over.
    /// </remarks>
    internal static List<string>? Lines(string gameFolder, ReadOnlySpan<byte> patchList,
                                        Func<string, bool> fits, Func<string, string?> shortName)
    {
        string game = System.IO.Path.TrimEndingDirectorySeparator(gameFolder);
        var archives = GameFolder.Archives(patchList);
        foreach (var folder in Forms(game, fits, shortName))
        {
            var lines = new List<string> { Overrides };
            // The client takes a line up to its first .dat, so each line ends there.
            foreach (var archive in archives)
                if (Archive(game, folder, archive + ".dat", fits, shortName) is { } line)
                    lines.Add(line);
            if (lines.Count == archives.Count + 1)
                return [.. lines, folder];
        }
        return null;
    }

    // The archive at name, relative to the game's folder or absolute, with its folder in a form
    // the client can read, the game's own given as folder, and its name as it was written.
    static string? Archive(string game, string folder, string name, Func<string, bool> fits, Func<string, string?> shortName)
    {
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(System.IO.Path.Combine(game, name));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        string parent = System.IO.Path.GetDirectoryName(full) ?? "";
        IEnumerable<string> forms = string.Equals(parent, game, StringComparison.OrdinalIgnoreCase) ? [folder] : Forms(parent, fits, shortName);
        foreach (var at in forms)
        {
            string line = System.IO.Path.Join(at, System.IO.Path.GetFileName(full));
            if (Readable(line, fits) && line.IndexOf(".dat", StringComparison.Ordinal) == line.Length - 4)
                return line;
        }
        return null;
    }

    // A folder as the client can be given it: itself, then its short name.
    static IEnumerable<string> Forms(string folder, Func<string, bool> fits, Func<string, string?> shortName)
    {
        if (Readable(folder, fits) && !folder.Contains(".dat", StringComparison.Ordinal))
            yield return folder;
        if (shortName(folder) is { } name && name != folder && Readable(name, fits) && !name.Contains(".dat", StringComparison.Ordinal))
            yield return name;
    }

    static bool Readable(string line, Func<string, bool> fits) =>
        line.Length <= LongestLine && !line.Contains(' ') && fits(line);
}
