using Filtarr.Core.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api")]
public class MetadataController(IMetadataService metadata) : ControllerBase
{
    [HttpGet("fields")]
    public IActionResult Fields() => Ok(metadata.GetFields());

    [HttpGet("series")]
    public async Task<IActionResult> Series(CancellationToken ct)
    {
        try { return Ok(await metadata.ListSonarrSeriesAsync(ct)); }
        catch (Exception ex) { return Problem(ex.Message, statusCode: 502); }
    }
}
