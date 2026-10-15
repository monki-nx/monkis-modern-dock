using System.Globalization;
using CedroModernDock.Core.Application;
using CedroModernDock.ViewModels;

namespace CedroModernDock.Tests;

public class CalendarMonthTest
{
    [Fact]
    public void Days_StartOnTheFirstDayOfWeek_PaddedWithThePreviousMonth()
    {
        // October 2026 starts on a Thursday.
        var days = CalendarMonth.Days(new DateTime(2026, 10, 15), DayOfWeek.Monday);

        Assert.Equal(42, days.Count);
        Assert.Equal(new DateTime(2026, 9, 28), days[0]);
        Assert.Equal(new DateTime(2026, 10, 1), days[3]);
        Assert.Equal(new DateTime(2026, 11, 8), days[41]);
    }

    [Fact]
    public void Days_SundayFirst_ShiftsTheGrid()
    {
        var days = CalendarMonth.Days(new DateTime(2026, 10, 1), DayOfWeek.Sunday);

        Assert.Equal(new DateTime(2026, 9, 27), days[0]);
        Assert.Equal(DayOfWeek.Sunday, days[0].DayOfWeek);
    }

    [Fact]
    public void Days_MonthStartingOnTheFirstDayOfWeek_HasNoLeadingPadding()
    {
        // June 2026 starts on a Monday.
        var days = CalendarMonth.Days(new DateTime(2026, 6, 1), DayOfWeek.Monday);

        Assert.Equal(new DateTime(2026, 6, 1), days[0]);
    }

    [Theory]
    [InlineData(2027, 28)]
    [InlineData(2028, 29)]
    public void Days_February_IncludesEveryDayOfTheMonth(int year, int length)
    {
        var days = CalendarMonth.Days(new DateTime(year, 2, 1), DayOfWeek.Monday);

        Assert.Equal(length, days.Count(d => d.Month == 2));
    }

    [Fact]
    public void Months_AreTheYearThenFourOfTheNext()
    {
        var months = CalendarMonth.Months(2026);

        Assert.Equal(16, months.Count);
        Assert.Equal(new DateTime(2026, 1, 1), months[0]);
        Assert.Equal(new DateTime(2026, 12, 1), months[11]);
        Assert.Equal(new DateTime(2027, 4, 1), months[15]);
    }

    [Fact]
    public void WeekDays_WrapAroundTheWeek()
    {
        var weekDays = CalendarMonth.WeekDays(DayOfWeek.Monday);

        Assert.Equal(DayOfWeek.Monday, weekDays[0]);
        Assert.Equal(DayOfWeek.Sunday, weekDays[6]);
    }
}

public class CalendarViewModelTest
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-001");
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-CL");
    private static readonly DateTime Today = new(2026, 10, 15, 10, 42, 0);

    [Fact]
    public void Reset_ShowsTodaysMonthWithTodayMarked()
    {
        var calendar = new CalendarViewModel(Today, English);

        Assert.Equal("Thursday, 15 October", calendar.HeaderText);
        Assert.Equal("October 2026", calendar.MonthTitle);
        var today = Assert.Single(calendar.Days, d => d.IsToday);
        Assert.Equal(new DateTime(2026, 10, 15), today.Date);
        Assert.DoesNotContain(calendar.Days, d => d.IsSelected);
    }

    [Fact]
    public void DayNames_AreTwoLettersInTheCulturesWeekOrder()
    {
        Assert.Equal(new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" },
            new CalendarViewModel(Today, English).DayNames);
        Assert.Equal(new[] { "Lu", "Ma", "Mi", "Ju", "Vi", "Sá", "Do" },
            new CalendarViewModel(Today, Spanish).DayNames);
    }

    [Fact]
    public void Names_FollowTheCulture()
    {
        var calendar = new CalendarViewModel(Today, Spanish);

        Assert.Equal("Jueves, 15 de octubre", calendar.HeaderText);
        Assert.Equal("Octubre de 2026", calendar.MonthTitle);
    }

    [Fact]
    public void Select_RingsOnlyTheClickedDay()
    {
        var calendar = new CalendarViewModel(Today, English);

        calendar.Select(new DateTime(2026, 10, 20));
        calendar.Select(new DateTime(2026, 10, 22));

        var ringed = Assert.Single(calendar.Days, d => d.IsSelected);
        Assert.Equal(new DateTime(2026, 10, 22), ringed.Date);
        Assert.True(ringed.ShowRing);
        Assert.False(ringed.ShowTodayRing);
    }

    [Fact]
    public void Select_Today_UsesTheInnerRing()
    {
        var calendar = new CalendarViewModel(Today, English);

        calendar.Select(Today);

        var today = Assert.Single(calendar.Days, d => d.IsToday);
        Assert.True(today.ShowTodayRing);
        Assert.False(today.ShowRing);
    }

    [Fact]
    public void Select_PaddingDay_BringsItsMonthIntoView()
    {
        var calendar = new CalendarViewModel(Today, English);

        calendar.Select(new DateTime(2026, 11, 3));

        Assert.Equal(new DateTime(2026, 11, 1), calendar.Month);
        Assert.Equal(new DateTime(2026, 11, 3), Assert.Single(calendar.Days, d => d.IsSelected).Date);
    }

    [Fact]
    public void MonthNavigation_KeepsTheRingAndReturnsToToday()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.Select(new DateTime(2026, 10, 20));

        calendar.NextCommand.Execute(null);
        Assert.Equal("November 2026", calendar.MonthTitle);
        Assert.DoesNotContain(calendar.Days, d => d.IsToday);

        calendar.PreviousCommand.Execute(null);
        calendar.PreviousCommand.Execute(null);
        Assert.Equal("September 2026", calendar.MonthTitle);

        calendar.GoToTodayCommand.Execute(null);
        Assert.Equal("October 2026", calendar.MonthTitle);
        Assert.Equal(new DateTime(2026, 10, 20), Assert.Single(calendar.Days, d => d.IsSelected).Date);
    }

    [Fact]
    public void Reset_ClearsTheRingAndTheBrowsedMonth()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.Select(new DateTime(2026, 10, 20));
        calendar.NextCommand.Execute(null);

        calendar.Reset(Today);

        Assert.Equal("October 2026", calendar.MonthTitle);
        Assert.DoesNotContain(calendar.Days, d => d.IsSelected);
    }

    [Fact]
    public void UpdateToday_MovesTheMarkAtMidnight_KeepingTheView()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.Select(new DateTime(2026, 10, 20));

        calendar.UpdateToday(new DateTime(2026, 10, 16, 0, 0, 1));

        Assert.Equal(new DateTime(2026, 10, 16), Assert.Single(calendar.Days, d => d.IsToday).Date);
        Assert.Equal(new DateTime(2026, 10, 20), Assert.Single(calendar.Days, d => d.IsSelected).Date);
        Assert.Equal("Friday, 16 October", calendar.HeaderText);
    }

    [Fact]
    public void ShowYear_OpensTheYearOfTheShownMonth_WithTodaysMonthMarked()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.NextCommand.Execute(null);
        calendar.NextCommand.Execute(null);
        calendar.NextCommand.Execute(null); // January 2027

        calendar.ShowYearCommand.Execute(null);

        Assert.True(calendar.IsYearView);
        Assert.Equal("2027", calendar.YearTitle);
        Assert.Equal(16, calendar.Months.Count);
        Assert.DoesNotContain(calendar.Months, m => m.IsCurrent);

        calendar.PreviousCommand.Execute(null);
        Assert.Equal("2026", calendar.YearTitle);
        var current = Assert.Single(calendar.Months, m => m.IsCurrent);
        Assert.Equal(new DateTime(2026, 10, 1), current.Date);
        Assert.Equal("Oct", current.Text);
    }

    [Fact]
    public void YearView_DimsTheNextYearsMonths()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.ShowYearCommand.Execute(null);

        Assert.All(calendar.Months.Take(12), m => Assert.True(m.IsInYear));
        Assert.All(calendar.Months.Skip(12), m => Assert.False(m.IsInYear));
        Assert.Equal("Jan", calendar.Months[12].Text);
    }

    [Fact]
    public void MonthNames_FollowTheCulture()
    {
        var calendar = new CalendarViewModel(Today, Spanish);
        calendar.ShowYearCommand.Execute(null);

        Assert.Equal("Ene", calendar.Months[0].Text);
        Assert.Equal("Oct", calendar.Months[9].Text);
    }

    [Fact]
    public void SelectMonth_OpensItsDays_EvenFromTheNextYear()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.Select(new DateTime(2026, 10, 20));
        calendar.ShowYearCommand.Execute(null);

        calendar.SelectMonth(new DateTime(2027, 2, 1));

        Assert.False(calendar.IsYearView);
        Assert.Equal("February 2027", calendar.MonthTitle);
        Assert.Equal(new DateTime(2027, 2, 1), calendar.Month);
    }

    [Fact]
    public void GoToToday_AndReset_LeaveTheYearView()
    {
        var calendar = new CalendarViewModel(Today, English);
        calendar.ShowYearCommand.Execute(null);
        calendar.NextCommand.Execute(null);

        calendar.GoToTodayCommand.Execute(null);
        Assert.False(calendar.IsYearView);
        Assert.Equal("October 2026", calendar.MonthTitle);

        calendar.ShowYearCommand.Execute(null);
        calendar.Reset(Today);
        Assert.False(calendar.IsYearView);
    }

    [Fact]
    public void Navigated_ReportsTheDirectionOfEachStep()
    {
        var calendar = new CalendarViewModel(Today, English);
        var steps = new List<int>();
        calendar.Navigated += steps.Add;

        calendar.NextCommand.Execute(null);      // November
        calendar.PreviousCommand.Execute(null);  // October
        calendar.Select(new DateTime(2026, 11, 2)); // padding day of November
        calendar.GoToTodayCommand.Execute(null); // back to October
        calendar.ShowYearCommand.Execute(null);  // view switch: no slide
        calendar.NextCommand.Execute(null);      // 2027
        calendar.SelectMonth(new DateTime(2027, 3, 1)); // view switch: no slide

        Assert.Equal(new[] { 1, -1, 1, -1, 1 }, steps);
    }

    [Fact]
    public void Navigated_IsNotRaisedWhenNothingMoves()
    {
        var calendar = new CalendarViewModel(Today, English);
        var steps = new List<int>();
        calendar.Navigated += steps.Add;

        calendar.GoToTodayCommand.Execute(null);   // already on today's month
        calendar.Select(new DateTime(2026, 10, 20)); // day of the shown month
        calendar.ShowYearCommand.Execute(null);
        calendar.GoToTodayCommand.Execute(null);   // from the year view: view switch

        Assert.Empty(steps);
    }
}
