using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api/filters")]
public class FiltersController(IFilterService filters, ISyncService sync) : ControllerBase
{
    [HttpGet]
    public async Task<List<Filter>> List(string? imdbId, bool? globalOnly, CancellationToken ct) =>
        await filters.ListAsync(imdbId, globalOnly == true, ct);

    [HttpPost]
    public async Task<IActionResult> Create(Filter filter, CancellationToken ct)
    {
        var created = await filters.CreateAsync(filter, ct);
        return Created($"/api/filters/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Filter filter, CancellationToken ct)
    {
        var updated = await filters.UpdateAsync(id, filter, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>
    /// Runs just this filter now, for real: monitors the episodes it matches in Sonarr (and searches for those without a file).
    /// No other include filter is evaluated.
    /// </summary>
    [HttpPost("{id:int}/run")]
    public async Task<IActionResult> Run(int id, CancellationToken ct)
    {
        try { return Ok(await sync.RunRuleAsync(id, ct)); }
        catch (RuleRunException ex) { return Problem(ex.Message, statusCode: ex.StatusCode); }
        catch (SyncInProgressException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return Problem($"Could not reach Sonarr: {ex.Message}", statusCode: 502);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await filters.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
