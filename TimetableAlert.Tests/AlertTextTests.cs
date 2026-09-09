using System.Globalization;
using TimetableAlert.Core;
using TimetableAlert.Core.Models;
using Xunit;

namespace TimetableAlert.Tests;

public class AlertTextTests
{
    // 7 September 2026 is a Monday.
    private static readonly DateTime MondayNine = new(2026, 9, 7, 9, 0, 0);

    private static Lesson Maths(TimeOnly? end = null, string? teacher = "Bailey Bravo") =>
        new(DayOfWeek.Monday, new TimeOnly(9, 0), end ?? new TimeOnly(10, 0), "Maths Higher", teacher);

    [Fact]
    public void Subject_IsShouted() => Assert.Equal("⏰ MATHS HIGHER", AlertText.Subject(Maths()));

    [Fact]
    public void Detail_ShowsTheTimeRangeAndTeacher() =>
        Assert.Equal("09:00–10:00  ·  Bailey Bravo", AlertText.Detail(Maths()));

    [Fact]
    public void Detail_OmitsTheTeacherWhenThereIsNone() =>
        Assert.Equal("09:00–10:00", AlertText.Detail(Maths(teacher: null)));

    [Fact]
    public void Detail_OmitsTheEndTimeWhenThereIsNone()
    {
        var lesson = new Lesson(DayOfWeek.Monday, new TimeOnly(9, 0), null, "Maths Higher", null);

        Assert.Equal("09:00", AlertText.Detail(lesson));
    }

    [Theory]
    // The final minute counts down in seconds.
    [InlineData(-1, "is starting now")]
    [InlineData(0, "is starting now")]
    [InlineData(1, "starts in 1 second")]
    [InlineData(43, "starts in 43 seconds")]
    [InlineData(60, "starts in 60 seconds")]
    // Then in whole minutes, up to an hour.
    [InlineData(120, "starts in 2 minutes")]
    [InlineData(420, "starts in 7 minutes")]
    [InlineData(3540, "starts in 59 minutes")]
    public void Timing_CountsDownWhenTheLessonIsClose(int secondsAway, string expected)
    {
        var startsAt = MondayNine.AddSeconds(secondsAway);

        Assert.Equal(expected, AlertText.Timing(startsAt, MondayNine));
    }

    [Fact]
    public void Timing_NamesTheTimeOnceALessonIsMoreThanAnHourOff()
    {
        // A countdown stops being useful this far out - this is what the load preview shows.
        Assert.Equal("starts at 13:00 today", AlertText.Timing(MondayNine.AddHours(4), MondayNine));
    }

    [Fact]
    public void Timing_SaysTomorrowForTheNextDay() =>
        Assert.Equal("starts at 09:00 tomorrow", AlertText.Timing(MondayNine.AddDays(1), MondayNine));

    [Fact]
    public void Timing_NamesTheDayForAnythingFurtherOut()
    {
        // Friday afternoon, previewing Monday morning: the case that made the old wording read
        // "starts in 4260 minutes".
        var friday = new DateTime(2026, 9, 11, 16, 0, 0);
        var mondayLesson = new DateTime(2026, 9, 14, 9, 0, 0);

        Assert.Equal("starts at 09:00 on Monday", AlertText.Timing(mondayLesson, friday));
    }

    [Fact]
    public void Timing_TreatsJustAfterMidnightAsTomorrowNotLaterToday()
    {
        var lateSunday = new DateTime(2026, 9, 6, 23, 30, 0);

        Assert.Equal("starts at 08:30 tomorrow", AlertText.Timing(new DateTime(2026, 9, 7, 8, 30, 0), lateSunday));
    }

    [Fact]
    public void Format_UsesTwentyFourHourTimeRegardlessOfCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("14:00", AlertText.Format(new TimeOnly(14, 0)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
