namespace BattleCity.Tests;

public sealed class LauncherSettingsTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bc-settings-" + Guid.NewGuid().ToString("N"));

    string File => Path.Combine(folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void No_file_gives_the_defaults()
    {
        var settings = LauncherSettings.Load(File);
        Assert.Null(settings.GameFolder);
        Assert.True(settings.Server().IsPublic);
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ not json");
        Assert.Null(LauncherSettings.Load(File).GameFolder);
    }

    [Fact]
    public void A_file_edited_by_hand_may_hold_comments_and_trailing_commas()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """
            {
              // a server on this machine
              "gateHost": "127.0.0.1",
              "api": "http://127.0.0.1:8080/",
            }
            """);
        Assert.Equal("127.0.0.1", LauncherSettings.Load(File).Server().GateHost);
    }

    [Fact]
    public void What_is_saved_loads_back()
    {
        new LauncherSettings { GameFolder = @"D:\Games\Yu-Gi-Oh! ONLINE" }.Save(File);
        Assert.Equal(@"D:\Games\Yu-Gi-Oh! ONLINE", LauncherSettings.Load(File).GameFolder);
        Assert.False(System.IO.File.Exists(File + ".new"));
    }

    [Fact]
    public void Usable_overrides_point_at_another_server()
    {
        var server = new LauncherSettings { GateHost = "127.0.0.1", GatePort = 5800, Api = "http://localhost:8080/" }.Server();
        Assert.Equal(new ServerEndpoints("127.0.0.1", 5800, new Uri("http://localhost:8080/")), server);
        Assert.False(server.IsPublic);
    }

    [Theory]
    [InlineData("bad host", null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, 70000, null)]
    [InlineData(null, null, "http://example.com/")]
    [InlineData(null, null, "not a url")]
    [InlineData("a-name-of-thirty-two-characters.x", null, null)]
    public void Unusable_overrides_are_ignored(string? host, int? port, string? api) =>
        Assert.True(new LauncherSettings { GateHost = host, GatePort = port, Api = api }.Server().IsPublic);
}
