using System.Text.Json;
using System.Text.Json.Serialization;
using BattleCity.Client;

namespace BattleCity;

/// <summary>What the launcher remembers between runs, kept as settings.json in its folder.</summary>
public sealed class LauncherSettings
{
    /// <summary>The launcher's folder: its settings and the patched client.</summary>
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BattleCity");

    public static string DefaultPath { get; } = Path.Combine(Folder, "settings.json");

    // Lenient on reading, since the file is also edited by hand to try another server.
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The game folder the player chose, when the launcher could not find it alone.</summary>
    public string? GameFolder { get; set; }

    // For trying the launcher against another server, such as one on this machine. Nothing in the
    // launcher's window sets these.
    public string? GateHost { get; set; }
    public int? GatePort { get; set; }
    public string? Api { get; set; }

    /// <summary>The settings at <paramref name="path"/>; the defaults if there are none or they cannot be read.</summary>
    public static LauncherSettings Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + ".new";
        File.WriteAllText(partial, JsonSerializer.Serialize(this, Json));
        File.Move(partial, path, overwrite: true);
    }

    /// <summary>The Battle City server, with whichever overrides set here are usable.</summary>
    public ServerEndpoints Server()
    {
        var server = ServerEndpoints.Public;
        if (GateHost is { } host && ClientPatcher.IsHostName(host) && host.Length < ClientPatcher.GateHostSlot)
            server = server with { GateHost = host };
        if (GatePort is > 0 and <= 0xFFFF)
            server = server with { GatePort = GatePort.Value };
        // A sign-up carries a password, so only a server on this machine may take it unencrypted.
        if (Api is { } api && Uri.TryCreate(api, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            server = server with { Api = uri };
        return server;
    }
}
