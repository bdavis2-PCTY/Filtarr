using Filtarr.Core.Configuration;
using Filtarr.Core.Mapping;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;
using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Serilog.Core;

namespace Filtarr.Core.Services;

public class SettingsService(IAppSettingsRepository repository, LoggingLevelSwitch levelSwitch) : ISettingsService
{
    public async Task<SettingsModel> GetAsync(CancellationToken ct = default) =>
        (await repository.GetAsync(ct) ?? new AppSettings()).ToModel();

    public async Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default) =>
        EffectiveSettings.From(await GetAsync(ct));

    public async Task UpdateAsync(SettingsUpdate update, CancellationToken ct = default)
    {
        // Edit the stored row itself (not a model copy) so columns the API never sees, like the one-time import flag, survive.
        var s = await repository.GetAsync(ct) ?? new AppSettings();

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
        await repository.SaveAsync(s, ct);
    }
}
