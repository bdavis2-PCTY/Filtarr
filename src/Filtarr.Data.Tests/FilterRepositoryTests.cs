using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Data.Tests;

public sealed class FilterRepositoryTests : IDisposable
{
    readonly TestDb _db = new();
    readonly IFilterRepository _repo;

    public FilterRepositoryTests() => _repo = _db.Get<IFilterRepository>();

    public void Dispose() => _db.Dispose();

    static Filter F(string name, string? imdb = null, string? title = null, bool enabled = true, FilterAction action = FilterAction.Include) => new()
    {
        Name = name, ImdbId = imdb, SeriesTitle = title, Enabled = enabled, Action = action,
        Conditions = { new() { Field = "series.year", Operator = "gte", Value = "2000" } },
    };

    [Fact]
    public async Task Add_ignores_a_supplied_id_and_round_trips_action_and_conditions()
    {
        var added = await _repo.AddAsync(new Filter { Id = 99, Name = "a", Action = FilterAction.Exclude, Conditions = F("x").Conditions });

        var loaded = await _repo.GetAsync(added.Id);
        Assert.NotEqual(99, added.Id);
        Assert.Equal(FilterAction.Exclude, loaded!.Action);
        Assert.Equal("series.year", loaded.Conditions.Single().Field);
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_id()
    {
        Assert.Null(await _repo.GetAsync(42));
    }

    [Fact]
    public async Task List_is_ordered_by_name_and_can_be_limited_to_global_or_one_series()
    {
        await _repo.AddAsync(F("b"));
        await _repo.AddAsync(F("a", "tt5"));
        await _repo.AddAsync(F("c", "tt6"));

        Assert.Equal(["a", "b", "c"], (await _repo.ListAsync(null, false)).Select(f => f.Name));
        Assert.Equal(["b"], (await _repo.ListAsync(null, true)).Select(f => f.Name));
        Assert.Equal(["a"], (await _repo.ListAsync("tt5", false)).Select(f => f.Name));
    }

    [Fact]
    public async Task Update_overwrites_every_field_and_reports_a_missing_id()
    {
        Assert.Null(await _repo.UpdateAsync(42, F("x")));

        var added = await _repo.AddAsync(F("old", "tt1", "Old"));
        var updated = await _repo.UpdateAsync(added.Id, F("new", null, null, enabled: false, action: FilterAction.Exclude));

        var loaded = (await _repo.GetAsync(added.Id))!;
        Assert.Equal(("new", null, null, false, FilterAction.Exclude), (loaded.Name, loaded.ImdbId, loaded.SeriesTitle, loaded.Enabled, loaded.Action));
        Assert.Equal("new", updated!.Name);
    }

    [Fact]
    public async Task Delete_removes_the_filter_and_reports_a_missing_id()
    {
        Assert.False(await _repo.DeleteAsync(42));

        var added = await _repo.AddAsync(F("a"));
        Assert.True(await _repo.DeleteAsync(added.Id));
        Assert.Null(await _repo.GetAsync(added.Id));
    }

    [Fact]
    public async Task Series_rows_skip_global_filters_and_can_be_limited()
    {
        await _repo.AddAsync(F("global"));
        await _repo.AddAsync(F("a", "tt1", "Alpha"));
        await _repo.AddAsync(F("b", "tt1", "Alpha", enabled: false));
        await _repo.AddAsync(F("c", "tt2", "Beta"));

        var all = await _repo.ListSeriesFilterRowsAsync(null);
        Assert.Equal(3, all.Count);
        Assert.Equal(1, all.Count(r => !r.Enabled));

        Assert.Equal(["tt2"], (await _repo.ListSeriesFilterRowsAsync(["tt2", "tt3"])).Select(r => r.ImdbId));
        Assert.Empty(await _repo.ListSeriesFilterRowsAsync([]));
    }
}
