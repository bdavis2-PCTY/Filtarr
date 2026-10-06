using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api/quick-filters")]
public class QuickFiltersController(IQuickFilterService quickFilters) : ControllerBase
{
    [HttpGet]
    public async Task<List<QuickFilter>> List(CancellationToken ct) => await quickFilters.ListAsync(ct);

    [HttpPost]
    public async Task<IActionResult> Create(QuickFilter quickFilter, CancellationToken ct)
    {
        var created = await quickFilters.CreateAsync(quickFilter, ct);
        return Created($"/api/quick-filters/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, QuickFilter quickFilter, CancellationToken ct)
    {
        var updated = await quickFilters.UpdateAsync(id, quickFilter, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await quickFilters.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
