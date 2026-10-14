using System.Text.Json;
using BattleCity.Accounts;

namespace BattleCity.Tests;

public class ServerInfoTests
{
    static ServerInfo? Info(string launcher)
    {
        using var json = JsonDocument.Parse($$"""
            {"ok": true, "registration": "open",
             "name": {"min": 3, "max": 16, "pattern": "^[A-Za-z0-9][A-Za-z0-9_-]*$"},
             "password": {"min": 8, "max": 16, "pattern": "^[!-~]+$"}{{launcher}}}
            """);
        return ServerInfo.FromInfo(json.RootElement);
    }

    [Fact]
    public void The_oldest_launcher_comes_with_the_rules()
    {
        var info = Info(""", "launcher": {"min": "0.2.0"}""")!;
        Assert.Equal(SignUpRules.Default, info.Rules);
        Assert.Equal(new Version(0, 2, 0), info.OldestLauncher);
    }

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.1.9", true)]
    [InlineData("0.2.0", false)]
    [InlineData("0.10.0", false)]
    [InlineData("1.0.0", false)]
    public void A_launcher_older_than_the_oldest_is_too_old(string version, bool tooOld) =>
        Assert.Equal(tooOld, Info(""", "launcher": {"min": "0.2.0"}""")!.TooOld(Version.Parse(version)));

    [Theory]
    [InlineData("")]
    [InlineData(""", "launcher": {"min": null}""")]
    [InlineData(""", "launcher": {"min": "soon"}""")]
    [InlineData(""", "launcher": "0.2.0" """)]
    public void Without_a_usable_oldest_any_launcher_will_do(string launcher)
    {
        var info = Info(launcher)!;
        Assert.Null(info.OldestLauncher);
        Assert.False(info.TooOld(new Version(0, 1, 0)));
    }

    [Fact]
    public void A_minimum_without_a_patch_number_reads_as_patch_zero() =>
        Assert.False(Info(""", "launcher": {"min": "0.2"}""")!.TooOld(new Version(0, 2, 0)));
}
