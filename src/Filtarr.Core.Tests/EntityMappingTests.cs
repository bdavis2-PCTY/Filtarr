using Filtarr.Core.Mapping;
using Filtarr.Core.Models;
using Filtarr.Data.Models;

namespace Filtarr.Core.Tests;

public class EntityMappingTests
{
    [Fact]
    public void A_filter_survives_the_round_trip_through_the_model()
    {
        var entity = new Filter
        {
            Id = 7, Name = "n", Enabled = false, Action = FilterAction.Exclude, ImdbId = "tt1", SeriesTitle = "Show",
            Conditions = { new FilterCondition { Field = "series.year", Operator = "gte", Value = "2000" } },
        };

        var model = entity.ToModel();
        Assert.Equal((7, "n", false, FilterActionType.Exclude, "tt1", "Show"), (model.Id, model.Name, model.Enabled, model.Action, model.ImdbId, model.SeriesTitle));
        Assert.Equal(("series.year", "gte", "2000"), (model.Conditions.Single().Field, model.Conditions.Single().Operator, model.Conditions.Single().Value));

        var back = model.ToEntity();
        Assert.Equal((7, "n", false, FilterAction.Exclude, "tt1", "Show"), (back.Id, back.Name, back.Enabled, back.Action, back.ImdbId, back.SeriesTitle));
        Assert.Equal("2000", back.Conditions.Single().Value);
    }

    [Fact]
    public void A_quick_filter_survives_the_round_trip_through_the_model()
    {
        var model = new QuickFilter
        {
            Id = 3, Name = "q", Action = FilterAction.Include, Conditions = { new FilterCondition { Field = "episode.rating", Operator = "gt", Value = "8" } },
        }.ToModel();

        var back = model.ToEntity();
        Assert.Equal((3, "q", FilterAction.Include, "8"), (back.Id, back.Name, back.Action, back.Conditions.Single().Value));
    }

    [Fact]
    public void A_sync_run_keeps_its_counts_and_items()
    {
        var run = new SyncRun
        {
            Id = 5, Trigger = "auto", SeriesScanned = 2, EpisodesMatched = 3, EpisodesMonitored = 1, EpisodesSkipped = 2, Error = "boom",
            Items = { new SyncedItem { EpisodeId = 11, Series = "Show", Episode = "S01E01", Filter = "f" } },
        };

        var model = run.ToModel();
        Assert.Equal((5, "auto", 2, 3, 1, 2, "boom"), (model.Id, model.Trigger, model.SeriesScanned, model.EpisodesMatched, model.EpisodesMonitored, model.EpisodesSkipped, model.Error));
        Assert.Equal(11, model.Items.Single().EpisodeId);
    }

    [Fact]
    public void Settings_models_carry_the_user_editable_values()
    {
        var model = new AppSettings
        {
            SonarrUrl = "http://nas:8989", ApiKey = "key", OmdbApiKey = "omdb", SonarrRootFolderPath = "M:\\TV", SonarrQualityProfile = "HD",
            LogLevel = "Information", AutoSyncEnabled = true, SyncIntervalMinutes = 15, SearchOnMonitor = false, SkipUnmonitoredSeries = false,
        }.ToModel();

        Assert.Equal(("http://nas:8989", "key", "omdb", "M:\\TV", "HD", "Information", true, 15, false, false),
            (model.SonarrUrl, model.ApiKey, model.OmdbApiKey, model.SonarrRootFolderPath, model.SonarrQualityProfile, model.LogLevel,
             model.AutoSyncEnabled, model.SyncIntervalMinutes, model.SearchOnMonitor, model.SkipUnmonitoredSeries));
    }
}
