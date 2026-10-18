using BattleCity.Client;
using BattleCity.Game;

namespace BattleCity.Display;

/// <summary>
/// cnc-ddraw (https://github.com/FunkyFr3sh/cnc-ddraw, MIT; THIRD-PARTY-NOTICES.md), a DirectDraw
/// of its own that draws the game's 800x600 picture scaled, in a bigger window or over the whole
/// screen. The build embeds the ddraw.dll of its release 7.1.0.0 (CncDdraw.targets), and the
/// launcher puts it beside its copy of the client, which loads it before it makes DirectDraw
/// (ClientPatcher.LoadWrapperFirst).
/// </summary>
public static class CncDdraw
{
    public const string Version = "7.1.0.0";
    public const string DllName = "ddraw.dll";
    public const string IniName = "ddraw.ini";
    public const long Length = 411_648;
    public const string Sha256 = "85e0f7d530dfda134793a57cb3e76b0287dcc96892ee57162dd68f47283b03a9";

    /// <summary>cnc-ddraw's licence, which goes wherever the DLL does.</summary>
    public const string LicenseName = "ddraw-LICENSE.txt";

    /// <summary>
    /// Where cnc-ddraw takes its settings from before anything else (cfg_init, src/config.c): it
    /// would otherwise look beside its DLL by the path GetModuleFileNameA gives.
    /// </summary>
    public const string ConfigFileVariable = "CNC_DDRAW_CONFIG_FILE";

    const string Resource = "BattleCity.Display.ddraw.dll";
    const string LicenseResource = "BattleCity.Display.cnc-ddraw-LICENSE.txt";

    /// <summary>The folder in the launcher's that screenshots go to.</summary>
    public const string Screenshots = "Screenshots";

    /// <summary>The DLL, as the build embedded it.</summary>
    public static byte[] Dll() => Embedded(Resource);

    /// <summary>cnc-ddraw's MIT licence as it stands at its tag v7.1.0.0.</summary>
    public static byte[] License() => Embedded(LicenseResource);

    static byte[] Embedded(string name)
    {
        using var stream = typeof(CncDdraw).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"this build of the launcher carries no {name}");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// The launcher's folder in the form the client and cnc-ddraw can be given it: itself when the
    /// system's code page can spell it, else its 8.3 short name. Null when it has neither: cnc-ddraw
    /// then reads its settings from a path full of question marks, finds none and makes a stock
    /// file in the working folder, which would switch the display itself to 800x600, so the game
    /// is shown in its own window.
    /// </summary>
    public static string? Folder(string folder) => AnsiPath.Folder(folder, AnsiPath.Fits, AnsiPath.ShortName);

    /// <summary>
    /// Sets up <paramref name="resolution"/> for the client in <paramref name="folder"/>. For a
    /// bigger window or full screen the DLL goes beside it, written only when it has changed,
    /// since the game holds it open while it runs, and checked against the release's hash; then
    /// its licence and the settings, a window going at <paramref name="window"/>. For the default
    /// all three are taken away, and the client loads the system's ddraw.dll, as it always did.
    /// </summary>
    /// <remarks>
    /// The game is started with -32 only once this has succeeded: without the wrapper -32 would
    /// switch the display itself to 800x600.
    /// </remarks>
    public static void Prepare(Resolution resolution, string folder, WindowPlacement? window)
    {
        string dll = Path.Combine(folder, DllName), ini = Path.Combine(folder, IniName);
        string license = Path.Combine(folder, LicenseName);
        if (resolution.IsDefault)
        {
            // Refused while the game has the DLL loaded.
            foreach (var file in new[] { dll, ini, license })
                if (File.Exists(file))
                    File.Delete(file);
            return;
        }
        Directory.CreateDirectory(folder);
        // cnc-ddraw reads its settings as ANSI text, as the client reads its paths.
        string? shots = AnsiPath.Folder(Path.Combine(folder, Screenshots), AnsiPath.Fits, AnsiPath.ShortName);
        byte[] settings = AnsiPath.CodePage().GetBytes(DdrawIni.For(resolution, window, shots));
        LauncherFiles.Replace(dll, Dll());
        if (SupportedClient.HashOf(dll) != Sha256)
            throw new IOException($"{dll} is not the ddraw.dll of cnc-ddraw {Version}");
        LauncherFiles.Replace(license, License());
        LauncherFiles.Replace(ini, settings);
    }
}
