using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Data;

/// <summary>
/// The Sonarr URL / API key / root folder / quality profile and the OMDb key used to live in the "Filtarr" section of appsettings.
/// They are now only stored in the database. So that an existing setup keeps working, the first start after the change copies any
/// of those values that are still present in configuration into the database (never overwriting a value already saved there),
/// then marks the row so this never runs again. After that, the old appsettings entries are ignored and can be deleted.
/// </summary>
public static class LegacyConfigImport
{
    public static void Run(AppDb db, IConfiguration configuration, ILogger log)
    {
        var row = db.Settings.FirstOrDefault();
        if (row is null) db.Settings.Add(row = new AppSettings());
        if (row.ConfigImported) { db.SaveChanges(); return; }

        var section = configuration.GetSection("Filtarr");
        var imported = new List<string>();

        void Take(string key, bool unset, Action<string> set)
        {
            var legacy = (section[key] ?? "").Trim();
            if (legacy == "" || !unset) return;
            set(legacy);
            imported.Add(key); // names only: these values include secrets and must never be logged
        }

        var urlUnset = string.IsNullOrWhiteSpace(row.SonarrUrl) || row.SonarrUrl == AppSettings.DefaultSonarrUrl;
        Take("SonarrUrl", urlUnset, v => row.SonarrUrl = v);
        Take("SonarrApiKey", string.IsNullOrWhiteSpace(row.ApiKey), v => row.ApiKey = v);
        Take("OmdbApiKey", string.IsNullOrWhiteSpace(row.OmdbApiKey), v => row.OmdbApiKey = v);
        Take("SonarrRootFolderPath", string.IsNullOrWhiteSpace(row.SonarrRootFolderPath), v => row.SonarrRootFolderPath = v);
        Take("SonarrQualityProfile", string.IsNullOrWhiteSpace(row.SonarrQualityProfile), v => row.SonarrQualityProfile = v);

        row.ConfigImported = true;
        db.SaveChanges();
        if (imported.Count > 0)
            log.LogWarning(
                "Copied {Settings} from appsettings into the database. These settings are now managed on the Settings page; the appsettings entries are ignored and can be removed.",
                string.Join(", ", imported));
    }
}
