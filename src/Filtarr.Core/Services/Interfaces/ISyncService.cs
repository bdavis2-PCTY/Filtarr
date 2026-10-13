using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

public interface ISyncService
{
    /// <summary>Evaluates all filters against Sonarr and returns the episodes that should be monitored. Changes nothing.</summary>
    Task<PreviewResult> PreviewAsync(int? onlySeriesId, CancellationToken ct = default);

    /// <summary>
    /// Shows what filters would do to one series (matched by IMDb id). With <paramref name="only"/> null, the saved global
    /// and series filters are used; otherwise exactly the supplied filters are evaluated (e.g. an unsaved draft). Changes nothing.
    /// </summary>
    Task<SeriesTestResult> TestSeriesAsync(string imdbId, IReadOnlyList<FilterModel>? only, CancellationToken ct = default);

    /// <summary>Monitors every matched episode that is not yet monitored. Never un-monitors. Throws <see cref="Exceptions.SyncInProgressException"/> if already running.</summary>
    Task<SyncRunModel> RunAsync(string trigger, CancellationToken ct = default);

    /// <summary>
    /// Runs ONE series filter now: monitors the episodes it matches (and starts a search for those without a file when enabled),
    /// as a sync would but without evaluating any other include filter. Enabled "never monitor" filters still hold episodes back,
    /// and episodes Filtarr monitored before are skipped. The run is recorded in the sync history. Throws
    /// <see cref="Exceptions.RuleRunException"/> when the filter can't be run, and <see cref="Exceptions.SyncInProgressException"/> while a sync is running.
    /// </summary>
    Task<RuleRunResult> RunRuleAsync(int filterId, CancellationToken ct = default);

    /// <summary>The sync history, newest first.</summary>
    Task<List<SyncRunModel>> GetRecentRunsAsync(int count, CancellationToken ct = default);
}
