using Filtarr.Data.Migrations;
using Filtarr.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Data.Tests;

public sealed class LegacyConfigImportTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;

    public LegacyConfigImportTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => $"Filtarr:{v.Key}", v => (string?)v.Value)).Build();

    AppSettings Row() { _db.ChangeTracker.Clear(); return _db.Settings.Single(); }

    [Fact]
    public void Old_appsettings_values_are_copied_into_the_database_once()
    {
        var config = Config(("SonarrUrl", "https://sonarr.example"), ("SonarrApiKey", "key"), ("OmdbApiKey", "omdb"),
            ("SonarrRootFolderPath", "M:\\TV"), ("SonarrQualityProfile", "High"));

        LegacyConfigImport.Run(_db, config, NullLogger.Instance);

        var s = Row();
        Assert.Equal(("https://sonarr.example", "key", "omdb", "M:\\TV", "High", true),
            (s.SonarrUrl, s.ApiKey, s.OmdbApiKey, s.SonarrRootFolderPath, s.SonarrQualityProfile, s.ConfigImported));
    }

    [Fact]
    public void It_never_overwrites_values_already_saved_and_only_fills_blanks()
    {
        _db.Settings.Add(new AppSettings { SonarrUrl = "http://saved:1", ApiKey = "saved-key" });
        _db.SaveChanges();

        LegacyConfigImport.Run(_db, Config(("SonarrUrl", "http://old"), ("SonarrApiKey", "old-key"), ("OmdbApiKey", "old-omdb")), NullLogger.Instance);

        var s = Row();
        Assert.Equal(("http://saved:1", "saved-key", "old-omdb"), (s.SonarrUrl, s.ApiKey, s.OmdbApiKey));
    }

    [Fact]
    public void It_runs_only_once_so_a_value_cleared_later_is_not_resurrected()
    {
        LegacyConfigImport.Run(_db, Config(("OmdbApiKey", "old-omdb")), NullLogger.Instance);
        _db.Settings.ExecuteUpdate(u => u.SetProperty(s => s.OmdbApiKey, ""));   // the user clears it on the Settings page

        LegacyConfigImport.Run(_db, Config(("OmdbApiKey", "old-omdb")), NullLogger.Instance);

        Assert.Equal("", Row().OmdbApiKey);
    }

    [Fact]
    public void Without_any_old_values_a_default_row_is_created_and_marked()
    {
        LegacyConfigImport.Run(_db, Config(), NullLogger.Instance);
        var s = Row();
        Assert.Equal(("http://localhost:8989", "", "", true), (s.SonarrUrl, s.ApiKey, s.OmdbApiKey, s.ConfigImported));
    }

    [Fact]
    public void Secrets_are_never_written_to_the_log()
    {
        var log = new CapturingLogger();
        LegacyConfigImport.Run(_db, Config(("SonarrApiKey", "super-secret-key"), ("OmdbApiKey", "another-secret")), log);

        var text = string.Join("\n", log.Messages);
        Assert.Contains("SonarrApiKey", text);        // names are fine
        Assert.DoesNotContain("super-secret-key", text);
        Assert.DoesNotContain("another-secret", text);
    }

    class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
