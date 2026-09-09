using TimetableAlert.Core;
using TimetableAlert.Core.Models;
using Xunit;

namespace TimetableAlert.Tests;

public class AlertScheduleTests
{
    // 7 September 2026 is a Monday; 11 September is the Friday of the same week.
    private static readonly DateTime Monday = new(2026, 9, 7);
    private static readonly DateTime Friday = new(2026, 9, 11);

    private static AlertSchedule OneMondayLesson() => Build(
        new Lesson(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), "Maths Higher", "Bailey Bravo"));

    private static AlertSchedule Build(params Lesson[] lessons) =>
        new(new Timetable("Sam", AlertOptions.Default, lessons));

    [Fact]
    public void LessonsOn_OrdersTheDayByStartTime()
    {
        var schedule = Build(
            new Lesson(DayOfWeek.Tuesday, new TimeOnly(14, 0), null, "Biology", null),
            new Lesson(DayOfWeek.Tuesday, new TimeOnly(9, 0), null, "Maths Higher", null),
            new Lesson(DayOfWeek.Tuesday, new TimeOnly(11, 0), null, "English Literature", null));

        var order = schedule.LessonsOn(DayOfWeek.Tuesday).Select(lesson => lesson.Subject).ToArray();

        Assert.Equal(new[] { "Maths Higher", "English Literature", "Biology" }, order);
    }

    [Fact]
    public void NextAfter_FindsTheNextLessonLaterTheSameDay()
    {
        var schedule = Build(
            new Lesson(DayOfWeek.Monday, new TimeOnly(9, 0), null, "Maths Higher", null),
            new Lesson(DayOfWeek.Monday, new TimeOnly(13, 0), null, "Wellbeing", null));

        var next = schedule.NextAfter(Monday.AddHours(10));

        Assert.Equal("Wellbeing", next!.Lesson.Subject);
        Assert.Equal(Monday.AddHours(13), next.StartsAt);
    }

    [Fact]
    public void NextAfter_RollsFromFridayRoundToMonday()
    {
        var schedule = OneMondayLesson();

        var next = schedule.NextAfter(Friday.AddHours(16));

        Assert.Equal("Maths Higher", next!.Lesson.Subject);
        Assert.Equal(new DateTime(2026, 9, 14, 9, 0, 0), next.StartsAt);
    }

    [Fact]
    public void NextAfter_IncludesALessonStartingThisVerySecond()
    {
        var next = OneMondayLesson().NextAfter(Monday.AddHours(9));

        Assert.Equal(Monday.AddHours(9), next!.StartsAt);
    }

    [Fact]
    public void NextAfter_IsNullForAnEmptyTimetable()
    {
        var schedule = new AlertSchedule(Timetable.Empty);

        Assert.Null(schedule.NextAfter(Monday));
        Assert.Null(schedule.Evaluate(Monday, new HashSet<AlertKey>()));
    }

    [Fact]
    public void EndsAt_UsesTheLessonsOwnDate()
    {
        var next = OneMondayLesson().NextAfter(Monday);

        Assert.Equal(Monday.AddHours(10), next!.EndsAt);
    }

    [Fact]
    public void Evaluate_IsQuietWhenTheLessonIsStillFarOff()
    {
        // Eight minutes out, one minute before the seven-minute warning is due.
        var decision = OneMondayLesson().Evaluate(Monday.AddHours(9).AddMinutes(-8), new HashSet<AlertKey>());

        Assert.Null(decision);
    }

    [Fact]
    public void Evaluate_RaisesTheEarlyWarningSevenMinutesOut()
    {
        var decision = OneMondayLesson().Evaluate(Monday.AddHours(9).AddMinutes(-7), new HashSet<AlertKey>());

        Assert.NotNull(decision);
        Assert.Equal(AlertPhase.Early, decision.Phase);
        Assert.Equal("Maths Higher", decision.Occurrence.Lesson.Subject);
        Assert.Equal(new DateOnly(2026, 9, 7), decision.Key.Date);
    }

    [Fact]
    public void Evaluate_RaisesTheEarlyWarningOnlyOnce()
    {
        var schedule = OneMondayLesson();
        var now = Monday.AddHours(9).AddMinutes(-7);
        var fired = new HashSet<AlertKey>();

        var first = schedule.Evaluate(now, fired);
        Assert.NotNull(first);
        fired.Add(first.Key);

        Assert.Null(schedule.Evaluate(now.AddSeconds(1), fired));
        Assert.Null(schedule.Evaluate(now.AddMinutes(3), fired));
    }

    [Fact]
    public void Evaluate_RaisesTheCountdownAtSixtySeconds()
    {
        var schedule = OneMondayLesson();
        var fired = new HashSet<AlertKey> { new(new DateOnly(2026, 9, 7), 0, AlertPhase.Early) };

        var decision = schedule.Evaluate(Monday.AddHours(9).AddSeconds(-60), fired);

        Assert.NotNull(decision);
        Assert.Equal(AlertPhase.Imminent, decision.Phase);
    }

    [Fact]
    public void Evaluate_RaisesTheCountdownOnlyOnce()
    {
        var schedule = OneMondayLesson();
        var fired = new HashSet<AlertKey>();
        var start = Monday.AddHours(9);

        var decision = schedule.Evaluate(start.AddSeconds(-60), fired);
        Assert.Equal(AlertPhase.Imminent, decision!.Phase);
        fired.Add(decision.Key);

        Assert.Null(schedule.Evaluate(start.AddSeconds(-30), fired));
        Assert.Null(schedule.Evaluate(start, fired));
    }

    [Fact]
    public void Evaluate_AfterWakingMidRunUpGivesOnlyTheCountdown()
    {
        // The machine was asleep through the seven-minute mark and wakes at T-30s.
        var decision = OneMondayLesson().Evaluate(Monday.AddHours(9).AddSeconds(-30), new HashSet<AlertKey>());

        Assert.NotNull(decision);
        Assert.Equal(AlertPhase.Imminent, decision.Phase);
    }

    [Fact]
    public void Evaluate_AfterWakingLateInTheRunUpStillGivesTheEarlyWarning()
    {
        // Asleep through T-7min, awake at T-4min: better a late heads-up than none.
        var decision = OneMondayLesson().Evaluate(Monday.AddHours(9).AddMinutes(-4), new HashSet<AlertKey>());

        Assert.NotNull(decision);
        Assert.Equal(AlertPhase.Early, decision.Phase);
    }

    [Fact]
    public void Evaluate_SaysNothingOnceTheLessonHasBegun()
    {
        var decision = OneMondayLesson().Evaluate(Monday.AddHours(9).AddSeconds(1), new HashSet<AlertKey>());

        Assert.Null(decision);
    }

    [Fact]
    public void Evaluate_TreatsTheSameLessonNextWeekAsANewAlert()
    {
        var schedule = OneMondayLesson();
        var fired = new HashSet<AlertKey> { new(new DateOnly(2026, 9, 7), 0, AlertPhase.Early) };

        var decision = schedule.Evaluate(new DateTime(2026, 9, 14, 8, 53, 0), fired);

        Assert.NotNull(decision);
        Assert.Equal(AlertPhase.Early, decision.Phase);
        Assert.Equal(new DateOnly(2026, 9, 14), decision.Key.Date);
    }

    [Fact]
    public void Evaluate_MovesOnToTheNextLessonOfTheDay()
    {
        var schedule = Build(
            new Lesson(DayOfWeek.Wednesday, new TimeOnly(12, 30), new TimeOnly(13, 0), "Social Room", "Georgie Golf"),
            new Lesson(DayOfWeek.Wednesday, new TimeOnly(13, 0), new TimeOnly(14, 0), "Chemistry", "Indy India"));

        var wednesday = new DateTime(2026, 9, 9, 12, 53, 0);
        var decision = schedule.Evaluate(wednesday, new HashSet<AlertKey>());

        Assert.Equal("Chemistry", decision!.Occurrence.Lesson.Subject);
        Assert.Equal(1, decision.Key.LessonIndex);
    }

    [Fact]
    public void Evaluate_HonoursCustomAlertTimings()
    {
        var timetable = new Timetable(
            "Sam",
            new AlertOptions(FirstWarningMinutes: 15, FirstWarningSeconds: 5, SecondWarningSeconds: 120),
            [new Lesson(DayOfWeek.Monday, new TimeOnly(9, 0), null, "Maths Higher", null)]);
        var schedule = new AlertSchedule(timetable);
        var start = Monday.AddHours(9);

        Assert.Null(schedule.Evaluate(start.AddMinutes(-16), new HashSet<AlertKey>()));
        Assert.Equal(AlertPhase.Early, schedule.Evaluate(start.AddMinutes(-15), new HashSet<AlertKey>())!.Phase);
        Assert.Equal(AlertPhase.Imminent, schedule.Evaluate(start.AddSeconds(-120), new HashSet<AlertKey>())!.Phase);
    }
}
