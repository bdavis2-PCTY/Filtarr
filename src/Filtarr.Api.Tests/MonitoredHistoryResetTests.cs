using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Api.Tests;

public sealed class MonitoredHistoryStoreResetTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly MonitoredEpisodeStore _store;
    int _next = 1;

    public MonitoredHistoryStoreResetTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _store = new MonitoredEpisodeStore(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    MonitoredEpisode Row(string title, string? imdb = null, int seriesId = 0) => new()
    {
        EpisodeId = _next++, SeriesId = seriesId, SeriesImdbId = imdb, SeriesTitle = title, Season = 1, Number = _next,
        Filter = "f", MonitoredAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    async Task<List<string>> RemainingTitles()
    {
        _db.ChangeTracker.Clear();
        return await _db.MonitoredEpisodes.OrderBy(e => e.EpisodeId).Select(e => e.SeriesTitle + "/" + e.EpisodeId).ToListAsync();
    }

    [Fact]
    public async Task Only_the_given_series_is_counted_and_removed()
    {
        await _store.RecordAsync([Row("Show A", "tt1", 10), Row("Show A", "tt1", 10), Row("Show B", "tt2", 20), Row("Show C", "tt3", 30)]);

        Assert.Equal(2, await _store.CountForSeriesAsync(new SeriesKey("tt1")));
        Assert.Equal(2, await _store.RemoveForSeriesAsync(new SeriesKey("tt1")));

        Assert.Equal(["Show B/3", "Show C/4"], await RemainingTitles());
        Assert.Equal(0, await _store.CountForSeriesAsync(new SeriesKey("tt1")));
    }

    [Fact]
    public async Task Rows_without_an_imdb_id_match_by_sonarr_series_id()
    {
        await _store.RecordAsync([Row("Show A", null, 10), Row("Show B", null, 20)]);
        Assert.Equal(1, await _store.RemoveForSeriesAsync(new SeriesKey("tt1", SonarrId: 10)));
        Assert.Equal(["Show B/2"], await RemainingTitles());
    }

    [Fact]
    public async Task Rows_seeded_from_old_runs_match_by_title_ignoring_case()
    {
        // Seeded rows have no series id and no IMDb id, only the title.
        await _store.RecordAsync([Row("Breaking Bad"), Row("Breaking Bad"), Row("Other Show")]);

        Assert.Equal(2, await _store.RemoveForSeriesAsync(new SeriesKey("tt1", 1, "  breaking BAD ")));
        Assert.Equal(["Other Show/3"], await RemainingTitles());
    }

    [Fact]
    public async Task A_title_alone_never_removes_rows_that_belong_to_a_known_series()
    {
        // Same title, but recorded for a different series (it has an id): must survive.
        await _store.RecordAsync([Row("Doctor Who", "tt-1963", 7), Row("Doctor Who", "tt-2005", 8)]);

        Assert.Equal(1, await _store.RemoveForSeriesAsync(new SeriesKey("tt-2005", 8, "Doctor Who")));
        Assert.Equal(["Doctor Who/1"], await RemainingTitles());
    }

    [Fact]
    public async Task Without_a_sonarr_match_only_the_imdb_id_is_used_and_missing_series_remove_nothing()
    {
        await _store.RecordAsync([Row("Show A", null, 10), Row("Show B", "tt2", 20)]);
        Assert.Equal(1, await _store.RemoveForSeriesAsync(new SeriesKey("tt2")));
        Assert.Equal(0, await _store.RemoveForSeriesAsync(new SeriesKey("tt999")));
        Assert.Equal(["Show A/1"], await RemainingTitles());
    }

    [Fact]
    public async Task After_a_reset_the_episodes_can_be_recorded_again()
    {
        var first = Row("Show A", "tt1", 10);
        await _store.RecordAsync([first]);
        await _store.RemoveForSeriesAsync(new SeriesKey("tt1"));
        await _store.RecordAsync([Row("Show A", "tt1", 10)]);
        Assert.Equal(1, await _store.CountForSeriesAsync(new SeriesKey("tt1")));
    }
}

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
        var filter = new Filter { Name = "all", Conditions = { new() { Field = "episode.season", Operator = "gte", Value = "1" } } };
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
