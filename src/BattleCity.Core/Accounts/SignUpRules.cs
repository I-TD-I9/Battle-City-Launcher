using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleCity.Accounts;

/// <summary>
/// The rules a new account's UserID and password follow, as the server's GET /api/info gives
/// them, and letters and digits only whatever those say. The launcher checks a sign-up against
/// them before sending it; the server checks it again either way, and its wording and order are
/// mirrored here so that both say the same thing.
/// </summary>
public sealed record SignUpRules(bool Open, int NameMin, int NameMax, string NamePattern,
                                 int PasswordMin, int PasswordMax, string PasswordPattern)
{
    /// <summary>The server's rules when this was written, for when they cannot be fetched.</summary>
    public static SignUpRules Default { get; } =
        new(true, 3, 16, "^[A-Za-z0-9]+$", 8, 16, "^[A-Za-z0-9]+$");

    // The server's CPU opponents adopt accounts with this prefix, so players may not take them.
    const string CpuPrefix = "cpu-";

    public string? NameProblem(string name)
    {
        if (name.StartsWith(CpuPrefix, StringComparison.OrdinalIgnoreCase))
            return $"UserIDs beginning with {CpuPrefix} belong to the server's CPU opponents.";
        if (name.Length < NameMin || name.Length > NameMax || !WholeMatch(NamePattern, name) || !Typeable(name))
            return $"A UserID is {NameMin} to {NameMax} letters and digits.";
        return null;
    }

    /// <summary>
    /// Why <paramref name="password"/> will not do, or null. With no <paramref name="name"/>, as
    /// for a new password set by email before the account is known, the rule against the UserID
    /// is left to the server.
    /// </summary>
    public string? PasswordProblem(string password, string? name)
    {
        if (password.Length < PasswordMin || password.Length > PasswordMax || !WholeMatch(PasswordPattern, password)
            || !Typeable(password))
            return $"A password is {PasswordMin} to {PasswordMax} letters and digits: the game's login screen takes nothing else.";
        if (name is not null && string.Equals(password, name, StringComparison.OrdinalIgnoreCase))
            return "The password cannot be the UserID.";
        return null;
    }

    /// <summary>The longest email address the server takes.</summary>
    public const int EmailMax = 254;

    // The server's own test of an address: plain ASCII, as nearly every mail provider hands out.
    const string EmailPattern = @"^[A-Za-z0-9._%+'-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$";

    public static string? EmailProblem(string email) =>
        email.Length <= EmailMax && WholeMatch(EmailPattern, email) && !email.StartsWith('+')
            ? null
            : "That doesn't look like an email address.";

    /// <summary>The digits in a code the server mailed.</summary>
    public const int CodeLength = 6;

    /// <summary>
    /// The code in what the player typed, spaces dropped, as a mail client may copy it with them;
    /// null when that is not six digits.
    /// </summary>
    public static string? Code(string typed)
    {
        string code = string.Concat(typed.Where(c => !char.IsWhiteSpace(c)));
        return code.Length == CodeLength && code.All(char.IsAsciiDigit) ? code : null;
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

    // Whether the game's login screen can take text: it keeps a typed character only when isalnum
    // passes it (0x6419e1, in sub_6416d0, the input routine of its two fields) and drops every
    // other, so an account with one could never log in, whatever a server lets through.
    static bool Typeable(string text) => text.All(char.IsAsciiLetterOrDigit);

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
