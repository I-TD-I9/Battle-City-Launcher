using System.Text.Json;
using BattleCity.Accounts;

namespace BattleCity.Tests;

public class SignUpRulesTests
{
    static readonly SignUpRules Rules = SignUpRules.Default;

    [Theory]
    [InlineData("abc")]
    [InlineData("Duelist")]
    [InlineData("a_b-c")]
    [InlineData("9lives")]
    [InlineData("SixteenCharName1")]
    public void Good_names_pass(string name) => Assert.Null(Rules.NameProblem(name));

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("SeventeenCharName")]
    [InlineData("_abc")]
    [InlineData("-abc")]
    [InlineData("a b c")]
    [InlineData("Duelist\n")]
    [InlineData("Düelist")]
    public void Bad_names_are_refused(string name) =>
        Assert.Equal("A UserID is 3 to 16 characters: letters, digits, _ and -, beginning with a letter or digit.",
                     Rules.NameProblem(name));

    [Theory]
    [InlineData("cpu-Yugi")]
    [InlineData("CPU-Kaiba")]
    [InlineData("Cpu-me")]
    public void The_CPU_opponents_names_are_refused(string name) =>
        Assert.Equal("UserIDs beginning with cpu- belong to the server's CPU opponents.", Rules.NameProblem(name));

    [Theory]
    [InlineData("Battle-City!")]
    [InlineData("12345678")]
    [InlineData("~!@#$%^&*()_+{}")]
    public void Good_passwords_pass(string password) => Assert.Null(Rules.PasswordProblem(password, "Duelist"));

    [Theory]
    [InlineData("short")]
    [InlineData("has a space")]
    [InlineData("SeventeenChars!!!")]
    [InlineData("pässword")]
    [InlineData("password\n")]
    public void Bad_passwords_are_refused(string password) =>
        Assert.Equal("A password is 8 to 16 characters: letters, digits and symbols, without spaces.",
                     Rules.PasswordProblem(password, "Duelist"));

    [Fact]
    public void The_password_cannot_be_the_name_in_any_case() =>
        Assert.Equal("The password cannot be the UserID.", Rules.PasswordProblem("DUELIST1", "duelist1"));

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
