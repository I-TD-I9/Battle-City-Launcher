using System.Globalization;

namespace BattleCity.Display;

/// <summary>
/// The ddraw.ini cnc-ddraw reads from beside its DLL (cfg_load, src/config.c), for a bigger
/// window or full screen. The launcher writes it afresh before every Play, so whatever is changed
/// in it lasts only until then.
/// </summary>
public static class DdrawIni
{
    // The same for every resolution. The game is scaled smoothly, bicubic on Direct3D 9 and with
    // the Bicubic shader, one built into cnc-ddraw, should it fall back to OpenGL; it keeps its 4:3
    // shape at any size rather than only at whole multiples, and the mouse follows the scaling.
    // Frames go out at the screen's own rate without vsync's lag. cnc-ddraw keeps nothing of its
    // own between games, the window may be resized, and full screen never takes the display over
    // for itself, so Alt+Tab is instant. The rest are cnc-ddraw's own defaults, written out so the
    // file says all it does.
    static readonly string[] Common =
    [
        "renderer=auto",
        "d3d9_filter=2",
        "shader=Bicubic",
        "maintas=true",
        "boxing=false",
        "adjmouse=true",
        "maxfps=-1",
        "vsync=false",
        "devmode=false",
        "savesettings=0",
        "resizable=true",
        "anti_aliased_fonts_min_size=13",
        "center_window=1",
        "nonexclusive=true",
        "singlecpu=true",
        "noactivateapp=false",
        "maxgameticks=0",
        "minfps=0",
        "resolutions=0",
    ];

    /// <summary>
    /// The file for <paramref name="resolution"/>: for a bigger window a bordered one of its size
    /// at <paramref name="window"/>, for full screen the whole primary screen without a border.
    /// Screenshots, taken with Print Screen, go to <paramref name="screenshots"/> when the game
    /// can be given that folder.
    /// </summary>
    public static string For(Resolution resolution, WindowPlacement? window, string? screenshots)
    {
        var lines = new List<string>
        {
            "; Battle City Launcher writes this file every time it starts the game, so changes made here don't last.",
            "[ddraw]",
        };
        if (resolution.IsFullScreen)
        {
            // At the screen's own resolution: a width and height of 0 leave it to cnc-ddraw.
            lines.AddRange(["fullscreen=true", "windowed=true", "border=false", "width=0", "height=0"]);
        }
        else if (resolution.IsWindow && window is { } at && (at.Width, at.Height) == (resolution.Width, resolution.Height))
        {
            lines.AddRange(["fullscreen=false", "windowed=true", "border=true",
                            Line("width", at.Width), Line("height", at.Height), Line("posX", at.X), Line("posY", at.Y)]);
        }
        else
        {
            throw new ArgumentException($"cnc-ddraw has no settings for the {resolution} resolution" +
                                        (resolution.IsWindow ? " without a place for its window" : ""));
        }
        lines.AddRange(Common);
        if (screenshots is not null)
            lines.Add($"screenshotdir={Path.TrimEndingDirectorySeparator(screenshots)}\\");
        return string.Join("\r\n", lines) + "\r\n";
    }

    // In plain digits whatever the player's language, which may write a minus sign of its own.
    static string Line(string key, int value) => key + "=" + value.ToString(CultureInfo.InvariantCulture);
}
