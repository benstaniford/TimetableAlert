using TimetableAlert.Core.Models;

namespace TimetableAlert.Core.Feed;

/// <summary>
/// Everything the downloader needs beyond the feed itself: whose timetable it is, how the school's
/// calendar titles turn into subject names, and who teaches what. The feed carries no teacher
/// names, which is why they are held here and refreshed separately.
/// </summary>
public sealed class FeedOptions
{
    /// <summary>Teaching-set names and filler that mean nothing on a banner.</summary>
    private static readonly string[] DefaultDropWords =
    [
        "Core", "Session", "Separate", "Science",
        "Apollo", "Ceres", "Fortuna", "Juno", "Jupiter", "Luna", "Mars", "Minerva", "Saturn", "Terra", "Vulcan",
    ];

    /// <summary>Calendar titles the general rules cannot tidy into anything sensible.</summary>
    private static readonly Dictionary<string, string> DefaultSubjectOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORIENTATION - Meet Your Head of Year"] = "Head of Year",
    };

    /// <summary>Whose timetable this is; copied onto the downloaded <see cref="Timetable"/>.</summary>
    public string? Student { get; init; }

    /// <summary>Warning timings to give the downloaded timetable.</summary>
    public AlertOptions Alerts { get; init; } = AlertOptions.Default;

    /// <summary>Trailing words stripped off a calendar title when working out the subject.</summary>
    public IReadOnlyList<string> DropWords { get; init; } = DefaultDropWords;

    /// <summary>Exact calendar titles mapped straight to a subject, for the ones the rules cannot tidy.</summary>
    public IReadOnlyDictionary<string, string> SubjectOverrides { get; init; } = DefaultSubjectOverrides;

    /// <summary>Canvas course id to teacher name.</summary>
    public IReadOnlyDictionary<string, string> TeachersByCourseId { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The stock options: the default drop words, no overrides and no teachers.</summary>
    public static FeedOptions Default { get; } = new();
}
