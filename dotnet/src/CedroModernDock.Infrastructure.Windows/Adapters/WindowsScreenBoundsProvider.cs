namespace CedroModernDock.Infrastructure.Windows.Adapters;

using CedroModernDock.Core.Application;
using CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Windows-specific implementation of IScreenBoundsProvider.
/// Uses the screens' working areas (bounds excluding taskbar) to replicate
/// JavaFX Screen.getVisualBounds().
/// </summary>
public class WindowsScreenBoundsProvider : IScreenBoundsProvider
{
    public ScreenBounds GetPrimaryScreenBounds()
    {
        // System.Windows.Forms.Screen.PrimaryScreen.WorkingArea gives the
        // taskbar-excluded bounds, equivalent to JavaFX visual bounds.
        var workingArea = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        return new ScreenBounds(
            workingArea.X, workingArea.Y,
            workingArea.Width, workingArea.Height);
    }

    public ScreenBounds GetScreenBoundsAt(double x, double y)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)x, (int)y));
        var area = screen.WorkingArea;

        // The dock's own appbar shrinks this work area; give that strip back
        // so a bottom-anchored dock sits inside it rather than above it.
        if (DockAppBar.Reserved is { } reserved
            && reserved.Top == area.Bottom
            && reserved.Left <= area.Left && reserved.Right >= area.Right)
        {
            area.Height += reserved.Bottom - reserved.Top;
        }

        return new ScreenBounds(area.X, area.Y, area.Width, area.Height);
    }
}
