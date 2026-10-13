using Filtarr.Data.Migrations;
using Filtarr.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Data.Tests;

public class SeasonRatingsSchemaTests
{
    [Fact]
    public void Existing_databases_get_the_cache_table_and_ef_can_use_it()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            // A database from before episode ratings: Filters only, already on ImdbId.
            cmd.CommandText = "CREATE TABLE Filters (Id INTEGER PRIMARY KEY, Name TEXT, Enabled INTEGER, Action TEXT, ImdbId TEXT NULL, SeriesTitle TEXT NULL, Conditions TEXT)";
            cmd.ExecuteNonQuery();
        }
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);

        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance); // idempotent

        db.SeasonRatings.Add(new SeasonRatings
        {
            SeriesImdbId = "tt1", Season = 1, FetchedAt = DateTime.UtcNow,
            Episodes = { new CachedEpisodeRating { Number = 1, ImdbId = "tt9", Rating = 8.5 } },
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var loaded = db.SeasonRatings.Single();
        Assert.Equal(8.5, loaded.Episodes.Single().Rating);
    }
}
