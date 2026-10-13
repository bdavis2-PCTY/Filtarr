using Filtarr.Data.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Data.Tests;

public class QuickFilterSchemaTests
{
    [Fact]
    public void Upgrade_creates_the_table_for_existing_databases()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("DROP TABLE QuickFilters"); // a database from before Quick Filters existed
        SchemaUpgrader.Upgrade(db, NullLogger.Instance);
        SchemaUpgrader.Upgrade(db, NullLogger.Instance); // idempotent
        Assert.Empty(db.QuickFilters.ToList());
    }
}
