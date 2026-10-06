using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Api.Tests;

public class NeverReMonitorTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeTracked _tracked = new();
    readonly FakeRuns _runs = new();
    readonly FakeTimeProvider _clock = new(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));

    SyncService Create(params Filter[] filters) =>
        new(_sonarr, new FakeSettings(), new FakeFilters(filters), new FilterEngine(), new FakeRatings(), _tracked, _runs, new SyncGate(),
            _clock, NullLogger<SyncService>.Instance);

    static SonarrSeries Show() => new(
        1, "Show", 2020, true, "continuing", null, "standard", null, 45, [], null, null, null, "tt1");

    static SonarrEpisode Ep(int id, int number, bool monitored = false, bool hasFile = false) =>
        new(id, 1, $"Ep{number}", 1, number, null, new DateTime(2024, 1, 1), 45, hasFile, monitored, null);

    static readonly Filter AllEpisodes = new()
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

    class CapturingStore : IMonitoredEpisodeStore
    {
        public List<MonitoredEpisode> Rows { get; } = new();
        public Task<Dictionary<int, DateTime>> GetMonitoredAtAsync(IEnumerable<int> ids, CancellationToken ct = default) => Task.FromResult(new Dictionary<int, DateTime>());
        public Task RecordAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default) { Rows.AddRange(episodes); return Task.CompletedTask; }
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

public sealed class MonitoredEpisodeStoreTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly MonitoredEpisodeStore _store;

    public MonitoredEpisodeStoreTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _store = new MonitoredEpisodeStore(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static MonitoredEpisode Row(int id, DateTime? at = null) => new()
    {
        EpisodeId = id, SeriesId = 1, SeriesTitle = "Show", Season = 1, Number = id, Filter = "f", MonitoredAt = at ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Records_and_looks_up_by_episode_id()
    {
        await _store.RecordAsync([Row(1), Row(2)]);
        var found = await _store.GetMonitoredAtAsync([2, 3, 2]);
        Assert.Equal([2], found.Keys);
        Assert.Empty(await _store.GetMonitoredAtAsync([]));
    }

    [Fact]
    public async Task Recording_an_episode_twice_keeps_the_first_record_and_does_not_throw()
    {
        var first = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _store.RecordAsync([Row(1, first)]);
        await _store.RecordAsync([Row(1, first.AddDays(30)), Row(1), Row(2)]);

        Assert.Equal(2, _db.MonitoredEpisodes.Count());
        Assert.Equal(first, (await _store.GetMonitoredAtAsync([1]))[1]);
    }

    [Fact]
    public async Task Handles_more_ids_than_one_query_batch()
    {
        await _store.RecordAsync(Enumerable.Range(1, 1200).Select(i => Row(i)));
        var found = await _store.GetMonitoredAtAsync(Enumerable.Range(1, 1500));
        Assert.Equal(1200, found.Count);
    }
}

public class MonitoredTrackingSchemaTests
{
    [Fact]
    public void Existing_databases_get_the_table_and_history_is_seeded_from_past_runs()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        // A database from before tracking: build it with the old schema by creating the current one, then dropping what is new.
        using (var seed = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options))
        {
            seed.Database.EnsureCreated();
            seed.Runs.Add(new SyncRun
            {
                StartedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), Trigger = "manual",
                Items = { new SyncedItem { EpisodeId = 11, Series = "Show", Episode = "S01E01 Pilot", Filter = "f1" } },
            });
            seed.Runs.Add(new SyncRun
            {
                StartedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), Trigger = "auto",
                Items =
                {
                    new SyncedItem { EpisodeId = 11, Series = "Show", Episode = "S01E01 Pilot", Filter = "f2" },
                    new SyncedItem { EpisodeId = 12, Series = "Show", Episode = "S01E02 Next", Filter = "f2" },
                },
            });
            seed.SaveChanges();
            seed.Database.ExecuteSqlRaw("DROP TABLE MonitoredEpisodes");
            seed.Database.ExecuteSqlRaw("ALTER TABLE Runs DROP COLUMN EpisodesSkipped");
        }

        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance); // idempotent: must not seed twice or fail

        var rows = db.MonitoredEpisodes.OrderBy(e => e.EpisodeId).ToList();
        Assert.Equal([11, 12], rows.Select(r => r.EpisodeId));
        Assert.Equal(new DateTime(2024, 1, 1), rows[0].MonitoredAt);   // earliest run wins
        Assert.Equal("f1", rows[0].Filter);
        Assert.Equal("S01E02 Next", rows[1].EpisodeTitle);
        Assert.Equal(0, db.Runs.First().EpisodesSkipped);              // new column readable by EF
    }
}
