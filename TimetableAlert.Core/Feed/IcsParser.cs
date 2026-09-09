using System.Globalization;
using System.Text.RegularExpressions;

namespace TimetableAlert.Core.Feed;

/// <summary>One timed entry read out of an iCalendar feed.</summary>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it ends, if the feed said.</param>
/// <param name="Summary">The title, as written in the feed.</param>
/// <param name="CourseId">The Canvas course the entry belongs to, if its URL named one.</param>
public sealed record CalendarEvent(DateTimeOffset StartsAt, DateTimeOffset? EndsAt, string Summary, string? CourseId);

/// <summary>
/// Reads the timed events out of an iCalendar document. Deliberately small: this handles the
/// shape Canvas actually emits rather than the whole of RFC 5545.
/// </summary>
public static partial class IcsParser
{
    private const string UtcStampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>
    /// Parses a feed. All-day entries are skipped: they come through as a bare
    /// <c>DTSTART;VALUE=DATE</c>, which is how Canvas represents assignment due dates, and they
    /// are not lessons.
    /// </summary>
    public static IReadOnlyList<CalendarEvent> Parse(string ics)
    {
        var events = new List<CalendarEvent>();
        if (string.IsNullOrWhiteSpace(ics))
        {
            return events;
        }

        foreach (var block in Unfold(ics).Split("BEGIN:VEVENT", StringSplitOptions.None).Skip(1))
        {
            var body = block.Split("END:VEVENT", StringSplitOptions.None)[0];
            var fields = ReadFields(body);

            if (!fields.TryGetValue("DTSTART", out var start) || !TryParseUtc(start, out var startsAt))
            {
                continue;
            }

            DateTimeOffset? endsAt = fields.TryGetValue("DTEND", out var end) && TryParseUtc(end, out var parsedEnd)
                ? parsedEnd
                : null;

            fields.TryGetValue("SUMMARY", out var summary);
            fields.TryGetValue("URL", out var url);

            events.Add(new CalendarEvent(startsAt, endsAt, Unescape(summary ?? string.Empty), CourseIdOf(url)));
        }

        return events;
    }

    /// <summary>
    /// Undoes RFC 5545 line folding, where a long value is continued on the next line behind a
    /// single space or tab. Without this a wrapped SUMMARY or URL arrives in pieces.
    /// </summary>
    private static string Unfold(string ics) =>
        ics.Replace("\r\n", "\n", StringComparison.Ordinal)
           .Replace("\n ", string.Empty, StringComparison.Ordinal)
           .Replace("\n\t", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Splits a VEVENT body into its properties, keeping the first of any repeated one. Property
    /// parameters are dropped: "DTSTART;TZID=..." is filed under "DTSTART".
    /// </summary>
    private static Dictionary<string, string> ReadFields(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in body.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon];
            var semicolon = name.IndexOf(';', StringComparison.Ordinal);
            if (semicolon >= 0)
            {
                name = name[..semicolon];
            }

            var value = line[(colon + 1)..].TrimEnd('\r');
            if (!fields.ContainsKey(name.Trim()))
            {
                fields[name.Trim()] = value;
            }
        }

        return fields;
    }

    /// <summary>
    /// Accepts only a UTC stamp of the form 20260908T080000Z. Anything else — most importantly a
    /// bare date — is not a timed event and is left alone.
    /// </summary>
    private static bool TryParseUtc(string value, out DateTimeOffset when) =>
        DateTimeOffset.TryParseExact(
            value.Trim(),
            UtcStampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out when);

    private static string Unescape(string value) =>
        value.Replace("\\n", "\n", StringComparison.Ordinal)
             .Replace("\\,", ",", StringComparison.Ordinal)
             .Replace("\\;", ";", StringComparison.Ordinal)
             .Replace("\\\\", "\\", StringComparison.Ordinal)
             .Trim();

    /// <summary>Canvas names the owning course in the event's URL, as include_contexts=course_123.</summary>
    private static string? CourseIdOf(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var match = CourseReference().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"course_(\d+)")]
    private static partial Regex CourseReference();
}
