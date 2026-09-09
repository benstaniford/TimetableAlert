using System.Globalization;

namespace TimetableAlert.Core.Diagnostics;

/// <summary>How much detail is written to the log.</summary>
public enum LogLevel
{
    /// <summary>Everything, including the routine per-tick chatter.</summary>
    Debug,

    /// <summary>What happened: alerts shown, timetables loaded, calendars fetched.</summary>
    Info,

    /// <summary>Something did not go to plan, but the app carried on.</summary>
    Warn,

    /// <summary>Something failed outright.</summary>
    Error,

    /// <summary>Nothing at all.</summary>
    Off,
}

/// <summary>
/// The app's log file. Deliberately tiny and dependency-free: this is a tray app that runs all
/// day on one machine, and the questions it has to answer after the fact are "did the warning
/// fire?" and "did the calendar download?" — a rolling text file answers both.
/// </summary>
/// <remarks>
/// Every write opens and closes the file, so a log line survives the app being killed, and the
/// whole thing is wrapped in a lock because alerts are written from the UI thread while downloads
/// are written from the thread pool. Writing is best-effort throughout: a log that cannot be
/// written must never take the app down with it.
/// </remarks>
public static class Log
{
    /// <summary>The base name of the current log file.</summary>
    public const string FileName = "timetable-alert.log";

    /// <summary>How large the log may grow before it is rolled over.</summary>
    private const long MaxBytes = 1024 * 1024;

    /// <summary>How many rolled-over logs are kept behind the current one.</summary>
    private const int Archives = 3;

    /// <summary>Set from the <c>TIMETABLEALERT_LOG</c> environment variable at first use.</summary>
    private const string LevelVariable = "TIMETABLEALERT_LOG";

    private static readonly Lock Gate = new();

    private static string _folder = DefaultFolder();

    private static LogLevel _level = LevelFromEnvironment();

    /// <summary>The folder the log is written to.</summary>
    public static string Folder
    {
        get
        {
            lock (Gate)
            {
                return _folder;
            }
        }
    }

    /// <summary>Full path of the current log file.</summary>
    public static string FilePath => Path.Combine(Folder, FileName);

    /// <summary>
    /// The least severe thing that gets written. Defaults to <see cref="LogLevel.Info"/>, or to
    /// whatever <c>TIMETABLEALERT_LOG</c> names — set it to <c>debug</c> to see the tick-by-tick
    /// detail when a warning has not fired as expected.
    /// </summary>
    public static LogLevel Level
    {
        get
        {
            lock (Gate)
            {
                return _level;
            }
        }

        set
        {
            lock (Gate)
            {
                _level = value;
            }
        }
    }

    /// <summary>Points the log at a folder, creating it on the first write.</summary>
    /// <param name="folder">The folder to write into.</param>
    public static void UseFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        lock (Gate)
        {
            _folder = folder;
        }
    }

    /// <summary>Writes a line of routine detail.</summary>
    /// <param name="line">The text to write.</param>
    public static void Debug(string line) => Write(LogLevel.Debug, line);

    /// <summary>Writes a line describing something the app did.</summary>
    /// <param name="line">The text to write.</param>
    public static void Info(string line) => Write(LogLevel.Info, line);

    /// <summary>Writes a line describing something that did not go to plan.</summary>
    /// <param name="line">The text to write.</param>
    public static void Warn(string line) => Write(LogLevel.Warn, line);

    /// <summary>Writes a line describing a failure.</summary>
    /// <param name="line">The text to write.</param>
    public static void Error(string line) => Write(LogLevel.Error, line);

    /// <summary>Writes a failure along with the exception that caused it.</summary>
    /// <param name="line">What was being attempted.</param>
    /// <param name="error">The exception, written indented underneath.</param>
    public static void Error(string line, Exception? error) =>
        Write(LogLevel.Error, error is null ? line : line + Environment.NewLine + Indent(error.ToString()));

    /// <summary>
    /// Just the scheme and host of a URL, which is all a log is ever allowed to say about one.
    /// The calendar feed address carries its access token in the path — <c>user_&lt;token&gt;.ics</c>
    /// — so it is a password in full, and neither the path nor the query may be written down.
    /// </summary>
    /// <param name="url">The URL to strip.</param>
    public static string Redact(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.IsAbsoluteUri ? url.GetLeftPart(UriPartial.Authority) : "(relative url)";
    }

    /// <summary>
    /// Makes sure the log file exists, so that opening it in an editor always shows something.
    /// </summary>
    /// <returns>The full path of the log file.</returns>
    public static string EnsureFile()
    {
        Write(LogLevel.Info, "Log opened for viewing");
        return FilePath;
    }

    private static void Write(LogLevel level, string line)
    {
        lock (Gate)
        {
            if (level < _level)
            {
                return;
            }

            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {Tag(level)} [{Environment.CurrentManagedThreadId,3}] {line}{Environment.NewLine}");

            try
            {
                Directory.CreateDirectory(_folder);
                var path = Path.Combine(_folder, FileName);
                Roll(path, text.Length);
                File.AppendAllText(path, text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // Nothing useful to do about it, and nowhere to say so.
            }
        }
    }

    /// <summary>
    /// Shuffles the log along one place once it has grown past <see cref="MaxBytes"/>, so that a
    /// machine left running for months keeps a bounded amount of history rather than one enormous
    /// file.
    /// </summary>
    private static void Roll(string path, int adding)
    {
        var current = new FileInfo(path);
        if (!current.Exists || current.Length + adding <= MaxBytes)
        {
            return;
        }

        File.Delete(ArchivePath(Archives));

        for (var i = Archives - 1; i >= 1; i--)
        {
            var older = ArchivePath(i);
            if (File.Exists(older))
            {
                File.Move(older, ArchivePath(i + 1), overwrite: true);
            }
        }

        File.Move(path, ArchivePath(1), overwrite: true);
    }

    /// <summary>The path of the nth rolled-over log, newest first.</summary>
    private static string ArchivePath(int index) =>
        Path.Combine(_folder, string.Create(CultureInfo.InvariantCulture, $"timetable-alert.{index}.log"));

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "OFF",
    };

    private static string Indent(string block) =>
        "    " + block.Replace(Environment.NewLine, Environment.NewLine + "    ", StringComparison.Ordinal);

    private static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimetableAlert",
        "logs");

    private static LogLevel LevelFromEnvironment() =>
        Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable(LevelVariable), ignoreCase: true, out var level)
            ? level
            : LogLevel.Info;
}
