using System.Diagnostics.CodeAnalysis;

namespace TimetableAlert.Core.Models;

/// <summary>Raw shape of the timetable JSON, before validation.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Grouping the JSON DTOs with the file they describe keeps the wire format in one place")]
public sealed record TimetableFile(string? Student, TimetableFile.AlertsSection? Alerts, IReadOnlyList<TimetableFile.LessonSection>? Lessons)
{
    /// <summary>Raw "alerts" section; every field is optional.</summary>
    public sealed record AlertsSection(int? FirstWarningMinutes, int? FirstWarningSeconds, int? SecondWarningSeconds);

    /// <summary>Raw "lessons" entry.</summary>
    public sealed record LessonSection(string? Day, string? Start, string? End, string? Subject, string? Teacher);
}
