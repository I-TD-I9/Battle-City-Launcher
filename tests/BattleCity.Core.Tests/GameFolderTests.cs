using BattleCity.Client;
using BattleCity.Game;

namespace BattleCity.Tests;

public sealed class GameFolderTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bc-folder-" + Guid.NewGuid().ToString("N"));

    public GameFolderTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    string Client => Path.Combine(folder, SupportedClient.FileName);

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
        var found = GameFolder.Inspect(folder);
        Assert.Equal(GameStatus.Unsupported, found.Status);
        Assert.Null(found.Client);
    }

    [Fact]
    public void The_supported_client_is_ready()
    {
        File.WriteAllBytes(Client, [1]);
        var found = GameFolder.Inspect(folder, _ => true);
        Assert.Equal(GameStatus.Ready, found.Status);
        Assert.Equal(Client, found.Client);
    }

    [Fact]
    public void The_original_kept_beside_a_client_patched_in_place_is_used()
    {
        File.WriteAllBytes(Client, [1]);
        File.WriteAllBytes(Client + ".orig", [2]);
        var found = GameFolder.Inspect(folder, path => path.EndsWith(".orig", StringComparison.Ordinal));
        Assert.Equal(GameStatus.Ready, found.Status);
        Assert.Equal(Client + ".orig", found.Client);
    }

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
