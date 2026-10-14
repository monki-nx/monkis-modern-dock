namespace CedroModernDock.Core.Application;

/// <summary>
/// Port providing screen geometry, abstracting the platform-specific screen
/// enumeration (JavaFX Screen / Avalonia Screen / Win32 monitor enumeration).
/// Coordinates are in device pixels, matching the original JavaFX visual bounds.
/// </summary>
public interface IScreenBoundsProvider
{
    /// <summary>The visual bounds of the primary screen (work area, excluding taskbar).</summary>
    ScreenBounds GetPrimaryScreenBounds();

    /// <summary>
    /// The work area of the screen containing (or nearest to) the point. The
    /// strip the dock itself reserves is not subtracted, so anchoring the dock
    /// to the bottom edge never pushes it above its own reservation.
    /// </summary>
    ScreenBounds GetScreenBoundsAt(double x, double y);
}

/// <summary>Immutable rectangle describing screen bounds in device pixels.</summary>
public readonly record struct ScreenBounds(double MinX, double MinY, double Width, double Height)
{
    public double MaxX => MinX + Width;
    public double MaxY => MinY + Height;
}
