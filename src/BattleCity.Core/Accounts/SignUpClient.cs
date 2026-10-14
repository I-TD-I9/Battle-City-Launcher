using System.Net;
using System.Text;
using System.Text.Json;

namespace BattleCity.Accounts;

/// <summary>The outcome of a sign-up: the new account's name, or why the server refused it.</summary>
/// <param name="Error">The server's error code, or <see cref="SignUpClient.Unreachable"/>.</param>
/// <param name="Message">A sentence to show the player.</param>
/// <param name="RetryAfter">For a refusal under the server's limits, the seconds to wait.</param>
public sealed record SignUpResult(bool Ok, string? Name, string Error, string Message, int? RetryAfter)
{
    internal static SignUpResult Created(string name) => new(true, name, "", "", null);

    internal static SignUpResult Failed(string error, string message, int? retryAfter = null) =>
        new(false, null, error, message, retryAfter);
}

/// <summary>
/// The server's sign-up API: GET /api/info for the rules, POST /api/register to make an account
/// (docs/WEB-API.md in the server's repository is the contract).
/// </summary>
public sealed class SignUpClient(HttpClient http, Uri api)
{
    /// <summary>The error code of a sign-up that never reached the server.</summary>
    public const string Unreachable = "unreachable";

    const string NoConnection = "Could not reach the Battle City server. Check your internet connection and try again.";
    const string NotAnswering = "The Battle City server is not answering right now. Try again later.";
    const int LongestMessage = 300;

    /// <summary>The server's current rules and oldest launcher, or null when it cannot be reached.</summary>
    public async Task<ServerInfo?> GetInfoAsync(CancellationToken cancel = default)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(api, "api/info"), cancel);
            if (response.StatusCode != HttpStatusCode.OK)
                return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancel));
            return ServerInfo.FromInfo(json.RootElement);
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException or JsonException)
                                  && !cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<SignUpResult> RegisterAsync(string name, string password, CancellationToken cancel = default)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = name, ["password"] = password });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.PostAsync(new Uri(api, "api/register"), content, cancel);
            return Outcome(response.StatusCode, await response.Content.ReadAsByteArrayAsync(cancel), name);
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException) && !cancel.IsCancellationRequested)
        {
            return SignUpResult.Failed(Unreachable, NoConnection);
        }
    }

    /// <summary>What a reply to POST /api/register says.</summary>
    /// <remarks>
    /// Replies are read as bytes and parsed as the UTF-8 that JSON is, whatever charset they claim:
    /// a filtering proxy's own page may name one .NET cannot decode at all.
    /// </remarks>
    internal static SignUpResult Outcome(HttpStatusCode status, byte[] body, string name)
    {
        JsonElement root;
        try
        {
            using var json = JsonDocument.Parse(body);
            root = json.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Not the API's own answer: a proxy's error page while the server is down, for one.
            return SignUpResult.Failed("server_error", NotAnswering);
        }
        if (root.ValueKind != JsonValueKind.Object)
            return SignUpResult.Failed("server_error", NotAnswering);
        if ((int)status is >= 200 and < 300 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return SignUpResult.Created(Text(root, "name") ?? name);
        var message = Text(root, "message") ?? NotAnswering;
        if (message.Length > LongestMessage)
            message = message[..LongestMessage];
        int? retryAfter = root.TryGetProperty("retry_after", out var wait) && wait.ValueKind == JsonValueKind.Number
                          && wait.TryGetInt32(out var seconds) ? seconds : null;
        return SignUpResult.Failed(Text(root, "error") ?? "server_error", message, retryAfter);
    }

    static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
