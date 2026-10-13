using Filtarr.Core.Clients.Models;
using Filtarr.Core.Exceptions;

namespace Filtarr.Core.Clients.Interfaces;

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
