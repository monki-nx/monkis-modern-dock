using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CedroModernDock.Core.Application;

namespace CedroModernDock.ViewModels;

/// <summary>
/// Month calendar opened from the dock clock, modeled on the Windows 11
/// taskbar calendar. Purely a visual reference: today is marked, a clicked
/// day keeps a ring, and the months can be browsed. No agenda, no other views.
/// Names and the first day of the week follow the system culture, like the
/// taskbar does.
/// </summary>
public class CalendarViewModel : ViewModelBase
{
    private readonly CultureInfo _culture;
    private DateTime _today;
    private DateTime _month;
    private DateTime? _selected;
    private string _headerText = "";
    private string _monthTitle = "";

    /// <summary>The 42 cells of the shown month (6 weeks).</summary>
    public ObservableCollection<CalendarDayViewModel> Days { get; } = new();

    /// <summary>Two-letter week day names in display order ("Mo", "Tu"...).</summary>
    public string[] DayNames { get; }

    /// <summary>Today's long date without the year, e.g. "Thursday, 15 October".</summary>
    public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }

    /// <summary>Shown month, e.g. "October 2026".</summary>
    public string MonthTitle { get => _monthTitle; private set => SetProperty(ref _monthTitle, value); }

    /// <summary>First day of the shown month.</summary>
    public DateTime Month => _month;

    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand GoToTodayCommand { get; }

    public CalendarViewModel() : this(DateTime.Today, CultureInfo.CurrentCulture) { }

    public CalendarViewModel(DateTime today, CultureInfo culture)
    {
        _culture = culture;
        var format = culture.DateTimeFormat;
        DayNames = CalendarMonth.WeekDays(format.FirstDayOfWeek)
            .Select(d => ShortDayName(format.GetAbbreviatedDayName(d)))
            .ToArray();

        PreviousMonthCommand = new RelayCommand(_ => ShowMonth(_month.AddMonths(-1)));
        NextMonthCommand = new RelayCommand(_ => ShowMonth(_month.AddMonths(1)));
        GoToTodayCommand = new RelayCommand(_ => ShowMonth(_today));

        Reset(today);
    }

    /// <summary>Back to today's month with no ring, as each time the taskbar calendar opens.</summary>
    public void Reset(DateTime today)
    {
        _today = today.Date;
        _selected = null;
        HeaderText = Capitalize(_today.ToString("dddd", _culture) + ", "
            + _today.ToString(_culture.DateTimeFormat.MonthDayPattern, _culture));
        ShowMonth(_today);
    }

    /// <summary>Moves the today mark when the date changes while the calendar is shown.</summary>
    public void UpdateToday(DateTime today)
    {
        if (today.Date == _today) return;
        var selected = _selected;
        var month = _month;
        Reset(today);
        _selected = selected;
        ShowMonth(month);
    }

    /// <summary>
    /// Rings the clicked day. A padding day of another month also brings that
    /// month into view, like the taskbar calendar.
    /// </summary>
    public void Select(DateTime date)
    {
        _selected = date.Date;
        if (date.Year != _month.Year || date.Month != _month.Month)
        {
            ShowMonth(date);
            return;
        }
        foreach (var day in Days)
            day.IsSelected = day.Date == _selected;
    }

    private void ShowMonth(DateTime month)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        MonthTitle = Capitalize(_month.ToString(_culture.DateTimeFormat.YearMonthPattern, _culture));

        Days.Clear();
        foreach (var date in CalendarMonth.Days(_month, _culture.DateTimeFormat.FirstDayOfWeek))
        {
            Days.Add(new CalendarDayViewModel(date, date.Month == _month.Month)
            {
                IsToday = date == _today,
                IsSelected = date == _selected
            });
        }
        OnPropertyChanged(nameof(Month));
    }

    /// <summary>"Mon" → "Mo", "lun." → "Lu", as the taskbar calendar shows them.</summary>
    private string ShortDayName(string abbreviated)
    {
        string name = abbreviated.TrimEnd('.');
        var info = new StringInfo(name);
        if (info.LengthInTextElements > 2)
            name = info.SubstringByTextElements(0, 2);
        return Capitalize(name);
    }

    private string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], _culture) + text[1..];
}
