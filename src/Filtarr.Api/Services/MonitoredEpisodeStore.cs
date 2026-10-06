using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Services;

public class MonitoredEpisodeStore(AppDb db) : IMonitoredEpisodeStore
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

    public async Task RecordAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default)
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

public class MonitoredHistoryService(IMonitoredEpisodeStore store, ISonarrClient sonarr, ILogger<MonitoredHistoryService> log) : IMonitoredHistoryService
{
    public async Task<int> CountAsync(string imdbId, CancellationToken ct = default) =>
        await store.CountForSeriesAsync(await KeyAsync(imdbId, ct), ct);

    public async Task<int> ResetAsync(string imdbId, CancellationToken ct = default)
    {
        var removed = await store.RemoveForSeriesAsync(await KeyAsync(imdbId, ct), ct);
        log.LogWarning("Reset the monitored history of {ImdbId}: {Count} episode(s) forgotten; Filtarr may monitor them again.", imdbId, removed);
        return removed;
    }

    /// <summary>The series' Sonarr id and title widen the match when Sonarr is reachable; the IMDb id alone is used otherwise.</summary>
    async Task<SeriesKey> KeyAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            var inLibrary = (await sonarr.GetSeriesAsync(ct)).FirstOrDefault(s => string.Equals(s.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));
            if (inLibrary is not null) return new SeriesKey(imdbId, inLibrary.Id, inLibrary.Title);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Could not read the Sonarr library while resolving {ImdbId}: {Message}", imdbId, ex.Message);
        }
        return new SeriesKey(imdbId);
    }
}
