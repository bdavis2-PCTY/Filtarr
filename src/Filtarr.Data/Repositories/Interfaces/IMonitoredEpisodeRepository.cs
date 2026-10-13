using Filtarr.Data.Models;

namespace Filtarr.Data.Repositories.Interfaces;

/// <summary>
/// Identifies a series in the history. Rows are matched by IMDb id, or by Sonarr series id, or, for rows seeded from old sync
/// runs (which only kept the title), by title.
/// </summary>
public record SeriesKey(string ImdbId, int? SonarrId = null, string? Title = null);

/// <summary>
/// Every episode Filtarr has set to monitored, so it never does it twice. Rows are only ever added, or forgotten on purpose
/// (reset history); they are not edited.
/// </summary>
public interface IMonitoredEpisodeRepository
{
    /// <summary>For each given episode id that was monitored before, when that happened (UTC).</summary>
    Task<Dictionary<int, DateTime>> GetMonitoredAtAsync(IEnumerable<int> episodeIds, CancellationToken ct = default);

    /// <summary>Every recorded episode, ordered by Sonarr episode id.</summary>
    Task<List<MonitoredEpisode>> ListAsync(CancellationToken ct = default);

    /// <summary>How many episodes of this series are recorded.</summary>
    Task<int> CountForSeriesAsync(SeriesKey series, CancellationToken ct = default);

    /// <summary>Records episodes as monitored; episodes already recorded (by Sonarr episode id) are left untouched.</summary>
    Task AddMissingAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default);

    /// <summary>Forgets every recorded episode of this series, so Filtarr may monitor them again. Returns how many were removed.</summary>
    Task<int> RemoveForSeriesAsync(SeriesKey series, CancellationToken ct = default);
}
