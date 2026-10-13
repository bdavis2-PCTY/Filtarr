namespace Filtarr.Core.Models;

public class SyncedItemModel
{
    public int EpisodeId { get; set; }
    public string Series { get; set; } = "";
    public string Episode { get; set; } = "";
    public string Filter { get; set; } = "";
}

public class SyncRunModel
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
    public List<SyncedItemModel> Items { get; set; } = new();
}
