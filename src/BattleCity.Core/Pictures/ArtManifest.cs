using System.Text.Json;
using System.Text.RegularExpressions;
using BattleCity.Client;

namespace BattleCity.Pictures;

/// <summary>One of the server's card picture files: where it goes in bc, and what it is.</summary>
/// <param name="Path">Relative to bc, with forward slashes, as data/card/list_card.txt.</param>
public readonly record struct ArtFile(string Path, string Sha256, long Size)
{
    /// <summary>The name the server keeps the file under, art/files/ and then this: its hash and extension.</summary>
    public string StoredName => Sha256 + System.IO.Path.GetExtension(Path);
}

/// <summary>
/// The list of card pictures the server hands out, its GET /art/manifest.json (docs/WEB-API.md
/// in the server's repository is the contract):
/// {"format": 1, "files": [{"path": "data/card/list_card.txt", "sha256": "...", "size": 117332}, ...]}.
/// </summary>
/// <remarks>
/// Every path stays inside data/card or data/mini, where the client finds its two card lists and
/// the pictures beside them, and none is named for a device such as con or com1, which Windows
/// would open instead of a file; so no manifest can put a file anywhere else. The limits are far
/// above what the game's 2351 cards need, and keep a broken server from filling the player's
/// disk.
/// </remarks>
public sealed partial class ArtManifest
{
    public const int MostFiles = 6000;
    public const long LargestFile = 2 * 1024 * 1024;
    public const long LargestTotal = 300L * 1024 * 1024;

    ArtManifest(IReadOnlyList<ArtFile> files) => Files = files;

    public IReadOnlyList<ArtFile> Files { get; }

    /// <summary>
    /// The manifest in <paramref name="json"/>; or, when it is none the launcher can take, what
    /// is wrong with it, and none of it is used.
    /// </summary>
    public static (ArtManifest? Manifest, string? Problem) Read(ReadOnlyMemory<byte> json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return (null, "it is not JSON");
        }
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out int version) || version != 1
            || !root.TryGetProperty("files", out var list) || list.ValueKind != JsonValueKind.Array)
            return (null, "it is no manifest of format 1");
        // A pack made for another build of the client would point its cards at the wrong pictures.
        if (root.TryGetProperty("client", out var client)
            && (client.ValueKind != JsonValueKind.String || client.GetString() != SupportedClient.Version))
            return (null, $"the pictures are for the client {client}, not {SupportedClient.Version}");
        if (list.GetArrayLength() > MostFiles)
            return (null, $"it lists {list.GetArrayLength()} files, more than {MostFiles}");

        var files = new List<ArtFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("sha256", out var sha) || sha.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number
                || !size.TryGetInt64(out long bytes))
                return (null, $"an entry is not a file: {entry}");
            var file = new ArtFile(path.GetString()!, sha.GetString()!, bytes);
            if (!PicturePath().IsMatch(file.Path))
                return (null, $"{file.Path} is no place for a picture");
            if (!Hash().IsMatch(file.Sha256))
                return (null, $"{file.Path} has no SHA-256 but {file.Sha256}");
            if (file.Size is < 0 or > LargestFile)
                return (null, $"{file.Path} is {file.Size} bytes");
            if (!paths.Add(file.Path))
                return (null, $"{file.Path} is listed twice");
            total += file.Size;
            files.Add(file);
        }
        if (total > LargestTotal)
            return (null, $"the files come to {total} bytes, more than {LargestTotal}");
        return (new ArtManifest(files), null);
    }

    [GeneratedRegex(@"\Adata/(card|mini)/(?!(con|prn|aux|nul|com[0-9]|lpt[0-9])\.)[a-z0-9_]+\.(jpg|txt)\z", RegexOptions.CultureInvariant)]
    private static partial Regex PicturePath();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Hash();
}
