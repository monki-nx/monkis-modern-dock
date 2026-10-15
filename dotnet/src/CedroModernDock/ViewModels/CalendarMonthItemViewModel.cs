using System;

namespace CedroModernDock.ViewModels;

/// <summary>One month cell of the dock calendar's year view.</summary>
public class CalendarMonthItemViewModel : ViewModelBase
{
    /// <summary>First day of the month.</summary>
    public DateTime Date { get; }

    /// <summary>Abbreviated month name, e.g. "Oct".</summary>
    public string Text { get; }

    /// <summary>False for the leading months of the next year (drawn dimmed).</summary>
    public bool IsInYear { get; }

    /// <summary>Today's month: filled accent circle.</summary>
    public bool IsCurrent { get; }

    public CalendarMonthItemViewModel(DateTime date, string text, bool isInYear, bool isCurrent)
    {
        Date = date;
        Text = text;
        IsInYear = isInYear;
        IsCurrent = isCurrent;
    }
}
