using System.Globalization;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Core;

/// <summary>
/// Builds the three lines of text shown on the banner. Kept here rather than in the WPF layer
/// so the phrasing can be unit tested.
/// </summary>
public static class AlertText
{
    /// <summary>The headline: the subject, shouted.</summary>
    public static string Subject(Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);
        return $"⏰ {lesson.Subject.ToUpper(CultureInfo.CurrentCulture)}";
    }

    /// <summary>
    /// How long until the lesson starts. Counts in seconds during the final minute and in
    /// minutes up to an hour; beyond that a countdown stops being useful, so it names the time
    /// and the day instead. The far-off wordings are only ever reached by the preview shown
    /// when a timetable is loaded — a real warning is always minutes away.
    /// </summary>
    public static string Timing(DateTime startsAt, DateTime now)
    {
        var remaining = startsAt - now;

        if (remaining <= TimeSpan.Zero)
        {
            return "is starting now";
        }

        if (remaining < TimeSpan.FromMinutes(2))
        {
            var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            return $"starts in {seconds} second{Plural(seconds)}";
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            return $"starts in {minutes} minute{Plural(minutes)}";
        }

        var at = startsAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        return (startsAt.Date - now.Date).Days switch
        {
            0 => $"starts at {at} today",
            1 => $"starts at {at} tomorrow",
            _ => $"starts at {at} on {startsAt.ToString("dddd", CultureInfo.CurrentCulture)}",
        };
    }

    /// <summary>The small print: when the lesson runs, and who takes it.</summary>
    public static string Detail(Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        var when = lesson.End is { } end
            ? $"{Format(lesson.Start)}–{Format(end)}"
            : Format(lesson.Start);

        return lesson.Teacher is null ? when : $"{when}  ·  {lesson.Teacher}";
    }

    /// <summary>Formats a time of day the way the banner and the menus show it.</summary>
    public static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
