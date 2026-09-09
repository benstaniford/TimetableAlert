namespace TimetableAlert.Core.Models;

/// <summary>A validated timetable: recurring weekly lessons, dated ones, or a mix of the two.</summary>
public sealed class Timetable
{
    /// <summary>Creates a timetable from already-validated lessons.</summary>
    /// <param name="student">Whose timetable this is.</param>
    /// <param name="alerts">Warning timings.</param>
    /// <param name="lessons">The lessons, in file order.</param>
    /// <param name="coversFrom">First date a downloaded timetable speaks for.</param>
    /// <param name="coversUntil">Last date a downloaded timetable speaks for, inclusive.</param>
    /// <param name="fetchedAt">When it was downloaded, which is what makes it go stale.</param>
    public Timetable(
        string? student,
        AlertOptions alerts,
        IReadOnlyList<Lesson> lessons,
        DateOnly? coversFrom = null,
        DateOnly? coversUntil = null,
        DateTimeOffset? fetchedAt = null)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(lessons);

        Student = student;
        Alerts = alerts;
        Lessons = lessons;
        CoversFrom = coversFrom;
        CoversUntil = coversUntil;
        FetchedAt = fetchedAt;
    }

    /// <summary>Whose timetable this is; shown in the About box and tray tooltip.</summary>
    public string? Student { get; }

    /// <summary>Warning timings for this timetable.</summary>
    public AlertOptions Alerts { get; }

    /// <summary>Every lesson, in file order.</summary>
    public IReadOnlyList<Lesson> Lessons { get; }

    /// <summary>First date this timetable speaks for, or null for an open-ended weekly pattern.</summary>
    public DateOnly? CoversFrom { get; }

    /// <summary>Last date this timetable speaks for, inclusive, or null for a weekly pattern.</summary>
    public DateOnly? CoversUntil { get; }

    /// <summary>When this timetable was downloaded, or null if it was not.</summary>
    public DateTimeOffset? FetchedAt { get; }

    /// <summary>An empty timetable, used before a file has been loaded.</summary>
    public static Timetable Empty { get; } = new(student: null, AlertOptions.Default, []);

    /// <summary>
    /// Whether this timetable still speaks for a given day. A weekly pattern always does; a
    /// downloaded week only does within the dates it was fetched for, which is what stops last
    /// week's lessons firing when a refresh has failed.
    /// </summary>
    public bool Covers(DateOnly date) =>
        (CoversFrom is not { } from || date >= from) && (CoversUntil is not { } until || date <= until);
}
