namespace Filtarr.Data.Models;

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
