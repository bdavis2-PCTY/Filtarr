using Filtarr.Data.Migrations;
using Filtarr.Data.Models;
using Filtarr.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Data.Tests;

public sealed class MonitoredEpisodeRepositoryTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly MonitoredEpisodeRepository _repo;

    public MonitoredEpisodeRepositoryTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _repo = new MonitoredEpisodeRepository(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static MonitoredEpisode Row(int id, DateTime? at = null) => new()
    {
        EpisodeId = id, SeriesId = 1, SeriesTitle = "Show", Season = 1, Number = id, Filter = "f", MonitoredAt = at ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Records_and_looks_up_by_episode_id()
    {
        await _repo.AddMissingAsync([Row(1), Row(2)]);
        var found = await _repo.GetMonitoredAtAsync([2, 3, 2]);
        Assert.Equal([2], found.Keys);
        Assert.Empty(await _repo.GetMonitoredAtAsync([]));
    }

    [Fact]
    public async Task Recording_an_episode_twice_keeps_the_first_record_and_does_not_throw()
    {
        var first = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _repo.AddMissingAsync([Row(1, first)]);
        await _repo.AddMissingAsync([Row(1, first.AddDays(30)), Row(1), Row(2)]);

        Assert.Equal(2, _db.MonitoredEpisodes.Count());
        Assert.Equal(first, (await _repo.GetMonitoredAtAsync([1]))[1]);
    }

    [Fact]
    public async Task Handles_more_ids_than_one_query_batch()
    {
        await _repo.AddMissingAsync(Enumerable.Range(1, 1200).Select(i => Row(i)));
        var found = await _repo.GetMonitoredAtAsync(Enumerable.Range(1, 1500));
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
