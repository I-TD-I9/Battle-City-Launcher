using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BattleCity.Pictures;

namespace BattleCity.Tests;

public sealed class CardPicturesTests : IDisposable
{
    static readonly Uri Api = new("https://api.example.test/");
    readonly string root = Path.Combine(Path.GetTempPath(), "bc-pictures-" + Guid.NewGuid().ToString("N"));
    readonly FakeServer server = new();
    readonly ManualClock clock = new();
    readonly List<TimeSpan> pauses = [];

    string Bc => Path.Combine(root, "bc");
    string Staging => Bc + ".partial";
    string Outside => Path.Combine(root, "outside");

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];

        /// <summary>What a request waits for before it is answered, by path.</summary>
        public Dictionary<string, Func<CancellationToken, Task>> Waits { get; } = [];

        readonly List<string> asked = [];

        public List<string> Asked
        {
            get
            {
                lock (asked)
                    return [.. asked];
            }
        }

        public void Forget()
        {
            lock (asked)
                asked.Clear();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string path = request.RequestUri!.AbsolutePath;
            lock (asked)
                asked.Add(path);
            if (Waits.TryGetValue(path, out var wait))
                await wait(cancel);
            return Routes.TryGetValue(path, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    static string Stored(string path, byte[] content) => Sha(content) + Path.GetExtension(path);

    static HttpResponseMessage Ok(byte[] content, string type = "application/octet-stream")
    {
        var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        reply.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
        return reply;
    }

    // A pack of card pictures: the two lists and a picture pair, the same list in both folders.
    static readonly Dictionary<string, byte[]> Pack = new()
    {
        ["data/card/list_card.txt"] = Encoding.ASCII.GetBytes("// slot 0\r\ncard_ura.bmp\r\n\r\nbc1441.jpg\r\n"),
        ["data/mini/list_card.txt"] = Encoding.ASCII.GetBytes("// slot 0\r\ncard_ura.bmp\r\n\r\nbc1441.jpg\r\n"),
        ["data/card/bc1441.jpg"] = [0xFF, 0xD8, 0xFF, .. Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)), 0xFF, 0xD9],
        ["data/mini/bc1441.jpg"] = [0xFF, 0xD8, 0xFF, .. Enumerable.Range(0, 300).Select(i => (byte)(i * 11)), 0xFF, 0xD9],
    };

    static readonly string CardPicture = "/art/files/" + Stored("data/card/bc1441.jpg", Pack["data/card/bc1441.jpg"]);
    static readonly string MiniPicture = "/art/files/" + Stored("data/mini/bc1441.jpg", Pack["data/mini/bc1441.jpg"]);

    // Serves pack's manifest and files; and returns the manifest.
    string Serve(Dictionary<string, byte[]> pack)
    {
        string manifest = JsonSerializer.Serialize(new
        {
            format = 1,
            client = "Ver.060110.00",
            files = pack.OrderBy(file => file.Key, StringComparer.Ordinal)
                        .Select(file => new { path = file.Key, sha256 = Sha(file.Value), size = file.Value.Length }),
        });
        server.Routes["/art/manifest.json"] = () => Ok(Encoding.UTF8.GetBytes(manifest), "application/json");
        foreach (var (path, content) in pack)
            server.Routes["/art/files/" + Stored(path, content)] = () => Ok(content);
        return manifest;
    }

    CardPictures Pictures() => new(new HttpClient(server), Api, clock);

    // Runs an update to its end, the clock moving on through each pause between tries as it
    // comes, and only through those.
    async Task<PicturesOutcome> Update(List<PicturesProgress>? steps = null)
    {
        var update = Pictures().UpdateAsync(Bc, steps is null ? null : new Steps(steps));
        while (!update.IsCompleted)
        {
            if (clock.Soonest(TimeSpan.FromSeconds(5)) is { } pause)
            {
                pauses.Add(pause);
                clock.Advance(pause);
            }
            else
            {
                await Task.WhenAny(update, Task.Delay(5));
            }
        }
        return await update;
    }

    // Waits, in real time, for what happens on other threads.
    static async Task Until(Func<bool> happened)
    {
        var waited = Stopwatch.StartNew();
        while (!happened())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), "it never happened");
            await Task.Delay(5);
        }
    }

    sealed class Steps(List<PicturesProgress> steps) : IProgress<PicturesProgress>
    {
        public void Report(PicturesProgress value)
        {
            lock (steps)
                steps.Add(value);
        }
    }

    // Every file under bc, with its bytes.
    Dictionary<string, byte[]> InBc() =>
        !Directory.Exists(Bc) ? new Dictionary<string, byte[]>()
        : Directory.GetFiles(Bc, "*", SearchOption.AllDirectories)
                   .ToDictionary(file => Path.GetRelativePath(Bc, file).Replace('\\', '/'), File.ReadAllBytes);

    static void AssertSame(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, content) in expected)
            Assert.Equal(content, actual[path]);
    }

    void Put(string path, byte[] content)
    {
        string file = Path.Combine(Bc, path.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, content);
    }

    // A junction or a hard link, as mklink makes them without an administrator.
    static void Link(string kind, string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink {kind} \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string error = mklink.StandardError.ReadToEnd();
        mklink.WaitForExit();
        Assert.True(mklink.ExitCode == 0, error);
    }

    [Fact]
    public async Task Pictures_missing_from_bc_are_fetched_and_put_in_place()
    {
        Serve(Pack);
        var steps = new List<PicturesProgress>();
        Assert.Equal(PicturesOutcome.UpToDate, await Update(steps));
        AssertSame(Pack, InBc());
        Assert.False(Directory.Exists(Staging));
        // Checked first, then fetched: the two lists are one file, fetched once.
        Assert.Equal(new PicturesProgress(0, 0), steps[0]);
        Assert.Equal(new PicturesProgress(0, 3), steps[1]);
        Assert.Equal([1, 2, 3], steps.Skip(2).Select(step => step.Done).Order());
        Assert.Equal(4, server.Asked.Count);
        Assert.Equal("/art/manifest.json", server.Asked[0]);
        Assert.Empty(pauses);
    }

    [Fact]
    public async Task Pictures_already_there_are_kept_and_not_fetched_again()
    {
        Serve(Pack);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        string picture = Path.Combine(Bc, "data", "card", "bc1441.jpg");
        var written = File.GetLastWriteTimeUtc(picture).AddHours(-1);
        File.SetLastWriteTimeUtc(picture, written);
        server.Forget();

        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        Assert.Equal(["/art/manifest.json"], server.Asked);
        Assert.Equal(written, File.GetLastWriteTimeUtc(picture));
        AssertSame(Pack, InBc());
    }

    [Fact]
    public async Task A_picture_that_differs_from_the_servers_is_fetched_again()
    {
        Serve(Pack);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        // Of the same size, and then of another.
        var changed = (byte[])Pack["data/mini/bc1441.jpg"].Clone();
        changed[10] ^= 0xFF;
        Put("data/mini/bc1441.jpg", changed);
        server.Forget();
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        Assert.Equal(["/art/manifest.json", MiniPicture], server.Asked);
        Put("data/card/bc1441.jpg", [1, 2, 3]);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
    }

    // The server keeps a file under its hash and extension, so two alike but for that are two.
    [Fact]
    public async Task Two_files_alike_but_for_their_extension_are_each_fetched()
    {
        byte[] same = Encoding.ASCII.GetBytes("the same bytes\r\n");
        var pack = new Dictionary<string, byte[]> { ["data/card/aaa.jpg"] = same, ["data/card/list_card.txt"] = same };
        Put("data/card/old.jpg", [1, 2, 3]);
        Serve(pack);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(pack, InBc());
        Assert.Contains("/art/files/" + Sha(same) + ".jpg", server.Asked);
        Assert.Contains("/art/files/" + Sha(same) + ".txt", server.Asked);
    }

    [Fact]
    public async Task Whatever_the_manifest_does_not_list_goes()
    {
        Put("data/card/bc9999.jpg", [1]);
        Put("data/card/old/x.jpg", [2]);
        Put("Voice/BGM01/b_water.wav", [3]);
        Put("readme.txt", [4]);
        Serve(Pack);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
        Assert.False(Directory.Exists(Path.Combine(Bc, "Voice")));
        Assert.False(Directory.Exists(Path.Combine(Bc, "data", "card", "old")));
    }

    // Copied first, pruned last: a copy that fails takes nothing away.
    [Fact]
    public async Task A_picture_that_cannot_be_replaced_leaves_whatever_else_bc_had()
    {
        Put("data/card/bc9999.jpg", [1]);
        Put("data/mini/bc1441.jpg", [2]);
        Serve(Pack);
        // As a program that holds the file open would.
        using (new FileStream(Path.Combine(Bc, "data", "mini", "bc1441.jpg"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(PicturesOutcome.Unwritable, await Update());
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(Bc, "data", "card", "bc9999.jpg")));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(Bc, "data", "mini", "bc1441.jpg")));

        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
    }

    [Theory]
    [InlineData("altered")]
    [InlineData("longer")]
    [InlineData("shorter")]
    public async Task A_file_that_does_not_match_its_hash_or_size_is_refused_and_bc_stays_as_it_was(string how)
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        Put("data/card/bc0001.jpg", [9, 9, 9]);
        var before = InBc();
        Serve(Pack);
        byte[] good = Pack["data/card/bc1441.jpg"];
        byte[] bad = how switch
        {
            "altered" => [.. good[..^1], 0x00],
            "longer" => [.. good, 0x00],
            _ => good[..^1],
        };
        server.Routes[CardPicture] = () => Ok(bad);
        Assert.Equal(PicturesOutcome.Refused, await Update());
        AssertSame(before, InBc());
        // Another try would bring the same file.
        Assert.Single(server.Asked, CardPicture);
    }

    [Fact]
    public async Task A_file_the_server_lists_but_does_not_have_is_refused()
    {
        Serve(Pack);
        server.Routes.Remove(MiniPicture);
        Assert.Equal(PicturesOutcome.Refused, await Update());
        Assert.Empty(InBc());
        Assert.Single(server.Asked, MiniPicture);
    }

    [Fact]
    public async Task Without_the_network_the_pictures_there_stay()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        server.Routes["/art/manifest.json"] = () => throw new HttpRequestException("no route to host");
        Assert.Equal(PicturesOutcome.Unreachable, await Update());
        AssertSame(before, InBc());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(null)]
    public async Task A_file_the_server_fails_to_give_comes_on_a_later_try(HttpStatusCode? status)
    {
        Serve(Pack);
        var answer = server.Routes[MiniPicture];
        int tries = 0;
        server.Routes[MiniPicture] = () => ++tries > 2 ? answer()
            : status is { } refusal ? new HttpResponseMessage(refusal) : throw new HttpRequestException("connection reset");
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
        Assert.Equal(3, server.Asked.Count(path => path == MiniPicture));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], pauses);
    }

    [Fact]
    public async Task A_network_failure_halfway_keeps_them_too_and_the_next_update_fetches_only_the_rest()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        Serve(Pack);
        var answer = server.Routes[MiniPicture];
        server.Routes[MiniPicture] = () => throw new HttpRequestException("connection reset");
        Assert.Equal(PicturesOutcome.Unreachable, await Update());
        AssertSame(before, InBc());
        // Asked for twice more, then given up on.
        Assert.Equal(3, server.Asked.Count(path => path == MiniPicture));

        // What came whole the first time waits beside bc, and is not fetched again.
        var kept = Directory.Exists(Staging) ? Directory.GetFiles(Staging, "*.*").Select(Path.GetFileName).ToList() : [];
        server.Routes[MiniPicture] = answer;
        server.Forget();
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
        Assert.Contains(MiniPicture, server.Asked);
        Assert.All(kept, name => Assert.DoesNotContain("/art/files/" + name, server.Asked));
        Assert.False(Directory.Exists(Staging));
    }

    // A file that never comes is given a minute and asked for again, and when the update's time
    // is up, the game starts with what bc had.
    [Fact]
    public async Task When_the_time_is_up_bc_stays_as_it_was_and_the_rest_comes_next_time()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        Serve(Pack);
        using var stuck = new SemaphoreSlim(0);
        server.Waits[CardPicture] = async cancel =>
        {
            stuck.Release();
            await Task.Delay(Timeout.Infinite, cancel);
        };
        var update = Pictures().UpdateAsync(Bc);

        Assert.True(await stuck.WaitAsync(TimeSpan.FromSeconds(10)));
        string[] others = [Stored("data/card/list_card.txt", Pack["data/card/list_card.txt"]), Stored("data/mini/bc1441.jpg", Pack["data/mini/bc1441.jpg"])];
        await Until(() => others.All(name => File.Exists(Path.Combine(Staging, name))));
        clock.Advance(TimeSpan.FromSeconds(60));
        await Until(() => clock.Soonest(TimeSpan.FromSeconds(5)) is not null);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Soonest(TimeSpan.FromSeconds(5)));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await stuck.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(update.IsCompleted);
        clock.Advance(CardPictures.Budget - TimeSpan.FromSeconds(61));

        Assert.Equal(PicturesOutcome.Unfinished, await update);
        AssertSame(before, InBc());
        Assert.All(others, name => Assert.True(File.Exists(Path.Combine(Staging, name)), name));

        server.Waits.Remove(CardPicture);
        server.Forget();
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
        Assert.Equal(["/art/manifest.json", CardPicture], server.Asked);
    }

    [Fact]
    public async Task A_server_that_never_answers_is_given_a_minute()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        Serve(Pack);
        using var stuck = new SemaphoreSlim(0);
        server.Waits["/art/manifest.json"] = async cancel =>
        {
            stuck.Release();
            await Task.Delay(Timeout.Infinite, cancel);
        };
        var update = Pictures().UpdateAsync(Bc);
        Assert.True(await stuck.WaitAsync(TimeSpan.FromSeconds(10)));
        clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50);
        Assert.False(update.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PicturesOutcome.Unreachable, await update);
        AssertSame(before, InBc());
        Assert.Equal(["/art/manifest.json"], server.Asked);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_server_in_trouble_leaves_the_pictures_there(HttpStatusCode status)
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        server.Routes["/art/manifest.json"] = () => new HttpResponseMessage(status) { Content = new StringContent("<html>busy</html>") };
        Assert.Equal(PicturesOutcome.Unreachable, await Update());
        AssertSame(before, InBc());
    }

    [Fact]
    public async Task A_server_without_pictures_empties_bc()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        Put("data/mini/bc1441.jpg", [1, 2, 3]);
        Directory.CreateDirectory(Staging);
        File.WriteAllBytes(Path.Combine(Staging, "left.jpg"), [1]);
        // GET /art/manifest.json answers 404 when the server has no pictures.
        Assert.Equal(PicturesOutcome.None, await Update());
        Assert.Empty(InBc());
        Assert.False(Directory.Exists(Staging));
    }

    // Only a 404 says the server has no pictures: a page from something in between, a list cut
    // short, or a manifest of another format, leaves the ones there.
    [Theory]
    [InlineData("<html><body>Sign in to the Wi-Fi</body></html>")]
    [InlineData("""{"format": 1, "client": "Ver.060110.00", "files": [{"path": "data/card/list_card.txt", "sha256": "00""")]
    [InlineData("""{"ok": true}""")]
    [InlineData("""{"format": 2, "files": []}""")]
    public async Task An_answer_that_is_no_manifest_leaves_the_pictures_there(string reply)
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        server.Routes["/art/manifest.json"] = () => Ok(Encoding.UTF8.GetBytes(reply), "text/html");
        Assert.Equal(PicturesOutcome.Refused, await Update());
        AssertSame(before, InBc());
    }

    [Fact]
    public async Task A_manifest_that_breaks_the_rules_leaves_the_pictures_there()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        var before = InBc();
        string manifest = JsonSerializer.Serialize(new
        {
            format = 1,
            files = new[] { new { path = "../../online_pc.exe", sha256 = Sha([1]), size = 1 } },
        });
        server.Routes["/art/manifest.json"] = () => Ok(Encoding.UTF8.GetBytes(manifest), "application/json");
        Assert.Equal(PicturesOutcome.Refused, await Update());
        AssertSame(before, InBc());
        Assert.Equal(["/art/manifest.json"], server.Asked);
    }

    // The launcher's HttpClient holds no reply over 1 MB in memory; a picture may be up to 2 MB.
    [Fact]
    public async Task A_picture_bigger_than_the_launchers_reply_buffer_comes_all_the_same()
    {
        var big = new Dictionary<string, byte[]> { ["data/card/bc2000.jpg"] = new byte[3 * 1024 * 1024 / 2] };
        Random.Shared.NextBytes(big["data/card/bc2000.jpg"]);
        Serve(big);
        var http = new HttpClient(server) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1 << 20 };
        Assert.Equal(PicturesOutcome.UpToDate, await new CardPictures(http, Api).UpdateAsync(Bc));
        AssertSame(big, InBc());
    }

    [Fact]
    public async Task A_manifest_over_2_MB_is_refused()
    {
        server.Routes["/art/manifest.json"] = () => Ok(new byte[2 * 1024 * 1024 + 1], "application/json");
        Assert.Equal(PicturesOutcome.Refused, await Update());
    }

    [Fact]
    public async Task A_server_with_no_files_listed_empties_bc_of_everything()
    {
        Put("data/card/list_card.txt", Encoding.ASCII.GetBytes("an older list\r\n"));
        Serve(new());
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        Assert.Empty(InBc());
    }

    // The launcher makes no links. One in bc goes, and what it leads to is never read, written
    // or deleted.
    [Fact]
    public async Task A_link_in_bc_goes_and_what_it_leads_to_stays_as_it_was()
    {
        Directory.CreateDirectory(Outside);
        File.WriteAllBytes(Path.Combine(Outside, "keep.jpg"), [7]);
        // Even holding the very file the manifest lists.
        File.WriteAllBytes(Path.Combine(Outside, "bc1441.jpg"), Pack["data/card/bc1441.jpg"]);
        Directory.CreateDirectory(Path.Combine(Bc, "data"));
        Link("/J", Path.Combine(Bc, "data", "card"), Outside);
        Serve(Pack);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        Assert.False(new DirectoryInfo(Path.Combine(Bc, "data", "card")).Attributes.HasFlag(FileAttributes.ReparsePoint));
        AssertSame(Pack, InBc());
        Assert.Equal(["bc1441.jpg", "keep.jpg"], Directory.GetFiles(Outside).Select(Path.GetFileName).Order());
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(Outside, "keep.jpg")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bc_itself_a_link_goes_and_what_it_leads_to_stays_as_it_was(bool pictures)
    {
        Directory.CreateDirectory(Path.Combine(Outside, "data", "card"));
        File.WriteAllBytes(Path.Combine(Outside, "data", "card", "keep.jpg"), [7]);
        Directory.CreateDirectory(root);
        Link("/J", Bc, Outside);
        if (pictures)
            Serve(Pack);
        Assert.Equal(pictures ? PicturesOutcome.UpToDate : PicturesOutcome.None, await Update());
        Assert.False(Directory.Exists(Bc) && new DirectoryInfo(Bc).Attributes.HasFlag(FileAttributes.ReparsePoint));
        AssertSame(pictures ? Pack : new Dictionary<string, byte[]>(), InBc());
        Assert.Equal([Path.Combine(Outside, "data", "card", "keep.jpg")], Directory.GetFiles(Outside, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Pruning_takes_a_link_away_without_going_into_it()
    {
        Directory.CreateDirectory(Outside);
        File.WriteAllBytes(Path.Combine(Outside, "keep.jpg"), [7]);
        Directory.CreateDirectory(Path.Combine(Bc, "data"));
        Link("/J", Path.Combine(Bc, "data", "card"), Outside);
        CardPictures.Prune(new DirectoryInfo(Bc), []);
        Assert.Empty(Directory.GetFileSystemEntries(Bc));
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(Outside, "keep.jpg")));
    }

    // A hard link is not a link to Windows' eyes, but a second name for a file elsewhere: the
    // server's file takes the name's place and leaves the other file alone.
    [Fact]
    public async Task A_hard_link_in_bc_is_replaced_and_never_written_through()
    {
        string document = Path.Combine(Outside, "document.txt");
        Directory.CreateDirectory(Outside);
        File.WriteAllText(document, "the player's own document");
        Directory.CreateDirectory(Path.Combine(Bc, "data", "card"));
        Link("/H", Path.Combine(Bc, "data", "card", "list_card.txt"), document);
        Serve(Pack);
        Assert.Equal(PicturesOutcome.UpToDate, await Update());
        AssertSame(Pack, InBc());
        Assert.Equal("the player's own document", File.ReadAllText(document));
    }
}
