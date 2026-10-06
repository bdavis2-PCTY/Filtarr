using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Filtarr.Api.Services;

public class SonarrClient(IHttpClientFactory factory, ISettingsService settings) : ISonarrClient
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>An explicit URL / key (used when testing user input) wins; otherwise the ones in force (saved in the UI, else configured).</summary>
    async Task<HttpClient> CreateAsync(string? url, string? apiKey, CancellationToken ct)
    {
        if (url is null || apiKey is null)
        {
            var current = await settings.GetEffectiveAsync(ct);
            url ??= current.SonarrUrl;
            apiKey ??= current.SonarrApiKey;
        }
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Sonarr URL and API key are not configured.");
        var http = factory.CreateClient("sonarr");
        http.BaseAddress = new Uri(url.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Remove("X-Api-Key");
        http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return http;
    }

    Task<HttpClient> CreateAsync(CancellationToken ct) => CreateAsync(null, null, ct);

    public async Task<string> TestAsync(string? url, string? apiKey, CancellationToken ct = default)
    {
        if (url is not null && !Uri.TryCreate(url, UriKind.Absolute, out _)) throw new InvalidOperationException("The Sonarr URL is not a valid address.");
        var http = await CreateAsync(url, apiKey, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TestTimeout);
        try
        {
            using var res = await http.GetAsync("api/v3/system/status", timeout.Token);
            if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new InvalidOperationException($"Sonarr rejected the API key (HTTP {(int)res.StatusCode}).");
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"{http.BaseAddress} answered HTTP {(int)res.StatusCode}; is this the Sonarr address?");
            JsonElement status;
            try { status = await res.Content.ReadFromJsonAsync<JsonElement>(Json, timeout.Token); }
            catch (JsonException) { throw new InvalidOperationException($"{http.BaseAddress} did not answer like Sonarr; is the URL right?"); }
            if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("appName", out var name))
                throw new InvalidOperationException($"{http.BaseAddress} did not answer like Sonarr; is the URL right?");
            return $"{name.GetString()} {(status.TryGetProperty("version", out var v) ? v.GetString() : "")}".Trim();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timed out after {TestTimeout.TotalSeconds:0} seconds waiting for {http.BaseAddress}.");
        }
    }

    public async Task<List<SonarrSeries>> GetSeriesAsync(CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        return await http.GetFromJsonAsync<List<SonarrSeries>>("api/v3/series", Json, ct) ?? [];
    }

    public async Task<List<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        return await http.GetFromJsonAsync<List<SonarrEpisode>>($"api/v3/episode?seriesId={seriesId}", Json, ct) ?? [];
    }

    public async Task MonitorAsync(IEnumerable<int> episodeIds, CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        foreach (var batch in episodeIds.Chunk(100))
        {
            var res = await http.PutAsJsonAsync("api/v3/episode/monitor", new { episodeIds = batch, monitored = true }, Json, ct);
            res.EnsureSuccessStatusCode();
        }
    }

    public async Task<JsonObject?> LookupSeriesAsync(string imdbId, CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        var results = await http.GetFromJsonAsync<JsonArray>($"api/v3/series/lookup?term={Uri.EscapeDataString($"imdb:{imdbId}")}", Json, ct);
        return results?.OfType<JsonObject>().FirstOrDefault(r => string.Equals((string?)r["imdbId"], imdbId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<SonarrRootFolder>> GetRootFoldersAsync(CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        return await http.GetFromJsonAsync<List<SonarrRootFolder>>("api/v3/rootfolder", Json, ct) ?? [];
    }

    public async Task<List<SonarrTag>> GetTagsAsync(CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        return await http.GetFromJsonAsync<List<SonarrTag>>("api/v3/tag", Json, ct) ?? [];
    }

    public async Task<List<SonarrQualityProfile>> GetQualityProfilesAsync(CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        return await http.GetFromJsonAsync<List<SonarrQualityProfile>>("api/v3/qualityprofile", Json, ct) ?? [];
    }

    public async Task<SonarrSeries> AddSeriesAsync(JsonObject series, CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        using var res = await http.PostAsJsonAsync("api/v3/series", series, Json, ct);
        if (!res.IsSuccessStatusCode) throw new SonarrException(await ReadErrorAsync(res, ct), (int)res.StatusCode);
        return await res.Content.ReadFromJsonAsync<SonarrSeries>(Json, ct)
               ?? throw new SonarrException("Sonarr returned an empty response.", (int)res.StatusCode);
    }

    /// <summary>Sonarr explains rejections either as a list of validation errors or as { "message": ... }.</summary>
    static async Task<string> ReadErrorAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        try
        {
            var node = JsonNode.Parse(body);
            var messages = node is JsonArray arr
                ? arr.Select(e => (string?)e?["errorMessage"]).Where(m => !string.IsNullOrWhiteSpace(m)).ToList()
                : [(string?)node?["message"]];
            var text = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
            if (text != "") return text;
        }
        catch (JsonException) { /* not JSON; fall through */ }
        return $"Sonarr returned HTTP {(int)res.StatusCode}.";
    }

    public async Task SearchAsync(IEnumerable<int> episodeIds, CancellationToken ct = default)
    {
        var http = await CreateAsync(ct);
        foreach (var batch in episodeIds.Chunk(100))
        {
            var res = await http.PostAsJsonAsync("api/v3/command", new { name = "EpisodeSearch", episodeIds = batch }, Json, ct);
            res.EnsureSuccessStatusCode();
        }
    }
}
