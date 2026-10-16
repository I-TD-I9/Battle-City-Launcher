using System.Text;
using BattleCity.Client;

namespace BattleCity.Game;

public enum GameStatus
{
    /// <summary>The folder does not exist.</summary>
    NotFound,

    /// <summary>The folder has no online_pc.dat.</summary>
    NoClient,

    /// <summary>The folder's online_pc.dat is another build than the one Battle City speaks to.</summary>
    Unsupported,

    /// <summary>
    /// The folder has the supported client, but not the game data it reads: patch.txt, or an
    /// archive patch.txt names that opens.
    /// </summary>
    DataMissing,

    Ready,
}

/// <summary>A folder the game may be installed in, and what the launcher found there.</summary>
/// <param name="Client">When the folder is ready, the supported client to patch a copy of.</param>
public sealed record GameFolder(string Path, GameStatus Status, string? Client)
{
    public const string PatchList = "patch.txt";

    public static GameFolder Inspect(string path) => Inspect(path, SupportedClient.IsSupported);

    internal static GameFolder Inspect(string path, Func<string, bool> supported)
    {
        if (!Directory.Exists(path))
            return new(path, GameStatus.NotFound, null);
        string client = System.IO.Path.Combine(path, SupportedClient.FileName);
        if (!File.Exists(client))
            return new(path, GameStatus.NoClient, null);
        string? retail = supported(client) ? client : null;
        // The server's own patch tool, run on an install, keeps the retail client beside the one
        // it changed.
        string kept = client + ".orig";
        if (retail is null && File.Exists(kept) && supported(kept))
            retail = kept;
        if (retail is null)
            return new(path, GameStatus.Unsupported, null);
        if (!HasData(path))
            return new(path, GameStatus.DataMissing, null);
        return new(path, GameStatus.Ready, retail);
    }

    /// <summary>
    /// Whether an archive the client reads its game data from is in <paramref name="folder"/>.
    /// The client takes the archives from patch.txt in its working folder, passing over any that
    /// will not open, and only falls back on a list built into it when patch.txt names none. That
    /// list names update files a February folder never has, since Konami's updater merged them
    /// all into merge.dat, which is the one line of the folder's patch.txt.
    /// </summary>
    internal static bool HasData(string folder)
    {
        string list = System.IO.Path.Combine(folder, PatchList);
        return File.Exists(list)
            && Archives(File.ReadAllBytes(list)).Any(name => IsArchive(System.IO.Path.Combine(folder, name + ".dat")));
    }

    // Konami's archives begin with this tag, which the client checks before it reads one.
    static ReadOnlySpan<byte> ArchiveTag => "KCEJYUGI"u8;

    static bool IsArchive(string path)
    {
        if (!File.Exists(path))
            return false;
        using var file = File.OpenRead(path);
        Span<byte> tag = stackalloc byte[ArchiveTag.Length];
        return file.ReadAtLeast(tag, tag.Length, throwOnEndOfStream: false) == tag.Length && tag.SequenceEqual(ArchiveTag);
    }

    /// <summary>
    /// The archives a patch.txt names, read as the client reads it: each line ends at its first
    /// line break, a line that is empty or starts with # is skipped, and the rest of a line, cut
    /// at its first ".dat", is an archive's name without the extension.
    /// </summary>
    internal static List<string> Archives(ReadOnlySpan<byte> text)
    {
        var names = new List<string>();
        foreach (var raw in Encoding.Latin1.GetString(text).Split('\n'))
        {
            string line = raw;
            int end = line.IndexOf('\r');
            if (end >= 0)
                line = line[..end];
            if (line.Length == 0 || line[0] == '#')
                continue;
            int dat = line.IndexOf(".dat", StringComparison.Ordinal);
            names.Add(dat >= 0 ? line[..dat] : line);
        }
        return names;
    }
}
