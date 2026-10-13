namespace Filtarr.Data.Models;

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
