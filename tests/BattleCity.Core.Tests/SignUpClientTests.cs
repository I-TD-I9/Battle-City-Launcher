using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BattleCity.Accounts;

namespace BattleCity.Tests;

public class SignUpClientTests
{
    static readonly Uri Api = new("https://api.example.test/");

    sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string? ContentType, string? Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancel);
            Seen.Add((request.Method, request.RequestUri, request.Content?.Headers.ContentType?.MediaType, body));
            return answer(request);
        }
    }

    static HttpResponseMessage Reply(HttpStatusCode status, string body, string type = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };

    // A reply labelled with a charset .NET has no decoder for, as a filtering proxy's page may be.
    static HttpResponseMessage Labelled(HttpStatusCode status, string body, string contentType)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(status) { Content = content };
    }

    static (SignUpClient, FakeServer) Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var server = new FakeServer(answer);
        return (new SignUpClient(new HttpClient(server), Api), server);
    }

    [Fact]
    public async Task A_sign_up_posts_the_name_and_password_as_json()
    {
        var (client, server) = Client(_ => Reply(HttpStatusCode.Created, """{"ok": true, "name": "Duelist"}"""));
        var result = await client.RegisterAsync("Duelist", "Battle-City!");
        Assert.True(result.Ok);
        Assert.Equal("Duelist", result.Name);
        var (method, uri, type, body) = Assert.Single(server.Seen);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(new Uri("https://api.example.test/api/register"), uri);
        Assert.Equal("application/json", type);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("Duelist", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("Battle-City!", json.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public async Task A_refusal_carries_the_servers_code_and_sentence()
    {
        var (client, _) = Client(_ => Reply(HttpStatusCode.Conflict,
            """{"ok": false, "error": "name_taken", "message": "That name is taken."}"""));
        var result = await client.RegisterAsync("Duelist", "Battle-City!");
        Assert.False(result.Ok);
        Assert.Equal(("name_taken", "That name is taken.", (int?)null), (result.Error, result.Message, result.RetryAfter));
    }

    [Fact]
    public async Task A_rate_limit_says_how_long_to_wait()
    {
        var (client, _) = Client(_ => Reply(HttpStatusCode.TooManyRequests,
            """{"ok": false, "error": "rate_limited", "message": "Too many. Try again in 5 minutes.", "retry_after": 300}"""));
        var result = await client.RegisterAsync("Duelist", "Battle-City!");
        Assert.Equal(("rate_limited", (int?)300), (result.Error, result.RetryAfter));
    }

    [Fact]
    public async Task A_proxys_error_page_reads_as_the_server_not_answering()
    {
        var (client, _) = Client(_ => Reply(HttpStatusCode.BadGateway, "<html>502 Bad gateway</html>", "text/html"));
        var result = await client.RegisterAsync("Duelist", "Battle-City!");
        Assert.Equal("server_error", result.Error);
        Assert.Contains("not answering", result.Message);
    }

    [Fact]
    public async Task No_connection_reads_as_unreachable()
    {
        var (client, _) = Client(_ => throw new HttpRequestException("no route"));
        var result = await client.RegisterAsync("Duelist", "Battle-City!");
        Assert.Equal(SignUpClient.Unreachable, result.Error);
        Assert.Null(await client.GetRulesAsync());
    }

    [Fact]
    public async Task The_rules_come_from_api_info()
    {
        var (client, server) = Client(_ => Reply(HttpStatusCode.OK, """
            {"ok": true, "registration": "open",
             "name": {"min": 3, "max": 16, "pattern": "^[A-Za-z0-9][A-Za-z0-9_-]*$"},
             "password": {"min": 8, "max": 16, "pattern": "^[!-~]+$"}}
            """));
        Assert.Equal(SignUpRules.Default, await client.GetRulesAsync());
        Assert.Equal(new Uri("https://api.example.test/api/info"), server.Seen.Single().Uri);
    }

    [Theory]
    [InlineData("windows-1252")]
    [InlineData("shift_jis")]
    [InlineData("no-such-charset")]
    public async Task A_charset_dotnet_cannot_decode_is_read_as_UTF8(string charset)
    {
        var (rules, _) = Client(_ => Labelled(HttpStatusCode.OK, """
            {"ok": true, "registration": "open",
             "name": {"min": 3, "max": 16, "pattern": "^[A-Za-z0-9][A-Za-z0-9_-]*$"},
             "password": {"min": 8, "max": 16, "pattern": "^[!-~]+$"}}
            """, "application/json; charset=" + charset));
        Assert.Equal(SignUpRules.Default, await rules.GetRulesAsync());

        var (blocked, _) = Client(_ => Labelled(HttpStatusCode.Forbidden, "<html>Blocked by policy</html>",
                                                "text/html; charset=" + charset));
        var result = await blocked.RegisterAsync("Duelist", "Battle-City!");
        Assert.Equal("server_error", result.Error);
    }

    [Fact]
    public async Task An_error_status_gives_no_rules()
    {
        var (client, _) = Client(_ => Reply(HttpStatusCode.InternalServerError, """{"ok": false}"""));
        Assert.Null(await client.GetRulesAsync());
    }

    [Fact]
    public void A_very_long_message_is_cut_short()
    {
        var result = SignUpClient.Outcome(HttpStatusCode.BadRequest,
            JsonSerializer.SerializeToUtf8Bytes(new { ok = false, error = "name_invalid", message = new string('x', 5000) }), "Duelist");
        Assert.Equal(300, result.Message.Length);
    }
}
