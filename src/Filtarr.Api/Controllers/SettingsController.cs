using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Serilog.Core;

namespace Filtarr.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(
    ISettingsService settings, ISettingsUpdater updater, ISonarrClient sonarr, IOmdbClient omdb,
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
        var url = string.IsNullOrWhiteSpace(request.SonarrUrl) ? null : request.SonarrUrl.Trim();
        var key = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        try { return Ok(new { ok = true, message = $"Connected to {await sonarr.TestAsync(url, key, ct)}" }); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { return Ok(new { ok = false, message = ex.Message }); }
    }

    /// <summary>The root folders and quality profiles Sonarr offers, for the dropdowns on the Settings page (uses the saved connection).</summary>
    [HttpGet("sonarr-options")]
    public async Task<IActionResult> SonarrOptions(CancellationToken ct)
    {
        try
        {
            var folders = await sonarr.GetRootFoldersAsync(ct);
            var profiles = await sonarr.GetQualityProfilesAsync(ct);
            return Ok(new
            {
                RootFolders = folders.Select(f => f.Path),
                QualityProfiles = profiles.Select(p => p.Name.Trim()),
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Problem($"Could not read from Sonarr: {ex.Message}", statusCode: 502);
        }
    }

    /// <summary>Tests an OMDb API key. A blank key tests the one currently in force.</summary>
    [HttpPost("test-omdb")]
    public async Task<IActionResult> TestOmdb(OmdbTestRequest request, CancellationToken ct)
    {
        var result = await omdb.TestKeyAsync(request.OmdbApiKey, ct);
        return Ok(new { ok = result.Ok, message = result.Message });
    }
}

public record SonarrTestRequest(string? SonarrUrl, string? ApiKey);
public record OmdbTestRequest(string? OmdbApiKey);
