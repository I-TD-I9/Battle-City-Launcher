using System.Diagnostics;
using BattleCity.Client;

namespace BattleCity.Game;

/// <summary>
/// Makes the patched client and starts it. The copy goes in the launcher's own folder, never in
/// the game's: that may sit under Program Files, which only an administrator may change, and the
/// player's own files stay as they were.
/// </summary>
public static class GameStarter
{
    public const string ClientName = "online_pc.exe";

    /// <summary>
    /// Writes the client patched for <paramref name="server"/> into <paramref name="folder"/>,
    /// unless it is there already, and returns its path.
    /// </summary>
    public static string Prepare(GameFolder game, ServerEndpoints server, string folder)
    {
        if (game.Status != GameStatus.Ready || game.Client is null)
            throw new InvalidOperationException($"{game.Path} holds no supported client");
        byte[] retail = File.ReadAllBytes(game.Client);
        // Checked again here: the file may have changed since the folder was looked at.
        if (SupportedClient.HashOf(retail) != SupportedClient.Sha256)
            throw new PatchException($"{game.Client} has changed since the launcher checked it");
        byte[] patched = ClientPatcher.Patch(retail, server);
        if (server.IsPublic && SupportedClient.HashOf(patched) != SupportedClient.PublicSha256)
            throw new PatchException("the patched client is not the file Battle City expects");

        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, ClientName);
        if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(patched))
            return target;
        string partial = target + ".new";
        try
        {
            File.WriteAllBytes(partial, patched);
            // Refused while the old copy is running.
            File.Move(partial, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            throw;
        }
        return target;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Overwritten by the next attempt.
        }
    }

    /// <summary>Starts the patched client at <paramref name="client"/> from the game's folder.</summary>
    public static Process Start(string client, GameFolder game) =>
        Process.Start(StartInfo(client, game)) ?? throw new InvalidOperationException("Windows did not start the game");

    internal static ProcessStartInfo StartInfo(string client, GameFolder game) => new(client)
    {
        // The client opens its game files by relative paths: patch.txt and the archives it names,
        // the sound archives in VOICE and Region.dat, and Konami's ygo.exe started it as
        // ./online_pc.dat from the install folder. So the copy runs with the game's folder as its
        // working directory, wherever the copy itself lives. Its saves go to the folder ClientKey
        // names.
        WorkingDirectory = game.Path,
        // A new profile starts in full screen at 16 bits a pixel (0x5fbc5b), a mode today's
        // Windows often shows as a blank screen, and ClientKey starts every player on a new
        // profile. -win runs the game in a window instead, whatever the profile says (read at
        // 0x4164b5, taken at 0x4150d1).
        Arguments = "-win",
        UseShellExecute = false,
    };
}
