using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;

namespace Filtarr.Core.Services;

public class ConnectionTestService(ISonarrClient sonarr, IOmdbClient omdb) : IConnectionTestService
{
    public async Task<ConnectionTestResult> TestSonarrAsync(string? url, string? apiKey, CancellationToken ct = default)
    {
        url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        try { return new(true, $"Connected to {await sonarr.TestAsync(url, apiKey, ct)}"); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { return new(false, ex.Message); }
    }

    public async Task<ConnectionTestResult> TestOmdbAsync(string? apiKey, CancellationToken ct = default)
    {
        var result = await omdb.TestKeyAsync(apiKey, ct);
        return new(result.Ok, result.Message);
    }

    public async Task<SonarrOptions> GetSonarrOptionsAsync(CancellationToken ct = default)
    {
        var folders = await sonarr.GetRootFoldersAsync(ct);
        var profiles = await sonarr.GetQualityProfilesAsync(ct);
        return new(folders.Select(f => f.Path).ToList(), profiles.Select(p => p.Name.Trim()).ToList());
    }
}
