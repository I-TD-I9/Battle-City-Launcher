using System.Text;
using System.Text.Json;
using BattleCity.Pictures;

namespace BattleCity.Tests;

public class ArtManifestTests
{
    const string Hash = "0b97740c27c2d17d158ce7601270c2298809d2a55149baf15a0025ca751da97e";

    static (ArtManifest? Manifest, string? Problem) Read(string json) => ArtManifest.Read(Encoding.UTF8.GetBytes(json));

    static string Manifest(params object[] files) =>
        JsonSerializer.Serialize(new { format = 1, client = "Ver.060110.00", built = "2026-10-18T12:00:00Z", files });

    static object Entry(string path, string sha256 = Hash, long size = 1234) => new { path, sha256, size };

    [Fact]
    public void A_manifest_lists_its_files()
    {
        var (manifest, problem) = Read(Manifest(Entry("data/card/list_card.txt", size: 117332),
                                                Entry("data/mini/list_card.txt", size: 117332),
                                                Entry("data/card/bc1441.jpg"), Entry("data/mini/bc1441.jpg")));
        Assert.Null(problem);
        Assert.Equal(["data/card/list_card.txt", "data/mini/list_card.txt", "data/card/bc1441.jpg", "data/mini/bc1441.jpg"],
                     manifest!.Files.Select(file => file.Path));
        Assert.Equal(new ArtFile("data/card/list_card.txt", Hash, 117332), manifest.Files[0]);
        Assert.Equal(Hash + ".txt", manifest.Files[0].StoredName);
        Assert.Equal(Hash + ".jpg", manifest.Files[2].StoredName);
    }

    [Fact]
    public void The_simplest_form_will_do_and_an_empty_one_lists_nothing()
    {
        Assert.Single(Read("""{"format": 1, "files": [{"path": "data/card/a.jpg", "sha256": "%", "size": 0}]}""".Replace("%", Hash)).Manifest!.Files);
        Assert.Empty(Read("""{"format": 1, "files": []}""").Manifest!.Files);
    }

    // A page from something in between, or a list cut short, among them.
    [Theory]
    [InlineData("")]
    [InlineData("<html>Not found</html>")]
    [InlineData("""{"format": 1, "client": "Ver.060110.00", "files": [{"path": "data/card/list_card.txt", "sha256": "00""")]
    [InlineData("""{"ok": false, "error": "not_found"}""")]
    [InlineData("""[{"format": 1, "files": []}]""")]
    [InlineData("""{"format": 2, "files": []}""")]
    [InlineData("""{"format": "1", "files": []}""")]
    [InlineData("""{"format": 1.5, "files": []}""")]
    [InlineData("""{"format": 1}""")]
    [InlineData("""{"format": 1, "files": {}}""")]
    public void What_is_no_manifest_is_refused(string json)
    {
        var (manifest, problem) = Read(json);
        Assert.Null(manifest);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("data/card/BC1441.jpg")]
    [InlineData("data/card/bc 1441.jpg")]
    [InlineData("data/card/bc1441.bmp")]
    [InlineData("data/card/bc1441.dll")]
    [InlineData("data/card/bc1441")]
    [InlineData("data/cards/bc1441.jpg")]
    [InlineData("data/card/sub/bc1441.jpg")]
    [InlineData("data/card/../../bc1441.jpg")]
    [InlineData("data/card/..jpg")]
    [InlineData("/data/card/bc1441.jpg")]
    [InlineData("data\\card\\bc1441.jpg")]
    [InlineData("C:/data/card/bc1441.jpg")]
    [InlineData("data/y/duel/card/card.txt")]
    [InlineData("data/card/bc1441.jpg\n")]
    [InlineData("")]
    // Windows opens these as devices, whatever the folder and the extension.
    [InlineData("data/card/con.txt")]
    [InlineData("data/card/prn.jpg")]
    [InlineData("data/mini/aux.txt")]
    [InlineData("data/mini/nul.jpg")]
    [InlineData("data/card/com0.jpg")]
    [InlineData("data/card/com1.jpg")]
    [InlineData("data/mini/com9.txt")]
    [InlineData("data/card/lpt1.txt")]
    [InlineData("data/mini/lpt9.jpg")]
    public void A_file_anywhere_but_the_two_picture_folders_breaks_the_manifest(string path)
    {
        var (manifest, problem) = Read(Manifest(Entry("data/card/list_card.txt"), Entry(path)));
        Assert.Null(manifest);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("0B97740C27C2D17D158CE7601270C2298809D2A55149BAF15A0025CA751DA97E")]
    [InlineData("0b97740c27c2d17d158ce7601270c2298809d2a55149baf15a0025ca751da97")]
    [InlineData("0b97740c27c2d17d158ce7601270c2298809d2a55149baf15a0025ca751da97e0")]
    [InlineData("zb97740c27c2d17d158ce7601270c2298809d2a55149baf15a0025ca751da97e")]
    [InlineData("../../../../../../../../../../../../../../../../../../../../../ab")]
    public void A_hash_that_is_not_64_lowercase_hex_digits_breaks_the_manifest(string sha256) =>
        Assert.NotNull(Read(Manifest(Entry("data/card/bc1441.jpg", sha256))).Problem);

    [Theory]
    [InlineData("-1")]
    [InlineData("2097153")]
    [InlineData("1.5")]
    [InlineData("\"1234\"")]
    [InlineData("null")]
    public void A_size_that_is_no_size_or_over_2_MB_breaks_the_manifest(string size)
    {
        string json = $$"""{"format": 1, "files": [{"path": "data/card/a.jpg", "sha256": "{{Hash}}", "size": {{size}}}]}""";
        Assert.NotNull(Read(json).Problem);
    }

    [Fact]
    public void A_file_of_exactly_2_MB_is_fine() =>
        Assert.Null(Read(Manifest(Entry("data/card/a.jpg", size: 2 * 1024 * 1024))).Problem);

    [Fact]
    public void A_path_listed_twice_breaks_the_manifest()
    {
        Assert.NotNull(Read(Manifest(Entry("data/card/a.jpg"), Entry("data/card/a.jpg"))).Problem);
        // Even with another hash.
        Assert.NotNull(Read(Manifest(Entry("data/card/a.jpg"), Entry("data/card/a.jpg", new string('1', 64)))).Problem);
    }

    [Theory]
    [InlineData("data/card/console.jpg")]
    [InlineData("data/card/com10.jpg")]
    [InlineData("data/mini/nul_1.txt")]
    [InlineData("data/card/auxx.jpg")]
    [InlineData("data/card/xcon.jpg")]
    public void A_name_that_only_begins_like_a_device_is_fine(string path) =>
        Assert.Null(Read(Manifest(Entry(path))).Problem);

    [Fact]
    public void Two_paths_may_hold_the_same_file() =>
        Assert.Equal(2, Read(Manifest(Entry("data/card/list_card.txt"), Entry("data/mini/list_card.txt"))).Manifest!.Files.Count);

    [Fact]
    public void More_than_6000_files_or_300_MB_break_the_manifest()
    {
        var many = Enumerable.Range(0, 6000).Select(i => Entry($"data/card/bc{i}.jpg", size: 1)).ToArray();
        Assert.Null(Read(Manifest(many)).Problem);
        Assert.NotNull(Read(Manifest([.. many, Entry("data/mini/bc0.jpg", size: 1)])).Problem);

        var heavy = Enumerable.Range(0, 150).Select(i => Entry($"data/card/bc{i}.jpg", size: 2 * 1024 * 1024)).ToArray();
        Assert.Null(Read(Manifest(heavy)).Problem);
        Assert.NotNull(Read(Manifest([.. heavy, Entry("data/mini/bc0.jpg", size: 1)])).Problem);
    }

    [Theory]
    [InlineData("\"Ver.051107.00\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    public void Pictures_for_another_client_break_the_manifest(string client)
    {
        string json = $$"""{"format": 1, "client": {{client}}, "files": []}""";
        Assert.Null(Read(json).Manifest);
        Assert.NotNull(Read(json).Problem);
    }

    [Theory]
    [InlineData("""[1, 2]""")]
    [InlineData("""[{"path": "data/card/a.jpg", "sha256": "%"}]""")]
    [InlineData("""[{"path": "data/card/a.jpg", "size": 1}]""")]
    [InlineData("""[{"sha256": "%", "size": 1}]""")]
    [InlineData("""[{"path": 7, "sha256": "%", "size": 1}]""")]
    public void An_entry_that_is_no_file_breaks_the_manifest(string files) =>
        Assert.NotNull(Read($$"""{"format": 1, "files": {{files.Replace("%", Hash)}}}""").Problem);
}
