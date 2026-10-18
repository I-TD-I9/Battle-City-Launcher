using BattleCity.Display;

namespace BattleCity.Tests;

public class WorkAreaTests
{
    // Windows 11's frame for a normal window, at 100% and at 150%.
    static readonly WindowFrame At100 = new(8, 31, 8, 8);
    static readonly WindowFrame At150 = new(11, 45, 11, 11);

    // Screens less a taskbar along the bottom.
    static readonly WorkArea Screen1080p = new(0, 0, 1920, 1040, At100);
    static readonly WorkArea Screen1440p = new(0, 0, 2560, 1400, At100);
    static readonly WorkArea Screen4k = new(0, 0, 3840, 2100, At100);
    static readonly WorkArea Screen1080pAt150 = new(0, 0, 1920, 1008, At150);

    static string[] Offered(WorkArea area) => [.. area.Choices().Select(choice => choice.ToString())];

    [Fact]
    public void The_default_and_full_screen_are_always_offered_around_the_windows_that_fit()
    {
        Assert.Equal(["default", "1024x768", "1152x864", "1280x960", "full"], Offered(Screen1080p));
        Assert.Equal(["default", "1024x768", "1152x864", "1280x960", "1400x1050", "1600x1200", "full"], Offered(Screen1440p));
        Assert.Equal(["default", "1024x768", "1152x864", "1280x960", "1400x1050", "1600x1200", "2048x1536", "2400x1800", "full"],
                     Offered(Screen4k));
        Assert.Equal(["default", "full"], Offered(new WorkArea(0, 0, 1024, 728, At100)));
        Assert.Equal(["default", "full"], Offered(default));
    }

    [Fact]
    public void Sizes_are_physical_pixels_with_the_frame_measured_at_the_screens_scale()
    {
        // 1080p at 150%: 952 pixels of height are left inside the frame, not enough for 960.
        Assert.Equal(["default", "1024x768", "1152x864", "full"], Offered(Screen1080pAt150));
        // 960 and the frame's 39 at 100%.
        Assert.Contains("1280x960", Offered(Screen1080p with { Bottom = 999 }));
        Assert.DoesNotContain("1280x960", Offered(Screen1080p with { Bottom = 998 }));
    }

    [Fact]
    public void Without_cnc_ddraw_only_the_default_is_offered_or_used()
    {
        Assert.Equal(["default"], [.. Screen4k.Choices(scaled: false).Select(choice => choice.ToString())]);
        Assert.Equal(Resolution.Default, Screen4k.Effective(Resolution.Parse("1600x1200"), scaled: false));
        Assert.Equal(Resolution.Default, Screen4k.Effective(Resolution.FullScreen, scaled: false));
    }

    [Fact]
    public void A_window_fits_only_with_room_for_its_frame()
    {
        var exactly = new WorkArea(0, 0, 1600 + 16, 1200 + 39, At100);
        Assert.True(exactly.Fits(Resolution.Parse("1600x1200")));
        Assert.False((exactly with { Right = exactly.Right - 1 }).Fits(Resolution.Parse("1600x1200")));
        Assert.False((exactly with { Bottom = exactly.Bottom - 1 }).Fits(Resolution.Parse("1600x1200")));
        Assert.False(exactly.Fits(Resolution.Default));
        Assert.False(exactly.Fits(Resolution.FullScreen));
    }

    [Fact]
    public void A_window_chosen_on_a_bigger_screen_falls_back_to_the_largest_that_fits()
    {
        Assert.Equal("1280x960", Screen1080p.Effective(Resolution.Parse("2400x1800")).ToString());
        Assert.Equal("1600x1200", Screen1440p.Effective(Resolution.Parse("2400x1800")).ToString());
        Assert.Equal("1152x864", Screen1080p.Effective(Resolution.Parse("1152x864")).ToString());
        Assert.Equal(Resolution.Default, new WorkArea(0, 0, 1024, 728, At100).Effective(Resolution.Parse("1600x1200")));
        Assert.Equal(Resolution.FullScreen, default(WorkArea).Effective(Resolution.FullScreen));
        Assert.Equal(Resolution.Default, Screen4k.Effective(Resolution.Default));
    }

    [Fact]
    public void A_window_is_centred_in_the_work_area_frame_and_all()
    {
        Assert.Equal(new WindowPlacement(480, 111, 1600, 1200), Screen1440p.Place(Resolution.Parse("1600x1200")));
        Assert.Equal(new WindowPlacement(320, 51, 1280, 960), Screen1080p.Place(Resolution.Parse("1280x960")));
    }

    [Theory]
    [InlineData(0, 0, 2560, 1400)]
    [InlineData(0, 0, 1920, 1040)]
    [InlineData(0, 0, 1366, 728)]
    [InlineData(-1920, 0, 0, 1040)]
    [InlineData(2560, -300, 4480, 780)]
    [InlineData(0, 48, 1920, 1080)]
    public void Every_window_offered_stays_inside_the_work_area(int left, int top, int right, int bottom)
    {
        var area = new WorkArea(left, top, right, bottom, At100);
        foreach (var window in area.Choices().Where(choice => choice.IsWindow))
        {
            var at = area.Place(window);
            Assert.True(at.X - At100.Left >= left && at.X + at.Width + At100.Right <= right, $"{window} {at}");
            Assert.True(at.Y - At100.Top >= top && at.Y + at.Height + At100.Bottom <= bottom, $"{window} {at}");
        }
    }

    [Fact]
    public void A_window_too_big_for_the_screen_keeps_its_title_bar_on_it() =>
        Assert.Equal(new WindowPlacement(8, 31, 1600, 1200), new WorkArea(0, 0, 1024, 728, At100).Place(Resolution.Parse("1600x1200")));

    [Fact]
    public void The_launchers_own_screen_is_measured()
    {
        // With no window, the primary screen, which offers at least the default and full screen.
        var area = WorkArea.Of(IntPtr.Zero);
        Assert.True(area.Width > 0 && area.Height > 0, $"{area}");
        Assert.True(area.Frame.Top > area.Frame.Bottom && area.Frame.Bottom > 0, $"{area.Frame}");
        Assert.Equal(Resolution.Default, area.Choices()[0]);
        Assert.Equal(Resolution.FullScreen, area.Choices()[^1]);
    }
}
