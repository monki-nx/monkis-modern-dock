namespace CedroModernDock.Tests;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

public class DockPositioningServiceTest
{
    // Primary 1920x1080 at the origin; a second screen above-right of it,
    // like a monitor stacked higher than the primary one.
    private static readonly ScreenBounds Primary = new(0, 0, 1920, 1040);
    private static readonly ScreenBounds Second = new(1920, -312, 1920, 1080);

    [Fact]
    public void StaticModeAnchorsToTheScreenTheDockWasLastOn()
    {
        var (service, positioning) = Create();
        service.SetDockPosition(2700, 690); // last spot: on the second screen
        positioning.SetPositioningMode(DockPositioningMode.STATIC);
        positioning.SetVerticalAnchor(DockVerticalAnchor.DOWN);
        positioning.SetHorizontalAnchor(DockHorizontalAnchor.MIDDLE);
        positioning.SetBottomSpacing(8);

        var (x, y) = positioning.ResolvePosition(300, 66);

        Assert.Equal(1920 + (1920 - 300) / 2.0, x);
        Assert.Equal(-312 + 1080 - 66 - 8, y);
    }

    [Fact]
    public void StaticModeUsesThePrimaryScreenForADockOnIt()
    {
        var (service, positioning) = Create();
        service.SetDockPosition(500, 900);
        positioning.SetPositioningMode(DockPositioningMode.STATIC);
        positioning.SetVerticalAnchor(DockVerticalAnchor.DOWN);
        positioning.SetHorizontalAnchor(DockHorizontalAnchor.LEFT);
        positioning.SetLeftSpacing(10);
        positioning.SetBottomSpacing(0);

        var (x, y) = positioning.ResolvePosition(300, 66);

        Assert.Equal(10, x);
        Assert.Equal(1040 - 66, y);
    }

    [Fact]
    public void DynamicModeKeepsTheSavedPosition()
    {
        var (service, positioning) = Create();
        positioning.SetPositioningMode(DockPositioningMode.DYNAMIC);
        service.SetDockPosition(2733, 694);

        Assert.Equal((2733.0, 694.0), positioning.ResolvePosition(300, 66));
    }

    [Fact]
    public void MenuAcrylicIntensityIsKeptWithinPercentBounds()
    {
        var (service, _) = Create();
        var appearance = new DockAppearanceService(service);

        appearance.SetMenuAcrylicIntensity(140);
        Assert.Equal(100, appearance.GetMenuAcrylicIntensity());

        appearance.SetMenuAcrylicIntensity(-5);
        Assert.Equal(0, appearance.GetMenuAcrylicIntensity());
    }

    private static (DockService, DockPositioningService) Create()
    {
        var service = new DockService(new InMemoryDockRepository());
        return (service, new DockPositioningService(service, new TwoScreens()));
    }

    private sealed class TwoScreens : IScreenBoundsProvider
    {
        public ScreenBounds GetPrimaryScreenBounds() => Primary;

        public ScreenBounds GetScreenBoundsAt(double x, double y) =>
            x >= Second.MinX && x < Second.MaxX && y >= Second.MinY && y < Second.MaxY ? Second : Primary;
    }

    private sealed class InMemoryDockRepository : IDockRepository
    {
        private readonly DockModel _model = new();
        public DockModel Load() => _model;
        public void Save(DockModel model) { }
    }
}
