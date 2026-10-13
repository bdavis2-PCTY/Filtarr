using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

public interface IQuickFilterService
{
    Task<List<QuickFilterModel>> ListAsync(CancellationToken ct = default);
    Task<QuickFilterModel> CreateAsync(QuickFilterModel quickFilter, CancellationToken ct = default);
    Task<QuickFilterModel?> UpdateAsync(int id, QuickFilterModel input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
