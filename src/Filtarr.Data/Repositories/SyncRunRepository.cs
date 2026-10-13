using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Data.Repositories;

internal sealed class SyncRunRepository(AppDb db) : ISyncRunRepository
{
    public async Task AddAsync(SyncRun run, CancellationToken ct = default)
    {
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public Task<SyncRun?> GetAsync(int id, CancellationToken ct = default) =>
        db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<SyncRun>> GetRecentAsync(int count, CancellationToken ct = default) =>
        db.Runs.AsNoTracking().OrderByDescending(r => r.Id).Take(count).ToListAsync(ct);

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var run = await db.Runs.FindAsync([id], ct);
        if (run is null) return false;
        db.Runs.Remove(run);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
