namespace Pictomancy;

public enum UIMask
{
    /// <summary>Choose best possible masking solution.</summary>
    Default,
    /// <summary>No UI masking. Pictomancy renders over the game's UI.</summary>
    None,
    /// <summary>
    /// Mask pixels where the game's backbuffer alpha indicates UI. Pictomancy renders "behind" the UI.
    /// Automatically disabled if using AutoDraw.NativeOverlay.
    /// There are some locations where this mask does not work well, such as O8.
    /// </summary>
    BackbufferAlpha,
}
