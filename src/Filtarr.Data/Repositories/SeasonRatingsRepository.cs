using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Data.Repositories;

internal sealed class SeasonRatingsRepository(AppDb db) : ISeasonRatingsRepository
{
    public Task<SeasonRatings?> GetAsync(string seriesImdbId, int season, CancellationToken ct = default) =>
        db.SeasonRatings.AsNoTracking().FirstOrDefaultAsync(r => r.SeriesImdbId == seriesImdbId && r.Season == season, ct);

    public Task<List<SeasonRatings>> ListAsync(CancellationToken ct = default) =>
        db.SeasonRatings.AsNoTracking().OrderBy(r => r.SeriesImdbId).ThenBy(r => r.Season).ToListAsync(ct);

    public async Task<SeasonRatings> SaveAsync(
        string seriesImdbId, int season, DateTime fetchedAt, List<CachedEpisodeRating> episodes, CancellationToken ct = default)
    {
        var row = await db.SeasonRatings.FirstOrDefaultAsync(r => r.SeriesImdbId == seriesImdbId && r.Season == season, ct);
        if (row is null) db.SeasonRatings.Add(row = new SeasonRatings { SeriesImdbId = seriesImdbId, Season = season });
        row.FetchedAt = fetchedAt;
        row.Episodes = episodes;
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<bool> DeleteAsync(string seriesImdbId, int season, CancellationToken ct = default)
    {
        var row = await db.SeasonRatings.FirstOrDefaultAsync(r => r.SeriesImdbId == seriesImdbId && r.Season == season, ct);
        if (row is null) return false;
        db.SeasonRatings.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
