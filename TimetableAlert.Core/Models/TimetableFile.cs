using System.Diagnostics.CodeAnalysis;

namespace TimetableAlert.Core.Models;

/// <summary>Raw shape of the timetable JSON, before validation.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Grouping the JSON DTOs with the file they describe keeps the wire format in one place")]
public sealed record TimetableFile(
    string? Student,
    TimetableFile.AlertsSection? Alerts,
    IReadOnlyList<TimetableFile.LessonSection>? Lessons,
    string? CoversFrom = null,
    string? CoversUntil = null,
    DateTimeOffset? FetchedAt = null)
{
    /// <summary>Raw "alerts" section; every field is optional.</summary>
    public sealed record AlertsSection(int? FirstWarningMinutes, int? FirstWarningSeconds, int? SecondWarningSeconds);

    /// <summary>Raw "lessons" entry. A lesson needs a "day" or a "date"; "date" wins if both are given.</summary>
    public sealed record LessonSection(string? Day, string? Start, string? End, string? Subject, string? Teacher, string? Date = null);
}
