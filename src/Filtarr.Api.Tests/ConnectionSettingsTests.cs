using System.Net;
using System.Text;
using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Core;
using Serilog.Events;

namespace Filtarr.Api.Tests;

public class EffectiveSettingsTests
{
    [Fact]
    public void A_fresh_settings_row_has_the_documented_defaults()
    {
        var s = new AppSettings();
        Assert.Equal("http://localhost:8989", s.SonarrUrl);
        Assert.Equal(("", "", "", ""), (s.ApiKey, s.OmdbApiKey, s.SonarrRootFolderPath, s.SonarrQualityProfile));
        Assert.Equal(new EffectiveSettings("http://localhost:8989", "", "", "", ""), EffectiveSettings.From(s));
    }

    [Fact]
    public void Effective_settings_are_the_saved_values_trimmed()
    {
        var saved = new AppSettings
        {
            SonarrUrl = " http://ui:1 ", ApiKey = " ui-key ", OmdbApiKey = " ui-omdb ",
            SonarrRootFolderPath = " M:\\TV ", SonarrQualityProfile = " High ",
        };
        Assert.Equal(new EffectiveSettings("http://ui:1", "ui-key", "ui-omdb", "M:\\TV", "High"), EffectiveSettings.From(saved));
    }

    [Fact]
    public void A_blank_url_falls_back_to_the_default_address()
    {
        Assert.Equal("http://localhost:8989", EffectiveSettings.From(new AppSettings { SonarrUrl = "  " }).SonarrUrl);
    }
}

public class LogLevelsTests
{
    [Theory]
    [InlineData("Verbose", LogEventLevel.Verbose)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData(" Warning ", LogEventLevel.Warning)]
    [InlineData("ERROR", LogEventLevel.Error)]
    public void Accepts_the_four_ui_levels_in_any_case(string text, LogEventLevel expected)
    {
        Assert.True(LogLevels.TryParse(text, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Debug")]
    [InlineData("Fatal")]
    [InlineData("loud")]
    public void Rejects_everything_else(string? text) => Assert.False(LogLevels.TryParse(text, out _));
}

public sealed class SettingsServiceTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly LoggingLevelSwitch _switch = new(LogEventLevel.Warning);

    public SettingsServiceTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    SettingsService Create() => new(_db, _switch);

    static SettingsUpdate Update(string? apiKey = null, string? url = null, string? omdb = null, bool clearOmdb = false, string? level = null) =>
        new(apiKey, true, 15, false, false, url, omdb, clearOmdb, level);

    [Fact]
    public async Task Saves_values_trimmed_and_blank_values_leave_existing_ones_alone()
    {
        var svc = Create();
        await svc.UpdateAsync(Update(" key ", " http://s:8989 ", " omdb "));
        await svc.UpdateAsync(Update(apiKey: "", url: null, omdb: "  "));

        var saved = await svc.GetAsync();
        Assert.Equal(("key", "http://s:8989", "omdb"), (saved.ApiKey, saved.SonarrUrl, saved.OmdbApiKey));
        Assert.Equal((true, 15), (saved.AutoSyncEnabled, saved.SyncIntervalMinutes));
    }

    [Fact]
    public async Task The_omdb_key_can_be_cleared_explicitly()
    {
        var svc = Create();
        await svc.UpdateAsync(Update(omdb: "omdb"));
        await svc.UpdateAsync(Update(clearOmdb: true));
        Assert.Equal("", (await svc.GetAsync()).OmdbApiKey);
    }

    [Fact]
    public async Task Without_a_saved_row_the_defaults_are_in_force()
    {
        var svc = Create();
        Assert.Equal(new EffectiveSettings("http://localhost:8989", "", "", "", ""), await svc.GetEffectiveAsync());
        Assert.Equal(60, (await svc.GetAsync()).SyncIntervalMinutes);
    }

    [Fact]
    public async Task Root_folder_and_quality_profile_can_be_set_and_reset_to_blank()
    {
        var svc = Create();
        await svc.UpdateAsync(Update() with { SonarrRootFolderPath = " M:\\TV ", SonarrQualityProfile = " High " });
        var saved = await svc.GetAsync();
        Assert.Equal(("M:\\TV", "High"), (saved.SonarrRootFolderPath, saved.SonarrQualityProfile));

        // Missing means unchanged...
        await svc.UpdateAsync(Update());
        Assert.Equal("High", (await svc.GetAsync()).SonarrQualityProfile);

        // ...but blank is a real value: "use the first one Sonarr has".
        await svc.UpdateAsync(Update() with { SonarrRootFolderPath = "", SonarrQualityProfile = "" });
        saved = await svc.GetAsync();
        Assert.Equal(("", ""), (saved.SonarrRootFolderPath, saved.SonarrQualityProfile));
    }

    [Fact]
    public async Task A_saved_log_level_is_applied_to_the_running_logger_and_persisted()
    {
        var svc = Create();
        await svc.UpdateAsync(Update(level: "Verbose"));
        Assert.Equal(LogEventLevel.Verbose, _switch.MinimumLevel);
        Assert.Equal("Verbose", (await svc.GetAsync()).LogLevel);

        await svc.UpdateAsync(Update(level: "Error"));
        Assert.Equal(LogEventLevel.Error, _switch.MinimumLevel);
    }

    [Fact]
    public async Task Omitting_the_level_keeps_the_current_one()
    {
        var svc = Create();
        await svc.UpdateAsync(Update(level: "Information"));
        await svc.UpdateAsync(Update(level: null));
        Assert.Equal(LogEventLevel.Information, _switch.MinimumLevel);
        Assert.Equal("Information", (await svc.GetAsync()).LogLevel);
    }
}

public class SettingsUpdaterTests
{
    class FakeOmdb : IOmdbClient
    {
        public List<string?> Tested { get; } = new();
        public OmdbTestResult Result { get; set; } = new(true, "ok");
        public Task<List<OmdbEpisode>> GetSeasonAsync(string id, int season, CancellationToken ct = default) => Task.FromResult(new List<OmdbEpisode>());
        public Task<OmdbTitle?> GetTitleAsync(string id, CancellationToken ct = default) => Task.FromResult<OmdbTitle?>(null);
        public Task<OmdbTestResult> TestKeyAsync(string? apiKey, CancellationToken ct = default)
        {
            Tested.Add(apiKey);
            return Task.FromResult(Result);
        }
    }

    readonly FakeSettings _settings = new(new AppSettings { ApiKey = "saved-key", SonarrUrl = "http://old:8989" });
    readonly FakeSonarr _sonarr = new();
    readonly FakeOmdb _omdb = new();

    SettingsUpdater Create() => new(_settings, _sonarr, _omdb);

    static SettingsUpdate Update(string? apiKey = null, string? url = null, string? omdb = null, bool clearOmdb = false, string? level = null) =>
        new(apiKey, false, 60, true, true, url, omdb, clearOmdb, level);

    [Fact]
    public async Task Unchanged_sonarr_settings_are_saved_without_a_connection_test()
    {
        var result = await Create().UpdateAsync(Update(url: "http://old:8989/", level: "Warning"));

        Assert.True(result.Ok);
        Assert.Empty(_sonarr.Tests);
        Assert.NotNull(_settings.LastUpdate);
    }

    [Fact]
    public async Task A_new_key_is_tested_against_the_url_in_force_and_saved_when_it_works()
    {
        var result = await Create().UpdateAsync(Update(apiKey: " new-key "));

        Assert.True(result.Ok);
        Assert.Equal([("http://old:8989", "new-key")], _sonarr.Tests);
        Assert.NotNull(_settings.LastUpdate);
    }

    [Fact]
    public async Task A_changed_url_is_tested_with_the_saved_key()
    {
        await Create().UpdateAsync(Update(url: "http://new:8989"));
        Assert.Equal([("http://new:8989", "saved-key")], _sonarr.Tests);
    }

    [Fact]
    public async Task A_failed_sonarr_test_rejects_the_update_and_saves_nothing()
    {
        _sonarr.FailTest = new InvalidOperationException("Sonarr rejected the API key (HTTP 401).");

        var result = await Create().UpdateAsync(Update(apiKey: "bad"));

        Assert.False(result.Ok);
        Assert.Contains("rejected the API key", result.Error);
        Assert.Null(_settings.LastUpdate);
    }

    [Theory]
    [InlineData("sonarr:8989")]
    [InlineData("ftp://sonarr")]
    [InlineData("not a url")]
    public async Task Urls_that_are_not_http_addresses_are_rejected_without_testing(string url)
    {
        var result = await Create().UpdateAsync(Update(url: url));
        Assert.False(result.Ok);
        Assert.Empty(_sonarr.Tests);
        Assert.Null(_settings.LastUpdate);
    }

    [Fact]
    public async Task A_new_omdb_key_must_pass_its_test()
    {
        _omdb.Result = new(false, "Invalid API key!");
        var rejected = await Create().UpdateAsync(Update(omdb: "bad"));
        Assert.False(rejected.Ok);
        Assert.Contains("Invalid API key", rejected.Error);
        Assert.Null(_settings.LastUpdate);

        _omdb.Result = new(true, "ok");
        var accepted = await Create().UpdateAsync(Update(omdb: " good "));
        Assert.True(accepted.Ok);
        Assert.Equal(["bad", "good"], _omdb.Tested);
    }

    [Fact]
    public async Task Clearing_or_leaving_the_omdb_key_needs_no_test()
    {
        Assert.True((await Create().UpdateAsync(Update(clearOmdb: true))).Ok);
        Assert.True((await Create().UpdateAsync(Update(omdb: " "))).Ok);
        Assert.Empty(_omdb.Tested);
    }

    [Fact]
    public async Task Unknown_log_levels_are_rejected()
    {
        var result = await Create().UpdateAsync(Update(level: "Debug"));
        Assert.False(result.Ok);
        Assert.Contains("Verbose", result.Error);
        Assert.Null(_settings.LastUpdate);
    }
}

/// <summary>Captures every request (method, URI, API key header, body) and answers from a script.</summary>
class RecordingHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    public record Seen(HttpMethod Method, Uri Uri, string? ApiKey, string Body);
    public List<Seen> Requests { get; } = new();
    public Exception? Throw { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new(request.Method, request.RequestUri!, request.Headers.TryGetValues("X-Api-Key", out var k) ? k.First() : null, body));
        if (Throw is not null) throw Throw;
        var (status, text) = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}

public class SonarrClientTests
{
    static SonarrClient Create(RecordingHandler handler, string url = "http://sonarr:8989/", string key = "saved") =>
        new(new StubFactory(handler), new FakeSettings(new AppSettings { SonarrUrl = url, ApiKey = key }));

    [Fact]
    public async Task Test_uses_the_given_url_and_key_and_reports_the_sonarr_version()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """{"appName":"Sonarr","version":"4.0.20"}"""));
        var message = await Create(handler).TestAsync("http://other:1234", "typed-key");

        Assert.Equal("Sonarr 4.0.20", message);
        var seen = Assert.Single(handler.Requests);
        Assert.Equal("http://other:1234/api/v3/system/status", seen.Uri.AbsoluteUri);
        Assert.Equal("typed-key", seen.ApiKey);
    }

    [Fact]
    public async Task Test_without_arguments_uses_the_settings_in_force()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """{"appName":"Sonarr","version":"4"}"""));
        await Create(handler).TestAsync(null, null);
        Assert.Equal(("http://sonarr:8989/api/v3/system/status", "saved"), (handler.Requests[0].Uri.AbsoluteUri, handler.Requests[0].ApiKey));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.NotFound, "is this the Sonarr address")]
    public async Task Test_explains_http_failures(HttpStatusCode status, string expected)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(new RecordingHandler((status, ""))).TestAsync(null, null));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Test_recognises_when_the_address_is_not_sonarr()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(new RecordingHandler((HttpStatusCode.OK, "<html>login</html>"))).TestAsync(null, null));
        Assert.Contains("did not answer like Sonarr", ex.Message);

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(new RecordingHandler((HttpStatusCode.OK, """{"hello":"world"}"""))).TestAsync(null, null));
        Assert.Contains("did not answer like Sonarr", ex2.Message);
    }

    [Fact]
    public async Task Test_rejects_a_malformed_url_before_connecting()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler).TestAsync("sonarr", "k"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Lookup_returns_only_the_result_with_the_requested_imdb_id()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """[{"title":"Other","imdbId":"tt9"},{"title":"Wanted","imdbId":"tt1"}]"""));
        var found = await Create(handler).LookupSeriesAsync("tt1");

        Assert.Equal("Wanted", (string?)found!["title"]);
        Assert.Contains("term=imdb%3Att1", handler.Requests[0].Uri.Query);

        Assert.Null(await Create(new RecordingHandler((HttpStatusCode.OK, "[]"))).LookupSeriesAsync("tt1"));
    }

    [Fact]
    public async Task Add_posts_the_series_and_returns_what_sonarr_created()
    {
        var handler = new RecordingHandler((HttpStatusCode.Created, """{"id":5,"title":"Show","year":2020,"monitored":true,"titleSlug":"show","imdbId":"tt1"}"""));
        var created = await Create(handler).AddSeriesAsync(new() { ["title"] = "Show" });

        Assert.Equal((5, "show"), (created.Id, created.TitleSlug));
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("\"title\":\"Show\"", handler.Requests[0].Body);
    }

    [Theory]
    [InlineData("""[{"propertyName":"Path","errorMessage":"Path is already configured for an existing series"},{"errorMessage":"Second."}]""", "Path is already configured for an existing series Second.")]
    [InlineData("""{"message":"This series has already been added"}""", "This series has already been added")]
    [InlineData("oops", "Sonarr returned HTTP 400.")]
    public async Task Add_surfaces_sonarrs_own_explanation(string body, string expected)
    {
        var ex = await Assert.ThrowsAsync<SonarrException>(() => Create(new RecordingHandler((HttpStatusCode.BadRequest, body))).AddSeriesAsync(new()));
        Assert.Equal(expected, ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }
}

public class OmdbKeyTestTests
{
    static OmdbClient Create(RecordingHandler handler, string saved = "saved") =>
        new(new StubFactory(handler), new FakeSettings(new AppSettings { OmdbApiKey = saved }), new FiltarrOptions());

    [Fact]
    public async Task A_working_key_is_accepted_and_sent_to_omdb()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """{"Title":"Breaking Bad","Response":"True"}"""));
        var result = await Create(handler).TestKeyAsync(" typed ");

        Assert.True(result.Ok);
        Assert.Contains("apikey=typed", handler.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task A_blank_key_tests_the_one_in_force()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, """{"Response":"True"}"""));
        await Create(handler).TestKeyAsync(" ");
        Assert.Contains("apikey=saved", handler.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task No_key_anywhere_fails_without_calling_omdb()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, "{}"));
        var result = await Create(handler, saved: "").TestKeyAsync(null);
        Assert.False(result.Ok);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_invalid_key_reports_omdbs_message_even_with_a_401()
    {
        var result = await Create(new RecordingHandler((HttpStatusCode.Unauthorized, """{"Response":"False","Error":"Invalid API key!"}"""))).TestKeyAsync("bad");
        Assert.Equal(new OmdbTestResult(false, "Invalid API key!"), result);
    }

    [Fact]
    public async Task Network_problems_become_a_failed_test_not_an_exception()
    {
        var unreachable = new RecordingHandler((HttpStatusCode.OK, "{}")) { Throw = new HttpRequestException("no route") };
        var result = await Create(unreachable).TestKeyAsync("k");
        Assert.False(result.Ok);
        Assert.Contains("no route", result.Message);

        var garbage = await Create(new RecordingHandler((HttpStatusCode.OK, "<html>"))).TestKeyAsync("k");
        Assert.False(garbage.Ok);
    }
}

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
