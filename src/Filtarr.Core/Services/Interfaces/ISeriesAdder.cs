using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

/// <summary>Adds series to the Sonarr library.</summary>
public interface ISeriesAdder
{
    /// <summary>
    /// Adds the series with this IMDb id to Sonarr as monitored, but with no seasons or episodes monitored and no search started,
    /// so nothing downloads until a Filtarr sync monitors episodes. Throws <see cref="Exceptions.SeriesAddException"/> when it can't be added.
    /// </summary>
    Task<AddedSeries> AddAsync(string imdbId, CancellationToken ct = default);
}
