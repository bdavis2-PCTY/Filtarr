using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Api.Tests;

public sealed class QuickFilterServiceTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly QuickFilterService _svc;

    public QuickFilterServiceTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _svc = new QuickFilterService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    static QuickFilter Q(string name, FilterAction action = FilterAction.Include) =>
        new() { Name = name, Action = action, Conditions = { new() { Field = "episode.rating", Operator = "gte", Value = "9" } } };

    [Fact]
    public async Task Create_persists_conditions_and_action_and_ignores_supplied_id()
    {
        var created = await _svc.CreateAsync(new QuickFilter { Id = 99, Name = "High", Action = FilterAction.Exclude, Conditions = Q("x").Conditions });
        var loaded = (await _svc.ListAsync()).Single();
        Assert.NotEqual(99, created.Id);
        Assert.Equal(FilterAction.Exclude, loaded.Action);
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
        var updated = await _svc.UpdateAsync(f.Id, Q("new", FilterAction.Exclude));
        Assert.Equal("new", updated!.Name);
        Assert.Equal(FilterAction.Exclude, (await _svc.ListAsync()).Single().Action);
        Assert.True(await _svc.DeleteAsync(f.Id));
        Assert.Empty(await _svc.ListAsync());
    }

    [Fact]
    public void Upgrade_creates_the_table_for_existing_databases()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("DROP TABLE QuickFilters"); // a database from before Quick Filters existed
        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance); // idempotent
        Assert.Empty(db.QuickFilters.ToList());
    }
}
