namespace TimetableAlert.Core.Models;

/// <summary>How far ahead the two warnings fire, and how long the first one lingers.</summary>
/// <param name="FirstWarningMinutes">Minutes before the start time for the first warning.</param>
/// <param name="FirstWarningSeconds">How long the first warning stays on screen.</param>
/// <param name="SecondWarningSeconds">Seconds before the start time for the counting-down warning.</param>
public sealed record AlertOptions(int FirstWarningMinutes, int FirstWarningSeconds, int SecondWarningSeconds)
{
    /// <summary>The defaults used when the timetable file omits an "alerts" section.</summary>
    public static AlertOptions Default { get; } = new(FirstWarningMinutes: 7, FirstWarningSeconds: 5, SecondWarningSeconds: 60);

    /// <summary>The first warning's lead time, in seconds.</summary>
    public int FirstWarningLeadSeconds => FirstWarningMinutes * 60;
}
