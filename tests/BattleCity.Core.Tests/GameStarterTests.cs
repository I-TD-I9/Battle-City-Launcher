using BattleCity.Client;
using BattleCity.Display;
using BattleCity.Game;

namespace BattleCity.Tests;

public class GameStarterTests
{
    [Fact]
    public void Prepare_refuses_a_folder_that_is_not_ready()
    {
        string empty = Path.Combine(Path.GetTempPath(), "bc-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                GameStarter.Prepare(GameFolder.Inspect(empty), ServerEndpoints.Public, empty));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    const string Client = @"C:\Launcher\online_pc.exe";
    static readonly GameFolder Game = new(@"D:\Games\Yu-Gi-Oh! ONLINE", GameStatus.Ready, null);

    [Fact]
    public void The_game_runs_in_a_window_from_its_own_folder()
    {
        var info = GameStarter.StartInfo(Client, Game, Resolution.Default, @"C:\Launcher");
        Assert.Equal((Client, @"D:\Games\Yu-Gi-Oh! ONLINE", "-win"), (info.FileName, info.WorkingDirectory, info.Arguments));
        Assert.False(info.UseShellExecute);
    }

    // The client takes -32 over -win wherever each stands, so the two never go together.
    [Theory]
    [InlineData("default", "-win")]
    [InlineData("1024x768", "-32")]
    [InlineData("1600x1200", "-32")]
    [InlineData("full", "-32")]
    public void The_resolution_decides_the_one_switch_the_client_gets(string resolution, string arguments) =>
        Assert.Equal(arguments, GameStarter.StartInfo(Client, Game, Resolution.Parse(resolution), @"C:\Launcher").Arguments);

    [Fact]
    public void The_client_starts_by_its_folders_form_the_code_page_can_spell()
    {
        const string Long = @"C:\Users\Дмитрий\AppData\Local\BattleCity";
        var info = GameStarter.StartInfo(Long + @"\online_pc.exe", Game, Resolution.FullScreen, @"C:\Users\8D0B~1\AppData\Local\BATTLE~1");
        Assert.Equal(@"C:\Users\8D0B~1\AppData\Local\BATTLE~1\online_pc.exe", info.FileName);
        Assert.Equal(@"C:\Users\8D0B~1\AppData\Local\BATTLE~1\ddraw.ini", info.Environment[CncDdraw.ConfigFileVariable]);
    }

    [Theory]
    [InlineData("1600x1200")]
    [InlineData("full")]
    public void Cnc_ddraw_is_told_where_its_settings_are(string resolution) =>
        Assert.Equal(@"C:\Launcher\ddraw.ini",
                     GameStarter.StartInfo(Client, Game, Resolution.Parse(resolution), @"C:\Launcher").Environment[CncDdraw.ConfigFileVariable]);

    [Fact]
    public void A_cnc_ddraw_setting_of_the_players_own_never_reaches_the_game()
    {
        string? before = Environment.GetEnvironmentVariable(CncDdraw.ConfigFileVariable);
        try
        {
            Environment.SetEnvironmentVariable(CncDdraw.ConfigFileVariable, @"D:\Other game\ddraw.ini");
            Assert.False(GameStarter.StartInfo(Client, Game, Resolution.Default, @"C:\Launcher").Environment
                                    .ContainsKey(CncDdraw.ConfigFileVariable));
            Assert.Equal(@"C:\Launcher\ddraw.ini", GameStarter.StartInfo(Client, Game, Resolution.FullScreen, @"C:\Launcher")
                                                               .Environment[CncDdraw.ConfigFileVariable]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CncDdraw.ConfigFileVariable, before);
        }
    }

    // cnc-ddraw could never find its settings there, and -32 would switch the display itself.
    [Fact]
    public void Without_a_folder_the_code_page_can_spell_the_game_keeps_its_own_window()
    {
        var info = GameStarter.StartInfo(Client, Game, Resolution.FullScreen, null);
        Assert.Equal((Client, "-win"), (info.FileName, info.Arguments));
        Assert.False(info.Environment.ContainsKey(CncDdraw.ConfigFileVariable));
    }

    [RetailClientFact]
    public void Prepare_writes_the_patched_copy_once()
    {
        string retailFolder = Path.Combine(Path.GetTempPath(), "bc-game-" + Guid.NewGuid().ToString("N"));
        string copies = Path.Combine(Path.GetTempPath(), "bc-copies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(retailFolder);
        try
        {
            File.Copy(RetailClient.Path!, Path.Combine(retailFolder, SupportedClient.FileName));
            GameFolderTests.WriteData(retailFolder);
            var game = GameFolder.Inspect(retailFolder);
            Assert.Equal(GameStatus.Ready, game.Status);

            string client = GameStarter.Prepare(game, ServerEndpoints.Public, copies);
            Assert.Equal(Path.Combine(copies, GameStarter.ClientName), client);
            Assert.Equal(SupportedClient.PublicSha256, SupportedClient.HashOf(client));

            // Left alone when it is already right, rewritten when it is not.
            var written = File.GetLastWriteTimeUtc(client);
            File.SetLastWriteTimeUtc(client, written.AddHours(-1));
            GameStarter.Prepare(game, ServerEndpoints.Public, copies);
            Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(client));
            File.WriteAllBytes(client, [1, 2, 3]);
            GameStarter.Prepare(game, ServerEndpoints.Public, copies);
            Assert.Equal(SupportedClient.PublicSha256, SupportedClient.HashOf(client));
            Assert.False(File.Exists(client + ".new"));

            // A copy that cannot be replaced, as while it runs, is reported and leaves nothing behind.
            File.WriteAllBytes(client, [1, 2, 3]);
            using (new FileStream(client, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var refused = Record.Exception(() => GameStarter.Prepare(game, ServerEndpoints.Public, copies));
                Assert.True(refused is IOException or UnauthorizedAccessException, refused?.ToString());
            }
            Assert.False(File.Exists(client + ".new"));
        }
        finally
        {
            Directory.Delete(retailFolder, recursive: true);
            if (Directory.Exists(copies))
                Directory.Delete(copies, recursive: true);
        }
    }
}
