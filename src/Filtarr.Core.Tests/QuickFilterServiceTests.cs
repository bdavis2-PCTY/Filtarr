using Filtarr.Core.Models;
using Filtarr.Core.Services;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Core.Tests;

public sealed class QuickFilterServiceTests : IDisposable
{
    readonly TestDb _db = new();
    readonly QuickFilterService _svc;

    public QuickFilterServiceTests() => _svc = new QuickFilterService(_db.Get<IQuickFilterRepository>());

    public void Dispose() => _db.Dispose();

    static QuickFilterModel Q(string name, FilterActionType action = FilterActionType.Include) =>
        new() { Name = name, Action = action, Conditions = { new() { Field = "episode.rating", Operator = "gte", Value = "9" } } };

    [Fact]
    public async Task Create_persists_conditions_and_action_and_ignores_supplied_id()
    {
        var created = await _svc.CreateAsync(new QuickFilterModel { Id = 99, Name = "High", Action = FilterActionType.Exclude, Conditions = Q("x").Conditions });
        var loaded = (await _svc.ListAsync()).Single();
        Assert.NotEqual(99, created.Id);
        Assert.Equal(FilterActionType.Exclude, loaded.Action);
        Assert.Equal("9", loaded.Conditions.Single().Value);
    }

    [Fact]
    public async Task List_is_ordered_by_name()
    {
        await _svc.CreateAsync(Q("b"));
        await _svc.CreateAsync(Q("a"));
        Assert.Equal(["a", "b"], (await _svc.ListAsync()).Select(f => f.Name));
    }

    [Fact]
    public async Task Update_and_delete_work_and_report_missing_ids()
    {
        Assert.Null(await _svc.UpdateAsync(42, Q("x")));
        Assert.False(await _svc.DeleteAsync(42));

        var f = await _svc.CreateAsync(Q("old"));
        var updated = await _svc.UpdateAsync(f.Id, Q("new", FilterActionType.Exclude));
        Assert.Equal("new", updated!.Name);
        Assert.Equal(FilterActionType.Exclude, (await _svc.ListAsync()).Single().Action);
        Assert.True(await _svc.DeleteAsync(f.Id));
        Assert.Empty(await _svc.ListAsync());
    }
}
