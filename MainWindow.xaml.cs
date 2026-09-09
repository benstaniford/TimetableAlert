using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using TimetableAlert.Core;
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

    public MainWindow()
    {
        InitializeComponent();

        _alerts.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => { TrayIcon.ToolTipText = $"Timetable Alert — {status}"; });
        Closed += (_, _) => _alerts.Dispose();

        Loaded += (_, _) => LoadAtStartup();
    }

    /// <summary>
    /// Loads the timetable remembered from last time, falling back to the sample shipped beside
    /// the executable so the app does something useful the first time it is run. Failures here
    /// are reported in the tooltip rather than a dialog: this runs at every logon.
    /// </summary>
    private void LoadAtStartup()
    {
        var path = _settings.TimetablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, SampleFileName);
            if (!File.Exists(path))
            {
                TrayIcon.ToolTipText = "Timetable Alert — no timetable loaded";
                return;
            }
        }

        if (!TryLoad(path, out _))
        {
            TrayIcon.ToolTipText = "Timetable Alert — timetable could not be loaded";
        }
    }

    private bool TryLoad(string path, out string errorSummary)
    {
        errorSummary = string.Empty;

        try
        {
            var result = TimetableLoader.LoadFile(path);
            if (!result.Success)
            {
                errorSummary = result.ErrorSummary;
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
            return false;
        }
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
