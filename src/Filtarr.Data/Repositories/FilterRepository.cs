using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Data.Repositories;

internal sealed class FilterRepository(AppDb db) : IFilterRepository
{
    public async Task<List<Filter>> ListAsync(string? imdbId, bool globalOnly, CancellationToken ct = default)
    {
        var q = db.Filters.AsNoTracking().AsQueryable();
        if (globalOnly) q = q.Where(f => f.ImdbId == null);
        else if (imdbId is not null) q = q.Where(f => f.ImdbId == imdbId);
        return await q.OrderBy(f => f.Name).ToListAsync(ct);
    }

    public Task<Filter?> GetAsync(int id, CancellationToken ct = default) =>
        db.Filters.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<List<SeriesFilterRow>> ListSeriesFilterRowsAsync(IEnumerable<string>? imdbIds, CancellationToken ct = default)
    {
        var q = db.Filters.AsNoTracking().Where(f => f.ImdbId != null);
        if (imdbIds is not null)
        {
            var ids = imdbIds.Distinct().ToList();
            if (ids.Count == 0) return [];
            q = q.Where(f => ids.Contains(f.ImdbId!));
        }
        return await q.Select(f => new SeriesFilterRow(f.ImdbId!, f.SeriesTitle, f.Enabled)).ToListAsync(ct);
    }

    public async Task<Filter> AddAsync(Filter filter, CancellationToken ct = default)
    {
        filter.Id = 0;
        db.Filters.Add(filter);
        await db.SaveChangesAsync(ct);
        return filter;
    }

    public async Task<Filter?> UpdateAsync(int id, Filter input, CancellationToken ct = default)
    {
        var f = await db.Filters.FindAsync([id], ct);
        if (f is null) return null;
        f.Name = input.Name; f.Enabled = input.Enabled; f.Action = input.Action;
        f.ImdbId = input.ImdbId; f.SeriesTitle = input.SeriesTitle; f.Conditions = input.Conditions;
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var f = await db.Filters.FindAsync([id], ct);
        if (f is null) return false;
        db.Filters.Remove(f);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
