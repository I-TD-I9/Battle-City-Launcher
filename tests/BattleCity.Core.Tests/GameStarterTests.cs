using BattleCity.Client;
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

    [RetailClientFact]
    public void Prepare_writes_the_patched_copy_once()
    {
        string retailFolder = Path.Combine(Path.GetTempPath(), "bc-game-" + Guid.NewGuid().ToString("N"));
        string copies = Path.Combine(Path.GetTempPath(), "bc-copies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(retailFolder);
        try
        {
            File.Copy(RetailClient.Path!, Path.Combine(retailFolder, SupportedClient.FileName));
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
