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

    Ready,
}

/// <summary>A folder the game may be installed in, and what the launcher found there.</summary>
/// <param name="Client">When the folder is ready, the supported client to patch a copy of.</param>
public sealed record GameFolder(string Path, GameStatus Status, string? Client)
{
    public static GameFolder Inspect(string path) => Inspect(path, SupportedClient.IsSupported);

    internal static GameFolder Inspect(string path, Func<string, bool> supported)
    {
        if (!Directory.Exists(path))
            return new(path, GameStatus.NotFound, null);
        string client = System.IO.Path.Combine(path, SupportedClient.FileName);
        if (!File.Exists(client))
            return new(path, GameStatus.NoClient, null);
        if (supported(client))
            return new(path, GameStatus.Ready, client);
        // The server's own patch tool, run on an install, keeps the retail client beside the one
        // it changed.
        string kept = client + ".orig";
        if (File.Exists(kept) && supported(kept))
            return new(path, GameStatus.Ready, kept);
        return new(path, GameStatus.Unsupported, null);
    }
}
