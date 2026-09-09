using TimetableAlert.Core;
using TimetableAlert.Core.Models;
using Xunit;

namespace TimetableAlert.Tests;

public class TimetableFreshnessTests
{
    // Wednesday of the week beginning Monday 7 September 2026.
    private static readonly DateTime Wednesday = new(2026, 9, 9, 12, 0, 0);

    private static Timetable Downloaded(DateTimeOffset fetchedAt, DateOnly? coversFrom = null) => new(
        "Sam",
        AlertOptions.Default,
        [Lesson.OnDate(new DateOnly(2026, 9, 8), new TimeOnly(9, 0), null, "Maths", null)],
        coversFrom ?? new DateOnly(2026, 9, 7),
        (coversFrom ?? new DateOnly(2026, 9, 7)).AddDays(6),
        fetchedAt);

    [Fact]
    public void IsStale_WithNothingCached() => Assert.True(TimetableFreshness.IsStale(null, Wednesday));

    [Fact]
    public void IsStale_JustAfterFetching() =>
        Assert.False(TimetableFreshness.IsStale(Downloaded(Wednesday.AddMinutes(-5)), Wednesday));

    [Fact]
    public void IsStale_ElevenHoursOn() =>
        Assert.False(TimetableFreshness.IsStale(Downloaded(Wednesday.AddHours(-11)), Wednesday));

    [Fact]
    public void IsStale_ThirteenHoursOn() =>
        Assert.True(TimetableFreshness.IsStale(Downloaded(Wednesday.AddHours(-13)), Wednesday));

    [Fact]
    public void IsStale_WhenTheWeekHasRolledOverEvenThoughItWasJustFetched()
    {
        // The whole point: a copy of last week must not go on firing this week's alerts, however
        // recently it was downloaded.
        var lastWeek = Downloaded(Wednesday.AddMinutes(-1), new DateOnly(2026, 8, 31));

        Assert.True(TimetableFreshness.IsStale(lastWeek, Wednesday));
    }

    [Fact]
    public void IsStale_ForAHandWrittenTimetableThatWasNeverDownloaded()
    {
        // No fetch stamp and no covering week: there is nothing to refresh it from.
        var handWritten = new Timetable("Sam", AlertOptions.Default,
            [new Lesson(DayOfWeek.Tuesday, new TimeOnly(9, 0), null, "Maths", null)]);

        Assert.True(TimetableFreshness.IsStale(handWritten, Wednesday));
    }

    [Fact]
    public void IsStale_HonoursAnExplicitMaxAge() =>
        Assert.True(TimetableFreshness.IsStale(Downloaded(Wednesday.AddHours(-2)), Wednesday, TimeSpan.FromHours(1)));
}
