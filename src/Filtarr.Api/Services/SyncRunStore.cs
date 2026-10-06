using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Services;

public class SyncRunStore(AppDb db) : ISyncRunStore
{
    public async Task AddAsync(SyncRun run, CancellationToken ct = default)
    {
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<SyncRun>> RecentAsync(int count, CancellationToken ct = default) =>
        db.Runs.AsNoTracking().OrderByDescending(r => r.Id).Take(count).ToListAsync(ct);
}
