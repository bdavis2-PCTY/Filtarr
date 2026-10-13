using Filtarr.Data.Models;

namespace Filtarr.Data.Repositories.Interfaces;

public interface IQuickFilterRepository
{
    /// <summary>All quick filters ordered by name.</summary>
    Task<List<QuickFilter>> ListAsync(CancellationToken ct = default);

    Task<QuickFilter?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Stores a new quick filter; any supplied id is ignored and the generated one is set on the returned object.</summary>
    Task<QuickFilter> AddAsync(QuickFilter quickFilter, CancellationToken ct = default);

    /// <summary>Overwrites the quick filter's fields with <paramref name="input"/>; null when there is no such quick filter.</summary>
    Task<QuickFilter?> UpdateAsync(int id, QuickFilter input, CancellationToken ct = default);

    /// <summary>False when there is no such quick filter.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
