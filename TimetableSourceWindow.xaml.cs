using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using TimetableAlert.Services;

namespace TimetableAlert;

/// <summary>
/// Where the timetable is downloaded from. The feed address is the only thing needed to fetch
/// lessons; the login below it is optional and buys teacher names, nothing else. Both are kept in
/// Windows Credential Manager rather than in any file of ours.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by WPF XAML framework")]
internal sealed partial class TimetableSourceWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>Opens the dialog over the settings it may update.</summary>
    public TimetableSourceWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        _settings = settings;

        FeedUrlBox.Text = TimetableSource.FeedUrl?.ToString() ?? string.Empty;
        UserNameBox.Text = TimetableSource.CanvasLogin?.UserName ?? string.Empty;

        // The stored password is deliberately not loaded back into the box; leaving it empty
        // keeps whatever is already saved.
        StatusText.Text = TimetableSource.IsConfigured
            ? "A calendar is already set up. Leave the password empty to keep the saved one."
            : "Nothing set up yet.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFeedUrl(out var feedUrl))
        {
            return;
        }

        if (!TimetableSource.SaveFeedUrl(feedUrl))
        {
            StatusText.Text = "Windows would not store the address in Credential Manager.";
            return;
        }

        var user = UserNameBox.Text.Trim();
        if (user.Length == 0)
        {
            TimetableSource.SaveCanvasLogin(null, null);
        }
        else if (PasswordEntry.Password.Length > 0)
        {
            TimetableSource.SaveCanvasLogin(user, PasswordEntry.Password);
        }

        DialogResult = true;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFeedUrl(out var feedUrl))
        {
            return;
        }

        using (Busy("Fetching…"))
        {
            try
            {
                var timetable = await TimetableSource.FetchAsync(feedUrl, _settings, DateOnly.FromDateTime(DateTime.Now));
                StatusText.Text = string.Create(
                    CultureInfo.CurrentCulture,
                    $"Found {timetable.Lessons.Count} lesson{(timetable.Lessons.Count == 1 ? string.Empty : "s")} for the week of {timetable.CoversFrom:d MMMM}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                StatusText.Text = $"Could not fetch that address: {ex.Message}";
            }
        }
    }

    private async void RefreshTeachers_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFeedUrl(out var feedUrl) || !TimetableSource.SaveFeedUrl(feedUrl))
        {
            return;
        }

        var user = UserNameBox.Text.Trim();
        var password = PasswordEntry.Password.Length > 0
            ? PasswordEntry.Password
            : TimetableSource.CanvasLogin?.Secret;

        if (user.Length == 0 || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "Fill in the parent username and password first.";
            return;
        }

        using (Busy("Signing in…"))
        {
            try
            {
                if (!await TimetableSource.RefreshTeachersAsync(_settings, user, password))
                {
                    StatusText.Text = "Canvas rejected those credentials. Note that student logins go through Google and will not work here — this needs the parent login.";
                    return;
                }

                TimetableSource.SaveCanvasLogin(user, password);
                var known = _settings.Feed?.Teachers?.Count ?? 0;
                StatusText.Text = string.Create(CultureInfo.CurrentCulture, $"Teacher names refreshed: {known} courses known.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                StatusText.Text = $"Could not reach Canvas: {ex.Message}";
            }
        }
    }

    private bool TryReadFeedUrl([NotNullWhen(true)] out Uri? feedUrl)
    {
        var text = FeedUrlBox.Text.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out feedUrl)
            && (feedUrl.Scheme == Uri.UriSchemeHttps || feedUrl.Scheme == Uri.UriSchemeHttp))
        {
            return true;
        }

        StatusText.Text = "That does not look like a web address. It should start with https:// and end with .ics";
        feedUrl = null;
        return false;
    }

    /// <summary>Shows a message and disables the buttons until the work finishes.</summary>
    private Restore Busy(string message)
    {
        StatusText.Text = message;
        TestButton.IsEnabled = false;
        TeachersButton.IsEnabled = false;
        SaveButton.IsEnabled = false;

        return new Restore(this);
    }

    private sealed class Restore(TimetableSourceWindow window) : IDisposable
    {
        public void Dispose()
        {
            window.TestButton.IsEnabled = true;
            window.TeachersButton.IsEnabled = true;
            window.SaveButton.IsEnabled = true;
        }
    }
}
