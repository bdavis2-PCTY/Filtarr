namespace Filtarr.Core.Services.Interfaces;

/// <summary>Per-series view of the monitored history, used by the "reset history" button.</summary>
public interface IMonitoredHistoryService
{
    Task<int> CountAsync(string imdbId, CancellationToken ct = default);
    /// <summary>Forgets what Filtarr monitored for this series; changes nothing in Sonarr. Returns how many episodes were forgotten.</summary>
    Task<int> ResetAsync(string imdbId, CancellationToken ct = default);
}
