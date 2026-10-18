using System.Text;
using BattleCity.Game;

namespace BattleCity.Tests;

public sealed class WorkingFolderTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "bc-working-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    const string February = @"C:\Program Files (x86)\KONAMI\Yu-Gi-Oh! ONLINE Ver.060110.00";
    const string FebruaryShort = @"C:\PROGRA~2\KONAMI\YU-GI-~1.00";

    // What a Western code page spells, near enough for these paths.
    static bool Ascii(string path) => path.All(c => c < 0x80);

    static Func<string, string?> Short(params (string Long, string Short)[] names) =>
        path => names.FirstOrDefault(name => string.Equals(name.Long, path, StringComparison.OrdinalIgnoreCase)).Short;

    static List<string>? Lines(string game, string patchList, Func<string, bool>? fits = null, Func<string, string?>? shortName = null) =>
        WorkingFolder.Lines(game, Encoding.Latin1.GetBytes(patchList), fits ?? Ascii, shortName ?? Short());

    [Fact]
    public void The_February_folder_in_its_usual_place_is_named_by_its_short_name()
    {
        var lines = Lines(February, "merge.dat\r\n", shortName: Short((February, FebruaryShort)))!;
        Assert.Equal(["bc", FebruaryShort + @"\merge.dat", FebruaryShort], lines);
        // A patch.txt the game was seen to play from, byte for byte.
        Assert.Equal("bc\r\nC:\\PROGRA~2\\KONAMI\\YU-GI-~1.00\\merge.dat\r\nC:\\PROGRA~2\\KONAMI\\YU-GI-~1.00\r\n",
                     string.Concat(lines.Select(line => line + "\r\n")));
    }

    [Fact]
    public void A_plain_folder_is_named_as_it_is()
    {
        Assert.Equal(["bc", @"D:\Games\YGO\merge.dat", @"D:\Games\YGO"], Lines(@"D:\Games\YGO", "merge.dat\r\n"));
        Assert.Equal(["bc", @"D:\Games\YGO\merge.dat", @"D:\Games\YGO"], Lines(@"D:\Games\YGO\", "merge.dat\r\n"));
    }

    [Fact]
    public void A_folder_with_spaces_needs_a_short_name()
    {
        Assert.Null(Lines(February, "merge.dat\r\n"));
        Assert.Null(Lines(@"D:\My Games\YGO", "merge.dat\r\n"));
    }

    [Fact]
    public void A_folder_the_code_page_cannot_spell_needs_a_short_name()
    {
        const string Russian = @"C:\Игры\Yu-Gi-Oh";
        Assert.Equal(["bc", @"C:\8D0B~1\Yu-Gi-Oh\merge.dat", @"C:\8D0B~1\Yu-Gi-Oh"],
                     Lines(Russian, "merge.dat\r\n", shortName: Short((Russian, @"C:\8D0B~1\Yu-Gi-Oh"))));
        Assert.Null(Lines(Russian, "merge.dat\r\n"));
    }

    // The client cuts every line at its first ".dat", so the folder must not have one, though a
    // short name's capitals pass.
    [Fact]
    public void A_folder_with_dat_in_its_name_cannot_be_named_but_by_a_short_name()
    {
        const string Data = @"D:\my.data\YGO";
        Assert.Null(Lines(Data, "merge.dat\r\n"));
        Assert.Equal(["bc", @"D:\MYDATA~1.DAT\YGO\merge.dat", @"D:\MYDATA~1.DAT\YGO"],
                     Lines(Data, "merge.dat\r\n", shortName: Short((Data, @"D:\MYDATA~1.DAT\YGO"))));
    }

    // fgets into 256 bytes: 253 characters, a line break and the zero after it.
    [Fact]
    public void A_line_holds_at_most_253_characters()
    {
        string fits = @"D:\" + new string('g', 253 - @"D:\".Length - @"\merge.dat".Length);
        Assert.Equal(253, Lines(fits, "merge.dat\r\n")![1].Length);
        string tooLong = fits + "g";
        Assert.Null(Lines(tooLong, "merge.dat\r\n"));
        Assert.Equal(["bc", @"D:\GGGGGG~1\merge.dat", @"D:\GGGGGG~1"],
                     Lines(tooLong, "merge.dat\r\n", shortName: Short((tooLong, @"D:\GGGGGG~1"))));
    }

    [Fact]
    public void Patch_txts_lines_keep_their_order_as_the_client_reads_them()
    {
        Assert.Equal(["bc", @"D:\YGO\p06020900.dat", @"D:\YGO\merge.dat", @"D:\YGO"],
                     Lines(@"D:\YGO", "# the archives\r\n\r\np06020900.dat\r\nmerge.dat\r\n"));
        // Lines ended by LF alone, and none at the end.
        Assert.Equal(["bc", @"D:\YGO\p06020900.dat", @"D:\YGO\merge.dat", @"D:\YGO"],
                     Lines(@"D:\YGO", "p06020900.dat\nmerge.dat"));
    }

    // Whatever follows the first .dat the client never sees, and a line without one is the same
    // place with .dat after it.
    [Theory]
    [InlineData("merge.dat.bak\r\n")]
    [InlineData("merge\r\n")]
    [InlineData("merge.dat junk\r\n")]
    public void A_line_ends_at_its_first_dat(string patchList) =>
        Assert.Equal(["bc", @"D:\YGO\merge.dat", @"D:\YGO"], Lines(@"D:\YGO", patchList));

    [Fact]
    public void An_archive_elsewhere_keeps_its_own_folder()
    {
        Assert.Equal(["bc", @"E:\Archives\merge.dat", @"D:\YGO"], Lines(@"D:\YGO", @"E:\Archives\merge.dat"));
        Assert.Equal(["bc", @"D:\YGO\old\merge.dat", @"D:\YGO"], Lines(@"D:\YGO", @"old\merge.dat"));
        Assert.Null(Lines(@"D:\YGO", @"E:\Old Archives\merge.dat"));
        Assert.Null(Lines(@"D:\YGO", "my merge.dat"));
    }

    [Fact]
    public void Prepare_writes_patch_txt_in_crlf_lines_and_gives_the_folder_to_start_in()
    {
        string game = Path.Combine(folder, "YGO");
        string working = Path.Combine(folder, "Launcher", "Game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "patch.txt"), "merge.dat\r\n");
        Assert.Equal((working, (NoPictures?)null), WorkingFolder.Prepare(game, working, Ascii, Short()));
        Assert.Equal($"bc\r\n{game}\\merge.dat\r\n{game}\r\n", File.ReadAllText(Path.Combine(working, "patch.txt")));
        Assert.Empty(Directory.GetFiles(working, "*.new"));
    }

    [Fact]
    public void Without_a_form_for_the_game_nothing_is_written_and_the_client_starts_in_the_games_folder()
    {
        string game = Path.Combine(folder, "My Games", "YGO");
        string working = Path.Combine(folder, "Game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "patch.txt"), "merge.dat\r\n");
        Assert.Equal((game, NoPictures.GameFolder), WorkingFolder.Prepare(game, working, Ascii, Short()));
        Assert.False(File.Exists(Path.Combine(working, "patch.txt")));
    }

    // The client keeps the folder it starts in as the code page spells it, so a short name for
    // the game's folder is still better than nothing there.
    [Fact]
    public void The_games_folder_to_fall_back_on_is_given_in_a_form_the_client_can_read_when_it_has_one()
    {
        const string Russian = @"C:\Игры\Yu-Gi-Oh";
        Assert.Equal(@"C:\8D0B~1\Yu-Gi-Oh", WorkingFolder.Fallback(Russian, Ascii, Short((Russian, @"C:\8D0B~1\Yu-Gi-Oh"))));
        Assert.Equal(Russian, WorkingFolder.Fallback(Russian, Ascii, Short()));
        Assert.Equal(@"D:\My Games\YGO", WorkingFolder.Fallback(@"D:\My Games\YGO", Ascii, Short()));
    }

    [Fact]
    public void A_working_folder_the_client_cannot_be_started_in_is_given_by_its_short_name_or_not_at_all()
    {
        string game = Path.Combine(folder, "YGO");
        string working = Path.Combine(folder, "Game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "patch.txt"), "merge.dat\r\n");
        bool fits(string path) => Ascii(path) && path != working;
        Assert.Equal((@"C:\GAME~1", (NoPictures?)null),
                     WorkingFolder.Prepare(game, working, path => fits(path) || path == @"C:\GAME~1", Short((working, @"C:\GAME~1"))));
        File.Delete(Path.Combine(working, "patch.txt"));
        // Then the launcher's folder is to blame, whatever the game's is like.
        Assert.Equal((game, NoPictures.LauncherFolder), WorkingFolder.Prepare(game, working, fits, Short()));
        Assert.Equal((game, NoPictures.LauncherFolder), WorkingFolder.Prepare(game, working, path => fits(path) && path != game, Short()));
        Assert.False(File.Exists(Path.Combine(working, "patch.txt")));
    }

    [RetailClientFact]
    public void The_retail_games_folder_can_be_named_in_patch_txt()
    {
        string game = Path.GetDirectoryName(RetailClient.Path!)!;
        var lines = WorkingFolder.Lines(game, File.ReadAllBytes(Path.Combine(game, "patch.txt")), AnsiPath.Fits, AnsiPath.ShortName);
        Assert.NotNull(lines);
        Assert.Equal("bc", lines[0]);
        Assert.EndsWith(@"\merge.dat", lines[1]);
        Assert.All(lines, line => Assert.True(line.Length <= 253 && !line.Contains(' ') && AnsiPath.Fits(line), line));
        Assert.True(Directory.Exists(lines[^1]));
        Assert.True(File.Exists(lines[1]));
    }

    [Fact]
    public void A_game_folder_that_lost_its_patch_txt_is_reported()
    {
        string game = Path.Combine(folder, "YGO");
        Directory.CreateDirectory(game);
        Assert.ThrowsAny<IOException>(() => WorkingFolder.Prepare(game, Path.Combine(folder, "Game"), Ascii, Short()));
    }
}
