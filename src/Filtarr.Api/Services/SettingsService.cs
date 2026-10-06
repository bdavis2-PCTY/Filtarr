using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using Serilog.Core;

namespace Filtarr.Api.Services;

public class SettingsService(AppDb db, LoggingLevelSwitch levelSwitch) : ISettingsService
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return s ?? new AppSettings();
    }

    public async Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default) =>
        EffectiveSettings.From(await GetAsync(ct));

    public async Task UpdateAsync(SettingsUpdate update, CancellationToken ct = default)
    {
        var s = await db.Settings.FirstOrDefaultAsync(ct);
        if (s is null) db.Settings.Add(s = new AppSettings());

        if (!string.IsNullOrWhiteSpace(update.SonarrUrl)) s.SonarrUrl = update.SonarrUrl.Trim();
        if (!string.IsNullOrWhiteSpace(update.ApiKey)) s.ApiKey = update.ApiKey.Trim();
        if (update.ClearOmdbApiKey) s.OmdbApiKey = "";
        else if (!string.IsNullOrWhiteSpace(update.OmdbApiKey)) s.OmdbApiKey = update.OmdbApiKey.Trim();

        // Unlike the keys above, blank is a real value here ("use Sonarr's first"), so only a missing value means unchanged.
        if (update.SonarrRootFolderPath is not null) s.SonarrRootFolderPath = update.SonarrRootFolderPath.Trim();
        if (update.SonarrQualityProfile is not null) s.SonarrQualityProfile = update.SonarrQualityProfile.Trim();

        s.AutoSyncEnabled = update.AutoSyncEnabled;
        s.SyncIntervalMinutes = Math.Max(1, update.SyncIntervalMinutes);
        s.SearchOnMonitor = update.SearchOnMonitor;
        s.SkipUnmonitoredSeries = update.SkipUnmonitoredSeries;

        if (LogLevels.TryParse(update.LogLevel, out var level))
        {
            s.LogLevel = level.ToString();
            levelSwitch.MinimumLevel = level; // takes effect immediately, no restart
        }
        await db.SaveChangesAsync(ct);
    }
}

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
