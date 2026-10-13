using Filtarr.Core.Clients.Models;

namespace Filtarr.Core.Clients.Interfaces;

/// <summary>Searches IMDb for TV series.</summary>
public interface IImdbClient
{
    Task<List<ImdbTitle>> SearchSeriesAsync(string query, CancellationToken ct = default);
    /// <summary>Looks up one title by IMDb id (e.g. "tt0903747"); null if not found or not a series.</summary>
    Task<ImdbTitle?> GetSeriesAsync(string imdbId, CancellationToken ct = default);
}
