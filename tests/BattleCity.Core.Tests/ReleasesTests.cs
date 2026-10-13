using System.Text.Json;

namespace BattleCity.Tests;

public class ReleasesTests
{
    static LauncherRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Releases.Parse(document.RootElement);
    }

    [Fact]
    public void A_release_gives_its_version_and_page()
    {
        var release = Parse("""
            {"tag_name": "v1.2.3",
             "html_url": "https://github.com/I-TD-I9/Battle-City-Launcher/releases/tag/v1.2.3"}
            """);
        Assert.Equal(new Version(1, 2, 3), release!.Version);
        Assert.Equal("https://github.com/I-TD-I9/Battle-City-Launcher/releases/tag/v1.2.3", release.Page.AbsoluteUri);
    }

    [Fact]
    public void A_two_part_tag_reads_as_patch_zero() =>
        Assert.Equal(new Version(2, 0, 0),
                     Parse("""{"tag_name": "2.0", "html_url": "https://github.com/I-TD-I9/Battle-City-Launcher/releases/tag/2.0"}""")!.Version);

    [Theory]
    [InlineData("""{"tag_name": "v1.0.0", "html_url": "https://example.com/I-TD-I9/Battle-City-Launcher/releases"}""")]
    [InlineData("""{"tag_name": "v1.0.0", "html_url": "https://github.com/someone-else/Battle-City-Launcher/releases"}""")]
    [InlineData("""{"tag_name": "latest", "html_url": "https://github.com/I-TD-I9/Battle-City-Launcher/releases"}""")]
    [InlineData("""{"message": "Not Found"}""")]
    public void Anything_else_is_no_release(string json) => Assert.Null(Parse(json));

    [Fact]
    public void This_build_has_a_three_part_version() => Assert.Equal(-1, Releases.Current.Revision);
}
