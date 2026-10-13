using System.Security.Cryptography;

namespace BattleCity.Client;

/// <summary>
/// The one client build Battle City speaks to: online_pc.dat as the update of 23 June 2005 left
/// it, the last Konami released. The installer records it as Version 20050623, Release
/// koe-release.
/// </summary>
public static class SupportedClient
{
    public const string FileName = "online_pc.dat";
    public const long Length = 3_006_464;
    public const string Sha256 = "feb90693fb5a2e69f3d81240ba659205e25b3b2d52031c5d46494c6e07f86bd5";

    /// <summary>
    /// The same file patched for <see cref="ServerEndpoints.Public"/>: the gate's host name
    /// replaced and the PE checksum written, 31 bytes in all. A client with exactly these bytes
    /// has played on the live server.
    /// </summary>
    public const string PublicSha256 = "046b16d9f3ac000505f661c1d4028c04d5f4d0f42041b030f58cda94ca035eca";

    /// <summary>Whether the file at <paramref name="path"/> is the supported client.</summary>
    public static bool IsSupported(string path) =>
        new FileInfo(path).Length == Length && HashOf(path) == Sha256;

    public static string HashOf(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string HashOf(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }
}
