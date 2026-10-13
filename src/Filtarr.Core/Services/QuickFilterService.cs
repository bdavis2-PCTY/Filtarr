using Filtarr.Core.Mapping;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Core.Services;

public class QuickFilterService(IQuickFilterRepository quickFilters) : IQuickFilterService
{
    public async Task<List<QuickFilterModel>> ListAsync(CancellationToken ct = default) =>
        (await quickFilters.ListAsync(ct)).Select(q => q.ToModel()).ToList();

    public async Task<QuickFilterModel> CreateAsync(QuickFilterModel quickFilter, CancellationToken ct = default) =>
        (await quickFilters.AddAsync(quickFilter.ToEntity(), ct)).ToModel();

    public async Task<QuickFilterModel?> UpdateAsync(int id, QuickFilterModel input, CancellationToken ct = default) =>
        (await quickFilters.UpdateAsync(id, input.ToEntity(), ct))?.ToModel();

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => quickFilters.DeleteAsync(id, ct);
}
