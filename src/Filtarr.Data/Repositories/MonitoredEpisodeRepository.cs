using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Data.Repositories;

internal sealed class MonitoredEpisodeRepository(AppDb db) : IMonitoredEpisodeRepository
{
    // Keeps each query's parameter list well below SQLite's limits when a library is large.
    const int BatchSize = 500;

    public async Task<Dictionary<int, DateTime>> GetMonitoredAtAsync(IEnumerable<int> episodeIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, DateTime>();
        foreach (var batch in episodeIds.Distinct().Chunk(BatchSize))
        {
            var rows = await db.MonitoredEpisodes.AsNoTracking()
                .Where(e => batch.Contains(e.EpisodeId))
                .Select(e => new { e.EpisodeId, e.MonitoredAt })
                .ToListAsync(ct);
            foreach (var r in rows) result[r.EpisodeId] = r.MonitoredAt;
        }
        return result;
    }

    public Task<List<MonitoredEpisode>> ListAsync(CancellationToken ct = default) =>
        db.MonitoredEpisodes.AsNoTracking().OrderBy(e => e.EpisodeId).ToListAsync(ct);

    public async Task AddMissingAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default)
    {
        foreach (var batch in episodes.DistinctBy(e => e.EpisodeId).Chunk(BatchSize))
        {
            var ids = batch.Select(e => e.EpisodeId).ToList();
            var existing = (await db.MonitoredEpisodes.AsNoTracking().Where(e => ids.Contains(e.EpisodeId)).Select(e => e.EpisodeId).ToListAsync(ct)).ToHashSet();
            db.MonitoredEpisodes.AddRange(batch.Where(e => !existing.Contains(e.EpisodeId)));
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<int> CountForSeriesAsync(SeriesKey series, CancellationToken ct = default) => ForSeries(series).CountAsync(ct);

    public Task<int> RemoveForSeriesAsync(SeriesKey series, CancellationToken ct = default) => ForSeries(series).ExecuteDeleteAsync(ct);

    IQueryable<MonitoredEpisode> ForSeries(SeriesKey series)
    {
        var imdbId = series.ImdbId;
        var sonarrId = series.SonarrId ?? 0;
        var title = string.IsNullOrWhiteSpace(series.Title) ? null : series.Title.Trim().ToLower();
        return db.MonitoredEpisodes.Where(e =>
            e.SeriesImdbId == imdbId
            || (sonarrId > 0 && e.SeriesId == sonarrId)
            // Rows seeded from old sync runs have no series id or IMDb id, only the title Sonarr had at the time.
            || (title != null && e.SeriesId == 0 && e.SeriesTitle.ToLower() == title));
    }
}
