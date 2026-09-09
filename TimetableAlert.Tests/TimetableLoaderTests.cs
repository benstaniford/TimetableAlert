using TimetableAlert.Core;
using TimetableAlert.Core.Models;
using Xunit;

namespace TimetableAlert.Tests;

public class TimetableLoaderTests
{
    private const string MinimalJson = """
        { "lessons": [ { "day": "Monday", "start": "09:00", "end": "10:00", "subject": "Maths" } ] }
        """;

    [Fact]
    public void ParseJson_ReadsTheShippedSample()
    {
        var result = TimetableLoader.ParseJson(File.ReadAllText("timetable.sample.json"));

        Assert.True(result.Success, result.ErrorSummary);
        var timetable = result.Timetable!;

        Assert.Equal("Sam", timetable.Student);
        Assert.Equal(17, timetable.Lessons.Count);
        Assert.Equal(AlertOptions.Default, timetable.Alerts);

        var schedule = new AlertSchedule(timetable);
        Assert.Equal(2, schedule.LessonsOn(DayOfWeek.Monday).Count);
        Assert.Equal(5, schedule.LessonsOn(DayOfWeek.Tuesday).Count);
        Assert.Equal(6, schedule.LessonsOn(DayOfWeek.Wednesday).Count);
        Assert.Single(schedule.LessonsOn(DayOfWeek.Thursday));
        Assert.Equal(3, schedule.LessonsOn(DayOfWeek.Friday).Count);
        Assert.Empty(schedule.LessonsOn(DayOfWeek.Saturday));
    }

    [Fact]
    public void ParseJson_KeepsAssemblyWithoutATeacher()
    {
        var result = TimetableLoader.ParseJson(File.ReadAllText("timetable.sample.json"));

        var assembly = Assert.Single(result.Timetable!.Lessons, lesson => lesson.Subject == "Assembly");
        Assert.Null(assembly.Teacher);
        Assert.Equal(DayOfWeek.Monday, assembly.Day);
        Assert.Equal(new TimeOnly(8, 30), assembly.Start);
        Assert.Equal(new TimeOnly(9, 0), assembly.End);
    }

    [Fact]
    public void ParseJson_DefaultsTheAlertTimingsWhenTheSectionIsAbsent()
    {
        var result = TimetableLoader.ParseJson(MinimalJson);

        Assert.True(result.Success, result.ErrorSummary);
        Assert.Equal(7, result.Timetable!.Alerts.FirstWarningMinutes);
        Assert.Equal(5, result.Timetable.Alerts.FirstWarningSeconds);
        Assert.Equal(60, result.Timetable.Alerts.SecondWarningSeconds);
        Assert.Equal(420, result.Timetable.Alerts.FirstWarningLeadSeconds);
    }

    [Fact]
    public void ParseJson_AcceptsAbbreviatedAndOddlyCasedDays()
    {
        var result = TimetableLoader.ParseJson("""
            { "lessons": [
                { "day": "mon", "start": "9:05", "subject": "Maths" },
                { "day": "THURSDAY", "start": "14:00", "subject": "Biology" } ] }
            """);

        Assert.True(result.Success, result.ErrorSummary);
        Assert.Equal(DayOfWeek.Monday, result.Timetable!.Lessons[0].Day);
        Assert.Equal(new TimeOnly(9, 5), result.Timetable.Lessons[0].Start);
        Assert.Null(result.Timetable.Lessons[0].End);
        Assert.Equal(DayOfWeek.Thursday, result.Timetable.Lessons[1].Day);
    }

    [Theory]
    [InlineData("""{ "lessons": [ { "day": "Someday", "start": "09:00", "subject": "Maths" } ] }""", "not a day")]
    [InlineData("""{ "lessons": [ { "day": "Monday", "start": "half nine", "subject": "Maths" } ] }""", "not a start time")]
    [InlineData("""{ "lessons": [ { "day": "Monday", "start": "09:00", "end": "ten", "subject": "Maths" } ] }""", "not an end time")]
    [InlineData("""{ "lessons": [ { "day": "Monday", "start": "09:00" } ] }""", "subject is missing")]
    [InlineData("""{ "lessons": [] }""", "no lessons")]
    public void ParseJson_RejectsBadEntriesAndSaysWhy(string json, string expectedFragment)
    {
        var result = TimetableLoader.ParseJson(json);

        Assert.False(result.Success);
        Assert.Contains(expectedFragment, result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_NamesTheOffendingEntry()
    {
        var result = TimetableLoader.ParseJson("""
            { "lessons": [
                { "day": "Monday", "start": "09:00", "subject": "Maths" },
                { "day": "Frugday", "start": "10:00", "subject": "Geography" } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains("lessons[1]", result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_ToleratesOddCasingCommentsAndTrailingCommas()
    {
        // Timetables are hand-edited, so the reader is deliberately forgiving.
        var result = TimetableLoader.ParseJson("""
            {
              // Sam's week
              "Student": "Sam",
              "LESSONS": [ { "Day": "Monday", "Start": "09:00", "Subject": "Maths", "Teacher": "Bailey Bravo" }, ]
            }
            """);

        Assert.True(result.Success, result.ErrorSummary);
        Assert.Equal("Sam", result.Timetable!.Student);
        Assert.Equal("Bailey Bravo", result.Timetable.Lessons[0].Teacher);
    }

    [Fact]
    public void ParseJson_RejectsMalformedJson()
    {
        var result = TimetableLoader.ParseJson("{ not json");

        Assert.False(result.Success);
        Assert.Contains("not valid JSON", result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsAnEarlyWarningThatIsNotAheadOfTheCountdown()
    {
        var result = TimetableLoader.ParseJson("""
            { "alerts": { "firstWarningMinutes": 1, "secondWarningSeconds": 60 },
              "lessons": [ { "day": "Monday", "start": "09:00", "subject": "Maths" } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains("further ahead", result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_ReadsADownloadedWeek()
    {
        var result = TimetableLoader.ParseJson("""
            {
              "student": "Sam",
              "coversFrom": "2026-09-07",
              "coversUntil": "2026-09-13",
              "lessons": [ { "date": "2026-09-08", "start": "09:00", "end": "10:00", "subject": "Maths" } ]
            }
            """);

        Assert.True(result.Success, result.ErrorSummary);
        var timetable = result.Timetable!;

        Assert.Equal(new DateOnly(2026, 9, 7), timetable.CoversFrom);
        Assert.Equal(new DateOnly(2026, 9, 13), timetable.CoversUntil);

        var lesson = Assert.Single(timetable.Lessons);
        Assert.Equal(new DateOnly(2026, 9, 8), lesson.Date);
        Assert.Equal(DayOfWeek.Tuesday, lesson.Day);
        Assert.True(lesson.IsDated);
    }

    [Fact]
    public void ParseJson_TreatsAHandWrittenLessonAsRecurring()
    {
        var lesson = Assert.Single(TimetableLoader.ParseJson(MinimalJson).Timetable!.Lessons);

        Assert.Null(lesson.Date);
        Assert.False(lesson.IsDated);
    }

    [Fact]
    public void ParseJson_RejectsALessonWithNeitherDayNorDate()
    {
        var result = TimetableLoader.ParseJson("""
            { "lessons": [ { "start": "09:00", "subject": "Maths" } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains("needs a day of the week or a date", result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsADayThatContradictsItsDate()
    {
        var result = TimetableLoader.ParseJson("""
            { "lessons": [ { "day": "Monday", "date": "2026-09-08", "start": "09:00", "subject": "Maths" } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains("is a Tuesday, not a Monday", result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsAMalformedDate()
    {
        var result = TimetableLoader.ParseJson("""
            { "lessons": [ { "date": "8th September", "start": "09:00", "subject": "Maths" } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains("is not a date of the form yyyy-MM-dd", result.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsACoveringWindowThatRunsBackwards()
    {
        var result = TimetableLoader.ParseJson("""
            {
              "coversFrom": "2026-09-13",
              "coversUntil": "2026-09-07",
              "lessons": [ { "day": "Monday", "start": "09:00", "subject": "Maths" } ]
            }
            """);

        Assert.False(result.Success);
        Assert.Contains("coversUntil is before coversFrom", result.ErrorSummary, StringComparison.Ordinal);
    }
}
