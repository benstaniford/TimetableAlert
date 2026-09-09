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

    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

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
        var coversFrom = ReadCoveringDate(file.CoversFrom, "coversFrom", errors);
        var coversUntil = ReadCoveringDate(file.CoversUntil, "coversUntil", errors);

        if (coversFrom is { } from && coversUntil is { } until && until < from)
        {
            errors.Add("coversUntil is before coversFrom.");
        }

        return errors.Count > 0
            ? new TimetableLoadResult(null, errors)
            : new TimetableLoadResult(
                new Timetable(file.Student, alerts, lessons, coversFrom, coversUntil, file.FetchedAt),
                []);
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

            var day = ReadWhen(entry, label, errors, out var date);

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
                lessons.Add(new Lesson(day, start, end, entry.Subject!.Trim(), teacher, date));
            }
        }

        return lessons;
    }

    /// <summary>
    /// Works out when a lesson happens. A "date" pins it to one day, which is what a downloaded
    /// week gives; a "day" alone makes it recur weekly, which is what hand-written files give.
    /// One or the other is required, and if both are present they have to agree.
    /// </summary>
    private static DayOfWeek ReadWhen(TimetableFile.LessonSection entry, string label, List<string> errors, out DateOnly? date)
    {
        date = null;
        var hasDay = !string.IsNullOrWhiteSpace(entry.Day);
        var hasDate = !string.IsNullOrWhiteSpace(entry.Date);

        if (!hasDay && !hasDate)
        {
            errors.Add($"{label}: needs a day of the week or a date.");
            return default;
        }

        var day = default(DayOfWeek);
        if (hasDay && !TryParseDay(entry.Day, out day))
        {
            errors.Add($"{label}: '{entry.Day}' is not a day of the week.");
        }

        if (!hasDate)
        {
            return day;
        }

        if (!TryParseDate(entry.Date, out var parsed))
        {
            errors.Add($"{label}: '{entry.Date}' is not a date of the form yyyy-MM-dd.");
            return day;
        }

        if (hasDay && day != parsed.DayOfWeek)
        {
            errors.Add($"{label}: {entry.Date} is a {parsed.DayOfWeek}, not a {entry.Day!.Trim()}.");
            return day;
        }

        date = parsed;
        return parsed.DayOfWeek;
    }

    private static DateOnly? ReadCoveringDate(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TryParseDate(value, out var parsed))
        {
            return parsed;
        }

        errors.Add($"{label}: '{value}' is not a date of the form yyyy-MM-dd.");
        return null;
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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
