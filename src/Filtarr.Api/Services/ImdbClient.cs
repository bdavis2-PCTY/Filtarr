using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Filtarr.Api.Services;

public record ImdbTitle(string ImdbId, string Title, int? Year, string? YearRange, string? Cast, string? ImageUrl, string Type);

/// <summary>Searches IMDb for TV series.</summary>
public interface IImdbClient
{
    Task<List<ImdbTitle>> SearchSeriesAsync(string query, CancellationToken ct = default);
    /// <summary>Looks up one title by IMDb id (e.g. "tt0903747"); null if not found or not a series.</summary>
    Task<ImdbTitle?> GetSeriesAsync(string imdbId, CancellationToken ct = default);
}

/// <summary>
/// Uses IMDb's public search-suggestion endpoint (the one behind the imdb.com search box). It needs no API key but is not an
/// officially documented API, so the base URL is configurable (Filtarr:ImdbSuggestionUrl).
/// </summary>
public partial class ImdbClient(IHttpClientFactory factory, FiltarrOptions options, IMemoryCache? cache = null) : IImdbClient
{
    public static readonly TimeSpan TitleCacheLifetime = TimeSpan.FromHours(6);

    static readonly string[] SeriesTypes = ["tvSeries", "tvMiniSeries"];

    [GeneratedRegex(@"^tt\d{1,12}$")]
    public static partial Regex ImdbIdPattern();

    public async Task<List<ImdbTitle>> SearchSeriesAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        var entries = await QueryAsync(query, ct);
        return entries.Where(e => e.Id.StartsWith("tt") && SeriesTypes.Contains(e.TypeId)).Select(ToTitle).ToList();
    }

    public async Task<ImdbTitle?> GetSeriesAsync(string imdbId, CancellationToken ct = default)
    {
        if (!ImdbIdPattern().IsMatch(imdbId)) return null;
        // Series details barely change, and the landing page looks up every filtered series, so remember hits for a while.
        if (cache?.TryGetValue(imdbId, out ImdbTitle? cached) == true) return cached;
        var entries = await QueryAsync(imdbId, ct);
        var hit = entries.FirstOrDefault(e => e.Id == imdbId && SeriesTypes.Contains(e.TypeId));
        if (hit is null) return null;
        var title = ToTitle(hit);
        cache?.Set(imdbId, title, TitleCacheLifetime);
        return title;
    }

    async Task<List<Entry>> QueryAsync(string query, CancellationToken ct)
    {
        // The endpoint shards by the first character of the query (a-z, 0-9; anything else goes under "x").
        var first = char.ToLowerInvariant(query[0]);
        var shard = char.IsAsciiLetterOrDigit(first) ? first : 'x';
        var url = $"{options.ImdbSuggestionUrl.TrimEnd('/')}/{shard}/{Uri.EscapeDataString(query)}.json";
        var http = factory.CreateClient("imdb");
        var res = await http.GetFromJsonAsync<Response>(url, ct);
        return res?.D ?? [];
    }

    static ImdbTitle ToTitle(Entry e) => new(e.Id, e.Title ?? e.Id, e.Year, e.YearRange, e.Cast, e.Image?.ImageUrl, e.TypeId ?? "");

    record Response([property: JsonPropertyName("d")] List<Entry>? D);
    record Entry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("l")] string? Title,
        [property: JsonPropertyName("qid")] string? TypeId,
        [property: JsonPropertyName("y")] int? Year,
        [property: JsonPropertyName("yr")] string? YearRange,
        [property: JsonPropertyName("s")] string? Cast,
        [property: JsonPropertyName("i")] EntryImage? Image);
    record EntryImage([property: JsonPropertyName("imageUrl")] string? ImageUrl);
}
