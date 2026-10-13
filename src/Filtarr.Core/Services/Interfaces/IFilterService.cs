using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

public interface IFilterService
{
    /// <summary>Lists filters, optionally limited to global filters or to one series.</summary>
    Task<List<FilterModel>> ListAsync(string? imdbId, bool globalOnly, CancellationToken ct = default);
    Task<FilterModel> CreateAsync(FilterModel filter, CancellationToken ct = default);
    Task<FilterModel?> UpdateAsync(int id, FilterModel input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// How many filters each series has (global filters are not counted). With <paramref name="imdbIds"/> null, every series
    /// that has at least one filter is returned; otherwise only the given series, and only those that have filters.
    /// </summary>
    Task<List<SeriesFilterSummary>> SummarizeBySeriesAsync(IEnumerable<string>? imdbIds, CancellationToken ct = default);
}
