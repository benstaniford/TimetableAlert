namespace TimetableAlert.Core.Models;

/// <summary>A single validated timetable entry.</summary>
/// <param name="Day">Day of the week the lesson runs on.</param>
/// <param name="Start">Local start time.</param>
/// <param name="End">Local end time, if the timetable supplied one.</param>
/// <param name="Subject">Subject name, e.g. "Maths Higher".</param>
/// <param name="Teacher">Teacher name, if the timetable supplied one.</param>
/// <param name="Date">
/// The one date this lesson falls on, for a timetable downloaded from the school calendar. Null
/// means the lesson simply recurs every week on <paramref name="Day"/>, which is how hand-written
/// timetables work. Keeping both lets a downloaded week say "the orientation happens once, on the
/// 7th" while an edited file still says "Maths, every Tuesday".
/// </param>
public sealed record Lesson(
    DayOfWeek Day,
    TimeOnly Start,
    TimeOnly? End,
    string Subject,
    string? Teacher,
    DateOnly? Date = null)
{
    /// <summary>True when this lesson happens once, on <see cref="Date"/>, rather than every week.</summary>
    public bool IsDated => Date is not null;

    /// <summary>Creates a lesson pinned to one date, taking its day of the week from that date.</summary>
    public static Lesson OnDate(DateOnly date, TimeOnly start, TimeOnly? end, string subject, string? teacher) =>
        new(date.DayOfWeek, start, end, subject, teacher, date);
}
