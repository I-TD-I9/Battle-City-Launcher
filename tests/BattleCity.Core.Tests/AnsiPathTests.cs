using BattleCity.Game;

namespace BattleCity.Tests;

public sealed class AnsiPathTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bc-ansi-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Plain_paths_fit_the_ansi_code_page() =>
        Assert.True(AnsiPath.Fits(@"C:\Users\Duelist\AppData\Local\BattleCity\Saves"));

    [Fact]
    public void A_path_no_ansi_code_page_can_spell_does_not_fit() =>
        // Letters from three scripts, which no single ANSI code page holds together.
        Assert.False(AnsiPath.Fits(@"C:\Users\Иван 田中 Ελένη\Saves"));

    [Fact]
    public void The_code_page_refuses_what_it_cannot_spell() =>
        Assert.ThrowsAny<ArgumentException>(() => AnsiPath.CodePage().GetBytes("Иван 田中 Ελένη"));

    [Fact]
    public void A_folder_is_made_and_given_as_it_is_when_it_fits()
    {
        string saves = Path.Combine(folder, "Saves");
        Assert.Equal(saves, AnsiPath.Folder(saves, _ => true, _ => null));
        Assert.True(Directory.Exists(saves));
    }

    [Fact]
    public void A_folder_that_does_not_fit_is_given_by_its_short_name_or_not_at_all()
    {
        string saves = Path.Combine(folder, "Saves");
        Assert.Equal(@"C:\SAVES~1", AnsiPath.Folder(saves, path => path == @"C:\SAVES~1", _ => @"C:\SAVES~1"));
        Assert.Null(AnsiPath.Folder(saves, _ => false, _ => @"C:\SAVES~1"));
        Assert.Null(AnsiPath.Folder(saves, _ => false, _ => null));
    }

    [Fact]
    public void A_path_that_is_not_there_has_no_short_name() =>
        Assert.Null(AnsiPath.ShortName(Path.Combine(folder, "nowhere")));
}
