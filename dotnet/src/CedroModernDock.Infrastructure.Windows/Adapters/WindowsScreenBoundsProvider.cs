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
        return WithDockStrip(new ScreenBounds(area.X, area.Y, area.Width, area.Height));
    }

    public IReadOnlyList<ScreenInfo> GetScreens()
    {
        var screens = new List<ScreenInfo>();
        foreach (var monitor in DisplayMonitors.List())
        {
            screens.Add(new ScreenInfo(
                monitor.Id,
                monitor.Name,
                monitor.IsPrimary,
                monitor.IsInternal,
                ToBounds(monitor.Bounds),
                WithDockStrip(ToBounds(monitor.WorkArea))));
        }
        return screens;
    }

    private static ScreenBounds ToBounds(RECT rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    /// <summary>
    /// The dock's own appbar shrinks the work area of its monitor; give that
    /// strip back so a bottom-anchored dock sits inside it rather than above it.
    /// </summary>
    private static ScreenBounds WithDockStrip(ScreenBounds area)
    {
        if (DockAppBar.Reserved is { } reserved
            && reserved.Top == (int)area.MaxY
            && reserved.Left <= (int)area.MinX && reserved.Right >= (int)area.MaxX)
        {
            return area with { Height = area.Height + (reserved.Bottom - reserved.Top) };
        }
        return area;
    }
}
