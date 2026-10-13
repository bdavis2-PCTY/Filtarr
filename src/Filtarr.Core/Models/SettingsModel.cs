namespace Filtarr.Core.Models;

/// <summary>The user-editable settings as saved. Holds secrets (API keys), so the API never serialises it as it is.</summary>
public class SettingsModel
{
    public const string DefaultSonarrUrl = "http://localhost:8989";

    public string SonarrUrl { get; set; } = DefaultSonarrUrl;
    public string ApiKey { get; set; } = "";
    public string OmdbApiKey { get; set; } = "";
    /// <summary>Root folder for series added from the UI; empty = the first root folder in Sonarr.</summary>
    public string SonarrRootFolderPath { get; set; } = "";
    /// <summary>Quality profile name for series added from the UI; empty = the first profile in Sonarr.</summary>
    public string SonarrQualityProfile { get; set; } = "";
    /// <summary>Saved log level; null = whatever the Serilog configuration says (Warning by default).</summary>
    public string? LogLevel { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int SyncIntervalMinutes { get; set; } = 60;
    /// <summary>Trigger an EpisodeSearch in Sonarr for newly monitored episodes that have no file.</summary>
    public bool SearchOnMonitor { get; set; } = true;
    public bool SkipUnmonitoredSeries { get; set; } = true;
}

/// <summary>
/// Body of PUT /api/settings. Blank or missing SonarrUrl / ApiKey / OmdbApiKey mean "leave unchanged";
/// ClearOmdbApiKey removes the OMDb key. SonarrRootFolderPath / SonarrQualityProfile are different: missing means unchanged,
/// but blank is a real value (use Sonarr's first). LogLevel is one of <see cref="Configuration.LogLevels.Allowed"/>.
/// </summary>
public record SettingsUpdate(
    string? ApiKey, bool AutoSyncEnabled, int SyncIntervalMinutes, bool SearchOnMonitor, bool SkipUnmonitoredSeries,
    string? SonarrUrl = null, string? OmdbApiKey = null, bool ClearOmdbApiKey = false, string? LogLevel = null,
    string? SonarrRootFolderPath = null, string? SonarrQualityProfile = null);

public record SettingsUpdateResult(bool Ok, string? Error = null);

/// <summary>The Sonarr / OMDb settings in force, all from the database. A blank Sonarr URL falls back to the default address.</summary>
public record EffectiveSettings(
    string SonarrUrl, string SonarrApiKey, string OmdbApiKey, string SonarrRootFolderPath = "", string SonarrQualityProfile = "")
{
    public static EffectiveSettings From(SettingsModel saved) => new(
        string.IsNullOrWhiteSpace(saved.SonarrUrl) ? SettingsModel.DefaultSonarrUrl : saved.SonarrUrl.Trim(),
        (saved.ApiKey ?? "").Trim(),
        (saved.OmdbApiKey ?? "").Trim(),
        (saved.SonarrRootFolderPath ?? "").Trim(),
        (saved.SonarrQualityProfile ?? "").Trim());
}

/// <summary>Outcome of testing a connection (Sonarr or OMDb) from the Settings page.</summary>
public record ConnectionTestResult(bool Ok, string Message);

/// <summary>What Sonarr offers for the Settings page dropdowns.</summary>
public record SonarrOptions(List<string> RootFolders, List<string> QualityProfiles);
