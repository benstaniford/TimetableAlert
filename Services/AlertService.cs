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

        _overlays.Dismissed += OnOverlayDismissed;
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

    /// <summary>
    /// Shows the next lesson's banner straight away, however far off it is, so that loading a
    /// timetable confirms what is coming up and shows exactly how the warning will look. A real
    /// countdown already on screen wins: it is more urgent than a preview.
    /// </summary>
    public void PreviewNextAlert()
    {
        var now = DateTime.Now;
        var next = _schedule.NextAfter(now);
        if (next is null || _countingDownTo is not null)
        {
            return;
        }

        _overlays.Show(AlertText.Subject(next.Lesson), AlertText.Timing(next.StartsAt, now), AlertText.Detail(next.Lesson));
        _hideBannerAt = now.AddSeconds(_schedule.Timetable.Alerts.FirstWarningSeconds);
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
                AlertText.Subject(next.Lesson),
                AlertText.Timing(now.AddMinutes(_schedule.Timetable.Alerts.FirstWarningMinutes), now),
                $"TEST  ·  {AlertText.Detail(next.Lesson)}");
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
                ? $"{AlertText.Format(lesson.Start)}–{AlertText.Format(end)}"
                : AlertText.Format(lesson.Start);
            return lesson.Teacher is null ? $"{when}  {lesson.Subject}" : $"{when}  {lesson.Subject} ({lesson.Teacher})";
        });

        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        _timer.Stop();
        _overlays.Dismissed -= OnOverlayDismissed;
        _overlays.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The banner is already off screen by the time this runs; all that is left is to forget the
    /// state that would put it back. <c>_fired</c> is deliberately untouched: the key for the
    /// phase just dismissed is already in it, which is what stops the next tick re-showing it,
    /// and the countdown's key is separate, so dismissing the early warning still leaves it to come.
    /// </summary>
    private void OnOverlayDismissed(object? sender, EventArgs e)
    {
        _countingDownTo = null;
        _hideBannerAt = DateTime.MaxValue;
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

            _overlays.UpdateText(AlertText.Subject(lesson.Lesson), AlertText.Timing(lesson.StartsAt, now), AlertText.Detail(lesson.Lesson));
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
        _overlays.Show(AlertText.Subject(occurrence.Lesson), AlertText.Timing(occurrence.StartsAt, now), AlertText.Detail(occurrence.Lesson));

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
            : $"Next: {next.Lesson.Subject} at {AlertText.Format(next.Lesson.Start)} {DayLabel(now, next.StartsAt)}".TrimEnd();

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

}
