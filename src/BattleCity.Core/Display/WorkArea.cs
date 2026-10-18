using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BattleCity.Display;

/// <summary>How far a window's frame reaches past its client area on each side.</summary>
public readonly record struct WindowFrame(int Left, int Top, int Right, int Bottom)
{
    public int Width => Left + Right;
    public int Height => Top + Bottom;
}

/// <summary>Where the game's window goes: its client area, in the screen's physical pixels.</summary>
public readonly record struct WindowPlacement(int X, int Y, int Width, int Height);

/// <summary>
/// The part of a monitor a window may take, the screen less the taskbar, with the frame a window
/// gets on that monitor, in physical pixels: what decides which bigger windows are offered, and
/// where one goes.
/// </summary>
/// <remarks>
/// cnc-ddraw makes the game's process per-monitor DPI aware as soon as it is loaded
/// (SetProcessDpiAwarenessContext in its DllMain, src/dllmain.c), so the width, height and
/// position in ddraw.ini are physical pixels, which Windows never stretches, at 125% or 150% as
/// at 100%. It makes the window's client area that size and puts the frame round it with
/// AdjustWindowRectEx. Left to itself it would centre the window on the primary display's whole
/// resolution, taskbar and all (dd_SetDisplayMode, src/dd.c), so the launcher gives the position
/// too, on the monitor whose room the size was chosen for. The launcher is per-monitor aware
/// itself (app.manifest), so it measures that monitor in the same physical pixels.
/// </remarks>
public readonly record struct WorkArea(int Left, int Top, int Right, int Bottom, WindowFrame Frame)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    /// <summary>
    /// The work area of the monitor <paramref name="window"/> is on, or of the primary monitor
    /// when there is no window.
    /// </summary>
    public static WorkArea Of(IntPtr window)
    {
        IntPtr monitor = window != IntPtr.Zero
            ? MonitorFromWindow(window, MonitorDefaultToNearest)
            : MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            throw new Win32Exception("Windows did not say which screen the launcher is on");
        if (GetDpiForMonitor(monitor, EffectiveDpi, out uint dpi, out _) != 0 || dpi == 0)
            dpi = 96;
        // cnc-ddraw gives the window the standard frame, whatever style the game made it with
        // (dd_SetDisplayMode with border=true).
        var frame = new Rect();
        if (!AdjustWindowRectExForDpi(ref frame, OverlappedWindow, false, 0, dpi))
        {
            // Windows 10's frame at 100%, to the scale of this monitor.
            int side = (int)(8 * dpi / 96), top = (int)(31 * dpi / 96);
            frame = new Rect { Left = -side, Top = -top, Right = side, Bottom = side };
        }
        return new WorkArea(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom,
                            new WindowFrame(-frame.Left, -frame.Top, frame.Right, frame.Bottom));
    }

    /// <summary>Whether <paramref name="resolution"/> is a bigger window that fits here, frame and all.</summary>
    public bool Fits(Resolution resolution) =>
        resolution.IsWindow && resolution.Width + Frame.Width <= Width && resolution.Height + Frame.Height <= Height;

    /// <summary>
    /// What the Settings page offers here: the default, each bigger window that fits, and full
    /// screen; or the default alone when cnc-ddraw can't be used (<paramref name="scaled"/> false:
    /// CncDdraw.Folder).
    /// </summary>
    public IReadOnlyList<Resolution> Choices(bool scaled = true) =>
        scaled ? [Resolution.Default, .. Resolution.Windows.Where(Fits), Resolution.FullScreen] : [Resolution.Default];

    /// <summary>
    /// <paramref name="chosen"/>, unless it is a window too big for here, as one chosen on another
    /// monitor may be: then the largest that fits, or the default when none does. Always the
    /// default when cnc-ddraw can't be used.
    /// </summary>
    public Resolution Effective(Resolution chosen, bool scaled = true) =>
        !scaled ? Resolution.Default
        : !chosen.IsWindow || Fits(chosen) ? chosen
        : Resolution.Windows.Where(Fits).DefaultIfEmpty(Resolution.Default).Last();

    /// <summary>
    /// Where a window of <paramref name="window"/>'s size goes: centred, frame and all, but never
    /// with its title bar off the top or the left.
    /// </summary>
    public WindowPlacement Place(Resolution window)
    {
        int x = Left + Math.Max((Width - Frame.Width - window.Width) / 2, 0) + Frame.Left;
        int y = Top + Math.Max((Height - Frame.Height - window.Height) / 2, 0) + Frame.Top;
        return new WindowPlacement(x, y, window.Width, window.Height);
    }

    const uint MonitorDefaultToPrimary = 1, MonitorDefaultToNearest = 2;
    const int EffectiveDpi = 0;
    // WS_OVERLAPPEDWINDOW: a caption, a system menu, a sizing border and the minimize and
    // maximize boxes.
    const uint OverlappedWindow = 0x00CF0000;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, bool menu, uint exStyle, uint dpi);
}
