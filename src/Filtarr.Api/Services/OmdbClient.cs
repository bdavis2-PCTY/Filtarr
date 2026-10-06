using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filtarr.Api.Services;

public record OmdbEpisode(int Number, string? ImdbId, double? Rating);

/// <summary>Reads IMDb episode data through the OMDb API.</summary>
public interface IOmdbClient
{
    /// <summary>
    /// Episodes of one season of a series. Returns an empty list when OMDb doesn't know the season.
    /// Throws <see cref="OmdbException"/> when OMDb can't be used (bad key, daily limit reached, unreachable or timing out).
    /// </summary>
    Task<List<OmdbEpisode>> GetSeasonAsync(string seriesImdbId, int season, CancellationToken ct = default);

    /// <summary>Checks an API key with a single lookup. A null or blank key tests the one currently in force. Never throws for bad keys.</summary>
    Task<OmdbTestResult> TestKeyAsync(string? apiKey, CancellationToken ct = default);

    /// <summary>
    /// What IMDb shows about a title (full plot, cast, genres, rating...). Best-effort: null when there is no key, OMDb doesn't know
    /// the title, or it can't be reached, so the details page just shows less.
    /// </summary>
    Task<OmdbTitle?> GetTitleAsync(string imdbId, CancellationToken ct = default);
}

public record OmdbTitle(
    string? Plot, List<string> Actors, List<string> Genres, string? Rated, int? RuntimeMinutes, string? Released,
    string? Language, string? Country, string? Awards, double? ImdbRating, int? ImdbVotes, int? TotalSeasons);

public record OmdbTestResult(bool Ok, string Message);

public class OmdbException(string message, Exception? inner = null) : Exception(message, inner);

public class OmdbClient(IHttpClientFactory factory, ISettingsService settings, FiltarrOptions options, ILogger<OmdbClient>? log = null) : IOmdbClient
{
    /// <summary>A well-known title; any valid key answers a lookup for it.</summary>
    const string TestTitle = "tt0903747";
    static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public async Task<OmdbTestResult> TestKeyAsync(string? apiKey, CancellationToken ct = default)
    {
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? (await settings.GetEffectiveAsync(ct)).OmdbApiKey : apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) return new(false, "No OMDb API key entered.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TestTimeout);
        try
        {
            var url = $"{options.OmdbUrl.TrimEnd('/')}/?apikey={Uri.EscapeDataString(apiKey)}&i={TestTitle}";
            using var res = await factory.CreateClient("omdb").GetAsync(url, timeout.Token);
            // OMDb explains problems in the JSON body, sometimes with a 401, so read it whatever the status.
            var reply = await res.Content.ReadFromJsonAsync<Reply>(timeout.Token);
            if (string.Equals(reply?.Status, "True", StringComparison.OrdinalIgnoreCase)) return new(true, "OMDb accepted the key.");
            return new(false, reply?.Error ?? $"OMDb answered HTTP {(int)res.StatusCode}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, $"OMDb did not respond within {TestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex) { return new(false, $"Could not reach OMDb ({ex.Message})."); }
        catch (JsonException) { return new(false, "OMDb returned an unreadable response."); }
    }

    public const int MaxAttempts = 3;

    /// <summary>Pause before a retry (grows with each attempt). Tests set this to zero.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public async Task<List<OmdbEpisode>> GetSeasonAsync(string seriesImdbId, int season, CancellationToken ct = default)
    {
        var key = (await settings.GetEffectiveAsync(ct)).OmdbApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new OmdbException("No OMDb API key is configured (set it in Settings).");
        var url = $"{options.OmdbUrl.TrimEnd('/')}/?apikey={Uri.EscapeDataString(key)}" +
                  $"&i={Uri.EscapeDataString(seriesImdbId)}&Season={season}";

        // A new HttpClient per attempt: a stale pooled connection is the usual cause of a request that just hangs,
        // and retrying lets the handler open a fresh one.
        for (var attempt = 1; ; attempt++)
        {
            string failure;
            try
            {
                using var res = await factory.CreateClient("omdb").GetAsync(url, ct);
                if ((int)res.StatusCode < 500) return await ParseAsync(res, ct);
                failure = $"OMDb returned HTTP {(int)res.StatusCode}";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = "OMDb did not respond in time";
            }
            catch (HttpRequestException ex)
            {
                failure = $"Could not reach OMDb ({ex.Message})";
            }

            if (attempt >= MaxAttempts) throw new OmdbException($"{failure} (gave up after {MaxAttempts} attempts).");
            log?.LogWarning("OMDb season {Season} of {ImdbId}: {Failure}; retrying ({Attempt}/{Max})", season, seriesImdbId, failure, attempt, MaxAttempts);
            await Task.Delay(RetryDelay * attempt, ct);
        }
    }

    // OMDb reports problems such as a bad key in the JSON body, sometimes with a 4xx status, so the body is read regardless.
    static async Task<List<OmdbEpisode>> ParseAsync(HttpResponseMessage res, CancellationToken ct)
    {
        Reply? reply;
        try { reply = await res.Content.ReadFromJsonAsync<Reply>(ct); }
        catch (JsonException) { throw new OmdbException($"OMDb returned an unreadable response (HTTP {(int)res.StatusCode})."); }

        if (reply is null) return [];
        if (!string.Equals(reply.Status, "True", StringComparison.OrdinalIgnoreCase))
        {
            if (reply.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) return [];
            throw new OmdbException($"OMDb: {reply.Error ?? $"HTTP {(int)res.StatusCode}"}");
        }
        return (reply.Episodes ?? [])
            .Where(e => int.TryParse(e.Episode, out _))
            .Select(e => new OmdbEpisode(int.Parse(e.Episode!), e.ImdbId, ParseRating(e.ImdbRating)))
            .ToList();
    }

    static double? ParseRating(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null; // "N/A" -> null

    public async Task<OmdbTitle?> GetTitleAsync(string imdbId, CancellationToken ct = default)
    {
        var key = (await settings.GetEffectiveAsync(ct)).OmdbApiKey;
        if (string.IsNullOrWhiteSpace(key)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TestTimeout);
        try
        {
            var url = $"{options.OmdbUrl.TrimEnd('/')}/?apikey={Uri.EscapeDataString(key)}&i={Uri.EscapeDataString(imdbId)}&plot=full";
            using var res = await factory.CreateClient("omdb").GetAsync(url, timeout.Token);
            var t = await res.Content.ReadFromJsonAsync<TitleReply>(timeout.Token);
            if (t is null || !string.Equals(t.Status, "True", StringComparison.OrdinalIgnoreCase)) return null;
            return new OmdbTitle(
                Clean(t.Plot), SplitList(t.Actors), SplitList(t.Genre), Clean(t.Rated), LeadingNumber(t.Runtime), Clean(t.Released),
                Clean(t.Language), Clean(t.Country), Clean(t.Awards), ParseRating(t.ImdbRating),
                int.TryParse(t.ImdbVotes?.Replace(",", ""), out var votes) ? votes : null,
                int.TryParse(t.TotalSeasons, out var seasons) ? seasons : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            log?.LogDebug(ex, "OMDb title lookup for {ImdbId} failed", imdbId);
            return null;
        }
    }

    /// <summary>OMDb writes "N/A" for anything it doesn't have.</summary>
    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "N/A" ? null : s.Trim();

    static List<string> SplitList(string? s) =>
        Clean(s)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];

    /// <summary>"49 min" -> 49.</summary>
    static int? LeadingNumber(string? s)
    {
        var digits = new string((Clean(s) ?? "").TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : null;
    }

    record TitleReply(
        [property: JsonPropertyName("Response")] string? Status,
        [property: JsonPropertyName("Plot")] string? Plot,
        [property: JsonPropertyName("Actors")] string? Actors,
        [property: JsonPropertyName("Genre")] string? Genre,
        [property: JsonPropertyName("Rated")] string? Rated,
        [property: JsonPropertyName("Runtime")] string? Runtime,
        [property: JsonPropertyName("Released")] string? Released,
        [property: JsonPropertyName("Language")] string? Language,
        [property: JsonPropertyName("Country")] string? Country,
        [property: JsonPropertyName("Awards")] string? Awards,
        [property: JsonPropertyName("imdbRating")] string? ImdbRating,
        [property: JsonPropertyName("imdbVotes")] string? ImdbVotes,
        [property: JsonPropertyName("totalSeasons")] string? TotalSeasons);

    record Reply(
        [property: JsonPropertyName("Response")] string? Status,
        [property: JsonPropertyName("Error")] string? Error,
        [property: JsonPropertyName("Episodes")] List<Ep>? Episodes);

    record Ep(
        [property: JsonPropertyName("Episode")] string? Episode,
        [property: JsonPropertyName("imdbID")] string? ImdbId,
        [property: JsonPropertyName("imdbRating")] string? ImdbRating);
}
