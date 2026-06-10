namespace Pictomancy;

public enum UIMask
{
    /// <summary>Choose best possible masking solution.</summary>
    Default,
    /// <summary>No UI masking. Pictomancy renders over the game's UI.</summary>
    None,
    /// <summary>
    /// Mask pixels where the game's backbuffer alpha indicates UI. Pictomancy renders "behind" the UI.
    /// BackbufferSubtractedAlpha masking is used instead if 3D resolution scaling is detected.
    /// Automatically disabled if using AutoDraw.NativeOverlay.
    /// There are some locations where this mask does not work well, such as O8.
    /// </summary>
    BackbufferAlpha,
    /// <summary>
    /// Mask specifically designed for scaled resolutions that don't support BackbufferAlpha.
    /// Pictomancy subtracts the "Pre-UI" backbuffer from a "Post-UI" backbuffer to create an approximate UI mask.
    /// This does not work as well for semi-transparent UI elements as BackbufferAlpha.
    /// Automatically disabled if using AutoDraw.NativeOverlay.
    /// </summary>
    BackbufferSubtraction,
}
