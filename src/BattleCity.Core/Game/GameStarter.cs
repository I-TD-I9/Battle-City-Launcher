using System.Diagnostics;
using BattleCity.Client;
using BattleCity.Display;

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
        // Refused while the old copy is running.
        LauncherFiles.Replace(target, patched);
        return target;
    }

    /// <summary>
    /// Starts the patched client at <paramref name="client"/> in <paramref name="workingFolder"/>,
    /// at <paramref name="resolution"/>. <paramref name="home"/> is the launcher's folder as the
    /// client can be given it (CncDdraw.Folder), or null when it has no such form.
    /// </summary>
    public static Process Start(string client, string workingFolder, Resolution resolution, string? home) =>
        Process.Start(StartInfo(client, workingFolder, resolution, home)) ?? throw new InvalidOperationException("Windows did not start the game");

    internal static ProcessStartInfo StartInfo(string client, string workingFolder, Resolution resolution, string? home)
    {
        // cnc-ddraw looks for its settings beside its DLL by the path GetModuleFileNameA gives
        // (cfg_init, src/config.c), which turns whatever the system's code page can't spell into
        // question marks. So the copy starts by the launcher's folder in a form the code page can
        // spell, which the DLL's path then takes, and cnc-ddraw is told where the file is besides.
        // A path to settings of the player's own, for other games, never reaches this one.
        bool scaled = !resolution.IsDefault && home is not null;
        var info = new ProcessStartInfo(home is null ? client : Path.Join(home, Path.GetFileName(client)))
        {
            // The client opens its game files by relative paths, patch.txt first, and Konami's
            // ygo.exe started it as ./online_pc.dat from the install folder. The copy runs in the
            // launcher's working folder instead, whose patch.txt leads the client to the game's
            // archives and folder by absolute path (WorkingFolder), and so to the sound archives in
            // VOICE and to Region.dat; or, when that can't be, in the game's folder itself. Its
            // saves go to the folder ClientKey names, wherever it runs.
            WorkingDirectory = workingFolder,
            // A new profile starts in full screen at 16 bits a pixel (0x5fbc5b), a mode today's
            // Windows often shows as a blank screen, and ClientKey starts every player on a new
            // profile. -win runs the game in a window instead, whatever the profile says (read at
            // 0x4164b5, taken at 0x4150d1). For a bigger window or full screen, -32 asks for the
            // client's own full screen at 32 bits a pixel, which cnc-ddraw takes over and scales
            // (CncDdraw). The client looks for -32 after -win (0x416544), so given both it would
            // take -32: only one is ever passed.
            Arguments = scaled ? "-32" : "-win",
            UseShellExecute = false,
        };
        info.Environment.Remove(CncDdraw.ConfigFileVariable);
        if (scaled)
            info.Environment[CncDdraw.ConfigFileVariable] = Path.Join(home, CncDdraw.IniName);
        return info;
    }
}
