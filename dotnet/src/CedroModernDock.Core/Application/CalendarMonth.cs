namespace CedroModernDock.Core.Application;

/// <summary>
/// Month grid of the dock calendar, laid out like the Windows 11 taskbar
/// calendar: always 6 weeks of 7 days, starting on the culture's first day
/// of the week, padded with the trailing days of the previous month and the
/// leading days of the next one.
/// </summary>
public static class CalendarMonth
{
    public const int Weeks = 6;
    public const int DaysPerWeek = 7;

    /// <summary>The 42 dates shown for the month containing <paramref name="month"/>.</summary>
    public static IReadOnlyList<DateTime> Days(DateTime month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        int lead = ((int)first.DayOfWeek - (int)firstDayOfWeek + DaysPerWeek) % DaysPerWeek;
        var start = first.AddDays(-lead);

        var days = new DateTime[Weeks * DaysPerWeek];
        for (int i = 0; i < days.Length; i++)
            days[i] = start.AddDays(i);
        return days;
    }

    public const int MonthColumns = 4;
    public const int MonthRows = 4;

    /// <summary>
    /// The 16 months of the year view, as on the taskbar calendar: the 12
    /// months of <paramref name="year"/> followed by the first 4 of the next
    /// year (drawn dimmed). Each is the first day of its month.
    /// </summary>
    public static IReadOnlyList<DateTime> Months(int year)
    {
        var first = new DateTime(year, 1, 1);
        var months = new DateTime[MonthColumns * MonthRows];
        for (int i = 0; i < months.Length; i++)
            months[i] = first.AddMonths(i);
        return months;
    }

    /// <summary>The 7 week days in display order, starting on <paramref name="firstDayOfWeek"/>.</summary>
    public static IReadOnlyList<DayOfWeek> WeekDays(DayOfWeek firstDayOfWeek)
    {
        var weekDays = new DayOfWeek[DaysPerWeek];
        for (int i = 0; i < DaysPerWeek; i++)
            weekDays[i] = (DayOfWeek)(((int)firstDayOfWeek + i) % DaysPerWeek);
        return weekDays;
    }
}
