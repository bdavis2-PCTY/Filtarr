using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api/series")]
public class SeriesController(ISeriesCatalog catalog, ISeriesDetailsService details, ISeriesAdder adder, ISyncService sync, IMonitoredHistoryService history) : ControllerBase
{
    /// <summary>Searches IMDb for TV series.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<SeriesInfo>());
        try { return Ok(await catalog.SearchAsync(q, ct)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Problem($"IMDb search failed: {ex.Message}", statusCode: 502);
        }
    }

    /// <summary>Every series that has at least one filter, whether or not it is in Sonarr.</summary>
    [HttpGet("with-filters")]
    public async Task<IActionResult> WithFilters(CancellationToken ct) => Ok(await catalog.ListWithFiltersAsync(ct));

    [HttpGet("{imdbId}")]
    public async Task<IActionResult> Get(string imdbId, CancellationToken ct)
    {
        if (!ImdbClient.ImdbIdPattern().IsMatch(imdbId)) return BadRequest(new { error = "Not a valid IMDb id (expected e.g. tt0903747)." });
        var info = await catalog.GetAsync(imdbId, ct);
        return info is null ? NotFound() : Ok(info);
    }

    /// <summary>The rich details for the series page (plot, cast, genres, rating, network, tags...). Parts can be missing.</summary>
    [HttpGet("{imdbId}/details")]
    public async Task<IActionResult> Details(string imdbId, CancellationToken ct)
    {
        if (!ImdbClient.ImdbIdPattern().IsMatch(imdbId)) return BadRequest(new { error = "Not a valid IMDb id." });
        var d = await details.GetAsync(imdbId, ct);
        return d is null ? NotFound() : Ok(d);
    }

    /// <summary>How many of this series' episodes Filtarr has monitored (and will therefore never monitor again).</summary>
    [HttpGet("{imdbId}/monitored-history")]
    public async Task<IActionResult> MonitoredHistory(string imdbId, CancellationToken ct)
    {
        if (!ImdbClient.ImdbIdPattern().IsMatch(imdbId)) return BadRequest(new { error = "Not a valid IMDb id." });
        return Ok(new { count = await history.CountAsync(imdbId, ct) });
    }

    /// <summary>
    /// Forgets which episodes of this series Filtarr has monitored, so a later sync may monitor them again. Only Filtarr's own
    /// record is cleared; nothing changes in Sonarr.
    /// </summary>
    [HttpDelete("{imdbId}/monitored-history")]
    public async Task<IActionResult> ResetMonitoredHistory(string imdbId, CancellationToken ct)
    {
        if (!ImdbClient.ImdbIdPattern().IsMatch(imdbId)) return BadRequest(new { error = "Not a valid IMDb id." });
        return Ok(new { removed = await history.ResetAsync(imdbId, ct) });
    }

    /// <summary>
    /// Adds the series to Sonarr (monitored, but with no seasons or episodes monitored) and returns its refreshed details,
    /// including the link to open it in Sonarr.
    /// </summary>
    [HttpPost("{imdbId}/add-to-sonarr")]
    public async Task<IActionResult> AddToSonarr(string imdbId, CancellationToken ct)
    {
        if (!ImdbClient.ImdbIdPattern().IsMatch(imdbId)) return BadRequest(new { error = "Not a valid IMDb id." });
        try
        {
            await adder.AddAsync(imdbId, ct);
            var info = await catalog.GetAsync(imdbId, ct);
            return info is null ? NotFound() : Ok(info);
        }
        catch (SeriesAddException ex) { return Problem(ex.Message, statusCode: ex.StatusCode); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Problem($"Could not reach Sonarr: {ex.Message}", statusCode: 502);
        }
    }

    /// <summary>
    /// Shows which episodes of the series the filters would monitor. Body is optional: without it the saved global and
    /// series filters are used; with it only the supplied filters are evaluated (e.g. a single filter or an unsaved draft).
    /// </summary>
    [HttpPost("{imdbId}/test")]
    public async Task<IActionResult> Test(string imdbId, [FromBody] TestRequest? request, CancellationToken ct)
    {
        if (!ImdbClient.ImdbIdPattern().IsMatch(imdbId)) return BadRequest(new { error = "Not a valid IMDb id." });
        try { return Ok(await sync.TestSeriesAsync(imdbId, request?.Filters, ct)); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return Problem(ex.Message, statusCode: 502);
        }
    }
}

public record TestRequest(List<Filter>? Filters);
