using Filtarr.Data.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Filtarr.Data;

/// <summary>Prepares the database at start-up: creates it, upgrades an older schema and imports legacy configuration.</summary>
public interface IDatabaseInitializer
{
    /// <summary>Safe to call on every start: each step is idempotent.</summary>
    void Initialize();
}

internal sealed class DatabaseInitializer(AppDb db, IConfiguration configuration, ILogger<DatabaseInitializer> log) : IDatabaseInitializer
{
    public void Initialize()
    {
        db.Database.EnsureCreated();
        SchemaUpgrader.Upgrade(db, log);
        LegacyConfigImport.Run(db, configuration, log);
    }
}
