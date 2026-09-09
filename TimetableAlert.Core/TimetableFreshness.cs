using TimetableAlert.Core.Models;

namespace TimetableAlert.Core;

/// <summary>When a downloaded timetable should be fetched again.</summary>
public static class TimetableFreshness
{
    /// <summary>
    /// How old a downloaded copy may be before it is fetched again. Twice a day is enough to
    /// notice a lesson the school has moved, without asking the feed for the same answer over
    /// and over.
    /// </summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Whether a cached timetable should be downloaded again: when there is none, when it never
    /// came from a download in the first place, when it has aged past <paramref name="maxAge"/>,
    /// or when the week it speaks for no longer includes today. That last one matters most — it
    /// is what stops a stale copy quietly replaying last week's lessons.
    /// </summary>
    /// <param name="cached">The timetable in hand, if any.</param>
    /// <param name="now">The current local time.</param>
    /// <param name="maxAge">How old a copy may get; <see cref="MaxAge"/> when omitted.</param>
    public static bool IsStale(Timetable? cached, DateTime now, TimeSpan? maxAge = null) =>
        cached is null
        || cached.FetchedAt is not { } fetchedAt
        || now - fetchedAt.LocalDateTime >= (maxAge ?? MaxAge)
        || !cached.Covers(DateOnly.FromDateTime(now));
}
