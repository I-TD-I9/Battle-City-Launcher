using System.Text.Json;
using BattleCity.Accounts;

namespace BattleCity.Tests;

public class SignUpRulesTests
{
    static readonly SignUpRules Rules = SignUpRules.Default;

    [Fact]
    public void Without_the_servers_rules_a_name_and_a_password_are_letters_and_digits()
    {
        Assert.Equal((3, 16, "^[A-Za-z0-9]+$"), (Rules.NameMin, Rules.NameMax, Rules.NamePattern));
        Assert.Equal((8, 16, "^[A-Za-z0-9]+$"), (Rules.PasswordMin, Rules.PasswordMax, Rules.PasswordPattern));
        Assert.True(Rules.Open);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("Duelist")]
    [InlineData("9lives")]
    [InlineData("SixteenCharName1")]
    [InlineData("cpuYugi")]
    public void Good_names_pass(string name) => Assert.Null(Rules.NameProblem(name));

    // The game's login screen drops anything but letters and digits as it is typed.
    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("SeventeenCharName")]
    [InlineData("a_b-c")]
    [InlineData("Duel_ist")]
    [InlineData("Duel-ist")]
    [InlineData("_abc")]
    [InlineData("-abc")]
    [InlineData("Duelist!")]
    [InlineData("a b c")]
    [InlineData("Duelist\n")]
    [InlineData("Düelist")]
    public void Bad_names_are_refused(string name) =>
        Assert.Equal("A UserID is 3 to 16 letters and digits.", Rules.NameProblem(name));

    // Checked first, as the server does.
    [Theory]
    [InlineData("cpu-Yugi")]
    [InlineData("CPU-Kaiba")]
    [InlineData("Cpu-me")]
    [InlineData("cpu-")]
    public void The_CPU_opponents_names_are_refused(string name) =>
        Assert.Equal("UserIDs beginning with cpu- belong to the server's CPU opponents.", Rules.NameProblem(name));

    // A server still giving the old patterns lets these through; the game's login screen would not.
    [Fact]
    public void Letters_and_digits_hold_whatever_the_server_says()
    {
        var old = new SignUpRules(true, 3, 16, "^[A-Za-z0-9][A-Za-z0-9_-]*$", 8, 16, "^[!-~]+$");
        Assert.Equal("A UserID is 3 to 16 letters and digits.", old.NameProblem("duel_king"));
        Assert.Equal("A UserID is 3 to 16 letters and digits.", old.NameProblem("duel-king"));
        Assert.Equal("A password is 8 to 16 letters and digits: the game's login screen takes nothing else.",
                     old.PasswordProblem("pass-word1", "DuelKing"));
        Assert.NotNull(old.PasswordProblem("Battle-City!", null));
        Assert.Null(old.NameProblem("DuelKing"));
        Assert.Null(old.PasswordProblem("password1", "DuelKing"));
    }

    [Theory]
    [InlineData("BattleCity1")]
    [InlineData("12345678")]
    [InlineData("abcdEFGH12345678")]
    public void Good_passwords_pass(string password) => Assert.Null(Rules.PasswordProblem(password, "Duelist"));

    [Theory]
    [InlineData("short")]
    [InlineData("Battle-City!")]
    [InlineData("pass_word1")]
    [InlineData("~!@#$%^&*()_+{}")]
    [InlineData("has a space")]
    [InlineData("SeventeenChars123")]
    [InlineData("pässword")]
    [InlineData("password\n")]
    public void Bad_passwords_are_refused(string password) =>
        Assert.Equal("A password is 8 to 16 letters and digits: the game's login screen takes nothing else.",
                     Rules.PasswordProblem(password, "Duelist"));

    [Fact]
    public void The_password_cannot_be_the_name_in_any_case() =>
        Assert.Equal("The password cannot be the UserID.", Rules.PasswordProblem("DUELIST1", "duelist1"));

    [Fact]
    public void Without_a_name_only_the_passwords_own_rules_apply()
    {
        Assert.Null(Rules.PasswordProblem("duelist1", null));
        Assert.NotNull(Rules.PasswordProblem("short", null));
    }

    [Theory]
    [InlineData("duelist@example.com")]
    [InlineData("Yugi.Muto+battle@mail.example.co.jp")]
    [InlineData("o'brien@example.ie")]
    public void Good_addresses_pass(string email) => Assert.Null(SignUpRules.EmailProblem(email));

    [Theory]
    [InlineData("")]
    [InlineData("duelist")]
    [InlineData("duelist@")]
    [InlineData("duelist@example")]
    [InlineData("@example.com")]
    [InlineData("+tag@example.com")]
    [InlineData("due list@example.com")]
    [InlineData("düelist@example.com")]
    [InlineData("duelist@example.com\n")]
    public void Bad_addresses_are_refused(string email) =>
        Assert.Equal("That doesn't look like an email address.", SignUpRules.EmailProblem(email));

    [Fact]
    public void An_address_past_254_characters_is_refused() =>
        Assert.NotNull(SignUpRules.EmailProblem(new string('a', 64) + "@" + string.Join(".", Enumerable.Repeat(new string('b', 60), 4)) + ".com"));

    [Theory]
    [InlineData("042917", "042917")]
    [InlineData(" 042 917 ", "042917")]
    [InlineData("04291", null)]
    [InlineData("0429170", null)]
    [InlineData("04291a", null)]
    [InlineData("０４２９１７", null)]
    public void A_code_is_six_digits_with_spaces_dropped(string typed, string? code) =>
        Assert.Equal(code, SignUpRules.Code(typed));

    [Fact]
    public void The_servers_info_reply_gives_the_rules()
    {
        using var json = JsonDocument.Parse("""
            {"ok": true, "registration": "closed",
             "name": {"min": 4, "max": 12, "pattern": "^[a-z]+$"},
             "password": {"min": 10, "max": 16, "pattern": "^[!-~]+$"}}
            """);
        var rules = SignUpRules.FromInfo(json.RootElement)!;
        Assert.False(rules.Open);
        Assert.Equal((4, 12, "^[a-z]+$", 10), (rules.NameMin, rules.NameMax, rules.NamePattern, rules.PasswordMin));
        Assert.NotNull(rules.NameProblem("Abcde"));
        Assert.Null(rules.NameProblem("abcde"));
    }

    [Theory]
    [InlineData("""{"ok": false}""")]
    [InlineData("""{"ok": true}""")]
    [InlineData("""{"ok": true, "registration": "open", "name": {"min": "3"}, "password": {}}""")]
    [InlineData("[]")]
    public void Anything_else_gives_no_rules(string reply)
    {
        using var json = JsonDocument.Parse(reply);
        Assert.Null(SignUpRules.FromInfo(json.RootElement));
    }

    [Fact]
    public void A_pattern_this_side_cannot_use_is_left_to_the_server()
    {
        var rules = Rules with { NamePattern = "([unclosed" };
        Assert.Null(rules.NameProblem("Duelist"));
    }
}
