using System.Text;
using BattleCity.Client;
using BattleCity.Game;

namespace BattleCity.Tests;

public sealed class GameFolderTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bc-folder-" + Guid.NewGuid().ToString("N"));

    public GameFolderTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    string Client => Path.Combine(folder, SupportedClient.FileName);

    /// <summary>The game data as the February folder holds it: patch.txt naming merge.dat.</summary>
    internal static void WriteData(string folder)
    {
        File.WriteAllText(Path.Combine(folder, GameFolder.PatchList), "merge.dat\r\n");
        WriteArchive(Path.Combine(folder, "merge.dat"));
    }

    // The tag a Konami archive begins with, and a little after it.
    static void WriteArchive(string path) => File.WriteAllBytes(path, [.. "KCEJYUGI"u8, 0xAF, 0x16, 0, 0]);

    void WriteList(string text) => File.WriteAllBytes(Path.Combine(folder, GameFolder.PatchList), Encoding.Latin1.GetBytes(text));

    [Fact]
    public void A_missing_folder_is_not_found() =>
        Assert.Equal(GameStatus.NotFound, GameFolder.Inspect(Path.Combine(folder, "nowhere")).Status);

    [Fact]
    public void A_folder_without_the_client_says_so() =>
        Assert.Equal(GameStatus.NoClient, GameFolder.Inspect(folder).Status);

    [Fact]
    public void Another_build_of_the_client_is_unsupported()
    {
        File.WriteAllBytes(Client, TestImage.Build());
        WriteData(folder);
        var found = GameFolder.Inspect(folder);
        Assert.Equal(GameStatus.Unsupported, found.Status);
        Assert.Null(found.Client);
    }

    [Fact]
    public void The_supported_client_with_its_data_is_ready()
    {
        File.WriteAllBytes(Client, [1]);
        WriteData(folder);
        var found = GameFolder.Inspect(folder, _ => true);
        Assert.Equal(GameStatus.Ready, found.Status);
        Assert.Equal(Client, found.Client);
    }

    [Fact]
    public void The_original_kept_beside_a_client_patched_in_place_is_used()
    {
        File.WriteAllBytes(Client, [1]);
        File.WriteAllBytes(Client + ".orig", [2]);
        WriteData(folder);
        var found = GameFolder.Inspect(folder, path => path.EndsWith(".orig", StringComparison.Ordinal));
        Assert.Equal(GameStatus.Ready, found.Status);
        Assert.Equal(Client + ".orig", found.Client);
    }

    [Fact]
    public void Another_build_is_unsupported_before_its_data_is_looked_at()
    {
        // As a June install is: its client is another build, and it has no patch.txt.
        File.WriteAllBytes(Client, TestImage.Build());
        Assert.Equal(GameStatus.Unsupported, GameFolder.Inspect(folder).Status);
    }

    [Fact]
    public void The_client_without_patch_txt_is_missing_its_data()
    {
        File.WriteAllBytes(Client, [1]);
        WriteArchive(Path.Combine(folder, "merge.dat"));
        var found = GameFolder.Inspect(folder, _ => true);
        Assert.Equal(GameStatus.DataMissing, found.Status);
        Assert.Null(found.Client);
    }

    [Fact]
    public void The_client_without_the_archive_patch_txt_names_is_missing_its_data()
    {
        File.WriteAllBytes(Client, [1]);
        WriteList("merge.dat\r\n");
        Assert.Equal(GameStatus.DataMissing, GameFolder.Inspect(folder, _ => true).Status);
    }

    [Fact]
    public void A_patch_txt_naming_nothing_leaves_the_data_missing()
    {
        File.WriteAllBytes(Client, [1]);
        WriteArchive(Path.Combine(folder, "merge.dat"));
        WriteList("# merge.dat\r\n\r\n");
        Assert.Equal(GameStatus.DataMissing, GameFolder.Inspect(folder, _ => true).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("KCEJ")]
    [InlineData("PK\u0003\u0004 a zip, not an archive")]
    public void A_file_that_is_no_archive_does_not_count(string content)
    {
        File.WriteAllBytes(Client, [1]);
        WriteList("merge.dat\r\n");
        File.WriteAllBytes(Path.Combine(folder, "merge.dat"), Encoding.Latin1.GetBytes(content));
        Assert.Equal(GameStatus.DataMissing, GameFolder.Inspect(folder, _ => true).Status);
    }

    // The client passes over an archive it cannot open and reads the next, so one that opens is enough.
    [Theory]
    [InlineData("p06020900.dat\r\nmerge.dat\r\n")]
    [InlineData("ï»¿# the archives\r\nmerge.dat\r\n")]
    [InlineData("merge.dat\r\n\u001A")]
    public void One_archive_that_opens_is_enough(string list)
    {
        File.WriteAllBytes(Client, [1]);
        WriteArchive(Path.Combine(folder, "merge.dat"));
        WriteList(list);
        Assert.Equal(GameStatus.Ready, GameFolder.Inspect(folder, _ => true).Status);
    }

    [Fact]
    public void The_locator_takes_the_first_ready_folder_then_one_missing_data_then_another_build()
    {
        GameFolder Folder(string path, GameStatus status) => new(path, status, status == GameStatus.Ready ? path : null);
        var statuses = new Dictionary<string, GameStatus>
        {
            ["gone"] = GameStatus.NotFound,
            ["june"] = GameStatus.Unsupported,
            ["broken"] = GameStatus.DataMissing,
            ["empty"] = GameStatus.NoClient,
            ["february"] = GameStatus.Ready,
            ["second"] = GameStatus.Ready,
        };
        GameFolder Inspect(string path) => Folder(path, statuses[path]);
        Assert.Equal("february", GameLocator.Find(["gone", "june", "broken", "empty", "february", "second"], Inspect)?.Path);
        Assert.Equal("broken", GameLocator.Find(["june", "broken", "empty"], Inspect)?.Path);
        Assert.Equal("june", GameLocator.Find(["gone", "june", "empty"], Inspect)?.Path);
        Assert.Null(GameLocator.Find(["gone", "empty"], Inspect));
        // A folder that cannot be read is passed over.
        Assert.Equal("february", GameLocator.Find(["locked", "february"],
            path => path == "locked" ? throw new UnauthorizedAccessException() : Inspect(path))?.Path);
    }

    [Theory]
    [InlineData("merge.dat\r\n", new[] { "merge" })]
    [InlineData("merge.dat", new[] { "merge" })]
    [InlineData("DATA.dat\n", new[] { "DATA" })]
    [InlineData("# old patches\r\n\r\np06020900.dat\r\nmerge.dat\r\n", new[] { "p06020900", "merge" })]
    [InlineData("merge\r\n", new[] { "merge" })]
    [InlineData("merge.DAT\r\n", new[] { "merge.DAT" })]
    [InlineData("merge.dat.bak\r\n", new[] { "merge" })]
    [InlineData(" #merge.dat\r\n", new[] { " #merge" })]
    [InlineData("", new string[0])]
    public void Patch_txt_is_read_as_the_client_reads_it(string text, string[] archives) =>
        Assert.Equal(archives, GameFolder.Archives(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void Candidates_start_with_the_chosen_folder_and_hold_no_repeats()
    {
        var candidates = GameLocator.Candidates(folder + Path.DirectorySeparatorChar).ToList();
        Assert.Equal(folder, candidates[0]);
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(candidates, c => c.EndsWith(@"KONAMI\Yu-Gi-Oh! ONLINE", StringComparison.OrdinalIgnoreCase));
    }

    [RetailClientFact]
    public void The_retail_client_is_recognised() => Assert.True(SupportedClient.IsSupported(RetailClient.Path!));
}
