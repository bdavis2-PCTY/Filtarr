using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

/// <summary>Reference data for the filter editor.</summary>
public interface IMetadataService
{
    /// <summary>The condition fields a filter can use.</summary>
    IReadOnlyList<FieldDef> GetFields();

    /// <summary>The Sonarr library ordered by title. Throws when Sonarr can't be reached.</summary>
    Task<List<SonarrSeriesSummary>> ListSonarrSeriesAsync(CancellationToken ct = default);
}
