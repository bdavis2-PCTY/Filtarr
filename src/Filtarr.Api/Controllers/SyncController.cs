using Filtarr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api")]
public class SyncController(ISyncService sync, ISyncRunStore runs) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(int? seriesId, CancellationToken ct)
    {
        try { return Ok(await sync.PreviewAsync(seriesId, ct)); }
        catch (Exception ex) { return Problem(ex.Message, statusCode: 502); }
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        try { return Ok(await sync.RunAsync("manual", ct)); }
        catch (SyncInProgressException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpGet("runs")]
    public async Task<IActionResult> Runs(CancellationToken ct) => Ok(await runs.RecentAsync(50, ct));
}
