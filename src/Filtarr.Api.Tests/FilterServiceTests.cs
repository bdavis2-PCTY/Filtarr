using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Tests;

public sealed class FilterServiceTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly FilterService _svc;

    public FilterServiceTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _svc = new FilterService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    static Filter F(string name, string? imdbId = null) =>
        new() { Name = name, ImdbId = imdbId, Conditions = { new() { Field = "series.year", Operator = "gte", Value = "2000" } } };

    [Fact]
    public async Task Create_persists_conditions_and_ignores_supplied_id()
    {
        var created = await _svc.CreateAsync(new Filter { Id = 99, Name = "a", Conditions = F("x").Conditions });
        var loaded = (await _svc.ListAsync(null, false)).Single();
        Assert.NotEqual(99, created.Id);
        Assert.Equal("series.year", loaded.Conditions.Single().Field);
    }

    [Fact]
    public async Task List_filters_by_scope()
    {
        await _svc.CreateAsync(F("global"));
        await _svc.CreateAsync(F("s5", "tt5"));
        await _svc.CreateAsync(F("s6", "tt6"));

        Assert.Equal(["global"], (await _svc.ListAsync(null, true)).Select(f => f.Name));
        Assert.Equal(["s5"], (await _svc.ListAsync("tt5", false)).Select(f => f.Name));
        Assert.Equal(3, (await _svc.ListAsync(null, false)).Count);
    }

    [Fact]
    public async Task Update_and_delete_report_missing_ids()
    {
        Assert.Null(await _svc.UpdateAsync(42, F("x")));
        Assert.False(await _svc.DeleteAsync(42));

        var f = await _svc.CreateAsync(F("old"));
        var updated = await _svc.UpdateAsync(f.Id, F("new"));
        Assert.Equal("new", updated!.Name);
        Assert.True(await _svc.DeleteAsync(f.Id));
    }
}
