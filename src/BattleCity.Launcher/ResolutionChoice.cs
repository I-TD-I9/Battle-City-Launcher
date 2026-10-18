using BattleCity.Display;

namespace BattleCity.Launcher;

/// <summary>
/// One of the resolutions in the Settings page's drop-down. Two are the same when their
/// resolutions are, so the drop-down finds the chosen one in a list made afresh.
/// </summary>
public sealed record ResolutionChoice(Resolution Resolution)
{
    public string Label =>
        Resolution.IsFullScreen ? "Full screen"
        : Resolution.IsWindow ? $"{Resolution.Width} x {Resolution.Height}"
        : "Default";

    /// <summary>Whether the item carries the star of the sharpest windows.</summary>
    public bool Sharp => Resolution.IsSharp;

    // What UI Automation reads for the item, and what typing in the drop-down searches.
    public override string ToString() => Label;
}
