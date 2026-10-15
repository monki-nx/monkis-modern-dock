namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Models;

/// <summary>
/// Direct port of DockPositioningService, with the JavaFX Screen dependency
/// abstracted behind IScreenBoundsProvider so the Core stays platform-agnostic.
/// </summary>
public class DockPositioningService
{
    private readonly DockService _dockService;
    private readonly IScreenBoundsProvider? _screenBoundsProvider;

    public DockPositioningService(DockService dockService, IScreenBoundsProvider? screenBoundsProvider = null)
    {
        _dockService = dockService;
        _screenBoundsProvider = screenBoundsProvider;
    }

    public DockPositioningMode GetPositioningMode() => _dockService.GetDock().PositioningMode;

    public void SetPositioningMode(DockPositioningMode positioningMode)
    {
        _dockService.GetDock().PositioningMode = positioningMode;
        _dockService.SaveChanges();
    }

    public DockVerticalAnchor GetVerticalAnchor() => _dockService.GetDock().VerticalAnchor;

    public void SetVerticalAnchor(DockVerticalAnchor verticalAnchor)
    {
        _dockService.GetDock().VerticalAnchor = verticalAnchor;
        _dockService.SaveChanges();
    }

    public DockHorizontalAnchor GetHorizontalAnchor() => _dockService.GetDock().HorizontalAnchor;

    public void SetHorizontalAnchor(DockHorizontalAnchor horizontalAnchor)
    {
        _dockService.GetDock().HorizontalAnchor = horizontalAnchor;
        _dockService.SaveChanges();
    }

    public int GetScreenEdgeSpacing() => _dockService.GetDock().TopSpacing;

    public void SetScreenEdgeSpacing(int spacing)
    {
        SetTopSpacing(spacing);
        SetLeftSpacing(spacing);
        SetRightSpacing(spacing);
        SetBottomSpacing(spacing);
    }

    public int GetTopSpacing() => _dockService.GetDock().TopSpacing;

    public void SetTopSpacing(int spacing)
    {
        _dockService.GetDock().TopSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public int GetLeftSpacing() => _dockService.GetDock().LeftSpacing;

    public void SetLeftSpacing(int spacing)
    {
        _dockService.GetDock().LeftSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public int GetRightSpacing() => _dockService.GetDock().RightSpacing;

    public void SetRightSpacing(int spacing)
    {
        _dockService.GetDock().RightSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public int GetBottomSpacing() => _dockService.GetDock().BottomSpacing;

    public void SetBottomSpacing(int spacing)
    {
        _dockService.GetDock().BottomSpacing = Math.Max(0, spacing);
        _dockService.SaveChanges();
    }

    public bool IsDynamicPositioning() => GetPositioningMode() == DockPositioningMode.DYNAMIC;

    public bool GetReserveScreenSpace() => _dockService.GetDock().ReserveScreenSpace;

    public void SetReserveScreenSpace(bool value)
    {
        _dockService.GetDock().ReserveScreenSpace = value;
        _dockService.SaveChanges();
    }

    /// <summary>
    /// Computes the dock's screen position based on the current positioning mode.
    /// Returns (x, y) in device pixels.
    /// </summary>
    public (double X, double Y) ResolvePosition(double windowWidth, double windowHeight)
    {
        DockModel dock = _dockService.GetDock();

        if (dock.PositioningMode == DockPositioningMode.DYNAMIC)
            return (SnapToPixel(dock.DockPositionX), SnapToPixel(dock.DockPositionY));

        if (_screenBoundsProvider == null)
            return (0, 0);

        // Anchor to the screen the dock was last placed on (its saved center),
        // not always the primary one; a point on no screen falls back to the
        // nearest screen.
        var bounds = _screenBoundsProvider.GetScreenBoundsAt(
            dock.DockPositionX + windowWidth / 2, dock.DockPositionY + windowHeight / 2);
        double x = SnapToPixel(ResolveHorizontalPosition(bounds, windowWidth, dock));
        double y = SnapToPixel(ResolveVerticalPosition(bounds, windowHeight, dock));
        return (x, y);
    }

    public static double SnapToPixel(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static double ResolveHorizontalPosition(
        ScreenBounds bounds, double windowWidth, DockModel dock)
    {
        return dock.HorizontalAnchor switch
        {
            DockHorizontalAnchor.LEFT => bounds.MinX + dock.LeftSpacing,
            DockHorizontalAnchor.MIDDLE => bounds.MinX + ((bounds.Width - windowWidth) / 2),
            DockHorizontalAnchor.RIGHT => bounds.MaxX - windowWidth - dock.RightSpacing,
            _ => bounds.MinX
        };
    }

    private static double ResolveVerticalPosition(
        ScreenBounds bounds, double windowHeight, DockModel dock)
    {
        return dock.VerticalAnchor switch
        {
            DockVerticalAnchor.TOP => bounds.MinY + dock.TopSpacing,
            DockVerticalAnchor.MIDDLE => bounds.MinY + ((bounds.Height - windowHeight) / 2),
            DockVerticalAnchor.DOWN => bounds.MaxY - windowHeight - dock.BottomSpacing,
            _ => bounds.MinY
        };
    }
}
