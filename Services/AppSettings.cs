using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimetableAlert.Core.Diagnostics;
using TimetableAlert.Core.Feed;

namespace TimetableAlert.Services;

/// <summary>What the app remembers between runs.</summary>
internal sealed class AppSettings
{
    /// <summary>Full path of the timetable file last loaded from the tray menu.</summary>
    public string? TimetablePath { get; set; }

    /// <summary>
    /// How the downloaded calendar is turned into a timetable. Only the harmless half lives here;
    /// the feed URL and the Canvas login are secrets and live in Credential Manager instead.
    /// </summary>
    public FeedSettings? Feed { get; set; }

    /// <summary>The folder holding the settings file: %APPDATA%\TimetableAlert.</summary>
    public static string SettingsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimetableAlert");

    /// <summary>Full path of the settings file.</summary>
    public static string SettingsPath { get; } = Path.Combine(SettingsFolder, "settings.json");

    /// <summary>Reads the saved settings, returning defaults if there are none or they are unreadable.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Error($"Settings: {SettingsPath} could not be read, carrying on with defaults", ex);
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings, silently giving up if the profile is not writable.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Nothing useful to do: the app still works, it just will not remember the path.
            Log.Error($"Settings: {SettingsPath} could not be written", ex);
        }
    }
}

/// <summary>The saved half of <see cref="FeedOptions"/>: naming rules and teacher names.</summary>
internal sealed class FeedSettings
{
    /// <summary>Whose timetable it is.</summary>
    public string? Student { get; set; }

    /// <summary>Trailing words stripped off a calendar title; the built-in list when empty.</summary>
    public List<string>? DropWords { get; set; }

    /// <summary>Exact calendar titles mapped to a subject; the built-in list when empty.</summary>
    public Dictionary<string, string>? SubjectOverrides { get; set; }

    /// <summary>Canvas course id to teacher name, as last refreshed.</summary>
    public Dictionary<string, string>? Teachers { get; set; }

    /// <summary>Turns the saved settings into options the downloader can use.</summary>
    public FeedOptions ToOptions() => new()
    {
        Student = Student,
        DropWords = DropWords is { Count: > 0 } ? DropWords : FeedOptions.Default.DropWords,
        SubjectOverrides = SubjectOverrides is { Count: > 0 }
            ? new Dictionary<string, string>(SubjectOverrides, StringComparer.OrdinalIgnoreCase)
            : FeedOptions.Default.SubjectOverrides,
        TeachersByCourseId = new Dictionary<string, string>(
            Teachers ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal),
    };
}

/// <summary>Source-generated JSON contract for <see cref="AppSettings"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
