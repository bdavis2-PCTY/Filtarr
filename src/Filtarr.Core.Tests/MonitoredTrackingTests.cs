using Filtarr.Core.Clients.Models;
using Filtarr.Core.Filtering;
using Filtarr.Core.Models;
using Filtarr.Core.Services;
using Filtarr.Core.Synchronization;
using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Core.Tests;

public class NeverReMonitorTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeTracked _tracked = new();
    readonly FakeRuns _runs = new();
    readonly FakeTimeProvider _clock = new(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));

    SyncService Create(params FilterModel[] filters) =>
        new(_sonarr, new FakeSettings(), new FakeFilters(filters), new FilterEngine(), new FakeRatings(), _tracked, _runs, new SyncGate(),
            _clock, NullLogger<SyncService>.Instance);

    static SonarrSeries Show() => new(
        1, "Show", 2020, true, "continuing", null, "standard", null, 45, [], null, null, null, "tt1");

    static SonarrEpisode Ep(int id, int number, bool monitored = false, bool hasFile = false) =>
        new(id, 1, $"Ep{number}", 1, number, null, new DateTime(2024, 1, 1), 45, hasFile, monitored, null);

    static readonly FilterModel AllEpisodes = new()
    {
        Name = "all", Conditions = { new() { Field = "episode.season", Operator = "gte", Value = "1" } },
    };

    [Fact]
    public async Task Episodes_are_recorded_when_monitored_and_never_monitored_again()
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = [Ep(11, 1), Ep(12, 2)];
        var svc = Create(AllEpisodes);

        var first = await svc.RunAsync("test");
        Assert.Equal(2, first.EpisodesMonitored);
        Assert.Equal([11, 12], _tracked.Monitored.Keys.Order());
        Assert.Equal([11, 12], _sonarr.Monitored.Order());

        // The user watched and deleted episode 11, so Sonarr has it unmonitored again.
        _sonarr.Monitored.Clear();
        _sonarr.Searched.Clear();
        var second = await svc.RunAsync("test");

        Assert.Equal(0, second.EpisodesMonitored);
        Assert.Equal(2, second.EpisodesSkipped);
        Assert.Empty(_sonarr.Monitored);
        Assert.Empty(_sonarr.Searched);
    }

    [Fact]
    public async Task Only_previously_monitored_episodes_are_skipped()
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = [Ep(11, 1), Ep(12, 2)];
        _tracked.Monitored[11] = new DateTime(2024, 1, 1);

        var run = await Create(AllEpisodes).RunAsync("test");

        Assert.Equal([12], _sonarr.Monitored);
        Assert.Equal(1, run.EpisodesSkipped);
        Assert.Equal(1, run.EpisodesMonitored);
        Assert.Equal(2, run.EpisodesMatched);
    }

    [Fact]
    public async Task Episodes_already_monitored_in_sonarr_are_neither_recorded_nor_counted_as_skipped()
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = [Ep(11, 1, monitored: true)];

        var run = await Create(AllEpisodes).RunAsync("test");

        Assert.Empty(_tracked.Monitored);
        Assert.Equal(0, run.EpisodesSkipped);
    }

    [Fact]
    public async Task Preview_and_test_flag_previously_monitored_episodes()
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = [Ep(11, 1), Ep(12, 2)];
        var when = new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc);
        _tracked.Monitored[11] = when;
        var svc = Create(AllEpisodes);

        var preview = await svc.PreviewAsync(null);
        Assert.Equal(when, preview.Matches.Single(m => m.EpisodeId == 11).PreviouslyMonitoredAt);
        Assert.Null(preview.Matches.Single(m => m.EpisodeId == 12).PreviouslyMonitoredAt);

        var test = await svc.TestSeriesAsync("tt1", null);
        Assert.Equal(when, test.Matches.Single(m => m.EpisodeId == 11).PreviouslyMonitoredAt);
        Assert.Empty(_tracked.RecordedBatches); // previews and tests never record anything
    }

    [Fact]
    public async Task Recorded_rows_carry_series_episode_filter_and_time()
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = [Ep(11, 1)];
        var store = new CapturingStore();
        var svc = new SyncService(_sonarr, new FakeSettings(), new FakeFilters(AllEpisodes), new FilterEngine(), new FakeRatings(), store, _runs,
            new SyncGate(), _clock, NullLogger<SyncService>.Instance);

        await svc.RunAsync("test");

        var row = Assert.Single(store.Rows);
        Assert.Equal((11, 1, "tt1", "Show", 1, 1, "Ep1", "all"), (row.EpisodeId, row.SeriesId, row.SeriesImdbId, row.SeriesTitle, row.Season, row.Number, row.EpisodeTitle, row.Filter));
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.MonitoredAt);
    }

    class CapturingStore : IMonitoredEpisodeRepository
    {
        public List<MonitoredEpisode> Rows { get; } = new();
        public Task<Dictionary<int, DateTime>> GetMonitoredAtAsync(IEnumerable<int> ids, CancellationToken ct = default) => Task.FromResult(new Dictionary<int, DateTime>());
        public Task AddMissingAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default) { Rows.AddRange(episodes); return Task.CompletedTask; }
        public Task<List<MonitoredEpisode>> ListAsync(CancellationToken ct = default) => Task.FromResult(Rows.ToList());
        public Task<int> CountForSeriesAsync(SeriesKey series, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RemoveForSeriesAsync(SeriesKey series, CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task A_failure_part_way_keeps_the_batches_that_already_succeeded_tracked()
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = Enumerable.Range(1, 150).Select(i => Ep(1000 + i, i)).ToList();
        _sonarr.FailMonitorOnCall = 2; // second batch of 100 fails
        _sonarr.FailMonitorWith = new HttpRequestException("boom");

        var run = await Create(AllEpisodes).RunAsync("test");

        Assert.Equal("boom", run.Error);
        Assert.Equal(100, run.EpisodesMonitored);
        Assert.Equal(100, _tracked.Monitored.Count);
        Assert.Equal(100, _sonarr.Monitored.Count);
    }
}
