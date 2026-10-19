namespace CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Cuts a window to a rounded rectangle. The dock bar's acrylic blur fills
/// the whole window rectangle, and DWM's own corner rounding stops at 8 px,
/// while the dock's radius is a setting. The region cuts the blur Avalonia
/// composes (host backdrop); it does not cut the DWM accent of the popups.
/// </summary>
public static class WindowShape
{
    /// <summary>
    /// Rounds the window's corners with the given diameter, in physical
    /// pixels; a diameter of 0 restores the full rectangle.
    /// </summary>
    public static void RoundCorners(IntPtr hwnd, int widthPx, int heightPx, int diameterPx)
    {
        if (hwnd == IntPtr.Zero) return;
        if (diameterPx <= 0)
        {
            User32.SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }
        // The region excludes its right and bottom edge: one more pixel keeps
        // the last row and column of the window.
        IntPtr region = User32.CreateRoundRectRgn(0, 0, widthPx + 1, heightPx + 1, diameterPx, diameterPx);
        if (region == IntPtr.Zero) return;
        // The system owns the region once set; it is freed only when refused.
        if (User32.SetWindowRgn(hwnd, region, true) == 0)
            Gdi32.DeleteObject(region);
    }
}
