namespace Filtarr.Core.Models;

public record MatchedEpisode(int EpisodeId, int SeriesId, string Series, int Season, int Number, string? Title,
    DateTime? AirDate, bool Monitored, bool HasFile, string Filter, string? EpisodeImdbId = null, double? Rating = null,
    string? SeriesImdbId = null, DateTime? PreviouslyMonitoredAt = null);

public record PreviewResult(int SeriesScanned, List<MatchedEpisode> Matches);

/// <summary>What a set of filters would do to one series in Sonarr.</summary>
public record SeriesTestResult(
    bool InSonarr, string? SonarrTitle, bool SeriesMonitored, bool SkippedBecauseUnmonitored,
    int TotalEpisodes, List<MatchedEpisode> Matches, string? Warning = null);

/// <summary>What running one rule did. When <see cref="Error"/> is set, the counts show what was done before it failed.</summary>
public record RuleRunResult(
    string RuleName = "", bool InSonarr = false, string? SeriesTitle = null, bool SkippedBecauseSeriesUnmonitored = false,
    int Matched = 0, int Monitored = 0, int AlreadyMonitored = 0, int SkippedPreviouslyMonitored = 0,
    List<MatchedEpisode>? NewlyMonitored = null, string? Warning = null, string? Error = null);

/// <summary>An episode's IMDb id and rating, from the OMDb cache.</summary>
public record EpisodeRating(string? ImdbId, double? Rating);
