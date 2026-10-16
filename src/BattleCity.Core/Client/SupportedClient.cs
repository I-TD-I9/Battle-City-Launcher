using System.Security.Cryptography;

namespace BattleCity.Client;

/// <summary>
/// The one client build Battle City speaks to: online_pc.dat of Konami's update of 9 February
/// 2006, which names itself Ver.060110.00 to the gate. It is the latest build that survives,
/// and it survives only as a copy of a game folder, which no installer registers.
/// </summary>
public static class SupportedClient
{
    public const string FileName = "online_pc.dat";
    public const string Version = "Ver.060110.00";
    public const long Length = 3_313_664;
    public const string Sha256 = "c57a405e6b890c17fe89551d36e6df64b56d826f101c675b67308c56381cf9c2";

    /// <summary>
    /// The same file patched for <see cref="ServerEndpoints.Public"/>: the gate's host name
    /// replaced and the PE checksum written, 31 bytes in all, as the server's own patch tool
    /// writes it when it changes the gate's host name alone.
    /// </summary>
    public const string PublicSha256 = "3c7dc09c5195354f9bae593d3f6766254c02cddb2200aee9f9fd5fed4a6bce94";

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
