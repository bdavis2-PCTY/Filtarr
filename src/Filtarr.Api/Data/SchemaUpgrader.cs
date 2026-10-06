using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Data;

/// <summary>
/// The database is created with EnsureCreated (no migrations), so schema changes to existing databases are applied here.
/// Every step is idempotent.
/// </summary>
public static class SchemaUpgrader
{
    public static void Upgrade(AppDb db, ILogger log)
    {
        MoveSeriesFiltersToImdbId(db, log);
        CreateSeasonRatingsTable(db);
        AddMonitoredEpisodeTracking(db, log);
        AddEditableConnectionSettings(db);
        CreateQuickFiltersTable(db);
    }

    // Added with Quick Filters.
    static void CreateQuickFiltersTable(AppDb db) =>
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "QuickFilters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_QuickFilters" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "Action" TEXT NOT NULL,
                "Conditions" TEXT NOT NULL)
            """);

    // v1 keyed per-series filters by Sonarr series id (SeriesId); they are now keyed by IMDb id.
    static void MoveSeriesFiltersToImdbId(AppDb db, ILogger log)
    {
        var columns = Columns(db, "Filters");
        if (columns.Contains("ImdbId")) return;

        db.Database.ExecuteSqlRaw("ALTER TABLE Filters ADD COLUMN ImdbId TEXT NULL");
        if (columns.Contains("SeriesId"))
        {
            // Without an IMDb id these would silently turn into global filters, so disable them instead.
            var disabled = db.Database.ExecuteSqlRaw("UPDATE Filters SET Enabled = 0 WHERE SeriesId IS NOT NULL");
            if (disabled > 0)
                log.LogWarning("{Count} per-series filter(s) from an older version were disabled; re-create them from the series page.", disabled);
        }
    }

    // Added with episode-rating filters.
    static void CreateSeasonRatingsTable(AppDb db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "SeasonRatings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SeasonRatings" PRIMARY KEY AUTOINCREMENT,
                "SeriesImdbId" TEXT NOT NULL,
                "Season" INTEGER NOT NULL,
                "FetchedAt" TEXT NOT NULL,
                "Episodes" TEXT NOT NULL)
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SeasonRatings_SeriesImdbId_Season" ON "SeasonRatings" ("SeriesImdbId", "Season")
            """);
    }

    // Added with the "never re-monitor" tracking.
    static void AddMonitoredEpisodeTracking(AppDb db, ILogger log)
    {
        var tableExisted = Columns(db, "MonitoredEpisodes").Count > 0;
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "MonitoredEpisodes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MonitoredEpisodes" PRIMARY KEY AUTOINCREMENT,
                "EpisodeId" INTEGER NOT NULL,
                "SeriesId" INTEGER NOT NULL,
                "SeriesImdbId" TEXT NULL,
                "SeriesTitle" TEXT NOT NULL,
                "Season" INTEGER NOT NULL,
                "Number" INTEGER NOT NULL,
                "EpisodeTitle" TEXT NULL,
                "Filter" TEXT NOT NULL,
                "MonitoredAt" TEXT NOT NULL)
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_MonitoredEpisodes_EpisodeId" ON "MonitoredEpisodes" ("EpisodeId")""");
        var runColumns = Columns(db, "Runs");
        if (runColumns.Count > 0 && !runColumns.Contains("EpisodesSkipped"))
            db.Database.ExecuteSqlRaw("ALTER TABLE Runs ADD COLUMN EpisodesSkipped INTEGER NOT NULL DEFAULT 0");

        if (tableExisted || runColumns.Count == 0) return;
        // Earlier versions already monitored episodes without tracking them. Every past run kept the episodes it monitored,
        // so seed the table from that history to protect them from being re-monitored. (Runs keep at most 500 episodes each.)
        var seeded = db.Database.ExecuteSqlRaw("""
            INSERT OR IGNORE INTO "MonitoredEpisodes" ("EpisodeId", "SeriesId", "SeriesImdbId", "SeriesTitle", "Season", "Number", "EpisodeTitle", "Filter", "MonitoredAt")
            SELECT json_extract(j.value, '$.EpisodeId'), 0, NULL, COALESCE(json_extract(j.value, '$.Series'), ''), 0, 0,
                   json_extract(j.value, '$.Episode'), COALESCE(json_extract(j.value, '$.Filter'), ''), r."StartedAt"
            FROM "Runs" r, json_each(r."Items") j
            WHERE json_extract(j.value, '$.EpisodeId') IS NOT NULL
            ORDER BY r."Id" ASC
            """);
        if (seeded > 0) log.LogInformation("Seeded the monitored-episode history with {Count} episode(s) from earlier sync runs.", seeded);
    }

    // Sonarr URL, keys, root folder, quality profile and log level are all stored in the Settings table now.
    static void AddEditableConnectionSettings(AppDb db)
    {
        var columns = Columns(db, "Settings");
        if (columns.Count == 0) return;
        if (!columns.Contains("SonarrUrl")) db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SonarrUrl TEXT NOT NULL DEFAULT 'http://localhost:8989'");
        if (!columns.Contains("OmdbApiKey")) db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN OmdbApiKey TEXT NOT NULL DEFAULT ''");
        if (!columns.Contains("LogLevel")) db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN LogLevel TEXT NULL");
        if (!columns.Contains("SonarrRootFolderPath")) db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SonarrRootFolderPath TEXT NOT NULL DEFAULT ''");
        if (!columns.Contains("SonarrQualityProfile")) db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SonarrQualityProfile TEXT NOT NULL DEFAULT ''");
        if (!columns.Contains("ConfigImported")) db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ConfigImported INTEGER NOT NULL DEFAULT 0");
        // An earlier version added SonarrUrl with an empty default; the URL is never blank.
        db.Database.ExecuteSqlRaw("UPDATE Settings SET SonarrUrl = 'http://localhost:8989' WHERE TRIM(SonarrUrl) = ''");
    }

    static HashSet<string> Columns(AppDb db, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State == System.Data.ConnectionState.Closed;
        if (wasClosed) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table}')";
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(r.GetString(1));
        }
        finally { if (wasClosed) conn.Close(); }
        return result;
    }
}
