using Filtarr.Core.Models;
using Filtarr.Data.Models;

namespace Filtarr.Core.Mapping;

/// <summary>
/// The only place database entities and API models meet. Services read entities from the repositories and hand callers models,
/// so an entity never leaves the library.
/// </summary>
internal static class EntityMappings
{
    public static FilterActionType ToModel(this FilterAction action) =>
        action == FilterAction.Exclude ? FilterActionType.Exclude : FilterActionType.Include;

    public static FilterAction ToEntity(this FilterActionType action) =>
        action == FilterActionType.Exclude ? FilterAction.Exclude : FilterAction.Include;

    static FilterConditionModel ToModel(this FilterCondition c) => new() { Field = c.Field, Operator = c.Operator, Value = c.Value };

    static FilterCondition ToEntity(this FilterConditionModel c) => new() { Field = c.Field, Operator = c.Operator, Value = c.Value };

    public static FilterModel ToModel(this Filter f) => new()
    {
        Id = f.Id, Name = f.Name, Enabled = f.Enabled, Action = f.Action.ToModel(), ImdbId = f.ImdbId, SeriesTitle = f.SeriesTitle,
        Conditions = f.Conditions.Select(c => c.ToModel()).ToList(),
    };

    public static Filter ToEntity(this FilterModel f) => new()
    {
        Id = f.Id, Name = f.Name, Enabled = f.Enabled, Action = f.Action.ToEntity(), ImdbId = f.ImdbId, SeriesTitle = f.SeriesTitle,
        Conditions = f.Conditions.Select(c => c.ToEntity()).ToList(),
    };

    public static QuickFilterModel ToModel(this QuickFilter q) => new()
    {
        Id = q.Id, Name = q.Name, Action = q.Action.ToModel(), Conditions = q.Conditions.Select(c => c.ToModel()).ToList(),
    };

    public static QuickFilter ToEntity(this QuickFilterModel q) => new()
    {
        Id = q.Id, Name = q.Name, Action = q.Action.ToEntity(), Conditions = q.Conditions.Select(c => c.ToEntity()).ToList(),
    };

    public static SyncRunModel ToModel(this SyncRun r) => new()
    {
        Id = r.Id, StartedAt = r.StartedAt, FinishedAt = r.FinishedAt, Trigger = r.Trigger, SeriesScanned = r.SeriesScanned,
        EpisodesMatched = r.EpisodesMatched, EpisodesMonitored = r.EpisodesMonitored, EpisodesSkipped = r.EpisodesSkipped, Error = r.Error,
        Items = r.Items.Select(i => new SyncedItemModel { EpisodeId = i.EpisodeId, Series = i.Series, Episode = i.Episode, Filter = i.Filter }).ToList(),
    };

    public static SettingsModel ToModel(this AppSettings s) => new()
    {
        SonarrUrl = s.SonarrUrl, ApiKey = s.ApiKey, OmdbApiKey = s.OmdbApiKey, SonarrRootFolderPath = s.SonarrRootFolderPath,
        SonarrQualityProfile = s.SonarrQualityProfile, LogLevel = s.LogLevel, AutoSyncEnabled = s.AutoSyncEnabled,
        SyncIntervalMinutes = s.SyncIntervalMinutes, SearchOnMonitor = s.SearchOnMonitor, SkipUnmonitoredSeries = s.SkipUnmonitoredSeries,
    };
}
