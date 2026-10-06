namespace Filtarr.Api.Services;

public record SeriesInfo(
    string ImdbId, string Title, int? Year, string? YearRange, string? Cast, string? ImageUrl,
    bool InSonarr, int? SonarrId, bool? SonarrMonitored, int FilterCount = 0, int DisabledFilterCount = 0,
    /// <summary>Address of the series page in Sonarr; null when the series isn't in the library.</summary>
    string? SonarrLink = null);

/// <summary>Finds series on IMDb and tells whether each is in the Sonarr library and how many filters it has.</summary>
public interface ISeriesCatalog
{
    Task<List<SeriesInfo>> SearchAsync(string query, CancellationToken ct = default);
    Task<SeriesInfo?> GetAsync(string imdbId, CancellationToken ct = default);
    /// <summary>Every series that has at least one filter, sorted by title (in Sonarr or not).</summary>
    Task<List<SeriesInfo>> ListWithFiltersAsync(CancellationToken ct = default);
}

public class SeriesCatalog(IImdbClient imdb, ISonarrClient sonarr, IFilterService filters, ISettingsService settings, ILogger<SeriesCatalog> log) : ISeriesCatalog
{
    // The Sonarr address used to build links; loaded with the library at the start of each call (this service is scoped to a request).
    string _sonarrUrl = "";

    /// <summary>Limits parallel IMDb lookups when the filtered series are not in the library.</summary>
    const int MaxParallelLookups = 4;

    public async Task<List<SeriesInfo>> SearchAsync(string query, CancellationToken ct = default)
    {
        var found = await imdb.SearchSeriesAsync(query, ct);
        var library = await TryGetLibraryAsync(ct);
        var counts = (await filters.SummarizeBySeriesAsync(found.Select(t => t.ImdbId), ct)).ToDictionary(s => s.ImdbId, StringComparer.OrdinalIgnoreCase);
        return found.Select(t => ToInfo(t, library.FirstOrDefault(s => Same(s.ImdbId, t.ImdbId)), counts.GetValueOrDefault(t.ImdbId))).ToList();
    }

    public async Task<SeriesInfo?> GetAsync(string imdbId, CancellationToken ct = default)
    {
        var library = await TryGetLibraryAsync(ct);
        var inSonarr = library.FirstOrDefault(s => Same(s.ImdbId, imdbId));
        var counts = (await filters.SummarizeBySeriesAsync([imdbId], ct)).FirstOrDefault();

        var title = await TryGetTitleAsync(imdbId, ct);
        if (title is not null) return ToInfo(title, inSonarr, counts);
        // IMDb unavailable: fall back to what Sonarr knows so the page still works for library series.
        return inSonarr is null ? null : FromSonarr(imdbId, inSonarr, counts);
    }

    public async Task<List<SeriesInfo>> ListWithFiltersAsync(CancellationToken ct = default)
    {
        var summaries = await filters.SummarizeBySeriesAsync(null, ct);
        if (summaries.Count == 0) return [];
        var library = await TryGetLibraryAsync(ct);

        // Library series are described from Sonarr's data (no extra calls); the rest need an IMDb lookup for title and poster.
        using var gate = new SemaphoreSlim(MaxParallelLookups);
        var infos = await Task.WhenAll(summaries.Select(async summary =>
        {
            var inSonarr = library.FirstOrDefault(s => Same(s.ImdbId, summary.ImdbId));
            if (inSonarr is not null) return FromSonarr(summary.ImdbId, inSonarr, summary);

            await gate.WaitAsync(ct);
            try
            {
                var title = await TryGetTitleAsync(summary.ImdbId, ct);
                return title is not null
                    ? ToInfo(title, null, summary)
                    : new SeriesInfo(summary.ImdbId, summary.SeriesTitle ?? summary.ImdbId, null, null, null, null, false, null, null,
                        summary.FilterCount, summary.DisabledCount);
            }
            finally { gate.Release(); }
        }));
        return infos.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    async Task<ImdbTitle?> TryGetTitleAsync(string imdbId, CancellationToken ct)
    {
        try { return await imdb.GetSeriesAsync(imdbId, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogWarning(ex, "IMDb lookup for {ImdbId} failed", imdbId);
            return null;
        }
    }

    /// <summary>Sonarr being down must not break searching, so presence information is best-effort.</summary>
    async Task<List<SonarrSeries>> TryGetLibraryAsync(CancellationToken ct)
    {
        _sonarrUrl = (await settings.GetEffectiveAsync(ct)).SonarrUrl;
        try { return await sonarr.GetSeriesAsync(ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogWarning("Could not read the Sonarr library: {Message}", ex.Message);
            return [];
        }
    }

    static bool Same(string? a, string? b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    SeriesInfo ToInfo(ImdbTitle t, SonarrSeries? s, SeriesFilterSummary? f) =>
        new(t.ImdbId, t.Title, t.Year, t.YearRange, t.Cast, t.ImageUrl, s is not null, s?.Id, s?.Monitored,
            f?.FilterCount ?? 0, f?.DisabledCount ?? 0, LinkTo(s));

    SeriesInfo FromSonarr(string imdbId, SonarrSeries s, SeriesFilterSummary? f)
    {
        var poster = s.Images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;
        return new(imdbId, s.Title, s.Year == 0 ? null : s.Year, null, null, poster, true, s.Id, s.Monitored,
            f?.FilterCount ?? 0, f?.DisabledCount ?? 0, LinkTo(s));
    }

    /// <summary>Sonarr serves a series at {url}/series/{titleSlug}.</summary>
    string? LinkTo(SonarrSeries? s) =>
        s is { TitleSlug: { Length: > 0 } slug } ? $"{_sonarrUrl.TrimEnd('/')}/series/{Uri.EscapeDataString(slug)}" : null;
}
