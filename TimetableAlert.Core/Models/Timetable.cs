namespace TimetableAlert.Core.Models;

/// <summary>A validated weekly timetable.</summary>
public sealed class Timetable
{
    /// <summary>Creates a timetable from already-validated lessons.</summary>
    public Timetable(string? student, AlertOptions alerts, IReadOnlyList<Lesson> lessons)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(lessons);

        Student = student;
        Alerts = alerts;
        Lessons = lessons;
    }

    /// <summary>Whose timetable this is; shown in the About box and tray tooltip.</summary>
    public string? Student { get; }

    /// <summary>Warning timings for this timetable.</summary>
    public AlertOptions Alerts { get; }

    /// <summary>Every lesson in the week, in file order.</summary>
    public IReadOnlyList<Lesson> Lessons { get; }

    /// <summary>An empty timetable, used before a file has been loaded.</summary>
    public static Timetable Empty { get; } = new(student: null, AlertOptions.Default, []);
}
