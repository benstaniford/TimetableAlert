using System.IO;
using System.Text.Json;
using TimetableAlert.Core;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Services;

/// <summary>
/// The last downloaded week, kept on disk so the app has something to work from at logon before
/// the network is up — or when it never comes up. Written in the ordinary timetable format, so
/// <see cref="TimetableLoader"/> reads it back with no special handling.
/// </summary>
internal static class TimetableCache
{
    /// <summary>Full path of the cache file.</summary>
    internal static string CachePath { get; } = Path.Combine(AppSettings.SettingsFolder, "timetable.cache.json");

    /// <summary>Reads the cached week, or null if there is none or it is unreadable.</summary>
    internal static Timetable? Load()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            var result = TimetableLoader.LoadFile(CachePath);
            return result.Success ? result.Timetable : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the cached week, silently giving up if the profile is not writable.</summary>
    /// <returns>False if it could not be written.</returns>
    internal static bool Save(Timetable timetable)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsFolder);
            File.WriteAllText(CachePath, TimetableWriter.ToJson(timetable));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Whether the cached week should be downloaded again. See <see cref="TimetableFreshness"/>.</summary>
    internal static bool IsStale(Timetable? cached, DateTime now) => TimetableFreshness.IsStale(cached, now);
}
