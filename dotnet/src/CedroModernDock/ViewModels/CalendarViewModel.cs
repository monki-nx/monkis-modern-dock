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
/// day keeps a ring, and the months can be browsed, one by one or from the
/// year view opened by the month title. The taskbar's decade view is left
/// out: the year title of the year view is display only. No agenda.
/// Names and the first day of the week follow the system culture, like the
/// taskbar does.
/// </summary>
public class CalendarViewModel : ViewModelBase
{
    private readonly CultureInfo _culture;
    private DateTime _today;
    private DateTime _month;
    private int _year;
    private DateTime? _selected;
    private bool _isYearView;
    private string _headerText = "";
    private string _monthTitle = "";
    private string _yearTitle = "";

    /// <summary>The 42 cells of the shown month (6 weeks).</summary>
    public ObservableCollection<CalendarDayViewModel> Days { get; } = new();

    /// <summary>The 16 cells of the year view (the year's months, then 4 of the next one).</summary>
    public ObservableCollection<CalendarMonthItemViewModel> Months { get; } = new();

    /// <summary>Two-letter week day names in display order ("Mo", "Tu"...).</summary>
    public string[] DayNames { get; }

    /// <summary>Today's long date without the year, e.g. "Thursday, 15 October".</summary>
    public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }

    /// <summary>Shown month, e.g. "October 2026".</summary>
    public string MonthTitle { get => _monthTitle; private set => SetProperty(ref _monthTitle, value); }

    /// <summary>Year of the year view, e.g. "2026".</summary>
    public string YearTitle { get => _yearTitle; private set => SetProperty(ref _yearTitle, value); }

    /// <summary>True while the year view (months grid) is shown instead of the month (days grid).</summary>
    public bool IsYearView
    {
        get => _isYearView;
        private set
        {
            if (SetProperty(ref _isYearView, value))
                OnPropertyChanged(nameof(IsMonthView));
        }
    }

    public bool IsMonthView => !IsYearView;

    /// <summary>First day of the shown month.</summary>
    public DateTime Month => _month;

    /// <summary>Year shown by the year view.</summary>
    public int Year => _year;

    /// <summary>Up arrow: previous month, or previous year in the year view.</summary>
    public ICommand PreviousCommand { get; }

    /// <summary>Down arrow: next month, or next year in the year view.</summary>
    public ICommand NextCommand { get; }

    public ICommand GoToTodayCommand { get; }

    /// <summary>Month title: opens the year view on the shown month's year.</summary>
    public ICommand ShowYearCommand { get; }

    public CalendarViewModel() : this(DateTime.Today, CultureInfo.CurrentCulture) { }

    public CalendarViewModel(DateTime today, CultureInfo culture)
    {
        _culture = culture;
        var format = culture.DateTimeFormat;
        DayNames = CalendarMonth.WeekDays(format.FirstDayOfWeek)
            .Select(d => ShortDayName(format.GetAbbreviatedDayName(d)))
            .ToArray();

        PreviousCommand = new RelayCommand(_ => Step(-1));
        NextCommand = new RelayCommand(_ => Step(1));
        GoToTodayCommand = new RelayCommand(_ =>
        {
            IsYearView = false;
            ShowMonth(_today);
        });
        ShowYearCommand = new RelayCommand(_ =>
        {
            IsYearView = true;
            ShowYear(_month.Year);
        });

        Reset(today);
    }

    /// <summary>Back to today's month with no ring, as each time the taskbar calendar opens.</summary>
    public void Reset(DateTime today)
    {
        _today = today.Date;
        _selected = null;
        IsYearView = false;
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
        bool yearView = IsYearView;
        int year = _year;
        Reset(today);
        _selected = selected;
        ShowMonth(month);
        if (yearView)
        {
            IsYearView = true;
            ShowYear(year);
        }
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

    /// <summary>A month clicked in the year view (the next year's dimmed ones too) opens its days.</summary>
    public void SelectMonth(DateTime month)
    {
        IsYearView = false;
        ShowMonth(month);
    }

    private void Step(int delta)
    {
        if (IsYearView)
            ShowYear(_year + delta);
        else
            ShowMonth(_month.AddMonths(delta));
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

    private void ShowYear(int year)
    {
        _year = Math.Clamp(year, DateTime.MinValue.Year, DateTime.MaxValue.Year - 1);
        YearTitle = _year.ToString(_culture);

        Months.Clear();
        foreach (var month in CalendarMonth.Months(_year))
        {
            Months.Add(new CalendarMonthItemViewModel(month,
                ShortMonthName(month),
                isInYear: month.Year == _year,
                isCurrent: month.Year == _today.Year && month.Month == _today.Month));
        }
        OnPropertyChanged(nameof(Year));
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

    /// <summary>"Oct", "Sept"; "oct." → "Oct", like the day names.</summary>
    private string ShortMonthName(DateTime month) =>
        Capitalize(_culture.DateTimeFormat.GetAbbreviatedMonthName(month.Month).TrimEnd('.'));

    private string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], _culture) + text[1..];
}
