using System.Text.Json;

namespace BattleCity.Accounts;

/// <summary>
/// What the server's GET /api/info says: its sign-up rules, and the oldest launcher it still takes
/// when it names one.
/// </summary>
public sealed record ServerInfo(SignUpRules Rules, Version? OldestLauncher)
{
    /// <summary>Whether a launcher of this version is older than the server takes.</summary>
    public bool TooOld(Version launcher) => OldestLauncher is { } oldest && launcher < oldest;

    /// <summary>The info in a GET /api/info reply, or null when it is not one.</summary>
    internal static ServerInfo? FromInfo(JsonElement root) =>
        SignUpRules.FromInfo(root) is { } rules ? new ServerInfo(rules, Oldest(root)) : null;

    // No minimum, a null one or one that is no version all let any launcher in: a server from
    // before the setting sends none.
    static Version? Oldest(JsonElement root)
    {
        if (!root.TryGetProperty("launcher", out var launcher) || launcher.ValueKind != JsonValueKind.Object
            || !launcher.TryGetProperty("min", out var min) || min.ValueKind != JsonValueKind.String
            || !Version.TryParse(min.GetString(), out var version))
            return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
