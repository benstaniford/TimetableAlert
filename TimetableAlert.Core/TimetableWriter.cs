using System.Globalization;
using System.Text.Json;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Core;

/// <summary>
/// Writes a timetable back out in the same format <see cref="TimetableLoader"/> reads, which is
/// what lets a downloaded week be cached on disk and re-read at the next logon with no special
/// handling.
/// </summary>
public static class TimetableWriter
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Renders a timetable as JSON.</summary>
    public static string ToJson(Timetable timetable)
    {
        ArgumentNullException.ThrowIfNull(timetable);

        var lessons = new List<TimetableFile.LessonSection>(timetable.Lessons.Count);
        foreach (var lesson in timetable.Lessons)
        {
            // A dated lesson writes only its date; the day of the week is implied by it, and
            // writing both would just be a chance for the two to disagree.
            lessons.Add(new TimetableFile.LessonSection(
                Day: lesson.IsDated ? null : lesson.Day.ToString(),
                Start: lesson.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                End: lesson.End?.ToString("HH:mm", CultureInfo.InvariantCulture),
                Subject: lesson.Subject,
                Teacher: lesson.Teacher,
                Date: lesson.Date?.ToString(DateFormat, CultureInfo.InvariantCulture)));
        }

        var file = new TimetableFile(
            timetable.Student,
            new TimetableFile.AlertsSection(
                timetable.Alerts.FirstWarningMinutes,
                timetable.Alerts.FirstWarningSeconds,
                timetable.Alerts.SecondWarningSeconds),
            lessons,
            timetable.CoversFrom?.ToString(DateFormat, CultureInfo.InvariantCulture),
            timetable.CoversUntil?.ToString(DateFormat, CultureInfo.InvariantCulture),
            timetable.FetchedAt);

        return JsonSerializer.Serialize(file, TimetableWriteJsonContext.Default.TimetableFile);
    }
}
