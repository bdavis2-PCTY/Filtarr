using Filtarr.Data.Models;

namespace Filtarr.Data.Repositories.Interfaces;

/// <summary>The history of sync runs. Runs are an audit log: they are added and removed, never edited.</summary>
public interface ISyncRunRepository
{
    Task AddAsync(SyncRun run, CancellationToken ct = default);

    Task<SyncRun?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>The newest runs first, at most <paramref name="count"/> of them.</summary>
    Task<List<SyncRun>> GetRecentAsync(int count, CancellationToken ct = default);

    /// <summary>False when there is no such run.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
