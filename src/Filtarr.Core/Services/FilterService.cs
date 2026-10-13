using Filtarr.Core.Mapping;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Core.Services;

public class FilterService(IFilterRepository filters) : IFilterService
{
    public async Task<List<FilterModel>> ListAsync(string? imdbId, bool globalOnly, CancellationToken ct = default) =>
        (await filters.ListAsync(imdbId, globalOnly, ct)).Select(f => f.ToModel()).ToList();

    public async Task<List<SeriesFilterSummary>> SummarizeBySeriesAsync(IEnumerable<string>? imdbIds, CancellationToken ct = default)
    {
        var rows = await filters.ListSeriesFilterRowsAsync(imdbIds, ct);
        return rows.GroupBy(r => r.ImdbId)
            .Select(g => new SeriesFilterSummary(g.Key, g.Select(r => r.SeriesTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)), g.Count(), g.Count(r => !r.Enabled)))
            .OrderBy(s => s.SeriesTitle ?? s.ImdbId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<FilterModel> CreateAsync(FilterModel filter, CancellationToken ct = default) =>
        (await filters.AddAsync(filter.ToEntity(), ct)).ToModel();

    public async Task<FilterModel?> UpdateAsync(int id, FilterModel input, CancellationToken ct = default) =>
        (await filters.UpdateAsync(id, input.ToEntity(), ct))?.ToModel();

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => filters.DeleteAsync(id, ct);
}
