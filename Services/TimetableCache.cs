using System.Globalization;
using System.IO;
using System.Text.Json;
using TimetableAlert.Core;
using TimetableAlert.Core.Diagnostics;
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
                Log.Debug($"Cache: nothing at {CachePath} yet");
                return null;
            }

            var result = TimetableLoader.LoadFile(CachePath);
            if (!result.Success)
            {
                Log.Warn($"Cache: {CachePath} could not be validated: {result.ErrorSummary.Replace(Environment.NewLine, "; ", StringComparison.Ordinal)}");
                return null;
            }

            Log.Info(string.Create(
                CultureInfo.InvariantCulture,
                $"Cache: read {result.Timetable!.Lessons.Count} lessons downloaded {result.Timetable.FetchedAt?.LocalDateTime:yyyy-MM-dd HH:mm}"));
            return result.Timetable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Error($"Cache: {CachePath} could not be read", ex);
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
            Log.Info(string.Create(CultureInfo.InvariantCulture, $"Cache: wrote {timetable.Lessons.Count} lessons to {CachePath}"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error($"Cache: {CachePath} could not be written", ex);
            return false;
        }
    }

    /// <summary>Whether the cached week should be downloaded again. See <see cref="TimetableFreshness"/>.</summary>
    internal static bool IsStale(Timetable? cached, DateTime now) => TimetableFreshness.IsStale(cached, now);
}
