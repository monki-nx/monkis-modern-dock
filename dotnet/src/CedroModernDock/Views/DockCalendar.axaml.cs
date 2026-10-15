using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CedroModernDock.Infrastructure.Windows.Native;
using CedroModernDock.ViewModels;

namespace CedroModernDock.Views;

/// <summary>Month calendar shown in a popup above the dock clock.</summary>
public partial class DockCalendar : UserControl
{
    public DockCalendar()
    {
        InitializeComponent();
        // Grayscale antialiasing, as in the context menu: ClearType leaves a
        // dark fringe around the letters over the transparent acrylic.
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
    }

    /// <summary>
    /// Dock styling, shared with the context menu: the dock's color and
    /// transparency as the background, white or dark text depending on how
    /// dark that color is, and the shell's accent shade for today.
    /// </summary>
    public void ApplyStyle(Color dockColor)
    {
        bool darkDock = (0.299 * dockColor.R + 0.587 * dockColor.G + 0.114 * dockColor.B) < 140;
        Color text = darkDock ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        Color Tint(byte a) => Color.FromArgb(a, text.R, text.G, text.B);

        // Dark surfaces use a light accent shade with dark text and vice
        // versa, as the taskbar calendar does in dark and light mode.
        var shade = SystemAccentPalette.Shade(darkDock ? SystemAccentPalette.Light2 : SystemAccentPalette.Dark1);
        Color accent = shade is var (r, g, b)
            ? Color.FromRgb(r, g, b)
            : (darkDock ? Color.FromRgb(0x99, 0xEB, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
        Color onAccent = darkDock ? Colors.Black : Colors.White;

        var res = Resources;
        res["CalendarBackground"] = new SolidColorBrush(dockColor);
        res["CalendarBorderBrush"] = new SolidColorBrush(Tint(0x30));
        res["CalendarHeaderBrush"] = new SolidColorBrush(Tint(0x0C));
        res["CalendarForeground"] = new SolidColorBrush(text);
        res["CalendarDimForeground"] = new SolidColorBrush(Tint(0x80));
        res["CalendarHoverBrush"] = new SolidColorBrush(Tint(0x26));
        res["CalendarPressedBrush"] = new SolidColorBrush(Tint(0x18));
        res["CalendarAccentBrush"] = new SolidColorBrush(accent);
        res["CalendarOnAccentBrush"] = new SolidColorBrush(onAccent);
    }

    /// <summary>Width of the compact layout at scale 1 (Root's Width in the XAML).</summary>
    private const double BaseWidth = 196;

    /// <summary>Below this the digits stop being legible, so the calendar stops shrinking.</summary>
    private const double MinScale = 0.6;

    /// <summary>
    /// Keeps the calendar within <paramref name="maxWidth"/> (half the dock's
    /// length): the compact layout at most, scaled down when the dock is
    /// narrower, never below <see cref="MinScale"/>.
    /// </summary>
    public void FitTo(double maxWidth)
    {
        double scale = Math.Clamp(maxWidth / BaseWidth, MinScale, 1);
        Scaler.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>The acrylic backdrop already carries the tint: drop the background so it is not applied twice.</summary>
    public void UseAcrylicBackground() => Resources["CalendarBackground"] = Brushes.Transparent;

    private void OnDayClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CalendarDayViewModel day } && DataContext is CalendarViewModel calendar)
            calendar.Select(day.Date);
    }

    private double _wheelDelta;

    /// <summary>
    /// The wheel browses the months, like the taskbar calendar: one month per
    /// notch. Touchpads report fractions of a notch, which add up first.
    /// </summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not CalendarViewModel calendar) return;
        e.Handled = true;
        _wheelDelta += e.Delta.Y;
        while (_wheelDelta >= 1)
        {
            calendar.PreviousMonthCommand.Execute(null);
            _wheelDelta -= 1;
        }
        while (_wheelDelta <= -1)
        {
            calendar.NextMonthCommand.Execute(null);
            _wheelDelta += 1;
        }
    }
}
