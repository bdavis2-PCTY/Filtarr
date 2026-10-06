using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace Filtarr.Api.Services;

/// <summary>Everything the series page shows, merged from IMDb, OMDb and Sonarr. Any part can be missing.</summary>
public record SeriesDetails(
    string ImdbId, string Title, int? Year, string? YearRange, string? PosterUrl, string? BackdropUrl,
    string? Plot, List<string> Cast, List<string> Genres,
    string? Certification, int? RuntimeMinutes, string? Network, string? Status, string? FirstAired,
    string? Language, string? Country, string? Awards,
    double? Rating, int? Votes, string? RatingSource,
    int? Seasons, int? Episodes, int? EpisodesDownloaded,
    List<string> Tags, bool InSonarr, bool HasFullDetails);

public interface ISeriesDetailsService
{
    /// <summary>Null when none of the sources knows the series.</summary>
    Task<SeriesDetails?> GetAsync(string imdbId, CancellationToken ct = default);
}

/// <summary>
/// Sources, best first for each field: OMDb (what imdb.com shows: full plot, full cast, genres, IMDb rating; needs the OMDb key),
/// then Sonarr (library entry, or Sonarr's own lookup for a series that isn't in the library), then IMDb's search data (title, years,
/// leading cast, poster). Sonarr is the only source of the network, status, tags and episode counts.
/// </summary>
public class SeriesDetailsService(
    IImdbClient imdb, IOmdbClient omdb, ISonarrClient sonarr, IMemoryCache cache, ILogger<SeriesDetailsService> log) : ISeriesDetailsService
{
    public static readonly TimeSpan OmdbCacheLifetime = TimeSpan.FromHours(24);

    public async Task<SeriesDetails?> GetAsync(string imdbId, CancellationToken ct = default)
    {
        var imdbTask = TryAsync(() => imdb.GetSeriesAsync(imdbId, ct), "IMDb");
        var omdbTask = OmdbAsync(imdbId, ct);
        var sonarrTask = SonarrAsync(imdbId, ct);
        await Task.WhenAll(imdbTask, omdbTask, sonarrTask);
        var (title, extra, son) = (imdbTask.Result, omdbTask.Result, sonarrTask.Result);
        if (title is null && extra is null && son is null) return null;

        var s = son?.Series;
        var sonarrPoster = Image(s, "poster");
        var cast = extra is { Actors.Count: > 0 } ? extra.Actors : SplitCast(title?.Cast);
        var genres = extra is { Genres.Count: > 0 } ? extra.Genres : s?.Genres ?? [];
        var rating = extra?.ImdbRating is { } r ? (rating: r, votes: extra.ImdbVotes, source: "IMDb")
                   : s?.Ratings is { Votes: > 0 } sr ? (rating: sr.Value, votes: sr.Votes, source: "Sonarr")
                   : (rating: (double?)null, votes: (int?)null, source: (string?)null);

        return new SeriesDetails(
            ImdbId: imdbId,
            Title: title?.Title ?? s?.Title ?? imdbId,
            Year: title?.Year ?? (s is { Year: > 0 } ? s.Year : null),
            YearRange: title?.YearRange,
            PosterUrl: title?.ImageUrl ?? sonarrPoster,
            BackdropUrl: Image(s, "fanart"),
            Plot: extra?.Plot ?? Blank(s?.Overview),
            Cast: cast, Genres: genres,
            Certification: Blank(s?.Certification) ?? extra?.Rated,
            RuntimeMinutes: s is { Runtime: > 0 } ? s.Runtime : extra?.RuntimeMinutes,
            Network: Blank(s?.Network), Status: Blank(s?.Status),
            FirstAired: DateOnly(s?.FirstAired) ?? extra?.Released,
            Language: Blank(s?.OriginalLanguage?.Name) ?? extra?.Language,
            Country: extra?.Country, Awards: extra?.Awards,
            Rating: rating.rating, Votes: rating.votes, RatingSource: rating.source,
            Seasons: s?.Statistics?.SeasonCount ?? son?.SeasonCount ?? extra?.TotalSeasons,
            Episodes: s?.Statistics is { TotalEpisodeCount: > 0 } st ? st.TotalEpisodeCount : null,
            EpisodesDownloaded: s?.Statistics?.EpisodeFileCount,
            Tags: son?.Tags ?? [],
            InSonarr: son?.InLibrary == true,
            HasFullDetails: extra is not null);
    }

    record SonarrPart(SonarrSeries? Series, bool InLibrary, int? SeasonCount, List<string> Tags);

    /// <summary>The library entry if there is one; otherwise what Sonarr's metadata lookup knows about the series.</summary>
    async Task<SonarrPart?> SonarrAsync(string imdbId, CancellationToken ct)
    {
        var inLibrary = await TryAsync(async () =>
            (await sonarr.GetSeriesAsync(ct)).FirstOrDefault(x => string.Equals(x.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase)), "Sonarr library");
        if (inLibrary is not null)
        {
            var tags = new List<string>();
            if (inLibrary.Tags is { Count: > 0 } ids)
            {
                var all = await TryAsync(() => sonarr.GetTagsAsync(ct), "Sonarr tags") ?? [];
                tags = ids.Select(id => all.FirstOrDefault(t => t.Id == id)?.Label).OfType<string>().ToList();
            }
            return new SonarrPart(inLibrary, true, inLibrary.Statistics?.SeasonCount, tags);
        }

        var lookup = await TryAsync(() => sonarr.LookupSeriesAsync(imdbId, ct), "Sonarr lookup");
        if (lookup is null) return null;
        var series = lookup.Deserialize<SonarrSeries>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var seasons = (lookup["seasons"] as JsonArray)?.Count(x => (int?)x?["seasonNumber"] > 0);
        return new SonarrPart(series, false, seasons, []);
    }

    async Task<OmdbTitle?> OmdbAsync(string imdbId, CancellationToken ct)
    {
        var key = $"omdb-title:{imdbId}";
        if (cache.TryGetValue(key, out OmdbTitle? cached)) return cached;
        var found = await TryAsync(() => omdb.GetTitleAsync(imdbId, ct), "OMDb");
        if (found is not null) cache.Set(key, found, OmdbCacheLifetime);
        return found;
    }

    /// <summary>Every source is optional, so one being down must not break the page.</summary>
    async Task<T?> TryAsync<T>(Func<Task<T>> call, string source)
    {
        try { return await call(); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or OmdbException or TaskCanceledException)
        {
            log.LogWarning("Series details: {Source} unavailable: {Message}", source, ex.Message);
            return default;
        }
    }

    static string? Image(SonarrSeries? s, string coverType) =>
        Blank(s?.Images?.FirstOrDefault(i => i.CoverType == coverType)?.RemoteUrl);

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Sonarr dates look like 2022-02-18T00:00:00Z.</summary>
    static string? DateOnly(string? s) => Blank(s) is { Length: >= 10 } d && d[4] == '-' ? d[..10] : null;

    static List<string> SplitCast(string? s) =>
        string.IsNullOrWhiteSpace(s) ? [] : s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
