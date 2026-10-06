using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Services;

public record EpisodeRating(string? ImdbId, double? Rating);

/// <summary>IMDb episode ratings (and episode IMDb ids) for a series, cached per season.</summary>
public interface IEpisodeRatingService
{
    /// <summary>False when no OMDb API key is in force (neither saved in the UI nor configured).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Ratings keyed by (season, episode number). Season 0 (specials) is skipped. Throws <see cref="OmdbException"/> or
    /// <see cref="HttpRequestException"/> if OMDb can't be reached; seasons fetched before the failure stay cached.
    /// </summary>
    Task<Dictionary<(int Season, int Episode), EpisodeRating>> GetAsync(string seriesImdbId, IEnumerable<int> seasons, CancellationToken ct = default);
}

public class EpisodeRatingService(IOmdbClient omdb, AppDb db, ISettingsService settings, TimeProvider clock) : IEpisodeRatingService
{
    /// <summary>Ratings drift slowly; a day keeps usage well inside OMDb's free quota.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace((await settings.GetEffectiveAsync(ct)).OmdbApiKey);

    public async Task<Dictionary<(int Season, int Episode), EpisodeRating>> GetAsync(
        string seriesImdbId, IEnumerable<int> seasons, CancellationToken ct = default)
    {
        var result = new Dictionary<(int, int), EpisodeRating>();
        foreach (var season in seasons.Where(s => s > 0).Distinct().Order())
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var row = await db.SeasonRatings.FirstOrDefaultAsync(r => r.SeriesImdbId == seriesImdbId && r.Season == season, ct);
            if (row is null || now - row.FetchedAt >= CacheLifetime)
            {
                var fetched = await omdb.GetSeasonAsync(seriesImdbId, season, ct);
                var episodes = fetched.Select(e => new CachedEpisodeRating { Number = e.Number, ImdbId = e.ImdbId, Rating = e.Rating }).ToList();
                if (row is null) db.SeasonRatings.Add(row = new SeasonRatings { SeriesImdbId = seriesImdbId, Season = season });
                row.FetchedAt = now;
                row.Episodes = episodes;
                await db.SaveChangesAsync(ct);
            }
            foreach (var e in row.Episodes) result[(season, e.Number)] = new EpisodeRating(e.ImdbId, e.Rating);
        }
        return result;
    }
}
