using Filtarr.Core.Filtering;
using Filtarr.Core.Models;
using Filtarr.Core.Services;
using Filtarr.Core.Synchronization;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Core.Tests;

public class MonitoredHistoryServiceTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeTracked _tracked = new();

    MonitoredHistoryService Create() => new(_tracked, _sonarr, NullLogger<MonitoredHistoryService>.Instance);

    [Fact]
    public async Task Uses_the_sonarr_id_and_title_when_the_series_is_in_the_library()
    {
        _sonarr.Series.Add(new(42, "Breaking Bad", 2008, true, null, null, null, null, 45, [], null, null, null, "tt0903747"));
        _tracked.CountResult = 7;

        Assert.Equal(7, await Create().ResetAsync("tt0903747"));
        Assert.Equal(new SeriesKey("tt0903747", 42, "Breaking Bad"), Assert.Single(_tracked.Resets));
    }

    [Fact]
    public async Task Falls_back_to_the_imdb_id_when_the_series_is_not_in_sonarr_or_sonarr_is_down()
    {
        await Create().ResetAsync("tt1");
        Assert.Equal(new SeriesKey("tt1"), _tracked.Resets[0]);

        _sonarr.FailLibrary = new HttpRequestException("down");
        await Create().ResetAsync("tt2");
        Assert.Equal(new SeriesKey("tt2"), _tracked.Resets[1]);
    }

    [Fact]
    public async Task Counting_does_not_reset_anything()
    {
        _tracked.CountResult = 3;
        Assert.Equal(3, await Create().CountAsync("tt1"));
        Assert.Empty(_tracked.Resets);
    }
}

/// <summary>The point of resetting: an episode Filtarr skipped before is monitored again by the next sync.</summary>
public class ResetThenSyncTests
{
    [Fact]
    public async Task After_a_reset_the_next_sync_monitors_and_searches_the_episode_again()
    {
        var sonarr = new FakeSonarr();
        sonarr.Series.Add(new(1, "Show", 2020, true, "continuing", null, "standard", null, 45, [], null, null, null, "tt1"));
        sonarr.Episodes[1] = [new(11, 1, "Ep1", 1, 1, null, new DateTime(2024, 1, 1), 45, false, false, null)];
        var tracked = new FakeTracked();
        var filter = new FilterModel { Name = "all", Conditions = { new() { Field = "episode.season", Operator = "gte", Value = "1" } } };
        var sync = new SyncService(sonarr, new FakeSettings(), new FakeFilters(filter), new FilterEngine(), new FakeRatings(), tracked, new FakeRuns(),
            new SyncGate(), new FakeTimeProvider(), NullLogger<SyncService>.Instance);

        await sync.RunAsync("test");
        Assert.Equal([11], sonarr.Monitored);

        // Watched and deleted: Sonarr has it unmonitored again, Filtarr remembers and leaves it alone...
        sonarr.Monitored.Clear();
        sonarr.Searched.Clear();
        Assert.Equal(1, (await sync.RunAsync("test")).EpisodesSkipped);
        Assert.Empty(sonarr.Monitored);

        // ...until the history is reset.
        tracked.Monitored.Clear();
        var third = await sync.RunAsync("test");
        Assert.Equal(1, third.EpisodesMonitored);
        Assert.Equal([11], sonarr.Monitored);
        Assert.Equal([11], sonarr.Searched);
    }
}
