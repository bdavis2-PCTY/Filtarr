using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

/// <summary>The connection checks behind the Settings page buttons and dropdowns.</summary>
public interface IConnectionTestService
{
    /// <summary>Tests a Sonarr URL and key. Blank values test the ones currently in force. Never throws for a failed connection.</summary>
    Task<ConnectionTestResult> TestSonarrAsync(string? url, string? apiKey, CancellationToken ct = default);

    /// <summary>Tests an OMDb API key. A blank key tests the one currently in force.</summary>
    Task<ConnectionTestResult> TestOmdbAsync(string? apiKey, CancellationToken ct = default);

    /// <summary>The root folders and quality profiles Sonarr offers (uses the saved connection). Throws when Sonarr can't be reached.</summary>
    Task<SonarrOptions> GetSonarrOptionsAsync(CancellationToken ct = default);
}
