using Microsoft.Win32;

namespace BattleCity.Game;

/// <summary>
/// The registry key Battle City's copy of the client reads its settings from, under
/// HKEY_CURRENT_USER (ClientPatcher.SetRegistryRoot): the folder it keeps its settings, profiles
/// and decks in, and the release it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The client takes its save folder from CommonDir. Left to itself, it would find an older
/// install's key, and with it that install's profiles, where a character remade under its old
/// name finds its old deck. On a machine without one, it would make a folder of its own beside the
/// game's, or use the game's folder itself when that has the name the client gives, with whatever
/// settings a previous owner left there. So the launcher points it at a folder in its own.
/// </para>
/// <para>
/// Release names the edition the game was installed as, and every installer wrote it. The client
/// compares it with the names of Konami's other editions, but only over as many characters as
/// Release holds, so an empty or missing one matches the first name it is compared with: at
/// start-up the client takes itself for the "matel" edition (0x414a96), and the Options screen is
/// built in its Korean version (0x4a86bb), whose layout the European archive does not hold, and
/// crashes. "koe", which Konami's October 2005 installer wrote, matches none of them.
/// </para>
/// </remarks>
public static class ClientKey
{
    /// <summary>The client's own key, read from HKEY_CURRENT_USER by the launcher's copy.</summary>
    public const string Key = @"SOFTWARE\KONAMI\Yu-Gi-Oh! ONLINE\system";

    public const string Release = "koe";

    public static string SaveFolder { get; } = Path.Combine(LauncherSettings.Folder, "Saves");

    /// <summary>The folder beside the game's that the saves go to when the client cannot open <see cref="SaveFolder"/>.</summary>
    public const string Fallback = "Battle City saves";

    /// <summary>
    /// Writes the key, CommonDir and Release, and makes the folder CommonDir names. Returns that
    /// folder, which is <paramref name="saveFolder"/> unless the client could not open it, or null
    /// when the client can open no folder of the launcher's, and so picks its own.
    /// </summary>
    public static string? Prepare(string saveFolder, string? gameFolder, string key = Key) =>
        Prepare(saveFolder, gameFolder, key, AnsiPath.Fits, AnsiPath.ShortName);

    internal static string? Prepare(string saveFolder, string? gameFolder, string key,
                                    Func<string, bool> fits, Func<string, string?> shortName)
    {
        // The client opens its files through Windows' ANSI functions, so its folder must be one
        // the system's code page can spell (AnsiPath).
        string? usable = AnsiPath.Folder(saveFolder, fits, shortName);
        if (usable is null && gameFolder is not null
            && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gameFolder)) is { Length: > 0 } parent)
            usable = AnsiPath.Folder(Path.Combine(parent, Fallback), fits, shortName);
        using var system = Registry.CurrentUser.CreateSubKey(key, writable: true);
        if (usable is null)
            system.DeleteValue("CommonDir", throwOnMissingValue: false);
        else
            system.SetValue("CommonDir", usable, RegistryValueKind.String);
        system.SetValue("Release", Release, RegistryValueKind.String);
        return usable;
    }
}
