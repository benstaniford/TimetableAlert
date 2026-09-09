using TimetableAlert.Core.Feed;
using Xunit;

namespace TimetableAlert.Tests;

public class SubjectNamingTests
{
    private static readonly FeedOptions Options = new()
    {
        SubjectOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ORIENTATION - Meet Your Head of Year"] = "Head of Year",
        },
    };

    [Theory]
    [InlineData("Y10 Maths Higher Core Luna [10MaHLun]", "Maths Higher")]
    [InlineData("Y10 Biology Terra", "Biology")]
    [InlineData("Y10 Classical Civilisation Apollo", "Classical Civilisation")]
    [InlineData("Whole School Assembly", "Assembly")]
    [InlineData("Y10 Assembly", "Assembly")]
    [InlineData("Y10 Social Room", "Social Room")]
    [InlineData("Y10 Personal Development Session", "Personal Development")]
    [InlineData("Year 11 Geography Ceres", "Geography")]
    public void Subject_TidiesACalendarTitle(string title, string expected) =>
        Assert.Equal(expected, SubjectNaming.Subject(title, Options));

    [Fact]
    public void Subject_PrefersAnExactOverride() =>
        Assert.Equal("Head of Year", SubjectNaming.Subject("ORIENTATION - Meet Your Head of Year [10PDvApo]", Options));

    [Fact]
    public void Subject_NeverStripsTheLastWord() =>
        // Otherwise a lesson whose title is nothing but a set name would come out blank.
        Assert.Equal("Luna", SubjectNaming.Subject("Y10 Luna", Options));

    [Fact]
    public void Subject_LeavesAnUnadornedTitleAlone() =>
        Assert.Equal("Mentor Session: Sam & Alex", SubjectNaming.Subject("Mentor Session: Sam & Alex", FeedOptions.Default));
}
