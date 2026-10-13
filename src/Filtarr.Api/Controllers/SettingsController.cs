using Filtarr.Core.Configuration;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Serilog.Core;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(
    ISettingsService settings, ISettingsUpdater updater, IConnectionTestService connections,
    LoggingLevelSwitch levelSwitch, FiltarrOptions options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var saved = await settings.GetAsync(ct);
        var effective = EffectiveSettings.From(saved);
        return Ok(new
        {
            effective.SonarrUrl,
            HasApiKey = effective.SonarrApiKey != "",
            OmdbConfigured = effective.OmdbApiKey != "",
            effective.SonarrRootFolderPath,
            effective.SonarrQualityProfile,
            LogLevel = saved.LogLevel ?? levelSwitch.MinimumLevel.ToString(),
            LogLevels = LogLevels.Allowed,
            DataDirectory = options.ResolveDataDirectory(),
            options.Port,
            saved.AutoSyncEnabled, saved.SyncIntervalMinutes, saved.SearchOnMonitor, saved.SkipUnmonitoredSeries,
        });
    }

    /// <summary>Saves the settings, but rejects a changed Sonarr URL/key or a new OMDb key that fails its connection test.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(SettingsUpdate update, CancellationToken ct)
    {
        var result = await updater.UpdateAsync(update, ct);
        return result.Ok ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Tests a Sonarr URL and key. Blank values test the ones currently in force.</summary>
    [HttpPost("test-sonarr")]
    public async Task<IActionResult> TestSonarr(SonarrTestRequest request, CancellationToken ct)
    {
        var result = await connections.TestSonarrAsync(request.SonarrUrl, request.ApiKey, ct);
        return Ok(new { ok = result.Ok, message = result.Message });
    }

    /// <summary>The root folders and quality profiles Sonarr offers, for the dropdowns on the Settings page (uses the saved connection).</summary>
    [HttpGet("sonarr-options")]
    public async Task<IActionResult> SonarrOptions(CancellationToken ct)
    {
        try { return Ok(await connections.GetSonarrOptionsAsync(ct)); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Problem($"Could not read from Sonarr: {ex.Message}", statusCode: 502);
        }
    }

    /// <summary>Tests an OMDb API key. A blank key tests the one currently in force.</summary>
    [HttpPost("test-omdb")]
    public async Task<IActionResult> TestOmdb(OmdbTestRequest request, CancellationToken ct)
    {
        var result = await connections.TestOmdbAsync(request.OmdbApiKey, ct);
        return Ok(new { ok = result.Ok, message = result.Message });
    }
}

public record SonarrTestRequest(string? SonarrUrl, string? ApiKey);
public record OmdbTestRequest(string? OmdbApiKey);
