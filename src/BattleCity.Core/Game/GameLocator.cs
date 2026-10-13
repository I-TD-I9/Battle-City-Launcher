using System.Security;
using Microsoft.Win32;

namespace BattleCity.Game;

/// <summary>Finds where the player installed the game.</summary>
public static class GameLocator
{
    // Where the installer records the game. It is a 32-bit program, so on 64-bit Windows its keys
    // live in the registry's 32-bit view, under WOW6432Node.
    const string SystemKey = @"SOFTWARE\KONAMI\Yu-Gi-Oh! ONLINE\system";
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{67FB570C-900D-4515-ACB3-1004074D63D3}";
    const string DefaultFolder = @"KONAMI\Yu-Gi-Oh! ONLINE";

    /// <summary>
    /// The folders worth looking in, most likely first: the one the player chose, the two the
    /// installer records, then the installer's default folder.
    /// </summary>
    public static IEnumerable<string> Candidates(string? chosen)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string?[] folders =
        [
            chosen,
            Registry32(SystemKey, "CommonDir"),
            Registry32(UninstallKey, "InstallLocation"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), DefaultFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), DefaultFolder),
        ];
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;
            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (seen.Add(full))
                yield return full;
        }
    }

    /// <summary>
    /// The first candidate holding the supported client. Failing that, the first holding some
    /// other client, so that the player hears why it will not do; failing that, null.
    /// </summary>
    public static GameFolder? Find(string? chosen)
    {
        GameFolder? other = null;
        foreach (var folder in Candidates(chosen))
        {
            GameFolder found;
            try
            {
                found = GameFolder.Inspect(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (found.Status == GameStatus.Ready)
                return found;
            if (found.Status == GameStatus.Unsupported)
                other ??= found;
        }
        return other;
    }

    static string? Registry32(string key, string value)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var sub = root.OpenSubKey(key);
            return sub?.GetValue(value) as string;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
