using TimetableAlert.Core.Models;

namespace TimetableAlert.Core;

/// <summary>Which of the two warnings an alert is.</summary>
public enum AlertPhase
{
    /// <summary>The early heads-up, shown a few minutes ahead and gone after a few seconds.</summary>
    Early,

    /// <summary>The final countdown, shown a minute ahead and held until the lesson starts.</summary>
    Imminent,
}

/// <summary>Identifies one warning for one lesson on one day, so it is only ever shown once.</summary>
/// <param name="Date">The date the lesson falls on.</param>
/// <param name="LessonIndex">Index into <see cref="Timetable.Lessons"/>.</param>
/// <param name="Phase">Which of the two warnings this is.</param>
public readonly record struct AlertKey(DateOnly Date, int LessonIndex, AlertPhase Phase);

/// <summary>A lesson pinned to a particular date and time.</summary>
/// <param name="Lesson">The lesson itself.</param>
/// <param name="LessonIndex">Index into <see cref="Timetable.Lessons"/>.</param>
/// <param name="StartsAt">Local date and time the lesson starts.</param>
public sealed record LessonOccurrence(Lesson Lesson, int LessonIndex, DateTime StartsAt)
{
    /// <summary>Local date and time the lesson ends, when the timetable gave an end time.</summary>
    public DateTime? EndsAt => Lesson.End is { } end ? StartsAt.Date.Add(end.ToTimeSpan()) : null;
}

/// <summary>A warning that is due to be shown.</summary>
/// <param name="Occurrence">The lesson it is about.</param>
/// <param name="Phase">Which of the two warnings it is.</param>
/// <param name="Key">The key to record so it is not shown twice.</param>
public sealed record AlertDecision(LessonOccurrence Occurrence, AlertPhase Phase, AlertKey Key);

/// <summary>
/// Turns a weekly timetable into concrete alerts. Deliberately free of UI and of the system
/// clock: every method takes "now", which is what makes the firing rules testable.
/// </summary>
public sealed class AlertSchedule
{
    private const int DaysToLookAhead = 8;

    private readonly Timetable _timetable;
    private readonly Dictionary<DayOfWeek, List<(Lesson Lesson, int Index)>> _recurringByDay;
    private readonly Dictionary<DateOnly, List<(Lesson Lesson, int Index)>> _datedByDate;

    /// <summary>Builds a schedule over the given timetable.</summary>
    public AlertSchedule(Timetable timetable)
    {
        ArgumentNullException.ThrowIfNull(timetable);

        _timetable = timetable;
        _recurringByDay = [];
        _datedByDate = [];

        for (var i = 0; i < timetable.Lessons.Count; i++)
        {
            var lesson = timetable.Lessons[i];
            if (lesson.Date is { } date)
            {
                Add(_datedByDate, date, lesson, i);
            }
            else
            {
                Add(_recurringByDay, lesson.Day, lesson, i);
            }
        }

        foreach (var list in _recurringByDay.Values)
        {
            list.Sort(static (a, b) => a.Lesson.Start.CompareTo(b.Lesson.Start));
        }

        foreach (var list in _datedByDate.Values)
        {
            list.Sort(static (a, b) => a.Lesson.Start.CompareTo(b.Lesson.Start));
        }
    }

    /// <summary>The timetable this schedule was built from.</summary>
    public Timetable Timetable => _timetable;

    /// <summary>The weekly-recurring lessons on a given weekday, earliest first.</summary>
    public IReadOnlyList<Lesson> LessonsOn(DayOfWeek day) =>
        _recurringByDay.TryGetValue(day, out var list) ? list.ConvertAll(static entry => entry.Lesson) : [];

    /// <summary>
    /// Every lesson falling on a given date, earliest first: those pinned to the date and those
    /// recurring on its weekday. This is the one to ask when a real day is in hand, because a
    /// downloaded timetable holds no recurring lessons at all.
    /// </summary>
    public IReadOnlyList<Lesson> LessonsOn(DateOnly date)
    {
        var occurring = Occurring(date);
        var lessons = new List<Lesson>(occurring.Count);
        foreach (var entry in occurring)
        {
            lessons.Add(entry.Lesson);
        }

        return lessons;
    }

    private static void Add<TKey>(Dictionary<TKey, List<(Lesson Lesson, int Index)>> map, TKey key, Lesson lesson, int index)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add((lesson, index));
    }

    /// <summary>
    /// The lessons on one date, merged from the dated and recurring sides. Timetables are in
    /// practice all of one kind or all of the other, so the merging path is rare and the common
    /// case hands back the already-sorted list without copying it.
    /// </summary>
    private IReadOnlyList<(Lesson Lesson, int Index)> Occurring(DateOnly date)
    {
        var hasDated = _datedByDate.TryGetValue(date, out var dated);
        var hasRecurring = _recurringByDay.TryGetValue(date.DayOfWeek, out var recurring);

        if (!hasDated)
        {
            return hasRecurring ? recurring! : [];
        }

        if (!hasRecurring)
        {
            return dated!;
        }

        var merged = new List<(Lesson Lesson, int Index)>(dated!.Count + recurring!.Count);
        merged.AddRange(dated);
        merged.AddRange(recurring);
        merged.Sort(static (a, b) => a.Lesson.Start.CompareTo(b.Lesson.Start));
        return merged;
    }

    /// <summary>
    /// The next lesson at or after <paramref name="now"/>, looking up to a week ahead so that a
    /// Friday afternoon rolls round to Monday morning. Null when the timetable has no lessons.
    /// </summary>
    public LessonOccurrence? NextAfter(DateTime now)
    {
        for (var dayOffset = 0; dayOffset < DaysToLookAhead; dayOffset++)
        {
            var date = now.Date.AddDays(dayOffset);
            foreach (var (lesson, index) in Occurring(DateOnly.FromDateTime(date)))
            {
                var startsAt = date.Add(lesson.Start.ToTimeSpan());
                if (startsAt >= now)
                {
                    return new LessonOccurrence(lesson, index, startsAt);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Decides whether a warning is due right now, given the ones already shown.
    /// </summary>
    /// <remarks>
    /// The rule is expressed as a window rather than a threshold crossing so that a machine
    /// waking from sleep part-way through the run-up still gets the warning that is currently
    /// relevant, instead of either a burst of stale ones or nothing at all. Waking at T-30s
    /// therefore shows only the countdown; waking after the lesson has begun shows nothing.
    /// </remarks>
    /// <param name="now">The current local time.</param>
    /// <param name="alreadyFired">Keys of warnings already shown; not modified by this call.</param>
    /// <returns>The warning to show, or null if none is due.</returns>
    public AlertDecision? Evaluate(DateTime now, IReadOnlySet<AlertKey> alreadyFired)
    {
        ArgumentNullException.ThrowIfNull(alreadyFired);

        var next = NextAfter(now);
        if (next is null)
        {
            return null;
        }

        var secondsAway = (next.StartsAt - now).TotalSeconds;
        var alerts = _timetable.Alerts;

        var phase = secondsAway <= alerts.SecondWarningSeconds ? AlertPhase.Imminent
            : secondsAway <= alerts.FirstWarningLeadSeconds ? AlertPhase.Early
            : (AlertPhase?)null;

        if (phase is null)
        {
            return null;
        }

        var key = new AlertKey(DateOnly.FromDateTime(next.StartsAt), next.LessonIndex, phase.Value);
        return alreadyFired.Contains(key) ? null : new AlertDecision(next, phase.Value, key);
    }
}
