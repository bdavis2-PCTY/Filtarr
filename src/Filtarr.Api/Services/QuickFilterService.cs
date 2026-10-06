using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Services;

public class QuickFilterService(AppDb db) : IQuickFilterService
{
    public async Task<List<QuickFilter>> ListAsync(CancellationToken ct = default) =>
        await db.QuickFilters.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);

    public async Task<QuickFilter> CreateAsync(QuickFilter quickFilter, CancellationToken ct = default)
    {
        quickFilter.Id = 0;
        db.QuickFilters.Add(quickFilter);
        await db.SaveChangesAsync(ct);
        return quickFilter;
    }

    public async Task<QuickFilter?> UpdateAsync(int id, QuickFilter input, CancellationToken ct = default)
    {
        var f = await db.QuickFilters.FindAsync([id], ct);
        if (f is null) return null;
        f.Name = input.Name; f.Action = input.Action; f.Conditions = input.Conditions;
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var f = await db.QuickFilters.FindAsync([id], ct);
        if (f is null) return false;
        db.QuickFilters.Remove(f);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
