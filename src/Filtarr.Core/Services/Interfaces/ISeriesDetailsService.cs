using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

public interface ISeriesDetailsService
{
    /// <summary>Null when none of the sources knows the series.</summary>
    Task<SeriesDetails?> GetAsync(string imdbId, CancellationToken ct = default);
}
