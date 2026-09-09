using System.Net.Http;
using TimetableAlert.Core.Feed;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Services;

/// <summary>What came of trying to get a timetable.</summary>
/// <param name="Timetable">The timetable to use, or null if there is nothing at all.</param>
/// <param name="Note">A short phrase for the tray tooltip when something is not quite right.</param>
internal sealed record TimetableOutcome(Timetable? Timetable, string? Note);

/// <summary>
/// Where the timetable comes from: the school's calendar feed, by way of a cached copy. The feed
/// URL is the only secret the ordinary path needs, which is why refreshing lessons never asks for
/// a password.
/// </summary>
internal static class TimetableSource
{
    /// <summary>Whether a feed URL has been set up yet.</summary>
    internal static bool IsConfigured => FeedUrl is not null;

    /// <summary>The stored feed URL, or null if none has been set or it is unusable.</summary>
    internal static Uri? FeedUrl =>
        CredentialStore.Read(CredentialStore.FeedTarget) is { Secret: var secret }
        && Uri.TryCreate(secret, UriKind.Absolute, out var url)
            ? url
            : null;

    /// <summary>The stored parent login, or null if none was given.</summary>
    internal static StoredCredential? CanvasLogin => CredentialStore.Read(CredentialStore.CanvasTarget);

    /// <summary>Stores the feed URL, replacing any previous one.</summary>
    internal static bool SaveFeedUrl(Uri feedUrl)
    {
        ArgumentNullException.ThrowIfNull(feedUrl);
        return CredentialStore.Write(CredentialStore.FeedTarget, userName: null, feedUrl.ToString());
    }

    /// <summary>Stores the parent login, or forgets it when either half is blank.</summary>
    internal static void SaveCanvasLogin(string? user, string? password)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            CredentialStore.Delete(CredentialStore.CanvasTarget);
            return;
        }

        CredentialStore.Write(CredentialStore.CanvasTarget, user, password);
    }

    /// <summary>Downloads the week containing a date, with no reference to the cache.</summary>
    /// <exception cref="HttpRequestException">The feed could not be fetched.</exception>
    /// <exception cref="TaskCanceledException">The download timed out.</exception>
    internal static async Task<Timetable> FetchAsync(
        Uri feedUrl,
        AppSettings settings,
        DateOnly weekOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = (settings.Feed ?? new FeedSettings()).ToOptions();
        return await TimetableFeed.FetchAsync(feedUrl, options, weekOf, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The timetable to run with. Uses the cached week while it is still fresh, downloads when it
    /// is not, and falls back to the stale cache when the download fails — a timetable that is a
    /// little out of date beats no warnings at all.
    /// </summary>
    /// <param name="settings">Naming rules and teacher names.</param>
    /// <param name="force">Download even if the cache is still fresh.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    internal static async Task<TimetableOutcome> RefreshAsync(
        AppSettings settings,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var feedUrl = FeedUrl;
        if (feedUrl is null)
        {
            return new TimetableOutcome(null, "no calendar set up");
        }

        var cached = TimetableCache.Load();
        if (!force && !TimetableCache.IsStale(cached, DateTime.Now))
        {
            return new TimetableOutcome(cached, null);
        }

        try
        {
            var fetched = await FetchAsync(feedUrl, settings, DateOnly.FromDateTime(DateTime.Now), cancellationToken)
                .ConfigureAwait(false);
            TimetableCache.Save(fetched);
            return new TimetableOutcome(fetched, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // Never let a failed download throw away a good cache.
            return cached is null
                ? new TimetableOutcome(null, "calendar unreachable")
                : new TimetableOutcome(cached, "calendar unreachable, showing the last copy");
        }
    }

    /// <summary>
    /// Signs in and refreshes the teacher names into <paramref name="settings"/>. This is the only
    /// thing the parent's password is used for, and the only thing that breaks without it.
    /// </summary>
    /// <returns>False if Canvas rejected the credentials.</returns>
    /// <exception cref="HttpRequestException">Canvas could not be reached.</exception>
    internal static async Task<bool> RefreshTeachersAsync(
        AppSettings settings,
        string user,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var feedUrl = FeedUrl ?? throw new InvalidOperationException("No calendar feed has been set up.");

        using var canvas = new CanvasClient(CanvasClient.SiteOf(feedUrl));
        if (!await canvas.LogInAsync(user, password, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var feed = settings.Feed ??= new FeedSettings();
        var known = feed.Teachers ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var teachers = await canvas.ReadTeachersAsync(known, cancellationToken).ConfigureAwait(false);

        feed.Teachers = new Dictionary<string, string>(teachers, StringComparer.Ordinal);
        settings.Save();
        return true;
    }
}
