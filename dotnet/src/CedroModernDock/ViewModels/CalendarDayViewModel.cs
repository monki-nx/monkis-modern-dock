using System;
using System.Globalization;

namespace CedroModernDock.ViewModels;

/// <summary>One day cell of the dock calendar's month grid.</summary>
public class CalendarDayViewModel : ViewModelBase
{
    private bool _isToday;
    private bool _isSelected;

    public DateTime Date { get; }

    public string Text => Date.Day.ToString(CultureInfo.CurrentCulture);

    /// <summary>False for the padding days of the previous and next months (drawn dimmed).</summary>
    public bool IsCurrentMonth { get; }

    /// <summary>Filled accent circle.</summary>
    public bool IsToday
    {
        get => _isToday;
        set
        {
            if (SetProperty(ref _isToday, value)) NotifyRings();
        }
    }

    /// <summary>Ring left by a click: a visual mark only, it triggers nothing.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) NotifyRings();
        }
    }

    /// <summary>Accent ring around a clicked day.</summary>
    public bool ShowRing => IsSelected && !IsToday;

    /// <summary>Clicked today: an inner ring inside the filled circle, as on the taskbar.</summary>
    public bool ShowTodayRing => IsSelected && IsToday;

    private void NotifyRings()
    {
        OnPropertyChanged(nameof(ShowRing));
        OnPropertyChanged(nameof(ShowTodayRing));
    }

    public CalendarDayViewModel(DateTime date, bool isCurrentMonth)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
    }
}
