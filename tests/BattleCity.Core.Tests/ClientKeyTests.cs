using BattleCity.Game;
using Microsoft.Win32;

namespace BattleCity.Tests;

public sealed class ClientKeyTests : IDisposable
{
    // A key of each test's own, so that the real one is never touched, deleted whole afterwards.
    readonly string root = @"SOFTWARE\BattleCityTests-" + Guid.NewGuid().ToString("N");
    readonly string folder = Path.Combine(Path.GetTempPath(), "bc-saves-" + Guid.NewGuid().ToString("N"));

    string key => root + @"\system";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    string? Value(string name)
    {
        using var system = Registry.CurrentUser.OpenSubKey(key);
        return system?.GetValue(name) as string;
    }

    string Saves => Path.Combine(folder, "Saves");
    string Game => Path.Combine(folder, "Games", "Yu-Gi-Oh! ONLINE");

    [Fact]
    public void The_folder_is_made_and_the_client_pointed_at_it()
    {
        Assert.Equal(Saves, ClientKey.Prepare(Saves, Game, key));
        Assert.True(Directory.Exists(Saves));
        Assert.Equal(Saves, Value("CommonDir"));
    }

    [Fact]
    public void The_release_is_one_no_edition_name_starts_with()
    {
        ClientKey.Prepare(Saves, Game, key);
        Assert.Equal("koe", Value("Release"));
        // The client compares Release with these over Release's own length (0x414a96, 0x4a86bb).
        foreach (string edition in new[] { "matel", "v-jump", "us-jump", "kmj-release", "korea" })
            Assert.False(edition.StartsWith(ClientKey.Release, StringComparison.Ordinal), edition);
    }

    [Fact]
    public void A_folder_set_before_is_set_again()
    {
        using (var system = Registry.CurrentUser.CreateSubKey(key))
            system.SetValue("CommonDir", @"C:\Program Files (x86)\KONAMI\Yu-Gi-Oh! ONLINE");
        ClientKey.Prepare(Saves, Game, key);
        Assert.Equal(Saves, Value("CommonDir"));
    }

    [Fact]
    public void A_folder_the_client_cannot_spell_is_given_by_its_short_name()
    {
        string? given = ClientKey.Prepare(Saves, Game, key, path => path == @"C:\SAVES~1", _ => @"C:\SAVES~1");
        Assert.Equal(@"C:\SAVES~1", given);
        Assert.Equal(@"C:\SAVES~1", Value("CommonDir"));
    }

    [Fact]
    public void Without_a_short_name_the_saves_go_beside_the_game()
    {
        string beside = Path.Combine(folder, "Games", ClientKey.Fallback);
        string? given = ClientKey.Prepare(Saves, Game, key, path => path == beside, _ => null);
        Assert.Equal(beside, given);
        Assert.True(Directory.Exists(beside));
        Assert.Equal(beside, Value("CommonDir"));
    }

    [Fact]
    public void With_no_folder_the_client_can_open_it_picks_its_own()
    {
        using (var system = Registry.CurrentUser.CreateSubKey(key))
            system.SetValue("CommonDir", Saves);
        Assert.Null(ClientKey.Prepare(Saves, Game, key, _ => false, _ => null));
        Assert.Null(Value("CommonDir"));
        Assert.Equal("koe", Value("Release"));
    }

    [Fact]
    public void The_saves_live_in_the_launchers_folder() =>
        Assert.Equal(Path.Combine(LauncherSettings.Folder, "Saves"), ClientKey.SaveFolder);
}
