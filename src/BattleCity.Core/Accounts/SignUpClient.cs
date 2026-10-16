using System.Net;
using System.Text;
using System.Text.Json;

namespace BattleCity.Accounts;

/// <summary>The outcome of a request to the account API: the account's UserID when it names one, or why the server refused.</summary>
/// <param name="Error">The server's error code, or <see cref="SignUpClient.Unreachable"/>.</param>
/// <param name="Message">A sentence to show the player.</param>
/// <param name="RetryAfter">For a refusal under the server's limits, the seconds to wait.</param>
/// <param name="TriesLeft">For a wrong code, how many more tries the code takes.</param>
/// <param name="Status">The reply's HTTP status, or 0 when there was none.</param>
public sealed record SignUpResult(bool Ok, string? Name, string Error, string Message, int? RetryAfter, int? TriesLeft = null,
                                  int Status = 0)
{
    internal static SignUpResult Done(string? name, int status) => new(true, name, "", "", null, Status: status);

    internal static SignUpResult Failed(string error, string message, int? retryAfter = null, int? triesLeft = null,
                                        int status = 0) =>
        new(false, null, error, message, retryAfter, triesLeft, status);
}

/// <summary>
/// The server's account API (docs/WEB-API.md in the server's repository is the contract): GET
/// /api/info for the rules, and the POSTs that make an account and set a new password, each
/// finished with a code the server mails to the player.
/// </summary>
public sealed class SignUpClient(HttpClient http, Uri api)
{
    /// <summary>The error code of a request that never reached the server.</summary>
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

    /// <summary>
    /// Asks for an account. The server mails a code to <paramref name="email"/>, and makes the
    /// account once the code comes back through <see cref="ConfirmAsync"/>.
    /// </summary>
    public Task<SignUpResult> RegisterAsync(string name, string password, string email, CancellationToken cancel = default) =>
        PostAsync("api/register", new() { ["name"] = name, ["password"] = password, ["email"] = email }, name, cancel);

    /// <summary>
    /// Makes the account that <paramref name="code"/> was mailed for. The password goes with it,
    /// so that only whoever asked for the account can finish it.
    /// </summary>
    public Task<SignUpResult> ConfirmAsync(string name, string code, string password, CancellationToken cancel = default) =>
        PostAsync("api/register/confirm", new() { ["name"] = name, ["code"] = code, ["password"] = password }, name, cancel);

    /// <summary>
    /// Asks for a code to set a new password, which the server mails to <paramref name="email"/>
    /// if an account has that address. The reply is the same whether one does or not.
    /// </summary>
    public Task<SignUpResult> RequestResetAsync(string email, CancellationToken cancel = default) =>
        PostAsync("api/reset", new() { ["email"] = email }, null, cancel);

    /// <summary>Sets a new password on the account of <paramref name="email"/>, with the code mailed to it.</summary>
    public Task<SignUpResult> ResetAsync(string email, string code, string password, CancellationToken cancel = default) =>
        PostAsync("api/reset/confirm", new() { ["email"] = email, ["code"] = code, ["password"] = password }, null, cancel);

    async Task<SignUpResult> PostAsync(string path, Dictionary<string, string> fields, string? name, CancellationToken cancel)
    {
        using var content = new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.PostAsync(new Uri(api, path), content, cancel);
            return Outcome(response.StatusCode, await response.Content.ReadAsByteArrayAsync(cancel), name);
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException) && !cancel.IsCancellationRequested)
        {
            return SignUpResult.Failed(Unreachable, NoConnection);
        }
    }

    /// <summary>What a reply to one of the POSTs says.</summary>
    /// <remarks>
    /// Replies are read as bytes and parsed as the UTF-8 that JSON is, whatever charset they claim:
    /// a filtering proxy's own page may name one .NET cannot decode at all.
    /// </remarks>
    internal static SignUpResult Outcome(HttpStatusCode status, byte[] body, string? name)
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
            return SignUpResult.Failed("server_error", NotAnswering, status: (int)status);
        }
        if (root.ValueKind != JsonValueKind.Object)
            return SignUpResult.Failed("server_error", NotAnswering, status: (int)status);
        if ((int)status is >= 200 and < 300 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return SignUpResult.Done(Text(root, "name") ?? name, (int)status);
        var message = Text(root, "message") ?? NotAnswering;
        if (message.Length > LongestMessage)
            message = message[..LongestMessage];
        return SignUpResult.Failed(Text(root, "error") ?? "server_error", message,
                                   Number(root, "retry_after"), Number(root, "tries_left"), (int)status);
    }

    static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static int? Number(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) ? number : null;
}
