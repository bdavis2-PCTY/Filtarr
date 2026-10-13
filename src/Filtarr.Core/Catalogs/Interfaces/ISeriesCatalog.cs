using Filtarr.Core.Models;

namespace Filtarr.Core.Catalogs.Interfaces;

/// <summary>Finds series on IMDb and tells whether each is in the Sonarr library and how many filters it has.</summary>
public interface ISeriesCatalog
{
    Task<List<SeriesInfo>> SearchAsync(string query, CancellationToken ct = default);
    Task<SeriesInfo?> GetAsync(string imdbId, CancellationToken ct = default);
    /// <summary>Every series that has at least one filter, sorted by title (in Sonarr or not).</summary>
    Task<List<SeriesInfo>> ListWithFiltersAsync(CancellationToken ct = default);
}
