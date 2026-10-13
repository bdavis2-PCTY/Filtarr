using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Services.Interfaces;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Filtarr.Core.Services;

public class MonitoredHistoryService(IMonitoredEpisodeRepository episodes, ISonarrClient sonarr, ILogger<MonitoredHistoryService> log) : IMonitoredHistoryService
{
    public async Task<int> CountAsync(string imdbId, CancellationToken ct = default) =>
        await episodes.CountForSeriesAsync(await KeyAsync(imdbId, ct), ct);

    public async Task<int> ResetAsync(string imdbId, CancellationToken ct = default)
    {
        var removed = await episodes.RemoveForSeriesAsync(await KeyAsync(imdbId, ct), ct);
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
