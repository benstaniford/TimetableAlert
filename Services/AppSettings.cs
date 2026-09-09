using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimetableAlert.Services;

/// <summary>What the app remembers between runs.</summary>
internal sealed class AppSettings
{
    /// <summary>Full path of the timetable file last loaded from the tray menu.</summary>
    public string? TimetablePath { get; set; }

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
        }
    }
}

/// <summary>Source-generated JSON contract for <see cref="AppSettings"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
