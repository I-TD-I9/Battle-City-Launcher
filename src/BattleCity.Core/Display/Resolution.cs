using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleCity.Display;

/// <summary>
/// How the game is shown, as chosen on the Settings page: by default in its own 800 x 600 window,
/// as it always was, or through cnc-ddraw in a bigger window or over the whole screen.
/// </summary>
/// <remarks>
/// settings.json keeps it as "default", "full", or a window's size such as "1600x1200".
/// Anything else found there, a size from a newer launcher or a hand edit gone wrong, reads as
/// "default", and so does no setting at all.
/// </remarks>
[JsonConverter(typeof(ResolutionConverter))]
public readonly record struct Resolution
{
    /// <summary>The size the game draws everything at.</summary>
    public const int GameWidth = 800, GameHeight = 600;

    Resolution(int width, int height, bool fullScreen)
    {
        Width = width;
        Height = height;
        IsFullScreen = fullScreen;
    }

    /// <summary>The game's own window, without cnc-ddraw, as a new launcher starts.</summary>
    public static Resolution Default => default;

    /// <summary>The whole screen, without a border, the game kept 4:3.</summary>
    public static Resolution FullScreen { get; } = new(0, 0, fullScreen: true);

    /// <summary>
    /// The bigger windows on offer: the usual 4:3 sizes, of which 1600 x 1200, 2400 x 1800 and
    /// 3200 x 2400 are whole multiples of the game's 800 x 600.
    /// </summary>
    public static IReadOnlyList<Resolution> Windows { get; } =
    [
        new(1024, 768, false), new(1152, 864, false), new(1280, 960, false), new(1400, 1050, false),
        new(1600, 1200, false), new(2048, 1536, false), new(2400, 1800, false), new(3200, 2400, false),
    ];

    /// <summary>For a bigger window, the size of the picture in it; 0 otherwise.</summary>
    public int Width { get; }

    public int Height { get; }

    public bool IsFullScreen { get; }

    public bool IsDefault => !IsFullScreen && Width == 0;

    public bool IsWindow => Width > 0;

    /// <summary>
    /// Whether every pixel of the game is drawn the same size, as in a window a whole number of
    /// times as big as 800 x 600: the sharpest a window gets.
    /// </summary>
    public bool IsSharp => IsWindow && Width % GameWidth == 0 && Height == Width / GameWidth * GameHeight;

    /// <summary>The form settings.json keeps it in.</summary>
    public override string ToString() =>
        IsFullScreen ? "full" : IsWindow ? string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}") : "default";

    /// <summary>The resolution <paramref name="text"/> names in that form; for anything else, the default.</summary>
    public static Resolution Parse(string? text)
    {
        string name = text?.Trim() ?? "";
        if (name.Equals("full", StringComparison.OrdinalIgnoreCase))
            return FullScreen;
        foreach (var window in Windows)
            if (name.Equals(window.ToString(), StringComparison.OrdinalIgnoreCase))
                return window;
        return Default;
    }
}

/// <summary>Reads and writes a resolution in settings.json as its name, anything else reading as the default.</summary>
sealed class ResolutionConverter : JsonConverter<Resolution>
{
    public override bool HandleNull => true;

    public override Resolution Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return Resolution.Parse(reader.GetString());
        reader.Skip();
        return Resolution.Default;
    }

    public override void Write(Utf8JsonWriter writer, Resolution value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
