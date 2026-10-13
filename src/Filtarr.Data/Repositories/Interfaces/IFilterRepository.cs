using Filtarr.Data.Models;

namespace Filtarr.Data.Repositories.Interfaces;

/// <summary>One row of the per-series filter listing: which series a filter belongs to and whether it is enabled.</summary>
public record SeriesFilterRow(string ImdbId, string? SeriesTitle, bool Enabled);

public interface IFilterRepository
{
    /// <summary>Filters ordered by name, optionally limited to global filters or to one series.</summary>
    Task<List<Filter>> ListAsync(string? imdbId, bool globalOnly, CancellationToken ct = default);

    Task<Filter?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// One row per series filter. With <paramref name="imdbIds"/> null every series filter is returned; otherwise only the
    /// filters of the given series.
    /// </summary>
    Task<List<SeriesFilterRow>> ListSeriesFilterRowsAsync(IEnumerable<string>? imdbIds, CancellationToken ct = default);

    /// <summary>Stores a new filter; any supplied id is ignored and the generated one is set on the returned filter.</summary>
    Task<Filter> AddAsync(Filter filter, CancellationToken ct = default);

    /// <summary>Overwrites the filter's fields with <paramref name="input"/>; null when there is no such filter.</summary>
    Task<Filter?> UpdateAsync(int id, Filter input, CancellationToken ct = default);

    /// <summary>False when there is no such filter.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
