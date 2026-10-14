using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BattleCity.Launcher;

/// <summary>Whether the launcher was started with "Run as administrator".</summary>
/// <remarks>
/// The game must not inherit that. Run as administrator it would load DLLs from the copy's folder,
/// which any of the player's own programs may write to, and it would save profiles in the install
/// folder itself rather than where Windows keeps them for an ordinary run, so the two would drift
/// apart.
/// </remarks>
static class Elevation
{
    const int TokenElevationType = 18;
    const int TokenElevationTypeFull = 2;

    /// <summary>
    /// True for the full token UAC hands out on request. With UAC switched off every program runs
    /// with the one token there is, which is not this case: there is no other way to run there.
    /// </summary>
    public static bool ByRequest()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return GetTokenInformation(identity.Token, TokenElevationType, out int type, sizeof(int), out _)
               && type == TokenElevationTypeFull;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information,
                                           int length, out int returned);
}
