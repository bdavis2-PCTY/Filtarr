using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

/// <summary>IMDb episode ratings (and episode IMDb ids) for a series, cached per season.</summary>
public interface IEpisodeRatingService
{
    /// <summary>False when no OMDb API key is in force (neither saved in the UI nor configured).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Ratings keyed by (season, episode number). Season 0 (specials) is skipped. Throws <see cref="Exceptions.OmdbException"/> or
    /// <see cref="HttpRequestException"/> if OMDb can't be reached; seasons fetched before the failure stay cached.
    /// </summary>
    Task<Dictionary<(int Season, int Episode), EpisodeRating>> GetAsync(string seriesImdbId, IEnumerable<int> seasons, CancellationToken ct = default);
}
