namespace TimetableAlert.Core.Models;

/// <summary>A single validated timetable entry.</summary>
/// <param name="Day">Day of the week the lesson runs on.</param>
/// <param name="Start">Local start time.</param>
/// <param name="End">Local end time, if the timetable supplied one.</param>
/// <param name="Subject">Subject name, e.g. "Maths Higher".</param>
/// <param name="Teacher">Teacher name, if the timetable supplied one.</param>
public sealed record Lesson(DayOfWeek Day, TimeOnly Start, TimeOnly? End, string Subject, string? Teacher);
