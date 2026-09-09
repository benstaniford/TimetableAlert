using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace TimetableAlert.Core.Feed;

/// <summary>
/// The one part that needs the parent's Canvas login: reading teacher names, which the calendar
/// feed does not carry. Lessons never come through here, so a broken login costs teacher names
/// and nothing else.
/// </summary>
/// <remarks>
/// Student accounts sign in through Google, so only a parent login works. Canvas answers a bad
/// login with 400 rather than redisplaying the form, which is what <see cref="LogInAsync"/> reads.
/// </remarks>
[SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "Sealed, no finalizer, only managed state to release")]
public sealed partial class CanvasClient : IDisposable
{
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _http;
    private readonly Uri _baseAddress;

    /// <summary>Creates a client against a Canvas site, e.g. https://school.instructure.com.</summary>
    public CanvasClient(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        _baseAddress = baseAddress;
        _handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            CheckCertificateRevocationList = true,
        };
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("User-Agent", "TimetableAlert");
    }

    /// <summary>The Canvas site a feed URL belongs to.</summary>
    public static Uri SiteOf(Uri feedUrl)
    {
        ArgumentNullException.ThrowIfNull(feedUrl);
        return new Uri(feedUrl.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>
    /// Signs in, returning false if Canvas rejected the credentials. Network failures throw
    /// instead, so the caller can tell "wrong password" from "no internet".
    /// </summary>
    /// <exception cref="HttpRequestException">Canvas could not be reached.</exception>
    public async Task<bool> LogInAsync(string user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentNullException.ThrowIfNull(password);

        var loginUrl = new Uri(_baseAddress, "/login/canvas");

        var form = await _http.GetStringAsync(loginUrl, cancellationToken).ConfigureAwait(false);
        var token = AuthenticityToken().Match(form);
        if (!token.Success)
        {
            return false;
        }

        using var body = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["authenticity_token"] = token.Groups[1].Value,
            ["pseudonym_session[unique_id]"] = user,
            ["pseudonym_session[password]"] = password,
            ["pseudonym_session[remember_me]"] = "0",
        });

        using var response = await _http.PostAsync(loginUrl, body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var landedOn = response.RequestMessage?.RequestUri?.Query ?? string.Empty;
        return landedOn.Contains("login_success", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the teacher for every course, merged onto what is already known. A course with
    /// exactly one teacher besides the operations accounts identifies its lesson; year-group
    /// courses list the whole year team, so those keep whatever was set by hand.
    /// </summary>
    /// <param name="known">Existing course id to teacher names, which win where nothing is deduced.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <exception cref="HttpRequestException">Canvas could not be reached.</exception>
    public async Task<IReadOnlyDictionary<string, string>> ReadTeachersAsync(
        IReadOnlyDictionary<string, string> known,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(known);

        var teachers = new Dictionary<string, string>(known, StringComparer.Ordinal);

        var courses = await GetAsync<IReadOnlyList<CanvasCourse>>("/api/v1/courses?per_page=100", cancellationToken)
            .ConfigureAwait(false);

        foreach (var course in courses ?? [])
        {
            var id = course.Id.ToString(CultureInfo.InvariantCulture);
            var staff = await GetAsync<IReadOnlyList<CanvasUser>>(
                $"/api/v1/courses/{id}/users?enrollment_type[]=teacher&per_page=50",
                cancellationToken).ConfigureAwait(false);

            var named = (staff ?? [])
                .Select(person => person.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !AdministrativeAccount().IsMatch(name!))
                .ToList();

            if (named.Count == 1)
            {
                teachers[id] = named[0]!;
            }
        }

        return teachers;
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var typeInfo = (JsonTypeInfo<T>)CanvasJsonContext.Default.GetTypeInfo(typeof(T))!;
        var stream = await _http.GetStreamAsync(new Uri(_baseAddress, path), cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    [GeneratedRegex("name=\"authenticity_token\"\\s+value=\"([^\"]+)\"")]
    private static partial Regex AuthenticityToken();

    /// <summary>Accounts enrolled on every course, which therefore never identify a lesson's teacher.</summary>
    [GeneratedRegex(@"\bOperations\b|^Test Student", RegexOptions.IgnoreCase)]
    private static partial Regex AdministrativeAccount();
}

/// <summary>A course as the Canvas API returns it.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Created by the JSON deserializer")]
internal sealed record CanvasCourse(long Id, string? Name);

/// <summary>A person as the Canvas API returns them.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Created by the JSON deserializer")]
internal sealed record CanvasUser(string? Name);

/// <summary>Source-generated JSON contracts for the handful of Canvas API shapes that are read.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IReadOnlyList<CanvasCourse>))]
[JsonSerializable(typeof(IReadOnlyList<CanvasUser>))]
internal sealed partial class CanvasJsonContext : JsonSerializerContext;
