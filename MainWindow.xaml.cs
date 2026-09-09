using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using TimetableAlert.Core;
using TimetableAlert.Core.Diagnostics;
using TimetableAlert.Services;

namespace TimetableAlert;

/// <summary>
/// The invisible window that hosts the tray icon. It owns the <see cref="AlertService"/> and
/// turns menu clicks into timetable operations.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by WPF XAML framework")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A WPF Window is torn down by the framework; the AlertService is disposed from the Closed event")]
internal sealed partial class MainWindow : Window
{
    private const string SampleFileName = "timetable.sample.json";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AlertService _alerts = new();

    /// <summary>Why the timetable is not quite right, shown after the next lesson in the tooltip.</summary>
    private string? _note;

    public MainWindow()
    {
        InitializeComponent();

        _alerts.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => ShowInTooltip(status));
        Closed += (_, _) => _alerts.Dispose();

        Loaded += (_, _) => LoadAtStartup();
    }

    /// <summary>
    /// Gets a timetable up as fast as possible. Once a calendar has been set up that means the
    /// cached week, downloading a fresh one only when the cache has expired; before then it means
    /// the file remembered from last time. Failures here are reported in the tooltip rather than a
    /// dialog: this runs at every logon, and nobody wants a message box at boot.
    /// </summary>
    private async void LoadAtStartup()
    {
        if (!TimetableSource.IsConfigured)
        {
            Log.Info("Startup: no calendar is set up, falling back to a timetable file");
            LoadFileAtStartup();
            return;
        }

        var cached = TimetableCache.Load();
        if (!TimetableCache.IsStale(cached, DateTime.Now))
        {
            Log.Info("Startup: the cached week is still fresh, using it without downloading");
            _alerts.SetTimetable(cached!);
            return;
        }

        Log.Info(cached is null
            ? "Startup: nothing cached, downloading the week"
            : "Startup: the cached week is stale, downloading a fresh one");

        // Run on the stale copy while the download happens, so a slow or absent network at logon
        // does not mean no warnings at all for the first lesson of the day.
        if (cached is not null)
        {
            _alerts.SetTimetable(cached);
        }

        var outcome = await TimetableSource.RefreshAsync(_settings, force: true);
        Apply(outcome);

        // Only when there is no timetable at all is it worth falling back, and then only to a file
        // that was actually chosen. Never to the bundled sample: a calendar is set up, so warning
        // about fictional lessons would be worse than staying quiet. An empty week is not a
        // failure either — it is a holiday.
        if (outcome.Timetable is null
            && !string.IsNullOrWhiteSpace(_settings.TimetablePath)
            && File.Exists(_settings.TimetablePath))
        {
            Log.Warn($"Startup: the download gave nothing, falling back to {_settings.TimetablePath}");
            TryLoad(_settings.TimetablePath, out _);
        }
    }

    /// <summary>
    /// Loads the timetable remembered from last time, falling back to the sample shipped beside
    /// the executable so the app does something useful the first time it is run.
    /// </summary>
    private void LoadFileAtStartup()
    {
        var path = _settings.TimetablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Log.Info(string.IsNullOrWhiteSpace(path)
                ? "Startup: no timetable has ever been chosen"
                : $"Startup: the remembered timetable {path} is no longer there");

            path = Path.Combine(AppContext.BaseDirectory, SampleFileName);
            Log.Info($"Startup: trying the bundled sample at {path}");
            if (!File.Exists(path))
            {
                Log.Warn("Startup: there is no sample either, so nothing is loaded");
                ShowInTooltip("no timetable loaded");
                return;
            }
        }

        if (!TryLoad(path, out _))
        {
            ShowInTooltip("timetable could not be loaded");
        }
    }

    private bool TryLoad(string path, out string errorSummary)
    {
        errorSummary = string.Empty;

        Log.Info($"Loading timetable file {path}");

        try
        {
            var result = TimetableLoader.LoadFile(path);
            if (!result.Success)
            {
                errorSummary = result.ErrorSummary;
                Log.Warn(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Timetable file {path} was rejected by {result.Errors.Count} validation error(s): {errorSummary.Replace(Environment.NewLine, "; ", StringComparison.Ordinal)}"));
                return false;
            }

            _alerts.SetTimetable(result.Timetable!);
            _settings.TimetablePath = path;
            _settings.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errorSummary = ex.Message;
            Log.Error($"Timetable file {path} could not be read", ex);
            return false;
        }
    }

    /// <summary>Takes a refreshed timetable into use, and remembers anything worth noting about it.</summary>
    private void Apply(TimetableOutcome outcome)
    {
        if (outcome.Timetable is { } timetable && !ReferenceEquals(timetable, _alerts.Timetable))
        {
            _alerts.SetTimetable(timetable);
        }

        _note = outcome.Note;
        if (_note is not null)
        {
            Log.Warn($"Timetable is not quite right: {_note}");
        }

        ShowInTooltip(_alerts.Status);
    }

    private void ShowInTooltip(string status) =>
        TrayIcon.ToolTipText = _note is null
            ? $"Timetable Alert — {status}"
            : $"Timetable Alert — {status}  ({_note})";

    private async void RefreshFromCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (!TimetableSource.IsConfigured)
        {
            Log.Warn("Refresh asked for, but no calendar is set up");
            Notify("No calendar has been set up yet. Use \"Timetable source…\" first.", "Nothing to refresh", MessageBoxImage.Information);
            return;
        }

        Log.Info("Refresh from the calendar asked for from the tray menu");
        var outcome = await TimetableSource.RefreshAsync(_settings, force: true);
        Apply(outcome);

        if (outcome.Timetable is not { } timetable)
        {
            Notify($"The calendar could not be fetched: {outcome.Note}.", "Refresh failed", MessageBoxImage.Warning);
            return;
        }

        var count = timetable.Lessons.Count;
        var week = timetable.CoversFrom is { } from
            ? $" for the week of {from.ToString("d MMMM", CultureInfo.CurrentCulture)}"
            : string.Empty;

        Notify(
            string.Create(CultureInfo.CurrentCulture, $"Downloaded {count} lesson{(count == 1 ? string.Empty : "s")}{week}.\n\n{_alerts.Status}"),
            outcome.Note is null ? "Timetable refreshed" : "Timetable not refreshed",
            outcome.Note is null ? MessageBoxImage.Information : MessageBoxImage.Warning,
            _alerts.PreviewNextAlert);
    }

    private void TimetableSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TimetableSourceWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            Log.Info("Timetable source: the dialog was cancelled");
            return;
        }

        Log.Info("Timetable source: saved, refreshing with it");

        _settings.Save();
        RefreshFromCalendar_Click(sender, e);
    }

    private void LoadTimetable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a timetable",
            Filter = "Timetable JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(AppSettings.SettingsFolder) ? AppSettings.SettingsFolder : AppContext.BaseDirectory,
        };

        if (dialog.ShowDialog(this) != true)
        {
            Log.Debug("Load timetable: the file dialog was cancelled");
            return;
        }

        if (TryLoad(dialog.FileName, out var errorSummary))
        {
            var count = _alerts.Timetable.Lessons.Count;
            Notify(
                string.Create(CultureInfo.CurrentCulture, $"Loaded {count} lesson{(count == 1 ? string.Empty : "s")}.\n\n{_alerts.Status}"),
                "Timetable loaded",
                MessageBoxImage.Information,
                _alerts.PreviewNextAlert);
        }
        else
        {
            Notify($"That timetable could not be used:\n\n{errorSummary}", "Timetable not loaded", MessageBoxImage.Warning);
        }
    }

    private void ReloadTimetable_Click(object sender, RoutedEventArgs e)
    {
        var path = _settings.TimetablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            Notify("No timetable has been loaded yet. Use \"Load timetable…\" first.", "Nothing to reload", MessageBoxImage.Information);
            return;
        }

        if (TryLoad(path, out var errorSummary))
        {
            Notify($"Reloaded {Path.GetFileName(path)}.\n\n{_alerts.Status}", "Timetable reloaded", MessageBoxImage.Information, _alerts.PreviewNextAlert);
        }
        else
        {
            Notify($"{Path.GetFileName(path)} could not be reloaded:\n\n{errorSummary}", "Reload failed", MessageBoxImage.Warning);
        }
    }

    private void TodaysLessons_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        var heading = now.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
        Notify($"{heading}\n\n{_alerts.DescribeToday(now)}", "Today's lessons", MessageBoxImage.Information);
    }

    private void TestOverlay_Click(object sender, RoutedEventArgs e) => _alerts.ShowTestBanner();

    /// <summary>
    /// Opens the log in Notepad. Writing a line first means there is always a file to open, even
    /// on a machine where nothing has happened yet.
    /// </summary>
    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        var path = Log.EnsureFile();

        try
        {
            using var notepad = Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = false });
            Log.Debug(notepad is null
                ? "Notepad was already showing the log"
                : string.Create(CultureInfo.InvariantCulture, $"Notepad opened the log as pid {notepad.Id}"));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            Log.Error($"Notepad would not open {path}", ex);
            Notify($"The log could not be opened in Notepad:\n\n{ex.Message}\n\nIt is at:\n{path}", "Log not opened", MessageBoxImage.Warning);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        var student = _alerts.Timetable.Student;
        var whose = student is null ? string.Empty : $"Timetable for {student}.\n";

        Notify(
            $"Timetable Alert v{version}\n\n{whose}Warns you on screen before each lesson starts.",
            "About Timetable Alert",
            MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Log.Info("Exit chosen from the tray menu");
        _alerts.Dispose();
        Application.Current.Shutdown();
    }

    private void TrayIcon_LeftClick(object sender, RoutedEventArgs e)
    {
        if (TrayIcon?.ContextMenu != null)
        {
            TrayIcon.ContextMenu.IsOpen = true;
        }
    }

    private void Notify(string message, string caption, MessageBoxImage image, Action? afterDismissed = null) =>
        Dispatcher.BeginInvoke(() =>
        {
            MessageBox.Show(message, caption, MessageBoxButton.OK, image);
            afterDismissed?.Invoke();
        });
}
