using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;
using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Core.Services;

public class EpisodeRatingService(IOmdbClient omdb, ISeasonRatingsRepository cache, ISettingsService settings, TimeProvider clock) : IEpisodeRatingService
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
            var row = await cache.GetAsync(seriesImdbId, season, ct);
            if (row is null || now - row.FetchedAt >= CacheLifetime)
            {
                var fetched = await omdb.GetSeasonAsync(seriesImdbId, season, ct);
                var episodes = fetched.Select(e => new CachedEpisodeRating { Number = e.Number, ImdbId = e.ImdbId, Rating = e.Rating }).ToList();
                row = await cache.SaveAsync(seriesImdbId, season, now, episodes, ct);
            }
            foreach (var e in row.Episodes) result[(season, e.Number)] = new EpisodeRating(e.ImdbId, e.Rating);
        }
        return result;
    }
}
