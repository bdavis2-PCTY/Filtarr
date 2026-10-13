using Filtarr.Data.Models;

namespace Filtarr.Data.Repositories.Interfaces;

/// <summary>The per-season cache of IMDb episode ratings. There is one row per (series IMDb id, season).</summary>
public interface ISeasonRatingsRepository
{
    Task<SeasonRatings?> GetAsync(string seriesImdbId, int season, CancellationToken ct = default);

    Task<List<SeasonRatings>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates the season's row, or replaces its episodes and fetch time in place; returns the stored row.</summary>
    Task<SeasonRatings> SaveAsync(string seriesImdbId, int season, DateTime fetchedAt, List<CachedEpisodeRating> episodes, CancellationToken ct = default);

    /// <summary>False when the season isn't cached.</summary>
    Task<bool> DeleteAsync(string seriesImdbId, int season, CancellationToken ct = default);
}
