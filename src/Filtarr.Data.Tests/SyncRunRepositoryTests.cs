using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Data.Tests;

public sealed class SyncRunRepositoryTests : IDisposable
{
    readonly TestDb _db = new();
    readonly ISyncRunRepository _repo;

    public SyncRunRepositoryTests() => _repo = _db.Get<ISyncRunRepository>();

    public void Dispose() => _db.Dispose();

    static SyncRun Run(string trigger, params SyncedItem[] items) => new()
    {
        StartedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), Trigger = trigger, EpisodesSkipped = 2, Items = [.. items],
    };

    [Fact]
    public async Task Add_stores_the_run_with_its_items()
    {
        var run = Run("manual", new SyncedItem { EpisodeId = 11, Series = "Show", Episode = "S01E01", Filter = "f" });
        await _repo.AddAsync(run);

        var loaded = await _repo.GetAsync(run.Id);
        Assert.Equal(("manual", 2), (loaded!.Trigger, loaded.EpisodesSkipped));
        Assert.Equal(11, loaded.Items.Single().EpisodeId);
    }

    [Fact]
    public async Task Recent_returns_the_newest_runs_first_and_limits_the_count()
    {
        foreach (var t in new[] { "a", "b", "c" }) await _repo.AddAsync(Run(t));

        Assert.Equal(["c", "b"], (await _repo.GetRecentAsync(2)).Select(r => r.Trigger));
        Assert.Equal(3, (await _repo.GetRecentAsync(50)).Count);
    }

    [Fact]
    public async Task Delete_removes_the_run_and_reports_a_missing_id()
    {
        Assert.False(await _repo.DeleteAsync(42));

        var run = Run("manual");
        await _repo.AddAsync(run);
        Assert.True(await _repo.DeleteAsync(run.Id));
        Assert.Null(await _repo.GetAsync(run.Id));
    }
}
