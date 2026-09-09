using TimetableAlert.Core.Feed;
using Xunit;

namespace TimetableAlert.Tests;

public class IcsParserTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine("Fixtures", "canvas-week.ics"));

    [Fact]
    public void Parse_SkipsAllDayEntries()
    {
        // The fixture holds four timed events and one all-day assignment due date.
        var events = IcsParser.Parse(Fixture());

        Assert.Equal(4, events.Count);
        Assert.DoesNotContain(events, e => e.Summary.Contains("homework", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ReadsTimesAsUtc()
    {
        var first = IcsParser.Parse(Fixture())[0];

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 7, 30, 0, TimeSpan.Zero), first.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero), first.EndsAt!.Value);
        Assert.Equal("Whole School Assembly", first.Summary);
    }

    [Fact]
    public void Parse_UnfoldsWrappedLinesToRecoverTheCourseId()
    {
        // Every URL in the fixture is folded across two lines, mid-way through "course_NNNN".
        var courses = IcsParser.Parse(Fixture()).Select(e => e.CourseId).ToArray();

        Assert.Equal(new[] { "3575", "3021", "3078", "2795" }, courses);
    }

    [Fact]
    public void Parse_UnescapesTheSummary()
    {
        var ics = """
            BEGIN:VEVENT
            DTSTART:20260908T080000Z
            SUMMARY:Maths\, Higher\; Set 1
            END:VEVENT
            """;

        Assert.Equal("Maths, Higher; Set 1", IcsParser.Parse(ics)[0].Summary);
    }

    [Fact]
    public void Parse_IsEmptyForRubbish()
    {
        Assert.Empty(IcsParser.Parse(string.Empty));
        Assert.Empty(IcsParser.Parse("not a calendar at all"));
    }
}
