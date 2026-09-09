using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Threading;
using TimetableAlert.Core;
using TimetableAlert.Core.Models;
using TimetableAlert.Overlay;

namespace TimetableAlert.Services;

/// <summary>
/// Drives the alerts: ticks once a second, asks <see cref="AlertSchedule"/> whether a warning is
/// due, and owns the on-screen banner including the final countdown.
/// </summary>
[SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "Sealed, no finalizer, only managed state to release")]
internal sealed class AlertService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly OverlayManager _overlays = new();
    private readonly HashSet<AlertKey> _fired = [];

    private AlertSchedule _schedule = new(Timetable.Empty);
    private DateOnly _firedOn = DateOnly.FromDateTime(DateTime.Now);
    private LessonOccurrence? _countingDownTo;
    private DateTime _hideBannerAt = DateTime.MaxValue;
    private string _status = "No timetable loaded";

    public AlertService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => Tick(DateTime.Now);
        _timer.Start();
    }

    /// <summary>Raised when the tray tooltip text should change.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>The timetable currently in force.</summary>
    public Timetable Timetable => _schedule.Timetable;

    /// <summary>A short description of the next lesson, for the tray tooltip.</summary>
    public string Status => _status;

    /// <summary>Replaces the timetable and forgets which warnings have already been shown.</summary>
    public void SetTimetable(Timetable timetable)
    {
        ArgumentNullException.ThrowIfNull(timetable);

        _schedule = new AlertSchedule(timetable);
        _fired.Clear();
        _countingDownTo = null;
        _hideBannerAt = DateTime.MaxValue;
        _overlays.Hide();
        Tick(DateTime.Now);
    }

    /// <summary>Shows a sample banner so the overlay can be checked without waiting for a lesson.</summary>
    public void ShowTestBanner()
    {
        var now = DateTime.Now;
        var next = _schedule.NextAfter(now);
        var seconds = _schedule.Timetable.Alerts.FirstWarningSeconds;

        if (next is null)
        {
            _overlays.Show("⏰ TEST ALERT", "starts in 7 minutes", "This is what a lesson reminder looks like");
        }
        else
        {
            _overlays.Show(
                SubjectLine(next.Lesson.Subject),
                TimingLine(TimeSpan.FromMinutes(_schedule.Timetable.Alerts.FirstWarningMinutes)),
                $"TEST  ·  {Detail(next)}");
        }

        _countingDownTo = null;
        _hideBannerAt = now.AddSeconds(seconds);
    }

    /// <summary>Today's lessons, formatted one per line for the tray menu.</summary>
    public string DescribeToday(DateTime now)
    {
        var lessons = _schedule.LessonsOn(now.DayOfWeek);
        if (lessons.Count == 0)
        {
            return "Nothing scheduled today.";
        }

        var lines = lessons.Select(lesson =>
        {
            var when = lesson.End is { } end
                ? $"{Format(lesson.Start)}–{Format(end)}"
                : Format(lesson.Start);
            return lesson.Teacher is null ? $"{when}  {lesson.Subject}" : $"{when}  {lesson.Subject} ({lesson.Teacher})";
        });

        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        _timer.Stop();
        _overlays.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Tick(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (today != _firedOn)
        {
            _fired.Clear();
            _firedOn = today;
        }

        UpdateVisibleBanner(now);
        ShowDueAlert(now);
        UpdateStatus(now);
    }

    private void UpdateVisibleBanner(DateTime now)
    {
        if (_countingDownTo is { } lesson)
        {
            var remaining = lesson.StartsAt - now;
            if (remaining <= TimeSpan.Zero)
            {
                _countingDownTo = null;
                _overlays.Hide();
                return;
            }

            _overlays.UpdateText(SubjectLine(lesson.Lesson.Subject), TimingLine(remaining), Detail(lesson));
            return;
        }

        if (_overlays.IsVisible && now >= _hideBannerAt)
        {
            _hideBannerAt = DateTime.MaxValue;
            _overlays.Hide();
        }
    }

    private void ShowDueAlert(DateTime now)
    {
        var decision = _schedule.Evaluate(now, _fired);
        if (decision is null)
        {
            return;
        }

        _fired.Add(decision.Key);

        var occurrence = decision.Occurrence;
        var remaining = occurrence.StartsAt - now;
        _overlays.Show(SubjectLine(occurrence.Lesson.Subject), TimingLine(remaining), Detail(occurrence));

        if (decision.Phase == AlertPhase.Imminent)
        {
            // Hold the countdown on screen right up to the start time.
            _countingDownTo = occurrence;
            _hideBannerAt = DateTime.MaxValue;
        }
        else
        {
            _countingDownTo = null;
            _hideBannerAt = now.AddSeconds(_schedule.Timetable.Alerts.FirstWarningSeconds);
        }
    }

    private void UpdateStatus(DateTime now)
    {
        var next = _schedule.NextAfter(now);
        var status = next is null
            ? "No timetable loaded"
            : $"Next: {next.Lesson.Subject} at {Format(next.Lesson.Start)} {DayLabel(now, next.StartsAt)}".TrimEnd();

        if (status == _status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private static string DayLabel(DateTime now, DateTime startsAt) => (startsAt.Date - now.Date).Days switch
    {
        0 => "today",
        1 => "tomorrow",
        _ => startsAt.ToString("dddd", CultureInfo.CurrentCulture),
    };

    private static string SubjectLine(string subject) => $"⏰ {subject.ToUpper(CultureInfo.CurrentCulture)}";

    private static string TimingLine(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "is starting now";
        }

        // Under two minutes, count in whole seconds; above that, whole minutes.
        if (remaining < TimeSpan.FromMinutes(2))
        {
            var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            return $"starts in {seconds} second{(seconds == 1 ? string.Empty : "s")}";
        }

        var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        return $"starts in {minutes} minute{(minutes == 1 ? string.Empty : "s")}";
    }

    private static string Detail(LessonOccurrence occurrence)
    {
        var lesson = occurrence.Lesson;
        var when = lesson.End is { } end ? $"{Format(lesson.Start)}–{Format(end)}" : Format(lesson.Start);
        return lesson.Teacher is null ? when : $"{when}  ·  {lesson.Teacher}";
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
