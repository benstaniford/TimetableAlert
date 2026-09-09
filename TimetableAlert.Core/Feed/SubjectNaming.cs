using System.Text.RegularExpressions;

namespace TimetableAlert.Core.Feed;

/// <summary>
/// Turns a school calendar title into the subject name that goes on the banner:
/// "Y10 Maths Higher Core Luna [10MaHLun]" becomes "Maths Higher".
/// </summary>
public static partial class SubjectNaming
{
    /// <summary>
    /// Tidies one calendar title. The trailing course code always comes off first; an exact
    /// override then wins outright, and failing that the year-group prefix and any trailing
    /// drop words are removed. The last word is never removed, so a title made entirely of
    /// drop words still names something.
    /// </summary>
    public static string Subject(string title, FeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(options);

        var tidied = CourseCodeSuffix().Replace(title, string.Empty).Trim();

        if (options.SubjectOverrides.TryGetValue(tidied, out var mapped))
        {
            return mapped;
        }

        tidied = YearGroupPrefix().Replace(tidied, string.Empty).Trim();

        var words = tidied.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var drop = new HashSet<string>(options.DropWords, StringComparer.OrdinalIgnoreCase);

        var keep = words.Length;
        while (keep > 1 && drop.Contains(words[keep - 1]))
        {
            keep--;
        }

        return string.Join(' ', words, 0, keep);
    }

    [GeneratedRegex(@"\s*\[[^\]]*\]\s*$")]
    private static partial Regex CourseCodeSuffix();

    [GeneratedRegex(@"^(Y\d+|Year \d+|Whole School)\s+")]
    private static partial Regex YearGroupPrefix();
}
