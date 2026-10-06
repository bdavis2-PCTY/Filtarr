namespace Filtarr.Api.Models;

public class AppSettings
{
    public const string DefaultSonarrUrl = "http://localhost:8989";

    public int Id { get; set; } = 1;
    public string SonarrUrl { get; set; } = DefaultSonarrUrl;
    public string ApiKey { get; set; } = "";
    public string OmdbApiKey { get; set; } = "";
    /// <summary>Root folder for series added from the UI; empty = the first root folder in Sonarr.</summary>
    public string SonarrRootFolderPath { get; set; } = "";
    /// <summary>Quality profile name for series added from the UI; empty = the first profile in Sonarr.</summary>
    public string SonarrQualityProfile { get; set; } = "";
    /// <summary>Saved log level; null = whatever the Serilog configuration says (Warning by default).</summary>
    public string? LogLevel { get; set; }
    /// <summary>Set once old appsettings values for the settings above have been copied into this row (see LegacyConfigImport).</summary>
    public bool ConfigImported { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int SyncIntervalMinutes { get; set; } = 60;
    /// <summary>Trigger an EpisodeSearch in Sonarr for newly monitored episodes that have no file.</summary>
    public bool SearchOnMonitor { get; set; } = true;
    public bool SkipUnmonitoredSeries { get; set; } = true;
}

public enum FilterAction { Include, Exclude }

public class FilterCondition
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = "";
}

/// <summary>
/// A filter is a set of conditions that must ALL match (AND). An episode is monitored when it matches
/// at least one enabled Include filter and no enabled Exclude filter. A filter with ImdbId == null is global; otherwise it only applies to the Sonarr series with that IMDb id.
/// </summary>
public class Filter
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FilterAction Action { get; set; } = FilterAction.Include;
    public string? ImdbId { get; set; }
    public string? SeriesTitle { get; set; }
    public List<FilterCondition> Conditions { get; set; } = new();
}

/// <summary>
/// A reusable filter template (name, action and conditions) that is not tied to any series and is never evaluated itself.
/// Picking one on a series page pre-fills a new, unsaved series filter.
/// </summary>
public class QuickFilter
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public FilterAction Action { get; set; } = FilterAction.Include;
    public List<FilterCondition> Conditions { get; set; } = new();
}

public class SyncedItem
{
    public int EpisodeId { get; set; }
    public string Series { get; set; } = "";
    public string Episode { get; set; } = "";
    public string Filter { get; set; } = "";
}

public class SyncRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Trigger { get; set; } = "manual";
    public int SeriesScanned { get; set; }
    public int EpisodesMatched { get; set; }
    public int EpisodesMonitored { get; set; }
    /// <summary>Matched episodes left alone because Filtarr had already monitored them in an earlier run.</summary>
    public int EpisodesSkipped { get; set; }
    public string? Error { get; set; }
    public List<SyncedItem> Items { get; set; } = new();
}

/// <summary>
/// Body of PUT /api/settings. Blank or missing SonarrUrl / ApiKey / OmdbApiKey mean "leave unchanged";
/// ClearOmdbApiKey removes the OMDb key. SonarrRootFolderPath / SonarrQualityProfile are different: missing means unchanged,
/// but blank is a real value (use Sonarr's first). LogLevel is one of <see cref="LogLevels.Allowed"/>.
/// </summary>
public record SettingsUpdate(
    string? ApiKey, bool AutoSyncEnabled, int SyncIntervalMinutes, bool SearchOnMonitor, bool SkipUnmonitoredSeries,
    string? SonarrUrl = null, string? OmdbApiKey = null, bool ClearOmdbApiKey = false, string? LogLevel = null,
    string? SonarrRootFolderPath = null, string? SonarrQualityProfile = null);

/// <summary>The Sonarr / OMDb settings in force, all from the database. A blank Sonarr URL falls back to the default address.</summary>
public record EffectiveSettings(
    string SonarrUrl, string SonarrApiKey, string OmdbApiKey, string SonarrRootFolderPath = "", string SonarrQualityProfile = "")
{
    public static EffectiveSettings From(AppSettings saved) => new(
        string.IsNullOrWhiteSpace(saved.SonarrUrl) ? AppSettings.DefaultSonarrUrl : saved.SonarrUrl.Trim(),
        (saved.ApiKey ?? "").Trim(),
        (saved.OmdbApiKey ?? "").Trim(),
        (saved.SonarrRootFolderPath ?? "").Trim(),
        (saved.SonarrQualityProfile ?? "").Trim());
}

public static class LogLevels
{
    /// <summary>The levels offered in the UI, most to least verbose.</summary>
    public static readonly string[] Allowed = ["Verbose", "Information", "Warning", "Error"];

    public static bool TryParse(string? value, out Serilog.Events.LogEventLevel level)
    {
        level = default;
        var match = Allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match is not null && Enum.TryParse(match, out level);
    }
}

public class CachedEpisodeRating
{
    public int Number { get; set; }
    public string? ImdbId { get; set; }
    public double? Rating { get; set; }
}

/// <summary>Cache of one season's IMDb episode ratings (fetched via OMDb) so syncs don't burn through the API quota.</summary>
public class SeasonRatings
{
    public int Id { get; set; }
    public string SeriesImdbId { get; set; } = "";
    public int Season { get; set; }
    public DateTime FetchedAt { get; set; }
    public List<CachedEpisodeRating> Episodes { get; set; } = new();
}

/// <summary>
/// One row per episode Filtarr has ever set to monitored in Sonarr. Once an episode is here Filtarr never monitors it again,
/// so an episode the user watched and deleted is not re-monitored (and re-downloaded) by a later sync.
/// </summary>
public class MonitoredEpisode
{
    public int Id { get; set; }
    /// <summary>Sonarr episode id (unique).</summary>
    public int EpisodeId { get; set; }
    public int SeriesId { get; set; }
    public string? SeriesImdbId { get; set; }
    public string SeriesTitle { get; set; } = "";
    public int Season { get; set; }
    public int Number { get; set; }
    public string? EpisodeTitle { get; set; }
    /// <summary>Name of the filter that caused the episode to be monitored.</summary>
    public string Filter { get; set; } = "";
    public DateTime MonitoredAt { get; set; }
}
