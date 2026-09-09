using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using TimetableAlert.Core.Diagnostics;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Core.Feed;

/// <summary>
/// Downloads a week of lessons from the school's calendar feed. The feed URL carries its own
/// token, so this needs no credentials — which is why the ordinary refresh never touches a
/// password. It also arrives de-duplicated and includes one-off personal entries, both of which
/// the REST API gets wrong.
/// </summary>
public static class TimetableFeed
{
    private static readonly HttpClientHandler Handler = new() { CheckCertificateRevocationList = true };

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Fetches the feed and builds the timetable for the week containing a date.</summary>
    /// <param name="feedUrl">The calendar feed URL, token and all.</param>
    /// <param name="options">Naming rules and teacher names.</param>
    /// <param name="weekOf">Any date in the wanted week.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="HttpRequestException">The feed could not be fetched.</exception>
    public static async Task<Timetable> FetchAsync(
        Uri feedUrl,
        FeedOptions options,
        DateOnly weekOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedUrl);
        ArgumentNullException.ThrowIfNull(options);

        Log.Info(string.Create(CultureInfo.InvariantCulture, $"Calendar: fetching {Log.Redact(feedUrl)} for the week of {weekOf:yyyy-MM-dd}"));

        var started = Stopwatch.GetTimestamp();
        string ics;
        try
        {
            ics = await Http.GetStringAsync(feedUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Error(
                string.Create(CultureInfo.InvariantCulture, $"Calendar: fetch of {Log.Redact(feedUrl)} failed after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms"),
                ex);
            throw;
        }

        Log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"Calendar: fetched {ics.Length} characters in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms"));

        return Build(ics, options, weekOf, DateTimeOffset.Now);
    }

    /// <summary>
    /// Builds a timetable from feed text already in hand. Separate from the download so the
    /// interesting half can be tested without a network.
    /// </summary>
    /// <param name="ics">The feed document.</param>
    /// <param name="options">Naming rules and teacher names.</param>
    /// <param name="weekOf">Any date in the wanted week.</param>
    /// <param name="fetchedAt">Stamped onto the timetable, and what later makes it stale.</param>
    /// <param name="timeZone">
    /// The zone the feed's UTC stamps are read in, defaulting to the machine's own. Named
    /// explicitly so tests do not shift with the clock of whatever runs them.
    /// </param>
    public static Timetable Build(
        string ics,
        FeedOptions options,
        DateOnly weekOf,
        DateTimeOffset fetchedAt,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var zone = timeZone ?? TimeZoneInfo.Local;
        var monday = MondayOf(weekOf);
        var sunday = monday.AddDays(6);

        var lessons = new List<Lesson>();
        var entries = IcsParser.Parse(ics);
        var outsideWeek = 0;
        var unnamed = 0;

        foreach (var entry in entries)
        {
            var start = TimeZoneInfo.ConvertTime(entry.StartsAt, zone).DateTime;
            var date = DateOnly.FromDateTime(start);
            if (date < monday || date > sunday)
            {
                outsideWeek++;
                continue;
            }

            var subject = SubjectNaming.Subject(entry.Summary, options);
            if (string.IsNullOrWhiteSpace(subject))
            {
                unnamed++;
                Log.Debug($"Calendar: dropped an entry with no subject: \"{entry.Summary}\"");
                continue;
            }

            var end = entry.EndsAt is { } finish
                ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(finish, zone).DateTime)
                : (TimeOnly?)null;
            lessons.Add(Lesson.OnDate(date, TimeOnly.FromDateTime(start), end, subject, TeacherFor(entry.CourseId, options)));
        }

        lessons.Sort(static (a, b) =>
        {
            var byDate = Nullable.Compare(a.Date, b.Date);
            return byDate != 0 ? byDate : a.Start.CompareTo(b.Start);
        });

        Log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"Calendar: {entries.Count} timed entries parsed, {lessons.Count} lessons kept for {monday:yyyy-MM-dd}..{sunday:yyyy-MM-dd} ({outsideWeek} outside the week, {unnamed} with no subject)"));

        if (Log.Level == LogLevel.Debug)
        {
            foreach (var lesson in lessons)
            {
                Log.Debug(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Calendar:   {lesson.Date:yyyy-MM-dd} {lesson.Start:HH:mm} {lesson.Subject}{(lesson.Teacher is null ? string.Empty : " (" + lesson.Teacher + ")")}"));
            }
        }

        return new Timetable(options.Student, options.Alerts, lessons, monday, sunday, fetchedAt);
    }

    /// <summary>The Monday of the week containing a date, weeks running Monday to Sunday.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static string? TeacherFor(string? courseId, FeedOptions options) =>
        courseId is not null && options.TeachersByCourseId.TryGetValue(courseId, out var teacher) && !string.IsNullOrWhiteSpace(teacher)
            ? teacher
            : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(Handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Add("User-Agent", "TimetableAlert");
        return client;
    }
}
