using System.Globalization;
using TimetableAlert.Core.Feed;
using TimetableAlert.Core.Models;
using Xunit;

namespace TimetableAlert.Tests;

public class TimetableFeedTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly FeedOptions Options = new()
    {
        Student = "Sam",
        SubjectOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ORIENTATION - Meet Your Head of Year"] = "Head of Year",
        },
        TeachersByCourseId = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["3078"] = "Bailey Bravo",
        },
    };

    // Read in UTC so the expected clock times do not move with the machine running the tests.
    private static Timetable Build(DateOnly weekOf) => TimetableFeed.Build(
        File.ReadAllText(Path.Combine("Fixtures", "canvas-week.ics")), Options, weekOf, FetchedAt, TimeZoneInfo.Utc);

    [Fact]
    public void Build_KeepsOnlyTheWantedWeek()
    {
        // The fixture's fourth timed event is the Monday after, and must not come through.
        var timetable = Build(new DateOnly(2026, 9, 9));

        Assert.Equal(3, timetable.Lessons.Count);
        Assert.DoesNotContain(timetable.Lessons, lesson => lesson.Subject == "Wellbeing");
    }

    [Theory]
    [InlineData("2026-09-07")]
    [InlineData("2026-09-09")]
    [InlineData("2026-09-13")]
    public void Build_TakesTheWholeWeekFromAnyDayInIt(string anyDay)
    {
        var timetable = Build(DateOnly.Parse(anyDay, CultureInfo.InvariantCulture));

        Assert.Equal(new DateOnly(2026, 9, 7), timetable.CoversFrom);
        Assert.Equal(new DateOnly(2026, 9, 13), timetable.CoversUntil);
        Assert.Equal(3, timetable.Lessons.Count);
    }

    [Fact]
    public void Build_OrdersLessonsAndNamesThem()
    {
        var subjects = Build(new DateOnly(2026, 9, 9)).Lessons.Select(lesson => lesson.Subject).ToArray();

        Assert.Equal(new[] { "Assembly", "Head of Year", "Maths Higher" }, subjects);
    }

    [Fact]
    public void Build_DatesEveryLessonSoNoneRecurs()
    {
        var timetable = Build(new DateOnly(2026, 9, 9));

        Assert.All(timetable.Lessons, lesson => Assert.True(lesson.IsDated));
        Assert.Equal(new DateOnly(2026, 9, 7), timetable.Lessons[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 8), timetable.Lessons[2].Date);
        Assert.Equal(DayOfWeek.Tuesday, timetable.Lessons[2].Day);
    }

    [Fact]
    public void Build_TakesTimesAndTheTeacherFromTheFeedAndTheOptions()
    {
        var maths = Build(new DateOnly(2026, 9, 9)).Lessons[2];

        Assert.Equal(new TimeOnly(8, 0), maths.Start);
        Assert.Equal(new TimeOnly(8, 55), maths.End);
        Assert.Equal("Bailey Bravo", maths.Teacher);
        Assert.Null(Build(new DateOnly(2026, 9, 9)).Lessons[0].Teacher);
    }

    [Fact]
    public void Build_RecordsWhenItWasFetched()
    {
        var timetable = Build(new DateOnly(2026, 9, 9));

        Assert.Equal(FetchedAt, timetable.FetchedAt);
        Assert.Equal("Sam", timetable.Student);
        Assert.True(timetable.Covers(new DateOnly(2026, 9, 10)));
        Assert.False(timetable.Covers(new DateOnly(2026, 9, 14)));
    }

    [Theory]
    [InlineData("2026-09-07", "2026-09-07")]
    [InlineData("2026-09-13", "2026-09-07")]
    [InlineData("2026-09-14", "2026-09-14")]
    public void MondayOf_RunsWeeksMondayToSunday(string date, string expected) =>
        Assert.Equal(
            DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            TimetableFeed.MondayOf(DateOnly.Parse(date, CultureInfo.InvariantCulture)));
}
