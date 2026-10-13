using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Data.Tests;

public sealed class SeasonRatingsRepositoryTests : IDisposable
{
    readonly TestDb _db = new();
    readonly ISeasonRatingsRepository _repo;
    static readonly DateTime T0 = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    public SeasonRatingsRepositoryTests() => _repo = _db.Get<ISeasonRatingsRepository>();

    public void Dispose() => _db.Dispose();

    static List<CachedEpisodeRating> Episodes(double rating) => [new() { Number = 1, ImdbId = "tt9", Rating = rating }];

    [Fact]
    public async Task Save_creates_the_row_and_get_finds_it_by_series_and_season()
    {
        await _repo.SaveAsync("tt1", 1, T0, Episodes(8.5));

        var loaded = await _repo.GetAsync("tt1", 1);
        Assert.Equal((T0, 8.5), (loaded!.FetchedAt, loaded.Episodes.Single().Rating));
        Assert.Null(await _repo.GetAsync("tt1", 2));
        Assert.Null(await _repo.GetAsync("tt2", 1));
    }

    [Fact]
    public async Task Saving_the_same_season_again_updates_it_in_place()
    {
        var first = await _repo.SaveAsync("tt1", 1, T0, Episodes(8.5));
        var second = await _repo.SaveAsync("tt1", 1, T0.AddDays(2), Episodes(9.0));

        Assert.Equal(first.Id, second.Id);
        var all = await _repo.ListAsync();
        Assert.Equal((T0.AddDays(2), 9.0), (all.Single().FetchedAt, all.Single().Episodes.Single().Rating));
    }

    [Fact]
    public async Task List_is_ordered_by_series_then_season()
    {
        await _repo.SaveAsync("tt2", 1, T0, Episodes(1));
        await _repo.SaveAsync("tt1", 2, T0, Episodes(1));
        await _repo.SaveAsync("tt1", 1, T0, Episodes(1));

        Assert.Equal([("tt1", 1), ("tt1", 2), ("tt2", 1)], (await _repo.ListAsync()).Select(r => (r.SeriesImdbId, r.Season)));
    }

    [Fact]
    public async Task Delete_removes_one_season_and_reports_a_missing_one()
    {
        await _repo.SaveAsync("tt1", 1, T0, Episodes(1));
        await _repo.SaveAsync("tt1", 2, T0, Episodes(1));

        Assert.True(await _repo.DeleteAsync("tt1", 1));
        Assert.False(await _repo.DeleteAsync("tt1", 1));
        Assert.Equal([2], (await _repo.ListAsync()).Select(r => r.Season));
    }
}
