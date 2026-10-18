using BattleCity.Display;

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
        new LauncherSettings { GameFolder = @"D:\Games\Yu-Gi-Oh! ONLINE", LastAccount = "Duelist" }.Save(File);
        var settings = LauncherSettings.Load(File);
        Assert.Equal(@"D:\Games\Yu-Gi-Oh! ONLINE", settings.GameFolder);
        Assert.Equal("Duelist", settings.LastAccount);
        Assert.False(System.IO.File.Exists(File + ".new"));
    }

    [Fact]
    public void The_resolution_is_the_default_until_another_is_chosen()
    {
        Assert.True(LauncherSettings.Load(File).Resolution.IsDefault);
        Assert.True(new LauncherSettings().Resolution.IsDefault);
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "gameFolder": "D:\\Games\\Yu-Gi-Oh! ONLINE" }""");
        Assert.True(LauncherSettings.Load(File).Resolution.IsDefault);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("full")]
    [InlineData("1024x768")]
    [InlineData("1600x1200")]
    [InlineData("3200x2400")]
    public void The_resolution_is_saved_by_its_name(string name)
    {
        new LauncherSettings { Resolution = Resolution.Parse(name) }.Save(File);
        Assert.Contains($"\"resolution\": \"{name}\"", System.IO.File.ReadAllText(File));
        Assert.Equal(name, LauncherSettings.Load(File).Resolution.ToString());
    }

    [Theory]
    [InlineData("\"FULL\"", "full")]
    [InlineData("\"original\"", "default")]
    [InlineData("\" 1600X1200 \"", "1600x1200")]
    [InlineData("\"1700x1275\"", "default")]
    [InlineData("\"800x600\"", "default")]
    [InlineData("\"1600 x 1200\"", "default")]
    [InlineData("\"FullScreen\"", "default")]
    [InlineData("\"\"", "default")]
    [InlineData("1600", "default")]
    [InlineData("null", "default")]
    [InlineData("true", "default")]
    [InlineData("""{ "width": 1600, "height": 1200 }""", "default")]
    [InlineData("""["full"]""", "default")]
    public void A_resolution_the_launcher_does_not_know_reads_as_the_default_and_keeps_the_rest(string value, string resolution)
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, $$"""{ "resolution": {{value}}, "lastAccount": "Duelist" }""");
        var settings = LauncherSettings.Load(File);
        Assert.Equal(resolution, settings.Resolution.ToString());
        Assert.Equal("Duelist", settings.LastAccount);
    }

    [Fact]
    public void A_file_without_a_last_account_still_loads()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "gameFolder": "D:\\Games\\Yu-Gi-Oh! ONLINE" }""");
        var settings = LauncherSettings.Load(File);
        Assert.Equal(@"D:\Games\Yu-Gi-Oh! ONLINE", settings.GameFolder);
        Assert.Null(settings.LastAccount);
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
