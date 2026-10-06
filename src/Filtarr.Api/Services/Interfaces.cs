using Filtarr.Api.Models;

namespace Filtarr.Api.Services;

/// <summary>Talks to the Sonarr v3 API.</summary>
public interface ISonarrClient
{
    /// <summary>
    /// Checks a Sonarr URL and key and returns "AppName version"; throws with a readable message if it can't connect or the key is
    /// rejected. A null <paramref name="url"/> or <paramref name="apiKey"/> uses the one currently in force.
    /// </summary>
    Task<string> TestAsync(string? url, string? apiKey, CancellationToken ct = default);
    Task<List<SonarrSeries>> GetSeriesAsync(CancellationToken ct = default);
    Task<List<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken ct = default);
    Task MonitorAsync(IEnumerable<int> episodeIds, CancellationToken ct = default);
    Task SearchAsync(IEnumerable<int> episodeIds, CancellationToken ct = default);

    /// <summary>Sonarr's metadata lookup for an IMDb id: the full series resource it would add, or null if unknown.</summary>
    Task<System.Text.Json.Nodes.JsonObject?> LookupSeriesAsync(string imdbId, CancellationToken ct = default);
    Task<List<SonarrRootFolder>> GetRootFoldersAsync(CancellationToken ct = default);
    Task<List<SonarrQualityProfile>> GetQualityProfilesAsync(CancellationToken ct = default);
    /// <summary>The tags defined in Sonarr (series carry their ids).</summary>
    Task<List<SonarrTag>> GetTagsAsync(CancellationToken ct = default);
    /// <summary>Adds a series from a (modified) lookup resource. Throws <see cref="SonarrException"/> if Sonarr rejects it.</summary>
    Task<SonarrSeries> AddSeriesAsync(System.Text.Json.Nodes.JsonObject series, CancellationToken ct = default);
}

/// <summary>Sonarr refused a request; the message is Sonarr's own explanation.</summary>
public class SonarrException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Pure filter evaluation; no I/O.</summary>
public interface IFilterEngine
{
    Dictionary<string, object?> BuildContext(SonarrSeries series, SonarrEpisode episode, DateTime nowUtc, double? episodeRating = null);
    bool Matches(Filter filter, Dictionary<string, object?> context);
    bool Evaluate(FilterCondition condition, Dictionary<string, object?> context);
}

/// <summary>User-editable settings stored in the database.</summary>
public interface ISettingsService
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    /// <summary>The Sonarr URL / keys in force: saved in the UI, else from configuration.</summary>
    Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default);
    /// <summary>Saves without validating; use <see cref="ISettingsUpdater"/> for user input. Applies a changed log level immediately.</summary>
    Task UpdateAsync(SettingsUpdate update, CancellationToken ct = default);
}

public record SettingsUpdateResult(bool Ok, string? Error = null);

/// <summary>Validates user-entered settings (including live connection tests) before saving them.</summary>
public interface ISettingsUpdater
{
    /// <summary>
    /// Rejects an invalid URL or log level, a changed Sonarr URL/key that fails a connection test, and a new OMDb key that fails
    /// a test. Nothing is saved when it rejects.
    /// </summary>
    Task<SettingsUpdateResult> UpdateAsync(SettingsUpdate update, CancellationToken ct = default);
}

public interface IQuickFilterService
{
    Task<List<QuickFilter>> ListAsync(CancellationToken ct = default);
    Task<QuickFilter> CreateAsync(QuickFilter quickFilter, CancellationToken ct = default);
    Task<QuickFilter?> UpdateAsync(int id, QuickFilter input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public interface IFilterService
{
    /// <summary>Lists filters, optionally limited to global filters or to one series.</summary>
    Task<List<Filter>> ListAsync(string? imdbId, bool globalOnly, CancellationToken ct = default);
    Task<Filter> CreateAsync(Filter filter, CancellationToken ct = default);
    Task<Filter?> UpdateAsync(int id, Filter input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// How many filters each series has (global filters are not counted). With <paramref name="imdbIds"/> null, every series
    /// that has at least one filter is returned; otherwise only the given series, and only those that have filters.
    /// </summary>
    Task<List<SeriesFilterSummary>> SummarizeBySeriesAsync(IEnumerable<string>? imdbIds, CancellationToken ct = default);
}

/// <summary>Filter counts for one series. <see cref="SeriesTitle"/> is the title stored when its filters were created.</summary>
public record SeriesFilterSummary(string ImdbId, string? SeriesTitle, int FilterCount, int DisabledCount);

/// <summary>Remembers which episodes Filtarr has already set to monitored, so it never does it twice.</summary>
public interface IMonitoredEpisodeStore
{
    /// <summary>For each given episode id that was monitored before, when that happened (UTC).</summary>
    Task<Dictionary<int, DateTime>> GetMonitoredAtAsync(IEnumerable<int> episodeIds, CancellationToken ct = default);
    /// <summary>Records episodes as monitored; episodes already recorded are left untouched.</summary>
    Task RecordAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default);

    /// <summary>How many episodes of this series are recorded.</summary>
    Task<int> CountForSeriesAsync(SeriesKey series, CancellationToken ct = default);
    /// <summary>Forgets every recorded episode of this series, so Filtarr may monitor them again. Returns how many were removed.</summary>
    Task<int> RemoveForSeriesAsync(SeriesKey series, CancellationToken ct = default);
}

/// <summary>
/// Identifies a series in the history. Rows are matched by IMDb id, or by Sonarr series id, or, for rows seeded from old sync
/// runs (which only kept the title), by title.
/// </summary>
public record SeriesKey(string ImdbId, int? SonarrId = null, string? Title = null);

/// <summary>Per-series view of the monitored history, used by the "reset history" button.</summary>
public interface IMonitoredHistoryService
{
    Task<int> CountAsync(string imdbId, CancellationToken ct = default);
    /// <summary>Forgets what Filtarr monitored for this series; changes nothing in Sonarr. Returns how many episodes were forgotten.</summary>
    Task<int> ResetAsync(string imdbId, CancellationToken ct = default);
}

public interface ISyncRunStore
{
    Task AddAsync(SyncRun run, CancellationToken ct = default);
    Task<List<SyncRun>> RecentAsync(int count, CancellationToken ct = default);
}

public interface ISyncService
{
    /// <summary>Evaluates all filters against Sonarr and returns the episodes that should be monitored. Changes nothing.</summary>
    Task<PreviewResult> PreviewAsync(int? onlySeriesId, CancellationToken ct = default);

    /// <summary>
    /// Shows what filters would do to one series (matched by IMDb id). With <paramref name="only"/> null, the saved global
    /// and series filters are used; otherwise exactly the supplied filters are evaluated (e.g. an unsaved draft). Changes nothing.
    /// </summary>
    Task<SeriesTestResult> TestSeriesAsync(string imdbId, IReadOnlyList<Filter>? only, CancellationToken ct = default);

    /// <summary>Monitors every matched episode that is not yet monitored. Never un-monitors. Throws <see cref="SyncInProgressException"/> if already running.</summary>
    Task<SyncRun> RunAsync(string trigger, CancellationToken ct = default);

    /// <summary>
    /// Runs ONE series filter now: monitors the episodes it matches (and starts a search for those without a file when enabled),
    /// as a sync would but without evaluating any other include filter. Enabled "never monitor" filters still hold episodes back,
    /// and episodes Filtarr monitored before are skipped. The run is recorded in the sync history. Throws
    /// <see cref="RuleRunException"/> when the filter can't be run, and <see cref="SyncInProgressException"/> while a sync is running.
    /// </summary>
    Task<RuleRunResult> RunRuleAsync(int filterId, CancellationToken ct = default);
}

public class SyncInProgressException() : InvalidOperationException("A sync is already running.");

/// <summary>A single filter can't be run; <see cref="StatusCode"/> is the HTTP status to answer with.</summary>
public class RuleRunException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>What running one rule did. When <see cref="Error"/> is set, the counts show what was done before it failed.</summary>
public record RuleRunResult(
    string RuleName = "", bool InSonarr = false, string? SeriesTitle = null, bool SkippedBecauseSeriesUnmonitored = false,
    int Matched = 0, int Monitored = 0, int AlreadyMonitored = 0, int SkippedPreviouslyMonitored = 0,
    List<MatchedEpisode>? NewlyMonitored = null, string? Warning = null, string? Error = null);
