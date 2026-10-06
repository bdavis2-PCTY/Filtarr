using Filtarr.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Api.Tests;

public class SchemaUpgraderTests
{
    [Fact]
    public void Legacy_series_filters_are_disabled_instead_of_becoming_global()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE Filters (Id INTEGER PRIMARY KEY, Name TEXT, Enabled INTEGER, Action TEXT, SeriesId INTEGER NULL, SeriesTitle TEXT NULL, Conditions TEXT);
                INSERT INTO Filters VALUES (1, 'global', 1, 'Include', NULL, NULL, '[]');
                INSERT INTO Filters VALUES (2, 'series', 1, 'Include', 42, 'Old', '[]');
                """;
            cmd.ExecuteNonQuery();
        }
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);

        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance); // idempotent

        var rows = db.Database.SqlQueryRaw<int>("SELECT Enabled AS Value FROM Filters ORDER BY Id").ToList();
        Assert.Equal([1, 0], rows);
    }

    [Fact]
    public void Fresh_databases_are_left_alone()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
    }
}
