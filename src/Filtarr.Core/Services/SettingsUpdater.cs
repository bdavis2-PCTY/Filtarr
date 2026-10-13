using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Configuration;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;

namespace Filtarr.Core.Services;

public class SettingsUpdater(ISettingsService settings, ISonarrClient sonarr, IOmdbClient omdb) : ISettingsUpdater
{
    public async Task<SettingsUpdateResult> UpdateAsync(SettingsUpdate update, CancellationToken ct = default)
    {
        if (update.LogLevel is not null && !LogLevels.TryParse(update.LogLevel, out _))
            return new(false, $"Log level must be one of: {string.Join(", ", LogLevels.Allowed)}.");

        var current = await settings.GetEffectiveAsync(ct);

        // Sonarr: any new key, or a different URL, must connect before it is saved.
        var newUrl = string.IsNullOrWhiteSpace(update.SonarrUrl) ? current.SonarrUrl : update.SonarrUrl.Trim();
        if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new(false, "The Sonarr URL must be a full http:// or https:// address.");
        var newKey = string.IsNullOrWhiteSpace(update.ApiKey) ? current.SonarrApiKey : update.ApiKey.Trim();
        var sonarrChanged = !string.IsNullOrWhiteSpace(update.ApiKey) || !SameUrl(newUrl, current.SonarrUrl);
        if (sonarrChanged)
        {
            try { await sonarr.TestAsync(newUrl, newKey, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new(false, $"Sonarr connection test failed: {ex.Message}");
            }
        }

        // OMDb: a new key must be accepted before it is saved. Clearing the key needs no test.
        if (!update.ClearOmdbApiKey && !string.IsNullOrWhiteSpace(update.OmdbApiKey))
        {
            var test = await omdb.TestKeyAsync(update.OmdbApiKey.Trim(), ct);
            if (!test.Ok) return new(false, $"OMDb key test failed: {test.Message}");
        }

        await settings.UpdateAsync(update, ct);
        return new(true);
    }

    static bool SameUrl(string a, string b) => string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
