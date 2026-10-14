using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleCity.Accounts;

/// <summary>
/// The rules a new account's UserID and password follow, as the server's GET /api/info gives
/// them. The launcher checks a sign-up against them before sending it; the server checks it
/// again either way, and its wording is mirrored here so that both say the same thing.
/// </summary>
public sealed record SignUpRules(bool Open, int NameMin, int NameMax, string NamePattern,
                                 int PasswordMin, int PasswordMax, string PasswordPattern)
{
    /// <summary>The server's rules when this was written, for when they cannot be fetched.</summary>
    public static SignUpRules Default { get; } =
        new(true, 3, 16, "^[A-Za-z0-9][A-Za-z0-9_-]*$", 8, 16, "^[!-~]+$");

    // The server's CPU opponents adopt accounts with this prefix, so players may not take them.
    const string CpuPrefix = "cpu-";

    public string? NameProblem(string name)
    {
        if (name.Length < NameMin || name.Length > NameMax || !WholeMatch(NamePattern, name))
            return $"A UserID is {NameMin} to {NameMax} characters: letters, digits, _ and -, beginning with a letter or digit.";
        if (name.StartsWith(CpuPrefix, StringComparison.OrdinalIgnoreCase))
            return $"UserIDs beginning with {CpuPrefix} belong to the server's CPU opponents.";
        return null;
    }

    public string? PasswordProblem(string password, string name)
    {
        if (password.Length < PasswordMin || password.Length > PasswordMax || !WholeMatch(PasswordPattern, password))
            return $"A password is {PasswordMin} to {PasswordMax} characters: letters, digits and symbols, without spaces.";
        if (string.Equals(password, name, StringComparison.OrdinalIgnoreCase))
            return "The password cannot be the UserID.";
        return null;
    }

    /// <summary>The rules in a GET /api/info reply, or null when it is not one.</summary>
    internal static SignUpRules? FromInfo(JsonElement root)
    {
        try
        {
            if (root.ValueKind != JsonValueKind.Object || !root.GetProperty("ok").GetBoolean())
                return null;
            var name = root.GetProperty("name");
            var password = root.GetProperty("password");
            return new SignUpRules(root.GetProperty("registration").GetString() == "open",
                                   name.GetProperty("min").GetInt32(), name.GetProperty("max").GetInt32(),
                                   name.GetProperty("pattern").GetString() ?? Default.NamePattern,
                                   password.GetProperty("min").GetInt32(), password.GetProperty("max").GetInt32(),
                                   password.GetProperty("pattern").GetString() ?? Default.PasswordPattern);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // The match must cover the whole text: .NET's $ also matches before a final newline, which
    // the server's re.fullmatch never lets through.
    static bool WholeMatch(string pattern, string text)
    {
        try
        {
            var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            return match.Success && match.Index == 0 && match.Length == text.Length;
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
        {
            // A pattern this side cannot use: the server's own check still applies.
            return true;
        }
    }
}
