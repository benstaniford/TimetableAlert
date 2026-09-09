using System.Globalization;
using System.Text.Json;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Core;

/// <summary>The outcome of reading a timetable file: either a timetable, or the reasons it was rejected.</summary>
/// <param name="Timetable">The parsed timetable, or null if validation failed.</param>
/// <param name="Errors">Human-readable problems, each naming the offending entry.</param>
public sealed record TimetableLoadResult(Timetable? Timetable, IReadOnlyList<string> Errors)
{
    /// <summary>True when a timetable was produced.</summary>
    public bool Success => Timetable is not null;

    /// <summary>The errors joined into a single message suitable for a dialog.</summary>
    public string ErrorSummary => string.Join(Environment.NewLine, Errors);
}

/// <summary>Reads and validates timetable JSON.</summary>
public static class TimetableLoader
{
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

    private static readonly Dictionary<string, DayOfWeek> DayNames = BuildDayNames();

    /// <summary>Reads and validates the timetable at <paramref name="path"/>.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be read.</exception>
    public static TimetableLoadResult LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ParseJson(File.ReadAllText(path));
    }

    /// <summary>Validates timetable JSON held in memory.</summary>
    public static TimetableLoadResult ParseJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        TimetableFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TimetableFile>(json, TimetableJson.Options);
        }
        catch (JsonException ex)
        {
            return new TimetableLoadResult(null, [$"The file is not valid JSON: {ex.Message}"]);
        }

        if (file is null)
        {
            return new TimetableLoadResult(null, ["The file is empty."]);
        }

        var errors = new List<string>();
        var alerts = ReadAlerts(file.Alerts, errors);
        var lessons = ReadLessons(file.Lessons, errors);

        return errors.Count > 0
            ? new TimetableLoadResult(null, errors)
            : new TimetableLoadResult(new Timetable(file.Student, alerts, lessons), []);
    }

    private static AlertOptions ReadAlerts(TimetableFile.AlertsSection? section, List<string> errors)
    {
        var defaults = AlertOptions.Default;
        if (section is null)
        {
            return defaults;
        }

        var firstMinutes = section.FirstWarningMinutes ?? defaults.FirstWarningMinutes;
        var firstSeconds = section.FirstWarningSeconds ?? defaults.FirstWarningSeconds;
        var secondSeconds = section.SecondWarningSeconds ?? defaults.SecondWarningSeconds;

        if (firstMinutes <= 0)
        {
            errors.Add("alerts.firstWarningMinutes must be greater than zero.");
        }

        if (firstSeconds <= 0)
        {
            errors.Add("alerts.firstWarningSeconds must be greater than zero.");
        }

        if (secondSeconds <= 0)
        {
            errors.Add("alerts.secondWarningSeconds must be greater than zero.");
        }

        if (firstMinutes * 60 <= secondSeconds)
        {
            errors.Add("alerts.firstWarningMinutes must be further ahead than alerts.secondWarningSeconds.");
        }

        return new AlertOptions(firstMinutes, firstSeconds, secondSeconds);
    }

    private static List<Lesson> ReadLessons(IReadOnlyList<TimetableFile.LessonSection>? sections, List<string> errors)
    {
        var lessons = new List<Lesson>();
        if (sections is null || sections.Count == 0)
        {
            errors.Add("The timetable contains no lessons.");
            return lessons;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var entry = sections[i];
            var label = string.Create(CultureInfo.InvariantCulture, $"lessons[{i}]");
            var errorsBefore = errors.Count;

            if (string.IsNullOrWhiteSpace(entry.Subject))
            {
                errors.Add($"{label}: subject is missing.");
            }

            if (!TryParseDay(entry.Day, out var day))
            {
                errors.Add($"{label}: '{entry.Day}' is not a day of the week.");
            }

            if (!TryParseTime(entry.Start, out var start))
            {
                errors.Add($"{label}: '{entry.Start}' is not a start time of the form HH:mm.");
            }

            TimeOnly? end = null;
            if (!string.IsNullOrWhiteSpace(entry.End))
            {
                if (TryParseTime(entry.End, out var parsedEnd))
                {
                    end = parsedEnd;
                }
                else
                {
                    errors.Add($"{label}: '{entry.End}' is not an end time of the form HH:mm.");
                }
            }

            if (errors.Count == errorsBefore)
            {
                var teacher = string.IsNullOrWhiteSpace(entry.Teacher) ? null : entry.Teacher.Trim();
                lessons.Add(new Lesson(day, start, end, entry.Subject!.Trim(), teacher));
            }
        }

        return lessons;
    }

    private static bool TryParseDay(string? value, out DayOfWeek day)
    {
        day = default;
        return !string.IsNullOrWhiteSpace(value) && DayNames.TryGetValue(value.Trim(), out day);
    }

    private static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(value)
            && TimeOnly.TryParseExact(value.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static Dictionary<string, DayOfWeek> BuildDayNames()
    {
        var names = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var full = day.ToString();
            names[full] = day;
            names[full[..3]] = day;
        }

        return names;
    }
}
