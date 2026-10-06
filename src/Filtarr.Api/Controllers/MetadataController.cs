using Filtarr.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api")]
public class MetadataController(ISonarrClient sonarr) : ControllerBase
{
    [HttpGet("fields")]
    public IActionResult Fields() => Ok(Services.Fields.All);

    [HttpGet("series")]
    public async Task<IActionResult> Series(CancellationToken ct)
    {
        try
        {
            var list = await sonarr.GetSeriesAsync(ct);
            return Ok(list.OrderBy(s => s.Title).Select(s => new { s.Id, s.Title, s.Year, s.Monitored }));
        }
        catch (Exception ex) { return Problem(ex.Message, statusCode: 502); }
    }
}
