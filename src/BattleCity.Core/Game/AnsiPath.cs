using System.Runtime.InteropServices;
using System.Text;

namespace BattleCity.Game;

/// <summary>
/// Paths for the game's client and the DLL it loads, which open their files through Windows' ANSI
/// functions: a path they are given must be one the system's code page can spell. A folder that
/// cannot be may still have an 8.3 short name that can, when the volume keeps them.
/// </summary>
static class AnsiPath
{
    static AnsiPath() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The system's ANSI code page, refusing what it cannot spell.</summary>
    public static Encoding CodePage() =>
        Encoding.GetEncoding((int)GetACP(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>
    /// Whether <paramref name="path"/> comes through the system's ANSI code page unchanged. In a
    /// double-byte code page a character's second byte can be 0x5c, which the client's folder
    /// maker splits paths on (0x648e02) as though it were a backslash, so such a byte must stand
    /// for one.
    /// </summary>
    public static bool Fits(string path)
    {
        try
        {
            var ansi = CodePage();
            byte[] bytes = ansi.GetBytes(path);
            return ansi.GetString(bytes) == path && bytes.Count(b => b == '\\') == path.Count(c => c == '\\');
        }
        catch (Exception e) when (e is EncoderFallbackException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The 8.3 short form of <paramref name="path"/>, which must exist, or null when it has none.</summary>
    public static string? ShortName(string path)
    {
        var buffer = new char[260];
        uint length = GetShortPathName(path, buffer, (uint)buffer.Length);
        return length is > 0 and < 260 ? new string(buffer, 0, (int)length) : null;
    }

    /// <summary>
    /// <paramref name="folder"/>, made if it is not there yet, in the form the client can be given
    /// it (Form).
    /// </summary>
    public static string? Folder(string folder, Func<string, bool> fits, Func<string, string?> shortName)
    {
        Directory.CreateDirectory(folder);
        return Form(folder, fits, shortName);
    }

    /// <summary>
    /// <paramref name="path"/> in the form the client can be given it: itself when it fits the
    /// code page, else its short name when that does, else null.
    /// </summary>
    public static string? Form(string path, Func<string, bool> fits, Func<string, string?> shortName) =>
        fits(path) ? path : shortName(path) is { } name && fits(name) ? name : null;

    [DllImport("kernel32.dll")]
    static extern uint GetACP();

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, uint length);
}
