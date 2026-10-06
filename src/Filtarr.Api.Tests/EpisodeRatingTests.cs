using System.Net;
using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Api.Tests;

/// <summary>Plays back one scripted outcome per request: an exception to throw, or a (status, body) response.</summary>
class ScriptedHandler(params object[] steps) : HttpMessageHandler
{
    int _calls;
    public int Calls => _calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var step = steps[Math.Min(_calls++, steps.Length - 1)];
        if (step is Exception ex) throw ex;
        var (status, body) = ((HttpStatusCode, string))step;
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

public class OmdbClientRetryTests
{
    const string Ok = """{"Response":"True","Episodes":[{"Episode":"1","imdbRating":"8.0","imdbID":"tt1"}]}""";
    static TaskCanceledException Timeout() => new("timed out", new TimeoutException());

    static (OmdbClient, ScriptedHandler) Create(params object[] steps)
    {
        var handler = new ScriptedHandler(steps);
        return (new OmdbClient(new StubFactory(handler), new FakeSettings(new AppSettings { OmdbApiKey = "k" }), new FiltarrOptions()) { RetryDelay = TimeSpan.Zero }, handler);
    }

    [Fact]
    public async Task A_timeout_is_retried_and_then_succeeds()
    {
        var (client, handler) = Create(Timeout(), (HttpStatusCode.OK, Ok));
        Assert.Single(await client.GetSeasonAsync("tt1", 1));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Server_errors_and_connection_failures_are_retried()
    {
        var (client, handler) = Create((HttpStatusCode.BadGateway, ""), new HttpRequestException("reset"), (HttpStatusCode.OK, Ok));
        Assert.Single(await client.GetSeasonAsync("tt1", 1));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_with_a_clear_message_instead_of_a_raw_cancellation()
    {
        var (client, handler) = Create(Timeout());
        var ex = await Assert.ThrowsAsync<OmdbException>(() => client.GetSeasonAsync("tt1", 1));
        Assert.Contains("did not respond in time", ex.Message);
        Assert.Contains("3 attempts", ex.Message);
        Assert.Equal(OmdbClient.MaxAttempts, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried()
    {
        var (client, handler) = Create(Timeout());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetSeasonAsync("tt1", 1, cts.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Invalid_key_is_reported_from_the_body_even_with_a_401_and_not_retried()
    {
        var (client, handler) = Create((HttpStatusCode.Unauthorized, """{"Response":"False","Error":"Invalid API key!"}"""));
        var ex = await Assert.ThrowsAsync<OmdbException>(() => client.GetSeasonAsync("tt1", 1));
        Assert.Contains("Invalid API key", ex.Message);
        Assert.Equal(1, handler.Calls);
    }
}

public class OmdbClientTests
{
    const string Season = """
        {"Title":"Show","Season":"1","totalSeasons":"5","Episodes":[
          {"Title":"Pilot","Released":"2008-01-20","Episode":"1","imdbRating":"9.0","imdbID":"tt0959621"},
          {"Title":"Next","Released":"2008-01-27","Episode":"2","imdbRating":"N/A","imdbID":"tt1054724"},
          {"Title":"Junk","Episode":"x","imdbRating":"5","imdbID":"tt1"}
        ],"Response":"True"}
        """;

    static (OmdbClient, StubHandler) Create(string json, string key = "secret")
    {
        var handler = new StubHandler(json);
        return (new OmdbClient(new StubFactory(handler), new FakeSettings(new AppSettings { OmdbApiKey = key }), new FiltarrOptions()), handler);
    }

    [Fact]
    public async Task Parses_episode_numbers_ratings_and_ids_and_treats_NA_as_no_rating()
    {
        var (client, handler) = Create(Season);
        var episodes = await client.GetSeasonAsync("tt0903747", 1);

        Assert.Equal([new OmdbEpisode(1, "tt0959621", 9.0), new OmdbEpisode(2, "tt1054724", null)], episodes);
        var url = handler.Requests.Single().AbsoluteUri;
        Assert.Contains("i=tt0903747", url);
        Assert.Contains("Season=1", url);
        Assert.Contains("apikey=secret", url);
    }

    [Fact]
    public async Task Unknown_season_is_empty_but_key_and_limit_errors_throw()
    {
        var (notFound, _) = Create("""{"Response":"False","Error":"Series or season not found!"}""");
        Assert.Empty(await notFound.GetSeasonAsync("tt1", 9));

        var (badKey, _) = Create("""{"Response":"False","Error":"Invalid API key!"}""");
        var ex = await Assert.ThrowsAsync<OmdbException>(() => badKey.GetSeasonAsync("tt1", 1));
        Assert.Contains("Invalid API key", ex.Message);

        var (limit, _) = Create("""{"Response":"False","Error":"Request limit reached!"}""");
        await Assert.ThrowsAsync<OmdbException>(() => limit.GetSeasonAsync("tt1", 1));
    }

    [Fact]
    public async Task Missing_key_throws_without_calling_omdb()
    {
        var (client, handler) = Create(Season, key: "");
        await Assert.ThrowsAsync<OmdbException>(() => client.GetSeasonAsync("tt1", 1));
        Assert.Empty(handler.Requests);
    }
}

public sealed class EpisodeRatingServiceTests : IDisposable
{
    class FakeOmdb : IOmdbClient
    {
        public List<int> Requested { get; } = new();
        public Exception? Fail { get; set; }
        public Task<OmdbTestResult> TestKeyAsync(string? apiKey, CancellationToken ct = default) => Task.FromResult(new OmdbTestResult(true, "ok"));
        public Task<OmdbTitle?> GetTitleAsync(string id, CancellationToken ct = default) => Task.FromResult<OmdbTitle?>(null);
        public Task<List<OmdbEpisode>> GetSeasonAsync(string id, int season, CancellationToken ct = default)
        {
            Requested.Add(season);
            if (Fail is not null) throw Fail;
            return Task.FromResult(new List<OmdbEpisode> { new(1, $"tt{season}01", 8.0 + season), new(2, null, null) });
        }
    }

    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly FakeOmdb _omdb = new();
    readonly FakeTimeProvider _clock = new(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
    readonly EpisodeRatingService _svc;

    public EpisodeRatingServiceTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _svc = new EpisodeRatingService(_omdb, _db, new FakeSettings(new AppSettings { OmdbApiKey = "k" }), _clock);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Returns_ratings_keyed_by_season_and_episode_and_skips_specials()
    {
        var r = await _svc.GetAsync("tt0903747", [0, 1, 2, 2]);

        Assert.Equal([1, 2], _omdb.Requested);
        Assert.Equal(new EpisodeRating("tt101", 9.0), r[(1, 1)]);
        Assert.Equal(new EpisodeRating("tt201", 10.0), r[(2, 1)]);
        Assert.Null(r[(1, 2)].Rating);
    }

    [Fact]
    public async Task Cached_seasons_are_not_refetched_until_the_cache_expires()
    {
        await _svc.GetAsync("tt1", [1]);
        await _svc.GetAsync("tt1", [1]);
        Assert.Equal([1], _omdb.Requested);

        _clock.Advance(EpisodeRatingService.CacheLifetime + TimeSpan.FromMinutes(1));
        await _svc.GetAsync("tt1", [1]);
        Assert.Equal([1, 1], _omdb.Requested);
        Assert.Equal(1, _db.SeasonRatings.Count()); // updated in place, not duplicated
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        _omdb.Fail = new OmdbException("Request limit reached!");
        await Assert.ThrowsAsync<OmdbException>(() => _svc.GetAsync("tt1", [1]));
        Assert.Empty(_db.SeasonRatings);

        _omdb.Fail = null;
        Assert.Single(await _svc.GetAsync("tt1", [1]), kv => kv.Value.Rating is not null);
    }
}

public class EpisodeRatingSyncTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeRatings _ratings = new();

    SyncService Create(params Filter[] filters) =>
        new(_sonarr, new FakeSettings(), new FakeFilters(filters), new FilterEngine(), _ratings, new FakeTracked(), new FakeRuns(), new SyncGate(),
            new FakeTimeProvider(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<SyncService>.Instance);

    static SonarrSeries Show(string? imdb = "tt1") => new(
        1, "Show", 2020, true, "continuing", null, "standard", null, 45, [], null, null, null, imdb);

    static SonarrEpisode Ep(int id, int season, int number) =>
        new(id, 1, $"Ep{number}", season, number, null, new DateTime(2024, 1, 1), 45, false, false, null);

    static Filter RatingAtLeast(double min, FilterAction action = FilterAction.Include) => new()
    {
        Name = $"rating>={min}", Action = action,
        Conditions = { new() { Field = "episode.rating", Operator = "gte", Value = min.ToString() } },
    };

    void Seed(string? imdb = "tt1")
    {
        _sonarr.Series.Add(Show(imdb));
        _sonarr.Episodes[1] = [Ep(11, 1, 1), Ep(12, 1, 2), Ep(13, 1, 3), Ep(21, 2, 1)];
        _ratings.Data[(1, 1)] = new("tt11", 9.1);
        _ratings.Data[(1, 2)] = new("tt12", 7.0);
        _ratings.Data[(1, 3)] = new("tt13", null);   // unrated
        _ratings.Data[(2, 1)] = new("tt21", 8.0);
    }

    [Fact]
    public async Task Episode_rating_filter_matches_only_episodes_at_or_above_the_rating()
    {
        Seed();
        var preview = await Create(RatingAtLeast(8)).PreviewAsync(null);
        Assert.Equal([11, 21], preview.Matches.Select(m => m.EpisodeId).Order());
        Assert.Equal(9.1, preview.Matches.Single(m => m.EpisodeId == 11).Rating);
    }

    [Fact]
    public async Task Exclude_rating_filter_removes_low_rated_episodes()
    {
        Seed();
        var all = new Filter { Name = "all", Conditions = { new() { Field = "episode.season", Operator = "gte", Value = "1" } } };
        var preview = await Create(all, RatingBelowExclude(8)).PreviewAsync(null);
        // 12 (7.0) is excluded; the unrated episode 13 is not, because a missing rating never matches a condition.
        Assert.Equal([11, 13, 21], preview.Matches.Select(m => m.EpisodeId).Order());
    }

    static Filter RatingBelowExclude(double max) => new()
    {
        Name = "low", Action = FilterAction.Exclude,
        Conditions = { new() { Field = "episode.rating", Operator = "lt", Value = max.ToString() } },
    };

    [Fact]
    public async Task Ratings_are_not_fetched_when_no_filter_uses_them_in_a_sync_preview()
    {
        Seed();
        var plain = new Filter { Name = "s1", Conditions = { new() { Field = "episode.season", Operator = "eq", Value = "1" } } };
        await Create(plain).PreviewAsync(null);
        Assert.Equal(0, _ratings.Calls);
    }

    [Fact]
    public async Task Test_attaches_episode_imdb_ids_even_when_filters_do_not_use_ratings()
    {
        Seed();
        var plain = new Filter { Name = "s1", Conditions = { new() { Field = "episode.season", Operator = "eq", Value = "1" } } };
        var result = await Create(plain).TestSeriesAsync("tt1", null);
        Assert.Equal("tt11", result.Matches.Single(m => m.Number == 1).EpisodeImdbId);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Unconfigured_omdb_warns_only_when_a_rating_filter_needs_it()
    {
        Seed();
        _ratings.IsConfigured = false;
        var plain = new Filter { Name = "s1", Conditions = { new() { Field = "episode.season", Operator = "eq", Value = "1" } } };

        Assert.Null((await Create(plain).TestSeriesAsync("tt1", null)).Warning);

        var result = await Create(RatingAtLeast(8)).TestSeriesAsync("tt1", null);
        Assert.Contains("OMDb", result.Warning);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Rating_lookup_failures_become_a_warning_not_an_exception()
    {
        Seed();
        _ratings.Fail = new OmdbException("OMDb: Request limit reached!");
        var result = await Create(RatingAtLeast(8)).TestSeriesAsync("tt1", null);
        Assert.Contains("limit", result.Warning);
        Assert.Empty(result.Matches);
        Assert.Empty((await Create(RatingAtLeast(8)).PreviewAsync(null)).Matches);
    }

    [Fact]
    public async Task Series_without_an_imdb_id_in_sonarr_cannot_use_ratings()
    {
        Seed(imdb: null);
        var preview = await Create(RatingAtLeast(1)).PreviewAsync(null);
        Assert.Empty(preview.Matches);
        Assert.Equal(0, _ratings.Calls);
    }
}

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
