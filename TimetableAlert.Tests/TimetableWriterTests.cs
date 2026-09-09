using TimetableAlert.Core;
using TimetableAlert.Core.Models;
using Xunit;

namespace TimetableAlert.Tests;

public class TimetableWriterTests
{
    [Fact]
    public void ToJson_RoundTripsADownloadedWeek()
    {
        // This is what caching relies on: what the writer emits, the loader must read back whole.
        var original = new Timetable(
            "Sam",
            new AlertOptions(FirstWarningMinutes: 5, FirstWarningSeconds: 4, SecondWarningSeconds: 45),
            [
                Lesson.OnDate(new DateOnly(2026, 9, 7), new TimeOnly(8, 30), new TimeOnly(9, 0), "Assembly", null),
                Lesson.OnDate(new DateOnly(2026, 9, 8), new TimeOnly(9, 0), null, "Maths Higher", "Bailey Bravo"),
            ],
            new DateOnly(2026, 9, 7),
            new DateOnly(2026, 9, 13),
            new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero));

        var result = TimetableLoader.ParseJson(TimetableWriter.ToJson(original));

        Assert.True(result.Success, result.ErrorSummary);
        var reloaded = result.Timetable!;

        Assert.Equal(original.Student, reloaded.Student);
        Assert.Equal(original.Alerts, reloaded.Alerts);
        Assert.Equal(original.CoversFrom, reloaded.CoversFrom);
        Assert.Equal(original.CoversUntil, reloaded.CoversUntil);
        Assert.Equal(original.FetchedAt, reloaded.FetchedAt);
        Assert.Equal(original.Lessons, reloaded.Lessons);
    }

    [Fact]
    public void ToJson_RoundTripsAHandWrittenWeeklyPattern()
    {
        var original = new Timetable("Sam", AlertOptions.Default,
            [new Lesson(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), "Maths Higher", "Bailey Bravo")]);

        var reloaded = TimetableLoader.ParseJson(TimetableWriter.ToJson(original)).Timetable!;

        Assert.Equal(original.Lessons, reloaded.Lessons);
        Assert.Null(reloaded.CoversFrom);
        Assert.Null(reloaded.FetchedAt);
        Assert.False(reloaded.Lessons[0].IsDated);
    }
}
