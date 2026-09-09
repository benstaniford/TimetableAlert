using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TimetableAlert.Core.Diagnostics;
using TimetableAlert.Services;

namespace TimetableAlert;

/// <summary>
/// The application object. Its only real job beyond hosting <see cref="MainWindow"/> is to open
/// the log before anything else runs, and to make sure that anything which goes wrong afterwards
/// — on the dispatcher, on a background thread, or across a suspend — ends up written down.
/// </summary>
internal sealed partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.UseFolder(Path.Combine(AppSettings.SettingsFolder, "logs"));

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

        Log.Info(new string('=', 72));
        Log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"Timetable Alert {version} starting on {Environment.MachineName} as {Environment.UserName} (pid {Environment.ProcessId}, {RuntimeInformation.FrameworkDescription})"));
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"Log level {Log.Level}; settings in {AppSettings.SettingsPath}"));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SessionEnding += (_, args) => Log.Info(
            string.Create(CultureInfo.InvariantCulture, $"Windows is ending the session ({args.ReasonSessionEnding})"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"Timetable Alert exiting with code {e.ApplicationExitCode}"));

        base.OnExit(e);
    }

    /// <summary>
    /// A crash on the UI thread would otherwise take the tray icon away with no trace at all, so
    /// it is written down before Windows deals with it.
    /// </summary>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        Log.Error("Unhandled exception on the UI thread", e.Exception);

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Error("Unhandled exception on a background thread", e.ExceptionObject as Exception);

    /// <summary>
    /// Worth recording because the alert rules are written around sleep: a warning that appears
    /// to have been missed is usually a machine that was asleep at the time.
    /// </summary>
    private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) =>
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"Power mode changed: {e.Mode}"));
}
