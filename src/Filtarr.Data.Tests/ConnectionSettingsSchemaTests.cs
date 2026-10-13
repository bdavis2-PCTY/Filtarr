using Filtarr.Data.Migrations;
using Filtarr.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Data.Tests;

public class ConnectionSettingsSchemaTests
{
    [Fact]
    public void Existing_databases_get_the_new_settings_columns_and_ef_can_read_them()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var seed = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options))
        {
            seed.Database.EnsureCreated();
            seed.Settings.Add(new AppSettings { ApiKey = "old-key" });
            seed.SaveChanges();
            seed.Database.ExecuteSqlRaw("ALTER TABLE Settings DROP COLUMN SonarrUrl");
            seed.Database.ExecuteSqlRaw("ALTER TABLE Settings DROP COLUMN OmdbApiKey");
            seed.Database.ExecuteSqlRaw("ALTER TABLE Settings DROP COLUMN LogLevel");
            seed.Database.ExecuteSqlRaw("ALTER TABLE Settings DROP COLUMN SonarrRootFolderPath");
            seed.Database.ExecuteSqlRaw("ALTER TABLE Settings DROP COLUMN SonarrQualityProfile");
            seed.Database.ExecuteSqlRaw("ALTER TABLE Settings DROP COLUMN ConfigImported");
        }

        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance); // idempotent

        var s = db.Settings.Single();
        Assert.Equal(("old-key", "http://localhost:8989", "", "", "", null, false),
            (s.ApiKey, s.SonarrUrl, s.OmdbApiKey, s.SonarrRootFolderPath, s.SonarrQualityProfile, s.LogLevel, s.ConfigImported));
    }

    [Fact]
    public void A_blank_url_left_by_an_earlier_version_becomes_the_default_but_a_real_url_is_kept()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        db.Settings.Add(new AppSettings { SonarrUrl = "" });
        db.SaveChanges();

        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        db.ChangeTracker.Clear();
        Assert.Equal("http://localhost:8989", db.Settings.Single().SonarrUrl);

        db.Settings.ExecuteUpdate(u => u.SetProperty(s => s.SonarrUrl, "http://nas:8989"));
        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        db.ChangeTracker.Clear();
        Assert.Equal("http://nas:8989", db.Settings.Single().SonarrUrl);
    }
}
