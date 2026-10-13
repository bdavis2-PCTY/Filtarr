using System.Text.Json.Nodes;
using Filtarr.Core.Clients.Models;

namespace Filtarr.Core.Clients.Interfaces;

/// <summary>Talks to the Sonarr v3 API.</summary>
public interface ISonarrClient
{
    /// <summary>
    /// Checks a Sonarr URL and key and returns "AppName version"; throws with a readable message if it can't connect or the key is
    /// rejected. A null <paramref name="url"/> or <paramref name="apiKey"/> uses the one currently in force.
    /// </summary>
    Task<string> TestAsync(string? url, string? apiKey, CancellationToken ct = default);
    Task<List<SonarrSeries>> GetSeriesAsync(CancellationToken ct = default);
    Task<List<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken ct = default);
    Task MonitorAsync(IEnumerable<int> episodeIds, CancellationToken ct = default);
    Task SearchAsync(IEnumerable<int> episodeIds, CancellationToken ct = default);

    /// <summary>Sonarr's metadata lookup for an IMDb id: the full series resource it would add, or null if unknown.</summary>
    Task<JsonObject?> LookupSeriesAsync(string imdbId, CancellationToken ct = default);
    Task<List<SonarrRootFolder>> GetRootFoldersAsync(CancellationToken ct = default);
    Task<List<SonarrQualityProfile>> GetQualityProfilesAsync(CancellationToken ct = default);
    /// <summary>The tags defined in Sonarr (series carry their ids).</summary>
    Task<List<SonarrTag>> GetTagsAsync(CancellationToken ct = default);
    /// <summary>Adds a series from a (modified) lookup resource. Throws <see cref="Exceptions.SonarrException"/> if Sonarr rejects it.</summary>
    Task<SonarrSeries> AddSeriesAsync(JsonObject series, CancellationToken ct = default);
}
