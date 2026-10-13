using Filtarr.Data.Models;
using Filtarr.Data.Repositories;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Filtarr.Data.Tests;

public sealed class MonitoredEpisodeRepositoryResetTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly MonitoredEpisodeRepository _repo;
    int _next = 1;

    public MonitoredEpisodeRepositoryResetTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _repo = new MonitoredEpisodeRepository(_db);
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
        await _repo.AddMissingAsync([Row("Show A", "tt1", 10), Row("Show A", "tt1", 10), Row("Show B", "tt2", 20), Row("Show C", "tt3", 30)]);

        Assert.Equal(2, await _repo.CountForSeriesAsync(new SeriesKey("tt1")));
        Assert.Equal(2, await _repo.RemoveForSeriesAsync(new SeriesKey("tt1")));

        Assert.Equal(["Show B/3", "Show C/4"], await RemainingTitles());
        Assert.Equal(0, await _repo.CountForSeriesAsync(new SeriesKey("tt1")));
    }

    [Fact]
    public async Task Rows_without_an_imdb_id_match_by_sonarr_series_id()
    {
        await _repo.AddMissingAsync([Row("Show A", null, 10), Row("Show B", null, 20)]);
        Assert.Equal(1, await _repo.RemoveForSeriesAsync(new SeriesKey("tt1", SonarrId: 10)));
        Assert.Equal(["Show B/2"], await RemainingTitles());
    }

    [Fact]
    public async Task Rows_seeded_from_old_runs_match_by_title_ignoring_case()
    {
        // Seeded rows have no series id and no IMDb id, only the title.
        await _repo.AddMissingAsync([Row("Breaking Bad"), Row("Breaking Bad"), Row("Other Show")]);

        Assert.Equal(2, await _repo.RemoveForSeriesAsync(new SeriesKey("tt1", 1, "  breaking BAD ")));
        Assert.Equal(["Other Show/3"], await RemainingTitles());
    }

    [Fact]
    public async Task A_title_alone_never_removes_rows_that_belong_to_a_known_series()
    {
        // Same title, but recorded for a different series (it has an id): must survive.
        await _repo.AddMissingAsync([Row("Doctor Who", "tt-1963", 7), Row("Doctor Who", "tt-2005", 8)]);

        Assert.Equal(1, await _repo.RemoveForSeriesAsync(new SeriesKey("tt-2005", 8, "Doctor Who")));
        Assert.Equal(["Doctor Who/1"], await RemainingTitles());
    }

    [Fact]
    public async Task Without_a_sonarr_match_only_the_imdb_id_is_used_and_missing_series_remove_nothing()
    {
        await _repo.AddMissingAsync([Row("Show A", null, 10), Row("Show B", "tt2", 20)]);
        Assert.Equal(1, await _repo.RemoveForSeriesAsync(new SeriesKey("tt2")));
        Assert.Equal(0, await _repo.RemoveForSeriesAsync(new SeriesKey("tt999")));
        Assert.Equal(["Show A/1"], await RemainingTitles());
    }

    [Fact]
    public async Task After_a_reset_the_episodes_can_be_recorded_again()
    {
        var first = Row("Show A", "tt1", 10);
        await _repo.AddMissingAsync([first]);
        await _repo.RemoveForSeriesAsync(new SeriesKey("tt1"));
        await _repo.AddMissingAsync([Row("Show A", "tt1", 10)]);
        Assert.Equal(1, await _repo.CountForSeriesAsync(new SeriesKey("tt1")));
    }
}
