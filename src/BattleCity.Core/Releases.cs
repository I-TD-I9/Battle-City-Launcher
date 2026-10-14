using System.Reflection;
using System.Text.Json;

namespace BattleCity;

/// <summary>A published version of the launcher.</summary>
public sealed record LauncherRelease(Version Version, Uri Page);

/// <summary>The launcher's own releases on GitHub, so a player hears when a newer one is out.</summary>
public static class Releases
{
    public static Uri ProjectPage { get; } = new("https://github.com/I-TD-I9/Battle-City-Launcher");

    /// <summary>The page of the newest release, wherever GitHub has it.</summary>
    public static Uri LatestPage { get; } = new("https://github.com/I-TD-I9/Battle-City-Launcher/releases/latest");

    static Uri LatestApi { get; } = new("https://api.github.com/repos/I-TD-I9/Battle-City-Launcher/releases/latest");

    /// <summary>This launcher's version, as major.minor.patch.</summary>
    public static Version Current { get; } = Trim(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>The newest release, or null when there is none or GitHub cannot be reached.</summary>
    public static async Task<LauncherRelease?> LatestAsync(HttpClient http, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
                return null;
            // As bytes, whatever charset the reply claims (SignUpClient.Outcome says why).
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancel));
            return Parse(json.RootElement);
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException or JsonException)
                                  && !cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The release a GitHub "latest release" reply describes, if it names one of this project's pages.</summary>
    internal static LauncherRelease? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String)
            return null;
        if (!Version.TryParse(tag.GetString()!.TrimStart('v', 'V'), out var version)
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var page)
            || !page.AbsoluteUri.StartsWith(ProjectPage.AbsoluteUri + "/", StringComparison.Ordinal))
            return null;
        return new LauncherRelease(Trim(version), page);
    }

    static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
