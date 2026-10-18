using BattleCity.Display;

namespace BattleCity.Tests;

public class ResolutionTests
{
    [Fact]
    public void The_windows_on_offer_are_the_usual_4_by_3_sizes()
    {
        Assert.Equal(["1024x768", "1152x864", "1280x960", "1400x1050", "1600x1200", "2048x1536", "2400x1800", "3200x2400"],
                     Resolution.Windows.Select(window => window.ToString()));
        Assert.All(Resolution.Windows, window => Assert.Equal(window.Width * 3, window.Height * 4));
        Assert.All(Resolution.Windows, window => Assert.True(window.IsWindow && !window.IsDefault && !window.IsFullScreen));
    }

    [Fact]
    public void Only_whole_multiples_of_800_by_600_are_the_sharpest() =>
        Assert.Equal(["1600x1200", "2400x1800", "3200x2400"],
                     Resolution.Windows.Where(window => window.IsSharp).Select(window => window.ToString()));

    [Fact]
    public void The_default_is_what_a_resolution_starts_as()
    {
        Assert.Equal(Resolution.Default, default);
        Assert.True(Resolution.Default.IsDefault);
        Assert.False(Resolution.Default.IsWindow || Resolution.Default.IsFullScreen || Resolution.Default.IsSharp);
        Assert.True(Resolution.FullScreen.IsFullScreen);
        Assert.False(Resolution.FullScreen.IsDefault || Resolution.FullScreen.IsWindow || Resolution.FullScreen.IsSharp);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("full")]
    [InlineData("1152x864")]
    [InlineData("2400x1800")]
    public void Each_name_reads_back_as_itself(string name) => Assert.Equal(name, Resolution.Parse(name).ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("800x600")]
    [InlineData("1920x1080")]
    [InlineData("1600x1200x32")]
    [InlineData("x")]
    [InlineData("fullscreen")]
    [InlineData("original")]
    public void Anything_else_reads_as_the_default(string? name) =>
        Assert.Equal(Resolution.Default, Resolution.Parse(name));
}
