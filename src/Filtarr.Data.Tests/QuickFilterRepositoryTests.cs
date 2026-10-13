using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Data.Tests;

public sealed class QuickFilterRepositoryTests : IDisposable
{
    readonly TestDb _db = new();
    readonly IQuickFilterRepository _repo;

    public QuickFilterRepositoryTests() => _repo = _db.Get<IQuickFilterRepository>();

    public void Dispose() => _db.Dispose();

    static QuickFilter Q(string name, FilterAction action = FilterAction.Include) =>
        new() { Name = name, Action = action, Conditions = { new() { Field = "episode.rating", Operator = "gte", Value = "9" } } };

    [Fact]
    public async Task Add_ignores_a_supplied_id_and_round_trips_action_and_conditions()
    {
        var added = await _repo.AddAsync(new QuickFilter { Id = 99, Name = "High", Action = FilterAction.Exclude, Conditions = Q("x").Conditions });

        var loaded = await _repo.GetAsync(added.Id);
        Assert.NotEqual(99, added.Id);
        Assert.Equal(FilterAction.Exclude, loaded!.Action);
        Assert.Equal("9", loaded.Conditions.Single().Value);
    }

    [Fact]
    public async Task List_is_ordered_by_name()
    {
        await _repo.AddAsync(Q("b"));
        await _repo.AddAsync(Q("a"));
        Assert.Equal(["a", "b"], (await _repo.ListAsync()).Select(f => f.Name));
    }

    [Fact]
    public async Task Update_and_delete_work_and_report_missing_ids()
    {
        Assert.Null(await _repo.UpdateAsync(42, Q("x")));
        Assert.False(await _repo.DeleteAsync(42));

        var added = await _repo.AddAsync(Q("old"));
        await _repo.UpdateAsync(added.Id, Q("new", FilterAction.Exclude));
        var loaded = (await _repo.GetAsync(added.Id))!;
        Assert.Equal(("new", FilterAction.Exclude), (loaded.Name, loaded.Action));

        Assert.True(await _repo.DeleteAsync(added.Id));
        Assert.Empty(await _repo.ListAsync());
    }
}
