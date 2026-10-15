using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using CedroModernDock.ViewModels;
using CedroModernDock.Views;

[assembly: AvaloniaTestApplication(typeof(CedroModernDock.Tests.HeadlessTestApp))]

namespace CedroModernDock.Tests;

public class HeadlessTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>The navigation slide, run on the real calendar view in a headless window.</summary>
public class DockCalendarSlideTest
{
    private static (DockCalendar View, CalendarViewModel Calendar) Show()
    {
        var calendar = new CalendarViewModel(new DateTime(2026, 10, 15), CultureInfo.GetCultureInfo("en-001"));
        var view = new DockCalendar { DataContext = calendar };
        var window = new Window { Content = view, Width = 300, Height = 400 };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (view, calendar);
    }

    private static double OffsetOf(Visual visual) =>
        visual.RenderTransform is TranslateTransform t ? t.Y : 0;

    /// <summary>
    /// Samples the vertical offset for <paramref name="milliseconds"/>. The
    /// headless animation clock follows real time, so ticks are spaced out.
    /// </summary>
    private static async Task<List<double>> Offsets(Visual visual, int milliseconds)
    {
        var offsets = new List<double>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            offsets.Add(OffsetOf(visual));
            await Task.Delay(10);
        }
        return offsets;
    }

    [AvaloniaFact]
    public async Task Next_SlidesTheNewMonthUpFromBelow_AndSettles()
    {
        var (view, calendar) = Show();
        var page = view.FindControl<StackPanel>("DaysPage")!;

        calendar.NextCommand.Execute(null);
        var offsets = await Offsets(page, 400);

        Assert.Contains(offsets, y => y > 1);      // started below its place
        Assert.Equal(0, offsets[^1], 3);          // and settled
        Assert.Equal(1, page.Opacity, 3);
    }

    [AvaloniaFact]
    public async Task Previous_InTheYearView_SlidesTheYearDownFromAbove()
    {
        var (view, calendar) = Show();
        calendar.ShowYearCommand.Execute(null);
        var page = view.FindControl<ItemsControl>("YearPage")!;

        calendar.PreviousCommand.Execute(null);
        var offsets = await Offsets(page, 400);

        Assert.Contains(offsets, y => y < -1);
        Assert.Equal(0, offsets[^1], 3);
    }

    [AvaloniaFact]
    public async Task ViewSwitch_DoesNotSlide()
    {
        var (view, calendar) = Show();
        var page = view.FindControl<ItemsControl>("YearPage")!;

        calendar.ShowYearCommand.Execute(null);
        var offsets = await Offsets(page, 250);

        Assert.All(offsets, y => Assert.Equal(0, y, 3));
    }
}
